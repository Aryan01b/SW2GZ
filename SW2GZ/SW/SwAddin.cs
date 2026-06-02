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
using SW2GZ.Build.Model;
using SW2GZ.SwSurface;
using SW2GZ.UI;
using SW2GZ.UI.Services.Sw;
using SW2GZ.UI.ViewModels;
using SW2GZ.UI.Wizard;
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
        public const int flyoutGroupID = 91;

        // SW2GZ ribbon command group (distinct from flyoutGroupID).
        public const int sw2gzCmdGroupID = 92;
        public const int sw2gzWizardCmdID = 0;
        // Stable user-ID handed to AddCommandItem2; SolidWorks persists toolbar
        // docking state against it, so it must not collide with other groups.
        private const int sw2gzWizardUserID = 920;

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
        private static string[] Sw2gzIconList()
        {
            string dir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location),
                "images");
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

        public void AddCommandMgr()
        {
            const string title = "SW2GZ";
            const string toolTip = "SW2GZ — Export to ROS 2 / Gazebo";
            const string hint = "Generate a simulation-ready ROS 2 package (URDF, ros2_control, Gazebo, sensors) from this assembly.";

            int errs = 0;
            ICommandGroup grp = CmdMgr.CreateCommandGroup2(
                sw2gzCmdGroupID, title, toolTip, hint, -1, false, ref errs);
            if (grp == null)
            {
                logger.Error("Failed to create SW2GZ command group (error code " + errs + ")");
            }
            else
            {
                string[] images = Sw2gzIconList();
                grp.IconList = images;       // toolbar button icons
                grp.MainIconList = images;   // command-group glyph

                int cmdIndex = grp.AddCommandItem2(
                    "Export to ROS 2 / Gz", -1, hint, toolTip, 0,
                    "LaunchWizard", "WizardEnable", sw2gzWizardUserID,
                    (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem));
                if (cmdIndex < 0)
                {
                    logger.Error("Failed to add SW2GZ wizard command item to the command group");
                }

                grp.HasToolbar = true;
                grp.HasMenu = true;
                grp.Activate();

                // Place the button on a dedicated CommandManager ribbon tab for
                // assembly documents. Best-effort: if any step fails we still have
                // the toolbar + menu + Tools fallback below.
                try
                {
                    int cmdId = grp.get_CommandID(cmdIndex);
                    ICommandTab tab = CmdMgr.GetCommandTab((int)swDocumentTypes_e.swDocASSEMBLY, title);
                    if (tab == null)
                    {
                        tab = CmdMgr.AddCommandTab((int)swDocumentTypes_e.swDocASSEMBLY, title);
                    }
                    if (tab != null)
                    {
                        ICommandTabBox box = tab.AddCommandTabBox();
                        int[] cmdIds = { cmdId };
                        int[] textTypes = { (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow };
                        box.AddCommands(cmdIds, textTypes);
                        logger.Info("Added SW2GZ command tab for assembly documents");
                    }
                }
                catch (Exception e)
                {
                    // Tab placement is the most fragile part of the COM surface;
                    // toolbar + menu + Tools fallback still expose the command.
                    logger.Warn("SW2GZ ribbon tab placement failed (toolbar/menu still available)", e);
                }

                logger.Info("SW2GZ command group activated");
            }

            // Fallback: a single Tools-menu entry so the wizard is always reachable
            // even if the ribbon tab needs in-session tuning. Replaces the retired
            // (and throwing) "Export as URDF" part/assembly menu items.
            int ret = SwApp.AddMenuItem5((int)swDocumentTypes_e.swDocASSEMBLY, add_in_id_, "SW2GZ…@&Tools",
                -1, "LaunchWizard", "", hint, Sw2gzIconList());
            if (ret < 0)
            {
                logger.Error("Failure to add Tools menu item 'SW2GZ'");
            }
            else
            {
                logger.Info("Added SW2GZ Tools menu fallback entry");
            }
        }

        public int ToolbarEnableMethod()
        {
            return 1;
        }
        public void RemoveCommandMgr()
        {
            // Symmetric with AddCommandMgr: remove the Tools fallback then the group.
            SwApp.RemoveMenu((int)swDocumentTypes_e.swDocASSEMBLY, "SW2GZ…@&Tools", "");
            logger.Info("Removing SW2GZ Tools menu entry");

            CmdMgr.RemoveCommandGroup(sw2gzCmdGroupID);
            logger.Info("Removing SW2GZ command group");
        }

        #endregion UI Methods

        #region UI Callbacks

        public void SetupAssemblyExporter()
        {
            ModelDoc2 modeldoc = SwApp.ActiveDoc;
            logger.Info("Assembly export called for file " + modeldoc.GetTitle());
            bool saveAndRebuild = false;
            if (modeldoc.GetSaveFlag())
            {
                saveAndRebuild = true;
                logger.Info("Save is required");
            }
            else if (modeldoc.Extension.NeedsRebuild2 !=
                (int)swModelRebuildStatus_e.swModelRebuildStatus_FullyRebuilt)
            {
                saveAndRebuild = true;
                logger.Info("A rebuild is required");
            }
            if (saveAndRebuild ||
                MessageBox.Show("The SW to URDF exporter requires saving and/or rebuilding before continuing",
                "Save and rebuild document?", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                int options = (int)swSaveAsOptions_e.swSaveAsOptions_SaveReferenced |
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent;
                logger.Info("Saving assembly");
                modeldoc.Save3(options, 0, 0);

                logger.Info("Opening property manager");
                SetupPropertyManager();
            }
        }

        public void AssemblyURDFExporter()
        {
            try
            {
                SetupAssemblyExporter();
            }
            catch (Exception e)
            {
                logger.Error("An exception was caught when trying to setup the assembly exporter", e);
                MessageBox.Show("There was a problem setting up the property manager: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        public void SetupPropertyManager()
        {
            ExportPropertyManager pm = new ExportPropertyManager((SldWorks)SwApp);
            logger.Info("Loading config tree");
            bool success = pm.LoadConfigTree();

            if (success)
            {
                logger.Info("Showing property manager");
                pm.Show();
            }
        }

        // The new SW2GZ wizard entry point. Composes a preview RobotModel from the
        // active assembly and shows the WPF wizard, wired to the SW-backed services
        // so Browse / Assign-geometry / Finish all work in-session. Invoked by name
        // (reflection) from both the ribbon button and the Tools fallback, so it
        // must stay public.
        public void LaunchWizard()
        {
            try
            {
                ModelDoc2 modeldoc = SwApp.ActiveDoc;
                if (modeldoc == null ||
                    modeldoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    SwApp.SendMsgToUser("Open an assembly first, then launch SW2GZ.");
                    return;
                }

                // Save/rebuild guard — mirror the legacy assembly exporter so the
                // mass/tessellation reads operate on an up-to-date model.
                bool needsSave = modeldoc.GetSaveFlag() ||
                    modeldoc.Extension.NeedsRebuild2 !=
                        (int)swModelRebuildStatus_e.swModelRebuildStatus_FullyRebuilt;
                if (needsSave)
                {
                    int options = (int)swSaveAsOptions_e.swSaveAsOptions_SaveReferenced |
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent;
                    logger.Info("Saving assembly before launching SW2GZ wizard");
                    modeldoc.Save3(options, 0, 0);
                }

                // SW boundary services — same construction ExportHelper uses.
                var assemblyDoc = (AssemblyDoc)modeldoc;
                var mass = new SolidWorksMassProperties((SldWorks)SwApp, assemblyDoc);
                var walker = new SolidWorksAssemblyWalker(assemblyDoc);
                var tess = new SolidWorksMeshTessellator((SldWorks)SwApp, assemblyDoc);
                var appearance = new DefaultAppearanceSource();

                string title = modeldoc.GetTitle();
                if (string.IsNullOrWhiteSpace(title))
                    title = "robot";

                var composer = new WizardModelComposer(mass, walker, tess, appearance);
                var meta = new RobotMeta(title, null, null, null, CoordinateConvention.Identity);
                WizardPreview preview = composer.Compose(meta);

                // Build the wizard VM from the preview + the SW-backed services so
                // Browse / Assign-geometry / Finish all operate on the live model.
                var vm = new WizardViewModel(
                    new WinFormsFolderBrowserService(),
                    new SwViewportSelectionService((SldWorks)SwApp),
                    new SwThemeService((SldWorks)SwApp),
                    new Sw2gzPipelineExportRunner(mass, walker, tess, appearance),
                    preview.Links,
                    preview.JointCount,
                    preview.Model,
                    preview.Joints);

                // Ensure a WPF Application exists for resource resolution; a single
                // ShowDialog works even without one, but the wizard pulls merged
                // resource dictionaries so an Application keeps them resolvable.
                if (System.Windows.Application.Current == null)
                {
                    var app = new System.Windows.Application
                    {
                        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
                    };
                }

                var window = new WizardWindow(vm);
                try
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(window);
                    helper.Owner = (IntPtr)SwApp.IFrameObject().GetHWnd();
                }
                catch (Exception ownerEx)
                {
                    // Non-fatal: the dialog just won't be owned by the SW main window.
                    logger.Warn("Could not set SW2GZ wizard owner window", ownerEx);
                }

                window.ShowDialog();
            }
            catch (Exception e)
            {
                logger.Error("An exception was caught launching the SW2GZ wizard", e);
                MessageBox.Show("There was a problem launching the SW2GZ wizard: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        // Enable-state callback for the ribbon/menu command: enabled only when the
        // active document is an assembly (the wizard targets assemblies).
        public int WizardEnable()
        {
            ModelDoc2 modeldoc = SwApp.ActiveDoc;
            return (modeldoc != null &&
                    modeldoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY) ? 1 : 0;
        }

        #endregion UI Callbacks

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

        private int FileOpenPostNotify(string FileName)
        {
            AttachEventsToAllDocuments();
            return 0;
        }

        public int OnFileNew(object newDoc, int docType, string templateName)
        {
            AttachEventsToAllDocuments();
            return 0;
        }

        public int OnModelChange()
        {
            return 0;
        }

        #endregion Event Handlers
    }
}