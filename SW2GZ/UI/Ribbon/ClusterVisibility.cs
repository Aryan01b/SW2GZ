/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure truth-table: given the current Sw2gzMode, which ribbon clusters are visible?
Common is always visible (Mode toggle / Preview / Export are mode-agnostic).
Robot/World/Asset clusters are mutually exclusive — only the one matching Mode
appears.

Used by SwAddin's per-cluster OnEnableCallback returning 1 (visible) or 0
(hidden).

Also hosts the Robot-subtree readiness predicate consumed by the Preview ribbon
button's enable callback — gates Preview until the user has actually assigned
components to a link and auto-detected at least one joint.
*/
using System.Linq;
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

        /// True iff the Robot subtree has enough content for Preview to render
        /// something meaningful: at least one LinkDef with components assigned
        /// AND at least one JointDef whose auto-detect succeeded (HasOrigin).
        /// Pure / null-safe so the SW addin's command-enable callback can call
        /// it on every UI tick without throwing.
        public static bool IsRobotReady(Sw2gzRobotConfig robot)
        {
            if (robot == null) return false;
            bool hasLink = robot.Links != null
                && robot.Links.Any(l => l.ComponentIds != null && l.ComponentIds.Count > 0);
            bool hasJoint = robot.Joints != null
                && robot.Joints.Any(j => j.HasOrigin);
            return hasLink && hasJoint;
        }
    }
}
