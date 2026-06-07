/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateWorldPmp — the "Create World" PropertyManagerPage opened from the
mode-specific Create button when the active mode is World. Mirrors the
Sw2gzCreateRobotPmp pattern (multi-step linear wizard, group-show/hide nav,
Back/Next footer) but maps to Sw2gzDoc.World fields:

Steps:
    0 — Scene    (pick a ground component → Sw2gzWorldConfig.Ground)
    1 — Assets   (pick extra components → Sw2gzWorldConfig.Assets)
    2 — Physics  (engine + step + RTF — defaults stay if user skips)
    3 — Review   (counts; Next caption flips to "Finish")

Schema is flat strings/numbers (no rich World/Asset model in v2.1.0). Pipeline
still reads the legacy Sw2gzExportConfig attribute until backend wiring lands.
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        private readonly PropertyManagerPage2 _page;

        private const int StepScene   = 0;
        private const int StepAssets  = 1;
        private const int StepPhysics = 2;
        private const int StepReview  = 3;
        private static readonly string[] StepNames = { "Scene", "Assets", "Physics", "Review" };
        private const int StepCount = 4;

        private int _currentStep = StepScene;

        private const int GroundSelectionMark = 0x4B0;
        private const int AssetsSelectionMark = 0x4B1;

        private const int IdHeader = 1;
        private const int IdFooter = 2;
        private const int IdBackBtn = 3;
        private const int IdNextBtn = 4;

        // Scene step
        private const int IdSceneGroup     = 10;
        private const int IdSceneDescr     = 11;
        private const int IdGroundPicker   = 12;
        private const int IdGroundSetBtn   = 13;
        private const int IdGroundLabel    = 14;
        private const int IdGroundClearBtn = 15;

        // Assets step
        private const int IdAssetsGroup     = 20;
        private const int IdAssetsDescr     = 21;
        private const int IdAssetsPicker    = 22;
        private const int IdAssetsAddBtn    = 23;
        private const int IdAssetsList      = 24;
        private const int IdAssetsRemoveBtn = 25;
        private const int IdAssetsClearBtn  = 26;

        // Physics step
        private const int IdPhysicsGroup        = 30;
        private const int IdPhysicsDescr        = 31;
        private const int IdPhysicsEngineCombo  = 32;
        private const int IdPhysicsStepBox      = 33;
        private const int IdPhysicsRtfBox       = 34;

        // Review step
        private const int IdReviewGroup        = 40;
        private const int IdReviewDescr        = 41;
        private const int IdReviewGroundLabel  = 42;
        private const int IdReviewAssetsLabel  = 43;
        private const int IdReviewPhysicsLabel = 44;

        private PropertyManagerPageLabel _hdrLabel;
        private PropertyManagerPageGroup[] _stepGroups;
        private PropertyManagerPageButton _backBtn;
        private PropertyManagerPageButton _nextBtn;

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

        public Sw2gzCreateWorldPmp(SldWorks swApp, ModelDoc2 modelDoc, Sw2gzDoc liveDoc, Action<Sw2gzDoc> onCommit)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _modelDoc = modelDoc ?? throw new ArgumentNullException(nameof(modelDoc));
            _liveDoc = liveDoc ?? throw new ArgumentNullException(nameof(liveDoc));
            _onCommit = onCommit ?? (d => { });

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

        private void BuildPage()
        {
            int leftEdge = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int indent   = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            int visibleEnabled = (int)swAddControlOptions_e.swControlOptions_Enabled |
                                 (int)swAddControlOptions_e.swControlOptions_Visible;

            _hdrLabel = (PropertyManagerPageLabel)_page.AddControl2(
                IdHeader,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Step 1 of " + StepCount + " — " + StepNames[0],
                (short)leftEdge, visibleEnabled, "");

            _stepGroups = new PropertyManagerPageGroup[StepCount];
            _stepGroups[StepScene]   = BuildSceneGroup(leftEdge, indent, visibleEnabled);
            _stepGroups[StepAssets]  = BuildAssetsGroup(leftEdge, indent, visibleEnabled);
            _stepGroups[StepPhysics] = BuildPhysicsGroup(leftEdge, indent, visibleEnabled);
            _stepGroups[StepReview]  = BuildReviewGroup(leftEdge, indent, visibleEnabled);

            var footer = (PropertyManagerPageGroup)_page.AddGroupBox(IdFooter, "",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);
            _backBtn = (PropertyManagerPageButton)footer.AddControl2(
                IdBackBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "< Back", (short)leftEdge, visibleEnabled, "Previous step");
            _nextBtn = (PropertyManagerPageButton)footer.AddControl2(
                IdNextBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Next >", (short)leftEdge, visibleEnabled, "Next step");
        }

        private PropertyManagerPageGroup BuildSceneGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdSceneGroup, "Scene",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);
            grp.AddControl2(IdSceneDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Pick the component that represents the ground / world floor.",
                (short)leftEdge, visibleEnabled, "");

            _groundPicker = (PropertyManagerPageSelectionbox)grp.AddControl2(
                IdGroundPicker,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Ground component", (short)leftEdge, visibleEnabled, "Pick exactly one component");
            _groundPicker.SingleEntityOnly = true;
            _groundPicker.Height = 24;
            _groundPicker.Mark = GroundSelectionMark;
            _groundPicker.SetSelectionFilters((object)new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS });

            grp.AddControl2(IdGroundSetBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Set ground from selection", (short)indent, visibleEnabled, "Use the picked component as ground");

            _groundLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdGroundLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");

            grp.AddControl2(IdGroundClearBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Clear", (short)indent, visibleEnabled, "Forget the assigned ground");

            RefreshGroundLabel();
            return grp;
        }

        private PropertyManagerPageGroup BuildAssetsGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdAssetsGroup, "Assets",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);
            grp.AddControl2(IdAssetsDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Pick world-asset components in the viewport, then Add.",
                (short)leftEdge, visibleEnabled, "");

            _assetsPicker = (PropertyManagerPageSelectionbox)grp.AddControl2(
                IdAssetsPicker,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Components", (short)leftEdge, visibleEnabled, "Pick components");
            _assetsPicker.SingleEntityOnly = false;
            _assetsPicker.AllowMultipleSelectOfSameEntity = false;
            _assetsPicker.Height = 30;
            _assetsPicker.Mark = AssetsSelectionMark;
            _assetsPicker.SetSelectionFilters((object)new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS });

            grp.AddControl2(IdAssetsAddBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Add selected", (short)indent, visibleEnabled, "Add picked components to the asset list");

            _assetsList = (PropertyManagerPageListbox)grp.AddControl2(
                IdAssetsList,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "Assets", (short)leftEdge, visibleEnabled, "Current world assets");
            ((IPropertyManagerPageListbox)_assetsList).Height = 110;

            grp.AddControl2(IdAssetsRemoveBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Remove selected", (short)indent, visibleEnabled, "");
            grp.AddControl2(IdAssetsClearBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Clear all", (short)indent, visibleEnabled, "");

            RefreshAssetsList();
            return grp;
        }

        private PropertyManagerPageGroup BuildPhysicsGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdPhysicsGroup, "Physics",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);
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

        private PropertyManagerPageGroup BuildReviewGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdReviewGroup, "Review",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);
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

        // ─── Action handlers ─────────────────────────────────────────
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
                    (string.IsNullOrEmpty(_liveDoc.World.Ground) ? "(not set)" : _liveDoc.World.Ground);
            if (_reviewAssetsLabel != null)
                _reviewAssetsLabel.Caption = "Assets: " + _liveDoc.World.Assets.Count;
            if (_reviewPhysicsLabel != null)
                _reviewPhysicsLabel.Caption = "Physics: " + _liveDoc.World.PhysicsEngine +
                    "  step=" + _liveDoc.World.MaxStepSize + "s  rtf=" + _liveDoc.World.RealTimeFactor;
        }

        private void ShowStep(int step)
        {
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;

            // Leaving Physics: persist current control values back into the doc
            // so Back/Next round-trip preserves the user's edits.
            if (_currentStep == StepPhysics && step != StepPhysics) CommitPhysicsFromControls();

            _currentStep = step;
            for (int i = 0; i < StepCount; i++)
            {
                try { _stepGroups[i].Visible = (i == _currentStep); } catch { }
            }
            _hdrLabel.Caption = "Step " + (_currentStep + 1) + " of " + StepCount + " — " + StepNames[_currentStep];
            ((IPropertyManagerPageControl)_backBtn).Enabled = _currentStep > 0;
            _nextBtn.Caption = (_currentStep == StepCount - 1) ? "Finish" : "Next >";
            if (_currentStep == StepReview) RefreshReviewLabels();
        }

        public void Show()
        {
            if (_page == null) { _swApp.SendMsgToUser("Could not open Create World."); return; }
            _page.Show2(0);
        }

        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            switch (Id)
            {
                case IdBackBtn: if (_currentStep > 0) ShowStep(_currentStep - 1); break;
                case IdNextBtn:
                    if (_currentStep < StepCount - 1) ShowStep(_currentStep + 1);
                    else { CommitPhysicsFromControls(); _page.Close(true); }
                    break;
                case IdGroundSetBtn:   HandleSetGround(); break;
                case IdGroundClearBtn: HandleClearGround(); break;
                case IdAssetsAddBtn:    HandleAddAssets(); break;
                case IdAssetsRemoveBtn: HandleRemoveAsset(); break;
                case IdAssetsClearBtn:  HandleClearAssets(); break;
            }
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            if (Reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Cancel)
            {
                Sw2gzDocSnapshot.Restore(_snapshot, _liveDoc);
                logger.Info("Sw2gzCreateWorldPmp: cancel → snapshot restored");
            }
            else
            {
                CommitPhysicsFromControls();
            }
        }

        void IPropertyManagerPage2Handler9.AfterClose() { if (_liveDoc != null) _onCommit(_liveDoc); }

        void IPropertyManagerPage2Handler9.AfterActivation() { }
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
