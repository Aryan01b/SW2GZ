/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateRobotPmp — the "Create Robot" PropertyManagerPage opened from the
mode-specific Create button on the SW2GZ ribbon.

Ports main branch's Sw2gzExportPmp Links + Joints + Review steps onto v2.1.0's
Sw2gzDoc.Robot subtree (LinkDef[] + JointDef[]). Mode is no longer a step
inside the wizard — it's chosen on the ribbon by pill before this PMP opens,
so the wizard is fixed 3 steps:
    0 — Links   (embedded LinkTreeView + pick funnel + Add/Remove + mass + validation)
    1 — Joints  (joints listbox + mates listbox + selected-joint detail)
    2 — Review  (compact metadata + links/joints lists; Finish persists Sw2gzDoc)

Output / Package / Author fields still live in ExportDialog under the Export
ribbon button (unchanged from main).
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.SwSurface;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.UI;
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

        private const int StepLinks  = 0;
        private const int StepJoints = 1;
        private const int StepReview = 2;
        private static readonly string[] StepNames = { "Create base model structure", "Joints", "Review" };
        private const int StepCount = 3;

        private int _currentStep = StepLinks;
        private bool _okay;

        private Sw2gzRobotConfig Robot => _liveDoc.Robot;

        private const int HeaderGroupID  = 1;
        private const int HeaderLabelID  = 2;
        private const int NavBarHandleID = 3;

        private const int StepIdBase = 100;
        private int StepGroupId(int step) => StepIdBase + step * 20;

        private const int TreeHandleID          = StepIdBase + 0 * 20 + 2;
        private const int PickFunnelID          = StepIdBase + 0 * 20 + 3;
        private const int BtnBarHandleID        = StepIdBase + 0 * 20 + 4;
        private const int LabelLinkMassID       = StepIdBase + 0 * 20 + 6;
        private const int LabelLinkValidationID = StepIdBase + 0 * 20 + 7;
        private const int LabelLinkInstrID      = StepIdBase + 0 * 20 + 8;
        private const int LabelLinkNameCapID    = StepIdBase + 0 * 20 + 9;
        private const int TextLinkNameID        = StepIdBase + 0 * 20 + 10;

        private const int LabelJointInstrID    = StepIdBase + 1 * 20 + 2;
        private const int JointBarHandleID     = StepIdBase + 1 * 20 + 3;
        private const int ListJointsID         = StepIdBase + 1 * 20 + 4;
        private const int LabelMatesCapID      = StepIdBase + 1 * 20 + 5;
        private const int LabelDetailCapID     = StepIdBase + 1 * 20 + 6;
        private const int LabelDetailLinksID   = StepIdBase + 1 * 20 + 7;
        private const int LabelDetailMateID    = StepIdBase + 1 * 20 + 8;
        private const int LabelDetailTypeID    = StepIdBase + 1 * 20 + 9;
        private const int LabelDetailLimitsID  = StepIdBase + 1 * 20 + 10;
        private const int LabelDetailFrameSwID  = StepIdBase + 1 * 20 + 11;
        private const int LabelDetailFrameRosID = StepIdBase + 1 * 20 + 12;
        private const int ListMatesID          = StepIdBase + 1 * 20 + 13;

        private const int LabelReviewInstrID     = StepIdBase + 2 * 20 + 2;
        private const int LabelReviewModeID      = StepIdBase + 2 * 20 + 3;
        private const int LabelReviewLinksCapID  = StepIdBase + 2 * 20 + 4;
        private const int ListReviewLinksID      = StepIdBase + 2 * 20 + 5;
        private const int LabelReviewJointsCapID = StepIdBase + 2 * 20 + 6;
        private const int ListReviewJointsID    = StepIdBase + 2 * 20 + 7;

        private const int LinkSelectionMark = 3;

        private PropertyManagerPageLabel _hdrLabel;
        private PropertyManagerPageGroup[] _stepGroups;
        private PropertyManagerPageWindowFromHandle _navHandle;
        private System.Windows.Forms.Panel _navBar;
        private System.Windows.Forms.Button _backBtn;
        private System.Windows.Forms.Button _nextBtn;
        // WinForms-side step indicator. _hdrLabel.Caption COM setter intermittently
        // AVs inside mscorlib (CSE that bypasses managed catch) when invoked from
        // a deferred button-click stack after a group-visibility flip. Routing the
        // dynamic step text through a WinForms label avoids the COM call entirely.
        private System.Windows.Forms.Label _stepIndicator;

        private PropertyManagerPageWindowFromHandle _treeHandle;
        private PropertyManagerPageWindowFromHandle _btnBarHandle;
        private System.Windows.Forms.Panel _linkBtnBar;
        private System.Windows.Forms.Button _addLinkBtn;
        private System.Windows.Forms.Button _removeLinkBtn;
        private LinkTreeView _linkTree;
        private PropertyManagerPageSelectionbox _pickFunnel;
        private PropertyManagerPageLabel _linkMass;
        private PropertyManagerPageLabel _linkValidation;
        private PropertyManagerPageTextbox _linkNameBox;
        private LinkDef _activeLink;
        private bool _suppressLinkSelectionLoad;
        private bool _suppressLinkNameEvents;
        private IMassProperties _massProps;
        private readonly List<string> _allComponentIds = new List<string>();

        private PropertyManagerPageListbox _jointsListBox;
        private PropertyManagerPageListbox _matesListBox;
        private PropertyManagerPageLabel _matesCap;
        private PropertyManagerPageLabel _detailLinks;
        private PropertyManagerPageLabel _detailMate;
        private PropertyManagerPageLabel _detailType;
        private PropertyManagerPageLabel _detailLimits;
        private PropertyManagerPageLabel _detailFrameSw;
        private PropertyManagerPageLabel _detailFrameRos;
        private int _activeJointIndex = -1;
        private int _activeMateIndex = -1;
        private List<string> _activeMateNames = new List<string>();

        // Apply-mate button bar (WinForms-embedded, dark theme). Sits between
        // the mates listbox and the detail labels. Click runs
        // AutoJointResolver.ResolveFromMateName against the selected mate
        // for the currently active joint, then UpdateJointDetails.
        private PropertyManagerPageWindowFromHandle _jointBarHandle;
        private System.Windows.Forms.Panel _jointBtnBar;
        private System.Windows.Forms.Button _applyMateBtn;

        private PropertyManagerPageLabel _reviewMode;
        private PropertyManagerPageLabel _reviewLinksCap;
        private PropertyManagerPageListbox _reviewLinksList;
        private PropertyManagerPageLabel _reviewJointsCap;
        private PropertyManagerPageListbox _reviewJointsList;

        public Sw2gzCreateRobotPmp(SldWorks swApp, ModelDoc2 modelDoc, Sw2gzDoc liveDoc, Action<Sw2gzDoc> onCommit)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _modelDoc = modelDoc ?? throw new ArgumentNullException(nameof(modelDoc));
            _liveDoc = liveDoc ?? throw new ArgumentNullException(nameof(liveDoc));
            _onCommit = onCommit ?? (d => { });

            _snapshot = Sw2gzDocSnapshot.Clone(liveDoc);

            int errs = 0;
            const string title = "Create Robot";
            long opts =
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_HandleKeystrokes;

            _page = (PropertyManagerPage2)swApp.CreatePropertyManagerPage(
                title, (int)opts, this, ref errs);

            if (_page == null)
            {
                logger.Error("Sw2gzCreateRobotPmp: CreatePropertyManagerPage failed (err=" + errs + ")");
                return;
            }

            try { _massProps = new SolidWorksMassProperties(swApp, (AssemblyDoc)modelDoc); }
            catch (Exception e) { logger.Warn("MassProperties init failed", e); }

            SeedLinksFromAssembly();
            BuildPage();
            ShowStep(StepLinks);
        }

        public void Show()
        {
            if (_page == null) { _swApp.SendMsgToUser("Could not open Create Robot."); return; }
            _page.Show2(0);
        }

        private void BuildPage()
        {
            const int visibleEnabled =
                (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            const int leftEdge =
                (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            const int indent =
                (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            int grpOptions =
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;

            var header = (PropertyManagerPageGroup)_page.AddGroupBox(HeaderGroupID, "Progress", grpOptions);
            _hdrLabel = (PropertyManagerPageLabel)header.AddControl2(
                HeaderLabelID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            // Nav bar — WinForms (PMP can't lay out horizontally). Embedded as
            // a WindowFromHandle so the two buttons sit on one row, centered.
            BuildNavBar();
            _navHandle = (PropertyManagerPageWindowFromHandle)header.AddControl2(
                NavBarHandleID,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Step navigation");
            _navHandle.Height = 58;
            _navHandle.SetWindowHandlex64(_navBar.Handle.ToInt64());

            _stepGroups = new PropertyManagerPageGroup[StepCount];
            for (int step = 0; step < StepCount; step++)
            {
                var stepGroup = (PropertyManagerPageGroup)_page.AddGroupBox(
                    StepGroupId(step), StepNames[step], grpOptions);
                _stepGroups[step] = stepGroup;
                switch (step)
                {
                    case StepLinks:  BuildLinksStep(stepGroup, leftEdge, indent, visibleEnabled); break;
                    case StepJoints: BuildJointsStep(stepGroup, leftEdge, indent, visibleEnabled); break;
                    case StepReview: BuildReviewStep(stepGroup, leftEdge, indent, visibleEnabled); break;
                }
            }
        }

        private PropertyManagerPageLabel AddFieldLabel(
            PropertyManagerPageGroup group, int id, string caption, int leftEdge, int labelOpts)
        {
            var label = (PropertyManagerPageLabel)group.AddControl2(
                id,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                caption, (short)leftEdge, labelOpts, "");
            label.Caption = caption;
            return label;
        }

        private void SeedLinksFromAssembly()
        {
            // Populate the assembly-component pool used by the validator —
            // independent of whether we seed the link tree or not.
            _allComponentIds.Clear();
            object[] comps = (object[])((AssemblyDoc)_modelDoc).GetComponents(true);
            if (comps != null)
            {
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (c.IsSuppressed()) continue;
                    _allComponentIds.Add(c.Name2);
                }
            }

            if (Robot.Links == null) Robot.Links = new List<LinkDef>();

            // If the user already configured a link tree (loaded from the SW
            // Doc attribute on a return visit), respect it verbatim — do NOT
            // wipe it by re-seeding from the assembly. The previous behaviour
            // clobbered the saved tree on every Create-Robot open.
            if (Robot.Links.Count > 0)
            {
                logger.Info("Sw2gzCreateRobotPmp: loaded existing tree, " +
                            Robot.Links.Count + " links");
                return;
            }

            // Empty doc → seed ONE empty root link the user will fill in.
            // Upstream's reference-CS workflow (D2+) drives joint origins
            // off Reference Coordinate Systems chosen per joint, so the
            // wizard no longer needs to auto-map every top-level component
            // into its own link on first open.
            Robot.Links.Add(new LinkDef
            {
                Name = "base_link",
                ComponentIds = new List<string>(),
                ParentName = string.Empty,
            });
            logger.Info("Sw2gzCreateRobotPmp: seeded empty base_link");
        }

        private void BuildLinksStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
        {
            int labelOpts = (int)swAddControlOptions_e.swControlOptions_Visible;

            int rows = Robot.Links != null ? Robot.Links.Count : 1;
            int treeHeight = System.Math.Min(260, System.Math.Max(90, rows * 20 + 30));
            _linkTree = new LinkTreeView { Height = treeHeight, Visible = true };
            _linkTree.ActiveLinkChanged += (s, l) =>
            {
                _activeLink = l;
                if (_activeLink != null) UpdateMassReadout(_activeLink);
                UpdateValidationLabel();
                UpdateLinkNameBox();
                if (!_suppressLinkSelectionLoad) LoadLinkSelection(_activeLink);
            };
            _linkTree.LinksChanged += (s, e) => UpdateValidationLabel();

            AddFieldLabel(group, LabelLinkInstrID,
                "Tree: click a link, then pick its parts in the viewport. " +
                "Drag to re-parent, F2 to rename, right-click to set base.",
                leftEdge, labelOpts);

            // WinForms button bar — PMP can't lay buttons horizontally, so we
            // embed a Panel via WindowFromHandle and center two Buttons inside.
            // Must come BEFORE the tree's WindowFromHandle (SW PMP drops controls
            // added after a WindowFromHandle in the same group).
            BuildLinkButtonBar();
            _btnBarHandle = (PropertyManagerPageWindowFromHandle)group.AddControl2(
                BtnBarHandleID,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Add or remove a link");
            _btnBarHandle.Height = 34;
            _btnBarHandle.SetWindowHandlex64(_linkBtnBar.Handle.ToInt64());

            _treeHandle = (PropertyManagerPageWindowFromHandle)group.AddControl2(
                TreeHandleID,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "Link tree", (short)leftEdge, visibleEnabled,
                "Drag to re-parent; F2 to rename; right-click to set the base link");
            _treeHandle.Height = treeHeight;
            _treeHandle.SetWindowHandlex64(_linkTree.Handle.ToInt64());

            _pickFunnel = (PropertyManagerPageSelectionbox)group.AddControl2(
                PickFunnelID,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Parts for the selected link", (short)leftEdge, visibleEnabled,
                "Pick components in the viewport — assigned to the selected link instantly");
            var filters = new swSelectType_e[]
            {
                swSelectType_e.swSelCOMPONENTS, swSelectType_e.swSelSOLIDBODIES,
            };
            _pickFunnel.SingleEntityOnly = false;
            _pickFunnel.AllowMultipleSelectOfSameEntity = false;
            _pickFunnel.Height = 24;
            _pickFunnel.Mark = LinkSelectionMark;
            _pickFunnel.SetSelectionFilters((object)filters);

            // Link name editor — live-rename the selected link. Children's
            // ParentName is rewritten too so the tree stays connected.
            AddFieldLabel(group, LabelLinkNameCapID, "Link name", leftEdge, labelOpts);
            _linkNameBox = (PropertyManagerPageTextbox)group.AddControl2(
                TextLinkNameID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)leftEdge, visibleEnabled,
                "Edit the active link's name (ROS-safe characters only)");
            _linkNameBox.Height = 20;

            _linkMass = AddFieldLabel(group, LabelLinkMassID, "", leftEdge, labelOpts);
            _linkValidation = AddFieldLabel(group, LabelLinkValidationID, "", leftEdge, labelOpts);

            _linkTree.SetLinks(Robot.Links);
            var roots = LinkHierarchy.Roots(Robot.Links);
            if (roots.Count > 0) _linkTree.SelectByLinkName(roots[0].Name);
            UpdateValidationLabel();
            UpdateLinkNameBox();
        }

        private void UpdateLinkNameBox()
        {
            if (_linkNameBox == null) return;
            _suppressLinkNameEvents = true;
            try { _linkNameBox.Text = _activeLink?.Name ?? ""; }
            finally { _suppressLinkNameEvents = false; }
        }

        private void RenameActiveLink(string proposed)
        {
            if (_activeLink == null) return;
            string sanitized = RosNameSanitizer.Sanitize(proposed ?? "").Value;
            if (string.IsNullOrEmpty(sanitized) || sanitized == _activeLink.Name) return;
            // Block dup: pick a unique variant rather than refuse silently.
            string unique = sanitized;
            int n = 2;
            while (Robot.Links.Any(l => l != _activeLink && l.Name == unique))
                unique = sanitized + "_" + n++;
            string old = _activeLink.Name;
            _activeLink.Name = unique;
            foreach (LinkDef l in Robot.Links)
                if (l.ParentName == old) l.ParentName = unique;
            // DO NOT call SetLinks here — it fires ActiveLinkChanged →
            // UpdateLinkNameBox resets textbox.Text mid-keystroke, cursor
            // jumps to 0, next char prepends, "link" becomes "knil". The
            // tree's node label is refreshed in-place instead.
            _linkTree.RefreshActiveNodeLabel();
            UpdateValidationLabel();
        }

        // Dark-theme palette — matches SW PMP's dark group surface so embedded
        // WinForms bars dissolve into the panel instead of showing as a slab.
        // Hardcoded (not SystemColors) because SW PMP is dark regardless of
        // the Windows theme.
        private static readonly System.Drawing.Color DarkBarBg     = System.Drawing.Color.FromArgb(53, 53, 53);
        private static readonly System.Drawing.Color DarkBtnBg     = System.Drawing.Color.FromArgb(70, 70, 72);
        private static readonly System.Drawing.Color DarkBtnHover  = System.Drawing.Color.FromArgb(95, 95, 98);
        private static readonly System.Drawing.Color DarkFg        = System.Drawing.Color.FromArgb(220, 220, 220);
        private static readonly System.Drawing.Color DarkBtnBorder = System.Drawing.Color.FromArgb(100, 100, 102);

        private static System.Windows.Forms.Panel NewBar(int width, int height)
        {
            return new System.Windows.Forms.Panel
            {
                Width = width,
                Height = height,
                BackColor = DarkBarBg,
            };
        }

        private static System.Windows.Forms.Button NewBarButton(string text, int width)
        {
            var b = new System.Windows.Forms.Button
            {
                Text = text,
                Width = width,
                Height = 26,
                Top = 3,
                FlatStyle = System.Windows.Forms.FlatStyle.Flat,
                BackColor = DarkBtnBg,
                ForeColor = DarkFg,
                UseVisualStyleBackColor = false,
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

        private void BuildLinkButtonBar()
        {
            _linkBtnBar    = NewBar(260, 32);
            _addLinkBtn    = NewBarButton("Add link",    90);
            _removeLinkBtn = NewBarButton("Remove link", 90);
            _addLinkBtn.Click    += (s, e) => AddLink();
            _removeLinkBtn.Click += (s, e) => RemoveLink();
            _linkBtnBar.Controls.Add(_addLinkBtn);
            _linkBtnBar.Controls.Add(_removeLinkBtn);
            _linkBtnBar.Resize += (s, e) => CenterRow(_linkBtnBar, _addLinkBtn, _removeLinkBtn);
            CenterRow(_linkBtnBar, _addLinkBtn, _removeLinkBtn);
        }

        private void BuildNavBar()
        {
            _navBar  = NewBar(260, 56);
            _stepIndicator = new System.Windows.Forms.Label
            {
                AutoSize = false,
                Width = 240,
                Height = 18,
                Top = 2,
                Left = 10,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(48, 48, 48),
                Font = new System.Drawing.Font("Segoe UI", 8.25f, System.Drawing.FontStyle.Bold),
                Text = ""
            };
            _navBar.Controls.Add(_stepIndicator);
            _backBtn = NewBarButton("◀", 50);
            _nextBtn = NewBarButton("▶", 50);
            _backBtn.Top = 24;
            _nextBtn.Top = 24;
            // Defer GoBack/GoNext to the next message-loop tick via BeginInvoke.
            // The click handler runs INSIDE SW's PMP-handler reentrancy frame
            // (WinForms button hosted in an embedded WindowFromHandle); mutating
            // PMP COM controls (e.g. _hdrLabel.Caption) from that frame crashes
            // SW's PMP renderer. BeginInvoke escapes the click-handler stack so
            // the COM mutations run on a clean callstack on the WinForms loop.
            _backBtn.Click += (s, e) =>
            {
                logger.Info("Sw2gzCreateRobotPmp NAV: BackBtn.Click step=" + _currentStep);
                _navBar.BeginInvoke((Action)(() =>
                {
                    logger.Info("Sw2gzCreateRobotPmp NAV: BackBtn.BeginInvoke fire step=" + _currentStep);
                    try { GoBack(); }
                    catch (Exception ex) { logger.Error("GoBack threw", ex); }
                }));
            };
            _nextBtn.Click += (s, e) =>
            {
                logger.Info("Sw2gzCreateRobotPmp NAV: NextBtn.Click step=" + _currentStep +
                            " nextBtn.Enabled=" + _nextBtn.Enabled +
                            " text=" + _nextBtn.Text);
                _navBar.BeginInvoke((Action)(() =>
                {
                    logger.Info("Sw2gzCreateRobotPmp NAV: NextBtn.BeginInvoke fire step=" + _currentStep);
                    try { GoNext(); }
                    catch (Exception ex) { logger.Error("GoNext threw", ex); }
                }));
            };
            _navBar.Controls.Add(_backBtn);
            _navBar.Controls.Add(_nextBtn);
            _navBar.Resize += (s, e) => CenterRow(_navBar, _backBtn, _nextBtn);
            CenterRow(_navBar, _backBtn, _nextBtn);
        }

        private void OnFunnelChanged()
        {
            if (_currentStep != StepLinks) return;
            if (_activeLink == null || _linkTree == null) return;
            List<string> box = ReadSelectionBoxNames();
            if (SameSet(box, _activeLink.ComponentIds)) return;

            foreach (string id in box)
                foreach (LinkDef l in Robot.Links)
                    if (l != _activeLink) l.ComponentIds.Remove(id);
            _activeLink.ComponentIds = box;

            _suppressLinkSelectionLoad = true;
            try { _linkTree.Rebuild(); }
            finally { _suppressLinkSelectionLoad = false; }

            UpdateMassReadout(_activeLink);
            UpdateValidationLabel();
        }

        private void LoadLinkSelection(LinkDef link)
        {
            if (link == null) return;
            _modelDoc.ClearSelection2(true);
            ISelectionMgr selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
            SelectData sd = selMgr.CreateSelectData();
            sd.Mark = LinkSelectionMark;
            object[] comps = (object[])((AssemblyDoc)_modelDoc).GetComponents(true);
            if (comps != null)
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (link.ComponentIds.Contains(c.Name2)) c.Select4(true, sd, false);
                }
            if (_pickFunnel != null) _pickFunnel.SetSelectionFocus();
        }

        private static bool SameSet(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            var set = new HashSet<string>(a);
            foreach (string x in b) if (!set.Contains(x)) return false;
            return true;
        }

        private void AddLink()
        {
            var roots = LinkHierarchy.Roots(Robot.Links);
            string parent = _activeLink?.Name ?? (roots.Count > 0 ? roots[0].Name : "");
            var link = new LinkDef { Name = UniqueLinkName(), ParentName = parent };
            Robot.Links.Add(link);
            _linkTree.SetLinks(Robot.Links);
            _linkTree.SelectByLinkName(link.Name);
            UpdateValidationLabel();
        }

        private void RemoveLink()
        {
            if (_activeLink == null || Robot.Links.Count <= 1) return;
            string removed = _activeLink.Name, parent = _activeLink.ParentName ?? "";
            foreach (LinkDef l in Robot.Links)
                if (l.ParentName == removed) l.ParentName = parent;
            Robot.Links.Remove(_activeLink);
            if (LinkHierarchy.Roots(Robot.Links).Count == 0 && Robot.Links.Count > 0)
                Robot.Links[0].ParentName = "";
            _activeLink = null;
            _linkTree.SetLinks(Robot.Links);
            UpdateValidationLabel();
        }

        private string UniqueLinkName()
        {
            int n = Robot.Links.Count + 1;
            while (true)
            {
                string candidate = RosNameSanitizer.Sanitize("link_" + n).Value;
                bool taken = false;
                foreach (LinkDef l in Robot.Links) if (l.Name == candidate) { taken = true; break; }
                if (!taken) return candidate;
                n++;
            }
        }

        private void UpdateMassReadout(LinkDef link)
        {
            if (_linkMass == null || _massProps == null) return;
            double total = 0; bool missing = false;
            // ComponentIds are Name2 strings; SolidWorksMassProperties.FindComponent
            // now matches Name2, so pass id directly (no PathName conversion).
            foreach (string id in link.ComponentIds)
            {
                try { total += _massProps.Get(id).Mass; }
                catch (Exception) { missing = true; }
            }
            string s = link.ComponentIds.Count + " component(s), mass " + total.ToString("0.###") + " kg";
            if (missing) s += " (set material on all parts)";
            _linkMass.Caption = s;
        }

        private void UpdateValidationLabel()
        {
            if (_linkValidation == null) return;
            List<string> issues = LinkDefValidator.Validate(Robot.Links, _allComponentIds);
            _linkValidation.Caption = issues.Count == 0
                ? "All components assigned."
                : issues.Count + " issue(s): " + issues[0];
        }

        private List<string> ReadSelectionBoxNames()
        {
            var names = new List<string>();
            ISelectionMgr selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
            if (selMgr == null) return names;
            int count = selMgr.GetSelectedObjectCount2(LinkSelectionMark);
            for (int i = 1; i <= count; i++)
            {
                object selObj = selMgr.GetSelectedObject6(i, LinkSelectionMark);
                int selType = selMgr.GetSelectedObjectType3(i, LinkSelectionMark);
                string name = DescribeSelection(selObj, selType);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names;
        }

        private static string DescribeSelection(object selObj, int selType)
        {
            switch ((swSelectType_e)selType)
            {
                case swSelectType_e.swSelSOLIDBODIES:
                case swSelectType_e.swSelSURFACEBODIES:
                    return selObj is Body2 body ? body.Name : null;
                case swSelectType_e.swSelCOMPONENTS:
                    return selObj is Component2 comp ? comp.Name2 : null;
                default:
                    return null;
            }
        }

        private void BuildJointsStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
        {
            int labelOpts = (int)swAddControlOptions_e.swControlOptions_Visible;

            AddFieldLabel(group, LabelJointInstrID,
                "Pick a joint, pick the mate that defines it, click Apply mate. " +
                "The mate's cylindrical face becomes the joint axis + origin.",
                leftEdge, labelOpts);

            _jointsListBox = (PropertyManagerPageListbox)group.AddControl2(
                ListJointsID,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "", (short)leftEdge, visibleEnabled, "Joints (from the link tree) — select one");
            ((IPropertyManagerPageListbox)_jointsListBox).Height = 96;

            _matesCap = AddFieldLabel(group, LabelMatesCapID,
                "— Mates between parent and child —", leftEdge, labelOpts);

            _matesListBox = (PropertyManagerPageListbox)group.AddControl2(
                ListMatesID,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "", (short)leftEdge, visibleEnabled, "Mates spanning the active joint's parent and child — pick one and click Apply mate");
            ((IPropertyManagerPageListbox)_matesListBox).Height = 80;

            // Apply-mate button bar — WinForms-embedded (dark theme). Must be
            // added AFTER the listboxes but BEFORE the trailing detail labels:
            // SW PMP drops controls added after a WindowFromHandle in the same
            // group, so any labels after the bar still render fine — they're
            // added below.
            BuildJointButtonBar();
            _jointBarHandle = (PropertyManagerPageWindowFromHandle)group.AddControl2(
                JointBarHandleID,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled,
                "Apply the selected mate's geometry to the active joint");
            _jointBarHandle.Height = 34;
            _jointBarHandle.SetWindowHandlex64(_jointBtnBar.Handle.ToInt64());

            AddFieldLabel(group, LabelDetailCapID, "— Selected joint —", leftEdge, labelOpts);
            _detailLinks  = AddFieldLabel(group, LabelDetailLinksID, "", leftEdge, labelOpts);
            _detailMate   = AddFieldLabel(group, LabelDetailMateID, "", leftEdge, labelOpts);
            _detailType   = AddFieldLabel(group, LabelDetailTypeID, "", leftEdge, labelOpts);
            _detailLimits = AddFieldLabel(group, LabelDetailLimitsID, "", leftEdge, labelOpts);
            // Side-by-side SW and ROS frame readout for the active joint's
            // axis + origin. ROS row applies the SwToRosRotation matrix built
            // from the export config's (up, forward) — defaults to +Y up / +Z
            // forward (the stock SW template convention).
            _detailFrameSw  = AddFieldLabel(group, LabelDetailFrameSwID,  "", leftEdge, labelOpts);
            _detailFrameRos = AddFieldLabel(group, LabelDetailFrameRosID, "", leftEdge, labelOpts);
        }

        private void BuildJointButtonBar()
        {
            _jointBtnBar = NewBar(260, 32);
            _applyMateBtn = NewBarButton("Apply mate", 110);
            _applyMateBtn.Click += (s, e) =>
            {
                try { ApplySelectedMate(); }
                catch (Exception ex) { logger.Error("Apply mate click threw", ex); }
            };
            _jointBtnBar.Controls.Add(_applyMateBtn);
            _jointBtnBar.Resize += (s, e) => CenterRow(_jointBtnBar, _applyMateBtn);
            CenterRow(_jointBtnBar, _applyMateBtn);
        }

        private void EnterJointsStep()
        {
            logger.Info("Sw2gzCreateRobotPmp.EnterJointsStep ENTER" +
                        " linksCount=" + (Robot?.Links?.Count ?? -1) +
                        " preSyncJointsCount=" + (Robot?.Joints?.Count ?? -1) +
                        " _activeJointIndex=" + _activeJointIndex);
            // Seed JointDef[] from the link-edge graph but DO NOT auto-detect
            // mates — the user picks each joint's mate explicitly via the
            // mates listbox + Apply-mate button below.
            Robot.Joints = JointSeeder.Sync(Robot.Links, Robot.Joints);
            if (_activeJointIndex < 0 && Robot.Joints.Count > 0) _activeJointIndex = 0;
            PopulateJointList();
            RepopulateMatesForActiveJoint();
            logger.Info("Sw2gzCreateRobotPmp.EnterJointsStep EXIT" +
                        " postSyncJointsCount=" + Robot.Joints.Count +
                        " _activeJointIndex=" + _activeJointIndex +
                        " _activeMateNames=" + (_activeMateNames?.Count ?? -1));
        }

        // Populate _matesListBox with the mates spanning the active joint's
        // parent and child links. Pre-selects the JointDef.MateName if it's
        // still in the list (so a previously-assigned mate stays visible on
        // wizard reopen).
        private void RepopulateMatesForActiveJoint()
        {
            _activeMateNames = new List<string>();
            _activeMateIndex = -1;
            if (_matesListBox == null) return;
            _matesListBox.Clear();

            JointDef j = ActiveJoint();
            if (j == null) return;
            LinkDef p = Robot.Links?.FirstOrDefault(l => l.Name == j.ParentLink);
            LinkDef c = Robot.Links?.FirstOrDefault(l => l.Name == j.ChildLink);
            if (p == null || c == null) return;

            AutoJointResolver resolver;
            try { resolver = new AutoJointResolver((AssemblyDoc)_modelDoc); }
            catch (Exception e) { logger.Warn("AutoJointResolver init failed", e); return; }

            IReadOnlyList<string> mates;
            try { mates = resolver.ListMateNamesBetween(p.ComponentIds, c.ComponentIds); }
            catch (Exception e)
            {
                logger.Warn("ListMateNamesBetween threw for " + j.Name, e);
                mates = new List<string>();
            }

            foreach (string m in mates)
            {
                _activeMateNames.Add(m);
                _matesListBox.AddItems(m);
            }

            if (_activeMateNames.Count > 0)
            {
                int preIdx = !string.IsNullOrEmpty(j.MateName)
                    ? _activeMateNames.IndexOf(j.MateName) : -1;
                _activeMateIndex = preIdx >= 0 ? preIdx : 0;
                _matesListBox.CurrentSelection = (short)_activeMateIndex;
            }
        }

        // Click handler for the Apply-mate button. Reads the selected mate
        // name out of _activeMateNames, runs AutoJointResolver.ResolveFromMateName
        // for the active joint's (parent, child) pair, and writes the resulting
        // type / axis / origin / limits onto the JointDef. Demotes to Fixed
        // when the cylinder extract fails (existing safety net inherited from
        // TryResolveMate).
        private void ApplySelectedMate()
        {
            JointDef j = ActiveJoint();
            if (j == null) return;
            if (_activeMateIndex < 0 || _activeMateIndex >= _activeMateNames.Count) return;
            string mateName = _activeMateNames[_activeMateIndex];

            LinkDef p = Robot.Links?.FirstOrDefault(l => l.Name == j.ParentLink);
            LinkDef c = Robot.Links?.FirstOrDefault(l => l.Name == j.ChildLink);
            if (p == null || c == null) return;

            AutoJointResolver resolver;
            try { resolver = new AutoJointResolver((AssemblyDoc)_modelDoc); }
            catch (Exception e) { logger.Warn("AutoJointResolver init failed", e); return; }

            AutoJointResolver.Resolved r;
            try { r = resolver.ResolveFromMateName(mateName, p.ComponentIds, c.ComponentIds); }
            catch (Exception e)
            {
                logger.Warn("ResolveFromMateName threw for " + j.Name + " / " + mateName, e);
                return;
            }

            if (r != null && r.Found)
            {
                j.Type       = MapMateKindToJointType(r.Kind);
                j.MateName   = r.MateName;
                j.SetAxis(r.AxisAssembly);
                j.LimitLower = r.LimitLower;
                j.LimitUpper = r.LimitUpper;
                if (r.OriginAssembly is Vector3 o)
                {
                    j.SetOrigin(o);
                    // Keep legacy MatePoint in sync so code paths that still
                    // read HasMatePoint see the same point.
                    j.SetMatePoint(o);

                    // Create a visible SolidWorks Reference Axis feature in
                    // the FeatureManager tree so the user sees the joint
                    // axis as a real SW feature, not just hidden numbers in
                    // the wizard. Failure is non-fatal — the JointDef-side
                    // axis numbers above are already populated.
                    try
                    {
                        var creator = new SwRefAxisCreator(_modelDoc);
                        string axisName = creator.CreateFromMate(
                            j.MateName, j.Name, p.ComponentIds, c.ComponentIds);
                        if (!string.IsNullOrEmpty(axisName))
                            j.RefAxisName = axisName;
                    }
                    catch (Exception ex)
                    {
                        logger.Warn("Reference axis creation failed for joint " + j.Name, ex);
                    }
                }
                else
                {
                    j.ClearOrigin();
                    j.ClearMatePoint();
                }
            }
            else
            {
                // Selected mate didn't classify (didn't span the pair, etc.).
                // Leave the JointDef alone except for the recorded mate name,
                // so the user can see what they tried.
                j.MateName = mateName;
            }

            // Refresh the joints listbox row caption (it shows mate name) and
            // the detail labels.
            PopulateJointList();
            UpdateJointDetails();
        }

        private static UrdfJointType MapMateKindToJointType(MateKind k)
        {
            switch (k)
            {
                case MateKind.Revolute:   return UrdfJointType.Revolute;
                case MateKind.Continuous: return UrdfJointType.Continuous;
                case MateKind.Prismatic:  return UrdfJointType.Prismatic;
                case MateKind.Planar:     return UrdfJointType.Planar;
                case MateKind.Floating:   return UrdfJointType.Floating;
                default:                  return UrdfJointType.Fixed;
            }
        }

        private void PopulateJointList()
        {
            if (_jointsListBox == null) return;
            _jointsListBox.Clear();
            foreach (JointDef j in Robot.Joints)
            {
                string tag = string.IsNullOrEmpty(j.MateName) ? "(no mate)" : j.MateName;
                _jointsListBox.AddItems(j.Name + "   —   " + tag);
            }
            if (Robot.Joints.Count > 0)
            {
                if (_activeJointIndex < 0 || _activeJointIndex >= Robot.Joints.Count) _activeJointIndex = 0;
                _jointsListBox.CurrentSelection = (short)_activeJointIndex;
            }
            UpdateJointDetails();
        }

        private JointDef ActiveJoint() =>
            _activeJointIndex >= 0 && _activeJointIndex < Robot.Joints.Count
                ? Robot.Joints[_activeJointIndex] : null;

        // D3 — joint detail labels read solely from JointDef. HasOrigin
        // distinguishes auto-detected joints (axis + origin + mate source)
        // from un-detected joints (NOT DETECTED + remediation hint).
        private void UpdateJointDetails()
        {
            JointDef j = ActiveJoint();
            bool limited = j != null &&
                (j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Prismatic);

            if (_detailLinks != null)
                _detailLinks.Caption = j == null
                    ? "No joint selected."
                    : "Links: " + j.ParentLink + " → " + j.ChildLink;
            if (_detailType != null)
                _detailType.Caption = j == null ? "" : "Type: " + j.Type;

            if (j == null)
            {
                if (_detailMate     != null) _detailMate.Caption     = "";
                if (_detailLimits   != null) _detailLimits.Caption   = "";
                if (_detailFrameSw  != null) _detailFrameSw.Caption  = "";
                if (_detailFrameRos != null) _detailFrameRos.Caption = "";
                return;
            }

            if (j.HasOrigin)
            {
                if (_detailMate != null)
                    _detailMate.Caption = "Source: mate '" +
                        (string.IsNullOrEmpty(j.MateName) ? "(unnamed)" : j.MateName) + "'";

                if (_detailLimits != null)
                {
                    _detailLimits.Caption =
                        (limited && j.LimitLower.HasValue && j.LimitUpper.HasValue)
                            ? "Limits: [" + Fmt(j.LimitLower) + ", " + Fmt(j.LimitUpper) + "]"
                            : "";
                }

                // D2 — apply the export config's coord-convention rotation to
                // show the user how the auto-detected axis/origin lands in the
                // ROS world frame. Defaults match Sw2gzExportConfig: +Y up,
                // +Z forward (the stock SW assembly template).
                AxisDirection up      = AxisDirection.PlusY;
                AxisDirection forward = AxisDirection.PlusZ;
                var R = SwToRosRotation.Build(up, forward);
                (double ax, double ay, double az) = R.Mul(j.AxisX,   j.AxisY,   j.AxisZ);
                (double ox, double oy, double oz) = R.Mul(j.OriginX, j.OriginY, j.OriginZ);

                if (_detailFrameSw != null)
                    _detailFrameSw.Caption = string.Format(
                        CultureInfo.InvariantCulture,
                        "SW:  axis=({0:F3}, {1:F3}, {2:F3})  origin=({3:F3}, {4:F3}, {5:F3}) m",
                        j.AxisX, j.AxisY, j.AxisZ,
                        j.OriginX, j.OriginY, j.OriginZ);
                if (_detailFrameRos != null)
                    _detailFrameRos.Caption = string.Format(
                        CultureInfo.InvariantCulture,
                        "ROS: axis=({0:F3}, {1:F3}, {2:F3})  origin=({3:F3}, {4:F3}, {5:F3}) m",
                        ax, ay, az, ox, oy, oz);
            }
            else
            {
                if (_detailMate != null) _detailMate.Caption = "NOT ASSIGNED";
                if (_detailLimits != null)
                    _detailLimits.Caption =
                        "Pick a mate from the list above and click Apply mate.";
                if (_detailFrameSw  != null) _detailFrameSw.Caption  = "";
                if (_detailFrameRos != null) _detailFrameRos.Caption = "";
            }
        }

        private static string Fmt(double? v) =>
            v.HasValue ? v.Value.ToString("0.###", CultureInfo.InvariantCulture) : "–";

        private void BuildReviewStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
        {
            int labelOpts = (int)swAddControlOptions_e.swControlOptions_Visible;
            AddFieldLabel(group, LabelReviewInstrID,
                "Review, then Finish to save to the SW2GZ Doc (v1) attribute.",
                leftEdge, labelOpts);

            _reviewMode = AddFieldLabel(group, LabelReviewModeID, "", leftEdge, labelOpts);
            _reviewLinksCap = AddFieldLabel(group, LabelReviewLinksCapID, "", leftEdge, labelOpts);
            _reviewLinksList = (PropertyManagerPageListbox)group.AddControl2(
                ListReviewLinksID,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "", (short)leftEdge, visibleEnabled, "Links");
            ((IPropertyManagerPageListbox)_reviewLinksList).Height = 76;

            _reviewJointsCap = AddFieldLabel(group, LabelReviewJointsCapID, "", leftEdge, labelOpts);
            _reviewJointsList = (PropertyManagerPageListbox)group.AddControl2(
                ListReviewJointsID,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "", (short)leftEdge, visibleEnabled, "Joints");
            ((IPropertyManagerPageListbox)_reviewJointsList).Height = 96;
        }

        private void EnterReviewStep()
        {
            if (_reviewMode == null) return;
            _reviewMode.Caption = "Mode:  Robot package (URDF/Xacro)";

            var links = Robot.Links ?? new List<LinkDef>();
            _reviewLinksCap.Caption = "Links  (" + links.Count + ")";
            _reviewLinksList.Clear();
            foreach (LinkDef l in links)
            {
                string rel = string.IsNullOrEmpty(l.ParentName) ? "base" : "← " + l.ParentName;
                int parts = l.ComponentIds != null ? l.ComponentIds.Count : 0;
                _reviewLinksList.AddItems(l.Name + "    " + rel + "    ·" + parts + "p");
            }

            var joints = Robot.Joints ?? new List<JointDef>();
            _reviewJointsCap.Caption = "Joints  (" + joints.Count + ")";
            _reviewJointsList.Clear();
            foreach (JointDef j in joints)
            {
                string mate = string.IsNullOrEmpty(j.MateName) ? "no mate" : j.MateName;
                string lim = (j.LimitLower.HasValue || j.LimitUpper.HasValue)
                    ? "  [" + Fmt(j.LimitLower) + "," + Fmt(j.LimitUpper) + "]" : "";
                _reviewJointsList.AddItems(j.Name + "    " + j.Type + "    " + mate + lim);
            }
        }

        private void ShowStep(int step)
        {
            int requested = step;
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;

            logger.Info("Sw2gzCreateRobotPmp.ShowStep ENTER requested=" + requested +
                        " clamped=" + step + " from _currentStep=" + _currentStep);

            if (_currentStep == StepLinks && step != StepLinks)
            {
                try
                {
                    if (_modelDoc != null) _modelDoc.ClearSelection2(true);
                    _activeLink = null;
                }
                catch (Exception ex) { logger.Warn("Links step exit: ClearSelection2 failed", ex); }
            }

            _currentStep = step;

            for (int i = 0; i < StepCount; i++)
            {
                try { _stepGroups[i].Visible = (i == _currentStep); }
                catch (Exception ex) { logger.Error("Set group[" + i + "].Visible failed", ex); }
            }
            // Read-back group visibility so we see what SW actually accepted.
            for (int i = 0; i < StepCount; i++)
            {
                try
                {
                    bool v = _stepGroups[i].Visible;
                    logger.Info("Sw2gzCreateRobotPmp.ShowStep readback group[" + i + "].Visible=" + v +
                                " (expected " + (i == _currentStep) + ")");
                }
                catch (Exception ex) { logger.Warn("Readback group[" + i + "].Visible threw", ex); }
            }

            // Step indicator is a WinForms label inside _navBar — no COM marshal,
            // no PMP RCW touched. The previous PMP-label Caption setter
            // intermittently AV'd inside mscorlib (CSE) when invoked from a
            // post-flip deferred callback; the WinForms label has no such issue.
            logger.Info("Sw2gzCreateRobotPmp.ShowStep MICRO before _stepIndicator.Text");
            try
            {
                _stepIndicator.Text = "Step " + (_currentStep + 1) + " of " + StepCount +
                                      " — " + StepNames[_currentStep];
            }
            catch (Exception ex) { logger.Error("ShowStep MICRO _stepIndicator.Text threw", ex); }
            logger.Info("Sw2gzCreateRobotPmp.ShowStep MICRO after _stepIndicator.Text");
            logger.Info("Sw2gzCreateRobotPmp.ShowStep MICRO before _backBtn.Enabled");
            try { _backBtn.Enabled = _currentStep > 0; }
            catch (Exception ex) { logger.Error("ShowStep MICRO _backBtn.Enabled threw", ex); }
            logger.Info("Sw2gzCreateRobotPmp.ShowStep MICRO before _nextBtn.Enabled");
            try { _nextBtn.Enabled = true; }
            catch (Exception ex) { logger.Error("ShowStep MICRO _nextBtn.Enabled threw", ex); }
            logger.Info("Sw2gzCreateRobotPmp.ShowStep MICRO before _nextBtn.Text");
            try { _nextBtn.Text = (_currentStep == StepCount - 1) ? "Finish" : "▶"; }
            catch (Exception ex) { logger.Error("ShowStep MICRO _nextBtn.Text threw", ex); }
            logger.Info("Sw2gzCreateRobotPmp.ShowStep MICRO after button block");

            logger.Info("Sw2gzCreateRobotPmp.ShowStep -> step=" + _currentStep +
                        " (" + StepNames[_currentStep] + ")" +
                        " nextBtn.Enabled=" + _nextBtn.Enabled +
                        " nextBtn.Text=" + _nextBtn.Text +
                        " backBtn.Enabled=" + _backBtn.Enabled);

            if (_currentStep == StepJoints) EnterJointsStep();
            else if (_currentStep == StepReview) EnterReviewStep();

            logger.Info("Sw2gzCreateRobotPmp.ShowStep EXIT step=" + _currentStep);
        }

        private void GoBack()
        {
            if (_currentStep > 0) ShowStep(_currentStep - 1);
        }

        private void GoNext()
        {
            logger.Info("Sw2gzCreateRobotPmp.GoNext ENTER step=" + _currentStep +
                        " linksCount=" + (Robot?.Links?.Count ?? -1) +
                        " jointsCount=" + (Robot?.Joints?.Count ?? -1));
            if (_currentStep == StepLinks)
            {
                List<string> issues = LinkDefValidator.Validate(Robot.Links, _allComponentIds);
                logger.Info("Sw2gzCreateRobotPmp.GoNext Links-validate issues=" + issues.Count);
                if (issues.Count > 0)
                {
                    _swApp.SendMsgToUser("Resolve link issues before continuing:\n• " +
                        string.Join("\n• ", issues.ToArray()));
                    logger.Info("Sw2gzCreateRobotPmp.GoNext BLOCKED by Links-validate");
                    return;
                }
            }
            if (_currentStep < StepCount - 1)
            {
                logger.Info("Sw2gzCreateRobotPmp.GoNext -> ShowStep(" + (_currentStep + 1) + ")");
                ShowStep(_currentStep + 1);
            }
            else
            {
                logger.Info("Sw2gzCreateRobotPmp.GoNext -> Finish (page.Close)");
                _okay = true;
                _page.Close(true);
            }
            logger.Info("Sw2gzCreateRobotPmp.GoNext EXIT step=" + _currentStep);
        }

        void IPropertyManagerPage2Handler9.AfterActivation()
        {
            logger.Info("Sw2gzCreateRobotPmp.AfterActivation ENTER step=" + _currentStep);
            ShowStep(_currentStep);
            if (_currentStep == StepLinks && _pickFunnel != null) _pickFunnel.SetSelectionFocus();
            logger.Info("Sw2gzCreateRobotPmp.AfterActivation EXIT step=" + _currentStep);
        }

        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            // Log every button press so the "Next doesn't work" bug is visible
            // in sw2gz.log if it ever recurs — the click either arrives here or
            // it's being swallowed by SW upstream (focus on a listbox etc.).
            logger.Info("Sw2gzCreateRobotPmp.OnButtonPress id=" + Id +
                        " step=" + _currentStep);
            // All buttons (nav + add/remove) are WinForms-embedded — they fire
            // via Click handlers directly. Nothing to dispatch from PMP.
            try
            {
            }
            catch (Exception e)
            {
                logger.Error("OnButtonPress " + Id + " threw", e);
                MessageBox.Show("Create Robot panel error:\n" + e.Message);
            }
        }

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

        void IPropertyManagerPage2Handler9.AfterClose()
        {
            if (_okay && _liveDoc != null) _onCommit(_liveDoc);
        }

        bool IPropertyManagerPage2Handler9.OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText)
        {
            if (Id != PickFunnelID) return true;
            switch ((swSelectType_e)SelType)
            {
                case swSelectType_e.swSelCOMPONENTS:
                case swSelectType_e.swSelSOLIDBODIES:
                    return true;
                default:
                    ItemText = "Only components or solid bodies can be assigned to a link.";
                    return false;
            }
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count)
        {
            if (Id == PickFunnelID) OnFunnelChanged();
        }

        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item)
        {
            if (Id == ListJointsID)
            {
                _activeJointIndex = Item;
                RepopulateMatesForActiveJoint();
                UpdateJointDetails();
            }
            else if (Id == ListMatesID)
            {
                _activeMateIndex = Item;
            }
        }

        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id) { }
        void IPropertyManagerPage2Handler9.OnLostFocus(int Id) { }
        bool IPropertyManagerPage2Handler9.OnHelp() => true;
        bool IPropertyManagerPage2Handler9.OnNextPage() => true;
        bool IPropertyManagerPage2Handler9.OnPreviousPage() => true;
        bool IPropertyManagerPage2Handler9.OnPreview() => true;
        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id) => true;
        bool IPropertyManagerPage2Handler9.OnKeystroke(int Wparam, int Message, int Lparam, int Id) => false;
        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text)
        {
            if (_suppressLinkNameEvents) return;
            if (Id == TextLinkNameID) RenameActiveLink(Text);
        }
        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id) { }
        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked) { }
        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text) { }
        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item) { }
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
