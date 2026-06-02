/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Native SolidWorks PropertyManagerPage (left panel) export shell for SW2GZ.

This is the "navigation shell" increment: a multi-step wizard rendered inside ONE
PropertyManagerPage, with working Back/Next navigation and placeholder content
only. NO backend / export logic is wired here — each step is just a heading label
and a short description. Real controls and logic are added one step at a time in
later increments.

Modelled on URDFExport\ExportPropertyManager.cs and GeometryPropertyManager.cs
(the established PMP patterns in this codebase): swApp.CreatePropertyManagerPage,
PMPage.AddGroupBox per step, PMGroup.AddControl2 for labels / buttons, a Show()
method, and the full PropertyManagerPage2Handler9 handler interface.

Navigation approach: one PropertyManagerPageGroup per step, all created up front.
Only the current step's group is visible (group.Visible = true/false); the rest
are hidden. A header label "Step N of 5 — <Name>" updates on navigation. Two
push-button controls ("< Back" and "Next >") live in a persistent navigation
group at the bottom and drive currentStep in OnButtonPress. This was chosen over
PMP tabs because group show/hide gives a true linear wizard feel (one visible
panel at a time) with reliable Back/Next semantics, and over the standard OK/
Cancel-as-navigation because we want OK/Cancel to keep their normal close meaning.

Guarded entirely by #if SW_INTEROP. When the symbol is undefined the class
collapses to a tiny throwing skeleton so the file still compiles outside a
SolidWorks workstation (consistent with GeometryPropertyManager.cs).
*/

using System;

#if SW_INTEROP
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2GZ.Ros2;
using SW2GZ.Utilities;
using System.Runtime.InteropServices;
using System.Windows.Forms;
#endif

namespace SW2GZ.URDFExport
{
#if SW_INTEROP
    [ComVisible(true)]
    [Serializable]
    public sealed class Sw2gzExportPmp : PropertyManagerPage2Handler9
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly SldWorks swApp;

        // The active assembly document (target of the checkpoint save/load).
        private readonly ModelDoc2 model;

        // Live wizard state — loaded on open, saved on each Next.
        private Sw2gzExportConfig config = new Sw2gzExportConfig();

        // PMP infrastructure.
        private readonly PropertyManagerPage2 PMPage;

        // One group per step, all created up front; only the current one is shown.
        private PropertyManagerPageGroup[] PMStepGroups;
        private PropertyManagerPageLabel PMLabelHeader;
        private PropertyManagerPageButton PMButtonBack;
        private PropertyManagerPageButton PMButtonNext;

        // Step 1 (Mode) radio buttons, indexed by ExportMode order.
        private PropertyManagerPageOption PMOptRobotPackage;
        private PropertyManagerPageOption PMOptSdfModel;
        private PropertyManagerPageOption PMOptSdfWorld;

        // Step model — placeholder headings + descriptions only (no real controls).
        private static readonly string[] StepNames =
        {
            "Mode",
            "Output",
            "Geometry",
            "Joints",
            "Review",
        };

        private static readonly string[] StepDescriptions =
        {
            "Choose what to generate: Robot package / Gz model / Gz world.",
            "Where to save + package name + ROS 2 / Gz versions.",
            "Define links: assign bodies to each link.",
            "Define joints: type, axis/orientation, naming.",
            "Summary + Finish.",
        };

        // Each control needs a unique ID. Header + nav buttons get fixed IDs; each
        // step group + its two labels get IDs derived from the step index.
        private const int HeaderGroupID = 1;
        private const int HeaderLabelID = 2;
        private const int NavGroupID = 3;
        private const int ButtonBackID = 4;
        private const int ButtonNextID = 5;

        // Step controls start well above the fixed IDs (20 IDs of headroom per step).
        private const int StepIdBase = 100;

        private int StepGroupId(int step) => StepIdBase + step * 20;
        private int StepHeadingId(int step) => StepIdBase + step * 20 + 1;
        private int StepDescId(int step) => StepIdBase + step * 20 + 2;

        // Step 1 (Mode) option IDs.
        private const int OptRobotPackageID = StepIdBase + 0 * 20 + 3;
        private const int OptSdfModelID     = StepIdBase + 0 * 20 + 4;
        private const int OptSdfWorldID     = StepIdBase + 0 * 20 + 5;

        private int StepCount => StepNames.Length;

        // Index of the current step (0-based).
        private int currentStep;

        public Sw2gzExportPmp(SldWorks swApp, ModelDoc2 model)
        {
            this.swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.currentStep = 0;

            int longerrors = 0;
            const string pageTitle = "SW2GZ — Export to ROS 2 / Gz";
            long options =
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_HandleKeystrokes;

            PMPage = (PropertyManagerPage2)swApp.CreatePropertyManagerPage(
                pageTitle, (int)options, this, ref longerrors);

            if (longerrors == (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
            {
                BuildPage();
            }
            else
            {
                logger.Error("Failed to create the SW2GZ export PropertyManager page. Error: " +
                    longerrors);
                MessageBox.Show("There was a problem setting up the SW2GZ export panel.\n" +
                    "Email your maintainer with the log file found at " + Logger.GetFileName());
            }
        }

        public void Show()
        {
            PMPage.Show2(0);
        }

        public void Close(bool ok)
        {
            PMPage.Close(ok);
        }

        // ───────────────────────────── page construction ─────────────────────

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

            // ── Header group: "Step N of 5 — <Name>" ──────────────────────────
            PropertyManagerPageGroup headerGroup =
                (PropertyManagerPageGroup)PMPage.AddGroupBox(
                    HeaderGroupID, "Progress", grpOptions);
            PMLabelHeader = (PropertyManagerPageLabel)headerGroup.AddControl2(
                HeaderLabelID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");

            // ── One group per step, each with a heading + description label ────
            PMStepGroups = new PropertyManagerPageGroup[StepCount];
            for (int step = 0; step < StepCount; step++)
            {
                PropertyManagerPageGroup stepGroup =
                    (PropertyManagerPageGroup)PMPage.AddGroupBox(
                        StepGroupId(step), StepNames[step], grpOptions);
                PMStepGroups[step] = stepGroup;

                stepGroup.AddControl2(
                    StepHeadingId(step),
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    StepNames[step], (short)leftEdge, visibleEnabled, "");

                switch (step)
                {
                    case 0:
                        BuildModeStep(stepGroup, indent, visibleEnabled);
                        break;
                    default:
                        // Generic placeholder for steps not yet implemented.
                        stepGroup.AddControl2(
                            StepDescId(step),
                            (short)swPropertyManagerPageControlType_e.swControlType_Label,
                            StepDescriptions[step], (short)indent, visibleEnabled, "");
                        break;
                }
            }

            // ── Navigation group: Back / Next buttons ─────────────────────────
            PropertyManagerPageGroup navGroup =
                (PropertyManagerPageGroup)PMPage.AddGroupBox(
                    NavGroupID, "Navigation", grpOptions);

            PMButtonBack = (PropertyManagerPageButton)navGroup.AddControl2(
                ButtonBackID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "< Back", 0, visibleEnabled, "Go to the previous step");
            ((IPropertyManagerPageControl)PMButtonBack).Width = 95;

            PMButtonNext = (PropertyManagerPageButton)navGroup.AddControl2(
                ButtonNextID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Next >", 0, visibleEnabled, "Go to the next step");
            ((IPropertyManagerPageControl)PMButtonNext).Width = 95;

            // Reflect any loaded checkpoint onto the controls.
            SeedModeControls();

            // Seed the UI on the first step.
            ShowStep(0);
        }

        // Step 1 — three mutually-exclusive radio buttons selecting the export
        // mode. SolidWorks treats a contiguous run of option controls in one
        // group as mutually exclusive; OnOptionCheck mirrors the pick into config.
        private void BuildModeStep(PropertyManagerPageGroup group, int indent, int visibleEnabled)
        {
            PMOptRobotPackage = (PropertyManagerPageOption)group.AddControl2(
                OptRobotPackageID,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Robot package (URDF/Xacro)", (short)indent, visibleEnabled,
                "Generate a ROS 2 robot package with URDF/Xacro");

            PMOptSdfModel = (PropertyManagerPageOption)group.AddControl2(
                OptSdfModelID,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Gz asset (SDF model)", (short)indent, visibleEnabled,
                "Generate a standalone Gazebo SDF model");

            PMOptSdfWorld = (PropertyManagerPageOption)group.AddControl2(
                OptSdfWorldID,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Gz world (SDF world)", (short)indent, visibleEnabled,
                "Generate a Gazebo SDF world containing the model");
        }

        // Reflects config.Mode onto the radio buttons' Checked state.
        private void SeedModeControls()
        {
            if (PMOptRobotPackage == null) return;
            PMOptRobotPackage.Checked = config.Mode == ExportMode.RobotPackage;
            PMOptSdfModel.Checked = config.Mode == ExportMode.SdfModel;
            PMOptSdfWorld.Checked = config.Mode == ExportMode.SdfWorld;
        }

        // ───────────────────────────── navigation ────────────────────────────

        // Shows only the requested step's group, hides the rest, and refreshes the
        // header + Back/Next button state.
        private void ShowStep(int step)
        {
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;
            currentStep = step;

            for (int i = 0; i < StepCount; i++)
            {
                // Group boxes expose Visible directly on IPropertyManagerPageGroup;
                // they do NOT support IPropertyManagerPageControl (that's leaf controls
                // only), so casting a group to it throws E_NOINTERFACE.
                PMStepGroups[i].Visible = (i == currentStep);
            }

            PMLabelHeader.Caption =
                "Step " + (currentStep + 1) + " of " + StepCount +
                " — " + StepNames[currentStep];

            // Back disabled on the first step.
            ((IPropertyManagerPageControl)PMButtonBack).Enabled = currentStep > 0;

            // Next becomes "Finish" on the last step.
            bool isLast = currentStep == StepCount - 1;
            PMButtonNext.Caption = isLast ? "Finish" : "Next >";
        }

        private void GoBack()
        {
            if (currentStep > 0)
            {
                ShowStep(currentStep - 1);
            }
        }

        private void GoNext()
        {
            if (currentStep < StepCount - 1)
            {
                ShowStep(currentStep + 1);
            }
            else
            {
                // Finish — no backend wired yet this increment.
                logger.Info("SW2GZ export shell Finish pressed (no backend wired yet)");
                swApp.SendMsgToUser("Export is not wired up yet — this is the navigation shell.");
                PMPage.Close(true);
            }
        }

        // ───────────────────────────── handler interface ─────────────────────

        void IPropertyManagerPage2Handler9.AfterActivation()
        {
            ShowStep(currentStep);
        }

        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            try
            {
                switch (Id)
                {
                    case ButtonBackID: GoBack(); break;
                    case ButtonNextID: GoNext(); break;
                    default: break;
                }
            }
            catch (Exception e)
            {
                logger.Error("Exception handling SW2GZ export panel button " + Id, e);
                MessageBox.Show("There was a problem with the SW2GZ export panel:\n\"" +
                    e.Message + "\"");
            }
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            if (Reason ==
                (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
            {
                logger.Info("SW2GZ export panel closed with OK");
            }
            else
            {
                logger.Info("SW2GZ export panel cancelled");
            }
        }

        // ───────────────── remaining handler members (no-ops) ─────────────────

        void IPropertyManagerPage2Handler9.AfterClose() { }
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
        void IPropertyManagerPage2Handler9.OnOptionCheck(int Id)
        {
            switch (Id)
            {
                case OptRobotPackageID: config.Mode = ExportMode.RobotPackage; break;
                case OptSdfModelID:     config.Mode = ExportMode.SdfModel; break;
                case OptSdfWorldID:     config.Mode = ExportMode.SdfWorld; break;
                default: break;
            }
        }
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
#else
    // Skeleton so the file compiles when SW_INTEROP is undefined (e.g. outside a
    // SolidWorks workstation). The real implementation above is COM-bound.
    public sealed class Sw2gzExportPmp
    {
        public Sw2gzExportPmp(object swApp)
        {
            throw new NotSupportedException(
                "Sw2gzExportPmp requires SW_INTEROP (a SolidWorks add-in build).");
        }

        public void Show() =>
            throw new NotSupportedException(
                "Sw2gzExportPmp requires SW_INTEROP (a SolidWorks add-in build).");
    }
#endif
}
