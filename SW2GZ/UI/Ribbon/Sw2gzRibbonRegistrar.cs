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

        // userId -> cmdId, populated as we add each command item. Cached at
        // addition time because walking ICommandGroup.CommandID after the fact
        // is fragile across SDK wrapper versions.
        private readonly Dictionary<int, int> _userToCmdId = new Dictionary<int, int>();

        public Sw2gzRibbonRegistrar(ICommandManager cmdMgr, string[] stripIcons, string[] mainIcons)
        {
            _cmdMgr     = cmdMgr     ?? throw new ArgumentNullException(nameof(cmdMgr));
            _stripIcons = stripIcons ?? throw new ArgumentNullException(nameof(stripIcons));
            _mainIcons  = mainIcons  ?? throw new ArgumentNullException(nameof(mainIcons));
        }

        public void Register()
        {
            const string title = "SW2GZ";
            int errs = 0;

            // Discard SolidWorks' cached CommandManager layout if the registry-
            // stored IDs no longer match what we're about to register.
            bool ignorePrevious = false;
            object registryIDs;
            int[] knownIDs = RibbonCommandIds.AllUserIds;
            bool hadRegistryData = _cmdMgr.GetGroupDataFromRegistry(RibbonCommandIds.CmdGroupId, out registryIDs);
            if (hadRegistryData && !ArraysMatch((int[])registryIDs, knownIDs))
                ignorePrevious = true;

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
            int img = 0;   // strip column — single-glyph fallback until per-mode art lands.

            // Mode pills
            AddItem(grp, RibbonCommandIds.ModeRobot, "Robot",  "Switch to Robot mode",  "ModeRobotClick",  "ModeRobotEnable",  img, toolbar);
            AddItem(grp, RibbonCommandIds.ModeWorld, "World",  "Switch to World mode",  "ModeWorldClick",  "ModeWorldEnable",  img, toolbar);
            AddItem(grp, RibbonCommandIds.ModeAsset, "Asset",  "Switch to Asset mode",  "ModeAssetClick",  "ModeAssetEnable",  img, toolbar);

            // Common cluster
            AddItem(grp, RibbonCommandIds.CoordPmp,   "Coord",   "Coordinate convention (advanced)", "OpenCoordPmp",   "AssemblyEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.PreviewPmp, "Preview", "Browser-based 3D preview",        "OpenPreviewPmp", "AssemblyEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.ExportPmp,  "Export",  "Export ROS 2 / Gz package",       "OpenExportPmp",  "AssemblyEnable", img, toolbar);

            // Robot cluster
            AddItem(grp, RibbonCommandIds.RobotLinks,     "Links",     "Define robot links",     "OpenRobotLinksPmp",     "RobotClusterEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.RobotJoints,    "Joints",    "Define robot joints",    "OpenRobotJointsPmp",    "RobotClusterEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.RobotInertia,   "Inertia",   "Per-link inertia",       "OpenRobotInertiaPmp",   "RobotClusterEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.RobotSensors,   "Sensors",   "Sensor mounts",          "OpenRobotSensorsPmp",   "RobotClusterEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.RobotActuation, "Actuation", "ros2_control / gz",      "OpenRobotActuationPmp", "RobotClusterEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.RobotStack,     "Stack",     "RSP + JSB + bridge",     "OpenRobotStackPmp",     "RobotClusterEnable", img, toolbar);

            // World cluster
            AddItem(grp, RibbonCommandIds.WorldGround,  "Ground",  "Static ground / heightmap", "OpenWorldGroundPmp",  "WorldClusterEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.WorldAssets,  "Assets",  "Non-ground asset includes", "OpenWorldAssetsPmp",  "WorldClusterEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.WorldPhysics, "Physics", "Engine + step + RTF",       "OpenWorldPhysicsPmp", "WorldClusterEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.WorldScene,   "Scene",   "Light + sky + GUI",         "OpenWorldScenePmp",   "WorldClusterEnable", img, toolbar);

            // Asset cluster
            AddItem(grp, RibbonCommandIds.AssetBody,    "Body",    "Single-part body",          "OpenAssetBodyPmp",    "AssetClusterEnable", img, toolbar);
            AddItem(grp, RibbonCommandIds.AssetSurface, "Surface", "Friction / contact",        "OpenAssetSurfacePmp", "AssetClusterEnable", img, toolbar);

            grp.HasToolbar = true;
            grp.HasMenu = false;
            grp.Activate();

            BuildTab(title, grp);
            logger.Info("Sw2gzRibbonRegistrar: registered 18 commands across 4 clusters");
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
            _userToCmdId[userId] = grp.get_CommandID(idx);
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

            int textBelow = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;

            // Common cluster (mode pills + 3 common actions)
            AddBox(tab, textBelow, new[] {
                RibbonCommandIds.ModeRobot, RibbonCommandIds.ModeWorld, RibbonCommandIds.ModeAsset,
                RibbonCommandIds.CoordPmp,  RibbonCommandIds.PreviewPmp, RibbonCommandIds.ExportPmp });

            // Robot cluster
            AddBox(tab, textBelow, new[] {
                RibbonCommandIds.RobotLinks, RibbonCommandIds.RobotJoints, RibbonCommandIds.RobotInertia,
                RibbonCommandIds.RobotSensors, RibbonCommandIds.RobotActuation, RibbonCommandIds.RobotStack });

            // World cluster
            AddBox(tab, textBelow, new[] {
                RibbonCommandIds.WorldGround, RibbonCommandIds.WorldAssets,
                RibbonCommandIds.WorldPhysics, RibbonCommandIds.WorldScene });

            // Asset cluster
            AddBox(tab, textBelow, new[] {
                RibbonCommandIds.AssetBody, RibbonCommandIds.AssetSurface });
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
            if (cmdIds.Count == 0) return;

            ICommandTabBox box = tab.AddCommandTabBox();
            if (box == null) return;
            box.AddCommands(cmdIds.ToArray(), textTypes.ToArray());
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
