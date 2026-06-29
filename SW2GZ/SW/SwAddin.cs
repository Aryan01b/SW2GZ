/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SolidWorksTools;
using SW2GZ.Build;
using SW2GZ.UI;
using SW2GZ.URDFExport;
using SW2GZ.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SW2GZ.SW
{
    // Adding a new line
    //
    /// <summary>
    /// Summary description for SW2GZ.
    /// </summary>
    [Guid("34fad620-2a46-4ba6-9f5f-1dfefde894c7"), ComVisible(true), ProgId("SwAddin.SW2GZ.Addin")]
    [SwAddin(
        Description = "SolidWorks to ROS 2 + Gz Sim Exporter",
        Title = "SW2GZ",
        LoadAtStartup = true
        )]
    public class SwAddin : ISwAddin
    {
        #region Static Variables

        private static readonly log4net.ILog logger = Logger.GetLogger();

        #endregion Static Variables

        #region Local Variables

        private int add_in_id_ = 0;

        public const int mainCmdGroupID = 5;
        public const int mainItemID1 = 0;
        public const int mainItemID2 = 1;
        public const int mainItemID3 = 2;

        // NOTE: the per-stack "Stacks" ribbon buttons (Actuation/Sensors/Gazebo/
        // Bridge, user IDs 922–925) are temporarily removed while the Create Model
        // flow is sanity-checked. Their enable callback re-loaded the full config
        // (a feature-tree COM walk + XML parse) on every ribbon poll and saturated
        // the UI thread. Re-add with a cached/throttled enable gate. The
        // StackConfigDialog / StackRibbonGate / StackProfile classes are kept intact.

        #region Event Handler Variables

        private SldWorks SwEventPtr = null;

        #endregion Event Handler Variables

        // Public Properties
        public ISldWorks SwApp { get; private set; } = null;

        public ICommandManager CmdMgr { get; private set; } = null;

        public Dictionary<ModelDoc2, DocumentEventHandler> OpenDocs { get; private set; } = new Dictionary<ModelDoc2, DocumentEventHandler>();

        #endregion Local Variables

        #region SolidWorks Registration

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            #region Get Custom Attribute: SwAddinAttribute

            SwAddinAttribute SWattr = null;
            Type type = typeof(SwAddin);

            foreach (System.Attribute attr in type.GetCustomAttributes(false))
            {
                if (attr is SwAddinAttribute)
                {
                    SWattr = attr as SwAddinAttribute;
                    break;
                }
            }

            #endregion Get Custom Attribute: SwAddinAttribute

            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

                string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                logger.Info("Registering " + keyname);
                Microsoft.Win32.RegistryKey addinkey = hklm.CreateSubKey(keyname);
                addinkey.SetValue(null, 0);

                addinkey.SetValue("Description", SWattr.Description);
                addinkey.SetValue("Title", SWattr.Title);

                keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                logger.Info("Registering " + keyname);
                addinkey = hkcu.CreateSubKey(keyname);
                addinkey.SetValue(
                    null, Convert.ToInt32(SWattr.LoadAtStartup), Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (NullReferenceException nl)
            {
                logger.Error("There was a problem registering this dll: SWattr is null. \n\"" +
                    nl.Message + "\"", nl);
                // MessageBox.Show("There was a problem registering this dll: SWattr is null. \n\"" +
                //     nl.Message + "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
            catch (Exception e)
            {
                logger.Error(e.Message);
                // MessageBox.Show("There was a problem registering the function: \n\"" + e.Message +
                //    "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            // Idempotent — runs from regasm /u during uninstall (and possibly during reinstall
            // when the previous install was partial). Missing subkeys are NOT an error condition,
            // and we never want to pop a dialog at the user during silent uninstall. Any genuine
            // error (e.g. ACL refusal) is logged but swallowed.
            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

                string hklmKey = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                logger.Info("Unregistering " + hklmKey);
                // throwOnMissingSubKey: false — silent no-op if already gone.
                hklm.DeleteSubKey(hklmKey, throwOnMissingSubKey: false);

                string hkcuKey = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                logger.Info("Unregistering " + hkcuKey);
                hkcu.DeleteSubKey(hkcuKey, throwOnMissingSubKey: false);
            }
            catch (Exception e)
            {
                // Log only — uninstall is best-effort. Showing a MessageBox during
                // /SILENT uninstall breaks unattended workflows and confuses end users
                // when the only "error" is an already-gone key.
                logger.Error("Non-fatal unregister exception (swallowed): " + e.Message);
            }
        }

        #endregion SolidWorks Registration

        #region ISwAddin Implementation

        public SwAddin()
        {
            Logger.Setup();
        }

        private void ExceptionHandler(object sender, ThreadExceptionEventArgs e)
        {
            logger.Warn("Exception encountered in Assembly export form", e.Exception);
        }

        private void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            logger.Error("Unhandled exception in Assembly Export form\nEmail your maintainer " +
                "with the log file found at " +
                Logger.GetFileName(), (Exception)e.ExceptionObject);
        }

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            logger.Info("Attempting to connect to SW");
            SwApp = (ISldWorks)ThisSW;
            add_in_id_ = cookie;

            //Setup callbacks
            logger.Info("Setting up callbacks");
            SwApp.SetAddinCallbackInfo(0, this, add_in_id_);

            #region Setup the Command Manager
            logger.Info("Setting up command manager");
            CmdMgr = SwApp.GetCommandManager(cookie);

            logger.Info("Adding command manager");
            AddCommandMgr();

            #endregion Setup the Command Manager

            #region Setup the Event Handlers
            logger.Info("Adding event handlers");
            SwEventPtr = (SldWorks)SwApp;
            OpenDocs = new Dictionary<ModelDoc2, DocumentEventHandler>();
            AttachEventHandlers();

            #endregion Setup the Event Handlers

            logger.Info("Connecting plugin to SolidWorks");
            return true;
        }

        public bool DisconnectFromSW()
        {
            RemoveCommandMgr();
            DetachEventHandlers();

            Marshal.ReleaseComObject(CmdMgr);
            CmdMgr = null;
            Marshal.ReleaseComObject(SwApp);
            SwApp = null;
            //The addin _must_ call GC.Collect() here in order to retrieve all managed code pointers
            GC.Collect();
            GC.WaitForPendingFinalizers();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            logger.Info("Disconnecting plugin from SolidWorks");
            return true;
        }

        #endregion ISwAddin Implementation

        #region UI Methods

        // Icon list reused by both the toolbar and the command-group glyph.
        // SolidWorks wants an array of square PNGs of increasing size and picks
        // the best fit. The PNGs ship next to the DLL (csproj copies them into an
        // images\ subfolder of the output) so they resolve regardless of where
        // the add-in is installed — computed from the assembly's own location.
        private static string ImagesDir()
        {
            return System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location),
                "images");
        }

        // Single-glyph set (the isometric cube) — used for the command-group
        // glyph (MainIconList).
        private static string[] Sw2gzIconList()
        {
            string dir = ImagesDir();
            return new[]
            {
                System.IO.Path.Combine(dir, "sw2gz_20.png"),
                System.IO.Path.Combine(dir, "sw2gz_32.png"),
                System.IO.Path.Combine(dir, "sw2gz_40.png"),
                System.IO.Path.Combine(dir, "sw2gz_64.png"),
                System.IO.Path.Combine(dir, "sw2gz_96.png"),
                System.IO.Path.Combine(dir, "sw2gz_128.png"),
            };
        }

        // Sprite-strip set — each PNG lays both button glyphs out horizontally
        // (column 0 = Create Model cube, column 1 = Export). This is what
        // ICommandGroup.IconList wants; AddCommandItem2's image index selects
        // the column.
        private static string[] Sw2gzStripIconList()
        {
            string dir = ImagesDir();
            return new[]
            {
                System.IO.Path.Combine(dir, "sw2gz_strip_20.png"),
                System.IO.Path.Combine(dir, "sw2gz_strip_32.png"),
                System.IO.Path.Combine(dir, "sw2gz_strip_40.png"),
                System.IO.Path.Combine(dir, "sw2gz_strip_64.png"),
                System.IO.Path.Combine(dir, "sw2gz_strip_96.png"),
                System.IO.Path.Combine(dir, "sw2gz_strip_128.png"),
            };
        }

        public void AddCommandMgr()
        {
            try
            {
                _ribbonRegistrar = new SW2GZ.UI.Ribbon.Sw2gzRibbonRegistrar(
                    CmdMgr, Sw2gzStripIconList(), Sw2gzIconList());
                _ribbonRegistrar.Register();
            }
            catch (Exception e)
            {
                logger.Error("AddCommandMgr: ribbon registration failed", e);
            }
        }

        public int ToolbarEnableMethod()
        {
            return 1;
        }
        public void RemoveCommandMgr()
        {
            // Symmetric with AddCommandMgr: tear down the command group built by
            // Sw2gzRibbonRegistrar.Register() so SolidWorks doesn't keep a stale
            // group ID in its registry across add-in unload/reload.
            CmdMgr.RemoveCommandGroup(SW2GZ.UI.Ribbon.RibbonCommandIds.CmdGroupId);
            logger.Info("Removed SW2GZ command group");
        }

        #endregion UI Methods

        #region UI Callbacks

        // Held as a field so the PropertyManagerPage2Handler9 COM callback stays
        // rooted while the PMP is open — as a local it would get GC'd after the
        // launch callback returns and OK/Cancel would silently stop firing.
        private SW2GZ.UI.Pmp.Sw2gzStubPmp _openPanel;

        // Held so SetMode can call RefreshTabForMode for the L3b "only active
        // mode visible" tab rebuild. Set in AddCommandMgr.
        private SW2GZ.UI.Ribbon.Sw2gzRibbonRegistrar _ribbonRegistrar;

        // Export command: requires a saved "SW2GZ Doc (v1)" attribute (the
        // mode pills + Export button are both gated by HasSaved so reaching
        // here without one is a keyboard-shortcut / poll-race path only).
        // Opens the modal multi-page Sw2gzExportWizardForm which collects
        // meta, shows scope, and runs the pipeline in-page.
        public void LaunchExport()
        {
            try
            {
                if (!TryGetActiveModelDoc(out ModelDoc2 modeldoc)) return;

                if (!SW2GZ.URDFExport.Sw2gzDocSerialization.HasSaved(modeldoc))
                {
                    SwApp.SendMsgToUser2(
                        "No SW2GZ Doc (v1) saved yet — run Create first.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);
                    return;
                }

                var doc = SW2GZ.URDFExport.Sw2gzDocSerialization.Load(modeldoc)
                          ?? SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);

                using (var wizard = new SW2GZ.UI.Forms.Sw2gzExportWizardForm(
                    (SldWorks)SwApp, modeldoc, doc))
                {
                    wizard.ShowDialog();
                }
            }
            catch (Exception e)
            {
                logger.Error("SW2GZ export failed", e);
                MessageBox.Show("Export failed:\n" + e.Message +
                    "\nLog: " + Logger.GetFileName());
            }
        }

        // Enable-state callback for the ribbon/menu command: enabled only when the
        // active document is an assembly. Null-safe against SW polling during
        // addin connect/disconnect, when SwApp may briefly be null.
        public int WizardEnable()
        {
            try
            {
                if (SwApp == null) return 0;
                ModelDoc2 modeldoc = SwApp.ActiveDoc;
                return (modeldoc != null &&
                        modeldoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY) ? 1 : 0;
            }
            catch (Exception e)
            {
                logger.Warn("WizardEnable poll threw — reporting disabled", e);
                return 0;
            }
        }

        // Shared precondition check for the ribbon callbacks. Returns false (with
        // an informational popup) when the active doc is missing or not an assembly,
        // so the ribbon button click is a no-op for non-assembly contexts. The
        // ribbon-enable callback (WizardEnable) already greys the buttons in the
        // normal case; this is the fallback for keyboard-shortcut / menu / poll-race
        // paths where the click goes through anyway.
        private bool TryGetActiveAssembly(out ModelDoc2 modeldoc)
        {
            modeldoc = null;
            if (SwApp == null) return false;
            modeldoc = SwApp.ActiveDoc;
            if (modeldoc != null &&
                modeldoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return true;
            }
            SwApp.SendMsgToUser2(
                "Open an assembly document first — SW2GZ exports assemblies only.",
                (int)swMessageBoxIcon_e.swMbInformation,
                (int)swMessageBoxBtn_e.swMbOk);
            return false;
        }

        // ─── Mode flyout (L3b) ────────────────────────────────────────
        // SW-native split button (face-only — no chevron). The flyout face's
        // click callback is OpenCreatePmp; the 3 mode pills registered next
        // to the Create button call ModeRobotClick / ModeWorldClick /
        // ModeAssetClick, which set the doc mode and trigger the L3b tab
        // rebuild via SetMode.

        // Face-click dispatcher — opens the create wizard for the active mode.
        // Each mode has its own multi-step PMP (Sw2gzCreateRobotPmp /
        // Sw2gzCreateWorldPmp / Sw2gzCreateAssetPmp). Held as fields — same
        // COM-handler-rooting reason as _openPanel (the SW PMP handler
        // interface is freed on AfterClose).
        private SW2GZ.UI.Pmp.Sw2gzCreateRobotPmp _createRobotPmp;
        private SW2GZ.UI.Pmp.Sw2gzCreateWorldPmp _createWorldPmp;
        private SW2GZ.UI.Pmp.Sw2gzCreateAssetPmp _createAssetPmp;
        private SW2GZ.UI.Pmp.Sw2gzWorldSettingsPmp _worldSettingsPmp;
        private SW2GZ.UI.Pmp.Sw2gzWorldSensorsPmp _worldSensorsPmp;

        // Bisecting confirmed: calling swApp.CreatePropertyManagerPage directly
        // from inside an IFlyoutGroup face callback throws InvalidCastException
        // at the COM marshaller — even when the PMP class is Sw2gzStubPmp, which
        // works fine from regular ribbon commands. SW's flyout face is invoked
        // in a COM apartment / marshalling state that refuses to take a managed
        // handler object. The fix: defer the actual PMP creation onto the next
        // WinForms message-loop tick via a one-shot Timer. The flyout callback
        // returns immediately; the Timer fires on the next idle, OUTSIDE the
        // flyout context, and the PMP opens normally.
        public void OpenCreatePmp()
        {
            try
            {
                if (!TryGetActiveModelDoc(out ModelDoc2 modeldoc)) return;

                // A standalone part document can only be an Asset — force the
                // mode and open the whole-part Create Asset wizard directly.
                bool isPart = modeldoc.GetType() == (int)swDocumentTypes_e.swDocPART;
                if (isPart)
                {
                    SW2GZ.URDFExport.Sw2gzDoc pdoc =
                        SW2GZ.URDFExport.Sw2gzDocSerialization.Load(modeldoc)
                        ?? SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                    pdoc.Mode = SW2GZ.URDFExport.Sw2gzMode.Asset;
                    SW2GZ.URDFExport.Sw2gzDocStore.Put(modeldoc, pdoc);
                    string partName = DerivePackageNameFromAssembly(modeldoc);
                    DeferToIdle(() => OpenCreateAsset(modeldoc, pdoc, partName));
                    return;
                }

                SW2GZ.URDFExport.Sw2gzDoc doc;
                if (SW2GZ.URDFExport.Sw2gzDocSerialization.HasSaved(modeldoc))
                {
                    // Edit an existing doc: the persisted attribute is the source
                    // of truth (the store is blank on a fresh SW launch, so it
                    // would otherwise reopen the default Robot wizard for a saved
                    // World/Asset). Seed the store with the loaded instance so the
                    // wizard's edits + cancel-snapshot operate on the live doc.
                    doc = SW2GZ.URDFExport.Sw2gzDocSerialization.Load(modeldoc)
                          ?? SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                    SW2GZ.URDFExport.Sw2gzDocStore.Put(modeldoc, doc);
                }
                else
                {
                    // Fresh assembly: drop any stale in-memory doc so the wizard
                    // re-seeds from live assembly state, but preserve the mode the
                    // user picked via the pills (SetMode only mutates the in-memory
                    // doc — the pills never persist — so a reset would discard a
                    // World/Asset pick and always reopen the Robot wizard).
                    var chosenMode = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc).Mode;
                    SW2GZ.URDFExport.Sw2gzDocStore.Reset(modeldoc);
                    doc = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                    doc.Mode = chosenMode;
                }
                switch (doc.Mode)
                {
                    case SW2GZ.URDFExport.Sw2gzMode.World:
                        DeferToIdle(() => OpenCreateWorld(modeldoc, doc));
                        break;
                    case SW2GZ.URDFExport.Sw2gzMode.Asset:
                        DeferToIdle(() => OpenCreateAsset(modeldoc, doc));
                        break;
                    default:
                        DeferToIdle(() => OpenCreateRobot(modeldoc, doc));
                        break;
                }
            }
            catch (Exception e)
            {
                logger.Error("OpenCreatePmp failed", e);
                MessageBox.Show("Could not open Create: " + e.Message);
            }
        }

        private void OpenCreateRobot(ModelDoc2 modelDoc, SW2GZ.URDFExport.Sw2gzDoc doc)
        {
            try
            {
                _createRobotPmp = new SW2GZ.UI.Pmp.Sw2gzCreateRobotPmp(
                    (SldWorks)SwApp, modelDoc, doc,
                    d => PersistDoc(modelDoc, d));
                _createRobotPmp.Show();
            }
            catch (Exception e)
            {
                logger.Error("OpenCreateRobot failed", e);
                MessageBox.Show("Could not open Create Robot: " + e.Message);
            }
        }

        private void OpenCreateWorld(ModelDoc2 modelDoc, SW2GZ.URDFExport.Sw2gzDoc doc)
        {
            try
            {
                _createWorldPmp = new SW2GZ.UI.Pmp.Sw2gzCreateWorldPmp(
                    (SldWorks)SwApp, modelDoc, doc,
                    d => PersistDoc(modelDoc, d));
                _createWorldPmp.Show();
            }
            catch (Exception e)
            {
                logger.Error("OpenCreateWorld failed", e);
                MessageBox.Show("Could not open Create World: " + e.Message);
            }
        }

        private void OpenCreateAsset(ModelDoc2 modelDoc, SW2GZ.URDFExport.Sw2gzDoc doc, string wholePartName = null)
        {
            try
            {
                _createAssetPmp = new SW2GZ.UI.Pmp.Sw2gzCreateAssetPmp(
                    (SldWorks)SwApp, modelDoc, doc,
                    d => PersistDoc(modelDoc, d), wholePartName);
                _createAssetPmp.Show();
            }
            catch (Exception e)
            {
                logger.Error("OpenCreateAsset failed", e);
                MessageBox.Show("Could not open Create Asset: " + e.Message);
            }
        }

        // Persists Sw2gzDoc to "SW2GZ Doc (v1)" Attribute on Finish (not Cancel
        // — each wizard guards _onCommit via an _okay flag set in OnClose).
        // While the attribute exists the mode pills are locked.
        private void PersistDoc(ModelDoc2 modelDoc, SW2GZ.URDFExport.Sw2gzDoc doc)
        {
            try
            {
                SW2GZ.URDFExport.Sw2gzDocSerialization.Save((SldWorks)SwApp, modelDoc, doc);
                // Doc now saved → swap the mode-start button to "Edit <Mode>" and
                // lock the pills (PillUpdate polls HasSaved and disables them).
                try { _ribbonRegistrar?.RefreshTabForMode(doc.Mode, true); modelDoc.Extension.ActiveCommandTab = "SW2GZ"; }
                catch (Exception re) { logger.Warn("PersistDoc: ribbon refresh failed", re); }
            }
            catch (Exception e)
            {
                logger.Error("PersistDoc failed", e);
                MessageBox.Show("Save failed: " + e.Message);
            }
        }

        // Run `action` on the next WinForms idle tick. Used by callbacks that
        // SW invokes from a COM marshalling context that breaks subsequent COM
        // calls (notably flyout face callbacks → CreatePropertyManagerPage).
        private void DeferToIdle(Action action)
        {
            if (action == null) return;
            // 500ms: empirically, a 1ms one-shot was not enough for the SW
            // flyout-face callback path — InvalidCastException still fired from
            // InterfaceMarshaler.ConvertToNative on CreatePropertyManagerPage's
            // handler param. SW appears to hold COM-apartment state past the
            // first message-loop tick after the flyout callback returns. A
            // half-second delay gives SW time to fully dismiss the flyout.
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                timer.Dispose();
                try { action(); }
                catch (Exception e) { logger.Error("DeferToIdle action failed", e); }
            };
            timer.Start();
        }
        public void ModeRobotClick() => SetMode(SW2GZ.URDFExport.Sw2gzMode.Robot);
        public void ModeWorldClick() => SetMode(SW2GZ.URDFExport.Sw2gzMode.World);
        public void ModeAssetClick() => SetMode(SW2GZ.URDFExport.Sw2gzMode.Asset);

        private void SetMode(SW2GZ.URDFExport.Sw2gzMode mode)
        {
            if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return;
            var doc = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
            if (SW2GZ.URDFExport.Sw2gzDocLock.IsLocked(doc))
            {
                logger.Info("SetMode: ignored (doc locked in mode=" + doc.Mode + ")");
                return;
            }
            if (doc.Mode == mode)
            {
                logger.Info("SetMode: already in " + mode + ", no-op");
                return;
            }
            var from = doc.Mode;
            doc.Mode = mode;
            logger.Info("SetMode: switched to " + mode);

            // L3b: rebuild the tab so only the active mode's panel cluster
            // shows. The Sw2gzModeChangeOverlay covers the rebuild flash with
            // a brief modal toast — clearer to the user than the bare ribbon
            // refresh, and stops the visual confusion of boxes vanishing +
            // reappearing while the active tab focus is preserved.
            try
            {
                var overlay = new SW2GZ.UI.Ribbon.Sw2gzModeChangeOverlay(
                    from, mode,
                    // saved:false — mode pills are only enabled on an unsaved doc,
                    // so a mode switch always lands on the "Create <Mode>" label.
                    () => _ribbonRegistrar?.RefreshTabForMode(mode, false));
                overlay.ShowDialog();

                // The box swap inside RefreshTabForMode makes SW drop the active
                // CommandManager tab back to its default (Assembly/Features). The
                // user clicked a SW2GZ mode pill, so keep them on the SW2GZ tab —
                // reactivate it by title (IModelDocExtension.ActiveCommandTab is a
                // string setter keyed on the tab name).
                modeldoc.Extension.ActiveCommandTab = "SW2GZ";
            }
            catch (Exception e) { logger.Warn("SetMode: tab refresh failed", e); }
        }

        // ─── Mode pills ───────────────────────────────────────────────
        // Three small TextHorizontal toggles next to the big Create button.
        // The pill matching the active mode returns 0 (disabled = grayed in
        // the ribbon) so the user reads "I'm in this mode now". Doc-lock
        // (Sw2gzDocLock.IsLocked) freezes all 3 pills, matching the prior
        // chevron-sub-item behaviour.
        public int ModeRobotPillUpdate() => PillUpdate(SW2GZ.URDFExport.Sw2gzMode.Robot);
        public int ModeWorldPillUpdate() => PillUpdate(SW2GZ.URDFExport.Sw2gzMode.World);
        public int ModeAssetPillUpdate() => PillUpdate(SW2GZ.URDFExport.Sw2gzMode.Asset);

        private int PillUpdate(SW2GZ.URDFExport.Sw2gzMode pillMode)
        {
            try
            {
                if (AssemblyEnable() == 0) return 0;
                if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return 0;
                var doc = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                if (SW2GZ.URDFExport.Sw2gzDocLock.IsLocked(doc)) return 0;
                // Locked by saved attribute — user must Delete Config first.
                if (SW2GZ.URDFExport.Sw2gzDocSerialization.HasSaved(modeldoc)) return 0;
                // Disable the pill that already represents the active mode —
                // gives the grayed-out "you are here" visual cue.
                return (doc.Mode == pillMode) ? 0 : 1;
            }
            catch (Exception e)
            {
                logger.Warn("PillUpdate(" + pillMode + ") failed", e);
                return 0;
            }
        }

        // Delete-from-tree path: the user removes "SW2GZ Doc (v1)" via the
        // FeatureManager right-click → Delete. SW polls PillUpdate every UI
        // tick and HasSaved becomes false → pills re-enable naturally. The
        // in-memory Sw2gzDocStore entry may be stale (holds the last edited
        // tree), so each OpenCreate* path below resets the store on
        // !HasSaved before opening — ensures the next wizard re-seeds from
        // assembly state, matching the "deleted from disk → fresh start"
        // expectation.

        // ─── Common cluster ───────────────────────────────────────────
        // Coord button removed in v2.1.0 — advanced coord convention now
        // lives in the Create wizard, not the ribbon.
        public void OpenPreviewPmp() => LaunchPreview();
        public void OpenExportPmp()  => LaunchExport();               // existing method

        // ─── Robot cluster ────────────────────────────────────────────
        // Links and Joints used to be ribbon buttons but have moved into the
        // Create-Robot wizard PMP (face-click of the split button).
        public void OpenRobotInertiaPmp()   => OpenStub("Inertia");
        public void OpenRobotSensorsPmp()   => OpenStub("Sensors");
        public void OpenRobotActuationPmp() => OpenStub("Actuation");
        public void OpenRobotStackPmp()     => OpenStub("Stack");

        // ─── World cluster ────────────────────────────────────────────
        public void OpenWorldGroundPmp()  => OpenStub("Ground");
        public void OpenWorldAssetsPmp()  => OpenStub("Assets");
        public void OpenWorldPhysicsPmp() => OpenStub("Physics");
        public void OpenWorldScenePmp()   => OpenStub("Scene");   // legacy stub (unwired)

        // Settings — scene/environment preferences (lighting, sky, fog, grid,
        // gravity, wind, geo). Native left-dock PMP seeded from doc.World.Scene;
        // persists the doc on Okay.
        public void OpenWorldSettings()
        {
            try
            {
                if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return;
                var doc = SW2GZ.URDFExport.Sw2gzDocSerialization.Load(modeldoc)
                          ?? SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                SW2GZ.URDFExport.Sw2gzDocStore.Put(modeldoc, doc);
                if (doc.World == null) doc.World = new SW2GZ.URDFExport.Sw2gzWorldConfig();
                if (doc.World.Scene == null) doc.World.Scene = new SW2GZ.URDFExport.Sw2gzWorldSceneConfig();

                _worldSettingsPmp = new SW2GZ.UI.Pmp.Sw2gzWorldSettingsPmp(
                    (SldWorks)SwApp, doc, d => PersistDoc(modeldoc, d));
                _worldSettingsPmp.Show();
            }
            catch (Exception e)
            {
                logger.Error("OpenWorldSettings failed", e);
                MessageBox.Show("Could not open Settings: " + e.Message);
            }
        }

        // Sensors — toggle the world-level support plugins (sensor families +
        // keyboard teleop) that spawned models use. Native left-dock PMP seeded
        // from doc.World.SensorPlugins; persists the doc on Okay.
        public void OpenWorldSensors()
        {
            try
            {
                if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return;
                var doc = SW2GZ.URDFExport.Sw2gzDocSerialization.Load(modeldoc)
                          ?? SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                SW2GZ.URDFExport.Sw2gzDocStore.Put(modeldoc, doc);
                if (doc.World == null) doc.World = new SW2GZ.URDFExport.Sw2gzWorldConfig();
                if (doc.World.SensorPlugins == null) doc.World.SensorPlugins = new SW2GZ.URDFExport.Sw2gzWorldSensorsConfig();

                _worldSensorsPmp = new SW2GZ.UI.Pmp.Sw2gzWorldSensorsPmp(
                    (SldWorks)SwApp, doc, d => PersistDoc(modeldoc, d));
                _worldSensorsPmp.Show();
            }
            catch (Exception e)
            {
                logger.Error("OpenWorldSensors failed", e);
                MessageBox.Show("Could not open Sensors: " + e.Message);
            }
        }

        // ─── Asset cluster ────────────────────────────────────────────
        public void OpenAssetBodyPmp()    => OpenStub("Body");
        public void OpenAssetSurfacePmp() => OpenStub("Surface");

        // ─── Per-cluster enable callbacks ─────────────────────────────
        public int AssemblyEnable() => WizardEnable();   // reuse the existing asm-only gate

        // Preview-specific gate: enabled only when an assembly is active AND
        // a "SW2GZ Doc (v1)" attribute has been saved (i.e. the user has run
        // a Create wizard at least once). Mirrors ExportEnable exactly so the
        // two ribbon buttons unlock together — once you have a saved doc v1,
        // you can both preview AND export from it. Previously we required
        // IsRobotReady (≥1 link with components AND ≥1 joint with HasOrigin),
        // which kept Preview disabled for users with a saved one-link rigid
        // body who legitimately wanted to inspect the rendered URDF.
        public int PreviewEnable()
        {
            try
            {
                ModelDoc2 m = ActivePartOrAssembly();
                if (m == null) return 0;
                return SW2GZ.URDFExport.Sw2gzDocSerialization.HasSaved(m) ? 1 : 0;
            }
            catch (Exception e) { logger.Warn("PreviewEnable failed", e); return 0; }
        }

        // Active doc if it's a part OR an assembly, else null. Silent (no popup)
        // so it's safe to call from the polled ribbon-enable callbacks.
        private ModelDoc2 ActivePartOrAssembly()
        {
            if (SwApp == null) return null;
            ModelDoc2 m = SwApp.ActiveDoc;
            if (m == null) return null;
            int t = m.GetType();
            return (t == (int)swDocumentTypes_e.swDocPART ||
                    t == (int)swDocumentTypes_e.swDocASSEMBLY) ? m : null;
        }

        // Enable for the Asset Create/Edit ribbon buttons — Asset mode works on a
        // part document too, so it's not gated to assemblies like Robot/World.
        public int AssetCreateEnable() => ActivePartOrAssembly() != null ? 1 : 0;

        // Action-handler variant with a friendly popup (parts OR assemblies).
        private bool TryGetActiveModelDoc(out ModelDoc2 modeldoc)
        {
            modeldoc = ActivePartOrAssembly();
            if (modeldoc != null) return true;
            SwApp?.SendMsgToUser2(
                "Open a part or assembly document first.",
                (int)swMessageBoxIcon_e.swMbInformation,
                (int)swMessageBoxBtn_e.swMbOk);
            return false;
        }

        // Export-specific gate: enabled only when an assembly is active AND
        // a "SW2GZ Doc (v1)" attribute has been saved (i.e. the user has run
        // a Create wizard at least once). Mirrors the pill-lock idiom so
        // Export greys until there's something to export.
        public int ExportEnable()
        {
            try
            {
                ModelDoc2 m = ActivePartOrAssembly();
                if (m == null) return 0;
                return SW2GZ.URDFExport.Sw2gzDocSerialization.HasSaved(m) ? 1 : 0;
            }
            catch (Exception e) { logger.Warn("ExportEnable failed", e); return 0; }
        }

        public int RobotClusterEnable() => ClusterEnable(SW2GZ.UI.Ribbon.RibbonCluster.Robot);
        public int WorldClusterEnable() => ClusterEnable(SW2GZ.UI.Ribbon.RibbonCluster.World);
        public int AssetClusterEnable() => ClusterEnable(SW2GZ.UI.Ribbon.RibbonCluster.Asset);

        private int ClusterEnable(SW2GZ.UI.Ribbon.RibbonCluster cluster)
        {
            try
            {
                if (AssemblyEnable() == 0) return 0;
                if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return 0;
                var doc = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                return SW2GZ.UI.Ribbon.ClusterVisibility.IsVisible(doc.Mode, cluster) ? 1 : 0;
            }
            catch (Exception e)
            {
                logger.Warn("ClusterEnable failed", e);
                return 0;
            }
        }

        // ─── Stub PMP launcher ────────────────────────────────────────
        private void OpenStub(string title)
        {
            try
            {
                if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return;
                var doc = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                _openPanel = new SW2GZ.UI.Pmp.Sw2gzStubPmp(
                    (SldWorks)SwApp, doc, title, _ => { /* no-op for UI-only phase */ });
                _openPanel.Show();
            }
            catch (Exception e)
            {
                logger.Error("OpenStub '" + title + "' failed", e);
                MessageBox.Show("Could not open " + title + ": " + e.Message);
            }
        }

        private void LaunchPreview()
        {
            try
            {
                if (!TryGetActiveModelDoc(out ModelDoc2 modeldoc)) return;

                // Load the saved SW2GZ Doc (v1) and bridge it to the legacy
                // Sw2gzExportConfig shape the pipeline still consumes. Mirror
                // LaunchExport's Load-first pattern: the persisted attribute
                // is the source of truth, the in-memory store is a
                // per-session cache that's blank on a fresh SW launch even
                // when the attribute exists on disk. Reading store-only here
                // would surface "empty doc" after every SW restart despite
                // the user's saved tree being intact.
                var doc = SW2GZ.URDFExport.Sw2gzDocSerialization.Load(modeldoc)
                          ?? SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);

                // A part document is always an Asset preview.
                bool isPart = modeldoc.GetType() == (int)swDocumentTypes_e.swDocPART;
                bool isWorld = !isPart && doc?.Mode == SW2GZ.URDFExport.Sw2gzMode.World;
                bool isAsset = isPart || doc?.Mode == SW2GZ.URDFExport.Sw2gzMode.Asset;
                if (isWorld)
                {
                    bool hasPicks = doc.World != null &&
                        (!string.IsNullOrWhiteSpace(doc.World.Ground) ||
                         (doc.World.Assets != null && doc.World.Assets.Count > 0));
                    if (!hasPicks)
                    {
                        SwApp.SendMsgToUser2(
                            "Saved World doc has no ground or assets — open Create World and pick at least one.",
                            (int)swMessageBoxIcon_e.swMbInformation,
                            (int)swMessageBoxBtn_e.swMbOk);
                        return;
                    }
                }
                else if (isAsset)
                {
                    if (doc.Asset == null || string.IsNullOrWhiteSpace(doc.Asset.BodyPart))
                    {
                        SwApp.SendMsgToUser2(
                            "Saved Asset doc has no part — open Create Asset and pick a part.",
                            (int)swMessageBoxIcon_e.swMbInformation,
                            (int)swMessageBoxBtn_e.swMbOk);
                        return;
                    }
                }
                else if (doc?.Robot == null || doc.Robot.Links == null || doc.Robot.Links.Count == 0)
                {
                    SwApp.SendMsgToUser2(
                        "Saved SW2GZ doc has no links — open Create Robot and define at least one link.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);
                    return;
                }

                var meta = new SW2GZ.URDFExport.ExportMetaInput
                {
                    PackageName = DerivePackageNameFromAssembly(modeldoc),
                    OutputFolder = System.IO.Path.GetTempPath(),
                };
                Sw2gzExportConfig config = SW2GZ.URDFExport.Sw2gzDocToExportConfig.Bridge(doc, meta);
                logger.Info("LaunchPreview: doc v1 loaded — mode=" + doc.Mode + ", pkg=" + config.PackageName +
                            ", links=" + (config.Links?.Count ?? 0) +
                            ", joints=" + (config.Joints?.Count ?? 0));

                Sw2gzModelPreviewer.PreviewResult result =
                    isWorld ? Sw2gzModelPreviewer.RunWorldPreview((SldWorks)SwApp, modeldoc, config)
                    : isAsset ? Sw2gzModelPreviewer.RunAssetPreview((SldWorks)SwApp, modeldoc, config)
                    : Sw2gzModelPreviewer.RunPreview((SldWorks)SwApp, modeldoc, config);
                bool proceedToExport;
                using (var dlg = new PreviewDialog(result))
                {
                    // "Looks good — Export" returns OK; "Back to edit" cancels.
                    proceedToExport = dlg.ShowDialog() == DialogResult.OK;
                }

                // Confirming the preview launches the real export sequence — same
                // entry as the ribbon Export button, so it covers every mode
                // (robot / world / asset / part) the same way.
                if (proceedToExport) LaunchExport();
            }
            catch (Exception e)
            {
                logger.Error("LaunchPreview failed", e);
                MessageBox.Show("Preview failed: " + e.Message);
            }
        }

        // Derive a sensible ROS-package-name default from the open assembly
        // when Sw2gzDoc doesn't carry one yet. Preview's pipeline call needs a
        // valid sanitized name (used for the temp workspace + URDF filename);
        // a real Export run would let the user override this via the Export
        // wizard. Falls back to "robot_preview" if sanitization rejects the
        // doc title (e.g. untitled doc, all-non-alphanumeric name).
        private static string DerivePackageNameFromAssembly(ModelDoc2 modeldoc)
        {
            string raw = string.Empty;
            try { raw = modeldoc?.GetTitle() ?? string.Empty; } catch { }
            int dot = raw.LastIndexOf('.');
            if (dot > 0) raw = raw.Substring(0, dot);
            try
            {
                string sanitized = SW2GZ.Build.PackageNameSanitizer.Sanitize(raw).Value;
                if (!string.IsNullOrEmpty(sanitized)) return sanitized;
            }
            catch { /* fall through to default */ }
            return "robot_preview";
        }

        #endregion UI Callbacks

        // NOTE: the Stacks section callbacks (LaunchActuationConfig / LaunchSensorsConfig
        // / LaunchGazeboConfig / LaunchBridgeConfig and the shared OpenStackConfig helper)
        // were removed along with the Stacks ribbon buttons while the Create Model flow is
        // sanity-checked. StackConfigDialog / StackRibbonGate / StackProfile are unchanged,
        // so re-adding the buttons + a cached enable gate restores the feature.

        #region Event Methods

        public bool AttachEventHandlers()
        {
            AttachSwEvents();
            //Listen for events on all currently open docs
            AttachEventsToAllDocuments();
            return true;
        }

        private bool AttachSwEvents()
        {
            try
            {
                SwEventPtr.ActiveDocChangeNotify +=
                    new DSldWorksEvents_ActiveDocChangeNotifyEventHandler(OnDocChange);
                SwEventPtr.DocumentLoadNotify2 +=
                    new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocLoad);
                SwEventPtr.FileNewNotify2 +=
                    new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNew);
                SwEventPtr.ActiveModelDocChangeNotify +=
                    new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnModelChange);
                SwEventPtr.FileOpenPostNotify +=
                    new DSldWorksEvents_FileOpenPostNotifyEventHandler(FileOpenPostNotify);
                return true;
            }
            catch (Exception e)
            {
                logger.Error("Attaching SW events failed", e);
                return false;
            }
        }

        private bool DetachSwEvents()
        {
            try
            {
                SwEventPtr.ActiveDocChangeNotify -=
                    new DSldWorksEvents_ActiveDocChangeNotifyEventHandler(OnDocChange);
                SwEventPtr.DocumentLoadNotify2 -=
                    new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocLoad);
                SwEventPtr.FileNewNotify2 -=
                    new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNew);
                SwEventPtr.ActiveModelDocChangeNotify -=
                    new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnModelChange);
                SwEventPtr.FileOpenPostNotify -=
                    new DSldWorksEvents_FileOpenPostNotifyEventHandler(FileOpenPostNotify);
                return true;
            }
            catch (Exception e)
            {
                logger.Error("Attaching SW events failed", e);
                return false;
            }
        }

        public void AttachEventsToAllDocuments()
        {
            ModelDoc2 modDoc = (ModelDoc2)SwApp.GetFirstDocument();
            while (modDoc != null)
            {
                if (!OpenDocs.ContainsKey(modDoc))
                {
                    AttachModelDocEventHandler(modDoc);
                }
                else if (OpenDocs.ContainsKey(modDoc))
                {
                    DocumentEventHandler docHandler = OpenDocs[modDoc];
                    if (docHandler != null)
                    {
                        bool connected = docHandler.ConnectModelViews();
                        if (!connected)
                        {
                            logger.Warn("Failed to connect to model views");
                        }
                    }
                }

                modDoc = (ModelDoc2)modDoc.GetNext();
            }
        }

        public bool AttachModelDocEventHandler(ModelDoc2 modDoc)
        {
            if (modDoc == null)
            {
                return false;
            }

            if (!OpenDocs.ContainsKey(modDoc))
            {
                DocumentEventHandler docHandler;
                switch (modDoc.GetType())
                {
                    case (int)swDocumentTypes_e.swDocPART:
                        {
                            docHandler = new PartEventHandler(modDoc, this);
                            break;
                        }
                    case (int)swDocumentTypes_e.swDocASSEMBLY:
                        {
                            docHandler = new AssemblyEventHandler(modDoc, this);
                            break;
                        }
                    case (int)swDocumentTypes_e.swDocDRAWING:
                        {
                            docHandler = new DrawingEventHandler(modDoc, this);
                            break;
                        }
                    default:
                        {
                            return false; //Unsupported document type
                        }
                }
                docHandler.AttachEventHandlers();
                OpenDocs.Add(modDoc, docHandler);
            }
            return true;
        }

        public bool DetachModelEventHandler(ModelDoc2 modDoc)
        {
            OpenDocs.Remove(modDoc);
            return true;
        }

        public bool DetachEventHandlers()
        {
            DetachSwEvents();

            //Close events on all currently open docs
            DocumentEventHandler docHandler;
            int numKeys = OpenDocs.Count;
            ModelDoc2[] keys = new ModelDoc2[numKeys];

            //Remove all document event handlers
            OpenDocs.Keys.CopyTo(keys, 0);
            foreach (ModelDoc2 key in keys)
            {
                docHandler = OpenDocs[key];
                docHandler.DetachEventHandlers(); //This also removes the pair from the hash
                docHandler = null;
            }
            return true;
        }

        #endregion Event Methods

        #region Event Handlers

        //Events
        public int OnDocChange()
        {
            try { SyncRibbonToActiveDoc(); }
            catch (Exception e) { logger.Warn("OnDocChange: SyncRibbonToActiveDoc threw", e); }
            return 0;
        }

        // Sync the ribbon's Create button label + active mode cluster to the
        // active assembly's persisted doc. On a fresh SW launch the in-memory
        // store is blank, so a saved World/Asset assembly would otherwise show
        // the default "Create Robot" button + Robot cluster. The persisted
        // attribute is the source of truth; we also seed the store from it so
        // OpenCreate / Preview / Export read the same mode.
        private void SyncRibbonToActiveDoc()
        {
            if (_ribbonRegistrar == null) return;
            // Only the assembly tab has a mode-dependent Create/cluster layout to
            // sync. Part docs use the static part tab — silently no-op (and never
            // pop the assembly-only message on a part-document activation).
            ModelDoc2 modeldoc = SwApp?.ActiveDoc;
            if (modeldoc == null ||
                modeldoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY) return;

            bool saved = SW2GZ.URDFExport.Sw2gzDocSerialization.HasSaved(modeldoc);
            SW2GZ.URDFExport.Sw2gzMode mode;
            if (saved)
            {
                var loaded = SW2GZ.URDFExport.Sw2gzDocSerialization.Load(modeldoc);
                if (loaded != null) SW2GZ.URDFExport.Sw2gzDocStore.Put(modeldoc, loaded);
                mode = loaded?.Mode ?? SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc).Mode;
            }
            else
            {
                mode = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc).Mode;
            }

            if (_ribbonRegistrar.ActiveMode != mode || _ribbonRegistrar.ActiveSaved != saved)
            {
                _ribbonRegistrar.RefreshTabForMode(mode, saved);
                try { modeldoc.Extension.ActiveCommandTab = "SW2GZ"; } catch { }
            }
        }

        public int OnDocLoad(string docTitle, string docPath)
        {
            return 0;
        }

        // FileOpenPostNotify / OnFileNew walk the SW document list via
        // AttachEventsToAllDocuments() — a COM call that can throw if a referenced
        // doc was closed mid-walk. Exceptions returned to SW from a *Notify
        // callback can destabilise the host; log and swallow.
        private int FileOpenPostNotify(string FileName)
        {
            try { AttachEventsToAllDocuments(); }
            catch (Exception e) { logger.Warn("FileOpenPostNotify: AttachEventsToAllDocuments threw", e); }
            try { SyncRibbonToActiveDoc(); }
            catch (Exception e) { logger.Warn("FileOpenPostNotify: SyncRibbonToActiveDoc threw", e); }
            return 0;
        }

        public int OnFileNew(object newDoc, int docType, string templateName)
        {
            try { AttachEventsToAllDocuments(); }
            catch (Exception e) { logger.Warn("OnFileNew: AttachEventsToAllDocuments threw", e); }
            return 0;
        }

        public int OnModelChange()
        {
            return 0;
        }

        #endregion Event Handlers
    }
}