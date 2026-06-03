/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure, SolidWorks-free wizard step navigation. Maps the export mode to the
ordered set of reachable physical step indices and computes Back / Next /
Finish transitions. Lives apart from the COM PropertyManagerPage shell
(Sw2gzExportPmp) so the navigation rules are unit-testable without SolidWorks.

Physical steps (match Sw2gzExportPmp BuildPage order + StepNames):
  0 Mode · 1 Links ("Create base model structure") · 2 Joints · 3 Review

Robot Package walks all four. gz asset/world are static visual models that
need neither a manual link tree nor joints (links auto-seed from the
assembly), so they go straight Mode -> Review.
*/
using System;
using SW2GZ.Ros2;

namespace SW2GZ.UI.ViewModels
{
    public static class WizardStepPlan
    {
        public const int StepMode   = 0;
        public const int StepLinks  = 1;
        public const int StepJoints = 2;
        public const int StepReview = 3;

        // Ordered reachable physical step indices for the mode.
        public static int[] Reachable(ExportMode mode) =>
            mode == ExportMode.RobotPackage
                ? new[] { StepMode, StepLinks, StepJoints, StepReview }
                : new[] { StepMode, StepReview };

        // Snap an arbitrary step (e.g. a persisted checkpoint saved under one mode,
        // reopened under another) to a reachable step. Unreachable -> first reachable.
        public static int Snap(ExportMode mode, int step)
        {
            int[] r = Reachable(mode);
            return Array.IndexOf(r, step) >= 0 ? step : r[0];
        }

        // 0-based position of step within the reachable set (after snapping).
        public static int Position(ExportMode mode, int step)
        {
            int[] r = Reachable(mode);
            int pos = Array.IndexOf(r, Snap(mode, step));
            return pos < 0 ? 0 : pos;
        }

        // Number of reachable steps for the mode.
        public static int Count(ExportMode mode) => Reachable(mode).Length;

        public static bool IsFirst(ExportMode mode, int step) => Position(mode, step) == 0;

        public static bool IsLast(ExportMode mode, int step) => Position(mode, step) == Count(mode) - 1;

        // Next reachable step after step, or -1 if step is the last (-> Finish).
        public static int Next(ExportMode mode, int step)
        {
            int[] r = Reachable(mode);
            int pos = Position(mode, step);
            return pos < r.Length - 1 ? r[pos + 1] : -1;
        }

        // Previous reachable step before step, or -1 if step is the first.
        public static int Back(ExportMode mode, int step)
        {
            int pos = Position(mode, step);
            return pos > 0 ? Reachable(mode)[pos - 1] : -1;
        }
    }
}
