/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateRobotPmp — Joints step slice. One PMP listbox row per non-root
link's joint plus a shared detail form (name/type/axis/limit) that is
"checked out" for whichever row is selected and committed back via
CommitSelectedJointFromControls before the selection changes or the wizard
leaves the Joints step. See docs/superpowers/specs/2026-07-03-robot-joint-
type-panel-design.md.
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;

namespace SW2GZ.UI.Pmp
{
    public sealed partial class Sw2gzCreateRobotPmp
    {
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
        private const int IdJointPivotSourceLabel = 34;
        private const int IdJointPivotSourceCombo = 35;

        private static readonly string[] JointTypeLabels =
            { "Fixed", "Revolute", "Continuous", "Prismatic" };
        private static readonly UrdfJointType[] JointTypeOptions =
            { UrdfJointType.Fixed, UrdfJointType.Revolute, UrdfJointType.Continuous, UrdfJointType.Prismatic };

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
        private PropertyManagerPageLabel _jointPivotSourceLabel;
        private PropertyManagerPageCombobox _jointPivotSourceCombo;

        private int _selectedJointIndex = -1;

        // Every real (non-Fixed) mate candidate found for whichever joint is
        // currently loaded into the detail form — index-parallel to
        // _jointPivotSourceCombo's items. Recomputed on every
        // LoadJointIntoControls call; empty/stale between loads.
        private List<MateJointClassification.Result> _currentJointCandidates = new List<MateJointClassification.Result>();

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

            _jointPivotSourceLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdJointPivotSourceLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Pivot source", (short)leftEdge, visibleEnabled, "");
            _jointPivotSourceCombo = (PropertyManagerPageCombobox)grp.AddControl2(
                IdJointPivotSourceCombo,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)leftEdge, visibleEnabled,
                "Which mate this joint's axis/pivot comes from — only shown when the link pair has more than one candidate");
            _jointPivotSourceCombo.Height = 14;
            _jointPivotSourceCombo.Style = (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;

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

        // The joint's parent/child link's own primary (first-assigned)
        // component — the same "first component defines the link's frame"
        // convention used everywhere else. Shared by the auto-suggest pass
        // and the pivot-source picker so both look up the identical pair.
        private (string parentPrimary, string childPrimary) PrimaryComponentsFor(JointDef j)
        {
            LinkDef parentLink = _liveDoc.Robot.Links.FirstOrDefault(l => l.Name == j.ParentLink);
            LinkDef childLink = _liveDoc.Robot.Links.FirstOrDefault(l => l.Name == j.ChildLink);
            return (parentLink?.ComponentIds?.FirstOrDefault(), childLink?.ComponentIds?.FirstOrDefault());
        }

        private void RefreshJointsList()
        {
            if (_jointsList == null) return;
            foreach (JointDef j in _liveDoc.Robot.Joints)
            {
                if (j.IsSuggested) continue;
                (string parentPrimary, string childPrimary) = PrimaryComponentsFor(j);
                if (string.IsNullOrEmpty(parentPrimary) || string.IsNullOrEmpty(childPrimary)) continue;

                MateJointClassification.Result suggestion;
                try { suggestion = _mateResolver.Resolve(parentPrimary, childPrimary); }
                catch (Exception ex) { logger.Warn("Mate suggestion failed for " + j.Name, ex); continue; }

                if (!suggestion.Found || suggestion.Type == UrdfJointType.Fixed) continue;

                j.Type = suggestion.Type;
                j.SetAxis(suggestion.AxisAssembly);
                if (suggestion.OriginAssembly.HasValue) j.SetMatePoint(suggestion.OriginAssembly.Value);
                if (suggestion.LimitLower.HasValue) j.LimitLower = suggestion.LimitLower;
                if (suggestion.LimitUpper.HasValue) j.LimitUpper = suggestion.LimitUpper;
                j.MateName = suggestion.MateName;
                j.IsSuggested = true;
            }

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
            _jointAxisXBox.Value = SnapNearZero(j.AxisX);
            _jointAxisYBox.Value = SnapNearZero(j.AxisY);
            // !HasAxis means X/Y/Z are all still 0 (never set) — X/Y correctly stay 0,
            // only Z needs the (0,0,1) default substituted in, matching the same
            // default OnComboboxSelectionChanged applies when a joint first leaves Fixed.
            _jointAxisZBox.Value = j.HasAxis ? SnapNearZero(j.AxisZ) : 1.0;
            _jointLimitLowerBox.Value = j.Type == UrdfJointType.Revolute ? RadToDeg(j.LimitLower ?? 0.0) : (j.LimitLower ?? 0.0);
            _jointLimitUpperBox.Value = j.Type == UrdfJointType.Revolute ? RadToDeg(j.LimitUpper ?? 0.0) : (j.LimitUpper ?? 0.0);
            UpdateJointFieldVisibility(j.Type);
            LoadPivotSourceCombo(j);
            HighlightJointPivotAxis(j);
        }

        // Populates the "Pivot source" picker with every real (non-Fixed)
        // mate candidate found for this joint's link pair — only shown when
        // there are 2+, since a single candidate has nothing to choose
        // between. Lets the user override which mate's geometry the
        // suggestion used (e.g. a link with two similar holes, only one of
        // which is the actual hinge — see docs/superpowers/specs/2026-07-03-
        // mate-pivot-dual-cylinder-agreement-design.md's follow-up).
        private void LoadPivotSourceCombo(JointDef j)
        {
            _currentJointCandidates = new List<MateJointClassification.Result>();
            if (_jointPivotSourceCombo == null) return;

            (string parentPrimary, string childPrimary) = PrimaryComponentsFor(j);
            if (!string.IsNullOrEmpty(parentPrimary) && !string.IsNullOrEmpty(childPrimary))
            {
                try
                {
                    _currentJointCandidates = _mateResolver
                        .ResolveAllCandidates(parentPrimary, childPrimary)
                        .Where(c => c.Found && c.Type != UrdfJointType.Fixed)
                        .ToList();
                }
                catch (Exception ex) { logger.Warn("LoadPivotSourceCombo: candidate lookup failed for " + j.Name, ex); }
            }

            bool show = j.Type != UrdfJointType.Fixed && _currentJointCandidates.Count > 1;
            ((IPropertyManagerPageControl)_jointPivotSourceLabel).Visible = show;
            ((IPropertyManagerPageControl)_jointPivotSourceCombo).Visible = show;
            if (!show) return;

            _jointPivotSourceCombo.Clear();
            foreach (MateJointClassification.Result c in _currentJointCandidates)
                _jointPivotSourceCombo.AddItems(c.MateName + " (" + c.Type + ")");

            int selIdx = string.IsNullOrEmpty(j.MateName)
                ? -1
                : _currentJointCandidates.FindIndex(c => c.MateName == j.MateName);
            if (selIdx < 0)
            {
                MateJointClassification.Result best = MateJointClassification.ChooseBest(_currentJointCandidates);
                selIdx = _currentJointCandidates.FindIndex(c => c == best);
            }
            _jointPivotSourceCombo.CurrentSelection = (short)System.Math.Max(0, selIdx);
        }

        // User picked a different candidate mate for this joint's pivot —
        // re-derive type/axis/pivot/limit from THAT mate instead of whatever
        // ChooseBest's tie-break originally guessed, tag it so the choice
        // sticks (RefreshJointsList never touches an already-IsSuggested
        // joint), and re-highlight so the change is visible immediately.
        private void HandlePivotSourceChanged(int item)
        {
            if (_selectedJointIndex < 0 || _selectedJointIndex >= _liveDoc.Robot.Joints.Count) return;
            if (item < 0 || item >= _currentJointCandidates.Count) return;

            JointDef j = _liveDoc.Robot.Joints[_selectedJointIndex];
            MateJointClassification.Result chosen = _currentJointCandidates[item];

            j.Type = chosen.Type;
            j.SetAxis(chosen.AxisAssembly);
            if (chosen.OriginAssembly.HasValue) j.SetMatePoint(chosen.OriginAssembly.Value);
            else j.ClearMatePoint();
            j.LimitLower = chosen.LimitLower;
            j.LimitUpper = chosen.LimitUpper;
            j.MateName = chosen.MateName;
            j.IsSuggested = true;

            LoadJointIntoControls(j);
        }

        // Highlights the selected joint's pivot by selecting the EXACT SW
        // face the suggestion's geometry came from (via j.MateName), not a
        // guessed nearby point — see docs/superpowers/specs/2026-07-03-mate-
        // pivot-dual-cylinder-agreement-design.md. Reuses the same "only the
        // active selection" pattern as HighlightLinkMesh. No color
        // distinction (pending vs confirmed) — SW has no confirmed
        // per-entity color setter for an arbitrary face selection in this
        // interop version (grepped the repo: no precedent), so this relies
        // on SW's own default selection highlight, same as before.
        private void HighlightJointPivotAxis(JointDef j)
        {
            try
            {
                _modelDoc.ClearSelection2(true);
                if (j == null || j.Type == UrdfJointType.Fixed || !j.HasMatePoint || string.IsNullOrEmpty(j.MateName))
                    return;

                (string parentPrimary, string childPrimary) = PrimaryComponentsFor(j);
                if (string.IsNullOrEmpty(parentPrimary) || string.IsNullOrEmpty(childPrimary)) return;

                bool selected = _mateResolver.SelectPivotFace(j.MateName, parentPrimary, childPrimary);
                if (!selected)
                {
                    logger.Warn("HighlightJointPivotAxis: could not re-select the pivot face for mate '" +
                        j.MateName + "' (joint '" + j.Name + "') — mate may have been renamed/deleted since " +
                        "the suggestion was made.");
                }
            }
            catch (Exception ex) { logger.Warn("HighlightJointPivotAxis failed", ex); }
        }

        private void ClearJointControls()
        {
            if (_jointNameBox == null) return;
            _jointNameBox.Text = string.Empty;
            _jointTypeCombo.CurrentSelection = 0;
            _jointAxisXBox.Value = 0; _jointAxisYBox.Value = 0; _jointAxisZBox.Value = 1;
            _jointLimitLowerBox.Value = 0; _jointLimitUpperBox.Value = 0;
            UpdateJointFieldVisibility(UrdfJointType.Fixed);
            _currentJointCandidates = new List<MateJointClassification.Result>();
            ((IPropertyManagerPageControl)_jointPivotSourceLabel).Visible = false;
            ((IPropertyManagerPageControl)_jointPivotSourceCombo).Visible = false;
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
            j.IsSuggested = true;

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

        // Mate-suggested axis components carry double-precision noise from
        // real SW geometry (e.g. 1.39e-16 instead of exactly 0) — below the
        // 6-decimal-place precision Sw2gzRobotExporter.Fmt() already rounds
        // to on export, so the exported URDF is unaffected either way. This
        // only cleans up the live panel display so a suggested axis reads
        // as "1, 0, 0" instead of scientific-notation noise.
        private static double SnapNearZero(double v) => System.Math.Abs(v) < 1e-6 ? 0.0 : v;

        private static UrdfJointType ComboIndexToType(int idx) =>
            JointTypeOptions[System.Math.Max(0, System.Math.Min(JointTypeOptions.Length - 1, idx))];

        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            if (Id == IdJointPivotSourceCombo) { HandlePivotSourceChanged(Item); return; }
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
    }
}
#endif
