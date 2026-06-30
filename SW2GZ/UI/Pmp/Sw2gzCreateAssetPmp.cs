/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateAssetPmp — the "Create Asset" PropertyManagerPage: export one part
as a reusable Gz model. Same WinForms nav-bar chrome as Sw2gzCreateWorldPmp /
Sw2gzCreateRobotPmp (no PMP swControlType_Button → no OnButtonPress re-entrancy
glitches). Maps to Sw2gzDoc.Asset.

Steps:
    0 — Part     (pick the part component → Sw2gzAssetConfig.BodyPart)
    1 — Surface  (static checkbox + friction μ)
    2 — Review   (Finish persists Sw2gzDoc)
*/
#if SW_INTEROP
using System;
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
    public sealed class Sw2gzCreateAssetPmp : PropertyManagerPage2Handler9
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _modelDoc;
        private readonly Sw2gzDoc _liveDoc;
        private readonly Sw2gzDoc _snapshot;
        private readonly Action<Sw2gzDoc> _onCommit;
        private readonly PropertyManagerPage2 _page;

        private const int StepPart    = 0;
        private const int StepSurface = 1;
        private const int StepReview  = 2;
        private static readonly string[] StepNames = { "Part", "Surface", "Review" };
        private const int StepCount = 3;

        private bool _okay;
        private int _currentStep = StepPart;

        // Whole-part mode: a standalone part document has no components to pick —
        // the part IS the body, so the Part step becomes an info label.
        private readonly bool _wholePart;

        private const int BodySelectionMark = 0x4C0;

        private const int IdHeaderGroup = 1;
        private const int IdHeaderLabel = 2;
        private const int IdNavBar      = 3;

        private const int IdPartGroup   = 10;
        private const int IdPartDescr   = 11;
        private const int IdPartBtnBar  = 12;
        private const int IdBodyPicker  = 13;
        private const int IdBodyLabel   = 14;

        private const int IdSurfaceGroup   = 20;
        private const int IdSurfaceDescr   = 21;
        private const int IdStaticCheck    = 22;
        private const int IdFrictionBox    = 23;
        private const int IdCollisionCombo = 24;
        private const int IdJointTypeCombo = 25;
        private const int IdJointAxisCombo = 26;
        private const int IdJointLowerBox  = 27;
        private const int IdJointUpperBox  = 28;
        private const int IdSensorKindCombo = 29;
        private const int IdSensorTopicText = 36;

        // Combobox value maps (index ↔ persisted string). Order must match the
        // AddItems order below; the commit reads CurrentSelection as the index.
        private static readonly string[] CollisionValues = { "mesh", "box", "sphere", "cylinder" };
        private static readonly string[] JointValues = { "none", "fixed", "revolute", "continuous", "prismatic" };
        private static readonly string[] AxisValues = { "x", "y", "z" };
        private static readonly string[] SensorValues = { "none", "camera", "gpu_lidar", "imu" };

        private const int IdReviewGroup  = 30;
        private const int IdReviewDescr  = 31;
        private const int IdReviewBody   = 32;
        private const int IdReviewSurf   = 33;

        private PropertyManagerPageGroup[] _stepGroups;

        private PropertyManagerPageWindowFromHandle _navHandle;
        private System.Windows.Forms.Panel _navBar;
        private System.Windows.Forms.Button _backBtn;
        private System.Windows.Forms.Button _nextBtn;
        private System.Windows.Forms.Label _stepIndicator;

        private PropertyManagerPageWindowFromHandle _partBarHandle;
        private System.Windows.Forms.Panel _partBar;
        private System.Windows.Forms.Button _setBodyBtn;
        private System.Windows.Forms.Button _clearBodyBtn;

        private PropertyManagerPageSelectionbox _bodyPicker;
        private PropertyManagerPageLabel _bodyLabel;
        private PropertyManagerPageCheckbox _staticCheck;
        private PropertyManagerPageNumberbox _frictionBox;
        private PropertyManagerPageCombobox _collisionCombo;
        private PropertyManagerPageCombobox _jointTypeCombo;
        private PropertyManagerPageCombobox _jointAxisCombo;
        private PropertyManagerPageNumberbox _jointLowerBox;
        private PropertyManagerPageNumberbox _jointUpperBox;
        private PropertyManagerPageCombobox _sensorKindCombo;
        private PropertyManagerPageTextbox _sensorTopicText;
        private PropertyManagerPageLabel _reviewBodyLabel;
        private PropertyManagerPageLabel _reviewSurfLabel;

        public Sw2gzCreateAssetPmp(SldWorks swApp, ModelDoc2 modelDoc, Sw2gzDoc liveDoc,
            Action<Sw2gzDoc> onCommit, string wholePartName = null)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _modelDoc = modelDoc ?? throw new ArgumentNullException(nameof(modelDoc));
            _liveDoc = liveDoc ?? throw new ArgumentNullException(nameof(liveDoc));
            _onCommit = onCommit ?? (d => { });

            // Standalone part doc: there's nothing to pick — the whole part is
            // the asset. Preset the body so the Part step is informational.
            if (!string.IsNullOrWhiteSpace(wholePartName))
            {
                _wholePart = true;
                _liveDoc.Asset.BodyPart = wholePartName;
            }

            _snapshot = Sw2gzDocSnapshot.Clone(liveDoc);

            int errs = 0;
            int opts = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                       (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;
            _page = (PropertyManagerPage2)swApp.CreatePropertyManagerPage("Create Asset", opts, this, ref errs);
            if (_page == null)
            {
                logger.Error("Sw2gzCreateAssetPmp: CreatePropertyManagerPage failed (err=" + errs + ")");
                return;
            }
            BuildPage();
            ShowStep(StepPart);
        }

        private static readonly System.Drawing.Color DarkBarBg     = System.Drawing.Color.FromArgb(53, 53, 53);
        private static readonly System.Drawing.Color DarkBtnBg     = System.Drawing.Color.FromArgb(70, 70, 72);
        private static readonly System.Drawing.Color DarkBtnHover  = System.Drawing.Color.FromArgb(95, 95, 98);
        private static readonly System.Drawing.Color DarkFg        = System.Drawing.Color.FromArgb(220, 220, 220);
        private static readonly System.Drawing.Color DarkBtnBorder = System.Drawing.Color.FromArgb(100, 100, 102);

        private static System.Windows.Forms.Panel NewBar(int w, int h) =>
            new System.Windows.Forms.Panel { Width = w, Height = h, BackColor = DarkBarBg };

        private static System.Windows.Forms.Button NewBarButton(string text, int width)
        {
            var b = new System.Windows.Forms.Button
            {
                Text = text, Width = width, Height = 26, Top = 3,
                FlatStyle = System.Windows.Forms.FlatStyle.Flat,
                BackColor = DarkBtnBg, ForeColor = DarkFg, UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderColor = DarkBtnBorder;
            b.FlatAppearance.MouseOverBackColor = DarkBtnHover;
            return b;
        }

        private static void CenterRow(System.Windows.Forms.Panel bar, params System.Windows.Forms.Button[] btns)
        {
            const int gap = 8;
            int total = -gap;
            foreach (var b in btns) total += b.Width + gap;
            int x = System.Math.Max(0, (bar.Width - total) / 2);
            foreach (var b in btns) { b.Left = x; x += b.Width + gap; }
        }

        private void BuildPage()
        {
            int leftEdge = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int visibleEnabled = (int)swAddControlOptions_e.swControlOptions_Enabled |
                                 (int)swAddControlOptions_e.swControlOptions_Visible;
            int grpOptions = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                             (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;

            var header = (PropertyManagerPageGroup)_page.AddGroupBox(IdHeaderGroup, "Progress", grpOptions);
            header.AddControl2(IdHeaderLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label, "",
                (short)leftEdge, visibleEnabled, "");
            BuildNavBar();
            _navHandle = (PropertyManagerPageWindowFromHandle)header.AddControl2(
                IdNavBar, (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Step navigation");
            _navHandle.Height = 58;
            _navHandle.SetWindowHandlex64(_navBar.Handle.ToInt64());

            _stepGroups = new PropertyManagerPageGroup[StepCount];
            _stepGroups[StepPart]    = BuildPartGroup(grpOptions, leftEdge, visibleEnabled);
            _stepGroups[StepSurface] = BuildSurfaceGroup(grpOptions, leftEdge, visibleEnabled);
            _stepGroups[StepReview]  = BuildReviewGroup(grpOptions, leftEdge, visibleEnabled);
        }

        private void BuildNavBar()
        {
            _navBar = NewBar(260, 56);
            _stepIndicator = new System.Windows.Forms.Label
            {
                AutoSize = false, Width = 240, Height = 18, Top = 2, Left = 10,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(48, 48, 48),
                Font = new System.Drawing.Font("Segoe UI", 8.25f, System.Drawing.FontStyle.Bold),
                Text = "",
            };
            _navBar.Controls.Add(_stepIndicator);
            _backBtn = NewBarButton("◀", 50);
            _nextBtn = NewBarButton("▶", 50);
            _backBtn.Top = 24; _nextBtn.Top = 24;
            _backBtn.Click += (s, e) => _navBar.BeginInvoke((Action)(() =>
            { try { GoBack(); } catch (Exception ex) { logger.Error("GoBack threw", ex); } }));
            _nextBtn.Click += (s, e) => _navBar.BeginInvoke((Action)(() =>
            { try { GoNext(); } catch (Exception ex) { logger.Error("GoNext threw", ex); } }));
            _navBar.Controls.Add(_backBtn);
            _navBar.Controls.Add(_nextBtn);
            _navBar.Resize += (s, e) => CenterRow(_navBar, _backBtn, _nextBtn);
            CenterRow(_navBar, _backBtn, _nextBtn);
        }

        private PropertyManagerPageGroup BuildPartGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdPartGroup, "Part", grpOptions);

            // Whole-part mode (part document): no picker — just confirm the part.
            if (_wholePart)
            {
                grp.AddControl2(IdPartDescr,
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    "This part is exported as-is, with its material colour.",
                    (short)leftEdge, visibleEnabled, "");
                _bodyLabel = (PropertyManagerPageLabel)grp.AddControl2(
                    IdBodyLabel, (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    "", (short)leftEdge, visibleEnabled, "");
                RefreshBodyLabel();
                return grp;
            }

            grp.AddControl2(IdPartDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Pick the part (or sub-assembly) to export as a reusable Gz model, then Set.",
                (short)leftEdge, visibleEnabled, "");

            _partBar      = NewBar(260, 32);
            _setBodyBtn   = NewBarButton("Set body", 90);
            _clearBodyBtn = NewBarButton("Clear", 70);
            _setBodyBtn.Click   += (s, e) => HandleSetBody();
            _clearBodyBtn.Click += (s, e) => HandleClearBody();
            _partBar.Controls.Add(_setBodyBtn);
            _partBar.Controls.Add(_clearBodyBtn);
            _partBar.Resize += (s, e) => CenterRow(_partBar, _setBodyBtn, _clearBodyBtn);
            CenterRow(_partBar, _setBodyBtn, _clearBodyBtn);
            _partBarHandle = (PropertyManagerPageWindowFromHandle)grp.AddControl2(
                IdPartBtnBar, (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Set or clear the asset part");
            _partBarHandle.Height = 34;
            _partBarHandle.SetWindowHandlex64(_partBar.Handle.ToInt64());

            _bodyPicker = (PropertyManagerPageSelectionbox)grp.AddControl2(
                IdBodyPicker, (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Part component", (short)leftEdge, visibleEnabled, "Pick exactly one component");
            _bodyPicker.SingleEntityOnly = true;
            _bodyPicker.Height = 24;
            _bodyPicker.Mark = BodySelectionMark;
            _bodyPicker.SetSelectionFilters((object)new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS });

            _bodyLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdBodyLabel, (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            RefreshBodyLabel();
            return grp;
        }

        private PropertyManagerPageGroup BuildSurfaceGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdSurfaceGroup, "Surface", grpOptions);
            grp.AddControl2(IdSurfaceDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Static = fixed prop (no physics). Friction applies to contacts.",
                (short)leftEdge, visibleEnabled, "");

            _staticCheck = (PropertyManagerPageCheckbox)grp.AddControl2(
                IdStaticCheck, (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Static (fixed in the world)", (short)leftEdge, visibleEnabled, "Export as a static model");
            _staticCheck.Checked = _liveDoc.Asset.IsStatic;

            _frictionBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdFrictionBox, (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Friction μ", (short)leftEdge, visibleEnabled, "Coulomb friction coefficient");
            _frictionBox.SetRange2((int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble,
                0.0, 2.0, true, 0.8, 0.05, 0.05);
            _frictionBox.Value = _liveDoc.Asset.FrictionMu;

            // A3 — collision geometry (mesh = exact; primitive = cheaper).
            _collisionCombo = NewCombo(grp, IdCollisionCombo, "Collision", leftEdge, visibleEnabled,
                CollisionValues, _liveDoc.Asset.Collision, "Collision shape (visual stays the mesh)");

            // A1 — 1-DOF joint to the world (door/lift/wheel/lever). "none" = plain.
            _jointTypeCombo = NewCombo(grp, IdJointTypeCombo, "Joint", leftEdge, visibleEnabled,
                JointValues, _liveDoc.Asset.JointType, "Anchor the asset to the world via one joint");
            _jointAxisCombo = NewCombo(grp, IdJointAxisCombo, "Joint axis", leftEdge, visibleEnabled,
                AxisValues, AxisFromVec(_liveDoc.Asset), "Axis of motion (revolute/prismatic)");

            _jointLowerBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdJointLowerBox, (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Joint lower", (short)leftEdge, visibleEnabled, "Lower limit (rad or m)");
            _jointLowerBox.SetRange2((int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble,
                -100.0, 100.0, true, -1.5708, 0.1, 0.1);
            _jointLowerBox.Value = _liveDoc.Asset.JointLower;

            _jointUpperBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdJointUpperBox, (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Joint upper", (short)leftEdge, visibleEnabled, "Upper limit (rad or m)");
            _jointUpperBox.SetRange2((int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble,
                -100.0, 100.0, true, 1.5708, 0.1, 0.1);
            _jointUpperBox.Value = _liveDoc.Asset.JointUpper;

            // A2 — optional sensor mounted on the asset link.
            _sensorKindCombo = NewCombo(grp, IdSensorKindCombo, "Sensor", leftEdge, visibleEnabled,
                SensorValues, _liveDoc.Asset.SensorKind, "Mount a sensor on the asset link");
            _sensorTopicText = (PropertyManagerPageTextbox)grp.AddControl2(
                IdSensorTopicText, (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "Sensor topic", (short)leftEdge, visibleEnabled, "ROS/Gz topic for the sensor");
            _sensorTopicText.Text = _liveDoc.Asset.SensorTopic ?? "/asset/sensor";
            return grp;
        }

        // Build a read-on-commit dropdown combobox seeded to `current`.
        private PropertyManagerPageCombobox NewCombo(
            PropertyManagerPageGroup grp, int id, string caption, int leftEdge, int visibleEnabled,
            string[] values, string current, string tip)
        {
            var combo = (PropertyManagerPageCombobox)grp.AddControl2(
                id, (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                caption, (short)leftEdge, visibleEnabled, tip);
            combo.Height = 14;
            combo.Style = (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            foreach (string item in values) combo.AddItems(item);
            combo.CurrentSelection = (short)System.Math.Max(0, IndexOf(values, current));
            return combo;
        }

        private static int IndexOf(string[] values, string v)
        {
            string s = string.IsNullOrWhiteSpace(v) ? "" : v.Trim().ToLowerInvariant();
            for (int i = 0; i < values.Length; i++) if (values[i] == s) return i;
            return 0;
        }

        // Map the stored axis vector → an X/Y/Z combo index (defaults to Z).
        private static string AxisFromVec(Sw2gzAssetConfig a)
        {
            if (a.JointAxisX != 0) return "x";
            if (a.JointAxisY != 0) return "y";
            return "z";
        }

        private PropertyManagerPageGroup BuildReviewGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdReviewGroup, "Review", grpOptions);
            grp.AddControl2(IdReviewDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Finish to commit. Cancel rolls back.",
                (short)leftEdge, visibleEnabled, "");
            _reviewBodyLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewBody, (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            _reviewSurfLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewSurf, (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            return grp;
        }

        private void HandleSetBody()
        {
            try
            {
                ISelectionMgr selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
                if (selMgr == null) return;
                if (selMgr.GetSelectedObjectCount2(BodySelectionMark) < 1) return;
                object selObj = selMgr.GetSelectedObject6(1, BodySelectionMark);
                if (selObj is Component2 c && !string.IsNullOrEmpty(c.Name2))
                {
                    _liveDoc.Asset.BodyPart = c.Name2;
                    RefreshBodyLabel();
                    _modelDoc.ClearSelection2(true);
                }
            }
            catch (Exception e) { logger.Warn("HandleSetBody failed", e); }
        }

        private void HandleClearBody()
        {
            _liveDoc.Asset.BodyPart = string.Empty;
            RefreshBodyLabel();
        }

        private void RefreshBodyLabel()
        {
            if (_bodyLabel == null) return;
            _bodyLabel.Caption = string.IsNullOrEmpty(_liveDoc.Asset.BodyPart)
                ? "Part: (not set)" : "Part: " + _liveDoc.Asset.BodyPart;
        }

        private void CommitSurfaceFromControls()
        {
            var a = _liveDoc.Asset;
            if (_staticCheck != null) a.IsStatic = _staticCheck.Checked;
            if (_frictionBox != null) a.FrictionMu = _frictionBox.Value;
            if (_collisionCombo != null) a.Collision = Pick(CollisionValues, _collisionCombo.CurrentSelection);
            if (_jointTypeCombo != null) a.JointType = Pick(JointValues, _jointTypeCombo.CurrentSelection);
            if (_jointAxisCombo != null)
            {
                string ax = Pick(AxisValues, _jointAxisCombo.CurrentSelection);
                a.JointAxisX = ax == "x" ? 1 : 0;
                a.JointAxisY = ax == "y" ? 1 : 0;
                a.JointAxisZ = ax == "z" ? 1 : 0;
            }
            if (_jointLowerBox != null) a.JointLower = _jointLowerBox.Value;
            if (_jointUpperBox != null) a.JointUpper = _jointUpperBox.Value;
            if (_sensorKindCombo != null) a.SensorKind = Pick(SensorValues, _sensorKindCombo.CurrentSelection);
            if (_sensorTopicText != null && !string.IsNullOrWhiteSpace(_sensorTopicText.Text))
                a.SensorTopic = _sensorTopicText.Text.Trim();
        }

        private static string Pick(string[] values, short idx) =>
            (idx >= 0 && idx < values.Length) ? values[idx] : values[0];

        private void RefreshReviewLabels()
        {
            CommitSurfaceFromControls();
            if (_reviewBodyLabel != null)
                _reviewBodyLabel.Caption = "Part: " +
                    (string.IsNullOrEmpty(_liveDoc.Asset.BodyPart) ? "(not set)" : _liveDoc.Asset.BodyPart);
            if (_reviewSurfLabel != null)
            {
                var a = _liveDoc.Asset;
                bool dyn = !a.IsStatic || (a.JointType != null && a.JointType != "none");
                string s = (dyn ? "Dynamic" : "Static") + "  μ=" + a.FrictionMu + "  col=" + a.Collision;
                if (a.JointType != null && a.JointType != "none") s += "  joint=" + a.JointType;
                if (a.SensorKind != null && a.SensorKind != "none") s += "  sensor=" + a.SensorKind;
                _reviewSurfLabel.Caption = s;
            }
        }

        private void ShowStep(int step)
        {
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;
            if (_currentStep == StepSurface && step != StepSurface) CommitSurfaceFromControls();

            _currentStep = step;
            for (int i = 0; i < StepCount; i++)
            {
                try { _stepGroups[i].Visible = (i == _currentStep); }
                catch (Exception ex) { logger.Error("Asset ShowStep group[" + i + "] failed", ex); }
            }
            try { _stepIndicator.Text = "Step " + (_currentStep + 1) + " of " + StepCount + " — " + StepNames[_currentStep]; }
            catch (Exception ex) { logger.Error("Asset ShowStep indicator threw", ex); }
            try { _backBtn.Enabled = _currentStep > 0; } catch (Exception ex) { logger.Error("Asset back threw", ex); }
            try
            {
                bool lastStep = _currentStep == StepCount - 1;
                _nextBtn.Text  = lastStep ? "Finish" : "▶";
                _nextBtn.Width = lastStep ? 80 : 50;
                CenterRow(_navBar, _backBtn, _nextBtn);
            }
            catch (Exception ex) { logger.Error("Asset next threw", ex); }

            if (_currentStep == StepReview) RefreshReviewLabels();
        }

        private void GoBack() { if (_currentStep > 0) ShowStep(_currentStep - 1); }

        private void GoNext()
        {
            if (_currentStep < StepCount - 1) ShowStep(_currentStep + 1);
            else { CommitSurfaceFromControls(); _okay = true; _page.Close(true); }
        }

        public void Show()
        {
            if (_page == null) { _swApp.SendMsgToUser("Could not open Create Asset."); return; }
            _page.Show2(0);
        }

        void IPropertyManagerPage2Handler9.AfterActivation() { ShowStep(_currentStep); }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            bool okay = Reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay;
            _okay = _okay || okay;
            if (!_okay)
            {
                Sw2gzDocSnapshot.Restore(_snapshot, _liveDoc);
                logger.Info("Sw2gzCreateAssetPmp: cancel → snapshot restored");
            }
            else { CommitSurfaceFromControls(); }
        }

        void IPropertyManagerPage2Handler9.AfterClose() { if (_okay && _liveDoc != null) _onCommit(_liveDoc); }

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
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked) { }
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
