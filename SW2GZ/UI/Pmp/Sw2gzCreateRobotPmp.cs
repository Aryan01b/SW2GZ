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

        private const int HeaderGroupID = 1;
        private const int HeaderLabelID = 2;
        private const int NavGroupID    = 3;
        private const int ButtonBackID  = 4;
        private const int ButtonNextID  = 5;

        private const int StepIdBase = 100;
        private int StepGroupId(int step) => StepIdBase + step * 20;

        private const int TreeHandleID          = StepIdBase + 0 * 20 + 2;
        private const int PickFunnelID          = StepIdBase + 0 * 20 + 3;
        private const int ButtonAddLinkID       = StepIdBase + 0 * 20 + 4;
        private const int ButtonRemoveLinkID    = StepIdBase + 0 * 20 + 5;
        private const int LabelLinkMassID       = StepIdBase + 0 * 20 + 6;
        private const int LabelLinkValidationID = StepIdBase + 0 * 20 + 7;
        private const int LabelLinkInstrID      = StepIdBase + 0 * 20 + 8;

        private const int LabelJointInstrID  = StepIdBase + 1 * 20 + 2;
        private const int ListJointsID       = StepIdBase + 1 * 20 + 3;
        private const int LabelMatesCapID    = StepIdBase + 1 * 20 + 4;
        private const int ListMatesID        = StepIdBase + 1 * 20 + 5;
        private const int LabelDetailCapID   = StepIdBase + 1 * 20 + 6;
        private const int LabelDetailLinksID = StepIdBase + 1 * 20 + 7;
        private const int LabelDetailMateID  = StepIdBase + 1 * 20 + 8;
        private const int LabelDetailTypeID  = StepIdBase + 1 * 20 + 9;
        private const int LabelDetailLimitsID = StepIdBase + 1 * 20 + 10;
        // D4 — per-joint Reference Coord System + Reference Axis pickers.
        private const int LabelRefCapID       = StepIdBase + 1 * 20 + 11;
        private const int ComboRefCsID        = StepIdBase + 1 * 20 + 12;
        private const int ComboRefAxisID      = StepIdBase + 1 * 20 + 13;
        private const int LabelRefHelpID      = StepIdBase + 1 * 20 + 14;

        private const int LabelReviewInstrID     = StepIdBase + 2 * 20 + 2;
        private const int LabelReviewModeID      = StepIdBase + 2 * 20 + 3;
        private const int LabelReviewLinksCapID  = StepIdBase + 2 * 20 + 4;
        private const int ListReviewLinksID      = StepIdBase + 2 * 20 + 5;
        private const int LabelReviewJointsCapID = StepIdBase + 2 * 20 + 6;
        private const int ListReviewJointsID    = StepIdBase + 2 * 20 + 7;

        private const int LinkSelectionMark = 3;

        private PropertyManagerPageLabel _hdrLabel;
        private PropertyManagerPageGroup[] _stepGroups;
        private PropertyManagerPageButton _backBtn;
        private PropertyManagerPageButton _nextBtn;

        private PropertyManagerPageWindowFromHandle _treeHandle;
        private LinkTreeView _linkTree;
        private PropertyManagerPageSelectionbox _pickFunnel;
        private PropertyManagerPageLabel _linkMass;
        private PropertyManagerPageLabel _linkValidation;
        private LinkDef _activeLink;
        private bool _suppressLinkSelectionLoad;
        private IMassProperties _massProps;
        private readonly List<string> _allComponentIds = new List<string>();

        private PropertyManagerPageListbox _jointsListBox;
        private PropertyManagerPageListbox _matesListBox;
        private PropertyManagerPageLabel _detailLinks;
        private PropertyManagerPageLabel _detailMate;
        private PropertyManagerPageLabel _detailType;
        private PropertyManagerPageLabel _detailLimits;
        private int _activeJointIndex = -1;
        private List<MateInfo> _allMates = new List<MateInfo>();
        private List<int> _visibleMateIndices = new List<int>();
        private bool _suppressMateListEvents;

        // D4 — per-joint Reference-CS / Reference-Axis pickers. The combo
        // boxes are reused across joints (one pair, not one pair per joint);
        // their contents rebuild when the active joint changes so the user
        // sees the CS / axis features defined on the active joint's child
        // component's part model.
        private PropertyManagerPageCombobox _refCsCombo;
        private PropertyManagerPageCombobox _refAxisCombo;
        private List<string> _refCsOptions = new List<string>();
        private List<string> _refAxisOptions = new List<string>();
        private bool _suppressRefComboEvents;

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

            var navGroup = (PropertyManagerPageGroup)_page.AddGroupBox(NavGroupID, "Navigation", grpOptions);
            _backBtn = (PropertyManagerPageButton)navGroup.AddControl2(
                ButtonBackID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "< Back", 0, visibleEnabled, "Previous step");
            ((IPropertyManagerPageControl)_backBtn).Width = 70;
            _nextBtn = (PropertyManagerPageButton)navGroup.AddControl2(
                ButtonNextID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Next >", 0, visibleEnabled, "Next step");
            ((IPropertyManagerPageControl)_nextBtn).Width = 70;
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
                if (!_suppressLinkSelectionLoad) LoadLinkSelection(_activeLink);
            };
            _linkTree.LinksChanged += (s, e) => UpdateValidationLabel();

            AddFieldLabel(group, LabelLinkInstrID,
                "Tree: click a link, then pick its parts in the viewport. " +
                "Drag to re-parent, F2 to rename, right-click to set base.",
                leftEdge, labelOpts);

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

            AddLinkButton(group, ButtonAddLinkID, "Add link", indent, visibleEnabled);
            AddLinkButton(group, ButtonRemoveLinkID, "Remove link", indent, visibleEnabled);

            _linkMass = AddFieldLabel(group, LabelLinkMassID, "", leftEdge, labelOpts);
            _linkValidation = AddFieldLabel(group, LabelLinkValidationID, "", leftEdge, labelOpts);

            _linkTree.SetLinks(Robot.Links);
            var roots = LinkHierarchy.Roots(Robot.Links);
            if (roots.Count > 0) _linkTree.SelectByLinkName(roots[0].Name);
            UpdateValidationLabel();
        }

        private PropertyManagerPageButton AddLinkButton(
            PropertyManagerPageGroup group, int id, string caption, int indent, int visibleEnabled)
        {
            var b = (PropertyManagerPageButton)group.AddControl2(
                id,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                caption, (short)indent, visibleEnabled, caption);
            ((IPropertyManagerPageControl)b).Width = 110;
            return b;
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
                "Joints come from the link tree — one per parent→child. Select a joint, " +
                "then the mate that drives it; the mate sets type and limits.",
                leftEdge, labelOpts);

            _jointsListBox = (PropertyManagerPageListbox)group.AddControl2(
                ListJointsID,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "", (short)leftEdge, visibleEnabled, "Joints (from the link tree) — select one");
            ((IPropertyManagerPageListbox)_jointsListBox).Height = 90;

            AddFieldLabel(group, LabelMatesCapID,
                "Mates — select one to assign to the joint (it highlights in the view):",
                leftEdge, labelOpts);
            _matesListBox = (PropertyManagerPageListbox)group.AddControl2(
                ListMatesID,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "", (short)leftEdge, visibleEnabled, "Assembly mates — click to assign + highlight");
            ((IPropertyManagerPageListbox)_matesListBox).Height = 90;

            AddFieldLabel(group, LabelDetailCapID, "— Selected joint —", leftEdge, labelOpts);
            _detailLinks  = AddFieldLabel(group, LabelDetailLinksID, "", leftEdge, labelOpts);
            _detailMate   = AddFieldLabel(group, LabelDetailMateID, "", leftEdge, labelOpts);
            _detailType   = AddFieldLabel(group, LabelDetailTypeID, "", leftEdge, labelOpts);
            _detailLimits = AddFieldLabel(group, LabelDetailLimitsID, "", leftEdge, labelOpts);

            // D4 — Reference Coordinate System + Reference Axis pickers per
            // joint. These mirror upstream solidworks_urdf_exporter's joint
            // page: a user-named CS on the child component anchors the joint
            // origin at the right fulcrum; a user-named Reference Axis sets
            // the joint axis. When both are empty, the legacy mate-driven
            // path stays in effect — backward compat for older assemblies.
            AddFieldLabel(group, LabelRefCapID,
                "— Reference geometry (preferred over mates) —",
                leftEdge, labelOpts);
            _refCsCombo = (PropertyManagerPageCombobox)group.AddControl2(
                ComboRefCsID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "Reference Coord System", (short)leftEdge, visibleEnabled,
                "Pick the Reference Coordinate System feature on the child component that defines this joint's origin");
            _refCsCombo.Height = 18;

            _refAxisCombo = (PropertyManagerPageCombobox)group.AddControl2(
                ComboRefAxisID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "Reference Axis", (short)leftEdge, visibleEnabled,
                "Pick the Reference Axis feature on the child component that defines this joint's axis direction");
            _refAxisCombo.Height = 18;

            AddFieldLabel(group, LabelRefHelpID,
                "Tip: add a Reference Coordinate System + Reference Axis on each child " +
                "part in SolidWorks first, then select them here. Leave both blank to " +
                "fall back to the mate-driven path.",
                leftEdge, labelOpts);
        }

        private void ReadAllMates()
        {
            try
            {
                _allMates = new List<MateInfo>(
                    new SolidWorksAssemblyWalker((AssemblyDoc)_modelDoc).WalkAllMates());
            }
            catch (Exception e)
            {
                logger.Warn("ReadAllMates failed", e);
                _allMates = new List<MateInfo>();
            }
        }

        private void EnterJointsStep()
        {
            Robot.Joints = JointSeeder.Sync(Robot.Links, Robot.Joints);
            ReadAllMates();
            PopulateMateList();
            if (_activeJointIndex < 0 && Robot.Joints.Count > 0) _activeJointIndex = 0;
            PopulateJointList();
        }

        private void PopulateMateList()
        {
            if (_matesListBox == null) return;
            _suppressMateListEvents = true;
            try
            {
                _matesListBox.Clear();
                _visibleMateIndices.Clear();

                JointDef j = ActiveJoint();
                string p = j?.ParentLink, c = j?.ChildLink;
                bool filter = j != null && !string.IsNullOrEmpty(p) && !string.IsNullOrEmpty(c);

                for (int i = 0; i < _allMates.Count; i++)
                {
                    MateInfo m = _allMates[i];
                    if (filter)
                    {
                        if (string.IsNullOrEmpty(m.LinkA) || string.IsNullOrEmpty(m.LinkB)) continue;
                        bool spans = (m.LinkA == p && m.LinkB == c) || (m.LinkA == c && m.LinkB == p);
                        if (!spans) continue;
                    }
                    _visibleMateIndices.Add(i);
                    _matesListBox.AddItems(m.Name + "  [" + m.Kind + "]");
                }
            }
            finally { _suppressMateListEvents = false; }
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

        private void UpdateJointDetails()
        {
            JointDef j = ActiveJoint();
            bool limited = j != null &&
                (j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Prismatic);

            if (_detailLinks != null)
                _detailLinks.Caption = j == null ? "No joint selected."
                    : "Links:  " + j.ParentLink + "  →  " + j.ChildLink;
            if (_detailMate != null)
                _detailMate.Caption = j == null ? ""
                    : "Mate:  " + (string.IsNullOrEmpty(j.MateName) ? "none — select a mate above" : j.MateName);
            if (_detailType != null)
                _detailType.Caption = j == null ? "" : "Type:  " + j.Type;
            if (_detailLimits != null)
                _detailLimits.Caption = (j == null || !limited) ? ""
                    : "Limits:  lower " + Fmt(j.LimitLower) + ",  upper " + Fmt(j.LimitUpper);

            PopulateMateList();
            PopulateRefGeometryCombos();
            HighlightActiveMate();
        }

        // D4 — enumerate Reference CS + Reference Axis features on the active
        // joint's child component (the part model the user added the geometry
        // to). Empty active joint or unresolvable child → combos go empty,
        // which leaves the mate-driven fallback path intact.
        private void PopulateRefGeometryCombos()
        {
            if (_refCsCombo == null || _refAxisCombo == null) return;

            _suppressRefComboEvents = true;
            try
            {
                _refCsCombo.Clear();
                _refAxisCombo.Clear();
                _refCsOptions = new List<string>();
                _refAxisOptions = new List<string>();

                ModelDoc2 childModel = ResolveActiveJointChildModel();
                _refCsOptions   = SwRefGeometryEnumerator.CoordinateSystems(childModel);
                _refAxisOptions = SwRefGeometryEnumerator.ReferenceAxes(childModel);

                // Both combos lead with "(none)" so the user can clear a prior
                // selection without retyping. Index 0 == unset == empty string.
                _refCsCombo.AddItems("(none)");
                foreach (string n in _refCsOptions) _refCsCombo.AddItems(n);
                _refAxisCombo.AddItems("(none)");
                foreach (string n in _refAxisOptions) _refAxisCombo.AddItems(n);

                JointDef j = ActiveJoint();
                int csIdx = (j == null || string.IsNullOrEmpty(j.RefCsName))
                    ? 0 : (_refCsOptions.IndexOf(j.RefCsName) + 1);
                int axIdx = (j == null || string.IsNullOrEmpty(j.RefAxisName))
                    ? 0 : (_refAxisOptions.IndexOf(j.RefAxisName) + 1);
                _refCsCombo.CurrentSelection   = (short)System.Math.Max(0, csIdx);
                _refAxisCombo.CurrentSelection = (short)System.Math.Max(0, axIdx);
            }
            finally { _suppressRefComboEvents = false; }
        }

        // Active joint's child link → first ComponentIds entry → Component2 in
        // the assembly → its part ModelDoc2. Returns null when anything in
        // that chain is missing.
        private ModelDoc2 ResolveActiveJointChildModel()
        {
            JointDef j = ActiveJoint();
            if (j == null || string.IsNullOrEmpty(j.ChildLink)) return null;
            LinkDef childLink = null;
            foreach (LinkDef l in Robot.Links)
                if (l.Name == j.ChildLink) { childLink = l; break; }
            if (childLink == null || childLink.ComponentIds == null ||
                childLink.ComponentIds.Count == 0) return null;
            string compId = childLink.ComponentIds[0];

            object[] comps = (object[])((AssemblyDoc)_modelDoc).GetComponents(true);
            if (comps == null) return null;
            foreach (object o in comps)
            {
                var c = (Component2)o;
                if (c.Name2 == compId)
                    return c.GetModelDoc2() as ModelDoc2;
            }
            return null;
        }

        private static string Fmt(double? v) =>
            v.HasValue ? v.Value.ToString("0.###", CultureInfo.InvariantCulture) : "–";

        private void HighlightActiveMate()
        {
            JointDef j = ActiveJoint();
            if (j == null || string.IsNullOrEmpty(j.MateName)) return;
            try { new SolidWorksAssemblyWalker((AssemblyDoc)_modelDoc).HighlightMate(j.MateName); }
            catch (Exception e) { logger.Warn("HighlightActiveMate threw", e); }
        }

        private void AssignMateToActiveJoint(int visibleIndex)
        {
            JointDef j = ActiveJoint();
            if (j == null) { _swApp.SendMsgToUser("Select a joint first, then a mate."); return; }
            if (visibleIndex < 0 || visibleIndex >= _visibleMateIndices.Count) return;
            int mateIndex = _visibleMateIndices[visibleIndex];
            if (mateIndex < 0 || mateIndex >= _allMates.Count) return;

            MateInfo m = _allMates[mateIndex];
            j.MateName = m.Name;
            j.Type = JointSeeder.ToJointType(m.Kind);
            j.SetAxis(m.Axis);
            j.LimitLower = m.LimitLower;
            j.LimitUpper = m.LimitUpper;
            // The mate's geometric reference point (assembly frame) is the new
            // URDF link-frame origin for this joint's child. Without it the
            // joint pivots around the child part's design origin, which can be
            // far from the actual mate axis. Null = fall back to legacy anchor.
            if (m.MatePointAssembly.HasValue) j.SetMatePoint(m.MatePointAssembly.Value);
            else j.ClearMatePoint();

            PopulateJointList();
        }

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
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;

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

            _hdrLabel.Caption = "Step " + (_currentStep + 1) + " of " + StepCount +
                                " — " + StepNames[_currentStep];

            ((IPropertyManagerPageControl)_backBtn).Enabled = _currentStep > 0;
            // DEFENSIVE: explicitly re-enable Next on every step show. Without
            // this, an SW SDK first-show race occasionally left _nextBtn in a
            // disabled state on a freshly opened wizard, producing the
            // "first time Joints Next doesn't work, reopen fixes it" bug.
            try { ((IPropertyManagerPageControl)_nextBtn).Enabled = true; }
            catch (Exception ex) { logger.Warn("ShowStep: re-enable _nextBtn failed", ex); }
            _nextBtn.Caption = (_currentStep == StepCount - 1) ? "Finish" : "Next >";

            logger.Info("Sw2gzCreateRobotPmp.ShowStep -> step=" + _currentStep +
                        " (" + StepNames[_currentStep] + ")");

            if (_currentStep == StepJoints) EnterJointsStep();
            else if (_currentStep == StepReview) EnterReviewStep();
        }

        private void GoBack()
        {
            if (_currentStep > 0) ShowStep(_currentStep - 1);
        }

        private void GoNext()
        {
            if (_currentStep == StepLinks)
            {
                List<string> issues = LinkDefValidator.Validate(Robot.Links, _allComponentIds);
                if (issues.Count > 0)
                {
                    _swApp.SendMsgToUser("Resolve link issues before continuing:\n• " +
                        string.Join("\n• ", issues.ToArray()));
                    return;
                }
            }
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

        void IPropertyManagerPage2Handler9.AfterActivation()
        {
            ShowStep(_currentStep);
            if (_currentStep == StepLinks && _pickFunnel != null) _pickFunnel.SetSelectionFocus();
        }

        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            // Log every button press so the "Next doesn't work" bug is visible
            // in sw2gz.log if it ever recurs — the click either arrives here or
            // it's being swallowed by SW upstream (focus on a listbox etc.).
            logger.Info("Sw2gzCreateRobotPmp.OnButtonPress id=" + Id +
                        " step=" + _currentStep);
            try
            {
                switch (Id)
                {
                    case ButtonBackID: GoBack(); break;
                    case ButtonNextID: GoNext(); break;
                    case ButtonAddLinkID: AddLink(); break;
                    case ButtonRemoveLinkID: RemoveLink(); break;
                }
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
            if (Id == ListJointsID) { _activeJointIndex = Item; UpdateJointDetails(); }
            else if (Id == ListMatesID)
            {
                if (_suppressMateListEvents) return;
                AssignMateToActiveJoint(Item);
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
        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id) { }
        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked) { }
        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text) { }
        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            if (_suppressRefComboEvents) return;
            JointDef j = ActiveJoint();
            if (j == null) return;
            // Item==0 == "(none)" sentinel → empty string clears the field
            // and the walker falls back to the mate-driven path.
            if (Id == ComboRefCsID)
            {
                j.RefCsName = (Item <= 0 || Item - 1 >= _refCsOptions.Count)
                    ? string.Empty : _refCsOptions[Item - 1];
            }
            else if (Id == ComboRefAxisID)
            {
                j.RefAxisName = (Item <= 0 || Item - 1 >= _refAxisOptions.Count)
                    ? string.Empty : _refAxisOptions[Item - 1];
            }
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
