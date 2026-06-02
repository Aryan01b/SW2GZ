/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — pure validation for wizard Step 3 link definitions. The PMP supplies the
full set of top-level component ids (COM-derived); everything here is COM-free
and unit-tested. Returns a flat list of human-readable blocking issues (empty =
ready to advance).
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

            // Base count.
            int baseCount = 0;
            foreach (LinkDef l in links) if (l.IsBase) baseCount++;
            if (baseCount == 0) issues.Add("No base link is set — mark exactly one link as the base.");
            else if (baseCount > 1) issues.Add("More than one base link is set — only one is allowed.");

            // Names: unique + non-empty; empty links.
            var seenNames = new HashSet<string>();
            foreach (LinkDef l in links)
            {
                string name = (l.Name ?? "").Trim();
                if (name.Length == 0) issues.Add("A link has an empty name.");
                else if (!seenNames.Add(name)) issues.Add("Duplicate link name: " + name);
                if (l.ComponentIds == null || l.ComponentIds.Count == 0)
                    issues.Add("Link '" + name + "' has no components assigned.");
            }

            // Assignment coverage + duplicates.
            var assignedOnce = new HashSet<string>();
            var assignedTwice = new HashSet<string>();
            foreach (LinkDef l in links)
            {
                if (l.ComponentIds == null) continue;
                foreach (string id in l.ComponentIds)
                {
                    if (!assignedOnce.Add(id)) assignedTwice.Add(id);
                }
            }
            foreach (string id in assignedTwice)
                issues.Add("Component assigned to more than one link: " + id);

            if (allComponentIds != null)
            {
                foreach (string id in allComponentIds)
                    if (!assignedOnce.Contains(id))
                        issues.Add("Component unassigned: " + id);
            }

            return issues;
        }
    }
}
