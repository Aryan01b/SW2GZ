/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Builds the SW2GZ ribbon tab — four clusters (Common / Robot / World / Asset).
Three mode pills + 15 panel buttons + 3 Common buttons = 18 command items
total (per RibbonCommandIds).

This file does NOT own the per-button callbacks — SwAddin supplies the
callback method names by reflection (the SolidWorks PMP convention) so the
callback bodies live next to the rest of the add-in plumbing.

Mirrors the existing AddCommandMgr layout pattern:
  CmdMgr.CreateCommandGroup2
    → group.AddCommandItem2 × 18
    → CmdMgr.GetCommandTab / AddCommandTab
      → tab.AddCommandTabBox × 4   (one per cluster)
        → box.AddCommands per cluster

Each panel button's enable-callback returns 1 only when its cluster is visible
for the current mode (per ClusterVisibility). Mode pills are always visible.
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2GZ.Utilities;

namespace SW2GZ.UI.Ribbon
{
    internal sealed class Sw2gzRibbonRegistrar
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly ICommandManager _cmdMgr;
        private readonly string[] _stripIcons;
        private readonly string[] _mainIcons;

        // userId -> AddCommandItem2 return index. Cached during AddItem; the
        // get_CommandID(idx) resolution happens AFTER grp.Activate() because the
        // SW SDK only returns valid command IDs once the group is activated.
        // Resolving before Activate quietly returns zero/invalid cmdIds, the
        // tab boxes get filled with bad IDs, and SW silently hides the tab.
        private readonly Dictionary<int, int> _userToCmdIdx = new Dictionary<int, int>();

        // userId -> cmdId. Populated by ResolveCmdIds() after Activate.
        private readonly Dictionary<int, int> _userToCmdId = new Dictionary<int, int>();

        public Sw2gzRibbonRegistrar(ICommandManager cmdMgr, string[] stripIcons, string[] mainIcons)
        {
            _cmdMgr     = cmdMgr     ?? throw new ArgumentNullException(nameof(cmdMgr));
            _stripIcons = stripIcons ?? throw new ArgumentNullException(nameof(stripIcons));
            _mainIcons  = mainIcons  ?? throw new ArgumentNullException(nameof(mainIcons));
        }

        public void Register()
        {
            _userToCmdIdx.Clear();
            _userToCmdId.Clear();

            const string title = "SW2GZ";
            int errs = 0;

            // Always discard SolidWorks' cached CommandManager layout for this
            // group. v2.1.0 has 18 commands where the v2.1.1 ribbon had 2; SW's
            // cached tab placement / visibility from the prior install was
            // applied to the rebuilt tab and silently hid it. Force-fresh is
            // what the SW SDK examples recommend for an upgrade of this shape.
            bool ignorePrevious = true;
            object registryIDs;
            _cmdMgr.GetGroupDataFromRegistry(RibbonCommandIds.CmdGroupId, out registryIDs);
            logger.Info("Sw2gzRibbonRegistrar: ignorePrevious=true (registry had cached IDs: " +
                (registryIDs != null) + ")");

            ICommandGroup grp = _cmdMgr.CreateCommandGroup2(
                RibbonCommandIds.CmdGroupId, title,
                "SW2GZ — SolidWorks → ROS 2 + Gazebo Harmonic",
                "Author Robot / World / Asset packages from this assembly.",
                -1, ignorePrevious, ref errs);

            if (grp == null)
            {
                logger.Error("Sw2gzRibbonRegistrar: CreateCommandGroup2 failed (err=" + errs + ")");
                return;
            }

            grp.IconList = _stripIcons;
            grp.MainIconList = _mainIcons;

            int toolbar = (int)swCommandItemType_e.swToolbarItem;

            // Strip column indices — match the column order in
            // scripts\GenerateIcons.ps1 ($drawers list).
            //
            // 0 ModeFlyout | 1 Robot | 2 World | 3 Asset |
            // 4 Coord | 5 Preview | 6 Export |
            // 7 Links | 8 Joints | 9 Inertia | 10 Sensors | 11 Actuation | 12 Stack |
            // 13 Ground | 14 Assets | 15 Physics | 16 Scene |
            // 17 Body | 18 Surface
            const int IMG_COORD = 4, IMG_PREVIEW = 5, IMG_EXPORT = 6;
            // IMG_LINKS=7, IMG_JOINTS=8 — Links/Joints moved into the Create-Robot
            // wizard PMP; the strip columns are kept (still drawn) so we don't
            // have to renumber later columns.
            const int IMG_INERTIA = 9;
            const int IMG_SENSORS = 10, IMG_ACTUATION = 11, IMG_STACK = 12;
            const int IMG_GROUND = 13, IMG_ASSETS = 14, IMG_PHYSICS = 15, IMG_SCENE = 16;
            const int IMG_BODY = 17, IMG_SURFACE = 18;

            // Common cluster — static "Create" button first, then Coord/Preview/Export.
            const int IMG_CREATE = 0;   // strip column 0 — mode-trio glyph, reused as the generic Create face
            AddItem(grp, RibbonCommandIds.ModeCreate, "Create",
                    "Create new content in the active mode (mode shown by pill at right).",
                    "OpenCreatePmp", "AssemblyEnable", IMG_CREATE, toolbar);
            AddItem(grp, RibbonCommandIds.CoordPmp,   "Coord",   "Coordinate convention (advanced)", "OpenCoordPmp",   "AssemblyEnable", IMG_COORD,   toolbar);
            AddItem(grp, RibbonCommandIds.PreviewPmp, "Preview", "Browser-based 3D preview",         "OpenPreviewPmp", "AssemblyEnable", IMG_PREVIEW, toolbar);
            AddItem(grp, RibbonCommandIds.ExportPmp,  "Export",  "Export ROS 2 / Gz package",        "OpenExportPmp",  "AssemblyEnable", IMG_EXPORT,  toolbar);

            // Robot cluster — Links/Joints moved into the Create-Robot wizard PMP.
            AddItem(grp, RibbonCommandIds.RobotInertia,   "Inertia",   "Per-link inertia",       "OpenRobotInertiaPmp",   "RobotClusterEnable", IMG_INERTIA,   toolbar);
            AddItem(grp, RibbonCommandIds.RobotSensors,   "Sensors",   "Sensor mounts",          "OpenRobotSensorsPmp",   "RobotClusterEnable", IMG_SENSORS,   toolbar);
            AddItem(grp, RibbonCommandIds.RobotActuation, "Actuation", "ros2_control / gz",      "OpenRobotActuationPmp", "RobotClusterEnable", IMG_ACTUATION, toolbar);
            AddItem(grp, RibbonCommandIds.RobotStack,     "Stack",     "RSP + JSB + bridge",     "OpenRobotStackPmp",     "RobotClusterEnable", IMG_STACK,     toolbar);

            // World cluster
            AddItem(grp, RibbonCommandIds.WorldGround,  "Ground",  "Static ground / heightmap", "OpenWorldGroundPmp",  "WorldClusterEnable", IMG_GROUND,  toolbar);
            AddItem(grp, RibbonCommandIds.WorldAssets,  "Assets",  "Non-ground asset includes", "OpenWorldAssetsPmp",  "WorldClusterEnable", IMG_ASSETS,  toolbar);
            AddItem(grp, RibbonCommandIds.WorldPhysics, "Physics", "Engine + step + RTF",       "OpenWorldPhysicsPmp", "WorldClusterEnable", IMG_PHYSICS, toolbar);
            AddItem(grp, RibbonCommandIds.WorldScene,   "Scene",   "Light + sky + GUI",         "OpenWorldScenePmp",   "WorldClusterEnable", IMG_SCENE,   toolbar);

            // Asset cluster
            AddItem(grp, RibbonCommandIds.AssetBody,    "Body",    "Single-part body",          "OpenAssetBodyPmp",    "AssetClusterEnable", IMG_BODY,    toolbar);
            AddItem(grp, RibbonCommandIds.AssetSurface, "Surface", "Friction / contact",        "OpenAssetSurfacePmp", "AssetClusterEnable", IMG_SURFACE, toolbar);

            // Mode pills — TextHorizontal style applied in BuildCommonTabBox.
            // Image columns 1/2/3 = Robot/World/Asset glyphs (same icons as
            // the deleted chevron sub-items used). Click callbacks are the
            // existing ModeRobotClick / WorldClick / AssetClick handlers.
            AddItem(grp, RibbonCommandIds.ModeRobotPill, "Robot", "Switch to Robot mode (disabled when already active or doc-locked).", "ModeRobotClick", "ModeRobotPillUpdate", 1, toolbar);
            AddItem(grp, RibbonCommandIds.ModeWorldPill, "World", "Switch to World mode (disabled when already active or doc-locked).", "ModeWorldClick", "ModeWorldPillUpdate", 2, toolbar);
            AddItem(grp, RibbonCommandIds.ModeAssetPill, "Asset", "Switch to Asset mode (disabled when already active or doc-locked).", "ModeAssetClick", "ModeAssetPillUpdate", 3, toolbar);

            grp.HasToolbar = true;
            grp.HasMenu = false;
            grp.Activate();

            // CRITICAL: get_CommandID is only valid AFTER Activate. Resolve
            // every userId -> cmdId here so BuildTab's AddBox calls can pass
            // real command IDs into ICommandTabBox.AddCommands. Doing this in
            // AddItem (before Activate) was the v2.1.0 hang where the tab
            // registered with all-zero cmdIds and SW silently dropped it.
            ResolveCmdIds(grp);

            BuildTab(title, grp);
            logger.Info("Sw2gzRibbonRegistrar: registered " + _userToCmdId.Count +
                " commands across 4 clusters");
        }

        private void ResolveCmdIds(ICommandGroup grp)
        {
            foreach (var kvp in _userToCmdIdx)
            {
                int cmdId = grp.get_CommandID(kvp.Value);
                _userToCmdId[kvp.Key] = cmdId;
            }
            logger.Info("Sw2gzRibbonRegistrar: resolved " + _userToCmdId.Count +
                " cmdIds post-Activate");
        }

        // Mirrors SwAddin.AddCommandMgr's AddCommandItem2 parameter order:
        //   (name, position, hint, tooltip, image, clickMethod, enableMethod, userId, itemType)
        // The 'tip' arg here lands in the hint slot (long description); 'name' is
        // reused as the tooltip (short label hover).
        private void AddItem(ICommandGroup grp, int userId, string name, string tip,
                             string clickMethod, string enableMethod, int img, int kind)
        {
            int idx = grp.AddCommandItem2(name, -1, tip, name, img, clickMethod, enableMethod, userId, kind);
            if (idx < 0)
            {
                logger.Error("Sw2gzRibbonRegistrar: AddCommandItem2 failed for '" + name + "' (userId=" + userId + ")");
                return;
            }
            // Stash the raw index. The userId -> cmdId resolution happens in
            // ResolveCmdIds() AFTER grp.Activate(); see the comment there.
            _userToCmdIdx[userId] = idx;
        }

        // Per-mode panel-cluster user-ID lists. Used by both BuildTab (initial
        // load — defaults to Robot) and RebuildTabForMode (after the user
        // picks a different mode from the Mode flyout dropdown). Exposed as
        // arrays so the same data drives both the initial build and rebuild.
        private static readonly int[] RobotClusterUserIds = new[] {
            RibbonCommandIds.RobotInertia,
            RibbonCommandIds.RobotSensors, RibbonCommandIds.RobotActuation, RibbonCommandIds.RobotStack };
        private static readonly int[] WorldClusterUserIds = new[] {
            RibbonCommandIds.WorldGround, RibbonCommandIds.WorldAssets,
            RibbonCommandIds.WorldPhysics, RibbonCommandIds.WorldScene };
        private static readonly int[] AssetClusterUserIds = new[] {
            RibbonCommandIds.AssetBody, RibbonCommandIds.AssetSurface };

        // Active mode used by the next BuildTab call. Refresh sets this then
        // calls BuildTab; the initial Register call also drives it from the
        // Sw2gzDoc state in SwAddin (see RefreshTabForMode below).
        private SW2GZ.URDFExport.Sw2gzMode _activeMode = SW2GZ.URDFExport.Sw2gzMode.Robot;

        // L3b only-one-mode-visible behaviour: SW's enable-callback can gray
        // a button but not hide it. To truly hide non-active mode clusters
        // we tear down the tab and rebuild with only the active mode's box.
        public void RefreshTabForMode(SW2GZ.URDFExport.Sw2gzMode mode)
        {
            _activeMode = mode;
            BuildTab("SW2GZ", null);   // grp not needed for tab rebuild
        }

        private void BuildTab(string title, ICommandGroup grp)
        {
            int asmType = (int)swDocumentTypes_e.swDocASSEMBLY;

            // Drop any existing tab from a previous load BEFORE re-adding,
            // otherwise AddCommandTabBox stacks duplicate boxes on top of the
            // persisted one each session.
            CommandTab existing = _cmdMgr.GetCommandTab(asmType, title);
            if (existing != null) _cmdMgr.RemoveCommandTab(existing);

            CommandTab tab = _cmdMgr.AddCommandTab(asmType, title);
            if (tab == null)
            {
                logger.Warn("Sw2gzRibbonRegistrar: AddCommandTab returned null — toolbar buttons still available");
                return;
            }
            logger.Info("Sw2gzRibbonRegistrar: tab '" + title + "' added for swDocASSEMBLY (mode=" + _activeMode + ")");

            int textBelow = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

            // Common cluster — static "Create" button + 3 pills + 3 common actions.
            BuildCommonTabBox(tab, textBelow);

            // Only the ACTIVE mode's cluster goes on the tab. L3b "one mode
            // at a time" — the others are not just disabled, they don't appear.
            int[] activeIds;
            string activeName;
            switch (_activeMode)
            {
                case SW2GZ.URDFExport.Sw2gzMode.World: activeIds = WorldClusterUserIds; activeName = "World"; break;
                case SW2GZ.URDFExport.Sw2gzMode.Asset: activeIds = AssetClusterUserIds; activeName = "Asset"; break;
                default:                               activeIds = RobotClusterUserIds; activeName = "Robot"; break;
            }
            AddBox(tab, textBelow, activeIds);
            logger.Info("Sw2gzRibbonRegistrar: rendered " + activeName + " panel cluster only (L3b hide-others)");
        }

        // Build the Common tab box: [Create] [pills] [Coord] [Preview] [Export].
        private void BuildCommonTabBox(CommandTab tab, int textBelow)
        {
            ICommandTabBox box = tab.AddCommandTabBox();
            if (box == null)
            {
                logger.Warn("Sw2gzRibbonRegistrar: Common tab box AddCommandTabBox returned null");
                return;
            }
            // Capacity 8 = 1 Create + 3 pills + 3 common buttons + slack.
            var cmdIds = new List<int>(8);
            var textTypes = new List<int>(8);

            // Static "Create" button (regular AddCommandItem2, no flyout) — replaces
            // the prior IFlyoutGroup-based dynamic-label trick which dropped the
            // button on mode switch (CreateFlyoutGroup2 doesn't survive re-call with
            // zero sub-items) and rendered a chevron the NoFlyout flag couldn't hide.
            // Mode context comes from the 3 pills appended below.
            if (_userToCmdId.TryGetValue(RibbonCommandIds.ModeCreate, out int createCmdId))
            {
                cmdIds.Add(createCmdId);
                textTypes.Add(textBelow);
            }
            // Mode pills — TextHorizontal stacks 3-per-column. The 3 pills
            // sit immediately right of the big Create button, matching its
            // height. NOTE: per-pill enable is via PillUpdate (set on each
            // AddCommandItem2), not via the textType.
            int textHorizontal = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal;
            foreach (int uid in new[] { RibbonCommandIds.ModeRobotPill, RibbonCommandIds.ModeWorldPill, RibbonCommandIds.ModeAssetPill })
            {
                if (_userToCmdId.TryGetValue(uid, out int pillCmdId))
                {
                    cmdIds.Add(pillCmdId);
                    textTypes.Add(textHorizontal);
                }
                else
                {
                    logger.Warn("Sw2gzRibbonRegistrar: pill cmdId missing for userId=" + uid);
                }
            }

            foreach (int uid in new[] { RibbonCommandIds.CoordPmp, RibbonCommandIds.PreviewPmp, RibbonCommandIds.ExportPmp })
            {
                if (_userToCmdId.TryGetValue(uid, out int cmdId))
                {
                    cmdIds.Add(cmdId);
                    textTypes.Add(textBelow);
                }
            }
            box.AddCommands(cmdIds.ToArray(), textTypes.ToArray());
            logger.Info("Sw2gzRibbonRegistrar: Common tab box added with " + cmdIds.Count +
                " entries (Create + pills + Coord/Preview/Export)");
        }

        private void AddBox(CommandTab tab, int textBelow, int[] userIds)
        {
            var cmdIds = new List<int>(userIds.Length);
            var textTypes = new List<int>(userIds.Length);
            foreach (int userId in userIds)
            {
                if (_userToCmdId.TryGetValue(userId, out int cmdId))
                {
                    cmdIds.Add(cmdId);
                    textTypes.Add(textBelow);
                }
                else
                {
                    logger.Warn("Sw2gzRibbonRegistrar: no cmdId cached for userId=" + userId + " (AddCommandItem2 failed?)");
                }
            }
            if (cmdIds.Count == 0)
            {
                logger.Warn("Sw2gzRibbonRegistrar: AddBox called with no resolvable cmdIds — skipped");
                return;
            }

            ICommandTabBox box = tab.AddCommandTabBox();
            if (box == null)
            {
                logger.Warn("Sw2gzRibbonRegistrar: AddCommandTabBox returned null — cluster skipped");
                return;
            }
            box.AddCommands(cmdIds.ToArray(), textTypes.ToArray());
            logger.Info("Sw2gzRibbonRegistrar: tab box added with " + cmdIds.Count + " commands");
        }

        private static bool ArraysMatch(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            foreach (int x in b)
                if (Array.IndexOf(a, x) < 0) return false;
            return true;
        }
    }
}
#endif
