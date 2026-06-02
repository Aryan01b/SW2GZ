/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — IThemeService that mirrors the SolidWorks UI theme. v2.1 best-effort:
defaults to light. The real SW theme read (via swUserPreferenceIntegerValue
color settings) lands behind #if SW_INTEROP later — for now it's a light stub
with a TODO so the wizard renders correctly during early integration.

Compiled only into SW2GZ.csproj (net48); NOT source-linked into the test
project. The VM layer is tested against NullThemeService instead.
*/
using System;
using SW2GZ.UI.Services;

#if SW_INTEROP
using SolidWorks.Interop.sldworks;
#endif

namespace SW2GZ.UI.Services.Sw
{
    public sealed class SwThemeService : IThemeService
    {
#if SW_INTEROP
        private readonly SldWorks _swApp;

        public SwThemeService(SldWorks swApp)
        {
            _swApp = swApp;
        }
#endif

        // Skeleton ctor — preserves a light default when no SW handle is present.
        public SwThemeService() { }

        // TODO P8-COM: read the SolidWorks background/UI color preference and
        // map a dark background to IsDarkTheme == true. Defaulting to light
        // keeps the wizard legible until that wiring lands.
        public bool IsDarkTheme => false;

#pragma warning disable CS0067 // raised once the SW theme-change hook is wired
        public event EventHandler ThemeChanged;
#pragma warning restore CS0067
    }
}
