/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — wizard service boundary. Surfaces the current SolidWorks theme so the
WPF shell can swap light/dark resource dictionaries. ThemeChanged fires when
SolidWorks toggles its theme.
*/
using System;

namespace SW2GZ.UI.Services
{
    public interface IThemeService
    {
        bool IsDarkTheme { get; }
        event EventHandler ThemeChanged;
    }
}
