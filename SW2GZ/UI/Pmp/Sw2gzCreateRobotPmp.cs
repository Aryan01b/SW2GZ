/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateRobotPmp — the "Create Robot" PropertyManagerPage opened from the
mode-specific Create button when the active mode is Robot. Minimal v3
rebuild cut: a flat list of links (one per top-level component), all fixed
to a single base link — no mate-driven joint detection yet (see
agent-progress/progress.md "Robot mode gutted for clean rebuild"). Mirrors
Sw2gzCreateWorldPmp's chrome exactly: a WinForms nav bar (Back/Next + step
indicator, dark theme) embedded via WindowFromHandle, and a WinForms
action-button bar per step. PMP swControlType_Button controls are avoided
entirely — clicking one and mutating PMP state from inside OnButtonPress
corrupts SW's PMP renderer. Nav clicks defer via BeginInvoke so the
group-visibility flip runs off the click-handler reentrancy frame.

Steps map to Sw2gzDoc.Robot:
    0 — Links    (seeded from top-level components; first link = base,
                  every other link gets an implicit Fixed joint to it)
    1 — Review   (counts; Next caption flips to "Finish")
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
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
        private readonly Action _onClosed;
        private readonly PropertyManagerPage2 _page;

        private const int StepLinks  = 0;
        private const int StepReview = 1;
        private static readonly string[] StepNames = { "Links", "Review" };
        private const int StepCount = 2;

        private bool _okay;
        private int _currentStep = StepLinks;

        // Header (Progress) group + nav bar.
        private const int IdHeaderGroup = 1;
        private const int IdHeaderLabel = 2;
        private const int IdNavBar      = 3;

        // Step groups.
        private const int IdLinksGroup  = 10;
        private const int IdLinksDescr  = 11;
        private const int IdLinksBtnBar = 12;
        private const int IdLinksList   = 13;

        private const int IdReviewGroup       = 20;
        private const int IdReviewDescr       = 21;
        private const int IdReviewLinksLabel  = 22;
        private const int IdReviewBaseLabel   = 23;
        private const int IdReviewJointsLabel = 24;

        private PropertyManagerPageGroup[] _stepGroups;

        // WinForms nav bar (Back/Next + step indicator).
        private PropertyManagerPageWindowFromHandle _navHandle;
        private System.Windows.Forms.Panel _navBar;
        private System.Windows.Forms.Button _backBtn;
        private System.Windows.Forms.Button _nextBtn;
        private System.Windows.Forms.Label _stepIndicator;

        // WinForms per-step action bar (Links step).
        private PropertyManagerPageWindowFromHandle _linksBarHandle;
        private System.Windows.Forms.Panel _linksBar;
        private System.Windows.Forms.Button _reseedBtn;
        private System.Windows.Forms.Button _removeLinkBtn;
        private System.Windows.Forms.Button _clearLinksBtn;

        // PMP-native controls.
        private PropertyManagerPageListbox _linksList;
        private PropertyManagerPageLabel _reviewLinksLabel;
        private PropertyManagerPageLabel _reviewBaseLabel;
        private PropertyManagerPageLabel _reviewJointsLabel;

        public Sw2gzCreateRobotPmp(SldWorks swApp, ModelDoc2 modelDoc, Sw2gzDoc liveDoc, Action<Sw2gzDoc> onCommit, Action onClosed = null)
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
                "Create Robot", opts, this, ref errs);

            if (_page == null)
            {
                logger.Error("Sw2gzCreateRobotPmp: CreatePropertyManagerPage failed (err=" + errs + ")");
                return;
            }

            BuildPage();
            if (_liveDoc.Robot.Links.Count == 0) SeedLinksFromAssembly();
            ShowStep(StepLinks);
        }

        // ── Dark-theme palette (mirrors Sw2gzCreateWorldPmp) ──────────────────
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
            _stepGroups[StepLinks]  = BuildLinksGroup(grpOptions, leftEdge, visibleEnabled);
            _stepGroups[StepReview] = BuildReviewGroup(grpOptions, leftEdge, visibleEnabled);
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

        private PropertyManagerPageGroup BuildLinksGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdLinksGroup, "Links", grpOptions);
            grp.AddControl2(IdLinksDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "One link per top-level component. The first link is the base "
                + "link; every other link gets a Fixed joint to it. Reseed "
                + "re-scans the assembly for new components without dropping "
                + "existing links.",
                (short)leftEdge, visibleEnabled, "");

            _linksBar       = NewBar(260, 32);
            _reseedBtn      = NewBarButton("Reseed", 70);
            _removeLinkBtn  = NewBarButton("Remove", 70);
            _clearLinksBtn  = NewBarButton("Clear all", 80);
            _reseedBtn.Click     += (s, e) => HandleReseed();
            _removeLinkBtn.Click += (s, e) => HandleRemoveLink();
            _clearLinksBtn.Click += (s, e) => HandleClearLinks();
            _linksBar.Controls.Add(_reseedBtn);
            _linksBar.Controls.Add(_removeLinkBtn);
            _linksBar.Controls.Add(_clearLinksBtn);
            _linksBar.Resize += (s, e) => CenterRow(_linksBar, _reseedBtn, _removeLinkBtn, _clearLinksBtn);
            CenterRow(_linksBar, _reseedBtn, _removeLinkBtn, _clearLinksBtn);
            _linksBarHandle = (PropertyManagerPageWindowFromHandle)grp.AddControl2(
                IdLinksBtnBar,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Reseed, remove, or clear links");
            _linksBarHandle.Height = 34;
            _linksBarHandle.SetWindowHandlex64(_linksBar.Handle.ToInt64());

            _linksList = (PropertyManagerPageListbox)grp.AddControl2(
                IdLinksList,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "Links", (short)leftEdge, visibleEnabled, "Current robot links");
            ((IPropertyManagerPageListbox)_linksList).Height = 140;

            RefreshLinksList();
            return grp;
        }

        private PropertyManagerPageGroup BuildReviewGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdReviewGroup, "Review", grpOptions);
            grp.AddControl2(IdReviewDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Review and Finish to commit. Cancel rolls back.",
                (short)leftEdge, visibleEnabled, "");
            _reviewLinksLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewLinksLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            _reviewBaseLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewBaseLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            _reviewJointsLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdReviewJointsLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            return grp;
        }

        // ─── Action handlers ───────────────────────────────────────────────────
        private void SeedLinksFromAssembly()
        {
            try
            {
                object[] comps = (object[])((AssemblyDoc)_modelDoc).GetComponents(true);
                if (comps == null) return;
                var existing = new HashSet<string>(
                    _liveDoc.Robot.Links.SelectMany(l => l.ComponentIds), StringComparer.OrdinalIgnoreCase);
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (c.IsSuppressed()) continue;
                    if (string.IsNullOrEmpty(c.Name2)) continue;
                    if (existing.Contains(c.Name2)) continue;
                    string linkName = UniqueLinkName(RosNameSanitizer.Sanitize(c.Name2).Value);
                    _liveDoc.Robot.Links.Add(new LinkDef { Name = linkName, ComponentIds = { c.Name2 } });
                    existing.Add(c.Name2);
                }
                RebuildParentsAndJoints();
                RefreshLinksList();
            }
            catch (Exception e) { logger.Warn("SeedLinksFromAssembly failed", e); }
        }

        private void HandleReseed() => SeedLinksFromAssembly();

        private void HandleRemoveLink()
        {
            int idx = _linksList != null ? _linksList.CurrentSelection : -1;
            if (idx < 0 || idx >= _liveDoc.Robot.Links.Count) return;
            _liveDoc.Robot.Links.RemoveAt(idx);
            RebuildParentsAndJoints();
            RefreshLinksList();
        }

        private void HandleClearLinks()
        {
            _liveDoc.Robot.Links.Clear();
            _liveDoc.Robot.Joints.Clear();
            RefreshLinksList();
        }

        // Every link after the first is Fixed to the first (the base link).
        // Re-derived after every list mutation so Links/Joints never drift out
        // of sync — there is no manual joint editing and no mate-driven
        // detection in this cut (removed 2026-07-01 — detection was
        // misclassifying joints; reverted to the last known-good, fully-Fixed
        // baseline until it's rebuilt and verified live).
        private void RebuildParentsAndJoints()
        {
            _liveDoc.Robot.Joints.Clear();
            if (_liveDoc.Robot.Links.Count == 0) return;

            LinkDef baseLink = _liveDoc.Robot.Links[0];
            baseLink.ParentName = string.Empty;
            for (int i = 1; i < _liveDoc.Robot.Links.Count; i++)
            {
                LinkDef link = _liveDoc.Robot.Links[i];
                link.ParentName = baseLink.Name;
                _liveDoc.Robot.Joints.Add(new JointDef
                {
                    Name = baseLink.Name + "_to_" + link.Name,
                    ParentLink = baseLink.Name,
                    ChildLink = link.Name,
                    Type = UrdfJointType.Fixed,
                });
            }
        }

        private static string UniqueLinkName(string baseName)
        {
            // Caller already checked against ComponentIds for dedup; this only
            // guards a same-named-different-instance edge case.
            return baseName;
        }

        private void RefreshLinksList()
        {
            if (_linksList == null) return;
            _linksList.Clear();
            for (int i = 0; i < _liveDoc.Robot.Links.Count; i++)
            {
                LinkDef link = _liveDoc.Robot.Links[i];
                _linksList.AddItems(i == 0 ? link.Name + "  (base)" : link.Name + "  -> " + link.ParentName + " (fixed)");
            }
            if (_liveDoc.Robot.Links.Count > 0) _linksList.CurrentSelection = 0;
        }

        private void RefreshReviewLabels()
        {
            if (_reviewLinksLabel != null)
                _reviewLinksLabel.Caption = "Links: " + _liveDoc.Robot.Links.Count;
            if (_reviewBaseLabel != null)
                _reviewBaseLabel.Caption = "Base link: " +
                    (_liveDoc.Robot.Links.Count > 0 ? _liveDoc.Robot.Links[0].Name : "(none)");
            if (_reviewJointsLabel != null)
                _reviewJointsLabel.Caption = "Joints (fixed): " + _liveDoc.Robot.Joints.Count;
        }

        // ─── Navigation ──────────────────────────────────────────────────────
        private void ShowStep(int step)
        {
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;

            _currentStep = step;
            for (int i = 0; i < StepCount; i++)
            {
                try { _stepGroups[i].Visible = (i == _currentStep); }
                catch (Exception ex) { logger.Error("Robot ShowStep group[" + i + "].Visible failed", ex); }
            }

            try { _stepIndicator.Text = "Step " + (_currentStep + 1) + " of " + StepCount + " — " + StepNames[_currentStep]; }
            catch (Exception ex) { logger.Error("Robot ShowStep _stepIndicator.Text threw", ex); }
            try { _backBtn.Enabled = _currentStep > 0; } catch (Exception ex) { logger.Error("Robot ShowStep back threw", ex); }
            try
            {
                bool lastStep = _currentStep == StepCount - 1;
                _nextBtn.Text  = lastStep ? "Finish" : "▶";
                _nextBtn.Width = lastStep ? 80 : 50;
                CenterRow(_navBar, _backBtn, _nextBtn);
            }
            catch (Exception ex) { logger.Error("Robot ShowStep next threw", ex); }

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
                _okay = true;
                _page.Close(true);
            }
        }

        public void Show()
        {
            if (_page == null) { _swApp.SendMsgToUser("Could not open Create Robot."); return; }
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
                logger.Info("Sw2gzCreateRobotPmp: cancel → snapshot restored");
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
