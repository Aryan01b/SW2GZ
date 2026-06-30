/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Central command-user-ID and command-group-ID constants for the SW2GZ ribbon.
Stable across builds — change only with an intentional version bump (the old
group ID's registry cache would then be discarded as stale).

Layout:
   0..2   Mode flyout sub-items    (Robot / World / Asset — slots reserved as documentation;
                                    chevron sub-items removed in v2.1.0, replaced by pills 4..6)
   3      Mode flyout group        (reserved — was IFlyoutGroup "Create [Mode]"; replaced by 14..16 in v2.1.0)
   4..6   Mode pills               (Robot / World / Asset — TextHorizontal toggles next to
                                    the big Create button; replace the chevron sub-items)
  11..12  Common cluster           (Preview / Export — Coord removed in v2.1.0)
  14..16  Mode-Create trio         (Create Robot / Create World / Create Asset — three pre-registered
                                    AddCommandItem2 buttons; only the active-mode one is placed in the
                                    Common box. SW SDK doesn't allow renaming a command after Activate,
                                    so per-mode labels need per-mode commands.)
  22..25  Robot cluster            (Inertia / Sensors / Actuation / Stack — Links+Joints live in the Create wizard PMP)
  30..33  World cluster            (Ground / Assets / Physics / Scene)
  40..41  Asset cluster            (Body / Surface)
*/
namespace SW2GZ.UI.Ribbon
{
    public static class RibbonCommandIds
    {
        // SolidWorks group ID — matches the value used by the v2.0.0 / v2.1.1
        // ribbon (was sw2gzCmdGroupID = 92 in the deleted SwAddin code). Keeping
        // 92 (a) reuses the registry slot SW already knows about, and
        // (b) avoids the numeric collision with ModeRobot = 0 below. A pristine
        // value like 0 caused AddCommandTab to silently refuse on first install.
        // Bumped 92 → 93 when DeleteConfig (cmd 17) was added — SW's persisted
        // tab-box layout for group 92 only had 18 cmd slots; force a brand-new
        // registry entry so SW renders the full 19-command layout from scratch
        // instead of trying to splice DeleteConfig into the stale cached layout
        // (which silently dropped the whole tab content to just the Create button).
        public const int CmdGroupId = 93;

        // Mode flyout — Robot/World/Asset are the sub-items inside a single
        // "Mode ▾" dropdown button (ModeFlyoutGroup). The sub-item IDs (0..2)
        // are kept stable so the click callbacks in SwAddin stay wired by name.
        public const int ModeRobot       = 0;
        public const int ModeWorld       = 1;
        public const int ModeAsset       = 2;
        // ID 99 — deliberately well away from SW built-in command IDs in the
        // low single-digit range. Using ID=3 caused the flyout button face to
        // show SW's "Form New Subassembly" tooltip (cmdID collision in SW's
        // internal command-name table).
        public const int ModeFlyoutGroup = 99;

        // Mode pills — three small TextHorizontal toggles next to the big
        // Create button. Replace the chevron-style sub-items (slots 0..2 in
        // the flyout's own ID space, kept reserved as documentation). Click
        // reuses the existing ModeRobotClick / WorldClick / AssetClick path.
        public const int ModeRobotPill   = 4;
        public const int ModeWorldPill   = 5;
        public const int ModeAssetPill   = 6;

        // Common cluster — Coord (was 10) removed in v2.1.0; advanced coord
        // convention now lives in the Create wizard. ID slot kept as a gap so
        // PreviewPmp / ExportPmp don't have to renumber.
        public const int PreviewPmp  = 11;
        public const int ExportPmp   = 12;
        // Slot 17 (DeleteConfig) reserved as a documentation gap — the user
        // deletes the "SW2GZ Doc (v1)" attribute from the FeatureManager tree
        // directly (right-click → Delete) instead of via a ribbon command.

        // Mode-Create trio — three pre-registered Create buttons (one per
        // mode), each with its own static "Create Robot" / "Create World" /
        // "Create Asset" label. Only the one matching the active mode is
        // placed in the Common tab box; mode switch swaps the box, not the
        // command name (SW SDK can't rename a command post-Activate).
        // Slot 13 = old generic ModeCreate, kept as a gap to avoid renumbering.
        public const int ModeCreateRobot = 14;
        public const int ModeCreateWorld = 15;
        public const int ModeCreateAsset = 16;

        // Edit-variant Create buttons — shown in place of the Create button when
        // a doc is already saved for this assembly. SW SDK can't rename a command
        // post-Activate, so the "Create X" ↔ "Edit X" swap needs distinct
        // commands selected by saved-state in BuildModeStartBox.
        public const int ModeEditRobot = 17;
        public const int ModeEditWorld = 18;
        public const int ModeEditAsset = 19;

        // Robot cluster (22..25: Inertia/Sensors/Actuation/Stack) removed for the
        // v2 rebuild — robot mode is a stub with no cluster buttons. IDs 22..25
        // left as gaps so World/Asset ids keep their stable values.

        // World cluster
        public const int WorldGround     = 30;
        public const int WorldAssets     = 31;
        public const int WorldPhysics    = 32;
        public const int WorldScene      = 33;

        // Asset cluster
        public const int AssetBody       = 40;
        public const int AssetSurface    = 41;

        // Note: ModeFlyoutGroup is intentionally NOT in this list because the
        // flyout-group ID is allocated by ICommandManager.CreateFlyoutGroup
        // (separate registry namespace from AddCommandItem2 user-IDs). The
        // Robot/World/Asset IDs are included because they're real
        // sub-command items inside the flyout. ModeCreate{Robot,World,Asset}
        // are regular AddCommandItem2 commands, not a flyout group.
        public static readonly int[] AllUserIds = new[]
        {
            ModeRobot, ModeWorld, ModeAsset,
            ModeRobotPill, ModeWorldPill, ModeAssetPill,
            ModeCreateRobot, ModeCreateWorld, ModeCreateAsset,
            ModeEditRobot, ModeEditWorld, ModeEditAsset,
            PreviewPmp, ExportPmp,
            WorldGround, WorldAssets, WorldPhysics, WorldScene,
            AssetBody, AssetSurface,
        };
    }
}
