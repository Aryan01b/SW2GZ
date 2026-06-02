/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P2 — joint-graph assembly. Pure-domain: turns (links, mates) into an
ordered, validated joint list plus the tree root.

Responsibilities:
  - Resolve each MateSpec's ParentLink/ChildLink (sanitized names) to the
    corresponding UrdfLink, then delegate to JointBuilder.Build.
  - Determine the tree root = the link that is the child of no joint.
  - Order the emitted joints so a parent's incoming joint precedes any of
    its children's joints (topological / parents-first).
  - Surface warnings (non-fatal) for unknown links, self-loops, and
    multiple / zero roots. Deeper structural validation (cycles, multiple
    parents) is owned by RobotModelValidator — this stage only warns so the
    caller sees an early hint; it does not duplicate those checks.
*/
using System.Collections.Generic;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public static class JointGraphBuilder
    {
        public static (IReadOnlyList<UrdfJoint> Joints, string RootLink, IReadOnlyList<string> Warnings)
            Build(IReadOnlyList<UrdfLink> links, IReadOnlyList<MateSpec> mates)
        {
            var warnings = new List<string>();
            var joints = new List<UrdfJoint>();

            if (links == null) links = System.Array.Empty<UrdfLink>();
            if (mates == null) mates = System.Array.Empty<MateSpec>();

            // O(1) name -> link lookup. Ordinal so sanitized names compare exactly.
            var byName = new Dictionary<string, UrdfLink>(System.StringComparer.Ordinal);
            foreach (UrdfLink link in links)
            {
                if (link == null) continue;
                // Last-wins on duplicate names; RobotModelValidator flags dup links.
                byName[link.Name] = link;
            }

            // Track each child so we can compute the root afterwards. A link that
            // never appears as a child is a candidate root.
            var childLinkNames = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (MateSpec mate in mates)
            {
                if (mate == null) continue;

                string parentName = mate.ParentLink;
                string childName = mate.ChildLink;

                // Self-loop guard — a mate that names the same link on both sides
                // can't be a joint (would create a 0-length cycle).
                if (parentName != null && parentName == childName)
                {
                    warnings.Add($"Mate '{mate.Name}' connects link '{parentName}' to itself — skipped.");
                    continue;
                }

                if (parentName == null || !byName.TryGetValue(parentName, out UrdfLink parent))
                {
                    warnings.Add($"Mate '{mate.Name}' references unknown parent link '{parentName}' — skipped.");
                    continue;
                }
                if (childName == null || !byName.TryGetValue(childName, out UrdfLink child))
                {
                    warnings.Add($"Mate '{mate.Name}' references unknown child link '{childName}' — skipped.");
                    continue;
                }

                var (joint, jointWarnings) = JointBuilder.Build(mate, parent, child);
                joints.Add(joint);
                foreach (string w in jointWarnings) warnings.Add(w);

                childLinkNames.Add(childName);
            }

            // Root determination — links that are never a child of any joint.
            var roots = new List<string>();
            foreach (UrdfLink link in links)
            {
                if (link == null) continue;
                if (!childLinkNames.Contains(link.Name))
                    roots.Add(link.Name);
            }

            string rootLink;
            if (roots.Count == 1)
            {
                rootLink = roots[0];
            }
            else if (roots.Count == 0)
            {
                // Every link is a child of something — implies a cycle (no tree
                // root). RobotModelValidator will reject the cycle; we warn and
                // fall back to the first link name (or empty) so callers have a
                // deterministic value.
                warnings.Add("No root link found (every link is a child of some joint) — " +
                             "the mate graph may contain a cycle.");
                rootLink = links.Count > 0 && links[0] != null ? links[0].Name : string.Empty;
            }
            else
            {
                // Multiple disconnected roots — the assembly isn't a single tree
                // (some links have no joint linking them to the rest). The first
                // root is returned as the canonical base; the others stay floating.
                warnings.Add($"Multiple root links found ({string.Join(", ", roots)}) — " +
                             "the robot is not a single connected tree; using '" + roots[0] + "' as base.");
                rootLink = roots[0];
            }

            // Order joints parents-first. Build a child-name -> joint map and walk
            // outward from the root via BFS so any parent's joint is emitted before
            // its children's. Joints unreachable from the root (disconnected
            // components) are appended in their original order afterwards.
            var ordered = OrderParentsFirst(joints, rootLink);

            return (ordered, rootLink, warnings);
        }

        // BFS from the root: emit joints in the order their child links are first
        // reached, guaranteeing a parent's incoming joint precedes its children's.
        // Disconnected joints (not reachable from root) are appended last in
        // original order so nothing is silently dropped.
        private static IReadOnlyList<UrdfJoint> OrderParentsFirst(
            List<UrdfJoint> joints, string rootLink)
        {
            if (joints.Count <= 1) return joints;

            // parentName -> joints whose ParentLink == parentName (preserve order).
            var byParent = new Dictionary<string, List<UrdfJoint>>(System.StringComparer.Ordinal);
            foreach (UrdfJoint j in joints)
            {
                if (!byParent.TryGetValue(j.ParentLink, out List<UrdfJoint> bucket))
                {
                    bucket = new List<UrdfJoint>();
                    byParent[j.ParentLink] = bucket;
                }
                bucket.Add(j);
            }

            var result = new List<UrdfJoint>(joints.Count);
            var emitted = new HashSet<UrdfJoint>();
            var visitedLinks = new HashSet<string>(System.StringComparer.Ordinal);

            var queue = new Queue<string>();
            if (!string.IsNullOrEmpty(rootLink))
            {
                queue.Enqueue(rootLink);
                visitedLinks.Add(rootLink);
            }

            while (queue.Count > 0)
            {
                string parentName = queue.Dequeue();
                if (!byParent.TryGetValue(parentName, out List<UrdfJoint> outgoing)) continue;
                foreach (UrdfJoint j in outgoing)
                {
                    if (emitted.Contains(j)) continue;
                    result.Add(j);
                    emitted.Add(j);
                    if (visitedLinks.Add(j.ChildLink))
                        queue.Enqueue(j.ChildLink);
                }
            }

            // Append any joints not reachable from the root (disconnected
            // components / cycles) in original order.
            foreach (UrdfJoint j in joints)
                if (!emitted.Contains(j))
                    result.Add(j);

            return result;
        }
    }
}
