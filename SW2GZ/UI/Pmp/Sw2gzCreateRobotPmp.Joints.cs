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

        private static readonly string[] JointTypeLabels =
            { "Fixed", "Revolute", "Continuous", "Prismatic" };
        private static readonly UrdfJointType[] JointTypeOptions =
            { UrdfJointType.Fixed, UrdfJointType.Revolute, UrdfJointType.Continuous, UrdfJointType.Prismatic };

        // Distinct from Sw2gzCreateRobotPmp.MeshSelectionMark (0x4C0) so a
        // pivot-axis selection is never confused with a Links-step mesh
        // selection — they don't coexist today (ShowStep clears between
        // steps) but keeping separate marks costs nothing and avoids a
        // future foot-gun if that ever changes.
        private const int PivotAxisSelectionMark = 0x4C1;

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

        private void RefreshJointsList()
        {
            if (_jointsList == null) return;
            foreach (JointDef j in _liveDoc.Robot.Joints)
            {
                if (j.IsSuggested) continue;
                LinkDef parentLink = _liveDoc.Robot.Links.FirstOrDefault(l => l.Name == j.ParentLink);
                LinkDef childLink = _liveDoc.Robot.Links.FirstOrDefault(l => l.Name == j.ChildLink);
                string parentPrimary = parentLink?.ComponentIds?.FirstOrDefault();
                string childPrimary = childLink?.ComponentIds?.FirstOrDefault();
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
            HighlightJointPivotAxis(j);
        }

        // Highlights the selected joint's pivot axis as a yellow (pending
        // suggestion) or neutral (confirmed) line in the SW viewport, reusing
        // the same "only the active selection" pattern as HighlightLinkMesh.
        // SPIKE (Task 5 of the mate-joint-suggestion plan): the design spec
        // deliberately left the exact rendering mechanism open. Primary
        // candidate implemented here: select SW's own system-generated
        // Temporary Axis (the same entity toggled by View > Temporary Axes
        // for any cylindrical/conical face) rather than creating new sketch
        // geometry — closest in spirit to HighlightLinkMesh's plain-selection
        // approach.
        //
        // UNCERTAIN — flag for the live tester:
        //   1) Entity targeting: IModelDocExtension.SelectByID2's Name param
        //      for a bare, feature-less temp axis is "" (there is no feature
        //      name to give it — a temp axis is not a tree feature). With
        //      Name == "", SW hit-tests the given (X,Y,Z) point against
        //      entities of the requested Type and picks the nearest match —
        //      this is the exact pattern this codebase's own legacy code
        //      already uses for "" + "EXTSKETCHPOINT" (see
        //      ExportHelperExtension.cs). j.MatePointX/Y/Z is the concentric
        //      mate's cylinder-axis origin in the ASSEMBLY frame (see
        //      SwMateJointResolver/MateJointClassification), which should sit
        //      exactly ON the temp axis line SW would hit-test against — but
        //      this has never been run live, so it's unverified whether
        //      SelectByID2 actually resolves a TEMPAXIS this way (vs.
        //      requiring the axis to be made visible first via
        //      IModelDoc2.ViewTempAxes(true), or vs. needing a component-
        //      qualified Name like "compName@asmName" the way component-
        //      scoped face/edge selects sometimes do).
        //   2) Selection-type string: "TEMPAXIS" (singular) is the string
        //      SelectByID2 expects for this entity kind; swSelTEMPAXES is the
        //      matching swSelectType_e enum member but SelectByID2's Type
        //      parameter takes the STRING name (this codebase's existing
        //      "AXIS"/"COORDSYS"/"SKETCH" SelectByID2 calls all use strings,
        //      not the enum cast to int) — unverified against a real
        //      TEMPAXIS pick in this SW version.
        //   3) Coloring: this codebase has no existing "color a selection"
        //      precedent anywhere (grepped SW2GZ/SwSurface, SW2GZ/UI,
        //      URDFExport — none). A temp axis is a system-generated display
        //      artifact, not a real body/face IEntity, so the usual
        //      IEntity.SetMaterialPropertyValues2 per-entity coloring call
        //      does not apply to it, and no confirmed per-selection color
        //      setter for temp axes was found. Rather than fabricate an
        //      unverified call, this implementation deliberately stops at
        //      "select the temp axis" and relies on SW's default selection
        //      highlight — still real visual feedback, just without the
        //      yellow/neutral distinction until the live tester confirms a
        //      working color call (see the method body for the full note).
        //   4) If TEMPAXIS selection turns out not to render visibly at all
        //      (e.g. temp axes are hidden by default and SelectByID2 can't
        //      select a hidden entity), the documented fallback per the plan
        //      is to fall back to a transient sketch line/point at
        //      j.MatePointX/Y/Z instead of a temp-axis select — that is
        //      explicitly NOT implemented here (out of scope for this spike
        //      candidate) and would need a follow-up task if Step 2 fails.
        private void HighlightJointPivotAxis(JointDef j)
        {
            try
            {
                _modelDoc.ClearSelection2(true);
                if (j == null || j.Type == UrdfJointType.Fixed || !j.HasMatePoint) return;

                LinkDef childLink = _liveDoc.Robot.Links.FirstOrDefault(l => l.Name == j.ChildLink);
                string childPrimary = childLink?.ComponentIds?.FirstOrDefault();
                if (string.IsNullOrEmpty(childPrimary)) return;

                // Note: IsSuggested flips true almost immediately in normal use — any
                // listbox click commits the PREVIOUSLY selected joint first (via
                // CommitSelectedJointFromControls, which unconditionally sets
                // IsSuggested = true), so the "pending" (yellow) state is only
                // observable in the brief window right after RefreshJointsList()
                // first applies a suggestion, before any other row is clicked. If a
                // live tester can't reproduce the pending/yellow case, this is why —
                // not a sign the color logic itself is broken.
                System.Drawing.Color lineColor = j.IsSuggested
                    ? System.Drawing.Color.Yellow
                    : System.Drawing.Color.FromArgb(180, 180, 180);

                // Hit-test for the temp axis at the mate's cylinder-origin
                // point — see uncertainty note (1)/(2) above. Mark reuses
                // MeshSelectionMark's sibling range so this selection is
                // distinguishable from the Links-step mesh selection if both
                // ever needed to coexist (they don't today — ShowStep clears
                // between steps — but keeps the mark namespace tidy).
                bool selected = _modelDoc.Extension.SelectByID2(
                    "", "TEMPAXIS",
                    j.MatePointX, j.MatePointY, j.MatePointZ,
                    false, PivotAxisSelectionMark, null, 0);

                if (!selected)
                {
                    logger.Warn("HighlightJointPivotAxis: SelectByID2 could not resolve a TEMPAXIS " +
                        "near (" + j.MatePointX + ", " + j.MatePointY + ", " + j.MatePointZ +
                        ") for joint '" + j.Name + "' (child component '" + childPrimary + "') — " +
                        "see Step 2 live-check notes.");
                    return;
                }

                // See uncertainty note (3) above. A temporary axis is a
                // system-generated display artifact, not a real body/face
                // entity — it does NOT implement IEntity, so the usual
                // IEntity.SetMaterialPropertyValues2 per-entity coloring call
                // (the standard way to color a face/edge/body in SW COM)
                // does not apply here. The one documented, targeted (i.e.
                // not a global preference) coloring surface for an arbitrary
                // *current selection* is ModelDocExtension.SelectByID2's own
                // selection combined with IModelDoc2.Extension.
                // SetSelectionColor... but no such per-selection color setter
                // for temp axes is confirmed to exist in the interop version
                // this project references (grepped this whole repo: no
                // prior SetElementColor2/SetMaterialPropertyValues2/entity-
                // color precedent anywhere to copy). Rather than fabricate a
                // call that looks plausible but is unverified, this spike
                // intentionally stops at "select the temp axis" (still gives
                // real visual feedback via SW's default selection highlight)
                // and leaves the yellow-vs-neutral color distinction for the
                // live tester to confirm one way or the other — see Step 2.
                // If SW does expose per-entity coloring for TEMPAXIS in this
                // version (e.g. via IFeature-like GetDefinition/
                // ModifyDefinition on the axis, or a Selection-object method
                // not enumerated above), the live tester should report the
                // working call so it can be wired in as a fast-follow.
                logger.Info("HighlightJointPivotAxis: selected TEMPAXIS for joint '" + j.Name +
                    "' (child component '" + childPrimary + "'); explicit " +
                    (j.IsSuggested ? "yellow" : "neutral") +
                    " coloring NOT applied — relying on SW's default selection highlight " +
                    "pending live confirmation of a working per-entity color call (see Step 2).");
                _ = lineColor; // computed for when a real color call is confirmed live; unused until then.
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
