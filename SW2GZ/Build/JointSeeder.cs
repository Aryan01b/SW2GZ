/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — derives the joint list from the link tree. One joint per non-root link
(its parent→child edge). Pure / COM-free + unit-tested; the Joints PMP step
calls Sync on entry to keep joints consistent with the (possibly re-shaped)
link tree while preserving the user's per-joint edits.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;

namespace SW2GZ.Build
{
    public static class JointSeeder
    {
        public static List<JointDef> Sync(
            IReadOnlyList<LinkDef> links, IReadOnlyList<JointDef> existing)
        {
            var result = new List<JointDef>();
            if (links == null) return result;

            // Index existing joints by child link so user edits survive a re-sync.
            var byChild = new Dictionary<string, JointDef>();
            if (existing != null)
                foreach (JointDef j in existing)
                    if (!string.IsNullOrEmpty(j.ChildLink) && !byChild.ContainsKey(j.ChildLink))
                        byChild[j.ChildLink] = j;

            // A link is a root when its parent is empty or names a link that
            // doesn't exist (same rule as LinkHierarchy.Roots). Roots get no joint.
            var names = new HashSet<string>();
            foreach (LinkDef l in links) names.Add(l.Name);

            foreach (LinkDef l in links)
            {
                string parent = l.ParentName ?? string.Empty;
                bool isRoot = parent.Length == 0 || !names.Contains(parent);
                if (isRoot) continue;

                if (byChild.TryGetValue(l.Name, out JointDef keep))
                {
                    keep.ParentLink = parent;   // reflect any re-parenting; keep edits
                    keep.ChildLink = l.Name;
                    result.Add(keep);
                }
                else
                {
                    result.Add(new JointDef
                    {
                        Name = RosNameSanitizer.Sanitize(l.Name + "_joint").Value,
                        ParentLink = parent,
                        ChildLink = l.Name,
                    });
                }
            }

            return result;
        }
    }
}
