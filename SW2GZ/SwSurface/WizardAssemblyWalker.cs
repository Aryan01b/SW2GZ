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
    public sealed class WizardAssemblyWalker : IAssemblyWalker
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
