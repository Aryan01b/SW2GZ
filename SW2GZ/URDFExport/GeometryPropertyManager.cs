/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Native SolidWorks PropertyManagerPage (left panel) for LINK GEOMETRY
assignment. Unlike the modal WPF wizard, a PMP keeps the 3D viewport live, so
the user can pick bodies/components directly in the model and have them land in
a selection box — exactly like the Mate / Insert-Component commands.

Modelled on URDFExport\ExportPropertyManager.cs (the established PMP pattern in
this codebase): swApp.CreatePropertyManagerPage(...), PMPage.AddGroupBox,
PMGroup.AddControl2(...) for labels / comboboxes / textboxes / selection boxes,
a Show() method, and the full PropertyManagerPage2Handler9 handler interface.

The page writes into a shared, COM-free SW2GZ.Build.Model.GeometryAssignment
(seeded with the top-level link names); on OK it raises an onClosed callback so
the caller (SwAddin.LaunchWizard) can continue into the modeless wizard with the
geometry already assigned.

Guarded entirely by #if SW_INTEROP. When the symbol is undefined the class
collapses to a tiny throwing skeleton so the file still compiles outside a
SolidWorks workstation (it is not source-linked into the net8 test project, but
the guard is kept consistent with the rest of the SwSurface code).
*/

using SW2GZ.Build;
using SW2GZ.Build.Model;
using System;

#if SW_INTEROP
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2GZ.Utilities;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
#endif

namespace SW2GZ.URDFExport
{
#if SW_INTEROP
    [ComVisible(true)]
    [Serializable]
    public sealed class GeometryPropertyManager : PropertyManagerPage2Handler9
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly SldWorks swApp;
        private readonly ModelDoc2 assemblyDoc;
        private readonly GeometryAssignment assignment;
        private readonly Action onClosed;

        // PMP infrastructure.
        private readonly PropertyManagerPage2 PMPage;
        private PropertyManagerPageGroup PMGroupLinks;
        private PropertyManagerPageGroup PMGroupGeometry;

        private PropertyManagerPageCombobox PMComboLinks;
        private PropertyManagerPageLabel PMLabelProgress;
        private PropertyManagerPageLabel PMLabelStatus;
        private PropertyManagerPageSelectionbox PMSelection;
        private PropertyManagerPageLabel PMLabelSelCount;
        private PropertyManagerPageTextbox PMTextBoxLinkName;
        private PropertyManagerPageButton PMButtonAssign;
        private PropertyManagerPageButton PMButtonClear;
        private PropertyManagerPageButton PMButtonPrev;
        private PropertyManagerPageButton PMButtonNext;

        // Each control needs a unique ID.
        private const int GroupLinksID = 1;
        private const int ComboLinksID = 2;
        private const int LabelProgressID = 3;
        private const int LabelStatusID = 4;
        private const int GroupGeometryID = 5;
        private const int SelectionID = 6;
        private const int LabelSelCountID = 7;
        private const int TextBoxLinkNameID = 8;
        private const int ButtonAssignID = 9;
        private const int ButtonClearID = 10;
        private const int ButtonPrevID = 11;
        private const int ButtonNextID = 12;

        // Mark used to tag selections that belong to our selection box.
        private const int SelectionMark = 1;

        // Index of the "current link" being edited.
        private int currentLinkIndex;

        // True once OnClose has fired so we don't raise onClosed twice.
        private bool closed;

        public GeometryPropertyManager(
            SldWorks swApp,
            ModelDoc2 assemblyDoc,
            GeometryAssignment assignment,
            Action onClosed = null)
        {
            this.swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            this.assemblyDoc = assemblyDoc ?? throw new ArgumentNullException(nameof(assemblyDoc));
            this.assignment = assignment ?? throw new ArgumentNullException(nameof(assignment));
            this.onClosed = onClosed;
            this.currentLinkIndex = 0;

            int longerrors = 0;
            const string pageTitle = "SW2GZ — Define Link Geometry";
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
                logger.Error("Failed to create the SW2GZ geometry PropertyManager page. Error: " +
                    longerrors);
                MessageBox.Show("There was a problem setting up the geometry panel.\n" +
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

            // ── Group: Links ──────────────────────────────────────────────────
            int grpOptions =
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            PMGroupLinks = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                GroupLinksID, "Links", grpOptions);

            PMComboLinks = (PropertyManagerPageCombobox)PMGroupLinks.AddControl2(
                ComboLinksID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "Link", (short)indent, visibleEnabled,
                "Select the link to assign geometry to");
            PMComboLinks.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            foreach (LinkGeometry link in assignment.Links)
            {
                PMComboLinks.AddItems(link.LinkName);
            }

            PMLabelProgress = (PropertyManagerPageLabel)PMGroupLinks.AddControl2(
                LabelProgressID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");

            PMLabelStatus = (PropertyManagerPageLabel)PMGroupLinks.AddControl2(
                LabelStatusID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");

            // ── Group: Geometry ───────────────────────────────────────────────
            PMGroupGeometry = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                GroupGeometryID, "Geometry", grpOptions);

            PMSelection = (PropertyManagerPageSelectionbox)PMGroupGeometry.AddControl2(
                SelectionID,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Bodies / components", (short)indent, visibleEnabled,
                "Pick solid bodies or components in the 3D viewport for this link");

            // Accept solid bodies AND components (a link may be defined either way).
            var filters = new swSelectType_e[]
            {
                swSelectType_e.swSelSOLIDBODIES,
                swSelectType_e.swSelCOMPONENTS,
            };
            object filterObj = filters;
            PMSelection.SingleEntityOnly = false;
            PMSelection.AllowMultipleSelectOfSameEntity = false;
            PMSelection.AllowSelectInMultipleBoxes = false;
            PMSelection.Height = 60;
            PMSelection.Mark = SelectionMark;
            PMSelection.SetSelectionFilters(filterObj);

            PMLabelSelCount = (PropertyManagerPageLabel)PMGroupGeometry.AddControl2(
                LabelSelCountID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "0 bodies selected", (short)leftEdge, visibleEnabled, "");

            PMTextBoxLinkName = (PropertyManagerPageTextbox)PMGroupGeometry.AddControl2(
                TextBoxLinkNameID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled,
                "Link name (seeded from the component; edited names are sanitized)");

            PMButtonAssign = (PropertyManagerPageButton)PMGroupGeometry.AddControl2(
                ButtonAssignID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Assign to current link", 0, visibleEnabled,
                "Capture the current selection into this link");
            ((IPropertyManagerPageControl)PMButtonAssign).Width = 200;

            PMButtonClear = (PropertyManagerPageButton)PMGroupGeometry.AddControl2(
                ButtonClearID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Clear", 0, visibleEnabled,
                "Clear the geometry assigned to this link");
            ((IPropertyManagerPageControl)PMButtonClear).Width = 200;

            PMButtonPrev = (PropertyManagerPageButton)PMGroupGeometry.AddControl2(
                ButtonPrevID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "< Previous link", 0, visibleEnabled, "Go to the previous link");
            ((IPropertyManagerPageControl)PMButtonPrev).Width = 95;

            PMButtonNext = (PropertyManagerPageButton)PMGroupGeometry.AddControl2(
                ButtonNextID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Next link >", 0, visibleEnabled, "Go to the next link");
            ((IPropertyManagerPageControl)PMButtonNext).Width = 95;

            // Seed the UI on the first link.
            LoadCurrentLink();
        }

        // ───────────────────────────── link navigation ───────────────────────

        private LinkGeometry Current =>
            (assignment.Links.Count > 0 &&
             currentLinkIndex >= 0 &&
             currentLinkIndex < assignment.Links.Count)
                ? assignment.Links[currentLinkIndex]
                : null;

        // Repopulates the combo selection, name box, selection box and status
        // labels for the current link. Called on activation and on every switch.
        private void LoadCurrentLink()
        {
            LinkGeometry link = Current;
            int count = assignment.Links.Count;

            if (count > 0)
            {
                PMComboLinks.CurrentSelection = (short)currentLinkIndex;
                PMLabelProgress.Caption =
                    "Link " + (currentLinkIndex + 1) + " of " + count;
            }
            else
            {
                PMLabelProgress.Caption = "No links found";
            }

            if (link != null)
            {
                PMTextBoxLinkName.Text = link.LinkName;
                UpdateStatus(link);
                UpdateSelCount(link.SelectedBodyNames.Count);
            }
            else
            {
                PMTextBoxLinkName.Text = "";
                PMLabelStatus.Caption = "";
                UpdateSelCount(0);
            }

            // Clear the viewport / box selection so each link starts from its own
            // (re)pick. The persistent body names live in the GeometryAssignment.
            assemblyDoc.ClearSelection2(true);
            PMSelection.SetSelectionFocus();
        }

        private void UpdateStatus(LinkGeometry link)
        {
            PMLabelStatus.Caption = link.HasGeometry
                ? "Assigned: " + link.SelectedBodyNames.Count + " body(ies)"
                : "Unassigned";
        }

        private void UpdateSelCount(int count)
        {
            PMLabelSelCount.Caption = count + " bodies selected";
        }

        private void GoToLink(int index)
        {
            if (assignment.Links.Count == 0) return;
            if (index < 0) index = 0;
            if (index > assignment.Links.Count - 1) index = assignment.Links.Count - 1;
            currentLinkIndex = index;
            LoadCurrentLink();
        }

        // ───────────────────────────── selection capture ─────────────────────

        // Reads the persistent identifiers (component Name2 / body Name) of every
        // entity currently in our selection box, via the model's SelectionMgr.
        private List<string> ReadSelectionBoxNames()
        {
            var names = new List<string>();
            ISelectionMgr selMgr = (ISelectionMgr)assemblyDoc.SelectionManager;
            if (selMgr == null) return names;

            int count = selMgr.GetSelectedObjectCount2(SelectionMark);
            for (int i = 1; i <= count; i++)
            {
                object selObj = selMgr.GetSelectedObject6(i, SelectionMark);
                int selType = selMgr.GetSelectedObjectType3(i, SelectionMark);
                string name = DescribeSelection(selObj, selType);
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
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

        private void AssignCurrent()
        {
            LinkGeometry link = Current;
            if (link == null) return;

            List<string> names = ReadSelectionBoxNames();
            if (names.Count == 0)
            {
                MessageBox.Show("Nothing is selected. Pick one or more solid bodies " +
                    "or components in the 3D viewport, then press Assign.");
                return;
            }

            link.Assign(names);

            // Commit the (possibly user-edited) link name through the same
            // sanitizer the walker uses, so it stays a valid ROS identifier.
            string rawName = PMTextBoxLinkName.Text;
            if (!string.IsNullOrWhiteSpace(rawName))
            {
                link.LinkName = RosNameSanitizer.Sanitize(rawName).Value;
                // Reflect the sanitized name back into the combo + textbox.
                PMTextBoxLinkName.Text = link.LinkName;
            }

            UpdateStatus(link);
            UpdateSelCount(link.SelectedBodyNames.Count);
        }

        private void ClearCurrent()
        {
            LinkGeometry link = Current;
            if (link == null) return;
            link.Clear();
            assemblyDoc.ClearSelection2(true);
            UpdateStatus(link);
            UpdateSelCount(0);
            PMSelection.SetSelectionFocus();
        }

        // ───────────────────────────── handler interface ─────────────────────

        void IPropertyManagerPage2Handler9.AfterActivation()
        {
            // Make our selection box "blue" so viewport picks land in it.
            PMSelection.SetSelectionFocus();
            LoadCurrentLink();
        }

        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            if (Id == ComboLinksID)
            {
                GoToLink(Item);
            }
        }

        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            try
            {
                switch (Id)
                {
                    case ButtonAssignID: AssignCurrent(); break;
                    case ButtonClearID: ClearCurrent(); break;
                    case ButtonPrevID: GoToLink(currentLinkIndex - 1); break;
                    case ButtonNextID: GoToLink(currentLinkIndex + 1); break;
                    default: break;
                }
            }
            catch (Exception e)
            {
                logger.Error("Exception handling geometry panel button " + Id, e);
                MessageBox.Show("There was a problem with the geometry panel:\n\"" +
                    e.Message + "\"");
            }
        }

        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text)
        {
            // Live edit kept in the textbox; sanitized + committed on Assign.
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count)
        {
            if (Id == SelectionID)
            {
                UpdateSelCount(Count);
            }
        }

        bool IPropertyManagerPage2Handler9.OnSubmitSelection(
            int Id, object Selection, int SelType, ref string ItemText)
        {
            // Accept only solid bodies and components; reject anything else so a
            // stray edge/face pick can't pollute the link's geometry.
            if (Id != SelectionID) return true;
            switch ((swSelectType_e)SelType)
            {
                case swSelectType_e.swSelSOLIDBODIES:
                case swSelectType_e.swSelCOMPONENTS:
                    return true;
                default:
                    ItemText = "Only solid bodies and components can be assigned to a link.";
                    return false;
            }
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            try
            {
                if (Reason ==
                    (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
                {
                    // The GeometryAssignment is the shared object — already populated.
                    logger.Info("Geometry panel closed with OK");
                }
                else
                {
                    logger.Info("Geometry panel cancelled");
                }
            }
            catch (Exception e)
            {
                logger.Error("Exception on geometry panel close", e);
            }
        }

        void IPropertyManagerPage2Handler9.AfterClose()
        {
            // Fire the continuation exactly once, after the page is fully closed.
            if (closed) return;
            closed = true;
            try
            {
                onClosed?.Invoke();
            }
            catch (Exception e)
            {
                logger.Error("Exception in geometry panel onClosed callback", e);
                MessageBox.Show("There was a problem continuing after the geometry panel:\n\"" +
                    e.Message + "\"");
            }
        }

        // ───────────────── remaining handler members (no-ops) ─────────────────

        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id) { }
        void IPropertyManagerPage2Handler9.OnLostFocus(int Id) { }
        bool IPropertyManagerPage2Handler9.OnHelp() => true;
        bool IPropertyManagerPage2Handler9.OnNextPage() => true;
        bool IPropertyManagerPage2Handler9.OnPreviousPage() => true;
        bool IPropertyManagerPage2Handler9.OnPreview() => true;
        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id) => true;
        bool IPropertyManagerPage2Handler9.OnKeystroke(int Wparam, int Message, int Lparam, int Id) => false;
        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id) { }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id) { }
        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value) { }
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked) { }
        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text) { }
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
#else
    // Skeleton so the file compiles when SW_INTEROP is undefined (e.g. outside a
    // SolidWorks workstation). The real implementation above is COM-bound.
    public sealed class GeometryPropertyManager
    {
        public GeometryPropertyManager(
            object swApp, object assemblyDoc, GeometryAssignment assignment, Action onClosed = null)
        {
            throw new NotSupportedException(
                "GeometryPropertyManager requires SW_INTEROP (a SolidWorks add-in build).");
        }

        public void Show() =>
            throw new NotSupportedException(
                "GeometryPropertyManager requires SW_INTEROP (a SolidWorks add-in build).");
    }
#endif
}
