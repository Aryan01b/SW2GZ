/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzTheme — minimal light/dark palette + recursive Apply for our WinForms
dialogs (Sw2gzExportWizardForm and friends). WinForms has no built-in
dark-mode story; we read HKCU\...\Personalize\AppsUseLightTheme and walk the
control tree once on Show, then again on WM_SETTINGCHANGE so the dialog
follows the system if the user flips theme while it's open.

The palette is intentionally tiny — two background tints + two foreground
tints + an accent — so the result matches a SolidWorks PMP's flat aesthetic
rather than ribbon shine.
*/
#if SW_INTEROP
using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SW2GZ.UI
{
    public static class Sw2gzTheme
    {
        public sealed class Palette
        {
            public Color FormBack;
            public Color SurfaceBack;     // panels, group sections
            public Color InputBack;       // textboxes, lists
            public Color BorderColor;     // subtle separators
            public Color ForeText;        // primary text
            public Color SubtleText;      // secondary / hint text
            public Color Accent;          // selection / focus
            public Color NavBack;         // footer button row
            public Color ButtonBack;
            public Color ButtonHoverBack;
            public bool  IsDark;
        }

        public static Palette Light => new Palette
        {
            FormBack        = Color.FromArgb(246, 247, 249),
            SurfaceBack     = Color.White,
            InputBack       = Color.White,
            BorderColor     = Color.FromArgb(208, 213, 221),
            ForeText        = Color.FromArgb(28, 32, 38),
            SubtleText      = Color.FromArgb(110, 118, 129),
            Accent          = Color.FromArgb(0, 120, 215),
            NavBack         = Color.FromArgb(238, 240, 244),
            ButtonBack      = Color.White,
            ButtonHoverBack = Color.FromArgb(228, 234, 244),
            IsDark          = false,
        };

        public static Palette Dark => new Palette
        {
            FormBack        = Color.FromArgb(32, 32, 36),
            SurfaceBack     = Color.FromArgb(40, 40, 46),
            InputBack       = Color.FromArgb(56, 56, 62),
            BorderColor     = Color.FromArgb(70, 70, 78),
            ForeText        = Color.FromArgb(232, 234, 238),
            SubtleText      = Color.FromArgb(160, 165, 175),
            Accent          = Color.FromArgb(76, 156, 224),
            NavBack         = Color.FromArgb(26, 26, 30),
            ButtonBack      = Color.FromArgb(56, 56, 62),
            ButtonHoverBack = Color.FromArgb(72, 72, 80),
            IsDark          = true,
        };

        /// True iff the user's Windows apps theme is set to dark.
        public static bool SystemIsDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key == null) return false;
                    object v = key.GetValue("AppsUseLightTheme");
                    if (v is int i) return i == 0;
                    return false;
                }
            }
            catch { return false; }
        }

        public static Palette Current() => SystemIsDark() ? Dark : Light;

        /// Recursively apply the palette to a control tree. Idempotent — safe
        /// to call again on theme change.
        public static void Apply(Control root, Palette p)
        {
            if (root == null || p == null) return;

            if (root is Form form)
            {
                form.BackColor = p.FormBack;
                form.ForeColor = p.ForeText;
            }
            else if (root is Button btn)
            {
                btn.FlatStyle              = FlatStyle.Flat;
                btn.BackColor              = p.ButtonBack;
                btn.ForeColor              = p.ForeText;
                btn.FlatAppearance.BorderColor      = p.BorderColor;
                btn.FlatAppearance.BorderSize       = 1;
                btn.FlatAppearance.MouseOverBackColor = p.ButtonHoverBack;
                btn.FlatAppearance.MouseDownBackColor = p.ButtonHoverBack;
                btn.UseVisualStyleBackColor = false;
            }
            else if (root is TextBox tb)
            {
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor   = p.InputBack;
                tb.ForeColor   = p.ForeText;
            }
            else if (root is ListBox lb)
            {
                lb.BorderStyle = BorderStyle.FixedSingle;
                lb.BackColor   = p.InputBack;
                lb.ForeColor   = p.ForeText;
            }
            else if (root is Panel panel)
            {
                // Panels named "nav" get the footer tint; everything else gets surface.
                if (string.Equals(panel.Name, "nav", StringComparison.OrdinalIgnoreCase))
                    panel.BackColor = p.NavBack;
                else
                    panel.BackColor = p.SurfaceBack;
                panel.ForeColor = p.ForeText;
            }
            else if (root is Label lbl)
            {
                lbl.BackColor = Color.Transparent;
                // Labels tagged "subtitle" get the subtle tint.
                lbl.ForeColor = (lbl.Tag as string) == "subtle" ? p.SubtleText : p.ForeText;
            }
            else if (root is ProgressBar pb)
            {
                pb.ForeColor = p.Accent;
                pb.BackColor = p.InputBack;
            }
            else
            {
                root.BackColor = p.SurfaceBack;
                root.ForeColor = p.ForeText;
            }

            foreach (Control child in root.Controls) Apply(child, p);
        }
    }
}
#endif
