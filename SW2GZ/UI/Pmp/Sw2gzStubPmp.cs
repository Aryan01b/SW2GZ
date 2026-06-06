/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Single generic stub PMP backing every SW2GZ ribbon panel button for the v2.1.0
UI shell. Shows the panel's title + a "Coming in backend phase" placeholder
label + OK / Cancel.

OK   → calls the supplied onCommit() so each panel can mutate Sw2gzDoc in the
       backend plan. Empty in this UI-only phase.
Cancel → restores the live doc from the snapshot taken at PMP open.

Each panel button in SwAddin instantiates one of these with its own title +
its own Sw2gzDoc-mutation callback (currently no-op stubs). When the backend
plan lands, each becomes its own concrete subclass / replaces this generic shell.

Held as a field by SwAddin (not a local) so the PropertyManagerPage2Handler9
COM callback object stays rooted while the page is open. As a local it would
get GC'd after the launch callback returns, and OK/Cancel would silently stop
firing — the standard SW PMP-handler footgun documented in Sw2gzExportPmp.cs.

Handler-surface note: the plan's stub method list was derived from a different
SDK build. This file matches the actual PropertyManagerPage2Handler9 surface
exposed by SolidWorks.Interop here (see Sw2gzExportPmp.cs for the same set):
explicit interface implementation, bool returns on OnHelp / OnPreview /
OnNextPage / OnPreviousPage / OnTabClicked / OnKeystroke / OnSubmitSelection,
extra members OnLostFocus / OnNumberBoxTrackingCompleted / OnListboxRMBUp /
OnPopupMenuItem / OnPopupMenuItemUpdate / OnWhatsNew, and
OnWindowFromHandleControlCreated (not …Attached). All are no-ops here.
*/
#if SW_INTEROP
using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2GZ.URDFExport;
using SW2GZ.Utilities;

namespace SW2GZ.UI.Pmp
{
    [ComVisible(true)]
    public sealed class Sw2gzStubPmp : PropertyManagerPage2Handler9
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly SldWorks _swApp;
        private readonly Sw2gzDoc _liveDoc;
        private readonly Sw2gzDoc _snapshot;
        private readonly Action<Sw2gzDoc> _onCommit;
        private readonly PropertyManagerPage2 _page;

        public Sw2gzStubPmp(SldWorks swApp, Sw2gzDoc liveDoc, string title, Action<Sw2gzDoc> onCommit)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _liveDoc = liveDoc ?? throw new ArgumentNullException(nameof(liveDoc));
            _onCommit = onCommit ?? (d => { });

            // Snapshot BEFORE the page is created so any auto-mutation during
            // page setup is rolled back on Cancel.
            _snapshot = Sw2gzDocSnapshot.Clone(liveDoc);

            int errs = 0;
            int opts = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                       (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;
            _page = (PropertyManagerPage2)swApp.CreatePropertyManagerPage(
                title, opts, this, ref errs);

            if (_page == null)
            {
                logger.Error("Sw2gzStubPmp: CreatePropertyManagerPage failed for '" + title + "' (err=" + errs + ")");
                return;
            }

            var group = (PropertyManagerPageGroup)_page.AddGroupBox(0, title, 0);
            int labelOpts = (int)swAddControlOptions_e.swControlOptions_Enabled |
                            (int)swAddControlOptions_e.swControlOptions_Visible;
            group.AddControl2(1,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Backend wiring lands in a later increment.",
                (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge,
                labelOpts, "Stub placeholder");
        }

        public void Show()
        {
            if (_page == null) return;
            _page.Show2(0);
        }

        // ─── PropertyManagerPage2Handler9 ────────────────────────────────
        // Cancel rolls back to the snapshot; OK leaves the live doc as-is and
        // hands it to onCommit in AfterClose.
        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            if (Reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Cancel)
            {
                Sw2gzDocSnapshot.Restore(_snapshot, _liveDoc);
                logger.Info("Sw2gzStubPmp: cancel → snapshot restored");
            }
        }

        // AfterClose fires for BOTH OK and Cancel. On Cancel, OnClose has
        // already restored the snapshot, so _onCommit sees the rolled-back doc.
        // For v2.1.0 stubs _onCommit is a no-op, so this is acceptable; the
        // backend plan will need to gate this on close reason.
        void IPropertyManagerPage2Handler9.AfterClose()
        {
            if (_liveDoc != null) _onCommit(_liveDoc);
        }

        // No-op handler stubs (PMP COM contract requires the full surface).
        void IPropertyManagerPage2Handler9.AfterActivation() { }
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
