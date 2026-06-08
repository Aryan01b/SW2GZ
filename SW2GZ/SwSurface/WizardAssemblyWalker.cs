/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — an IAssemblyWalker that feeds the export pipeline the WIZARD's model rather
than re-deriving everything from the raw assembly:

  WalkActive()  → one LinkSpec per wizard LinkDef, resolving its assigned
                  component ids to leaf part paths via the live assembly.
  WalkMates()   → one MateSpec per wizard JointDef (type, axis, limits, parent,
                  child), so the pipeline's JointGraphBuilder emits exactly the
                  joints the user configured.

This lets Sw2gzPipeline build the package from the user's link groupings and
mate-assigned joints. COM-bound; only compiled in the SW add-in build.
*/
#if SW_INTEROP
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SW2GZ.SwSurface
{
    public sealed class WizardAssemblyWalker : IAssemblyWalker, IComponentPoseSource, IComponentRawTransformSource
    {
        private readonly AssemblyDoc _doc;
        private readonly IReadOnlyList<LinkDef> _links;
        private readonly IReadOnlyList<JointDef> _joints;

        public WizardAssemblyWalker(AssemblyDoc doc, IReadOnlyList<LinkDef> links, IReadOnlyList<JointDef> joints)
        {
            _doc = doc;
            _links = links ?? new List<LinkDef>();
            _joints = joints ?? new List<JointDef>();
        }

        public IReadOnlyList<LinkSpec> WalkActive()
        {
            // Map top-level component Name2 → its leaf part paths.
            var byName = new Dictionary<string, List<string>>();
            object[] comps = (object[])_doc.GetComponents(true);
            if (comps != null)
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    var paths = new List<string>();
                    CollectLeafPaths(c, paths);
                    byName[c.Name2] = paths;
                }

            var specs = new List<LinkSpec>();
            foreach (LinkDef l in _links)
            {
                var parts = new List<string>();
                if (l.ComponentIds != null)
                    foreach (string id in l.ComponentIds)
                        if (id != null && byName.TryGetValue(id, out List<string> p)) parts.AddRange(p);
                if (parts.Count == 0) continue;   // skip geometry-less links
                specs.Add(new LinkSpec(l.Name, parts.AsReadOnly()));
            }
            return specs.AsReadOnly();
        }

        public IReadOnlyList<MateSpec> WalkMates()
        {
            // Auto-detect path (D2 of the AutoJointResolver plan). The PMP's
            // Joints step runs AutoJointResolver once per JointDef and writes
            // the cylinder axis + origin into AxisX/Y/Z + OriginX/Y/Z. Here
            // we just translate that cached data into MateSpecs — no more
            // Reference-CS or Reference-Axis lookups at export time.
            //
            // RefCsName / RefAxisName remain on JointDef as DataMembers for
            // back-compat with older saved Sw2gzDocs, but their values are
            // ignored on this path.
            var mates = new List<MateSpec>();
            foreach (JointDef j in _joints)
            {
                var cachedAxis = new Vector3((float)j.AxisX, (float)j.AxisY, (float)j.AxisZ);

                // Joint origin: prefer the new auto-detect HasOrigin field;
                // fall back to legacy HasMatePoint for payloads written by
                // the pre-D2 wizard (so existing saved docs keep working).
                Vector3? originAsm = null;
                if (j.HasOrigin)
                    originAsm = new Vector3((float)j.OriginX, (float)j.OriginY, (float)j.OriginZ);
                else if (j.HasMatePoint)
                    originAsm = new Vector3((float)j.MatePointX, (float)j.MatePointY, (float)j.MatePointZ);

                // MateSpec.Origin stays Identity. The assembly-frame mate point
                // travels on MatePointAssembly and Sw2gzPipeline routes it
                // through JointOriginResolver.Compute(... , matePoint), which
                // localizes it into the parent frame (R_parent⁻¹ · (matePoint
                // − parentAnchor.pos)) and computes the joint rotation from
                // the parent/child anchor pair. Setting Origin to a non-
                // identity pose here would make the pipeline's
                // walkerProvidedOrigin branch fire and emit the raw
                // assembly-frame point as a parent-frame origin — wrong.
                mates.Add(new MateSpec(
                    Name:              j.Name,
                    Kind:              ToMateKind(j.Type),
                    Origin:            Pose.Identity,
                    Axis:              cachedAxis,
                    LimitLower:        j.LimitLower,
                    LimitUpper:        j.LimitUpper,
                    LimitEffort:       0.0,
                    LimitVelocity:     0.0,
                    Interface:         UrdfCmdInterface.Position,
                    ParentLink:        j.ParentLink,
                    ChildLink:         j.ChildLink,
                    MatePointAssembly: originAsm));
            }
            return mates.AsReadOnly();
        }

        // IComponentPoseSource — assembly-frame pose of a part. Used by
        // Sw2gzPipeline to anchor link frames at their first-part location and
        // to rebase mesh vertices / joint origins into the right URDF frames.
        // The Pose extraction itself is in SW2GZ.Math.MathTransformPose so
        // SwJointPoseReader can reuse the same row-major + Shepperd's path
        // for Reference Coordinate System transforms.
        public Pose GetComponentPose(string partPath)
        {
            if (string.IsNullOrEmpty(partPath)) return Pose.Identity;
            object[] comps = (object[])_doc.GetComponents(false);
            Component2 comp = SolidWorksMassProperties.FindComponent(comps, partPath);
            if (comp == null) return Pose.Identity;

            MathTransform xform = comp.Transform2;
            return MathTransformPose.FromArrayData(xform?.ArrayData as double[]);
        }

        // IComponentRawTransformSource — returns the verbatim 16 doubles SW
        // hands back from Component2.Transform2.ArrayData. Used by the
        // PoseDumpWriter diagnostic so the column-major vs row-major
        // interpretation can be inspected against live values without
        // changing the production pose-extraction path. Returns null when
        // the part is unknown.
        public double[] GetComponentRawTransform(string partPath)
        {
            if (string.IsNullOrEmpty(partPath)) return null;
            object[] comps = (object[])_doc.GetComponents(false);
            Component2 comp = SolidWorksMassProperties.FindComponent(comps, partPath);
            if (comp == null) return null;
            MathTransform xform = comp.Transform2;
            if (!(xform?.ArrayData is double[] d)) return null;
            var copy = new double[d.Length];
            System.Array.Copy(d, copy, d.Length);
            return copy;
        }

        private static MateKind ToMateKind(UrdfJointType t)
        {
            switch (t)
            {
                case UrdfJointType.Revolute:   return MateKind.Revolute;
                case UrdfJointType.Continuous: return MateKind.Continuous;
                case UrdfJointType.Prismatic:  return MateKind.Prismatic;
                case UrdfJointType.Planar:     return MateKind.Planar;
                case UrdfJointType.Floating:   return MateKind.Floating;
                default:                       return MateKind.Fixed;
            }
        }

        // Recursively collect leaf-component INSTANCE identifiers (Component2.Name2).
        // Mirrors SolidWorksAssemblyWalker.CollectLeafPaths: Name2 is instance-
        // unique ("base-1@3R_ARM"), whereas GetPathName() returns the part-file
        // path — shared across instances of the same part. The downstream lookup
        // (SolidWorksMassProperties.FindComponent / SolidWorksMeshTessellator)
        // matches against Name2, so emitting GetPathName() here yielded the
        // "Component path not found in active assembly: ...\foo.SLDPRT" error.
        private static void CollectLeafPaths(Component2 comp, List<string> paths)
        {
            object[] children = (object[])comp.GetChildren();
            bool hasChildren = children != null && children.Length > 0;
            if (!hasChildren)
            {
                IModelDoc2 m = (IModelDoc2)comp.GetModelDoc2();
                if (m != null && m.GetType() == (int)swDocumentTypes_e.swDocPART)
                    paths.Add(comp.Name2);
                return;
            }
            foreach (object o in children) CollectLeafPaths((Component2)o, paths);
        }
    }
}
#endif
