/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Walks the active assembly's mates via COM, extracts local (part-frame)
cylinder/plane geometry for mates spanning a given (parent, child) component
pair, and delegates classification to the pure MateJointClassification —
mirrors the recovered pre-gut AutoJointResolver's COM-walking mechanics
(MateGroup traversal, Marshal.ReleaseComObject hygiene, entity-to-component
matching, walk-to-top-level-name) but NEVER reads Component2.Transform2.
ArrayData directly — that 3x3 rotation block is column-major, and the
pre-gut code's raw reads predate the fix that already bit
SolidWorksMeshTessellator/SolidWorksAssemblyWalker once (memory
sw-mathtransform-column-major). Every local-to-assembly-frame transform here
goes through IComponentPoses.GetPose (already column-major-correct, the
same interface Sw2gzRobotExporter already depends on) and Matrix3.Mul — see
the plan's "Why not reuse AutoJointResolver.cs verbatim" section.

For a Concentric mate, extracts BOTH mated faces' cylinder geometry (not
just parent-with-child-fallback) and lets MateJointClassification's
agreement check compare them — a satisfied Concentric mate geometrically
forces both cylinders onto the same line, so a real disagreement here means
a bug (wrong entity/pose), not noise. When Classify reports the two sides
disagree beyond tolerance, this class is the one that owns a logger and
decides to warn — the pure classifier only reports the numbers.
*/
#if SW_INTEROP
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2GZ.Build;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;

namespace SW2GZ.SwSurface
{
    public sealed class SwMateJointResolver
    {
        // ~2.5deg — a satisfied Concentric mate's two cylinder axes should
        // be parallel to within SW's own solve tolerance, far tighter than
        // this; anything past it means the two sides genuinely disagree.
        private const double AxisAgreementDotMin = 0.999;
        // 1mm — same reasoning for how far the origins sit off each
        // other's axis line.
        private const double OriginPerpendicularDistanceMaxMeters = 0.001;

        private static readonly log4net.ILog logger = SW2GZ.Utilities.Logger.GetLogger();

        private readonly AssemblyDoc _doc;
        private readonly IComponentPoses _poses;

        public SwMateJointResolver(AssemblyDoc doc, IComponentPoses poses)
        {
            _doc = doc;
            _poses = poses;
        }

        // Finds the best joint suggestion for a (parentComponentName,
        // childComponentName) pair — both TOP-LEVEL Component2.Name2
        // values, matching LinkDef.ComponentIds[0]'s own convention. Returns
        // a not-found Result if the assembly has no mate spanning that pair,
        // or if _doc/_poses is null.
        public MateJointClassification.Result Resolve(string parentComponentName, string childComponentName) =>
            MateJointClassification.ChooseBest(ResolveAllCandidates(parentComponentName, childComponentName));

        // Every real (Found) mate spanning this link pair, each tagged with
        // its own MateName — lets the Joints step UI offer a picker when a
        // link has more than one plausible pivot mate (e.g. two similar
        // holes, only one of which is the actual hinge) instead of silently
        // trusting whichever ChooseBest's tie-break happens to prefer.
        public List<MateJointClassification.Result> ResolveAllCandidates(string parentComponentName, string childComponentName)
        {
            var candidates = new List<MateJointClassification.Result>();
            if (_doc == null || _poses == null ||
                string.IsNullOrEmpty(parentComponentName) || string.IsNullOrEmpty(childComponentName))
                return candidates;

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
                                MateJointClassification.Result hit =
                                    TryResolveMate(sub, parentComponentName, childComponentName);
                                if (hit != null) candidates.Add(hit);
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

            return candidates;
        }

        // Re-finds a specific mate BY NAME (walks MateGroup sub-features
        // same as ResolveAllCandidates, stopping at the first name match —
        // this interop version has no confirmed IModelDoc2.FeatureByName)
        // and selects the real entity it used for classification — parent-
        // side preferred, same fallback order TryExtractCylinderLocal
        // already uses. Replaces guessing a TEMPAXIS by point-proximity:
        // this selects the exact entity the data came from, so what lights
        // up in the viewport is provably what the suggestion used, not a
        // nearby look-alike. Returns false if the mate/entities/geometry
        // can't be re-resolved (mate renamed/deleted since the suggestion
        // was made).
        public bool SelectPivotFace(string mateName, string parentComponentName, string childComponentName)
        {
            if (_doc == null || string.IsNullOrEmpty(mateName)) return false;

            Feature feat = FindMateFeatureByName(mateName);
            if (feat == null) return false;

            object specific = null;
            Mate2 mate = null;
            try
            {
                specific = feat.GetSpecificFeature2();
                mate = specific as Mate2;
                if (mate == null) return false;

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
                            if (parentEntIdx < 0 && name == parentComponentName) parentEntIdx = i;
                            else if (childEntIdx < 0 && name == childComponentName) childEntIdx = i;
                        }
                        finally { Marshal.ReleaseComObject(comp); }
                    }
                    finally { Marshal.ReleaseComObject(ent); }
                }

                return TrySelectEntityFace(mate, parentEntIdx) || TrySelectEntityFace(mate, childEntIdx);
            }
            catch { return false; }
            finally
            {
                if (mate != null) try { Marshal.ReleaseComObject(mate); } catch { }
                else if (specific != null) try { Marshal.ReleaseComObject(specific); } catch { }
                try { Marshal.ReleaseComObject(feat); } catch { }
            }
        }

        private Feature FindMateFeatureByName(string mateName)
        {
            var modelDoc = (IModelDoc2)_doc;
            Feature feat = (Feature)modelDoc.FirstFeature();
            try
            {
                while (feat != null)
                {
                    if (feat.GetTypeName2() == "MateGroup")
                    {
                        Feature sub = (Feature)feat.GetFirstSubFeature();
                        while (sub != null)
                        {
                            if (sub.Name == mateName)
                            {
                                Marshal.ReleaseComObject(feat); // sub returned to caller; feat itself is done
                                return sub;
                            }
                            Feature nextSub = (Feature)sub.GetNextSubFeature();
                            Marshal.ReleaseComObject(sub);
                            sub = nextSub;
                        }
                    }
                    Feature next = (Feature)feat.GetNextFeature();
                    Marshal.ReleaseComObject(feat);
                    feat = next;
                }
            }
            catch { if (feat != null) try { Marshal.ReleaseComObject(feat); } catch { } }
            return null;
        }

        private static bool TrySelectEntityFace(Mate2 mate, int entityIdx)
        {
            if (entityIdx < 0) return false;
            MateEntity2 ent = mate.MateEntity(entityIdx);
            if (ent == null) return false;

            object refObj = null;
            try
            {
                try { refObj = ent.Reference; } catch { refObj = null; }
                return refObj is Entity ent2 && ent2.Select4(false, null);
            }
            catch { return false; }
            finally
            {
                if (refObj != null) try { Marshal.ReleaseComObject(refObj); } catch { }
                try { Marshal.ReleaseComObject(ent); } catch { }
            }
        }

        private MateJointClassification.Result TryResolveMate(
            Feature feat, string parentName, string childName)
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
                            if (parentEntIdx < 0 && name == parentName) parentEntIdx = i;
                            else if (childEntIdx < 0 && name == childName) childEntIdx = i;
                        }
                        finally { Marshal.ReleaseComObject(comp); }
                    }
                    finally { Marshal.ReleaseComObject(ent); }
                }
                if (parentEntIdx < 0 || childEntIdx < 0) return null;

                double? lower = null, upper = null;
                try
                {
                    double max = mate.MaximumVariation, min = mate.MinimumVariation;
                    if (System.Math.Abs(max) > 1e-9 || System.Math.Abs(min) > 1e-9) { lower = min; upper = max; }
                }
                catch { /* some mate types don't expose variation — leave null */ }

                SwMateTypeCode code = (swMateType_e)mate.Type switch
                {
                    swMateType_e.swMateLOCK => SwMateTypeCode.Lock,
                    swMateType_e.swMateCONCENTRIC => SwMateTypeCode.Concentric,
                    swMateType_e.swMateANGLE => SwMateTypeCode.Angle,
                    swMateType_e.swMateDISTANCE => SwMateTypeCode.Distance,
                    _ => SwMateTypeCode.Other,
                };

                MateJointClassification.CylinderPair cylPair = null;
                MateJointClassification.PlanePair planes = null;

                if (code == SwMateTypeCode.Concentric)
                {
                    var parentCyl = TryExtractCylinderLocal(mate, parentEntIdx, parentName);
                    var childCyl = TryExtractCylinderLocal(mate, childEntIdx, childName);
                    if (parentCyl.HasValue || childCyl.HasValue)
                    {
                        cylPair = new MateJointClassification.CylinderPair(
                            parentOriginLocal: parentCyl?.origin, parentAxisLocal: parentCyl?.axis,
                            parentRotation: parentCyl?.rotation ?? Matrix3.Identity,
                            parentTranslation: parentCyl?.translation ?? Vector3.Zero,
                            childOriginLocal: childCyl?.origin, childAxisLocal: childCyl?.axis,
                            childRotation: childCyl?.rotation ?? Matrix3.Identity,
                            childTranslation: childCyl?.translation ?? Vector3.Zero);
                    }
                }
                else if (code == SwMateTypeCode.Angle || code == SwMateTypeCode.Distance)
                {
                    var parentPlane = TryExtractPlaneLocal(mate, parentEntIdx, parentName);
                    var childPlane = TryExtractPlaneLocal(mate, childEntIdx, childName);
                    if (parentPlane.HasValue && childPlane.HasValue)
                    {
                        planes = new MateJointClassification.PlanePair(
                            parentNormalLocal: parentPlane.Value.normal,
                            parentPointLocal: parentPlane.Value.point,
                            parentRotation: parentPlane.Value.rotation,
                            parentTranslation: parentPlane.Value.translation,
                            childNormalLocal: childPlane.Value.normal,
                            childPointLocal: childPlane.Value.point,
                            childRotation: childPlane.Value.rotation,
                            childTranslation: childPlane.Value.translation);
                    }
                }

                MateJointClassification.Result result =
                    MateJointClassification.Classify(code, lower, upper, cylPair, planes);
                result.MateName = feat.Name;

                if (result.AxisAgreementDot.HasValue &&
                    (result.AxisAgreementDot.Value < AxisAgreementDotMin ||
                     result.OriginPerpendicularDistance.GetValueOrDefault() > OriginPerpendicularDistanceMaxMeters))
                {
                    logger.Warn("Mate '" + feat.Name + "' (" + parentName + " <-> " + childName +
                        "): parent/child cylinder geometry disagree (axis dot=" +
                        result.AxisAgreementDot.Value.ToString("F6") + ", perp dist=" +
                        result.OriginPerpendicularDistance.GetValueOrDefault().ToString("F6") +
                        "m) — suggested pivot may be inaccurate; consider a manual override.");
                }

                return result;
            }
            finally { Marshal.ReleaseComObject(mate); }
        }

        private (Vector3 origin, Vector3 axis, Matrix3 rotation, Vector3 translation)? TryExtractCylinderLocal(
            Mate2 mate, int entityIdx, string componentName)
        {
            if (entityIdx < 0) return null;
            MateEntity2 ent = mate.MateEntity(entityIdx);
            if (ent == null) return null;

            object refObj = null, surfObj = null;
            try
            {
                try { refObj = ent.Reference; } catch { refObj = null; }
                if (!(refObj is IFace2 face)) return null;
                surfObj = face.GetSurface();
                if (!(surfObj is ISurface surf) || !surf.IsCylinder()) return null;
                if (!(surf.CylinderParams is double[] cp) || cp.Length < 6) return null;

                (Matrix3 r, Vector3 t) = _poses.GetPose(componentName);
                return (new Vector3((float)cp[0], (float)cp[1], (float)cp[2]),
                        new Vector3((float)cp[3], (float)cp[4], (float)cp[5]), r, t);
            }
            catch { return null; }
            finally
            {
                if (surfObj != null) try { Marshal.ReleaseComObject(surfObj); } catch { }
                if (refObj != null) try { Marshal.ReleaseComObject(refObj); } catch { }
                try { Marshal.ReleaseComObject(ent); } catch { }
            }
        }

        private (Vector3 normal, Vector3 point, Matrix3 rotation, Vector3 translation)? TryExtractPlaneLocal(
            Mate2 mate, int entityIdx, string componentName)
        {
            if (entityIdx < 0) return null;
            MateEntity2 ent = mate.MateEntity(entityIdx);
            if (ent == null) return null;

            object refObj = null, surfObj = null;
            try
            {
                try { refObj = ent.Reference; } catch { refObj = null; }
                if (!(refObj is IFace2 face)) return null;
                surfObj = face.GetSurface();
                if (!(surfObj is ISurface surf) || !surf.IsPlane()) return null;
                if (!(surf.PlaneParams is double[] pp) || pp.Length < 6) return null;

                (Matrix3 r, Vector3 t) = _poses.GetPose(componentName);
                return (new Vector3((float)pp[0], (float)pp[1], (float)pp[2]),
                        new Vector3((float)pp[3], (float)pp[4], (float)pp[5]), r, t);
            }
            catch { return null; }
            finally
            {
                if (surfObj != null) try { Marshal.ReleaseComObject(surfObj); } catch { }
                if (refObj != null) try { Marshal.ReleaseComObject(refObj); } catch { }
                try { Marshal.ReleaseComObject(ent); } catch { }
            }
        }

        // Walk up the component owner chain to its top-level instance name
        // — same convention SolidWorksAssemblyWalker.TopLevelName uses, so
        // the ids line up with LinkDef.ComponentIds. Pure COM identity
        // logic, unrelated to (and unaffected by) the column-major issue.
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
