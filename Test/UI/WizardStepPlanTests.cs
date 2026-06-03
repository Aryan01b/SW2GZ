/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sanity tests for wizard navigation (WizardStepPlan): proves Back/Next/Finish
move as expected and that gz asset/world skip the Links + Joints steps while
Robot Package keeps the full Mode -> Links -> Joints -> Review flow.
*/
using SW2GZ.Ros2;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class WizardStepPlanTests
    {
        // physical indices: 0 Mode, 1 Links, 2 Joints, 3 Review
        private const int Mode = 0, Links = 1, Joints = 2, Review = 3;

        [Fact]
        public void Reachable_RobotPackage_AllFourSteps()
        {
            Assert.Equal(new[] { Mode, Links, Joints, Review },
                WizardStepPlan.Reachable(ExportMode.RobotPackage));
        }

        [Theory]
        [InlineData(ExportMode.SdfModel)]
        [InlineData(ExportMode.SdfWorld)]
        public void Reachable_GzModes_ModeThenReviewOnly(ExportMode mode)
        {
            Assert.Equal(new[] { Mode, Review }, WizardStepPlan.Reachable(mode));
        }

        [Fact]
        public void Next_RobotPackage_WalksEveryStepThenFinish()
        {
            Assert.Equal(Links,  WizardStepPlan.Next(ExportMode.RobotPackage, Mode));
            Assert.Equal(Joints, WizardStepPlan.Next(ExportMode.RobotPackage, Links));
            Assert.Equal(Review, WizardStepPlan.Next(ExportMode.RobotPackage, Joints));
            Assert.Equal(-1,     WizardStepPlan.Next(ExportMode.RobotPackage, Review)); // Finish
        }

        [Fact]
        public void Back_RobotPackage_WalksBackThenStop()
        {
            Assert.Equal(Joints, WizardStepPlan.Back(ExportMode.RobotPackage, Review));
            Assert.Equal(Links,  WizardStepPlan.Back(ExportMode.RobotPackage, Joints));
            Assert.Equal(Mode,   WizardStepPlan.Back(ExportMode.RobotPackage, Links));
            Assert.Equal(-1,     WizardStepPlan.Back(ExportMode.RobotPackage, Mode)); // first
        }

        [Theory]
        [InlineData(ExportMode.SdfModel)]
        [InlineData(ExportMode.SdfWorld)]
        public void Next_GzModes_ModeJumpsStraightToReviewThenFinish(ExportMode mode)
        {
            Assert.Equal(Review, WizardStepPlan.Next(mode, Mode));   // skips Links + Joints
            Assert.Equal(-1,     WizardStepPlan.Next(mode, Review)); // Finish
        }

        [Theory]
        [InlineData(ExportMode.SdfModel)]
        [InlineData(ExportMode.SdfWorld)]
        public void Back_GzModes_ReviewReturnsToModeThenStop(ExportMode mode)
        {
            Assert.Equal(Mode, WizardStepPlan.Back(mode, Review));
            Assert.Equal(-1,   WizardStepPlan.Back(mode, Mode)); // first
        }

        [Theory]
        [InlineData(ExportMode.SdfModel)]
        [InlineData(ExportMode.SdfWorld)]
        public void Snap_GzModes_UnreachableLinksOrJoints_FallsBackToMode(ExportMode mode)
        {
            // A checkpoint saved on Links/Joints under Robot Package, reopened in a
            // gz mode, must not strand the wizard on an unreachable step.
            Assert.Equal(Mode, WizardStepPlan.Snap(mode, Links));
            Assert.Equal(Mode, WizardStepPlan.Snap(mode, Joints));
            Assert.Equal(Review, WizardStepPlan.Snap(mode, Review)); // reachable, unchanged
        }

        [Fact]
        public void Snap_RobotPackage_KeepsReachableStep()
        {
            Assert.Equal(Joints, WizardStepPlan.Snap(ExportMode.RobotPackage, Joints));
        }

        [Fact]
        public void Position_And_Count_RobotPackage()
        {
            Assert.Equal(4, WizardStepPlan.Count(ExportMode.RobotPackage));
            Assert.Equal(0, WizardStepPlan.Position(ExportMode.RobotPackage, Mode));
            Assert.Equal(2, WizardStepPlan.Position(ExportMode.RobotPackage, Joints));
        }

        [Theory]
        [InlineData(ExportMode.SdfModel)]
        [InlineData(ExportMode.SdfWorld)]
        public void Position_And_Count_GzModes(ExportMode mode)
        {
            Assert.Equal(2, WizardStepPlan.Count(mode));
            Assert.Equal(0, WizardStepPlan.Position(mode, Mode));
            Assert.Equal(1, WizardStepPlan.Position(mode, Review));
        }

        [Fact]
        public void IsFirst_IsLast_RobotPackage()
        {
            Assert.True(WizardStepPlan.IsFirst(ExportMode.RobotPackage, Mode));
            Assert.False(WizardStepPlan.IsFirst(ExportMode.RobotPackage, Links));
            Assert.True(WizardStepPlan.IsLast(ExportMode.RobotPackage, Review));
            Assert.False(WizardStepPlan.IsLast(ExportMode.RobotPackage, Joints));
        }

        [Theory]
        [InlineData(ExportMode.SdfModel)]
        [InlineData(ExportMode.SdfWorld)]
        public void IsLast_GzModes_ReviewIsFinish(ExportMode mode)
        {
            Assert.True(WizardStepPlan.IsFirst(mode, Mode));
            Assert.True(WizardStepPlan.IsLast(mode, Review)); // Review = Finish in gz modes
            Assert.False(WizardStepPlan.IsLast(mode, Mode));
        }
    }
}
