/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Syncs Robot.Joints against Robot.Links after any link-tree edit
(add/remove/reparent). Previously RebuildJoints() cleared and rebuilt every
JointDef from scratch on each edit — harmless while Type stayed hardcoded
Fixed, but would silently discard the user's Type/Axis/Limit edits (see
docs/superpowers/specs/2026-07-03-robot-joint-type-panel-design.md) once
those fields became real. Match by (ParentLink, ChildLink): a pair that
still exists keeps its JointDef untouched; new pairs get a fresh
default-Fixed JointDef; pairs whose link was removed/reparented away are
dropped.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public static class JointDefReconciler
    {
        public static List<JointDef> Reconcile(IReadOnlyList<JointDef> existing, IReadOnlyList<LinkDef> links)
        {
            var existingByPair = new Dictionary<(string Parent, string Child), JointDef>();
            if (existing != null)
            {
                foreach (JointDef j in existing)
                    existingByPair[(j.ParentLink, j.ChildLink)] = j;
            }

            var result = new List<JointDef>();
            if (links == null) return result;

            foreach (LinkDef link in links)
            {
                if (string.IsNullOrEmpty(link.ParentName)) continue;

                var key = (link.ParentName, link.Name);
                if (existingByPair.TryGetValue(key, out JointDef kept))
                {
                    result.Add(kept);
                }
                else
                {
                    result.Add(new JointDef
                    {
                        Name = link.ParentName + "_to_" + link.Name,
                        ParentLink = link.ParentName,
                        ChildLink = link.Name,
                        Type = UrdfJointType.Fixed,
                    });
                }
            }
            return result;
        }
    }
}
