/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — pure validation for the Step 3 link hierarchy. Blocking issues (empty =
ready to advance): exactly one root, valid parents, no cycle, unique non-empty
names, no empty link, and full component coverage. COM-free + unit-tested.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;

namespace SW2GZ.Build
{
    public static class LinkDefValidator
    {
        public static List<string> Validate(
            IReadOnlyList<LinkDef> links, IReadOnlyCollection<string> allComponentIds)
        {
            var issues = new List<string>();
            if (links == null) links = new List<LinkDef>();

            var names = new HashSet<string>();
            foreach (LinkDef l in links) names.Add(l.Name ?? "");

            // Roots: exactly one parentless (or unknown-parent) link.
            List<LinkDef> roots = LinkHierarchy.Roots(links);
            if (links.Count > 0 && roots.Count == 0)
                issues.Add("No root (base) link — every link has a parent (cycle?).");
            else if (roots.Count > 1)
                issues.Add("More than one root link — exactly one base is allowed.");

            if (LinkHierarchy.HasCycle(links))
                issues.Add("The link hierarchy has a cycle.");

            // Names + parents + empty links.
            var seenNames = new HashSet<string>();
            foreach (LinkDef l in links)
            {
                string name = (l.Name ?? "").Trim();
                if (name.Length == 0) issues.Add("A link has an empty name.");
                else if (!seenNames.Add(name)) issues.Add("Duplicate link name: " + name);

                string p = l.ParentName ?? "";
                if (p.Length > 0 && !names.Contains(p))
                    issues.Add("Link '" + name + "' has an unknown parent: " + p);

                if (l.ComponentIds == null || l.ComponentIds.Count == 0)
                    issues.Add("Link '" + name + "' has no components assigned.");
            }

            // Coverage + duplicates.
            var once = new HashSet<string>();
            var twice = new HashSet<string>();
            foreach (LinkDef l in links)
                if (l.ComponentIds != null)
                    foreach (string id in l.ComponentIds)
                        if (!once.Add(id)) twice.Add(id);
            foreach (string id in twice)
                issues.Add("Component assigned to more than one link: " + id);
            if (allComponentIds != null)
                foreach (string id in allComponentIds)
                    if (!once.Contains(id)) issues.Add("Component unassigned: " + id);

            return issues;
        }
    }
}
