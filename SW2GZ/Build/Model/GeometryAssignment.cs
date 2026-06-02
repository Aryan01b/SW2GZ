/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure-C#, COM-free model for link geometry assignment. The native geometry
PropertyManagerPage (GeometryPropertyManager, #if SW_INTEROP) writes into it
as the user picks bodies/components in the 3D viewport; the WPF wizard reads
it back to seed each LinkViewModel's HasGeometry + body count.

Deliberately a dumb mutable container plus a couple of helpers so it compiles
and unit-tests in the net8 test project (no SolidWorks dependency).
*/
using System;
using System.Collections.Generic;

namespace SW2GZ.Build.Model
{
    /// One top-level link and the viewport body/component identifiers assigned to it.
    public sealed class LinkGeometry
    {
        public LinkGeometry(string linkName)
        {
            LinkName = linkName;
            SelectedBodyNames = new List<string>();
        }

        /// Sanitized link/component name. Mutable: the PMP lets the user edit it.
        public string LinkName { get; set; }

        /// Persistent component/body identifiers picked in the viewport.
        public List<string> SelectedBodyNames { get; }

        /// True once at least one body/component has been assigned to this link.
        public bool HasGeometry => SelectedBodyNames.Count > 0;

        /// Replaces the current selection with the supplied identifiers.
        public void Assign(IEnumerable<string> bodyNames)
        {
            SelectedBodyNames.Clear();
            if (bodyNames == null) return;
            foreach (string name in bodyNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    SelectedBodyNames.Add(name);
            }
        }

        /// Empties the assignment (HasGeometry becomes false).
        public void Clear() => SelectedBodyNames.Clear();
    }

    /// One LinkGeometry per top-level link, written by the PMP and read by the wizard.
    public sealed class GeometryAssignment
    {
        public GeometryAssignment(IEnumerable<string> linkNames)
        {
            Links = new List<LinkGeometry>();
            if (linkNames == null) return;
            foreach (string name in linkNames)
            {
                Links.Add(new LinkGeometry(name));
            }
        }

        /// One entry per top-level link, in seed order.
        public List<LinkGeometry> Links { get; }

        /// Returns the LinkGeometry for the given link name, or null if unknown.
        public LinkGeometry Find(string linkName)
        {
            if (linkName == null) return null;
            foreach (LinkGeometry link in Links)
            {
                if (string.Equals(link.LinkName, linkName, StringComparison.Ordinal))
                    return link;
            }
            return null;
        }
    }
}
