/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — derives the joint list from the link tree. One joint per non-root link
(its parent→child edge). Pure / COM-free + unit-tested; the Joints PMP step
calls Sync on entry to keep joints consistent with the (possibly re-shaped)
link tree while preserving the user's per-joint edits.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public static class JointSeeder
    {
        public static List<JointDef> Sync(
            IReadOnlyList<LinkDef> links,
            IReadOnlyList<JointDef> existing,
            IReadOnlyList<MateAxis> mateAxes = null)
        {
            var result = new List<JointDef>();
            if (links == null) return result;

            // Index existing joints by child link so user edits survive a re-sync.
            var byChild = new Dictionary<string, JointDef>();
            if (existing != null)
                foreach (JointDef j in existing)
                    if (!string.IsNullOrEmpty(j.ChildLink) && !byChild.ContainsKey(j.ChildLink))
                        byChild[j.ChildLink] = j;

            // Mate-derived (axis, type, limits) keyed by the edge's child link
            // name. Only applied to newly-seeded joints — never overrides an edit.
            Dictionary<string, MateDerived> mateByChild = BuildMateByChild(links, mateAxes);

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
                    var fresh = new JointDef
                    {
                        Name = RosNameSanitizer.Sanitize(l.Name + "_joint").Value,
                        ParentLink = parent,
                        ChildLink = l.Name,
                    };
                    if (mateByChild.TryGetValue(l.Name, out MateDerived derived))
                    {
                        fresh.Type = derived.Type;
                        if (derived.Axis != Vector3.Zero) fresh.SetAxis(derived.Axis);
                        if (derived.Lower.HasValue) fresh.LimitLower = derived.Lower;
                        if (derived.Upper.HasValue) fresh.LimitUpper = derived.Upper;
                    }
                    result.Add(fresh);
                }
            }

            return result;
        }

        // Per-edge aggregate of what the mates tell us about a joint.
        private struct MateDerived
        {
            public Vector3 Axis;
            public UrdfJointType Type;
            public double? Lower;
            public double? Upper;
        }

        // Resolves each mate's two components to their owning links, identifies the
        // child end of that tree edge, and aggregates axis/type/limits. A joint may
        // be defined by several mates (e.g. a concentric mate for the axis plus a
        // limit-angle mate for the range), so entries merge across mates per child.
        private static Dictionary<string, MateDerived> BuildMateByChild(
            IReadOnlyList<LinkDef> links, IReadOnlyList<MateAxis> mateAxes)
        {
            var map = new Dictionary<string, MateDerived>();
            if (mateAxes == null) return map;

            // component id -> owning link name
            var compToLink = new Dictionary<string, string>();
            foreach (LinkDef l in links)
                if (l.ComponentIds != null)
                    foreach (string cid in l.ComponentIds)
                        if (cid != null) compToLink[cid] = l.Name;

            var byName = new Dictionary<string, LinkDef>();
            foreach (LinkDef l in links) byName[l.Name] = l;

            foreach (MateAxis ma in mateAxes)
            {
                if (ma == null) continue;
                if (!compToLink.TryGetValue(ma.ComponentA ?? "", out string la)) continue;
                if (!compToLink.TryGetValue(ma.ComponentB ?? "", out string lb)) continue;
                if (la == lb) continue;

                // Which link is the child of the other on this edge?
                string child = null;
                if (byName.TryGetValue(la, out LinkDef da) && da.ParentName == lb) child = la;
                else if (byName.TryGetValue(lb, out LinkDef db) && db.ParentName == la) child = lb;
                if (child == null) continue;

                map.TryGetValue(child, out MateDerived cur);
                UrdfJointType k = MapKind(ma.Kind);
                if (k != UrdfJointType.Fixed) { cur.Type = k; cur.Axis = ma.Axis; }
                if (ma.LimitLower.HasValue) cur.Lower = ma.LimitLower;
                if (ma.LimitUpper.HasValue) cur.Upper = ma.LimitUpper;
                map[child] = cur;
            }

            return map;
        }

        private static UrdfJointType MapKind(MateKind kind)
        {
            switch (kind)
            {
                case MateKind.Revolute:   return UrdfJointType.Revolute;
                case MateKind.Continuous: return UrdfJointType.Continuous;
                case MateKind.Prismatic:  return UrdfJointType.Prismatic;
                default:                  return UrdfJointType.Fixed;
            }
        }
    }
}
