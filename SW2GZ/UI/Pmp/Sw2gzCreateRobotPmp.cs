/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateRobotPmp — the "Create Robot" PropertyManagerPage opened from the
mode-specific Create button on the SW2GZ ribbon. Multi-step wizard modelled
on main's Sw2gzExportPmp linear-wizard pattern (one PMP, one group per step,
only currentStep's group .Visible = true, persistent footer group with Back /
Next).

Steps:
    0 — Links   (pick components → robot links)
    1 — Joints  (pick mates → robot joints)
    2 — Review  (counts; Next caption flips to "Finish")

v2.1.0 schema constraint: Sw2gzDoc.Robot.Links and .Joints are flat
List<string> (just names). Rich LinkDef/JointDef + parent/child hierarchy
moves in with the backend-wiring plan. This wizard collects names today;
the export pipeline still reads from the legacy Sw2gzExportConfig attribute
until backend wiring lands.

Mode is NOT a step inside the PMP — it's chosen on the ribbon via mode pills
before this PMP opens. World / Asset get their own PMPs.

COM-rooting note: held as a field on SwAddin (_createRobotPmp). The PMP COM
handler interface is released on AfterClose, so a local would get GC'd after
OpenCreatePmp returns and OK/Cancel callbacks would silently stop firing.

CCW marshalling note: when invoked from the IFlyoutGroup face callback,
swApp.CreatePropertyManagerPage throws InvalidCastException from the COM
marshaller. SwAddin.OpenCreatePmp wraps the launch in DeferToIdle so the
PMP is created on the next message-loop tick, OUTSIDE the flyout callback
context. See SwAddin.cs for the full diagnosis.
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
    public sealed class Sw2gzCreateRobotPmp : PropertyManagerPage2Handler9
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _modelDoc;
        private readonly Sw2gzDoc _liveDoc;
        private readonly Sw2gzDoc _snapshot;
        private readonly Action<Sw2gzDoc> _onCommit;
        private readonly PropertyManagerPage2 _page;

        // ─── Step plan ───────────────────────────────────────────────
        private const int StepLinks  = 0;
        private const int StepJoints = 1;
        private const int StepReview = 2;
        private static readonly string[] StepNames = { "Links", "Joints", "Review" };
        private const int StepCount = 3;

        private int _currentStep = StepLinks;

        // ─── Selection mark — keeps our SelectionBox picks separate from
        //     anything the user selected outside the PMP.
        private const int LinkSelectionMark = 0x4C4;   // arbitrary, must differ from joint mark
        private const int JointSelectionMark = 0x4A1;

        // ─── Control IDs ─────────────────────────────────────────────
        // 1-9 reserved for nav / header.
        private const int IdHeader    = 1;
        private const int IdFooter    = 2;
        private const int IdBackBtn   = 3;
        private const int IdNextBtn   = 4;

        // 10-19 Links step.
        private const int IdLinksGroup       = 10;
        private const int IdLinksDescr       = 11;
        private const int IdLinksPicker      = 12;
        private const int IdLinksAddBtn      = 13;
        private const int IdLinksList        = 14;
        private const int IdLinksRemoveBtn   = 15;
        private const int IdLinksClearBtn    = 16;
        private const int IdLinksReseedBtn   = 17;

        // 20-29 Joints step.
        private const int IdJointsGroup      = 20;
        private const int IdJointsDescr      = 21;
        private const int IdJointsMatesList  = 22;
        private const int IdJointsAddBtn     = 23;
        private const int IdJointsList       = 24;
        private const int IdJointsRemoveBtn  = 25;
        private const int IdJointsClearBtn   = 26;
        private const int IdJointsRefreshBtn = 27;

        // 30-39 Review step.
        private const int IdReviewGroup       = 30;
        private const int IdReviewDescr       = 31;
        private const int IdReviewLinksLabel  = 32;
        private const int IdReviewJointsLabel = 33;
        private const int IdReviewModeLabel   = 34;

        // ─── PMP control refs ────────────────────────────────────────
        private PropertyManagerPageLabel _hdrLabel;
        private PropertyManagerPageGroup[] _stepGroups;
        private PropertyManagerPageButton _backBtn;
        private PropertyManagerPageButton _nextBtn;

        private PropertyManagerPageSelectionbox _linksPicker;
        private PropertyManagerPageListbox _linksList;

        private PropertyManagerPageListbox _matesList;
        private PropertyManagerPageListbox _jointsList;

        private PropertyManagerPageLabel _reviewLinksLabel;
        private PropertyManagerPageLabel _reviewJointsLabel;
        private PropertyManagerPageLabel _reviewModeLabel;

        // ─── Cached mate names (one read per PMP open). ──────────────
        private List<string> _allMateNames = new List<string>();

        public Sw2gzCreateRobotPmp(SldWorks swApp, ModelDoc2 modelDoc, Sw2gzDoc liveDoc, Action<Sw2gzDoc> onCommit)
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
                "Create Robot", opts, this, ref errs);

            if (_page == null)
            {
                logger.Error("Sw2gzCreateRobotPmp: CreatePropertyManagerPage failed (err=" + errs + ")");
                return;
            }

            SeedLinksFromAssemblyIfEmpty();
            ReadAllMates();

            BuildPage();
            ShowStep(StepLinks);
        }

        // ─── Page build ──────────────────────────────────────────────
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
            _stepGroups[StepLinks]  = BuildLinksGroup(leftEdge, indent, visibleEnabled);
            _stepGroups[StepJoints] = BuildJointsGroup(leftEdge, indent, visibleEnabled);
            _stepGroups[StepReview] = BuildReviewGroup(leftEdge, indent, visibleEnabled);

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

        private PropertyManagerPageGroup BuildLinksGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdLinksGroup, "Links",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);

            grp.AddControl2(IdLinksDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Pick assembly components in the viewport, then 'Add as link'.",
                (short)leftEdge, visibleEnabled, "");

            _linksPicker = (PropertyManagerPageSelectionbox)grp.AddControl2(
                IdLinksPicker,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Components", (short)leftEdge, visibleEnabled,
                "Pick components in the viewport — click Add to convert to links");
            _linksPicker.SingleEntityOnly = false;
            _linksPicker.AllowMultipleSelectOfSameEntity = false;
            _linksPicker.Height = 30;
            _linksPicker.Mark = LinkSelectionMark;
            var linkFilters = new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS };
            _linksPicker.SetSelectionFilters((object)linkFilters);

            grp.AddControl2(IdLinksAddBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Add as link(s)", (short)indent, visibleEnabled, "Convert each picked component into a link");

            _linksList = (PropertyManagerPageListbox)grp.AddControl2(
                IdLinksList,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "Robot links", (short)leftEdge, visibleEnabled, "Current robot links — select one to remove");
            ((IPropertyManagerPageListbox)_linksList).Height = 110;

            grp.AddControl2(IdLinksRemoveBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Remove selected", (short)indent, visibleEnabled, "Remove the highlighted link");
            grp.AddControl2(IdLinksClearBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Clear all", (short)indent, visibleEnabled, "Remove every link");
            grp.AddControl2(IdLinksReseedBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Reseed from assembly", (short)indent, visibleEnabled,
                "Replace the list with one link per top-level component");

            RefreshLinksList();
            return grp;
        }

        private PropertyManagerPageGroup BuildJointsGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdJointsGroup, "Joints",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);

            grp.AddControl2(IdJointsDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Pick an assembly mate from the list, then 'Add as joint'.",
                (short)leftEdge, visibleEnabled, "");

            _matesList = (PropertyManagerPageListbox)grp.AddControl2(
                IdJointsMatesList,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "Assembly mates", (short)leftEdge, visibleEnabled, "All mates in this assembly");
            ((IPropertyManagerPageListbox)_matesList).Height = 110;

            grp.AddControl2(IdJointsAddBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Add as joint", (short)indent, visibleEnabled, "Convert the selected mate into a joint");

            _jointsList = (PropertyManagerPageListbox)grp.AddControl2(
                IdJointsList,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "Robot joints", (short)leftEdge, visibleEnabled, "Current robot joints");
            ((IPropertyManagerPageListbox)_jointsList).Height = 90;

            grp.AddControl2(IdJointsRemoveBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Remove selected", (short)indent, visibleEnabled, "Remove the highlighted joint");
            grp.AddControl2(IdJointsClearBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Clear all", (short)indent, visibleEnabled, "Remove every joint");
            grp.AddControl2(IdJointsRefreshBtn,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Reload mates", (short)indent, visibleEnabled,
                "Re-read the assembly's mate list (use after editing mates in the FeatureManager)");

            RefreshMatesList();
            RefreshJointsList();
            return grp;
        }

        private PropertyManagerPageGroup BuildReviewGroup(int leftEdge, int indent, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdReviewGroup, "Review",
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded);

            grp.AddControl2(IdReviewDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Review and click Finish to commit. Cancel rolls back to the snapshot.",
                (short)leftEdge, visibleEnabled, "");

            _reviewModeLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewModeLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Mode: Robot",
                (short)leftEdge, visibleEnabled, "");
            _reviewLinksLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewLinksLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Links: 0", (short)leftEdge, visibleEnabled, "");
            _reviewJointsLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewJointsLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Joints: 0", (short)leftEdge, visibleEnabled, "");
            return grp;
        }

        // ─── Assembly enumeration ────────────────────────────────────
        private void SeedLinksFromAssemblyIfEmpty()
        {
            if (_liveDoc.Robot.Links.Count > 0) return;
            try
            {
                AssemblyDoc asm = _modelDoc as AssemblyDoc;
                if (asm == null) return;
                object[] comps = (object[])asm.GetComponents(true);
                if (comps == null) return;
                foreach (object o in comps)
                {
                    var c = o as Component2;
                    if (c == null || c.IsSuppressed()) continue;
                    _liveDoc.Robot.Links.Add(c.Name2);
                }
                logger.Info("Sw2gzCreateRobotPmp: seeded " + _liveDoc.Robot.Links.Count + " links from assembly");
            }
            catch (Exception e)
            {
                logger.Warn("SeedLinksFromAssemblyIfEmpty failed", e);
            }
        }

        // Walks the FeatureManager looking for the MatesFolder feature, then
        // enumerates its sub-features (each is a mate). This is simpler than
        // the SolidWorksAssemblyWalker.WalkAllMates path on main and is
        // sufficient for the v2.1.0 wizard, which only needs the mate name.
        private void ReadAllMates()
        {
            _allMateNames = new List<string>();
            try
            {
                Feature feat = (Feature)_modelDoc.FirstFeature();
                while (feat != null)
                {
                    string typeName = feat.GetTypeName2();
                    if (typeName == "MateGroup")
                    {
                        Feature mate = (Feature)feat.GetFirstSubFeature();
                        while (mate != null)
                        {
                            string subType = mate.GetTypeName2();
                            // MateGroup children include both real mates and
                            // construction features (e.g. reference axes). Real
                            // mate features have the "Mate" prefix on TypeName2.
                            if (subType != null && subType.StartsWith("Mate"))
                            {
                                _allMateNames.Add(mate.Name);
                            }
                            mate = (Feature)mate.GetNextSubFeature();
                        }
                    }
                    feat = (Feature)feat.GetNextFeature();
                }
                logger.Info("Sw2gzCreateRobotPmp: found " + _allMateNames.Count + " mates");
            }
            catch (Exception e)
            {
                logger.Warn("ReadAllMates failed", e);
            }
        }

        // ─── List refresh helpers ────────────────────────────────────
        private void RefreshLinksList()
        {
            if (_linksList == null) return;
            _linksList.Clear();
            foreach (string name in _liveDoc.Robot.Links) _linksList.AddItems(name);
            if (_liveDoc.Robot.Links.Count > 0) _linksList.CurrentSelection = 0;
        }

        private void RefreshMatesList()
        {
            if (_matesList == null) return;
            _matesList.Clear();
            foreach (string name in _allMateNames) _matesList.AddItems(name);
            if (_allMateNames.Count > 0) _matesList.CurrentSelection = 0;
        }

        private void RefreshJointsList()
        {
            if (_jointsList == null) return;
            _jointsList.Clear();
            foreach (string name in _liveDoc.Robot.Joints) _jointsList.AddItems(name);
            if (_liveDoc.Robot.Joints.Count > 0) _jointsList.CurrentSelection = 0;
        }

        private void RefreshReviewLabels()
        {
            if (_reviewLinksLabel != null)
                _reviewLinksLabel.Caption = "Links: " + _liveDoc.Robot.Links.Count;
            if (_reviewJointsLabel != null)
                _reviewJointsLabel.Caption = "Joints: " + _liveDoc.Robot.Joints.Count;
            if (_reviewModeLabel != null)
                _reviewModeLabel.Caption = "Mode: Robot";
        }

        // ─── Action handlers ─────────────────────────────────────────
        private void HandleAddLink()
        {
            try
            {
                ISelectionMgr selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
                if (selMgr == null) return;
                int count = selMgr.GetSelectedObjectCount2(LinkSelectionMark);
                int added = 0;
                for (int i = 1; i <= count; i++)
                {
                    object selObj = selMgr.GetSelectedObject6(i, LinkSelectionMark);
                    if (selObj is Component2 c && !string.IsNullOrEmpty(c.Name2)
                        && !_liveDoc.Robot.Links.Contains(c.Name2))
                    {
                        _liveDoc.Robot.Links.Add(c.Name2);
                        added++;
                    }
                }
                logger.Info("HandleAddLink: " + added + " new links from " + count + " picks");
                RefreshLinksList();
                // Clear the picker so user can start a fresh selection.
                _modelDoc.ClearSelection2(true);
            }
            catch (Exception e)
            {
                logger.Warn("HandleAddLink failed", e);
            }
        }

        private void HandleRemoveLink()
        {
            int idx = _linksList != null ? _linksList.CurrentSelection : -1;
            if (idx < 0 || idx >= _liveDoc.Robot.Links.Count) return;
            _liveDoc.Robot.Links.RemoveAt(idx);
            RefreshLinksList();
        }

        private void HandleClearLinks()
        {
            _liveDoc.Robot.Links.Clear();
            RefreshLinksList();
        }

        private void HandleReseedLinks()
        {
            _liveDoc.Robot.Links.Clear();
            SeedLinksFromAssemblyIfEmpty();
            RefreshLinksList();
        }

        private void HandleAddJoint()
        {
            int idx = _matesList != null ? _matesList.CurrentSelection : -1;
            if (idx < 0 || idx >= _allMateNames.Count) return;
            string mateName = _allMateNames[idx];
            if (!_liveDoc.Robot.Joints.Contains(mateName))
            {
                _liveDoc.Robot.Joints.Add(mateName);
                RefreshJointsList();
            }
        }

        private void HandleRemoveJoint()
        {
            int idx = _jointsList != null ? _jointsList.CurrentSelection : -1;
            if (idx < 0 || idx >= _liveDoc.Robot.Joints.Count) return;
            _liveDoc.Robot.Joints.RemoveAt(idx);
            RefreshJointsList();
        }

        private void HandleClearJoints()
        {
            _liveDoc.Robot.Joints.Clear();
            RefreshJointsList();
        }

        private void HandleReloadMates()
        {
            ReadAllMates();
            RefreshMatesList();
        }

        // ─── Navigation ──────────────────────────────────────────────
        private void ShowStep(int step)
        {
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;
            _currentStep = step;

            for (int i = 0; i < StepCount; i++)
            {
                try { _stepGroups[i].Visible = (i == _currentStep); }
                catch (Exception ex) { logger.Error("Set group[" + i + "].Visible failed", ex); }
            }

            _hdrLabel.Caption = "Step " + (_currentStep + 1) + " of " + StepCount +
                                " — " + StepNames[_currentStep];

            ((IPropertyManagerPageControl)_backBtn).Enabled = _currentStep > 0;
            _nextBtn.Caption = (_currentStep == StepCount - 1) ? "Finish" : "Next >";

            if (_currentStep == StepReview) RefreshReviewLabels();
        }

        public void Show()
        {
            if (_page == null)
            {
                _swApp.SendMsgToUser("Could not open the Create Robot panel. See log for details.");
                return;
            }
            _page.Show2(0);
        }

        // ─── PMP handler ─────────────────────────────────────────────
        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            switch (Id)
            {
                case IdBackBtn:
                    if (_currentStep > 0) ShowStep(_currentStep - 1);
                    break;
                case IdNextBtn:
                    if (_currentStep < StepCount - 1) ShowStep(_currentStep + 1);
                    else _page.Close(true);   // last step → Finish = OK
                    break;
                case IdLinksAddBtn:    HandleAddLink(); break;
                case IdLinksRemoveBtn: HandleRemoveLink(); break;
                case IdLinksClearBtn:  HandleClearLinks(); break;
                case IdLinksReseedBtn: HandleReseedLinks(); break;
                case IdJointsAddBtn:    HandleAddJoint(); break;
                case IdJointsRemoveBtn: HandleRemoveJoint(); break;
                case IdJointsClearBtn:  HandleClearJoints(); break;
                case IdJointsRefreshBtn: HandleReloadMates(); break;
            }
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            if (Reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Cancel)
            {
                Sw2gzDocSnapshot.Restore(_snapshot, _liveDoc);
                logger.Info("Sw2gzCreateRobotPmp: cancel → snapshot restored");
            }
        }

        void IPropertyManagerPage2Handler9.AfterClose()
        {
            if (_liveDoc != null) _onCommit(_liveDoc);
        }

        // No-op handler stubs (PMP COM contract requires the full surface).
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
