/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzWorldSensorsPmp — the "Sensors" PropertyManagerPage (left dock) for World
mode. The world does NOT place individual sensors; it toggles the world-level Gz
system/GUI plugins spawned models need (sensor families + keyboard teleop). Each
checkbox maps to one flag on Sw2gzWorldSensorsConfig (doc.World.SensorPlugins).

The checkboxes live in a WinForms panel hosted via swControlType_WindowFromHandle
— the same pattern the Create wizards use. Native PMP checkboxes were AV-crashing
SW on toggle; WinForms controls never enter SW's native-control event path, so
there's no re-entrancy. Values are read back on Okay; Cancel rolls the doc back.
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
    public sealed class Sw2gzWorldSensorsPmp : PropertyManagerPage2Handler9
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly SldWorks _swApp;
        private readonly Sw2gzDoc _liveDoc;
        private readonly Sw2gzDoc _snapshot;
        private readonly Action<Sw2gzDoc> _onCommit;
        private readonly PropertyManagerPage2 _page;
        private bool _okay;

        private const int IdGroup = 10;
        private const int IdHost = 11;

        private PropertyManagerPageWindowFromHandle _hostHandle;
        private Panel _host;
        private CheckBox _cbSensors, _cbImu, _cbContact, _cbForce, _cbNavsat;
        private CheckBox _cbUserCmds, _cbSceneBcast, _cbKeyPub, _cbTriggered;

        public Sw2gzWorldSensorsPmp(SldWorks swApp, Sw2gzDoc liveDoc, Action<Sw2gzDoc> onCommit)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _liveDoc = liveDoc ?? throw new ArgumentNullException(nameof(liveDoc));
            _onCommit = onCommit ?? (d => { });
            if (_liveDoc.World == null) _liveDoc.World = new Sw2gzWorldConfig();
            if (_liveDoc.World.SensorPlugins == null) _liveDoc.World.SensorPlugins = new Sw2gzWorldSensorsConfig();
            _snapshot = Sw2gzDocSnapshot.Clone(liveDoc);

            int errs = 0;
            int opts = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                       (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;
            _page = (PropertyManagerPage2)swApp.CreatePropertyManagerPage("Sensors", opts, this, ref errs);
            if (_page == null)
            {
                logger.Error("Sw2gzWorldSensorsPmp: CreatePropertyManagerPage failed (err=" + errs + ")");
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
            var sp = _liveDoc.World.SensorPlugins;

            BuildHostPanel(sp);

            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdGroup, "Plugins", grpOptions);
            _hostHandle = (PropertyManagerPageWindowFromHandle)grp.AddControl2(IdHost,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, ve, "World support plugins");
            _hostHandle.Height = _host.Height;
            _hostHandle.SetWindowHandlex64(_host.Handle.ToInt64());
        }

        // ── WinForms panel of checkboxes (dark to match SW's PMP) ──────────────
        private static readonly Color Bg = SystemDark() ? Color.FromArgb(53, 53, 53) : SystemColors.Control;
        private static readonly Color Fg = SystemDark() ? Color.FromArgb(220, 220, 220) : SystemColors.ControlText;

        private void BuildHostPanel(Sw2gzWorldSensorsConfig sp)
        {
            _host = new Panel { Width = 300, BackColor = Bg };
            int y = 4;
            Header("Sensor systems — what a spawned robot's sensors need to run", ref y);
            _cbSensors = Cb("Cameras / lidar / depth (sensors-system)", sp.Sensors, ref y);
            _cbImu     = Cb("IMU (imu-system)", sp.Imu, ref y);
            _cbContact = Cb("Contact (contact-system)", sp.Contact, ref y);
            _cbForce   = Cb("Force / torque (forcetorque-system)", sp.ForceTorque, ref y);
            _cbNavsat  = Cb("NavSat / GPS (navsat-system)", sp.Navsat, ref y);
            Header("Runtime — spawn models + stream scene (leave on)", ref y);
            _cbUserCmds   = Cb("Spawn / delete models (user-commands-system)", sp.UserCommands, ref y);
            _cbSceneBcast = Cb("Broadcast scene (scene-broadcaster-system)", sp.SceneBroadcaster, ref y);
            Header("Keyboard teleop — drive a spawned robot", ref y);
            _cbKeyPub    = Cb("KeyPublisher (publish keystrokes)", sp.KeyPublisher, ref y);
            _cbTriggered = Cb("Arrow keys -> /cmd_vel (triggered-publisher)", sp.TriggeredPublisher, ref y);
            _host.Height = y + 6;
        }

        private void Header(string text, ref int y)
        {
            y += 6;
            _host.Controls.Add(new Label
            {
                Text = text, Left = 6, Top = y, Width = 288, Height = 18,
                ForeColor = Color.White, Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
            });
            y += 22;
        }

        private CheckBox Cb(string text, bool seed, ref int y)
        {
            var cb = new CheckBox
            {
                Text = text, Checked = seed, Left = 14, Top = y, Width = 284, Height = 22,
                ForeColor = Fg, BackColor = Bg, FlatStyle = FlatStyle.Flat,
            };
            _host.Controls.Add(cb);
            y += 24;
            return cb;
        }

        private void CommitFromControls()
        {
            var sp = _liveDoc.World.SensorPlugins;
            if (_cbSensors != null)    sp.Sensors            = _cbSensors.Checked;
            if (_cbImu != null)        sp.Imu                = _cbImu.Checked;
            if (_cbContact != null)    sp.Contact            = _cbContact.Checked;
            if (_cbForce != null)      sp.ForceTorque        = _cbForce.Checked;
            if (_cbNavsat != null)     sp.Navsat             = _cbNavsat.Checked;
            if (_cbUserCmds != null)   sp.UserCommands       = _cbUserCmds.Checked;
            if (_cbSceneBcast != null) sp.SceneBroadcaster   = _cbSceneBcast.Checked;
            if (_cbKeyPub != null)     sp.KeyPublisher       = _cbKeyPub.Checked;
            if (_cbTriggered != null)  sp.TriggeredPublisher = _cbTriggered.Checked;
        }

        // Cheap dark-mode probe (registry); mirrors the deleted Sw2gzDarkTheme.
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
            if (_page == null) { _swApp.SendMsgToUser("Could not open Sensors."); return; }
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
                logger.Info("Sw2gzWorldSensorsPmp: cancel -> snapshot restored");
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
