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
        public MateJointClassification.Result Resolve(string parentComponentName, string childComponentName)
        {
            if (_doc == null || _poses == null ||
                string.IsNullOrEmpty(parentComponentName) || string.IsNullOrEmpty(childComponentName))
                return new MateJointClassification.Result { Found = false };

            var candidates = new List<MateJointClassification.Result>();
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

            return MateJointClassification.ChooseBest(candidates);
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

                Vector3? cylOrigin = null, cylAxis = null;
                Matrix3 cylRot = Matrix3.Identity;
                Vector3 cylTrans = Vector3.Zero;
                MateJointClassification.PlanePair planes = null;

                if (code == SwMateTypeCode.Concentric)
                {
                    var cyl = TryExtractCylinderLocal(mate, parentEntIdx, parentName)
                              ?? TryExtractCylinderLocal(mate, childEntIdx, childName);
                    if (cyl.HasValue)
                    {
                        cylOrigin = cyl.Value.origin;
                        cylAxis = cyl.Value.axis;
                        cylRot = cyl.Value.rotation;
                        cylTrans = cyl.Value.translation;
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

                return MateJointClassification.Classify(
                    code, lower, upper, cylOrigin, cylAxis, cylRot, cylTrans, planes);
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
