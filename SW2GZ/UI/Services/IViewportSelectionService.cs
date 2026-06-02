/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — wizard service boundary. Abstracts SolidWorks viewport selection so
the Links step can assign geometry without touching COM in tests. The real
impl (SwViewportSelectionService) reads ISelectionMgr behind #if SW_INTEROP.
*/
using System.Collections.Generic;

namespace SW2GZ.UI.Services
{
    public interface IViewportSelectionService
    {
        /// Body/component names currently selected in the SW viewport.
        IReadOnlyList<string> GetSelectedBodyNames();

        /// Convenience count of the current selection.
        int SelectedCount { get; }
    }
}
