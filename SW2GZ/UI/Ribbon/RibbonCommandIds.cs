/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Central command-user-ID and command-group-ID constants for the SW2GZ ribbon.
Stable across builds — change only with an intentional version bump (the old
group ID's registry cache would then be discarded as stale).

Layout:
   0..2   Mode toggle pills        (Robot / World / Asset)
  10..12  Common cluster           (Coord / Preview / Export)
  20..25  Robot cluster            (Links / Joints / Inertia / Sensors / Actuation / Stack)
  30..33  World cluster            (Ground / Assets / Physics / Scene)
  40..41  Asset cluster            (Body / Surface)
*/
namespace SW2GZ.UI.Ribbon
{
    public static class RibbonCommandIds
    {
        public const int CmdGroupId = 0;   // unchanged from v2.1.1 so registry stays warm

        // Mode pills
        public const int ModeRobot   = 0;
        public const int ModeWorld   = 1;
        public const int ModeAsset   = 2;

        // Common cluster
        public const int CoordPmp    = 10;
        public const int PreviewPmp  = 11;
        public const int ExportPmp   = 12;

        // Robot cluster
        public const int RobotLinks      = 20;
        public const int RobotJoints     = 21;
        public const int RobotInertia    = 22;
        public const int RobotSensors    = 23;
        public const int RobotActuation  = 24;
        public const int RobotStack      = 25;

        // World cluster
        public const int WorldGround     = 30;
        public const int WorldAssets     = 31;
        public const int WorldPhysics    = 32;
        public const int WorldScene      = 33;

        // Asset cluster
        public const int AssetBody       = 40;
        public const int AssetSurface    = 41;

        public static int[] AllUserIds = new[]
        {
            ModeRobot, ModeWorld, ModeAsset,
            CoordPmp, PreviewPmp, ExportPmp,
            RobotLinks, RobotJoints, RobotInertia, RobotSensors, RobotActuation, RobotStack,
            WorldGround, WorldAssets, WorldPhysics, WorldScene,
            AssetBody, AssetSurface,
        };
    }
}
