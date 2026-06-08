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
            // 4 Coord (unused, removed v2.1.0) | 5 Preview | 6 Export |
            // 7 Links | 8 Joints | 9 Inertia | 10 Sensors | 11 Actuation | 12 Stack |
            // 13 Ground | 14 Assets | 15 Physics | 16 Scene |
            // 17 Body | 18 Surface
            const int IMG_PREVIEW = 5, IMG_EXPORT = 6;
            // IMG_LINKS=7, IMG_JOINTS=8 — Links/Joints moved into the Create-Robot
            // wizard PMP; the strip columns are kept (still drawn) so we don't
            // have to renumber later columns.
            const int IMG_INERTIA = 9;
            const int IMG_SENSORS = 10, IMG_ACTUATION = 11, IMG_STACK = 12;
            const int IMG_GROUND = 13, IMG_ASSETS = 14, IMG_PHYSICS = 15, IMG_SCENE = 16;
            const int IMG_BODY = 17, IMG_SURFACE = 18;

            // Mode-Create trio — three pre-registered Create buttons, each
            // with its own static label. Only the active-mode one is placed
            // in the Common box; mode switch swaps the box (see
            // BuildCommonTabBox / RefreshTabForMode). All three share the
            // OpenCreatePmp callback, which already branches on doc.Mode.
            // Icon column 0 (mode-trio glyph) used for all three so the
            // big-button face stays visually consistent across modes.
            const int IMG_CREATE = 0;
            AddItem(grp, RibbonCommandIds.ModeCreateRobot, "Create Robot",
                    "Create / edit the robot package for this assembly.",
                    "OpenCreatePmp", "AssemblyEnable", IMG_CREATE, toolbar);
            AddItem(grp, RibbonCommandIds.ModeCreateWorld, "Create World",
                    "Create / edit the Gz world for this assembly.",
                    "OpenCreatePmp", "AssemblyEnable", IMG_CREATE, toolbar);
            AddItem(grp, RibbonCommandIds.ModeCreateAsset, "Create Asset",
                    "Create / edit a reusable asset from this assembly.",
                    "OpenCreatePmp", "AssemblyEnable", IMG_CREATE, toolbar);

            // Common cluster — Coord button removed in v2.1.0 (advanced coord
            // convention moved into the Create wizard).
            AddItem(grp, RibbonCommandIds.PreviewPmp, "Preview", "Browser-based 3D preview",  "OpenPreviewPmp", "PreviewEnable",  IMG_PREVIEW, toolbar);
            AddItem(grp, RibbonCommandIds.ExportPmp,  "Export",  "Export ROS 2 / Gz package", "OpenExportPmp",  "ExportEnable",   IMG_EXPORT,  toolbar);

            // Robot cluster — Links/Joints moved into the Create-Robot wizard PMP.
            // Inertia + Stack removed from the cluster: inertia is computed
            // automatically per-link by InertialAggregator, and the launch /
            // RSP / bridge wiring is generated by Sw2gzPipeline (StackProfile
            // defaults). The orphaned RibbonCommandIds + Open*Pmp callbacks
            // are left in place for now in case they're re-introduced later.
            AddItem(grp, RibbonCommandIds.RobotSensors,   "Sensors",   "Sensor mounts",          "OpenRobotSensorsPmp",   "RobotClusterEnable", IMG_SENSORS,   toolbar);
            AddItem(grp, RibbonCommandIds.RobotActuation, "Actuation", "ros2_control / gz",      "OpenRobotActuationPmp", "RobotClusterEnable", IMG_ACTUATION, toolbar);

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

            BuildTab(title);
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
            RibbonCommandIds.RobotSensors, RibbonCommandIds.RobotActuation };
        private static readonly int[] WorldClusterUserIds = new[] {
            RibbonCommandIds.WorldGround, RibbonCommandIds.WorldAssets,
            RibbonCommandIds.WorldPhysics, RibbonCommandIds.WorldScene };
        private static readonly int[] AssetClusterUserIds = new[] {
            RibbonCommandIds.AssetBody, RibbonCommandIds.AssetSurface };

        // Active mode used by the next BuildTab call. Refresh sets this then
        // calls BuildTab; the initial Register call also drives it from the
        // Sw2gzDoc state in SwAddin (see RefreshTabForMode below).
        private SW2GZ.URDFExport.Sw2gzMode _activeMode = SW2GZ.URDFExport.Sw2gzMode.Robot;

        // Cached refs so RefreshTabForMode can swap just the two boxes instead
        // of tearing down the whole tab. RemoveCommandTab + AddCommandTab
        // forces the SW2GZ tab to become active — which yanked the user off
        // whatever tab they were on (Assembly / Layout / etc.) every time a
        // mode pill was clicked. Per-box swap keeps the tab itself untouched
        // so the user's active-tab focus is preserved.
        // Three managed boxes on the tab, rendered left-to-right with SW's
        // built-in gap between adjacent CommandTabBox instances (SW's API has
        // no per-button separator — multiple boxes IS the separator idiom):
        //   _modeStartBox    : [Create <Mode>] [Robot/World/Asset pills]
        //   _actionsBox      : [Preview] [Export]
        //   _modeClusterBox  : the active mode's panel-cluster buttons
        private CommandTab _tab;
        private CommandTabBox _modeStartBox;
        private CommandTabBox _actionsBox;
        private CommandTabBox _modeClusterBox;

        public void RefreshTabForMode(SW2GZ.URDFExport.Sw2gzMode mode)
        {
            _activeMode = mode;

            int asmType = (int)swDocumentTypes_e.swDocASSEMBLY;
            // Resolve a live tab handle each call — _tab may be stale across
            // a SW Disconnect/Reconnect cycle.
            CommandTab live = _cmdMgr.GetCommandTab(asmType, "SW2GZ");
            if (live == null)
            {
                // No tab yet — first-time build path (or recovery after a
                // missed initial registration). Falls through to BuildTab.
                BuildTab("SW2GZ");
                return;
            }
            _tab = live;

            int textBelow = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

            // Surgical swap: drop the managed boxes and rebuild them in
            // place. The CommandTab itself stays — so the ribbon's currently
            // active tab (whether SW2GZ or another) doesn't change. Only the
            // mode-start box needs rebuilding to swap the Create label, but
            // we rebuild all three to keep ordering deterministic (SW appends
            // new boxes after existing ones, so partial rebuild would
            // reshuffle the layout left-to-right).
            if (_modeStartBox   != null) { try { _tab.RemoveCommandTabBox(_modeStartBox);   } catch { } _modeStartBox = null; }
            if (_actionsBox     != null) { try { _tab.RemoveCommandTabBox(_actionsBox);     } catch { } _actionsBox = null; }
            if (_modeClusterBox != null) { try { _tab.RemoveCommandTabBox(_modeClusterBox); } catch { } _modeClusterBox = null; }

            BuildAllBoxes(_tab, textBelow);
            logger.Info("Sw2gzRibbonRegistrar: RefreshTabForMode swapped boxes for mode=" + _activeMode +
                " (tab focus preserved)");
        }

        private void BuildTab(string title)
        {
            int asmType = (int)swDocumentTypes_e.swDocASSEMBLY;

            // Drop any existing tab from a previous load BEFORE re-adding,
            // otherwise AddCommandTabBox stacks duplicate boxes on top of the
            // persisted one each session. This full-rebuild path is for
            // initial registration only — RefreshTabForMode takes the
            // box-swap path to avoid the tab-focus side effect.
            CommandTab existing = _cmdMgr.GetCommandTab(asmType, title);
            if (existing != null) _cmdMgr.RemoveCommandTab(existing);

            _tab = _cmdMgr.AddCommandTab(asmType, title);
            if (_tab == null)
            {
                logger.Warn("Sw2gzRibbonRegistrar: AddCommandTab returned null — toolbar buttons still available");
                return;
            }
            logger.Info("Sw2gzRibbonRegistrar: tab '" + title + "' added for swDocASSEMBLY (mode=" + _activeMode + ")");

            int textBelow = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;
            BuildAllBoxes(_tab, textBelow);
        }

        private void BuildAllBoxes(CommandTab tab, int textBelow)
        {
            _modeStartBox   = BuildModeStartBox(tab, textBelow);
            _actionsBox     = BuildActionsBox(tab, textBelow);
            _modeClusterBox = BuildModeClusterBox(tab, textBelow);
        }

        // Box 1 — mode-start: [Create <Mode>] [Robot/World/Asset pills].
        // The Create button is the mode-specific variant (one of three
        // pre-registered commands); the SW SDK can't rename a command after
        // Activate, so per-mode labels need per-mode commands swapped in here.
        private CommandTabBox BuildModeStartBox(CommandTab tab, int textBelow)
        {
            ICommandTabBox box = tab.AddCommandTabBox();
            if (box == null)
            {
                logger.Warn("Sw2gzRibbonRegistrar: mode-start tab box AddCommandTabBox returned null");
                return null;
            }
            var cmdIds = new List<int>(4);
            var textTypes = new List<int>(4);

            int createUserId;
            switch (_activeMode)
            {
                case SW2GZ.URDFExport.Sw2gzMode.World: createUserId = RibbonCommandIds.ModeCreateWorld; break;
                case SW2GZ.URDFExport.Sw2gzMode.Asset: createUserId = RibbonCommandIds.ModeCreateAsset; break;
                default:                               createUserId = RibbonCommandIds.ModeCreateRobot; break;
            }
            if (_userToCmdId.TryGetValue(createUserId, out int createCmdId))
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
            box.AddCommands(cmdIds.ToArray(), textTypes.ToArray());
            logger.Info("Sw2gzRibbonRegistrar: mode-start tab box added with " + cmdIds.Count +
                " entries (Create[" + _activeMode + "] + pills)");
            return (CommandTabBox)box;
        }

        // Box 2 — actions: [Preview] [Export]. Rendered with the SW-default
        // gap between this box and the mode-start box, which serves as the
        // group separator.
        private CommandTabBox BuildActionsBox(CommandTab tab, int textBelow)
        {
            return AddBox(tab, textBelow, new[] {
                RibbonCommandIds.PreviewPmp,
                RibbonCommandIds.ExportPmp,
            });
        }

        // Box 3 — mode-cluster: the active mode's panel-cluster buttons.
        // Only one cluster goes on the tab at a time — L3b "one mode at a
        // time" (others are hidden, not just grayed).
        private CommandTabBox BuildModeClusterBox(CommandTab tab, int textBelow)
        {
            int[] activeIds;
            string activeName;
            switch (_activeMode)
            {
                case SW2GZ.URDFExport.Sw2gzMode.World: activeIds = WorldClusterUserIds; activeName = "World"; break;
                case SW2GZ.URDFExport.Sw2gzMode.Asset: activeIds = AssetClusterUserIds; activeName = "Asset"; break;
                default:                               activeIds = RobotClusterUserIds; activeName = "Robot"; break;
            }
            var result = AddBox(tab, textBelow, activeIds);
            if (result != null)
                logger.Info("Sw2gzRibbonRegistrar: rendered " + activeName + " panel cluster only (L3b hide-others)");
            return result;
        }

        private CommandTabBox AddBox(CommandTab tab, int textBelow, int[] userIds)
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
                return null;
            }

            ICommandTabBox box = tab.AddCommandTabBox();
            if (box == null)
            {
                logger.Warn("Sw2gzRibbonRegistrar: AddCommandTabBox returned null — cluster skipped");
                return null;
            }
            box.AddCommands(cmdIds.ToArray(), textTypes.ToArray());
            logger.Info("Sw2gzRibbonRegistrar: tab box added with " + cmdIds.Count + " commands");
            return (CommandTabBox)box;
        }
    }
}
#endif
