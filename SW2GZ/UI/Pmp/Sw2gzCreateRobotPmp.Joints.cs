/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzCreateRobotPmp — Joints step slice. One PMP listbox row per non-root
link's joint plus a shared detail form (name/type/axis/limit) that is
"checked out" for whichever row is selected and committed back via
CommitSelectedJointFromControls before the selection changes or the wizard
leaves the Joints step. Type and Limit auto-suggest from mate classification
(SwMateJointResolver.Resolve); Axis and pivot are a manual geometry pick
(click a cylindrical face or straight edge) — see
docs/superpowers/specs/2026-07-03-manual-axis-pivot-pick-design.md for why
axis moved off mate-geometry guessing.
*/
#if SW_INTEROP
using System;
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
        private const int IdJointAxisPicker    = 28;
        private const int IdJointAxisXBox      = 29;
        private const int IdJointAxisYBox      = 30;
        private const int IdJointAxisZBox      = 31;
        private const int IdJointLimitLabel    = 32;
        private const int IdJointLimitLowerBox = 33;
        private const int IdJointLimitUpperBox = 34;

        private static readonly string[] JointTypeLabels =
            { "Fixed", "Revolute", "Continuous", "Prismatic" };
        private static readonly UrdfJointType[] JointTypeOptions =
            { UrdfJointType.Fixed, UrdfJointType.Revolute, UrdfJointType.Continuous, UrdfJointType.Prismatic };

        // Distinct from Sw2gzCreateRobotPmp.MeshSelectionMark (0x4C0) — the
        // two never coexist (ShowStep clears between steps) but keeping
        // separate marks avoids a future foot-gun if that changes.
        private const int AxisPickSelectionMark = 0x4C1;

        private PropertyManagerPageListbox _jointsList;
        private PropertyManagerPageTextbox _jointNameBox;
        private PropertyManagerPageCombobox _jointTypeCombo;
        private PropertyManagerPageLabel _jointAxisLabel;
        private PropertyManagerPageSelectionbox _jointAxisPicker;
        private PropertyManagerPageNumberbox _jointAxisXBox;
        private PropertyManagerPageNumberbox _jointAxisYBox;
        private PropertyManagerPageNumberbox _jointAxisZBox;
        private PropertyManagerPageLabel _jointLimitLabel;
        private PropertyManagerPageNumberbox _jointLimitLowerBox;
        private PropertyManagerPageNumberbox _jointLimitUpperBox;

        private int _selectedJointIndex = -1;

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
            _jointAxisPicker = (PropertyManagerPageSelectionbox)grp.AddControl2(
                IdJointAxisPicker,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "", (short)leftEdge, visibleEnabled,
                "Click a cylindrical face (hole/pin) or a straight edge to read the axis and pivot off it");
            _jointAxisPicker.SingleEntityOnly = true;
            _jointAxisPicker.Height = 16;
            _jointAxisPicker.Mark = AxisPickSelectionMark;
            _jointAxisPicker.SetSelectionFilters((object)new swSelectType_e[]
                { swSelectType_e.swSelFACES, swSelectType_e.swSelEDGES });
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

        // The joint's parent/child link's own primary (first-assigned)
        // component — the same "first component defines the link's frame"
        // convention used everywhere else.
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

                // Type + Limit only — axis/pivot is always a manual pick
                // now (see file header); IsSuggested stays false so a
                // moving joint the mate walk found still prompts the user
                // to pick its axis, rather than silently looking "done".
                if (!suggestion.Found || suggestion.Type == UrdfJointType.Fixed) continue;

                j.Type = suggestion.Type;
                if (suggestion.LimitLower.HasValue) j.LimitLower = suggestion.LimitLower;
                if (suggestion.LimitUpper.HasValue) j.LimitUpper = suggestion.LimitUpper;
                j.MateName = suggestion.MateName;
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
            // Drop whatever was selected for the PREVIOUS joint row — the
            // axis picker itself is the live highlight now (no separate
            // re-select-by-mate-name step), so a stale pick lingering here
            // would look like it belongs to the newly loaded joint.
            try { _modelDoc.ClearSelection2(true); }
            catch (Exception ex) { logger.Warn("LoadJointIntoControls: clearing axis-picker selection failed", ex); }
        }

        // User clicked a cylindrical face or straight edge in the Axis
        // picker — read axis direction + pivot point directly off it and
        // write them into this joint. Replaces mate-geometry guessing
        // entirely (see file header).
        private void HandleAxisPicked()
        {
            if (_selectedJointIndex < 0 || _selectedJointIndex >= _liveDoc.Robot.Joints.Count) return;
            try
            {
                var selMgr = (ISelectionMgr)_modelDoc.SelectionManager;
                if (selMgr == null || selMgr.GetSelectedObjectCount2(AxisPickSelectionMark) != 1) return;

                object entity = selMgr.GetSelectedObject6(1, AxisPickSelectionMark);
                Component2 owner = selMgr.GetSelectedObjectsComponent3(1, AxisPickSelectionMark);
                if (entity == null || owner == null) return;

                if (!_mateResolver.TryExtractAxisFromSelection(entity, owner, out var axis, out var origin))
                {
                    logger.Warn("HandleAxisPicked: picked entity wasn't a cylindrical face or straight edge, or its geometry couldn't be read.");
                    return;
                }

                JointDef j = _liveDoc.Robot.Joints[_selectedJointIndex];
                j.SetAxis(axis);
                j.SetMatePoint(origin);
                j.IsSuggested = true;

                _jointAxisXBox.Value = SnapNearZero(axis.X);
                _jointAxisYBox.Value = SnapNearZero(axis.Y);
                _jointAxisZBox.Value = SnapNearZero(axis.Z);
            }
            catch (Exception ex) { logger.Warn("HandleAxisPicked failed", ex); }
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
            ((IPropertyManagerPageControl)_jointAxisPicker).Visible = showAxis;
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
