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
    public sealed class WizardAssemblyWalker : IAssemblyWalker, IComponentPoseSource
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
            var mates = new List<MateSpec>();
            foreach (JointDef j in _joints)
            {
                var axis = new Vector3((float)j.AxisX, (float)j.AxisY, (float)j.AxisZ);
                mates.Add(new MateSpec(
                    Name:          j.Name,
                    Kind:          ToMateKind(j.Type),
                    Origin:        Pose.Identity,        // SW→ROS conversion deferred
                    Axis:          axis,
                    LimitLower:    j.LimitLower,
                    LimitUpper:    j.LimitUpper,
                    LimitEffort:   0.0,
                    LimitVelocity: 0.0,
                    Interface:     UrdfCmdInterface.Position,
                    ParentLink:    j.ParentLink,
                    ChildLink:     j.ChildLink));
            }
            return mates.AsReadOnly();
        }

        // IComponentPoseSource — assembly-frame pose of a part. Used by
        // Sw2gzPipeline to anchor link frames at their first-part location and
        // to rebase mesh vertices / joint origins into the right URDF frames.
        // SW Transform2.ArrayData layout: [0..8] rotation 3x3 row-major,
        // [9..11] translation, [12] scale, [13..15] padding. Matches the
        // convention already used by SolidWorksAssemblyWalker.RotateByComponent
        // and SolidWorksMeshTessellator's vertex bake-in.
        public Pose GetComponentPose(string partPath)
        {
            if (string.IsNullOrEmpty(partPath)) return Pose.Identity;
            object[] comps = (object[])_doc.GetComponents(false);
            Component2 comp = SolidWorksMassProperties.FindComponent(comps, partPath);
            if (comp == null) return Pose.Identity;

            MathTransform xform = comp.Transform2;
            if (!(xform?.ArrayData is double[] d) || d.Length < 12) return Pose.Identity;

            // Translation is direct.
            var translation = new Vector3((float)d[9], (float)d[10], (float)d[11]);

            // Build a quaternion from the 3x3 rotation block (row-major as
            // applied throughout the codebase). Shepperd's method handles the
            // sign-ambiguity case without trig.
            float m00 = (float)d[0], m01 = (float)d[1], m02 = (float)d[2];
            float m10 = (float)d[3], m11 = (float)d[4], m12 = (float)d[5];
            float m20 = (float)d[6], m21 = (float)d[7], m22 = (float)d[8];
            float trace = m00 + m11 + m22;
            float qx, qy, qz, qw;
            if (trace > 0f)
            {
                float s = (float)System.Math.Sqrt(trace + 1f) * 2f;   // 4 * qw
                qw = 0.25f * s;
                qx = (m21 - m12) / s;
                qy = (m02 - m20) / s;
                qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)System.Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                qw = (m21 - m12) / s;
                qx = 0.25f * s;
                qy = (m01 + m10) / s;
                qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = (float)System.Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                qw = (m02 - m20) / s;
                qx = (m01 + m10) / s;
                qy = 0.25f * s;
                qz = (m12 + m21) / s;
            }
            else
            {
                float s = (float)System.Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                qw = (m10 - m01) / s;
                qx = (m02 + m20) / s;
                qy = (m12 + m21) / s;
                qz = 0.25f * s;
            }
            return new Pose(translation, Quaternion.Normalize(new Quaternion(qx, qy, qz, qw)));
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

        // Recursively collect leaf-component part-doc paths (mirrors the assembly
        // walker): a leaf is a childless component whose model is a part doc.
        private static void CollectLeafPaths(Component2 comp, List<string> paths)
        {
            object[] children = (object[])comp.GetChildren();
            bool hasChildren = children != null && children.Length > 0;
            if (!hasChildren)
            {
                IModelDoc2 m = (IModelDoc2)comp.GetModelDoc2();
                if (m != null && m.GetType() == (int)swDocumentTypes_e.swDocPART)
                    paths.Add(comp.GetPathName());
                return;
            }
            foreach (object o in children) CollectLeafPaths((Component2)o, paths);
        }
    }
}
#endif
