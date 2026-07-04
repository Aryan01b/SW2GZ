/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Two independent responsibilities that happen to share this class because
both need IComponentPoses:

1. Walks the active assembly's mates via COM to classify a (parent, child)
   link pair's joint TYPE and LIMIT from the mate's own type + variation —
   mirrors the recovered pre-gut AutoJointResolver's COM-walking mechanics
   (MateGroup traversal, Marshal.ReleaseComObject hygiene, entity-to-
   component matching, walk-to-top-level-name).

2. Reads AXIS + PIVOT from an arbitrary user-picked cylindrical face or
   straight edge (TryExtractAxisFromSelection) — the Joints step's manual
   axis pick. This has nothing to do with mates; see
   docs/superpowers/specs/2026-07-03-manual-axis-pivot-pick-design.md for
   why axis/pivot moved here from mate-geometry guessing (three live-tested
   attempts at an accurate mate-derived axis each fixed one bug and
   surfaced another against FULL_ARM.SLDASM).

Every local-to-assembly-frame transform goes through IComponentPoses.
GetPose (already column-major-correct, the same interface
Sw2gzRobotExporter already depends on) and Matrix3.Mul — NEVER raw
Component2.Transform2.ArrayData reads, which are column-major and silently
invert rotation if read naively (memory sw-mathtransform-column-major).
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

        // Finds the best joint suggestion (Type + Limit only — no axis, see
        // file header) for a (parentComponentName, childComponentName) pair
        // — both TOP-LEVEL Component2.Name2 values, matching
        // LinkDef.ComponentIds[0]'s own convention. Returns a not-found
        // Result if the assembly has no mate spanning that pair, or if
        // _doc/_poses is null.
        public MateJointClassification.Result Resolve(string parentComponentName, string childComponentName) =>
            MateJointClassification.ChooseBest(ResolveAllCandidates(parentComponentName, childComponentName));

        // Every real (Found) mate spanning this link pair, each tagged with
        // its own MateName.
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

                MateJointClassification.Result result = MateJointClassification.Classify(code, lower, upper);
                result.MateName = feat.Name;
                return result;
            }
            finally { Marshal.ReleaseComObject(mate); }
        }

        // Joints step manual axis pick: reads axis direction + a pivot
        // point from whatever the user selected in the viewport — a
        // cylindrical face (axis = cylinder axis, pivot = a point on it) or
        // a straight edge (axis = endpoint-to-endpoint direction, pivot =
        // start point). Both are PART-LOCAL in the entity's own component,
        // transformed to assembly frame via IComponentPoses.GetPose like
        // everything else in this codebase. Returns false for anything else
        // (a planar/spline face, a curved edge) rather than guessing.
        public bool TryExtractAxisFromSelection(
            object selectedEntity, Component2 owningComponent,
            out Vector3 axisAssembly, out Vector3 originAssembly)
        {
            axisAssembly = Vector3.Zero;
            originAssembly = Vector3.Zero;
            if (selectedEntity == null || owningComponent == null || _poses == null) return false;

            string componentName;
            try { componentName = TopLevelName(owningComponent); }
            catch { return false; }
            if (string.IsNullOrEmpty(componentName)) return false;

            (Matrix3 r, Vector3 t) = _poses.GetPose(componentName);

            if (selectedEntity is Face2 face)
            {
                object surfObj = null;
                try
                {
                    surfObj = face.GetSurface();
                    if (!(surfObj is ISurface surf) || !surf.IsCylinder()) return false;
                    if (!(surf.CylinderParams is double[] cp) || cp.Length < 6) return false;

                    var localOrigin = new Vector3((float)cp[0], (float)cp[1], (float)cp[2]);
                    var localAxis = new Vector3((float)cp[3], (float)cp[4], (float)cp[5]);
                    Vector3 asmAxis = r.Mul(localAxis);
                    if (asmAxis.LengthSquared() < 1e-12f) return false;

                    axisAssembly = Vector3.Normalize(asmAxis);
                    originAssembly = r.Mul(localOrigin) + t;
                    return true;
                }
                catch { return false; }
                finally { if (surfObj != null) try { Marshal.ReleaseComObject(surfObj); } catch { } }
            }

            if (selectedEntity is Edge edge)
            {
                Vertex startVertex = null, endVertex = null;
                try
                {
                    startVertex = edge.GetStartVertex() as Vertex;
                    endVertex = edge.GetEndVertex() as Vertex;
                    if (startVertex == null || endVertex == null) return false;
                    if (!(startVertex.GetPoint() is double[] sp) || sp.Length < 3) return false;
                    if (!(endVertex.GetPoint() is double[] ep) || ep.Length < 3) return false;

                    var localStart = new Vector3((float)sp[0], (float)sp[1], (float)sp[2]);
                    var localEnd = new Vector3((float)ep[0], (float)ep[1], (float)ep[2]);
                    Vector3 localDir = localEnd - localStart;
                    if (localDir.LengthSquared() < 1e-12f) return false;

                    axisAssembly = Vector3.Normalize(r.Mul(Vector3.Normalize(localDir)));
                    originAssembly = r.Mul(localStart) + t;
                    return true;
                }
                catch { return false; }
                finally
                {
                    if (startVertex != null) try { Marshal.ReleaseComObject(startVertex); } catch { }
                    if (endVertex != null) try { Marshal.ReleaseComObject(endVertex); } catch { }
                }
            }

            return false;
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
