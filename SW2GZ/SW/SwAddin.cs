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

        // SW2GZ ribbon command group.
        public const int sw2gzCmdGroupID = 92;
        public const int sw2gzWizardCmdID = 0;
        // Stable user-ID handed to AddCommandItem2; SolidWorks persists toolbar
        // docking state against it, so it must not collide with other groups.
        private const int sw2gzWizardUserID = 920;
        private const int sw2gzExportUserID = 921;
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
                var registrar = new SW2GZ.UI.Ribbon.Sw2gzRibbonRegistrar(
                    CmdMgr, Sw2gzStripIconList(), Sw2gzIconList());
                registrar.Register();
            }
            catch (Exception e)
            {
                logger.Error("AddCommandMgr: ribbon registration failed", e);
            }
        }

        // True iff the registry-stored command IDs match exactly what the add-in
        // registers now. Used to decide whether SolidWorks' cached CommandManager
        // layout is stale (see AddCommandMgr / ignorePrevious).
        private static bool CompareIDs(int[] stored, int[] known)
        {
            if (stored == null || known == null) return false;
            if (stored.Length != known.Length) return false;
            foreach (int id in known)
            {
                if (System.Array.IndexOf(stored, id) < 0) return false;
            }
            return true;
        }

        public int ToolbarEnableMethod()
        {
            return 1;
        }
        public void RemoveCommandMgr()
        {
            // Symmetric with AddCommandMgr: only the command group is created, so
            // only the command group is removed (single ribbon/toolbar button).
            CmdMgr.RemoveCommandGroup(sw2gzCmdGroupID);
            logger.Info("Removing SW2GZ command group");
        }

        #endregion UI Methods

        #region UI Callbacks

        // Held as a field so the PropertyManagerPage2Handler9 COM callback stays
        // rooted while the PMP is open — as a local it would get GC'd after the
        // launch callback returns and OK/Cancel would silently stop firing.
        private SW2GZ.UI.Pmp.Sw2gzStubPmp _openPanel;

        // Export command: loads the saved model, confirms what's implemented +
        // collects meta in a dialog, then runs the bare-model export.
        public void LaunchExport()
        {
            try
            {
                if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return;

                Sw2gzExportConfig config = Sw2gzConfigSerialization.Load(modeldoc);
                if (config.Links == null || config.Links.Count == 0)
                {
                    SwApp.SendMsgToUser2(
                        "No model saved yet — run Create Model first.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);
                    return;
                }

                using (var dlg = new ExportDialog(config, (SldWorks)SwApp, modeldoc))
                {
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                }

                if (string.IsNullOrWhiteSpace(config.OutputFolder))
                {
                    SwApp.SendMsgToUser("Set an output folder before exporting.");
                    return;
                }

                Sw2gzConfigSerialization.Save((SldWorks)SwApp, modeldoc, config);
                string pkg = PackageNameSanitizer.Sanitize(config.PackageName).Value;
                SW2GZ.Validate.ValidationReport report =
                    Sw2gzModelExporter.Run((SldWorks)SwApp, modeldoc, config);
                string ws = Sw2gzModelExporter.WorkspacePath(config.OutputFolder, config.PackageName);

                if (report.HasErrors)
                {
                    SwApp.SendMsgToUser("Export finished with errors:\n• " +
                        string.Join("\n• ", System.Linq.Enumerable.ToArray(
                            System.Linq.Enumerable.Select(report.Errors, x => x.Message))));
                    return;
                }

                // Success: information icon (SendMsgToUser defaults to a caution
                // triangle, which reads as a warning for a clean export).
                SwApp.SendMsgToUser2(
                    "Exported to:\n" + ws + "\n\nBuild and launch:\n" +
                    "  cd \"" + ws + "\"\n  colcon build\n  source install/setup.bash\n" +
                    "  ros2 launch " + pkg + " gz_sim.launch.py",
                    (int)swMessageBoxIcon_e.swMbInformation,
                    (int)swMessageBoxBtn_e.swMbOk);
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

        // ─── Mode pills ───────────────────────────────────────────────
        public void ModeRobotClick() => SetMode(SW2GZ.URDFExport.Sw2gzMode.Robot);
        public void ModeWorldClick() => SetMode(SW2GZ.URDFExport.Sw2gzMode.World);
        public void ModeAssetClick() => SetMode(SW2GZ.URDFExport.Sw2gzMode.Asset);

        public int ModeRobotEnable() => AssemblyEnable();
        public int ModeWorldEnable() => AssemblyEnable();
        public int ModeAssetEnable() => AssemblyEnable();

        private void SetMode(SW2GZ.URDFExport.Sw2gzMode mode)
        {
            if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return;
            var doc = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
            doc.Mode = mode;
            // SolidWorks re-polls enable-callbacks on the next paint, so the
            // cluster visibility flips automatically — no explicit refresh
            // call (ICommandManager has no UpdateCommandBars on this SDK).
        }

        // ─── Common cluster ───────────────────────────────────────────
        public void OpenCoordPmp()   => OpenStub("Coord");
        public void OpenPreviewPmp() => LaunchPreview();
        public void OpenExportPmp()  => LaunchExport();               // existing method

        // ─── Robot cluster ────────────────────────────────────────────
        public void OpenRobotLinksPmp()     => OpenStub("Links");
        public void OpenRobotJointsPmp()    => OpenStub("Joints");
        public void OpenRobotInertiaPmp()   => OpenStub("Inertia");
        public void OpenRobotSensorsPmp()   => OpenStub("Sensors");
        public void OpenRobotActuationPmp() => OpenStub("Actuation");
        public void OpenRobotStackPmp()     => OpenStub("Stack");

        // ─── World cluster ────────────────────────────────────────────
        public void OpenWorldGroundPmp()  => OpenStub("Ground");
        public void OpenWorldAssetsPmp()  => OpenStub("Assets");
        public void OpenWorldPhysicsPmp() => OpenStub("Physics");
        public void OpenWorldScenePmp()   => OpenStub("Scene");

        // ─── Asset cluster ────────────────────────────────────────────
        public void OpenAssetBodyPmp()    => OpenStub("Body");
        public void OpenAssetSurfacePmp() => OpenStub("Surface");

        // ─── Per-cluster enable callbacks ─────────────────────────────
        public int AssemblyEnable() => WizardEnable();   // reuse the existing asm-only gate

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
                if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return;

                // Pull the live config — until the backend plan migrates Sw2gzDoc
                // into the persisted attribute, preview keeps using the legacy
                // Sw2gzExportConfig loaded from the attribute.
                Sw2gzExportConfig config = Sw2gzConfigSerialization.Load(modeldoc);
                if (config.Links == null || config.Links.Count == 0)
                {
                    SwApp.SendMsgToUser2(
                        "No model saved yet — define links from the Robot cluster first.",
                        (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);
                    return;
                }

                var result = Sw2gzModelPreviewer.RunPreview(
                    (SldWorks)SwApp, modeldoc, config);
                using (var dlg = new PreviewDialog(result))
                {
                    dlg.ShowDialog();
                }
            }
            catch (Exception e)
            {
                logger.Error("LaunchPreview failed", e);
                MessageBox.Show("Preview failed: " + e.Message);
            }
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
            return 0;
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