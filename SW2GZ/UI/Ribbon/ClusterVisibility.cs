/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure truth-table: given the current Sw2gzMode, which ribbon clusters are visible?
Common is always visible (Mode toggle / Preview / Export are mode-agnostic).
Robot/World/Asset clusters are mutually exclusive — only the one matching Mode
appears.

Used by SwAddin's per-cluster OnEnableCallback returning 1 (visible) or 0
(hidden).
*/
using SW2GZ.URDFExport;

namespace SW2GZ.UI.Ribbon
{
    public enum RibbonCluster { Common, Robot, World, Asset }

    public static class ClusterVisibility
    {
        public static bool IsVisible(Sw2gzMode mode, RibbonCluster cluster)
        {
            if (cluster == RibbonCluster.Common) return true;
            switch (cluster)
            {
                case RibbonCluster.Robot: return mode == Sw2gzMode.Robot;
                case RibbonCluster.World: return mode == Sw2gzMode.World;
                case RibbonCluster.Asset: return mode == Sw2gzMode.Asset;
                default:                  return false;
            }
        }
    }
}
