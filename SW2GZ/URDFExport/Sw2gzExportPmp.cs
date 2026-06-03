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
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Ros2;
using SW2GZ.SwSurface;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.UI;
using SW2GZ.Utilities;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
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

        // Step 2 (Output) controls.
        private PropertyManagerPageTextbox PMTextOutputFolder;
        private PropertyManagerPageButton PMButtonBrowse;
        private PropertyManagerPageTextbox PMTextPackageName;
        private PropertyManagerPageTextbox PMTextAuthor;
        private PropertyManagerPageTextbox PMTextEmail;
        private PropertyManagerPageCombobox PMComboLicense;

        // Step 3 (Links) controls — embedded WinForms hierarchy tree + pick funnel.
        private PropertyManagerPageWindowFromHandle PMTreeHandle;
        private LinkTreeView linkTree;
        private PropertyManagerPageSelectionbox PMPickFunnel;
        private PropertyManagerPageButton PMButtonAddLink;
        private PropertyManagerPageButton PMButtonRemoveLink;
        private PropertyManagerPageLabel PMLabelLinkMass;
        private PropertyManagerPageLabel PMLabelLinkValidation;
        private LinkDef activeLink;

        private const int LinkSelectionMark = 3;
        private IMassProperties massProps;                  // combined-mass readout
        private readonly List<string> allComponentIds = new List<string>();

        // Step 4 (Joints) controls — structural only (type, axis, motion limits).
        // A selector + Prev/Next + "Joint i of N" let the user walk every joint so
        // none is missed. Limits are plain textboxes (reuse the proven textbox path).
        private PropertyManagerPageListbox PMListJoints;
        private PropertyManagerPageListbox PMListMates;
        private PropertyManagerPageLabel PMLabelDetailLinks;
        private PropertyManagerPageLabel PMLabelDetailMate;
        private PropertyManagerPageLabel PMLabelDetailType;
        private PropertyManagerPageLabel PMLabelDetailLimits;

        // Index of the joint currently selected in the list (-1 = none).
        private int activeJointIndex = -1;

        // All mates in the assembly, backing the mate list (indexed by position).
        private List<MateInfo> allMates = new List<MateInfo>();

        // SPDX license choices for the optional License dropdown. First entry is
        // blank ("none"); the combo is editable so a custom id can be typed.
        private static readonly string[] LicenseChoices =
        {
            "", "MIT", "Apache-2.0", "BSD-3-Clause", "BSD-2-Clause",
            "GPL-3.0-only", "LGPL-3.0-only", "MPL-2.0", "Proprietary",
        };

        // Step model — placeholder headings + descriptions only (no real controls).
        private static readonly string[] StepNames =
        {
            "Mode",
            "Output",
            "Create base model structure",
            "Joints",
            "Review",
        };

        private static readonly string[] StepDescriptions =
        {
            "Choose what to generate: Robot package / Gz model / Gz world.",
            "Where to save + package name + ROS 2 / Gz versions.",
            "Build the link tree and assign geometry to each link.",
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

        // Step 2 (Output) control IDs (step index 1 → base 120).
        private const int LabelOutputFolderID = StepIdBase + 1 * 20 + 2;
        private const int TextOutputFolderID  = StepIdBase + 1 * 20 + 3;
        private const int ButtonBrowseID      = StepIdBase + 1 * 20 + 4;
        private const int LabelPackageNameID  = StepIdBase + 1 * 20 + 5;
        private const int TextPackageNameID   = StepIdBase + 1 * 20 + 6;
        private const int LabelAuthorID       = StepIdBase + 1 * 20 + 7;
        private const int TextAuthorID        = StepIdBase + 1 * 20 + 8;
        private const int LabelEmailID        = StepIdBase + 1 * 20 + 9;
        private const int TextEmailID         = StepIdBase + 1 * 20 + 10;
        private const int LabelLicenseID      = StepIdBase + 1 * 20 + 11;
        private const int TextLicenseID       = StepIdBase + 1 * 20 + 12;
        private const int LabelTargetsID      = StepIdBase + 1 * 20 + 13;

        // Step 3 (Links) control IDs (step index 2 → base 140).
        private const int TreeHandleID          = StepIdBase + 2 * 20 + 2;
        private const int PickFunnelID          = StepIdBase + 2 * 20 + 3;
        private const int ButtonAddLinkID       = StepIdBase + 2 * 20 + 4;
        private const int ButtonRemoveLinkID    = StepIdBase + 2 * 20 + 5;
        private const int LabelLinkMassID       = StepIdBase + 2 * 20 + 6;
        private const int LabelLinkValidationID = StepIdBase + 2 * 20 + 7;

        // Step 4 (Joints) control IDs (step index 3 → base 160).
        private const int LabelJointInstrID       = StepIdBase + 3 * 20 + 2;
        private const int ListJointsID            = StepIdBase + 3 * 20 + 3;
        private const int LabelMatesCapID         = StepIdBase + 3 * 20 + 4;
        private const int ListMatesID             = StepIdBase + 3 * 20 + 5;
        private const int LabelDetailCapID        = StepIdBase + 3 * 20 + 6;
        private const int LabelDetailLinksID      = StepIdBase + 3 * 20 + 7;
        private const int LabelDetailMateID       = StepIdBase + 3 * 20 + 8;
        private const int LabelDetailTypeID        = StepIdBase + 3 * 20 + 9;
        private const int LabelDetailLimitsID     = StepIdBase + 3 * 20 + 10;

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
                config = Sw2gzConfigSerialization.Load(model);
                ApplyDefaults();
                massProps = new SolidWorksMassProperties(swApp, (AssemblyDoc)model);
                SeedLinksFromAssembly();
                BuildPage();
                ShowStep(config.LastStep);
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

                // The group-box title already names the step and the header group
                // shows "Step N of 5 — <Name>", so no redundant heading label here.
                switch (step)
                {
                    case 0:
                        BuildModeStep(stepGroup, leftEdge, indent, visibleEnabled);
                        break;
                    case 1:
                        BuildOutputStep(stepGroup, leftEdge, indent, visibleEnabled);
                        break;
                    case 2:
                        BuildLinksStep(stepGroup, leftEdge, indent, visibleEnabled);
                        break;
                    case 3:
                        BuildJointsStep(stepGroup, leftEdge, indent, visibleEnabled);
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
            ((IPropertyManagerPageControl)PMButtonBack).Width = 70;

            PMButtonNext = (PropertyManagerPageButton)navGroup.AddControl2(
                ButtonNextID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Next >", 0, visibleEnabled, "Go to the next step");
            ((IPropertyManagerPageControl)PMButtonNext).Width = 70;

            // Reflect any loaded checkpoint onto the controls.
            SeedModeControls();
            SeedOutputControls();

            // Seed the UI on the first step.
            ShowStep(0);
        }

        // Step 1 — three mutually-exclusive radio buttons selecting the export
        // mode. SolidWorks treats a contiguous run of option controls in one
        // group as mutually exclusive; OnOptionCheck mirrors the pick into config.
        private void BuildModeStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
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

        // Step 2 — output folder (+ Browse), package name, author/email/license,
        // and a read-only target note (ROS 2 Jazzy + Gz Harmonic are locked).
        //
        // Each field is a caption Label (left edge) above an indented Textbox.
        // The label Caption is set explicitly after creation — passing it only via
        // AddControl2 proved unreliable for some rows (labels rendered blank), so
        // we always set Caption on the returned control (matches the dynamic-label
        // pattern in GeometryPropertyManager).
        private void BuildOutputStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
        {
            int labelOpts = (int)swAddControlOptions_e.swControlOptions_Visible;

            AddFieldLabel(group, LabelOutputFolderID, "Output folder", leftEdge, labelOpts);
            PMTextOutputFolder = (PropertyManagerPageTextbox)group.AddControl2(
                TextOutputFolderID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "Folder the package is written to");
            PMButtonBrowse = (PropertyManagerPageButton)group.AddControl2(
                ButtonBrowseID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Browse...", (short)indent, visibleEnabled, "Choose the output folder");
            ((IPropertyManagerPageControl)PMButtonBrowse).Width = 90;

            AddFieldLabel(group, LabelPackageNameID, "Package name", leftEdge, labelOpts);
            PMTextPackageName = (PropertyManagerPageTextbox)group.AddControl2(
                TextPackageNameID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "ROS 2 package name (sanitized on export)");

            AddFieldLabel(group, LabelAuthorID, "Author", leftEdge, labelOpts);
            PMTextAuthor = (PropertyManagerPageTextbox)group.AddControl2(
                TextAuthorID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "Maintainer name for package.xml");

            AddFieldLabel(group, LabelEmailID, "Email (optional)", leftEdge, labelOpts);
            PMTextEmail = (PropertyManagerPageTextbox)group.AddControl2(
                TextEmailID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "Maintainer email for package.xml (optional)");

            AddFieldLabel(group, LabelLicenseID, "License (optional)", leftEdge, labelOpts);
            PMComboLicense = (PropertyManagerPageCombobox)group.AddControl2(
                TextLicenseID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)indent, visibleEnabled,
                "SPDX license id for package.xml (pick one or type your own)");
            // No EditBoxReadOnly style => editable; user may type a custom id.
            PMComboLicense.AddItems(LicenseChoices);

            PropertyManagerPageLabel targets = AddFieldLabel(group, LabelTargetsID,
                "Targets: ROS 2 Jazzy + Gz Sim Harmonic (fixed in this release)",
                leftEdge, labelOpts);
        }

        // Adds a caption label and sets its Caption explicitly (see BuildOutputStep).
        private PropertyManagerPageLabel AddFieldLabel(
            PropertyManagerPageGroup group, int id, string caption, int leftEdge, int labelOpts)
        {
            PropertyManagerPageLabel label = (PropertyManagerPageLabel)group.AddControl2(
                id,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                caption, (short)leftEdge, labelOpts, "");
            label.Caption = caption;
            return label;
        }

        private void SeedOutputControls()
        {
            if (PMTextOutputFolder == null) return;
            PMTextOutputFolder.Text = config.OutputFolder ?? "";
            PMTextPackageName.Text = config.PackageName ?? "";
            PMTextAuthor.Text = config.Author ?? "";
            PMTextEmail.Text = config.Email ?? "";
            SeedLicenseCombo();
        }

        // Selects config.License in the combo. A custom (saved) value not in the
        // preset list is appended and selected.
        private void SeedLicenseCombo()
        {
            string val = config.License ?? "";
            short idx = -1;
            for (short i = 0; i < LicenseChoices.Length; i++)
            {
                if (LicenseChoices[i] == val) { idx = i; break; }
            }
            if (idx < 0 && val.Length > 0)
            {
                PMComboLicense.AddItems(val);
                idx = (short)LicenseChoices.Length;
            }
            PMComboLicense.CurrentSelection = idx < 0 ? (short)0 : idx;
        }

        // Seeds blank fields with sensible defaults the first time the wizard runs
        // for a document: package name from the assembly file, output folder under
        // the user's Documents. A saved checkpoint always takes precedence.
        private void ApplyDefaults()
        {
            if (string.IsNullOrWhiteSpace(config.PackageName))
            {
                config.PackageName = DefaultPackageName();
            }
            if (string.IsNullOrWhiteSpace(config.OutputFolder))
            {
                config.OutputFolder = DefaultOutputFolder();
            }
        }

        // Assembly file name (no extension); falls back to the window title for an
        // unsaved document.
        private string DefaultPackageName()
        {
            try
            {
                string path = model.GetPathName();
                string name = !string.IsNullOrEmpty(path)
                    ? Path.GetFileNameWithoutExtension(path)
                    : model.GetTitle();
                return Path.GetFileNameWithoutExtension(name ?? "").Trim();
            }
            catch (Exception e)
            {
                logger.Warn("Could not derive default package name", e);
                return "";
            }
        }

        // Generic per-PC default: <Documents>\SW2GZ Exports.
        private string DefaultOutputFolder()
        {
            try
            {
                string docs = System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.MyDocuments);
                return string.IsNullOrEmpty(docs) ? "" : Path.Combine(docs, "SW2GZ Exports");
            }
            catch (Exception e)
            {
                logger.Warn("Could not derive default output folder", e);
                return "";
            }
        }

        // ───────────────────────────── step 3: links ─────────────────────────

        // Enumerates top-level components (records allComponentIds) and, only when
        // the checkpoint has no links yet, seeds a single tree: the grounded (or
        // first) component is the root/base, the rest are parented under it.
        private void SeedLinksFromAssembly()
        {
            allComponentIds.Clear();
            object[] comps = (object[])((AssemblyDoc)model).GetComponents(true);
            var top = new List<Component2>();
            if (comps != null)
            {
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (c.IsSuppressed()) continue;
                    top.Add(c);
                    allComponentIds.Add(c.Name2);
                }
            }

            if (config.Links == null) config.Links = new List<LinkDef>();
            if (config.Links.Count > 0) return;   // resume from checkpoint

            int rootIdx = 0;
            for (int i = 0; i < top.Count; i++)
            {
                try { if (top[i].IsFixed()) { rootIdx = i; break; } } catch { }
            }
            string rootName = top.Count > 0
                ? RosNameSanitizer.Sanitize(top[rootIdx].Name2).Value : "base_link";

            for (int i = 0; i < top.Count; i++)
            {
                config.Links.Add(new LinkDef
                {
                    Name = RosNameSanitizer.Sanitize(top[i].Name2).Value,
                    ComponentIds = new List<string> { top[i].Name2 },
                    ParentName = i == rootIdx ? "" : rootName,
                });
            }
        }

        // Step 3 — embedded WinForms link-hierarchy tree + pick funnel + Add/Remove.
        private void BuildLinksStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
        {
            int labelOpts = (int)swAddControlOptions_e.swControlOptions_Visible;

            // Size the tree to the link count so small robots don't show a big empty
            // box and large ones still scroll within a sensible cap.
            int rows = config.Links != null ? config.Links.Count : 1;
            int treeHeight = System.Math.Min(260, System.Math.Max(90, rows * 20 + 30));
            linkTree = new LinkTreeView { Height = treeHeight, Visible = true };
            linkTree.ActiveLinkChanged += (s, l) =>
            {
                activeLink = l;
                if (activeLink != null) UpdateMassReadout(activeLink);
                UpdateValidationLabel();
                LoadLinkSelection(activeLink);   // show + highlight this link's parts
            };
            linkTree.LinksChanged += (s, e) => UpdateValidationLabel();

            AddFieldLabel(group, LabelLinkMassID + 100,
                "Tree: click a link, then pick its parts in the viewport. " +
                "Drag to re-parent, F2 to rename, right-click to set base.",
                leftEdge, labelOpts);

            PMTreeHandle = (PropertyManagerPageWindowFromHandle)group.AddControl2(
                TreeHandleID, (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "Link tree", (short)leftEdge, visibleEnabled,
                "Drag to re-parent; F2 to rename; right-click to set the base link");
            PMTreeHandle.Height = treeHeight;
            PMTreeHandle.SetWindowHandlex64(linkTree.Handle.ToInt64());

            PMPickFunnel = (PropertyManagerPageSelectionbox)group.AddControl2(
                PickFunnelID, (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Parts for the selected link", (short)leftEdge, visibleEnabled,
                "Pick components in the viewport — assigned to the selected link instantly");
            var filters = new swSelectType_e[]
            {
                swSelectType_e.swSelCOMPONENTS, swSelectType_e.swSelSOLIDBODIES,
            };
            PMPickFunnel.SingleEntityOnly = false;
            PMPickFunnel.AllowMultipleSelectOfSameEntity = false;
            PMPickFunnel.Height = 24;
            PMPickFunnel.Mark = LinkSelectionMark;
            PMPickFunnel.SetSelectionFilters((object)filters);

            PMButtonAddLink = AddLinkButton(group, ButtonAddLinkID, "Add link", indent, visibleEnabled);
            PMButtonRemoveLink = AddLinkButton(group, ButtonRemoveLinkID, "Remove link", indent, visibleEnabled);

            PMLabelLinkMass = AddFieldLabel(group, LabelLinkMassID, "", leftEdge, labelOpts);
            PMLabelLinkValidation = AddFieldLabel(group, LabelLinkValidationID, "", leftEdge, labelOpts);

            linkTree.SetLinks(config.Links);
            // Auto-select the base so picking geometry works immediately (flow fix).
            List<LinkDef> roots = LinkHierarchy.Roots(config.Links);
            if (roots.Count > 0) linkTree.SelectByLinkName(roots[0].Name);
            UpdateValidationLabel();
        }

        private PropertyManagerPageButton AddLinkButton(
            PropertyManagerPageGroup group, int id, string caption, int indent, int visibleEnabled)
        {
            var b = (PropertyManagerPageButton)group.AddControl2(
                id, (short)swPropertyManagerPageControlType_e.swControlType_Button,
                caption, (short)indent, visibleEnabled, caption);
            ((IPropertyManagerPageControl)b).Width = 110;
            return b;
        }

        // The parts box is a live mirror of the active link's components: it shows
        // (and viewport-highlights) what's assigned, and editing it reassigns. When
        // the box and the link already agree (e.g. after a programmatic reselect),
        // nothing happens — that guard breaks the select<->event feedback loop.
        private void OnFunnelChanged()
        {
            if (activeLink == null || linkTree == null) return;
            List<string> box = ReadSelectionBoxNames();
            if (SameSet(box, activeLink.ComponentIds)) return;

            // Move any newly-added component off whatever link previously held it.
            foreach (string id in box)
                foreach (LinkDef l in config.Links)
                    if (l != activeLink) l.ComponentIds.Remove(id);
            activeLink.ComponentIds = box;   // box is the source of truth (adds + removes)

            linkTree.Rebuild();              // refreshes [N parts] + colors; re-selects active
            UpdateMassReadout(activeLink);
            UpdateValidationLabel();
        }

        // Selects the active link's components in the viewport (into the parts box,
        // under our mark) so they are visible + highlighted when the link is chosen.
        private void LoadLinkSelection(LinkDef link)
        {
            if (link == null) return;
            model.ClearSelection2(true);
            ISelectionMgr selMgr = (ISelectionMgr)model.SelectionManager;
            SelectData sd = selMgr.CreateSelectData();
            sd.Mark = LinkSelectionMark;
            object[] comps = (object[])((AssemblyDoc)model).GetComponents(true);
            if (comps != null)
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (link.ComponentIds.Contains(c.Name2)) c.Select4(true, sd, false);
                }
            if (PMPickFunnel != null) PMPickFunnel.SetSelectionFocus();
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
            List<LinkDef> roots = LinkHierarchy.Roots(config.Links);
            string parent = activeLink?.Name ?? (roots.Count > 0 ? roots[0].Name : "");
            var link = new LinkDef { Name = UniqueLinkName(), ParentName = parent };
            config.Links.Add(link);
            linkTree.SetLinks(config.Links);
            linkTree.SelectByLinkName(link.Name);
            UpdateValidationLabel();
        }

        private void RemoveLink()
        {
            if (activeLink == null || config.Links.Count <= 1) return;
            string removed = activeLink.Name, parent = activeLink.ParentName ?? "";
            foreach (LinkDef l in config.Links)
                if (l.ParentName == removed) l.ParentName = parent;   // children adopt grandparent
            config.Links.Remove(activeLink);
            if (LinkHierarchy.Roots(config.Links).Count == 0 && config.Links.Count > 0)
                config.Links[0].ParentName = "";                      // ensure a root remains
            activeLink = null;
            linkTree.SetLinks(config.Links);
            UpdateValidationLabel();
        }

        private string UniqueLinkName()
        {
            int n = config.Links.Count + 1;
            while (true)
            {
                string candidate = RosNameSanitizer.Sanitize("link_" + n).Value;
                bool taken = false;
                foreach (LinkDef l in config.Links) if (l.Name == candidate) { taken = true; break; }
                if (!taken) return candidate;
                n++;
            }
        }

        private void UpdateMassReadout(LinkDef link)
        {
            if (PMLabelLinkMass == null) return;
            double total = 0; bool missing = false;
            foreach (string id in link.ComponentIds)
            {
                try { total += massProps.Get(ComponentPathForId(id)).Mass; }
                catch (Exception) { missing = true; }
            }
            string s = link.ComponentIds.Count + " component(s), mass " + total.ToString("0.###") + " kg";
            if (missing) s += " (set material on all parts)";
            PMLabelLinkMass.Caption = s;
        }

        // Resolve a stored Name2 id to the component's part path for IMassProperties.
        private string ComponentPathForId(string name2)
        {
            object[] comps = (object[])((AssemblyDoc)model).GetComponents(true);
            if (comps != null)
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (c.Name2 == name2) return c.GetPathName();
                }
            return name2;
        }

        private void UpdateValidationLabel()
        {
            if (PMLabelLinkValidation == null) return;
            List<string> issues = LinkDefValidator.Validate(config.Links, allComponentIds);
            PMLabelLinkValidation.Caption = issues.Count == 0
                ? "All components assigned."
                : issues.Count + " issue(s): " + issues[0];
        }

        // Reads the Component2.Name2 / Body2.Name of every entity in our selection box.
        private List<string> ReadSelectionBoxNames()
        {
            var names = new List<string>();
            ISelectionMgr selMgr = (ISelectionMgr)model.SelectionManager;
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

        // ───────────────────────────── step 4: joints ────────────────────────

        // Step 4 — map joints (parent → child) and assign each a SolidWorks mate.
        // The mate defines the joint's type and limits. The panel is two lists
        // (joints + all mates) plus parent/child controls to add a joint.
        private void BuildJointsStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
        {
            int labelOpts = (int)swAddControlOptions_e.swControlOptions_Visible;

            AddFieldLabel(group, LabelJointInstrID,
                "Joints come from the link tree — one per parent→child. Select a joint, " +
                "then the mate that drives it; the mate sets type and limits.",
                leftEdge, labelOpts);

            // ── Joints list (from the link tree) ─────────────────────────────
            PMListJoints = (PropertyManagerPageListbox)group.AddControl2(
                ListJointsID,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "", (short)leftEdge, visibleEnabled, "Joints (from the link tree) — select one");
            ((IPropertyManagerPageListbox)PMListJoints).Height = 90;

            // ── Mates list ───────────────────────────────────────────────────
            AddFieldLabel(group, LabelMatesCapID,
                "Mates — select one to assign to the joint (it highlights in the view):",
                leftEdge, labelOpts);
            PMListMates = (PropertyManagerPageListbox)group.AddControl2(
                ListMatesID,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "", (short)leftEdge, visibleEnabled, "Assembly mates — click to assign + highlight");
            ((IPropertyManagerPageListbox)PMListMates).Height = 90;

            // ── Selected-joint details ───────────────────────────────────────
            AddFieldLabel(group, LabelDetailCapID, "— Selected joint —", leftEdge, labelOpts);
            PMLabelDetailLinks  = AddFieldLabel(group, LabelDetailLinksID, "", leftEdge, labelOpts);
            PMLabelDetailMate   = AddFieldLabel(group, LabelDetailMateID, "", leftEdge, labelOpts);
            PMLabelDetailType   = AddFieldLabel(group, LabelDetailTypeID, "", leftEdge, labelOpts);
            PMLabelDetailLimits = AddFieldLabel(group, LabelDetailLimitsID, "", leftEdge, labelOpts);
        }

        // Reads all assembly mates (for the mate list). COM may throw on odd
        // assemblies — degrade to an empty list.
        private void ReadAllMates()
        {
            try
            {
                allMates = new List<MateInfo>(
                    new SolidWorksAssemblyWalker((AssemblyDoc)model).WalkAllMates());
            }
            catch (Exception e)
            {
                logger.Warn("Could not read assembly mates.", e);
                allMates = new List<MateInfo>();
            }
        }

        // On entering the step: re-derive joints from the link tree (preserving
        // mate assignments; names follow the tree), then refresh both lists.
        private void EnterJointsStep()
        {
            config.Joints = JointSeeder.Sync(config.Links, config.Joints);

            ReadAllMates();
            PopulateMateList();

            if (activeJointIndex < 0 && config.Joints.Count > 0) activeJointIndex = 0;
            PopulateJointList();
        }

        private void PopulateMateList()
        {
            if (PMListMates == null) return;
            PMListMates.Clear();
            foreach (MateInfo m in allMates)
                PMListMates.AddItems(m.Name + "  [" + m.Kind + "]");
        }

        // Refreshes the joint list — each row shows the joint name + assigned mate.
        private void PopulateJointList()
        {
            if (PMListJoints == null) return;
            PMListJoints.Clear();
            foreach (JointDef j in config.Joints)
            {
                string tag = string.IsNullOrEmpty(j.MateName) ? "(no mate)" : j.MateName;
                PMListJoints.AddItems(j.Name + "   —   " + tag);
            }
            if (config.Joints.Count > 0)
            {
                if (activeJointIndex < 0 || activeJointIndex >= config.Joints.Count) activeJointIndex = 0;
                PMListJoints.CurrentSelection = (short)activeJointIndex;
            }
            UpdateJointDetails();
        }

        private JointDef ActiveJoint() =>
            activeJointIndex >= 0 && activeJointIndex < config.Joints.Count
                ? config.Joints[activeJointIndex] : null;

        // Fills the selected-joint details panel + highlights its assigned mate.
        private void UpdateJointDetails()
        {
            JointDef j = ActiveJoint();
            bool limited = j != null &&
                (j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Prismatic);

            if (PMLabelDetailLinks != null)
                PMLabelDetailLinks.Caption = j == null ? "No joint selected."
                    : "Links:  " + j.ParentLink + "  →  " + j.ChildLink;
            if (PMLabelDetailMate != null)
                PMLabelDetailMate.Caption = j == null ? ""
                    : "Mate:  " + (string.IsNullOrEmpty(j.MateName) ? "none — select a mate above" : j.MateName);
            if (PMLabelDetailType != null)
                PMLabelDetailType.Caption = j == null ? "" : "Type:  " + j.Type;
            if (PMLabelDetailLimits != null)
                PMLabelDetailLimits.Caption = (j == null || !limited) ? ""
                    : "Limits:  lower " + Fmt(j.LimitLower) + ",  upper " + Fmt(j.LimitUpper);

            HighlightActiveMate();
        }

        private static string Fmt(double? v) =>
            v.HasValue ? v.Value.ToString("0.###", CultureInfo.InvariantCulture) : "–";

        // Highlights the active joint's assigned mate in the viewport (if any).
        private void HighlightActiveMate()
        {
            JointDef j = ActiveJoint();
            if (j == null || string.IsNullOrEmpty(j.MateName)) return;
            try { new SolidWorksAssemblyWalker((AssemblyDoc)model).HighlightMate(j.MateName); }
            catch (Exception e) { logger.Warn("Could not highlight mate in the viewport.", e); }
        }

        // Assigns the mate at 'mateIndex' to the selected joint: the mate's kind
        // sets the joint type, and its axis/limits are copied in. The mate is then
        // highlighted in the viewport (via the details refresh) to verify it.
        private void AssignMateToActiveJoint(int mateIndex)
        {
            JointDef j = ActiveJoint();
            if (j == null) { swApp.SendMsgToUser("Select a joint first, then a mate."); return; }
            if (mateIndex < 0 || mateIndex >= allMates.Count) return;

            MateInfo m = allMates[mateIndex];
            j.MateName = m.Name;
            j.Type = JointSeeder.ToJointType(m.Kind);
            j.SetAxis(m.Axis);
            j.LimitLower = m.LimitLower;
            j.LimitUpper = m.LimitUpper;

            PopulateJointList();   // refresh row tag + details (+ highlight)
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

            // Entering Joints: seed (if empty), refresh the lists + combos.
            if (currentStep == 3) EnterJointsStep();
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
            // Step 3 (Links) must pass validation before advancing.
            if (currentStep == 2)
            {
                List<string> issues = LinkDefValidator.Validate(config.Links, allComponentIds);
                if (issues.Count > 0)
                {
                    swApp.SendMsgToUser("Resolve link issues before continuing:\n• " +
                        string.Join("\n• ", issues.ToArray()));
                    return;
                }
            }

            if (currentStep < StepCount - 1)
            {
                SaveCheckpoint(currentStep + 1);
                ShowStep(currentStep + 1);
            }
            else
            {
                // Finish — for now the wizard only SAVES the basic robot data (links +
                // joints) into the assembly checkpoint. ROS 2 / Gz (modular xacro)
                // export is a later increment, driven from this saved data.
                SaveCheckpoint(currentStep);
                logger.Info("SW2GZ export shell Finish pressed — basic data saved to the assembly checkpoint");
                swApp.SendMsgToUser(
                    "Saved. The link tree and joints are stored in this assembly and reload " +
                    "next time you open the SW2GZ panel. (Export is not generated yet.)");
                PMPage.Close(true);
            }
        }

        // Persists the live config to the assembly document tree (the "checkpoint").
        // resumeStep is the step the wizard should reopen on.
        private void SaveCheckpoint(int resumeStep)
        {
            try
            {
                config.LastStep = resumeStep;
                Sw2gzConfigSerialization.Save(swApp, model, config);
            }
            catch (Exception e)
            {
                logger.Error("Failed to save SW2GZ wizard checkpoint", e);
            }
        }

        // Opens a folder picker and writes the choice into config + the textbox.
        private void BrowseForOutputFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(config.OutputFolder))
                {
                    dialog.SelectedPath = config.OutputFolder;
                }
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    config.OutputFolder = dialog.SelectedPath;
                    if (PMTextOutputFolder != null)
                    {
                        PMTextOutputFolder.Text = dialog.SelectedPath;
                    }
                }
            }
        }

        // ───────────────────────────── handler interface ─────────────────────

        void IPropertyManagerPage2Handler9.AfterActivation()
        {
            ShowStep(currentStep);
            if (currentStep == 2 && PMPickFunnel != null)
            {
                PMPickFunnel.SetSelectionFocus();
            }
        }

        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            try
            {
                switch (Id)
                {
                    case ButtonBackID: GoBack(); break;
                    case ButtonNextID: GoNext(); break;
                    case ButtonBrowseID: BrowseForOutputFolder(); break;
                    case ButtonAddLinkID: AddLink(); break;
                    case ButtonRemoveLinkID: RemoveLink(); break;
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
        bool IPropertyManagerPage2Handler9.OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText)
        {
            // Step 3 pick funnel: accept only components / solid bodies.
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
        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text)
        {
            switch (Id)
            {
                case TextOutputFolderID: config.OutputFolder = Text ?? ""; break;
                case TextPackageNameID:  config.PackageName = Text ?? ""; break;
                case TextAuthorID:       config.Author = Text ?? ""; break;
                case TextEmailID:        config.Email = Text ?? ""; break;
                default: break;
            }
        }
        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count)
        {
            if (Id == PickFunnelID && Count > 0) OnFunnelChanged();
        }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id) { }
        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked) { }
        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text)
        {
            if (Id == TextLicenseID)
            {
                config.License = Text ?? "";
            }
        }

        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            if (Id == TextLicenseID)
                config.License = PMComboLicense.get_ItemText((short)Item) ?? "";
            // Parent/child combos are read on "Add joint"; no per-change handling.
        }
        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item)
        {
            if (Id == ListJointsID) { activeJointIndex = Item; UpdateJointDetails(); }
            else if (Id == ListMatesID) AssignMateToActiveJoint(Item);
        }
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
