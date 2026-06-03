/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — seeds the joint list from the link tree: one joint per non-root link edge
(parent→child), named <parent>_<child>_joint, type Fixed. The user then assigns
a SolidWorks mate to each joint (which sets its type/axis/limits) and can add or
remove joints by hand. Pure / COM-free + unit-tested. Called once when the joint
list is empty; existing joints are preserved (matched by child link) so mate
assignments survive a re-seed, and joints whose child link is gone are dropped.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public static class JointSeeder
    {
        // Canonical joint name for a (parent, child) edge.
        public static string JointName(string parent, string child) =>
            RosNameSanitizer.Sanitize((parent ?? "") + "_" + (child ?? "") + "_joint").Value;

        // Joint type implied by an assigned mate's kind.
        public static UrdfJointType ToJointType(MateKind kind)
        {
            switch (kind)
            {
                case MateKind.Revolute:   return UrdfJointType.Revolute;
                case MateKind.Continuous: return UrdfJointType.Continuous;
                case MateKind.Prismatic:  return UrdfJointType.Prismatic;
                default:                  return UrdfJointType.Fixed;
            }
        }

        public static List<JointDef> Sync(IReadOnlyList<LinkDef> links, IReadOnlyList<JointDef> existing)
        {
            var result = new List<JointDef>();
            if (links == null) return result;

            var byChild = new Dictionary<string, JointDef>();
            if (existing != null)
                foreach (JointDef j in existing)
                    if (!string.IsNullOrEmpty(j.ChildLink) && !byChild.ContainsKey(j.ChildLink))
                        byChild[j.ChildLink] = j;

            var names = new HashSet<string>();
            foreach (LinkDef l in links) names.Add(l.Name);

            foreach (LinkDef l in links)
            {
                string parent = l.ParentName ?? string.Empty;
                bool isRoot = parent.Length == 0 || !names.Contains(parent);
                if (isRoot) continue;

                if (byChild.TryGetValue(l.Name, out JointDef keep))
                {
                    // Keep the mate assignment, but track the tree: name + parent
                    // follow the (possibly renamed/re-parented) links.
                    keep.ParentLink = parent;
                    keep.ChildLink = l.Name;
                    keep.Name = JointName(parent, l.Name);
                    result.Add(keep);
                }
                else
                {
                    result.Add(new JointDef
                    {
                        Name = JointName(parent, l.Name),
                        ParentLink = parent,
                        ChildLink = l.Name,
                    });
                }
            }

            return result;
        }
    }
}
