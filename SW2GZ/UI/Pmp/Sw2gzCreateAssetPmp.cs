/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateAssetPmp — the "Create Asset" PropertyManagerPage opened from the
mode-specific Create button when the active mode is Asset. Maps to
Sw2gzDoc.Asset fields:

Steps:
    0 — Body     (pick a single component → Sw2gzAssetConfig.BodyPart, set static)
    1 — Surface  (friction coefficient)
    2 — Review

Reusable static or dynamic asset for a Gz world include — schema is the
flat Sw2gzAssetConfig in v2.1.0.
*/
#if SW_INTEROP
using System;
using System.Runtime.InteropServices;
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

        private const int StepBody    = 0;
        private const int StepSurface = 1;
        private const int StepReview  = 2;
        private static readonly string[] StepNames = { "Body", "Surface", "Review" };
        private const int StepCount = 3;

        private int _currentStep = StepBody;
        private bool _okay;

        private const int BodySelectionMark = 0x4C0;

        private const int IdHeader  = 1;
        private const int IdFooter  = 2;
        private const int IdBackBtn = 3;
        private const int IdNextBtn = 4;

        // Body step
        private const int IdBodyGroup     = 10;
        private const int IdBodyDescr     = 11;
        private const int IdBodyPicker    = 12;
        private const int IdBodySetBtn    = 13;
        private const int IdBodyLabel     = 14;
        private const int IdBodyClearBtn  = 15;
        private const int IdBodyStaticChk = 16;

        // Surface step
        private const int IdSurfaceGroup     = 20;
        private const int IdSurfaceDescr     = 21;
        private const int IdSurfaceFrictionBox = 22;

        // Review step
        private const int IdReviewGroup      = 30;
        private const int IdReviewDescr      = 31;
        private const int IdReviewBodyLabel  = 32;
        private const int IdReviewSurfLabel  = 33;

        private PropertyManagerPageLabel _hdrLabel;
        private PropertyManagerPageGroup[] _stepGroups;
        private PropertyManagerPageButton _backBtn;
        private PropertyManagerPageButton _nextBtn;

        private PropertyManagerPageSelectionbox _bodyPicker;
        private PropertyManagerPageLabel _bodyLabel;
        private PropertyManagerPageCheckbox _bodyStaticChk;

        private PropertyManagerPageNumberbox _frictionBox;

        private PropertyManagerPageLabel _reviewBodyLabel;
        private PropertyManagerPageLabel _reviewSurfLabel;

        public Sw2gzCreateAssetPmp(SldWorks swApp, ModelDoc2 modelDoc, Sw2gzDoc liveDoc, Action<Sw2gzDoc> onCommit)
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
                "Create Asset", opts, this, ref errs);

            if (_page == null)
            {
                logger.Error("Sw2gzCreateAssetPmp: CreatePropertyManagerPage failed (err=" + errs + ")");
                return;
            }
            BuildPage();
            ShowStep(StepBody);
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
            _stepGroups[StepBody]    = BuildBodyGroup(leftEdge, indent, visibleEnabled);
            _stepGroups[StepSurface] = BuildSurfaceGroup(leftEdge, indent, visibleEnabled);
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

        private PropertyManagerPageGroup BuildBodyGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdBodyGroup, "Body",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);
            grp.AddControl2(IdBodyDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Pick the component representing this asset's body.",
                (short)leftEdge, visibleEnabled, "");

            _bodyPicker = (PropertyManagerPageSelectionbox)grp.AddControl2(
                IdBodyPicker,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Body component", (short)leftEdge, visibleEnabled, "Pick exactly one component");
            _bodyPicker.SingleEntityOnly = true;
            _bodyPicker.Height = 24;
            _bodyPicker.Mark = BodySelectionMark;
            _bodyPicker.SetSelectionFilters((object)new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS });

            grp.AddControl2(IdBodySetBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Set body from selection", (short)indent, visibleEnabled, "");

            _bodyLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdBodyLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");

            grp.AddControl2(IdBodyClearBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Clear", (short)indent, visibleEnabled, "");

            _bodyStaticChk = (PropertyManagerPageCheckbox)grp.AddControl2(
                IdBodyStaticChk,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Static (won't move in physics)", (short)leftEdge, visibleEnabled, "");
            _bodyStaticChk.Checked = _liveDoc.Asset.IsStatic;

            RefreshBodyLabel();
            return grp;
        }

        private PropertyManagerPageGroup BuildSurfaceGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdSurfaceGroup, "Surface",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);
            grp.AddControl2(IdSurfaceDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Set the surface friction coefficient (Coulomb μ).",
                (short)leftEdge, visibleEnabled, "");

            _frictionBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdSurfaceFrictionBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Friction μ", (short)leftEdge, visibleEnabled, "Static friction coefficient");
            _frictionBox.SetRange2((int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble,
                0.0, 5.0, true, 0.8, 0.01, 0.05);
            _frictionBox.Value = _liveDoc.Asset.FrictionMu;
            return grp;
        }

        private PropertyManagerPageGroup BuildReviewGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdReviewGroup, "Review",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);
            grp.AddControl2(IdReviewDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Review and Finish to commit.",
                (short)leftEdge, visibleEnabled, "");
            _reviewBodyLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewBodyLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            _reviewSurfLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewSurfLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            return grp;
        }

        private void HandleSetBody()
        {
            try
            {
                ISelectionMgr selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
                if (selMgr == null) return;
                int count = selMgr.GetSelectedObjectCount2(BodySelectionMark);
                if (count < 1) return;
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
                ? "Body: (not set)" : "Body: " + _liveDoc.Asset.BodyPart;
        }

        private void CommitBodyFromControls()
        {
            if (_bodyStaticChk != null) _liveDoc.Asset.IsStatic = _bodyStaticChk.Checked;
        }

        private void CommitSurfaceFromControls()
        {
            if (_frictionBox != null) _liveDoc.Asset.FrictionMu = _frictionBox.Value;
        }

        private void RefreshReviewLabels()
        {
            CommitBodyFromControls();
            CommitSurfaceFromControls();
            if (_reviewBodyLabel != null)
                _reviewBodyLabel.Caption = "Body: " +
                    (string.IsNullOrEmpty(_liveDoc.Asset.BodyPart) ? "(not set)" : _liveDoc.Asset.BodyPart) +
                    (_liveDoc.Asset.IsStatic ? "  [static]" : "  [dynamic]");
            if (_reviewSurfLabel != null)
                _reviewSurfLabel.Caption = "Friction μ: " + _liveDoc.Asset.FrictionMu;
        }

        private void ShowStep(int step)
        {
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;

            if (_currentStep == StepBody    && step != StepBody)    CommitBodyFromControls();
            if (_currentStep == StepSurface && step != StepSurface) CommitSurfaceFromControls();

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
            if (_page == null) { _swApp.SendMsgToUser("Could not open Create Asset."); return; }
            _page.Show2(0);
        }

        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            switch (Id)
            {
                case IdBackBtn: if (_currentStep > 0) ShowStep(_currentStep - 1); break;
                case IdNextBtn:
                    if (_currentStep < StepCount - 1) ShowStep(_currentStep + 1);
                    else { CommitBodyFromControls(); CommitSurfaceFromControls(); _okay = true; _page.Close(true); }
                    break;
                case IdBodySetBtn:   HandleSetBody(); break;
                case IdBodyClearBtn: HandleClearBody(); break;
            }
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            bool okay = Reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay;
            _okay = _okay || okay;
            if (!_okay)
            {
                Sw2gzDocSnapshot.Restore(_snapshot, _liveDoc);
                logger.Info("Sw2gzCreateAssetPmp: cancel → snapshot restored");
            }
            else
            {
                CommitBodyFromControls();
                CommitSurfaceFromControls();
            }
        }

        void IPropertyManagerPage2Handler9.AfterClose() { if (_okay && _liveDoc != null) _onCommit(_liveDoc); }

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
