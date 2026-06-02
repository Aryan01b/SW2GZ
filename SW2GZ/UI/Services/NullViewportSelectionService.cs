/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — no-op IViewportSelectionService for design-time + unit tests. Always
reports an empty selection so view-models can be constructed without a SW
session. Pure C#; source-linked into the test project.
*/
using System;
using System.Collections.Generic;

namespace SW2GZ.UI.Services
{
    public sealed class NullViewportSelectionService : IViewportSelectionService
    {
        public IReadOnlyList<string> GetSelectedBodyNames() => Array.Empty<string>();
        public int SelectedCount => 0;
    }
}
