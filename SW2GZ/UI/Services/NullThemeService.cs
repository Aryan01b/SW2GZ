/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — no-op IThemeService for design-time + unit tests. Reports light theme
and never raises ThemeChanged. Pure C#; source-linked into the test project.
*/
using System;

namespace SW2GZ.UI.Services
{
    public sealed class NullThemeService : IThemeService
    {
        public bool IsDarkTheme => false;

#pragma warning disable CS0067 // event never raised — intentional for the null impl
        public event EventHandler ThemeChanged;
#pragma warning restore CS0067
    }
}
