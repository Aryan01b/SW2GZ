/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — pure (COM-free) helpers for the Step 3 link hierarchy: roots, children,
descendant test, cycle detection, instant assign-with-move, and re-rooting.
Unit-tested in the net8 project; the WinForms LinkTreeView + PMP drive these.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;

namespace SW2GZ.Build
{
    public static class LinkHierarchy
    {
        public static List<LinkDef> Roots(IReadOnlyList<LinkDef> links)
        {
            var names = new HashSet<string>();
            foreach (LinkDef l in links) names.Add(l.Name);
            var roots = new List<LinkDef>();
            foreach (LinkDef l in links)
            {
                string p = l.ParentName ?? "";
                if (p.Length == 0 || !names.Contains(p)) roots.Add(l);
            }
            return roots;
        }

        public static List<LinkDef> ChildrenOf(IReadOnlyList<LinkDef> links, string name)
        {
            var kids = new List<LinkDef>();
            foreach (LinkDef l in links)
                if (string.Equals(l.ParentName, name)) kids.Add(l);
            return kids;
        }

        public static bool IsDescendant(IReadOnlyList<LinkDef> links, string ancestor, string candidate)
        {
            string cur = candidate;
            var guard = new HashSet<string>();
            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                LinkDef node = Find(links, cur);
                if (node == null) return false;
                if (string.Equals(node.ParentName, ancestor)) return true;
                cur = node.ParentName;
            }
            return false;
        }

        public static bool HasCycle(IReadOnlyList<LinkDef> links)
        {
            foreach (LinkDef start in links)
            {
                var seen = new HashSet<string>();
                string cur = start.Name;
                while (!string.IsNullOrEmpty(cur))
                {
                    if (!seen.Add(cur)) return true;
                    LinkDef node = Find(links, cur);
                    if (node == null) break;
                    cur = node.ParentName;
                }
            }
            return false;
        }

        public static void AssignComponent(IReadOnlyList<LinkDef> links, string activeName, string componentId)
        {
            foreach (LinkDef l in links)
                if (!string.Equals(l.Name, activeName))
                    l.ComponentIds.Remove(componentId);
            LinkDef target = Find(links, activeName);
            if (target != null && !target.ComponentIds.Contains(componentId))
                target.ComponentIds.Add(componentId);
        }

        public static void Reroot(IReadOnlyList<LinkDef> links, string newRootName)
        {
            // Reverse parent pointers along the path from newRoot up to the old root.
            LinkDef node = Find(links, newRootName);
            if (node == null) return;
            string prevParent = node.ParentName;
            node.ParentName = "";
            string childName = node.Name;
            while (!string.IsNullOrEmpty(prevParent))
            {
                LinkDef parent = Find(links, prevParent);
                if (parent == null) break;
                string grand = parent.ParentName;
                parent.ParentName = childName;
                childName = parent.Name;
                prevParent = grand;
            }
        }

        private static LinkDef Find(IReadOnlyList<LinkDef> links, string name)
        {
            foreach (LinkDef l in links)
                if (string.Equals(l.Name, name)) return l;
            return null;
        }
    }
}
