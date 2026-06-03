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
        // Per-stack config buttons (the "Stacks" ribbon section). Stable user IDs,
        // contiguous after the two existing buttons so SolidWorks docking state is
        // persisted distinctly per button.
        private const int sw2gzActuationUserID = 922;
        private const int sw2gzSensorsUserID   = 923;
        private const int sw2gzGazeboUserID     = 924;
        private const int sw2gzBridgeUserID     = 925;

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
            const string title = "SW2GZ";
            const string buttonName = "Create Model";
            const string toolTip = "Create Model";
            const string hint = "Generate a simulation-ready ROS 2 package (URDF, ros2_control, Gazebo, sensors) from this assembly.";

            int errs = 0;

            // Guard against SolidWorks' cached CommandManager layout. If the commands
            // stored in the registry from a previous load differ from what we register
            // now, tell SW to discard the cached group (ignorePrevious = true) instead
            // of merging old + new into duplicate buttons.
            bool ignorePrevious = false;
            object registryIDs;
            int[] knownIDs = new int[] { sw2gzWizardUserID, sw2gzExportUserID,
                sw2gzActuationUserID, sw2gzSensorsUserID, sw2gzGazeboUserID, sw2gzBridgeUserID };
            bool hadRegistryData = CmdMgr.GetGroupDataFromRegistry(sw2gzCmdGroupID, out registryIDs);
            if (hadRegistryData && !CompareIDs((int[])registryIDs, knownIDs))
            {
                ignorePrevious = true;
            }

            ICommandGroup grp = CmdMgr.CreateCommandGroup2(
                sw2gzCmdGroupID, title, toolTip, hint, -1, ignorePrevious, ref errs);
            if (grp == null)
            {
                logger.Error("Failed to create SW2GZ command group (error code " + errs + ")");
            }
            else
            {
                grp.IconList = Sw2gzStripIconList();  // per-button glyphs (strip)
                grp.MainIconList = Sw2gzIconList();    // command-group glyph (cube)

                // Image index 0 = cube column of the strip.
                int cmdIndex = grp.AddCommandItem2(
                    buttonName, -1, hint, toolTip, 0,
                    "LaunchWizard", "WizardEnable", sw2gzWizardUserID,
                    (int)swCommandItemType_e.swToolbarItem);
                if (cmdIndex < 0)
                {
                    logger.Error("Failed to add SW2GZ command item to the command group");
                }

                // Image index 1 = export (cube + arrow) column of the strip.
                int exportIndex = grp.AddCommandItem2(
                    "Export", -1, "Export the saved model to a ROS 2 / Gz package", "Export", 1,
                    "LaunchExport", "WizardEnable", sw2gzExportUserID,
                    (int)swCommandItemType_e.swToolbarItem);
                if (exportIndex < 0)
                {
                    logger.Error("Failed to add SW2GZ Export command item");
                }

                // Four per-stack config buttons (the "Stacks" ribbon section). Each
                // opens a parameterized StackConfigDialog and shares one enable method
                // (StacksEnable) — they're greyed until a model is saved in
                // RobotPackage mode. Image index 0 = cube column of the strip
                // (distinct per-stack icons are a deferred follow-up).
                int actIndex = grp.AddCommandItem2("Actuation", -1,
                    "Configure the actuation backend (none / Gz plugin / ros2_control)", "Actuation", 0,
                    "LaunchActuationConfig", "StacksEnable", sw2gzActuationUserID,
                    (int)swCommandItemType_e.swToolbarItem);
                if (actIndex < 0)
                {
                    logger.Error("Failed to add SW2GZ Actuation command item");
                }

                int senIndex = grp.AddCommandItem2("Sensors", -1,
                    "Configure sensors emitted into the Gz model", "Sensors", 0,
                    "LaunchSensorsConfig", "StacksEnable", sw2gzSensorsUserID,
                    (int)swCommandItemType_e.swToolbarItem);
                if (senIndex < 0)
                {
                    logger.Error("Failed to add SW2GZ Sensors command item");
                }

                int gzIndex = grp.AddCommandItem2("Gazebo", -1,
                    "Configure Gazebo simulation options", "Gazebo", 0,
                    "LaunchGazeboConfig", "StacksEnable", sw2gzGazeboUserID,
                    (int)swCommandItemType_e.swToolbarItem);
                if (gzIndex < 0)
                {
                    logger.Error("Failed to add SW2GZ Gazebo command item");
                }

                int brIndex = grp.AddCommandItem2("Bridge", -1,
                    "Configure the ros_gz_bridge topic selection", "Bridge", 0,
                    "LaunchBridgeConfig", "StacksEnable", sw2gzBridgeUserID,
                    (int)swCommandItemType_e.swToolbarItem);
                if (brIndex < 0)
                {
                    logger.Error("Failed to add SW2GZ Bridge command item");
                }

                // Exactly ONE entry: the ribbon/toolbar button. HasMenu=false so the
                // command group does NOT auto-create a duplicate Tools-menu item.
                grp.HasToolbar = true;
                grp.HasMenu = false;
                grp.Activate();

                // Place the button on a dedicated CommandManager ribbon tab for
                // assembly documents. Best-effort: if any step fails we still have
                // the toolbar button.
                try
                {
                    int cmdId = grp.get_CommandID(cmdIndex);
                    int exportCmdId = grp.get_CommandID(exportIndex);

                    // Remove any existing SW2GZ tab from a previous load BEFORE re-adding,
                    // otherwise AddCommandTabBox() stacks a second button box on top of the
                    // persisted one each session — the cause of the duplicate ribbon button.
                    CommandTab existing = CmdMgr.GetCommandTab((int)swDocumentTypes_e.swDocASSEMBLY, title);
                    if (existing != null)
                    {
                        CmdMgr.RemoveCommandTab(existing);
                        existing = null;
                    }

                    CommandTab tab = CmdMgr.AddCommandTab((int)swDocumentTypes_e.swDocASSEMBLY, title);
                    if (tab != null)
                    {
                        int textBelow = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

                        // Primary box: Create Model + Export.
                        ICommandTabBox box = tab.AddCommandTabBox();
                        int[] cmdIds = new int[] { cmdId, exportCmdId };
                        int[] textTypes = new int[] { textBelow, textBelow };
                        box.AddCommands(cmdIds, textTypes);

                        // Second box → renders as a distinct ribbon SECTION: the four
                        // per-stack "Stacks" config buttons. Kept inside this try/catch
                        // so a failure here still leaves the two main buttons working.
                        ICommandTabBox box2 = tab.AddCommandTabBox();
                        int[] stackIds = { grp.get_CommandID(actIndex), grp.get_CommandID(senIndex),
                                           grp.get_CommandID(gzIndex), grp.get_CommandID(brIndex) };
                        int[] stackText = { textBelow, textBelow, textBelow, textBelow };
                        box2.AddCommands(stackIds, stackText);

                        logger.Info("Added SW2GZ command tab for assembly documents");
                    }
                }
                catch (Exception e)
                {
                    // Tab placement is the most fragile part of the COM surface;
                    // the toolbar button still exposes the command.
                    logger.Warn("SW2GZ ribbon tab placement failed (toolbar button still available)", e);
                }

                logger.Info("SW2GZ command group activated");
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

        // The SW2GZ entry point. Opens the native PropertyManagerPage export shell
        // (left panel) so the 3D viewport stays live. Invoked by name (reflection)
        // from the ribbon/toolbar button, so it must stay public.
        public void LaunchWizard()
        {
            try
            {
                ModelDoc2 modeldoc = SwApp.ActiveDoc;
                if (modeldoc == null ||
                    modeldoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    SwApp.SendMsgToUser("Open an assembly first.");
                    return;
                }

                var pmp = new Sw2gzExportPmp((SldWorks)SwApp, modeldoc);
                pmp.Show();
            }
            catch (Exception e)
            {
                logger.Error("An exception was caught launching the SW2GZ export panel", e);
                MessageBox.Show("There was a problem launching the SW2GZ export panel: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        // Export command: loads the saved model, confirms what's implemented +
        // collects meta in a dialog, then runs the bare-model export.
        public void LaunchExport()
        {
            try
            {
                ModelDoc2 modeldoc = SwApp.ActiveDoc;
                if (modeldoc == null || modeldoc.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    SwApp.SendMsgToUser("Open an assembly first.");
                    return;
                }

                Sw2gzExportConfig config = Sw2gzConfigSerialization.Load(modeldoc);
                if (config.Links == null || config.Links.Count == 0)
                {
                    SwApp.SendMsgToUser("No model saved yet — run Create Model first.");
                    return;
                }

                using (var dlg = new ExportDialog(config))
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
        // active document is an assembly (the wizard targets assemblies).
        public int WizardEnable()
        {
            ModelDoc2 modeldoc = SwApp.ActiveDoc;
            return (modeldoc != null &&
                    modeldoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY) ? 1 : 0;
        }

        // Enable-state for the Stacks section buttons. They tune a robot package built
        // from a saved model, so they're enabled only when the active doc is an assembly
        // AND a model has been saved (Create Model run) AND the export Mode is
        // RobotPackage. StackRibbonGate holds the pure rule; the assembly check is here.
        // Never throw into the COM caller — any failure greys the buttons (return 0).
        public int StacksEnable()
        {
            try
            {
                ModelDoc2 m = SwApp.ActiveDoc as ModelDoc2;
                if (m == null || m.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY) return 0;
                Sw2gzExportConfig config = Sw2gzConfigSerialization.Load(m);
                return SW2GZ.Ros2.StackRibbonGate.IsEnabled(config) ? 1 : 0;
            }
            catch (Exception e) { logger.Warn("StacksEnable failed", e); return 0; }
        }

        #endregion UI Callbacks

        #region Stacks Section Callbacks

        // String-named launch handlers (resolved by SW via reflection — must stay public).
        public void LaunchActuationConfig() => OpenStackConfig(StackTarget.Actuation);
        public void LaunchSensorsConfig()   => OpenStackConfig(StackTarget.Sensors);
        public void LaunchGazeboConfig()    => OpenStackConfig(StackTarget.Gazebo);
        public void LaunchBridgeConfig()    => OpenStackConfig(StackTarget.Bridge);

        // Open the per-stack config dialog for the active assembly, editing a CLONE of
        // its saved StackProfile and persisting only if the user clicks OK. Guarded +
        // logged; never throws into the COM caller.
        private void OpenStackConfig(StackTarget target)
        {
            try
            {
                ModelDoc2 m = SwApp.ActiveDoc as ModelDoc2;
                if (m == null || m.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    SwApp.SendMsgToUser("Open an assembly first.");
                    return;
                }
                Sw2gzExportConfig config = Sw2gzConfigSerialization.Load(m);
                if (!SW2GZ.Ros2.StackRibbonGate.IsEnabled(config))
                {
                    SwApp.SendMsgToUser("Run Create Model first (and set Mode = Robot Package).");
                    return;
                }
                using (var dlg = new StackConfigDialog(target,
                           config.Stacks ?? SW2GZ.Ros2.StackProfile.Default()))
                {
                    if (dlg.ShowDialog() == DialogResult.OK && dlg.Result != null)
                    {
                        config.Stacks = dlg.Result;
                        Sw2gzConfigSerialization.Save((SldWorks)SwApp, m, config);
                        logger.Info("Stacks: saved " + target + " -> Actuation=" + config.Stacks.Actuation
                            + " GzSim=" + config.Stacks.GzSim + " Sensors=" + config.Stacks.SensorsEnabled);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error("Stack config (" + target + ") failed", e);
                MessageBox.Show("Stack config failed:\n" + e.Message);
            }
        }

        #endregion Stacks Section Callbacks

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