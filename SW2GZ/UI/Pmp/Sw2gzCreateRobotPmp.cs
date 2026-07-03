/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateRobotPmp — the "Create Robot" PropertyManagerPage opened from the
mode-specific Create button when the active mode is Robot. Manual, URDF-
hierarchy-shaped link building: no auto-seed, the user picks the mesh
component(s) for each link (parts or sub-assemblies), names it, and picks
its parent — the first link added is always the root, forced to
"base_link" per ROS2/REP-105 convention. Every non-root link gets an
implicit Fixed joint to its chosen parent (joint-type refinement is a later
increment, see agent-progress/progress.md). Mirrors Sw2gzCreateWorldPmp's
chrome exactly: a WinForms nav bar (Back/Next + step indicator, dark theme)
embedded via WindowFromHandle, and a WinForms action-button bar per step.
PMP swControlType_Button controls are avoided entirely — clicking one and
mutating PMP state from inside OnButtonPress corrupts SW's PMP renderer.
Nav clicks defer via BeginInvoke so the group-visibility flip runs off the
click-handler reentrancy frame.

Steps map to Sw2gzDoc.Robot:
    0 — Links    (pick mesh -> name -> parent -> Add; first Add = base_link;
                  Joints re-synced (merge-preserve) from each link's
                  ParentName via JointDefReconciler)
    1 — Joints   (one row per non-root link; edit type/axis/limit for the
                  selected row)
    2 — Review   (counts; Next caption flips to "Finish")
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
using SW2GZ.SwSurface;
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
        private readonly Action _onClosed;
        private readonly PropertyManagerPage2 _page;

        private const int StepLinks  = 0;
        private const int StepJoints = 1;
        private const int StepReview = 2;
        private static readonly string[] StepNames = { "Links", "Joints", "Review" };
        private const int StepCount = 3;

        private bool _okay;
        private int _currentStep = StepLinks;

        // Header (Progress) group + nav bar.
        private const int IdHeaderGroup = 1;
        private const int IdHeaderLabel = 2;
        private const int IdNavBar      = 3;

        // Step groups.
        private const int IdLinksGroup     = 10;
        private const int IdMeshLabel      = 11;
        private const int IdMeshPicker     = 12;
        private const int IdLinkNameLabel  = 13;
        private const int IdLinkNameBox    = 14;
        private const int IdLinksBtnBar    = 15;
        private const int IdSelectedInfo   = 16;
        private const int IdLinksListLabel = 17;
        private const int IdLinksTree      = 18;

        private const int IdJointsGroup        = 20;
        private const int IdJointsDescr        = 21;
        private const int IdJointsList         = 22;
        private const int IdJointNameLabel     = 23;
        private const int IdJointNameBox       = 24;
        private const int IdJointTypeLabel     = 25;
        private const int IdJointTypeCombo     = 26;
        private const int IdJointAxisLabel     = 27;
        private const int IdJointAxisXBox      = 28;
        private const int IdJointAxisYBox      = 29;
        private const int IdJointAxisZBox      = 30;
        private const int IdJointLimitLabel    = 31;
        private const int IdJointLimitLowerBox = 32;
        private const int IdJointLimitUpperBox = 33;

        private const int MeshSelectionMark = 0x4C0;
        private const string LinkNamePlaceholder = "e.g. wheel_link";

        private static readonly string[] JointTypeLabels =
            { "Fixed", "Revolute", "Continuous", "Prismatic" };
        private static readonly UrdfJointType[] JointTypeOptions =
            { UrdfJointType.Fixed, UrdfJointType.Revolute, UrdfJointType.Continuous, UrdfJointType.Prismatic };

        private const int IdReviewGroup       = 40;
        private const int IdReviewDescr       = 41;
        private const int IdReviewLinksLabel  = 42;
        private const int IdReviewBaseLabel   = 43;
        private const int IdReviewJointsLabel = 44;

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
        private System.Windows.Forms.Button _addLinkBtn;
        private System.Windows.Forms.Button _removeLinkBtn;
        private System.Windows.Forms.Button _clearLinksBtn;

        // PMP-native controls.
        private PropertyManagerPageSelectionbox _meshPicker;
        private PropertyManagerPageTextbox _linkNameBox;
        private PropertyManagerPageLabel _selectedInfoLabel;
        private PropertyManagerPageLabel _reviewLinksLabel;
        private PropertyManagerPageLabel _reviewBaseLabel;
        private PropertyManagerPageLabel _reviewJointsLabel;

        private PropertyManagerPageListbox _jointsList;
        private PropertyManagerPageTextbox _jointNameBox;
        private PropertyManagerPageCombobox _jointTypeCombo;
        private PropertyManagerPageLabel _jointAxisLabel;
        private PropertyManagerPageNumberbox _jointAxisXBox;
        private PropertyManagerPageNumberbox _jointAxisYBox;
        private PropertyManagerPageNumberbox _jointAxisZBox;
        private PropertyManagerPageLabel _jointLimitLabel;
        private PropertyManagerPageNumberbox _jointLimitLowerBox;
        private PropertyManagerPageNumberbox _jointLimitUpperBox;

        private int _selectedJointIndex = -1;

        // WinForms tree (Links step) — drag-to-reparent hierarchy, embedded
        // via WindowFromHandle like the nav/action bars. Reuses the pure
        // SW2GZ.Build.LinkHierarchy helpers (already unit-tested) for
        // roots/children/cycle-detection; operates directly on the live
        // Robot.Links list.
        private PropertyManagerPageWindowFromHandle _treeHandle;
        private LinkTreeView _linkTree;

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
            _stepGroups[StepJoints] = BuildJointsGroup(grpOptions, leftEdge, visibleEnabled);
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

            AddFieldLabel(grp, IdMeshLabel, "Mesh", leftEdge, visibleEnabled);
            _meshPicker = (PropertyManagerPageSelectionbox)grp.AddControl2(
                IdMeshPicker,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "", (short)leftEdge, visibleEnabled,
                "Pick one or more components in the viewport — parts or sub-assemblies");
            _meshPicker.SingleEntityOnly = false;
            _meshPicker.AllowMultipleSelectOfSameEntity = false;
            _meshPicker.Height = 30;
            _meshPicker.Mark = MeshSelectionMark;
            _meshPicker.SetSelectionFilters((object)new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS });

            AddFieldLabel(grp, IdLinkNameLabel, "Link name", leftEdge, visibleEnabled);
            _linkNameBox = (PropertyManagerPageTextbox)grp.AddControl2(
                IdLinkNameBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)leftEdge, visibleEnabled,
                "Auto-fills from a single part pick, editable. Ignored for the first/base link.");
            SetLinkNamePlaceholder();

            _linksBar       = NewBar(260, 32);
            _addLinkBtn     = NewBarButton("Add link", 80);
            _removeLinkBtn  = NewBarButton("Remove", 70);
            _clearLinksBtn  = NewBarButton("Clear all", 80);
            _addLinkBtn.Click     += (s, e) => HandleAddLink();
            _removeLinkBtn.Click  += (s, e) => HandleRemoveLink();
            _clearLinksBtn.Click  += (s, e) => HandleClearLinks();
            _linksBar.Controls.Add(_addLinkBtn);
            _linksBar.Controls.Add(_removeLinkBtn);
            _linksBar.Controls.Add(_clearLinksBtn);
            _linksBar.Resize += (s, e) => CenterRow(_linksBar, _addLinkBtn, _removeLinkBtn, _clearLinksBtn);
            CenterRow(_linksBar, _addLinkBtn, _removeLinkBtn, _clearLinksBtn);
            _linksBarHandle = (PropertyManagerPageWindowFromHandle)grp.AddControl2(
                IdLinksBtnBar,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Add, remove, or clear links");
            _linksBarHandle.Height = 34;
            _linksBarHandle.SetWindowHandlex64(_linksBar.Handle.ToInt64());

            _selectedInfoLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdSelectedInfo,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Selected: (none)", (short)leftEdge, visibleEnabled, "");

            AddFieldLabel(grp, IdLinksListLabel, "Hierarchy — drag to reparent, click to select",
                leftEdge, visibleEnabled);
            _linkTree = new LinkTreeView { Width = 260, Height = 220 };
            _linkTree.ActiveLinkChanged += (s, link) => { RefreshSelectedInfo(link); HighlightLinkMesh(link); };
            _linkTree.LinksChanged += (s, e) => RebuildJoints();
            _linkTree.SetLinks(_liveDoc.Robot.Links);
            _treeHandle = (PropertyManagerPageWindowFromHandle)grp.AddControl2(
                IdLinksTree,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "", (short)leftEdge, visibleEnabled, "Drag a link onto another to re-parent it");
            _treeHandle.Height = 220;
            _treeHandle.SetWindowHandlex64(_linkTree.Handle.ToInt64());

            return grp;
        }

        private PropertyManagerPageGroup BuildJointsGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdJointsGroup, "Joints", grpOptions);
            grp.AddControl2(IdJointsDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "One joint per non-root link. Click a joint to edit its type, axis, and limits.",
                (short)leftEdge, visibleEnabled, "");

            _jointsList = (PropertyManagerPageListbox)grp.AddControl2(
                IdJointsList,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "Joints", (short)leftEdge, visibleEnabled, "Current robot joints");
            ((IPropertyManagerPageListbox)_jointsList).Height = 90;

            AddFieldLabel(grp, IdJointNameLabel, "Joint name", leftEdge, visibleEnabled);
            _jointNameBox = (PropertyManagerPageTextbox)grp.AddControl2(
                IdJointNameBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)leftEdge, visibleEnabled, "Renamable; defaults to parent_to_child");

            AddFieldLabel(grp, IdJointTypeLabel, "Type", leftEdge, visibleEnabled);
            _jointTypeCombo = (PropertyManagerPageCombobox)grp.AddControl2(
                IdJointTypeCombo,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)leftEdge, visibleEnabled, "Joint type");
            _jointTypeCombo.Height = 14;
            _jointTypeCombo.Style = (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            foreach (string label in JointTypeLabels) _jointTypeCombo.AddItems(label);

            _jointAxisLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdJointAxisLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Axis (assembly frame X/Y/Z)", (short)leftEdge, visibleEnabled, "");
            _jointAxisXBox = NewAxisBox(grp, IdJointAxisXBox, leftEdge, visibleEnabled);
            _jointAxisYBox = NewAxisBox(grp, IdJointAxisYBox, leftEdge, visibleEnabled);
            _jointAxisZBox = NewAxisBox(grp, IdJointAxisZBox, leftEdge, visibleEnabled);

            _jointLimitLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdJointLimitLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            _jointLimitLowerBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdJointLimitLowerBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Lower", (short)leftEdge, visibleEnabled, "Lower motion limit");
            _jointLimitUpperBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdJointLimitUpperBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Upper", (short)leftEdge, visibleEnabled, "Upper motion limit");

            RefreshJointsList();
            return grp;
        }

        private static PropertyManagerPageNumberbox NewAxisBox(
            PropertyManagerPageGroup grp, int id, int leftEdge, int visibleEnabled)
        {
            var box = (PropertyManagerPageNumberbox)grp.AddControl2(
                id, (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "", (short)leftEdge, visibleEnabled, "Axis component");
            box.SetRange2((int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble,
                -1.0, 1.0, true, 0.0, 0.05, 0.05);
            return box;
        }

        private static void AddFieldLabel(PropertyManagerPageGroup grp, int id, string text, int leftEdge, int visibleEnabled)
        {
            grp.AddControl2(id, (short)swPropertyManagerPageControlType_e.swControlType_Label,
                text, (short)leftEdge, visibleEnabled, "");
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
        // Reads the mesh picker + name box and appends one link. The first
        // link ever added is forced to root/base_link regardless of the name
        // box — every URDF tree needs exactly one root, and REP-105 names it
        // base_link, so there is nothing for the user to choose there. Every
        // later link's parent is whichever node is selected in the hierarchy
        // tree below (no separate parent picker — click to target; drag a
        // node there afterward to move it under a different parent).
        private void HandleAddLink()
        {
            try
            {
                ISelectionMgr selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
                if (selMgr == null) return;
                int count = selMgr.GetSelectedObjectCount2(MeshSelectionMark);
                if (count < 1) return;

                var componentIds = new List<string>();
                for (int i = 1; i <= count; i++)
                {
                    object selObj = selMgr.GetSelectedObject6(i, MeshSelectionMark);
                    if (selObj is Component2 c && !string.IsNullOrEmpty(c.Name2))
                        componentIds.Add(c.Name2);
                }
                if (componentIds.Count == 0) return;

                bool isRoot = _liveDoc.Robot.Links.Count == 0;
                string parentName = string.Empty;
                string name;
                if (isRoot)
                {
                    name = "base_link";
                }
                else
                {
                    LinkDef parent = _linkTree?.ActiveLink;
                    if (parent == null) return; // click a link in the tree to parent this one to it
                    parentName = parent.Name;
                    name = LinkNameBoxValue();
                }
                if (string.IsNullOrEmpty(name)) return;
                if (_liveDoc.Robot.Links.Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return;

                _liveDoc.Robot.Links.Add(new LinkDef { Name = name, ComponentIds = componentIds, ParentName = parentName });
                RebuildJoints();
                _linkTree.Rebuild();
                _linkTree.SelectByLinkName(name);   // chain the next Add onto what was just created
                SetLinkNamePlaceholder();
                _modelDoc.ClearSelection2(true);
            }
            catch (Exception e) { logger.Warn("HandleAddLink failed", e); }
        }

        private void HandleRemoveLink()
        {
            LinkDef link = _linkTree?.ActiveLink;
            if (link == null) return;
            if (_liveDoc.Robot.Links.Any(l => l.ParentName == link.Name))
            {
                _swApp.SendMsgToUser("Remove its child links first.");
                return;
            }
            _liveDoc.Robot.Links.Remove(link);
            RebuildJoints();
            _linkTree.Rebuild();
        }

        private void HandleClearLinks()
        {
            _liveDoc.Robot.Links.Clear();
            _liveDoc.Robot.Joints.Clear();
            _linkTree.Rebuild();
            RefreshSelectedInfo(null);
        }

        // Auto-fills the name box from a single-part mesh pick (sub-assembly
        // or multi-component picks are ambiguous — leave it for the user to
        // type). Root/base_link ignores the name box entirely, so skip while
        // the tree is still empty.
        private void AutoFillLinkName()
        {
            if (_linkNameBox == null || _liveDoc.Robot.Links.Count == 0) return;
            ISelectionMgr selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
            if (selMgr == null) return;
            if (selMgr.GetSelectedObjectCount2(MeshSelectionMark) != 1)
            {
                SetLinkNamePlaceholder();
                return;
            }
            if (!(selMgr.GetSelectedObject6(1, MeshSelectionMark) is Component2 c)) return;
            bool isSubAssembly = (c.GetModelDoc2() as ModelDoc2)?.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY;
            if (isSubAssembly) SetLinkNamePlaceholder();
            else _linkNameBox.Text = RosNameSanitizer.Sanitize(c.Name2).Value;
        }

        private void SetLinkNamePlaceholder()
        {
            if (_linkNameBox != null) _linkNameBox.Text = LinkNamePlaceholder;
        }

        private string LinkNameBoxValue()
        {
            string t = (_linkNameBox?.Text ?? string.Empty).Trim();
            return t == LinkNamePlaceholder ? string.Empty : t;
        }

        // Selects the clicked link's own mesh components in the SW model,
        // tagged with the same MeshSelectionMark the Mesh box already
        // listens to — so clicking a tree node highlights that link's
        // geometry in the viewport AND populates the Mesh box, instead of
        // the box staying empty regardless of tree selection. Reuses the
        // legacy CommonSwOperations.SelectComponents helper (its own doc
        // comment: "Helps highlight when the associated node is selected
        // from the tree" — this exact use case, just never wired into the
        // current wizard until now).
        private void HighlightLinkMesh(LinkDef link)
        {
            try
            {
                if (link == null || link.ComponentIds == null || link.ComponentIds.Count == 0)
                {
                    _modelDoc.ClearSelection2(true);
                    return;
                }

                object[] topLevel = (object[])((AssemblyDoc)_modelDoc).GetComponents(false);
                var components = new List<Component2>();
                foreach (string compName in link.ComponentIds)
                {
                    Component2 c = SolidWorksMassProperties.FindComponent(topLevel, compName);
                    if (c != null) components.Add(c);
                }
                CommonSwOperations.SelectComponents(_modelDoc, components, clearSelection: true, mark: MeshSelectionMark);
            }
            catch (Exception e) { logger.Warn("HighlightLinkMesh failed", e); }
        }

        private void RefreshSelectedInfo(LinkDef link)
        {
            if (_selectedInfoLabel == null) return;
            _selectedInfoLabel.Caption = link == null
                ? "Selected: (none)"
                : "Selected: " + link.Name + "    Mesh: " + DescribeMeshes(link.ComponentIds);
        }

        // The first component defines the link's own frame (mesh anchor,
        // joint origin, inertial rebase — see
        // docs/superpowers/specs/2026-07-02-robot-joint-relative-pose-design.md),
        // so it's marked (primary) wherever the mesh list is shown —
        // otherwise which one drives the frame is invisible.
        private static string DescribeMeshes(List<string> componentIds)
        {
            if (componentIds == null || componentIds.Count == 0) return "(none)";
            var parts = new List<string>(componentIds.Count);
            for (int i = 0; i < componentIds.Count; i++)
                parts.Add(i == 0 ? componentIds[i] + " (primary)" : componentIds[i]);
            return string.Join(", ", parts);
        }

        // Joints stay 1:1 with the link tree (one per non-root link), but
        // MERGE-preserve instead of clear-and-rebuild — a link add/remove/
        // reparent elsewhere must not wipe out Type/Axis/Limit edits the
        // user already made on a joint whose (parent, child) pair is
        // unaffected. See JointDefReconciler and
        // docs/superpowers/specs/2026-07-03-robot-joint-type-panel-design.md.
        private void RebuildJoints()
        {
            _liveDoc.Robot.Joints = JointDefReconciler.Reconcile(_liveDoc.Robot.Joints, _liveDoc.Robot.Links);
        }

        private void RefreshJointsList()
        {
            if (_jointsList == null) return;
            _jointsList.Clear();
            foreach (JointDef j in _liveDoc.Robot.Joints) _jointsList.AddItems(j.Name);
            if (_liveDoc.Robot.Joints.Count > 0)
            {
                _jointsList.CurrentSelection = 0;
                _selectedJointIndex = 0;
                LoadJointIntoControls(_liveDoc.Robot.Joints[0]);
            }
            else
            {
                _selectedJointIndex = -1;
                ClearJointControls();
            }
        }

        private void LoadJointIntoControls(JointDef j)
        {
            if (_jointNameBox == null) return;
            _jointNameBox.Text = j.Name;
            int typeIdx = Array.IndexOf(JointTypeOptions, j.Type);
            _jointTypeCombo.CurrentSelection = (short)(typeIdx >= 0 ? typeIdx : 0);
            _jointAxisXBox.Value = j.AxisX;
            _jointAxisYBox.Value = j.AxisY;
            // !HasAxis means X/Y/Z are all still 0 (never set) — X/Y correctly stay 0,
            // only Z needs the (0,0,1) default substituted in, matching the same
            // default OnComboboxSelectionChanged applies when a joint first leaves Fixed.
            _jointAxisZBox.Value = j.HasAxis ? j.AxisZ : 1.0;
            _jointLimitLowerBox.Value = j.Type == UrdfJointType.Revolute ? RadToDeg(j.LimitLower ?? 0.0) : (j.LimitLower ?? 0.0);
            _jointLimitUpperBox.Value = j.Type == UrdfJointType.Revolute ? RadToDeg(j.LimitUpper ?? 0.0) : (j.LimitUpper ?? 0.0);
            UpdateJointFieldVisibility(j.Type);
        }

        private void ClearJointControls()
        {
            if (_jointNameBox == null) return;
            _jointNameBox.Text = string.Empty;
            _jointTypeCombo.CurrentSelection = 0;
            _jointAxisXBox.Value = 0; _jointAxisYBox.Value = 0; _jointAxisZBox.Value = 1;
            _jointLimitLowerBox.Value = 0; _jointLimitUpperBox.Value = 0;
            UpdateJointFieldVisibility(UrdfJointType.Fixed);
        }

        // Axis is only meaningful for a moving joint; limits only for
        // Revolute/Prismatic (Continuous is unlimited by definition, Fixed
        // moves at all). IPropertyManagerPageControl.Visible is the same
        // generic control property already used to toggle whole step groups
        // (see docs/reference/solidworks-api.md) — this is its first use on
        // an individual control rather than a group.
        private void UpdateJointFieldVisibility(UrdfJointType type)
        {
            bool showAxis = type != UrdfJointType.Fixed;
            bool showLimit = type == UrdfJointType.Revolute || type == UrdfJointType.Prismatic;
            ((IPropertyManagerPageControl)_jointAxisLabel).Visible = showAxis;
            ((IPropertyManagerPageControl)_jointAxisXBox).Visible = showAxis;
            ((IPropertyManagerPageControl)_jointAxisYBox).Visible = showAxis;
            ((IPropertyManagerPageControl)_jointAxisZBox).Visible = showAxis;
            ((IPropertyManagerPageControl)_jointLimitLabel).Visible = showLimit;
            _jointLimitLabel.Caption = type == UrdfJointType.Revolute ? "Limit (degrees)" : "Limit (meters)";
            ((IPropertyManagerPageControl)_jointLimitLowerBox).Visible = showLimit;
            ((IPropertyManagerPageControl)_jointLimitUpperBox).Visible = showLimit;
        }

        // Reads whatever is currently in the shared detail-form controls
        // back into the JointDef that was loaded into them. Must run BEFORE
        // switching the selected list row (single shared control set, one
        // JointDef "checked out" at a time) and before leaving the Joints
        // step entirely (ShowStep) or reviewing it (RefreshReviewLabels).
        private void CommitSelectedJointFromControls()
        {
            if (_selectedJointIndex < 0 || _selectedJointIndex >= _liveDoc.Robot.Joints.Count) return;
            JointDef j = _liveDoc.Robot.Joints[_selectedJointIndex];

            string newName = (_jointNameBox?.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(newName)) j.Name = newName;

            short typeIdx = _jointTypeCombo?.CurrentSelection ?? 0;
            UrdfJointType type = ComboIndexToType(typeIdx);
            j.Type = type;

            if (type != UrdfJointType.Fixed)
            {
                j.SetAxis(new System.Numerics.Vector3(
                    (float)_jointAxisXBox.Value, (float)_jointAxisYBox.Value, (float)_jointAxisZBox.Value));
            }

            if (type == UrdfJointType.Revolute || type == UrdfJointType.Prismatic)
            {
                j.LimitLower = type == UrdfJointType.Revolute ? DegToRad(_jointLimitLowerBox.Value) : _jointLimitLowerBox.Value;
                j.LimitUpper = type == UrdfJointType.Revolute ? DegToRad(_jointLimitUpperBox.Value) : _jointLimitUpperBox.Value;
            }
        }

        private static double DegToRad(double deg) => deg * System.Math.PI / 180.0;
        private static double RadToDeg(double rad) => rad * 180.0 / System.Math.PI;

        private static UrdfJointType ComboIndexToType(int idx) =>
            JointTypeOptions[System.Math.Max(0, System.Math.Min(JointTypeOptions.Length - 1, idx))];

        private void RefreshReviewLabels()
        {
            CommitSelectedJointFromControls();
            if (_reviewLinksLabel != null)
                _reviewLinksLabel.Caption = "Links: " + _liveDoc.Robot.Links.Count;
            if (_reviewBaseLabel != null)
                _reviewBaseLabel.Caption = "Base link: " +
                    (_liveDoc.Robot.Links.Count > 0 ? _liveDoc.Robot.Links[0].Name : "(none)");
            if (_reviewJointsLabel != null)
                _reviewJointsLabel.Caption = "Joints: " + _liveDoc.Robot.Joints.Count;
        }

        // ─── Navigation ──────────────────────────────────────────────────────
        private void ShowStep(int step)
        {
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;

            if (_currentStep == StepJoints && step != StepJoints) CommitSelectedJointFromControls();

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

            if (_currentStep == StepJoints) RefreshJointsList();
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

        // Fake cue-banner placeholder: PMP-native Textbox has no real one, so
        // swap the placeholder text for real empty content on focus and back
        // on blur-while-still-empty.
        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id)
        {
            if (Id == IdLinkNameBox && _linkNameBox != null && _linkNameBox.Text == LinkNamePlaceholder)
                _linkNameBox.Text = string.Empty;
        }
        void IPropertyManagerPage2Handler9.OnLostFocus(int Id)
        {
            if (Id == IdLinkNameBox && _linkNameBox != null && string.IsNullOrEmpty(_linkNameBox.Text))
                SetLinkNamePlaceholder();
        }
        bool IPropertyManagerPage2Handler9.OnHelp() => true;
        bool IPropertyManagerPage2Handler9.OnNextPage() => true;
        bool IPropertyManagerPage2Handler9.OnPreviousPage() => true;
        bool IPropertyManagerPage2Handler9.OnPreview() => true;
        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id) => true;
        bool IPropertyManagerPage2Handler9.OnKeystroke(int Wparam, int Message, int Lparam, int Id) => false;
        bool IPropertyManagerPage2Handler9.OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText) => true;
        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count)
        {
            if (Id != IdMeshPicker) return;
            try { AutoFillLinkName(); } catch (Exception e) { logger.Warn("AutoFillLinkName failed", e); }
        }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id) { }
        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked) { }
        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text) { }
        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            if (Id != IdJointTypeCombo) return;
            UrdfJointType type = ComboIndexToType(Item);
            // Suggest a sane default axis the first time a joint leaves
            // Fixed, instead of leaving it at (0,0,0) — a zero-vector axis
            // is meaningless in URDF.
            if (type != UrdfJointType.Fixed && _jointAxisXBox.Value == 0 && _jointAxisYBox.Value == 0 && _jointAxisZBox.Value == 0)
                _jointAxisZBox.Value = 1;
            UpdateJointFieldVisibility(type);
        }
        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item)
        {
            if (Id != IdJointsList) return;
            CommitSelectedJointFromControls();
            _selectedJointIndex = Item;
            if (Item >= 0 && Item < _liveDoc.Robot.Joints.Count) LoadJointIntoControls(_liveDoc.Robot.Joints[Item]);
        }
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
