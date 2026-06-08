/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Auto-detect the SolidWorks mate between a (parent, child) component pair and
distil it into a joint type + axis + origin + limits. The Joints step in
Sw2gzCreateRobotPmp runs this once per JointDef in lieu of asking the user to
pick a mate / Reference CS / Reference Axis by hand.

Walks the assembly's MateGroup (same iteration + COM-hygiene pattern as
SolidWorksAssemblyWalker.WalkAllMates), filters to mates whose two MateEntity
components fall one in parentIds and one in childIds, and classifies:

  swMateLOCK       → Fixed
  swMateCONCENTRIC → Continuous (Revolute when the mate carries a non-zero
                                 MinimumVariation / MaximumVariation range)
  swMateDISTANCE   → Prismatic
  swMateANGLE      → Revolute
  anything else    → Fixed (v1 default; no fabricated axis)

Cylinder-axis geometry comes from the cylindrical face on the parent side
(or the first cylindrical face we find — preferred over non-cylinder mates
when multiple span the same pair) via ISurface.CylinderParams +
Component2.Transform2 (math lives in pure CylinderTransform so it can be
unit-tested off-COM).

Selection priority when multiple mates span the same (parent, child) pair:
  1. cylinder-bearing AND limit-bearing  (→ Revolute / Prismatic)
  2. cylinder-bearing, no limit          (→ Continuous)
  3. non-cylinder fallback               (→ Fixed)
Within each priority bucket the first hit wins. The pure ChooseBest helper
(AutoJointResolved.ChooseBest in AutoJointResolved.cs) implements the rank,
unit-testable off-COM; the COM walk just collects candidates.

The entire class is gated on SW_INTEROP because it touches Mate2 /
MateEntity2 / IFace2 / ISurface / Component2 / MathTransform. The pure-C#
parts (component-id matching predicate + Resolved DTO) are aggregated into
the type for ergonomics; tests that don't need COM construct Resolved
directly. CylinderTransform (pure) lives at SW2GZ.Math.CylinderTransform.
*/
#if SW_INTEROP
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using SW2GZ.Build;
using SW2GZ.Math;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SW2GZ.SwSurface
{
    public sealed class AutoJointResolver
    {
        // Public alias so callers (and tests) can write AutoJointResolver.Resolved
        // — the actual type definition lives in AutoJointResolved.cs (pure, no
        // SW_INTEROP gate) so the off-COM test project can reference it.
        public sealed class Resolved : AutoJointResolved { }

        private readonly AssemblyDoc _doc;

        public AutoJointResolver(AssemblyDoc doc)
        {
            _doc = doc;
        }

        public Resolved Resolve(
            IReadOnlyList<string> parentComponentIds,
            IReadOnlyList<string> childComponentIds)
        {
            var miss = new Resolved();
            if (_doc == null || parentComponentIds == null || childComponentIds == null) return miss;
            var parents = new HashSet<string>(parentComponentIds);
            var children = new HashSet<string>(childComponentIds);
            if (parents.Count == 0 || children.Count == 0) return miss;

            var cylinderHits = new List<Resolved>();
            Resolved nonCylinderHit = null;
            var modelDoc = (IModelDoc2)_doc;
            Feature feat = (Feature)modelDoc.FirstFeature();
            try
            {
                while (feat != null)
                {
                    if (feat.GetTypeName2() == "MateGroup")
                    {
                        Feature sub = (Feature)feat.GetFirstSubFeature();
                        try
                        {
                            while (sub != null)
                            {
                                Resolved hit = TryResolveMate(sub, parents, children);
                                if (hit != null)
                                {
                                    if (hit.OriginAssembly.HasValue)
                                    {
                                        // Cylinder-derived geometry — collect and
                                        // pick the limit-bearing one (if any)
                                        // after the walk finishes.
                                        cylinderHits.Add(hit);
                                    }
                                    else if (nonCylinderHit == null)
                                    {
                                        nonCylinderHit = hit;
                                    }
                                }
                                Feature nextSub = (Feature)sub.GetNextSubFeature();
                                Marshal.ReleaseComObject(sub);
                                sub = nextSub;
                            }
                        }
                        finally { if (sub != null) Marshal.ReleaseComObject(sub); }
                    }
                    Feature next = (Feature)feat.GetNextFeature();
                    Marshal.ReleaseComObject(feat);
                    feat = next;
                }
            }
            finally { if (feat != null) Marshal.ReleaseComObject(feat); }

            // ChooseBest returns the base AutoJointResolved; cast back to the
            // derived Resolved alias the call site expects. The cast is safe
            // because we only populate the list with Resolved instances above.
            Resolved chosen = (Resolved)AutoJointResolved.ChooseBest(cylinderHits);
            return chosen ?? nonCylinderHit ?? miss;
        }

        private static Resolved TryResolveMate(
            Feature feat,
            HashSet<string> parents,
            HashSet<string> children)
        {
            object specific = feat.GetSpecificFeature2();
            var mate = specific as Mate2;
            if (mate == null)
            {
                if (specific != null) Marshal.ReleaseComObject(specific);
                return null;
            }

            try
            {
                int parentEntIdx = -1, childEntIdx = -1;
                int n = mate.GetMateEntityCount();
                for (int i = 0; i < n; i++)
                {
                    MateEntity2 ent = mate.MateEntity(i);
                    if (ent == null) continue;
                    try
                    {
                        Component2 comp = ent.ReferenceComponent;
                        if (comp == null) continue;
                        try
                        {
                            string name = TopLevelName(comp);
                            if (parentEntIdx < 0 && parents.Contains(name)) parentEntIdx = i;
                            else if (childEntIdx < 0 && children.Contains(name)) childEntIdx = i;
                        }
                        finally { Marshal.ReleaseComObject(comp); }
                    }
                    finally { Marshal.ReleaseComObject(ent); }
                }

                if (parentEntIdx < 0 || childEntIdx < 0) return null;

                // Limit range from mate's MaximumVariation / MinimumVariation.
                double? lower = null, upper = null;
                bool hasLimit = false;
                try
                {
                    double max = mate.MaximumVariation, min = mate.MinimumVariation;
                    if (System.Math.Abs(max) > 1e-9 || System.Math.Abs(min) > 1e-9)
                    {
                        lower = min; upper = max; hasLimit = true;
                    }
                }
                catch { }

                MateKind kind;
                switch ((swMateType_e)mate.Type)
                {
                    case swMateType_e.swMateLOCK:       kind = MateKind.Fixed; break;
                    case swMateType_e.swMateCONCENTRIC: kind = hasLimit ? MateKind.Revolute : MateKind.Continuous; break;
                    case swMateType_e.swMateDISTANCE:   kind = MateKind.Prismatic; break;
                    case swMateType_e.swMateANGLE:      kind = MateKind.Revolute; break;
                    default:                            kind = MateKind.Fixed; break;
                }

                // Cylinder geometry — prefer the parent-side entity (so the
                // resulting origin lives at the parent's mate face, e.g. the
                // hole the child pin is concentric with), fall back to either.
                (Vector3 origin, Vector3 dir, bool ok) cyl =
                    TryExtractCylinder(mate, parentEntIdx);
                if (!cyl.ok) cyl = TryExtractCylinder(mate, childEntIdx);

                var res = new Resolved
                {
                    Found      = true,
                    MateName   = feat.Name,
                    Kind       = kind,
                    LimitLower = lower,
                    LimitUpper = upper,
                };
                if (cyl.ok)
                {
                    res.AxisAssembly   = cyl.dir;
                    res.OriginAssembly = cyl.origin;
                }
                else
                {
                    // No usable cylinder face (axis-axis concentric, non-face
                    // entity, etc.). A Revolute/Continuous/Prismatic joint with
                    // a zero axis vector trips the pre-write validator, so
                    // demote to Fixed — the user can add a cleaner mate and
                    // hit Re-detect.
                    res.Kind = MateKind.Fixed;
                }
                return res;
            }
            finally { Marshal.ReleaseComObject(mate); }
        }

        private static (Vector3 origin, Vector3 dir, bool ok) TryExtractCylinder(Mate2 mate, int entityIdx)
        {
            if (entityIdx < 0) return (Vector3.Zero, Vector3.Zero, false);
            MateEntity2 ent = mate.MateEntity(entityIdx);
            if (ent == null) return (Vector3.Zero, Vector3.Zero, false);

            object refObj = null, surfObj = null;
            Component2 comp = null;
            try
            {
                try { refObj = ent.Reference; } catch { refObj = null; }
                if (!(refObj is IFace2 face)) return (Vector3.Zero, Vector3.Zero, false);

                surfObj = face.GetSurface();
                if (!(surfObj is ISurface surf) || !surf.IsCylinder()) return (Vector3.Zero, Vector3.Zero, false);
                if (!(surf.CylinderParams is double[] cp) || cp.Length < 6) return (Vector3.Zero, Vector3.Zero, false);

                comp = ent.ReferenceComponent;
                double[] xform = null;
                if (comp != null)
                {
                    MathTransform mt = comp.Transform2;
                    xform = mt?.ArrayData as double[];
                }
                // No component transform → treat part-local as assembly-frame.
                if (xform == null)
                {
                    var locOrig = new Vector3((float)cp[0], (float)cp[1], (float)cp[2]);
                    var locDir  = new Vector3((float)cp[3], (float)cp[4], (float)cp[5]);
                    if (locDir.LengthSquared() > 1e-12f) locDir = Vector3.Normalize(locDir);
                    return (locOrig, locDir, true);
                }
                (Vector3 o, Vector3 d) = CylinderTransform.TransformCylinderToAssembly(xform, cp);
                return (o, d, true);
            }
            catch
            {
                return (Vector3.Zero, Vector3.Zero, false);
            }
            finally
            {
                if (surfObj != null) try { Marshal.ReleaseComObject(surfObj); } catch { }
                if (refObj  != null) try { Marshal.ReleaseComObject(refObj);  } catch { }
                if (comp    != null) try { Marshal.ReleaseComObject(comp);    } catch { }
                try { Marshal.ReleaseComObject(ent); } catch { }
            }
        }

        // Walk up the component owner chain to its top-level instance name —
        // same convention SolidWorksAssemblyWalker.TopLevelName uses, so the
        // ids we match against (LinkDef.ComponentIds) line up.
        private static string TopLevelName(Component2 comp)
        {
            string name = comp.Name2;
            Component2 parent = (Component2)comp.GetParent();
            while (parent != null)
            {
                name = parent.Name2;
                Component2 next = (Component2)parent.GetParent();
                Marshal.ReleaseComObject(parent);
                parent = next;
            }
            return name;
        }
    }
}
#endif
