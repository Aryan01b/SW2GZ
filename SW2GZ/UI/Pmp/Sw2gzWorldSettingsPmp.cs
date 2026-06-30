/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzWorldSettingsPmp — the "Settings" PropertyManagerPage (left dock) for World
mode: scene/environment preferences (View / Lighting / Sky & fog / Environment /
Geo) editing Sw2gzWorldSceneConfig (doc.World.Scene).

Controls live in a WinForms panel hosted via swControlType_WindowFromHandle (the
Create-wizard pattern) rather than native PMP controls, which were AV-crashing SW
on interaction. Values are read back on Okay; Cancel rolls the doc back to the
entry snapshot.
*/
#if SW_INTEROP
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2GZ.URDFExport;
using SW2GZ.Utilities;

namespace SW2GZ.UI.Pmp
{
    [ComVisible(true)]
    public sealed class Sw2gzWorldSettingsPmp : PropertyManagerPage2Handler9
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly SldWorks _swApp;
        private readonly Sw2gzDoc _liveDoc;
        private readonly Sw2gzDoc _snapshot;
        private readonly Action<Sw2gzDoc> _onCommit;
        private readonly PropertyManagerPage2 _page;
        private bool _okay;

        private static readonly string[] ViewOptions = { "iso", "top", "front" };

        private const int IdGroup = 10;
        private const int IdHost = 11;

        private PropertyManagerPageWindowFromHandle _hostHandle;
        private Panel _host;
        private ComboBox _view;
        private CheckBox _grid, _shadows, _sky, _fog, _useGeo;
        private NumericUpDown _sunAz, _sunEl, _sunInt, _fogDensity, _bgR, _bgG, _bgB;
        private NumericUpDown _gravity, _windX, _windY, _windZ, _lat, _lon, _elev, _heading;
        private NumericUpDown _friction;

        // W3 — up to two optional extra fill lights (beyond the sun).
        private const int LightSlots = 2;
        private static readonly string[] LightTypes = { "point", "spot", "directional" };
        private readonly CheckBox[] _lightOn = new CheckBox[LightSlots];
        private readonly ComboBox[] _lightType = new ComboBox[LightSlots];
        private readonly NumericUpDown[] _lightX = new NumericUpDown[LightSlots];
        private readonly NumericUpDown[] _lightY = new NumericUpDown[LightSlots];
        private readonly NumericUpDown[] _lightZ = new NumericUpDown[LightSlots];
        private readonly NumericUpDown[] _lightInt = new NumericUpDown[LightSlots];

        public Sw2gzWorldSettingsPmp(SldWorks swApp, Sw2gzDoc liveDoc, Action<Sw2gzDoc> onCommit)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _liveDoc = liveDoc ?? throw new ArgumentNullException(nameof(liveDoc));
            _onCommit = onCommit ?? (d => { });
            if (_liveDoc.World == null) _liveDoc.World = new Sw2gzWorldConfig();
            if (_liveDoc.World.Scene == null) _liveDoc.World.Scene = new Sw2gzWorldSceneConfig();
            _snapshot = Sw2gzDocSnapshot.Clone(liveDoc);

            int errs = 0;
            int opts = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                       (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;
            _page = (PropertyManagerPage2)swApp.CreatePropertyManagerPage("Settings", opts, this, ref errs);
            if (_page == null)
            {
                logger.Error("Sw2gzWorldSettingsPmp: CreatePropertyManagerPage failed (err=" + errs + ")");
                return;
            }
            BuildPage();
        }

        private void BuildPage()
        {
            int leftEdge = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int ve = (int)swAddControlOptions_e.swControlOptions_Enabled |
                     (int)swAddControlOptions_e.swControlOptions_Visible;
            int grpOptions = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                             (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;

            BuildHostPanel(_liveDoc.World.Scene);

            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdGroup, "Scene & environment", grpOptions);
            _hostHandle = (PropertyManagerPageWindowFromHandle)grp.AddControl2(IdHost,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, ve, "Scene / environment settings");
            _hostHandle.Height = _host.Height;
            _hostHandle.SetWindowHandlex64(_host.Handle.ToInt64());
        }

        private static readonly Color Bg = SystemDark() ? Color.FromArgb(53, 53, 53) : SystemColors.Control;
        private static readonly Color Fg = SystemDark() ? Color.FromArgb(220, 220, 220) : SystemColors.ControlText;
        private const int LblL = 6, LblW = 150, EdL = 162, EdW = 124, RowH = 26;

        private void BuildHostPanel(Sw2gzWorldSceneConfig s)
        {
            _host = new Panel { Width = 300, BackColor = Bg };
            int y = 4;

            Header("View", ref y);
            _view = Combo("Initial camera", new[] { "Iso", "Top", "Front" }, ViewIndex(s.InitialView), ref y);
            _grid = Check("Show grid", s.ShowGrid, ref y);

            Header("Lighting", ref y);
            _sunAz  = Num("Sun azimuth (deg)",   0, 360, 1,   1, (decimal)s.SunAzimuthDeg, ref y);
            _sunEl  = Num("Sun elevation (deg)", 0, 90,  1,   1, (decimal)s.SunElevationDeg, ref y);
            _sunInt = Num("Sun intensity",       0, 3,   0.1m, 2, (decimal)s.SunIntensity, ref y);
            _shadows = Check("Cast shadows", s.CastShadows, ref y);

            Header("Sky & fog", ref y);
            _sky = Check("Sky", s.Sky, ref y);
            _fog = Check("Fog", s.Fog, ref y);
            _fogDensity = Num("Fog density", 0, 1, 0.01m, 3, (decimal)s.FogDensity, ref y);
            _bgR = Num("Background R", 0, 1, 0.05m, 3, (decimal)s.BgR, ref y);
            _bgG = Num("Background G", 0, 1, 0.05m, 3, (decimal)s.BgG, ref y);
            _bgB = Num("Background B", 0, 1, 0.05m, 3, (decimal)s.BgB, ref y);

            Header("Environment", ref y);
            _gravity = Num("Gravity Z (m/s2)", -30, 0,  0.1m, 2, (decimal)s.GravityZ, ref y);
            _friction = Num("Ground friction μ", 0, 2, 0.05m, 2, (decimal)s.Friction, ref y);
            _windX   = Num("Wind X (m/s)",     -50, 50, 0.1m, 2, (decimal)s.WindX, ref y);
            _windY   = Num("Wind Y (m/s)",     -50, 50, 0.1m, 2, (decimal)s.WindY, ref y);
            _windZ   = Num("Wind Z (m/s)",     -50, 50, 0.1m, 2, (decimal)s.WindZ, ref y);

            Header("Lights (extra fill, beyond the sun)", ref y);
            for (int i = 0; i < LightSlots; i++)
            {
                Sw2gzLightConfig lc = (s.Lights != null && i < s.Lights.Count) ? s.Lights[i] : null;
                _lightOn[i]   = Check("Light " + (i + 1) + " enabled", lc != null, ref y);
                _lightType[i] = Combo("  Type", new[] { "Point", "Spot", "Directional" },
                                      LightTypeIndex(lc?.Type), ref y);
                _lightX[i]    = Num("  X (m)", -100, 100, 0.1m, 2, (decimal)(lc?.X ?? 0.0), ref y);
                _lightY[i]    = Num("  Y (m)", -100, 100, 0.1m, 2, (decimal)(lc?.Y ?? 0.0), ref y);
                _lightZ[i]    = Num("  Z (m)", -100, 100, 0.1m, 2, (decimal)(lc?.Z ?? 2.0), ref y);
                _lightInt[i]  = Num("  Intensity", 0, 5, 0.1m, 2, (decimal)(lc?.Intensity ?? 1.0), ref y);
            }

            Header("Geo (spherical coordinates)", ref y);
            _useGeo = Check("Use spherical coordinates", s.UseGeo, ref y);
            _lat     = Num("Latitude (deg)",  -90,  90,   0.0001m, 6, (decimal)s.Latitude, ref y);
            _lon     = Num("Longitude (deg)", -180, 180,  0.0001m, 6, (decimal)s.Longitude, ref y);
            _elev    = Num("Elevation (m)",   -500, 9000, 1,       2, (decimal)s.Elevation, ref y);
            _heading = Num("Heading (deg)",   -360, 360,  1,       2, (decimal)s.HeadingDeg, ref y);

            _host.Height = y + 6;
        }

        private void Header(string text, ref int y)
        {
            y += 6;
            _host.Controls.Add(new Label
            {
                Text = text, Left = LblL, Top = y, Width = 288, Height = 18,
                ForeColor = Color.White, Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
            });
            y += 22;
        }

        private CheckBox Check(string text, bool seed, ref int y)
        {
            var cb = new CheckBox
            {
                Text = text, Checked = seed, Left = LblL + 8, Top = y, Width = 280, Height = 22,
                ForeColor = Fg, BackColor = Bg, FlatStyle = FlatStyle.Flat,
            };
            _host.Controls.Add(cb);
            y += RowH;
            return cb;
        }

        private NumericUpDown Num(string label, decimal min, decimal max, decimal step, int dec, decimal seed, ref int y)
        {
            _host.Controls.Add(new Label { Text = label, Left = LblL, Top = y + 3, Width = LblW, Height = 18, ForeColor = Fg });
            var n = new NumericUpDown
            {
                Left = EdL, Top = y, Width = EdW, Minimum = min, Maximum = max, Increment = step, DecimalPlaces = dec,
                Value = seed < min ? min : (seed > max ? max : seed),
            };
            _host.Controls.Add(n);
            y += RowH;
            return n;
        }

        private ComboBox Combo(string label, string[] items, int idx, ref int y)
        {
            _host.Controls.Add(new Label { Text = label, Left = LblL, Top = y + 3, Width = LblW, Height = 18, ForeColor = Fg });
            var c = new ComboBox { Left = EdL, Top = y, Width = EdW, DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            c.SelectedIndex = idx < 0 || idx >= items.Length ? 0 : idx;
            _host.Controls.Add(c);
            y += RowH;
            return c;
        }

        private void CommitFromControls()
        {
            var s = _liveDoc.World.Scene;
            if (_view != null) s.InitialView = ViewOptions[_view.SelectedIndex < 0 ? 0 : _view.SelectedIndex];
            if (_grid != null)    s.ShowGrid = _grid.Checked;
            if (_sunAz != null)   s.SunAzimuthDeg = (double)_sunAz.Value;
            if (_sunEl != null)   s.SunElevationDeg = (double)_sunEl.Value;
            if (_sunInt != null)  s.SunIntensity = (double)_sunInt.Value;
            if (_shadows != null) s.CastShadows = _shadows.Checked;
            if (_sky != null)     s.Sky = _sky.Checked;
            if (_fog != null)     s.Fog = _fog.Checked;
            if (_fogDensity != null) s.FogDensity = (double)_fogDensity.Value;
            if (_bgR != null)     s.BgR = (double)_bgR.Value;
            if (_bgG != null)     s.BgG = (double)_bgG.Value;
            if (_bgB != null)     s.BgB = (double)_bgB.Value;
            if (_gravity != null) s.GravityZ = (double)_gravity.Value;
            if (_friction != null) s.Friction = (double)_friction.Value;
            if (_windX != null)   s.WindX = (double)_windX.Value;
            if (_windY != null)   s.WindY = (double)_windY.Value;
            if (_windZ != null)   s.WindZ = (double)_windZ.Value;
            if (_useGeo != null)  s.UseGeo = _useGeo.Checked;
            if (_lat != null)     s.Latitude = (double)_lat.Value;
            if (_lon != null)     s.Longitude = (double)_lon.Value;
            if (_elev != null)    s.Elevation = (double)_elev.Value;
            if (_heading != null) s.HeadingDeg = (double)_heading.Value;

            // W3 — rebuild the lights list from the enabled slots.
            var lights = new System.Collections.Generic.List<Sw2gzLightConfig>();
            for (int i = 0; i < LightSlots; i++)
            {
                if (_lightOn[i] == null || !_lightOn[i].Checked) continue;
                int ti = _lightType[i] != null && _lightType[i].SelectedIndex >= 0 ? _lightType[i].SelectedIndex : 0;
                lights.Add(new Sw2gzLightConfig
                {
                    Type = LightTypes[ti],
                    X = (double)_lightX[i].Value, Y = (double)_lightY[i].Value, Z = (double)_lightZ[i].Value,
                    Intensity = (double)_lightInt[i].Value,
                });
            }
            s.Lights = lights;
        }

        private static int LightTypeIndex(string t)
        {
            switch ((t ?? "point").Trim().ToLowerInvariant())
            {
                case "spot": return 1;
                case "directional": return 2;
                default: return 0;
            }
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

        private static bool SystemDark()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return v is int i && i == 0;
            }
            catch { return false; }
        }

        public void Show()
        {
            if (_page == null) { _swApp.SendMsgToUser("Could not open Settings."); return; }
            _page.Show2(0);
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            bool okay = Reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay;
            _okay = _okay || okay;
            if (_okay) CommitFromControls();
            else
            {
                Sw2gzDocSnapshot.Restore(_snapshot, _liveDoc);
                logger.Info("Sw2gzWorldSettingsPmp: cancel -> snapshot restored");
            }
        }

        void IPropertyManagerPage2Handler9.AfterClose() { if (_okay && _liveDoc != null) _onCommit(_liveDoc); }

        void IPropertyManagerPage2Handler9.AfterActivation() { }
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked) { }
        void IPropertyManagerPage2Handler9.OnButtonPress(int Id) { }
        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id) { }
        void IPropertyManagerPage2Handler9.OnLostFocus(int Id) { }
        bool IPropertyManagerPage2Handler9.OnHelp() => true;
        bool IPropertyManagerPage2Handler9.OnNextPage() => true;
        bool IPropertyManagerPage2Handler9.OnPreviousPage() => true;
        bool IPropertyManagerPage2Handler9.OnPreview() => true;
        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id) => true;
        bool IPropertyManagerPage2Handler9.OnKeystroke(int Wparam, int Message, int Lparam, int Id) => false;
        bool IPropertyManagerPage2Handler9.OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText) => true;
        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id) { }
        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text) { }
        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item) { }
        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item) { }
        void IPropertyManagerPage2Handler9.OnListboxRMBUp(int Id, int PosX, int PosY) { }
        void IPropertyManagerPage2Handler9.OnGroupCheck(int Id, bool Checked) { }
        void IPropertyManagerPage2Handler9.OnGroupExpand(int Id, bool Expanded) { }
        void IPropertyManagerPage2Handler9.OnOptionCheck(int Id) { }
        void IPropertyManagerPage2Handler9.OnPopupMenuItem(int Id) { }
        void IPropertyManagerPage2Handler9.OnPopupMenuItemUpdate(int Id, ref int retval) { }
        void IPropertyManagerPage2Handler9.OnSliderPositionChanged(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnSliderTrackingCompleted(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnRedo() { }
        void IPropertyManagerPage2Handler9.OnUndo() { }
        void IPropertyManagerPage2Handler9.OnWhatsNew() { }
        int IPropertyManagerPage2Handler9.OnWindowFromHandleControlCreated(int Id, bool Status) => 0;
        int IPropertyManagerPage2Handler9.OnActiveXControlCreated(int Id, bool Status) => 0;
    }
}
#endif
