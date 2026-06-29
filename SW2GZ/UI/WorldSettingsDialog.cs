/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Modal "World Settings" dialog — the scene/environment preferences for World
mode (View / Lighting / Sky & fog / Environment / Geo). Reads and writes a
Sw2gzWorldSceneConfig.

Layout: a scrolling content panel of grouped rows, with a fixed Save / Apply /
Cancel bar docked at the bottom. Save closes (the caller persists the doc);
Apply writes the values + persists via the onApply callback but keeps the
dialog open. Plain WinForms Form (mirrors ExportDialog) — no PMP, so no
re-entrancy risk.
*/
#if SW_INTEROP
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SW2GZ.URDFExport;

namespace SW2GZ.UI
{
    public sealed class WorldSettingsDialog : Form
    {
        private readonly Sw2gzWorldSceneConfig _seed;
        private readonly Action _onApply;

        private readonly ComboBox _view;
        private readonly CheckBox _grid, _shadows, _sky, _fog, _useGeo;
        private readonly NumericUpDown _sunAz, _sunEl, _sunInt, _fogDensity;
        private readonly NumericUpDown _bgR, _bgG, _bgB;
        private readonly NumericUpDown _gravity, _windX, _windY, _windZ;
        private readonly NumericUpDown _lat, _lon, _elev, _heading;

        // Layout constants — roomy rows, labels left, editors right.
        private const int Pad = 16;
        private const int RowH = 30;
        private const int LabelW = 210;
        private const int EditL = 240;
        private const int EditW = 150;

        private Panel _content;
        private int _y;   // running Y inside the content panel while building

        public WorldSettingsDialog(Sw2gzWorldSceneConfig seed) : this(seed, null) { }

        public WorldSettingsDialog(Sw2gzWorldSceneConfig seed, Action onApply)
        {
            _seed = seed ?? new Sw2gzWorldSceneConfig();
            _onApply = onApply;

            Text = "SW2GZ — World Settings";
            ClientSize = new System.Drawing.Size(470, 600);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false; MaximizeBox = false;
            MinimumSize = new System.Drawing.Size(440, 420);

            // Bottom button bar (docked first so the Fill panel takes the rest).
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            var save   = new Button { Text = "Save",   Width = 90, Height = 28, DialogResult = DialogResult.OK };
            var apply  = new Button { Text = "Apply",  Width = 90, Height = 28 };
            var cancel = new Button { Text = "Cancel", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
            bar.Controls.Add(save); bar.Controls.Add(apply); bar.Controls.Add(cancel);
            void LayoutBar()
            {
                cancel.Left = bar.ClientSize.Width - cancel.Width - Pad; cancel.Top = 12;
                apply.Left  = cancel.Left - apply.Width - 8;            apply.Top  = 12;
                save.Left   = apply.Left - save.Width - 8;              save.Top   = 12;
            }
            bar.Resize += (s, e) => LayoutBar();
            apply.Click += (s, e) => { ApplyTo(_seed); _onApply?.Invoke(); };
            Controls.Add(bar);

            // Scrolling content area.
            _content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(Pad, Pad, Pad, Pad) };
            Controls.Add(_content);
            _content.BringToFront();

            _y = Pad;

            // View
            BeginGroup("View");
            _view = ComboRow("Initial camera", new[] { "Iso", "Top", "Front" });
            _grid = CheckRow("Show grid");
            EndGroup();
            _view.SelectedIndex = ViewIndex(_seed.InitialView);
            _grid.Checked = _seed.ShowGrid;

            // Lighting
            BeginGroup("Lighting");
            _sunAz  = NumRow("Sun azimuth (°)",   0, 360, 1,    1);
            _sunEl  = NumRow("Sun elevation (°)", 0, 90,  1,    1);
            _sunInt = NumRow("Sun intensity",     0, 3,   0.1m, 2);
            _shadows = CheckRow("Cast shadows");
            EndGroup();
            _sunAz.Value  = Clamp(_sunAz,  (decimal)_seed.SunAzimuthDeg);
            _sunEl.Value  = Clamp(_sunEl,  (decimal)_seed.SunElevationDeg);
            _sunInt.Value = Clamp(_sunInt, (decimal)_seed.SunIntensity);
            _shadows.Checked = _seed.CastShadows;

            // Sky & fog
            BeginGroup("Sky & fog");
            _sky = CheckRow("Sky");
            _fog = CheckRow("Fog");
            _fogDensity = NumRow("Fog density", 0, 1, 0.01m, 3);
            _bgR = NumRow("Background R", 0, 1, 0.05m, 3);
            _bgG = NumRow("Background G", 0, 1, 0.05m, 3);
            _bgB = NumRow("Background B", 0, 1, 0.05m, 3);
            EndGroup();
            _sky.Checked = _seed.Sky; _fog.Checked = _seed.Fog;
            _fogDensity.Value = Clamp(_fogDensity, (decimal)_seed.FogDensity);
            _bgR.Value = Clamp(_bgR, (decimal)_seed.BgR);
            _bgG.Value = Clamp(_bgG, (decimal)_seed.BgG);
            _bgB.Value = Clamp(_bgB, (decimal)_seed.BgB);

            // Environment
            BeginGroup("Environment");
            _gravity = NumRow("Gravity Z (m/s²)", -30, 0, 0.1m, 2);
            _windX = NumRow("Wind X (m/s)", -50, 50, 0.1m, 2);
            _windY = NumRow("Wind Y (m/s)", -50, 50, 0.1m, 2);
            _windZ = NumRow("Wind Z (m/s)", -50, 50, 0.1m, 2);
            EndGroup();
            _gravity.Value = Clamp(_gravity, (decimal)_seed.GravityZ);
            _windX.Value = Clamp(_windX, (decimal)_seed.WindX);
            _windY.Value = Clamp(_windY, (decimal)_seed.WindY);
            _windZ.Value = Clamp(_windZ, (decimal)_seed.WindZ);

            // Geo
            BeginGroup("Geo (spherical coordinates)");
            _useGeo = CheckRow("Use spherical coordinates");
            _lat = NumRow("Latitude (°)",  -90, 90,   0.0001m, 6);
            _lon = NumRow("Longitude (°)", -180, 180, 0.0001m, 6);
            _elev = NumRow("Elevation (m)", -500, 9000, 1, 2);
            _heading = NumRow("Heading (°)", -360, 360, 1, 2);
            EndGroup();
            _useGeo.Checked = _seed.UseGeo;
            _lat.Value = Clamp(_lat, (decimal)_seed.Latitude);
            _lon.Value = Clamp(_lon, (decimal)_seed.Longitude);
            _elev.Value = Clamp(_elev, (decimal)_seed.Elevation);
            _heading.Value = Clamp(_heading, (decimal)_seed.HeadingDeg);

            AcceptButton = save; CancelButton = cancel;
            LayoutBar();

            // Follow the Windows app theme — apply a dark palette when the user
            // is in dark mode (WinForms doesn't theme itself).
            if (IsSystemDark()) ApplyDarkTheme(save, apply, cancel);
        }

        // ── dark theme ────────────────────────────────────────────────────────
        private static readonly Color DarkBack  = Color.FromArgb(37, 37, 38);
        private static readonly Color DarkGroup = Color.FromArgb(45, 45, 48);
        private static readonly Color DarkEdit  = Color.FromArgb(51, 51, 55);
        private static readonly Color DarkBtn   = Color.FromArgb(62, 62, 66);
        private static readonly Color DarkFg    = Color.FromArgb(220, 220, 220);

        private void ApplyDarkTheme(params Button[] buttons)
        {
            BackColor = DarkBack;
            ForeColor = DarkFg;
            ThemeChildren(this);
            foreach (Button b in buttons)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 84);
                b.BackColor = DarkBtn;
                b.ForeColor = DarkFg;
            }
        }

        private void ThemeChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                switch (c)
                {
                    case GroupBox g:
                        g.BackColor = DarkGroup; g.ForeColor = DarkFg; ThemeChildren(g); break;
                    case Panel p:
                        p.BackColor = DarkBack; p.ForeColor = DarkFg; ThemeChildren(p); break;
                    case Label l:
                        l.ForeColor = DarkFg; l.BackColor = Color.Transparent; break;
                    case CheckBox ck:
                        ck.ForeColor = DarkFg; ck.BackColor = Color.Transparent; break;
                    case NumericUpDown n:
                        n.BackColor = DarkEdit; n.ForeColor = DarkFg; break;
                    case ComboBox cb:
                        cb.BackColor = DarkEdit; cb.ForeColor = DarkFg; cb.FlatStyle = FlatStyle.Flat; break;
                    case Button:
                        break;   // styled explicitly in ApplyDarkTheme
                    default:
                        c.BackColor = DarkBack; c.ForeColor = DarkFg; ThemeChildren(c); break;
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!IsSystemDark()) return;
            // Dark title bar (DWMWA_USE_IMMERSIVE_DARK_MODE = 20; 19 on older 1809).
            int on = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref on, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private static bool IsSystemDark()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (k?.GetValue("AppsUseLightTheme") is int v) return v == 0;
                }
            }
            catch { /* default to light on any access error */ }
            return false;
        }

        // Write the dialog values back into the target config.
        public void ApplyTo(Sw2gzWorldSceneConfig t)
        {
            if (t == null) return;
            t.InitialView = new[] { "iso", "top", "front" }[_view.SelectedIndex < 0 ? 0 : _view.SelectedIndex];
            t.ShowGrid = _grid.Checked;
            t.SunAzimuthDeg = (double)_sunAz.Value;
            t.SunElevationDeg = (double)_sunEl.Value;
            t.SunIntensity = (double)_sunInt.Value;
            t.CastShadows = _shadows.Checked;
            t.Sky = _sky.Checked; t.Fog = _fog.Checked;
            t.FogDensity = (double)_fogDensity.Value;
            t.BgR = (double)_bgR.Value; t.BgG = (double)_bgG.Value; t.BgB = (double)_bgB.Value;
            t.GravityZ = (double)_gravity.Value;
            t.WindX = (double)_windX.Value; t.WindY = (double)_windY.Value; t.WindZ = (double)_windZ.Value;
            t.UseGeo = _useGeo.Checked;
            t.Latitude = (double)_lat.Value; t.Longitude = (double)_lon.Value;
            t.Elevation = (double)_elev.Value; t.HeadingDeg = (double)_heading.Value;
        }

        // ── group / row builders ──────────────────────────────────────────────
        private GroupBox _g;
        private int _gy;

        private void BeginGroup(string title)
        {
            _g = new GroupBox { Text = title, Left = Pad, Top = _y, Width = ContentW(), Height = 40 };
            _content.Controls.Add(_g);
            _gy = 26;
        }

        private void EndGroup()
        {
            _g.Height = _gy + 10;
            _y += _g.Height + 12;
        }

        private int ContentW() => ClientSize.Width - 2 * Pad - SystemInformation.VerticalScrollBarWidth - 4;

        private NumericUpDown NumRow(string label, decimal min, decimal max, decimal step, int decimals)
        {
            _g.Controls.Add(new Label { Left = 12, Top = _gy + 4, Width = LabelW, Text = label });
            var n = new NumericUpDown
            {
                Left = EditL, Top = _gy, Width = EditW, Minimum = min, Maximum = max,
                Increment = step, DecimalPlaces = decimals,
            };
            _g.Controls.Add(n);
            _gy += RowH;
            return n;
        }

        private CheckBox CheckRow(string label)
        {
            var c = new CheckBox { Left = 14, Top = _gy + 2, Width = LabelW + EditW, Text = label };
            _g.Controls.Add(c);
            _gy += RowH - 4;
            return c;
        }

        private ComboBox ComboRow(string label, string[] items)
        {
            _g.Controls.Add(new Label { Left = 12, Top = _gy + 4, Width = LabelW, Text = label });
            var c = new ComboBox { Left = EditL, Top = _gy, Width = EditW, DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            _g.Controls.Add(c);
            _gy += RowH;
            return c;
        }

        private static int ViewIndex(string v)
        {
            switch ((v ?? "iso").Trim().ToLowerInvariant())
            {
                case "top": return 1;
                case "front": return 2;
                default: return 0;
            }
        }

        private static decimal Clamp(NumericUpDown n, decimal v)
            => v < n.Minimum ? n.Minimum : (v > n.Maximum ? n.Maximum : v);
    }
}
#endif
