/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateWorldPmp — the "Create World" PropertyManagerPage opened from the
mode-specific Create button when the active mode is World. Mirrors the
Sw2gzCreateRobotPmp chrome: a WinForms nav bar (Back/Next + step indicator,
dark theme) embedded via WindowFromHandle, and WinForms action-button bars per
step. PMP swControlType_Button controls are avoided entirely — clicking one and
mutating PMP state from inside OnButtonPress corrupts SW's PMP renderer
(buttons vanish, selection glitches). Nav clicks defer via BeginInvoke so the
group-visibility flip runs off the click-handler reentrancy frame.

Steps map to Sw2gzDoc.World:
    0 — Scene    (pick a ground component → Sw2gzWorldConfig.Ground; auto-seeds Assets)
    1 — Assets   (auto-located list, editable → Sw2gzWorldConfig.Assets)
    2 — Physics  (engine + step + RTF)
    3 — Review   (counts; Next caption flips to "Finish")
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
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
    public sealed class Sw2gzCreateWorldPmp : PropertyManagerPage2Handler9
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _modelDoc;
        private readonly Sw2gzDoc _liveDoc;
        private readonly Sw2gzDoc _snapshot;
        private readonly Action<Sw2gzDoc> _onCommit;
        private readonly Action _onClosed;
        private readonly PropertyManagerPage2 _page;

        private const int StepScene   = 0;
        private const int StepAssets  = 1;
        private const int StepPhysics = 2;
        private const int StepReview  = 3;
        private static readonly string[] StepNames = { "Scene", "Assets", "Physics", "Review" };
        private const int StepCount = 4;

        private bool _okay;
        private int _currentStep = StepScene;

        private const int GroundSelectionMark = 0x4B0;
        private const int AssetsSelectionMark = 0x4B1;

        // Header (Progress) group + nav bar.
        private const int IdHeaderGroup  = 1;
        private const int IdHeaderLabel  = 2;
        private const int IdNavBar       = 3;

        // Step groups.
        private const int IdSceneGroup   = 10;
        private const int IdSceneDescr   = 11;
        private const int IdGroundPicker = 12;
        private const int IdGroundLabel  = 14;
        private const int IdSceneBtnBar  = 16;

        private const int IdAssetsGroup  = 20;
        private const int IdAssetsDescr  = 21;
        private const int IdAssetsPicker = 22;
        private const int IdAssetsList   = 24;
        private const int IdAssetsBtnBar = 27;

        private const int IdPhysicsGroup       = 30;
        private const int IdPhysicsDescr       = 31;
        private const int IdPhysicsEngineCombo = 32;
        private const int IdPhysicsStepBox     = 33;
        private const int IdPhysicsRtfBox      = 34;

        private const int IdReviewGroup        = 40;
        private const int IdReviewDescr        = 41;
        private const int IdReviewGroundLabel  = 42;
        private const int IdReviewAssetsLabel  = 43;
        private const int IdReviewPhysicsLabel = 44;

        private PropertyManagerPageGroup[] _stepGroups;

        // WinForms nav bar (Back/Next + step indicator).
        private PropertyManagerPageWindowFromHandle _navHandle;
        private System.Windows.Forms.Panel _navBar;
        private System.Windows.Forms.Button _backBtn;
        private System.Windows.Forms.Button _nextBtn;
        private System.Windows.Forms.Label _stepIndicator;

        // WinForms per-step action bars.
        private PropertyManagerPageWindowFromHandle _sceneBarHandle;
        private System.Windows.Forms.Panel _sceneBar;
        private System.Windows.Forms.Button _setGroundBtn;
        private System.Windows.Forms.Button _clearGroundBtn;

        private PropertyManagerPageWindowFromHandle _assetsBarHandle;
        private System.Windows.Forms.Panel _assetsBar;
        private System.Windows.Forms.Button _addAssetBtn;
        private System.Windows.Forms.Button _removeAssetBtn;
        private System.Windows.Forms.Button _clearAssetsBtn;

        // PMP-native controls (selection boxes / listbox / combo / numberboxes).
        private PropertyManagerPageSelectionbox _groundPicker;
        private PropertyManagerPageLabel _groundLabel;
        private PropertyManagerPageSelectionbox _assetsPicker;
        private PropertyManagerPageListbox _assetsList;
        private PropertyManagerPageCombobox _engineCombo;
        private PropertyManagerPageNumberbox _stepBox;
        private PropertyManagerPageNumberbox _rtfBox;
        private PropertyManagerPageLabel _reviewGroundLabel;
        private PropertyManagerPageLabel _reviewAssetsLabel;
        private PropertyManagerPageLabel _reviewPhysicsLabel;

        private static readonly string[] EngineOptions = { "ode", "bullet", "dart" };

        public Sw2gzCreateWorldPmp(SldWorks swApp, ModelDoc2 modelDoc, Sw2gzDoc liveDoc, Action<Sw2gzDoc> onCommit, Action onClosed = null)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _modelDoc = modelDoc ?? throw new ArgumentNullException(nameof(modelDoc));
            _liveDoc = liveDoc ?? throw new ArgumentNullException(nameof(liveDoc));
            _onCommit = onCommit ?? (d => { });
            _onClosed = onClosed;

            _snapshot = Sw2gzDocSnapshot.Clone(liveDoc);

            int errs = 0;
            int opts = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                       (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;
            _page = (PropertyManagerPage2)swApp.CreatePropertyManagerPage(
                "Create World", opts, this, ref errs);

            if (_page == null)
            {
                logger.Error("Sw2gzCreateWorldPmp: CreatePropertyManagerPage failed (err=" + errs + ")");
                return;
            }

            BuildPage();
            ShowStep(StepScene);
        }

        // ── Dark-theme palette (mirrors Sw2gzCreateRobotPmp) ──────────────────
        private static readonly System.Drawing.Color DarkBarBg     = System.Drawing.Color.FromArgb(53, 53, 53);
        private static readonly System.Drawing.Color DarkBtnBg     = System.Drawing.Color.FromArgb(70, 70, 72);
        private static readonly System.Drawing.Color DarkBtnHover  = System.Drawing.Color.FromArgb(95, 95, 98);
        private static readonly System.Drawing.Color DarkFg        = System.Drawing.Color.FromArgb(220, 220, 220);
        private static readonly System.Drawing.Color DarkBtnBorder = System.Drawing.Color.FromArgb(100, 100, 102);

        private static System.Windows.Forms.Panel NewBar(int width, int height) =>
            new System.Windows.Forms.Panel { Width = width, Height = height, BackColor = DarkBarBg };

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
            int indent   = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            int visibleEnabled = (int)swAddControlOptions_e.swControlOptions_Enabled |
                                 (int)swAddControlOptions_e.swControlOptions_Visible;
            int grpOptions = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                             (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;

            var header = (PropertyManagerPageGroup)_page.AddGroupBox(IdHeaderGroup, "Progress", grpOptions);
            header.AddControl2(IdHeaderLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            BuildNavBar();
            _navHandle = (PropertyManagerPageWindowFromHandle)header.AddControl2(
                IdNavBar,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Step navigation");
            _navHandle.Height = 58;
            _navHandle.SetWindowHandlex64(_navBar.Handle.ToInt64());

            _stepGroups = new PropertyManagerPageGroup[StepCount];
            _stepGroups[StepScene]   = BuildSceneGroup(grpOptions, leftEdge, visibleEnabled);
            _stepGroups[StepAssets]  = BuildAssetsGroup(grpOptions, leftEdge, visibleEnabled);
            _stepGroups[StepPhysics] = BuildPhysicsGroup(grpOptions, leftEdge, visibleEnabled);
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
            _backBtn.Top = 24;
            _nextBtn.Top = 24;
            // Defer GoBack/GoNext off the click-handler reentrancy frame — the
            // group-visibility flip mutates PMP COM controls and crashes SW's
            // PMP renderer if run inside the WinForms button click stack.
            _backBtn.Click += (s, e) => _navBar.BeginInvoke((Action)(() =>
            {
                try { GoBack(); } catch (Exception ex) { logger.Error("GoBack threw", ex); }
            }));
            _nextBtn.Click += (s, e) => _navBar.BeginInvoke((Action)(() =>
            {
                try { GoNext(); } catch (Exception ex) { logger.Error("GoNext threw", ex); }
            }));
            _navBar.Controls.Add(_backBtn);
            _navBar.Controls.Add(_nextBtn);
            _navBar.Resize += (s, e) => CenterRow(_navBar, _backBtn, _nextBtn);
            CenterRow(_navBar, _backBtn, _nextBtn);
        }

        private PropertyManagerPageGroup BuildSceneGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdSceneGroup, "Scene", grpOptions);
            grp.AddControl2(IdSceneDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Pick the ground component (room / floor), then Set. Other top-level "
                + "components are auto-located as assets on the next step. No ground? "
                + "Skip this — a default flat ground plane is used.",
                (short)leftEdge, visibleEnabled, "");

            // WinForms button bar before the selectionbox (mirrors robot wizard).
            _sceneBar       = NewBar(260, 32);
            _setGroundBtn   = NewBarButton("Set ground", 90);
            _clearGroundBtn = NewBarButton("Clear", 70);
            _setGroundBtn.Click   += (s, e) => HandleSetGround();
            _clearGroundBtn.Click += (s, e) => HandleClearGround();
            _sceneBar.Controls.Add(_setGroundBtn);
            _sceneBar.Controls.Add(_clearGroundBtn);
            _sceneBar.Resize += (s, e) => CenterRow(_sceneBar, _setGroundBtn, _clearGroundBtn);
            CenterRow(_sceneBar, _setGroundBtn, _clearGroundBtn);
            _sceneBarHandle = (PropertyManagerPageWindowFromHandle)grp.AddControl2(
                IdSceneBtnBar,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Set or clear the ground component");
            _sceneBarHandle.Height = 34;
            _sceneBarHandle.SetWindowHandlex64(_sceneBar.Handle.ToInt64());

            _groundPicker = (PropertyManagerPageSelectionbox)grp.AddControl2(
                IdGroundPicker,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Ground component", (short)leftEdge, visibleEnabled, "Pick exactly one component");
            _groundPicker.SingleEntityOnly = true;
            _groundPicker.Height = 24;
            _groundPicker.Mark = GroundSelectionMark;
            _groundPicker.SetSelectionFilters((object)new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS });

            _groundLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdGroundLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");

            RefreshGroundLabel();
            return grp;
        }

        private PropertyManagerPageGroup BuildAssetsGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdAssetsGroup, "Assets", grpOptions);
            grp.AddControl2(IdAssetsDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Auto-located from the assembly. Remove any you don't want, or pick "
                + "more in the viewport and Add. All assets export as static.",
                (short)leftEdge, visibleEnabled, "");

            _assetsBar      = NewBar(260, 32);
            _addAssetBtn    = NewBarButton("Add", 60);
            _removeAssetBtn = NewBarButton("Remove", 70);
            _clearAssetsBtn = NewBarButton("Clear all", 80);
            _addAssetBtn.Click    += (s, e) => HandleAddAssets();
            _removeAssetBtn.Click += (s, e) => HandleRemoveAsset();
            _clearAssetsBtn.Click += (s, e) => HandleClearAssets();
            _assetsBar.Controls.Add(_addAssetBtn);
            _assetsBar.Controls.Add(_removeAssetBtn);
            _assetsBar.Controls.Add(_clearAssetsBtn);
            _assetsBar.Resize += (s, e) => CenterRow(_assetsBar, _addAssetBtn, _removeAssetBtn, _clearAssetsBtn);
            CenterRow(_assetsBar, _addAssetBtn, _removeAssetBtn, _clearAssetsBtn);
            _assetsBarHandle = (PropertyManagerPageWindowFromHandle)grp.AddControl2(
                IdAssetsBtnBar,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Add, remove, or clear assets");
            _assetsBarHandle.Height = 34;
            _assetsBarHandle.SetWindowHandlex64(_assetsBar.Handle.ToInt64());

            _assetsPicker = (PropertyManagerPageSelectionbox)grp.AddControl2(
                IdAssetsPicker,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Components", (short)leftEdge, visibleEnabled, "Pick components");
            _assetsPicker.SingleEntityOnly = false;
            _assetsPicker.AllowMultipleSelectOfSameEntity = false;
            _assetsPicker.Height = 30;
            _assetsPicker.Mark = AssetsSelectionMark;
            _assetsPicker.SetSelectionFilters((object)new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS });

            _assetsList = (PropertyManagerPageListbox)grp.AddControl2(
                IdAssetsList,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "Assets", (short)leftEdge, visibleEnabled, "Current world assets");
            ((IPropertyManagerPageListbox)_assetsList).Height = 110;

            RefreshAssetsList();
            return grp;
        }

        private PropertyManagerPageGroup BuildPhysicsGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdPhysicsGroup, "Physics", grpOptions);
            grp.AddControl2(IdPhysicsDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Pick the physics engine and step size for the world.",
                (short)leftEdge, visibleEnabled, "");

            _engineCombo = (PropertyManagerPageCombobox)grp.AddControl2(
                IdPhysicsEngineCombo,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "Engine", (short)leftEdge, visibleEnabled, "Physics engine");
            _engineCombo.Height = 18;
            foreach (string eng in EngineOptions) _engineCombo.AddItems(eng);
            int engIdx = Array.IndexOf(EngineOptions, _liveDoc.World.PhysicsEngine ?? "ode");
            _engineCombo.CurrentSelection = (short)(engIdx >= 0 ? engIdx : 0);

            _stepBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdPhysicsStepBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Max step (s)", (short)leftEdge, visibleEnabled, "Max simulation step");
            _stepBox.SetRange2((int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble,
                0.0001, 1.0, true, 0.001, 0.0001, 0.0001);
            _stepBox.Value = _liveDoc.World.MaxStepSize;

            _rtfBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdPhysicsRtfBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Real-time factor", (short)leftEdge, visibleEnabled, "Target real-time factor");
            _rtfBox.SetRange2((int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble,
                0.1, 10.0, true, 1.0, 0.1, 0.1);
            _rtfBox.Value = _liveDoc.World.RealTimeFactor;

            return grp;
        }

        private PropertyManagerPageGroup BuildReviewGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdReviewGroup, "Review", grpOptions);
            grp.AddControl2(IdReviewDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Review and Finish to commit. Cancel rolls back.",
                (short)leftEdge, visibleEnabled, "");
            _reviewGroundLabel  = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewGroundLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            _reviewAssetsLabel  = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewAssetsLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            _reviewPhysicsLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewPhysicsLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            return grp;
        }

        // ─── Action handlers (called directly from WinForms button clicks; they
        //     mutate the doc + listbox but never flip group visibility, so no
        //     BeginInvoke deferral is needed — only nav needs that) ────────────
        private void HandleSetGround()
        {
            try
            {
                ISelectionMgr selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
                if (selMgr == null) return;
                int count = selMgr.GetSelectedObjectCount2(GroundSelectionMark);
                if (count < 1) return;
                object selObj = selMgr.GetSelectedObject6(1, GroundSelectionMark);
                if (selObj is Component2 c && !string.IsNullOrEmpty(c.Name2))
                {
                    _liveDoc.World.Ground = c.Name2;
                    AutoSeedAssets(c.Name2);
                    RefreshGroundLabel();
                    _modelDoc.ClearSelection2(true);
                }
            }
            catch (Exception e) { logger.Warn("HandleSetGround failed", e); }
        }

        private void HandleClearGround()
        {
            _liveDoc.World.Ground = string.Empty;
            RefreshGroundLabel();
        }

        // Setting the ground auto-locates every other top-level component as a
        // world asset (the user then edits the list). Only seeds an empty list
        // so a return visit keeps prior edits.
        private void AutoSeedAssets(string groundName)
        {
            if (_liveDoc.World.Assets.Count > 0) return;
            try
            {
                object[] comps = (object[])((AssemblyDoc)_modelDoc).GetComponents(true);
                if (comps == null) return;
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (c.IsSuppressed()) continue;
                    if (string.IsNullOrEmpty(c.Name2)) continue;
                    if (c.Name2 == groundName) continue;
                    if (!_liveDoc.World.Assets.Contains(c.Name2))
                        _liveDoc.World.Assets.Add(c.Name2);
                }
                RefreshAssetsList();
            }
            catch (Exception e) { logger.Warn("AutoSeedAssets failed", e); }
        }

        private void RefreshGroundLabel()
        {
            if (_groundLabel == null) return;
            _groundLabel.Caption = string.IsNullOrEmpty(_liveDoc.World.Ground)
                ? "Ground: (not set)" : "Ground: " + _liveDoc.World.Ground;
        }

        private void HandleAddAssets()
        {
            try
            {
                ISelectionMgr selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
                if (selMgr == null) return;
                int count = selMgr.GetSelectedObjectCount2(AssetsSelectionMark);
                for (int i = 1; i <= count; i++)
                {
                    object selObj = selMgr.GetSelectedObject6(i, AssetsSelectionMark);
                    if (selObj is Component2 c && !string.IsNullOrEmpty(c.Name2)
                        && !_liveDoc.World.Assets.Contains(c.Name2))
                    {
                        _liveDoc.World.Assets.Add(c.Name2);
                    }
                }
                RefreshAssetsList();
                _modelDoc.ClearSelection2(true);
            }
            catch (Exception e) { logger.Warn("HandleAddAssets failed", e); }
        }

        private void HandleRemoveAsset()
        {
            int idx = _assetsList != null ? _assetsList.CurrentSelection : -1;
            if (idx < 0 || idx >= _liveDoc.World.Assets.Count) return;
            _liveDoc.World.Assets.RemoveAt(idx);
            RefreshAssetsList();
        }

        private void HandleClearAssets()
        {
            _liveDoc.World.Assets.Clear();
            RefreshAssetsList();
        }

        private void RefreshAssetsList()
        {
            if (_assetsList == null) return;
            _assetsList.Clear();
            foreach (string name in _liveDoc.World.Assets) _assetsList.AddItems(name);
            if (_liveDoc.World.Assets.Count > 0) _assetsList.CurrentSelection = 0;
        }

        private void CommitPhysicsFromControls()
        {
            if (_engineCombo != null)
            {
                int idx = _engineCombo.CurrentSelection;
                if (idx >= 0 && idx < EngineOptions.Length) _liveDoc.World.PhysicsEngine = EngineOptions[idx];
                else _liveDoc.World.PhysicsEngine = _engineCombo.EditText ?? _liveDoc.World.PhysicsEngine;
            }
            if (_stepBox != null) _liveDoc.World.MaxStepSize = _stepBox.Value;
            if (_rtfBox != null)  _liveDoc.World.RealTimeFactor = _rtfBox.Value;
        }

        private void RefreshReviewLabels()
        {
            CommitPhysicsFromControls();
            if (_reviewGroundLabel != null)
                _reviewGroundLabel.Caption = "Ground: " +
                    (string.IsNullOrEmpty(_liveDoc.World.Ground) ? "(default ground plane)" : _liveDoc.World.Ground);
            if (_reviewAssetsLabel != null)
                _reviewAssetsLabel.Caption = "Assets: " + _liveDoc.World.Assets.Count;
            if (_reviewPhysicsLabel != null)
                _reviewPhysicsLabel.Caption = "Physics: " + _liveDoc.World.PhysicsEngine +
                    "  step=" + _liveDoc.World.MaxStepSize + "s  rtf=" + _liveDoc.World.RealTimeFactor;
        }

        // ─── Navigation ──────────────────────────────────────────────────────
        private void ShowStep(int step)
        {
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;

            // Leaving Physics: persist current control values so Back/Next round-trips.
            if (_currentStep == StepPhysics && step != StepPhysics) CommitPhysicsFromControls();

            _currentStep = step;
            for (int i = 0; i < StepCount; i++)
            {
                try { _stepGroups[i].Visible = (i == _currentStep); }
                catch (Exception ex) { logger.Error("World ShowStep group[" + i + "].Visible failed", ex); }
            }

            // Step text rides the WinForms label (no PMP COM caption mutation
            // from the deferred-click stack → no mscorlib AV).
            try { _stepIndicator.Text = "Step " + (_currentStep + 1) + " of " + StepCount + " — " + StepNames[_currentStep]; }
            catch (Exception ex) { logger.Error("World ShowStep _stepIndicator.Text threw", ex); }
            try { _backBtn.Enabled = _currentStep > 0; } catch (Exception ex) { logger.Error("World ShowStep back threw", ex); }
            try
            {
                bool lastStep = _currentStep == StepCount - 1;
                _nextBtn.Text  = lastStep ? "Finish" : "▶";
                _nextBtn.Width = lastStep ? 80 : 50;
                CenterRow(_navBar, _backBtn, _nextBtn);
            }
            catch (Exception ex) { logger.Error("World ShowStep next threw", ex); }

            if (_currentStep == StepReview) RefreshReviewLabels();
        }

        private void GoBack()
        {
            if (_currentStep > 0) ShowStep(_currentStep - 1);
        }

        private void GoNext()
        {
            if (_currentStep < StepCount - 1)
            {
                ShowStep(_currentStep + 1);
            }
            else
            {
                CommitPhysicsFromControls();
                _okay = true;
                _page.Close(true);
            }
        }

        public void Show()
        {
            if (_page == null) { _swApp.SendMsgToUser("Could not open Create World."); return; }
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
                logger.Info("Sw2gzCreateWorldPmp: cancel → snapshot restored");
            }
            else
            {
                CommitPhysicsFromControls();
            }
        }

        void IPropertyManagerPage2Handler9.AfterClose() { if (_okay && _liveDoc != null) _onCommit(_liveDoc); _onClosed?.Invoke(); }

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
