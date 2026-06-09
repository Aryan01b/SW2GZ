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

Axis + origin geometry is dispatched per mate type:
  CONCENTRIC → cylindrical face axis via ISurface.CylinderParams (pure
               math in CylinderTransform).
  ANGLE      → hinge axis = cross product of the two planar face normals
               (via ISurface.PlaneParams); origin = parent face root point.
               Required for LimitAngle mates — they have no cylindrical
               face, and the prior cylinder-only path silently demoted
               them to Fixed.
  DISTANCE   → prismatic slide direction = parent (or fallback child)
               planar face normal; origin = that face's root point.
  LOCK       → Fixed, no geometry needed.
All transforms run through Component2.Transform2.ArrayData (row-major
rotation 3x3 + translation), same convention as CylinderTransform.

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

            return WalkBestSpanningMate(parents, children) ?? miss;
        }

        // Per-mate API used by the Joints step's explicit mate picker. Walks
        // MateGroup looking for a sub-feature whose Feature.Name matches
        // mateName, then runs the standard TryResolveMate classification
        // against that mate. Returns a Resolved with Found=true on success
        // or Found=false on any miss (mate not found, mate not spanning the
        // pair, etc.). COM-hygiene mirrors Resolve() — every walked
        // Feature / Mate2 gets released.
        public Resolved ResolveFromMateName(
            string mateName,
            IReadOnlyList<string> parentComponentIds,
            IReadOnlyList<string> childComponentIds)
        {
            var miss = new Resolved();
            if (_doc == null || string.IsNullOrEmpty(mateName) ||
                parentComponentIds == null || childComponentIds == null) return miss;
            var parents = new HashSet<string>(parentComponentIds);
            var children = new HashSet<string>(childComponentIds);
            if (parents.Count == 0 || children.Count == 0) return miss;

            Resolved hit = null;
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
                                if (hit == null && sub.Name == mateName)
                                {
                                    hit = TryResolveMate(sub, parents, children);
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

            return hit ?? miss;
        }

        // Enumerate the names of every mate feature spanning a (parent, child)
        // pair without running cylinder extraction or classification. Used by
        // the Joints step UI to populate the "pick a mate" listbox. Same
        // MateGroup walk + COM-hygiene as Resolve().
        public IReadOnlyList<string> ListMateNamesBetween(
            IReadOnlyList<string> parentComponentIds,
            IReadOnlyList<string> childComponentIds)
        {
            var names = new List<string>();
            if (_doc == null || parentComponentIds == null || childComponentIds == null) return names;
            var parents = new HashSet<string>(parentComponentIds);
            var children = new HashSet<string>(childComponentIds);
            if (parents.Count == 0 || children.Count == 0) return names;

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
                                if (MateSpansPair(sub, parents, children))
                                    names.Add(sub.Name);
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

            return names;
        }

        // Pure walk: collect candidates, run ChooseBest. Extracted from
        // Resolve so the public API can stay short; returns null when the
        // assembly has no spanning mates (caller substitutes a fresh
        // empty Resolved).
        private Resolved WalkBestSpanningMate(HashSet<string> parents, HashSet<string> children)
        {
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
            return chosen ?? nonCylinderHit;
        }

        // Cheap predicate used by ListMateNamesBetween — checks if a Mate2
        // sub-feature's two MateEntity components fall one in parents and
        // one in children, WITHOUT running cylinder extraction or
        // classification. Mirrors the entity-walking prefix of TryResolveMate.
        private static bool MateSpansPair(
            Feature feat,
            HashSet<string> parents,
            HashSet<string> children)
        {
            object specific = feat.GetSpecificFeature2();
            var mate = specific as Mate2;
            if (mate == null)
            {
                if (specific != null) Marshal.ReleaseComObject(specific);
                return false;
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
                return parentEntIdx >= 0 && childEntIdx >= 0;
            }
            finally { Marshal.ReleaseComObject(mate); }
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

                // Axis + origin geometry — dispatched per mate type. CONCENTRIC
                // takes the cylinder face. ANGLE (incl. LimitAngle) takes the
                // cross product of the two planar face normals as the hinge
                // axis (no cylindrical face exists for an angle mate, so the
                // old cylinder-only path always demoted these to Fixed — the
                // root cause behind LimitAngleN joints rendering as Fixed in
                // the wizard's Joints + Review steps). DISTANCE takes the
                // parent face normal as the prismatic slide direction.
                (Vector3 origin, Vector3 dir, bool ok) extracted =
                    (Vector3.Zero, Vector3.Zero, false);
                switch ((swMateType_e)mate.Type)
                {
                    case swMateType_e.swMateCONCENTRIC:
                        extracted = TryExtractCylinder(mate, parentEntIdx);
                        if (!extracted.ok) extracted = TryExtractCylinder(mate, childEntIdx);
                        break;
                    case swMateType_e.swMateANGLE:
                    {
                        var pn = TryExtractPlane(mate, parentEntIdx);
                        var cn = TryExtractPlane(mate, childEntIdx);
                        if (pn.ok && cn.ok)
                        {
                            Vector3 axis = Vector3.Cross(pn.normal, cn.normal);
                            if (axis.LengthSquared() > 1e-12f)
                            {
                                axis = Vector3.Normalize(axis);
                                // Origin = parent face root point. Good enough
                                // for the URDF: the actual hinge sits on the
                                // shared edge between the two faces, which is
                                // co-planar with both root points up to the
                                // parent face's tangent extent.
                                extracted = (pn.point, axis, true);
                            }
                        }
                        break;
                    }
                    case swMateType_e.swMateDISTANCE:
                    {
                        var pp = TryExtractPlane(mate, parentEntIdx);
                        if (!pp.ok) pp = TryExtractPlane(mate, childEntIdx);
                        if (pp.ok) extracted = (pp.point, pp.normal, true);
                        break;
                    }
                    // LOCK / unknown → stays Fixed, no geometry needed.
                }

                var res = new Resolved
                {
                    Found      = true,
                    MateName   = feat.Name,
                    Kind       = kind,
                    LimitLower = lower,
                    LimitUpper = upper,
                };
                if (extracted.ok)
                {
                    res.AxisAssembly   = extracted.dir;
                    res.OriginAssembly = extracted.origin;
                }
                else if (kind != MateKind.Fixed)
                {
                    // Movable kind with no extractable geometry → would write a
                    // zero-axis joint and trip the pre-write validator. Demote
                    // to Fixed so the user can add a cleaner mate and Re-detect.
                    // LOCK / unknown were already Fixed; don't double-demote.
                    res.Kind = MateKind.Fixed;
                }
                return res;
            }
            finally { Marshal.ReleaseComObject(mate); }
        }

        // Pull the plane normal + a root point from a planar MateEntity face,
        // transformed into the assembly frame. Mirrors TryExtractCylinder's
        // COM-hygiene + transform pattern; powers ANGLE + DISTANCE extraction.
        //
        // ISurface.PlaneParams layout (9 doubles, part-local):
        //   [0..2] normal unit vector
        //   [3..5] root point on plane
        //   [6..8] X-axis vector parallel to plane (unused here)
        //
        // Normal is rotated only (no translation applied to a direction);
        // root point is rotated AND translated. Component2.Transform2.ArrayData
        // is row-major rotation in d[0..8] + translation in d[9..11], same as
        // CylinderTransform.
        private static (Vector3 normal, Vector3 point, bool ok) TryExtractPlane(Mate2 mate, int entityIdx)
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
                if (!(surfObj is ISurface surf) || !surf.IsPlane()) return (Vector3.Zero, Vector3.Zero, false);
                if (!(surf.PlaneParams is double[] pp) || pp.Length < 6) return (Vector3.Zero, Vector3.Zero, false);

                comp = ent.ReferenceComponent;
                double[] xform = null;
                if (comp != null)
                {
                    MathTransform mt = comp.Transform2;
                    xform = mt?.ArrayData as double[];
                }
                if (xform == null || xform.Length < 12)
                {
                    var locN = new Vector3((float)pp[0], (float)pp[1], (float)pp[2]);
                    var locP = new Vector3((float)pp[3], (float)pp[4], (float)pp[5]);
                    if (locN.LengthSquared() > 1e-12f) locN = Vector3.Normalize(locN);
                    return (locN, locP, true);
                }
                double[] d = xform;
                // Normal: rotation only.
                float nx = (float)(d[0] * pp[0] + d[1] * pp[1] + d[2] * pp[2]);
                float ny = (float)(d[3] * pp[0] + d[4] * pp[1] + d[5] * pp[2]);
                float nz = (float)(d[6] * pp[0] + d[7] * pp[1] + d[8] * pp[2]);
                var normalAsm = new Vector3(nx, ny, nz);
                if (normalAsm.LengthSquared() > 1e-12f) normalAsm = Vector3.Normalize(normalAsm);
                // Point: rotation + translation.
                float px = (float)(d[0] * pp[3] + d[1] * pp[4] + d[2] * pp[5] + d[9]);
                float py = (float)(d[3] * pp[3] + d[4] * pp[4] + d[5] * pp[5] + d[10]);
                float pz = (float)(d[6] * pp[3] + d[7] * pp[4] + d[8] * pp[5] + d[11]);
                return (normalAsm, new Vector3(px, py, pz), true);
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
