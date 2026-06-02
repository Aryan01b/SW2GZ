/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — Review step: summary counts derive from the earlier steps, Finish
delegates to IExportRunner + stores LastResult, and CanExport gates on a
ready model + assigned geometry + valid output.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.Ros2;
using SW2GZ.UI.Services;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class ReviewStepViewModelTests
    {
        private static RobotModel MinimalModel()
        {
            var mesh = new MeshData(new Vector3[0], new int[0], null);
            var link = new UrdfLink("base_link", 1.0, Vector3.Zero, Matrix3.Identity,
                mesh, mesh, "base_link.dae", "base_link.stl");
            var meta = new RobotMeta("three_dof_arm", "a", "a@b.c", "MIT",
                CoordinateConvention.Identity);
            return RobotModelBuilder.Build(meta, new[] { link }, new UrdfJoint[0]);
        }

        // Build the four upstream steps in a "ready" state (one assigned link,
        // valid output), then a Review step over them.
        private static (ReviewStepViewModel Review, FakeExportRunner Runner) BuildReady(
            ExportResult result, RobotModel preview)
        {
            var mode = new ModeStepViewModel { SelectedMode = ExportMode.RobotPackage };
            var targets = new TargetsStepViewModel { SelectedDistro = Ros2Distro.Jazzy };
            var output = new OutputStepViewModel(new NullFolderBrowserService())
            {
                OutputFolder = @"D:\ros2_ws\src",
                PackageName = "three_dof_arm",
            };
            var selection = new FakeViewportSelectionService("body_a");
            var links = new LinksStepViewModel(
                new List<LinkDto> { new LinkDto("base_link", 1.42, "base_link.dae") }, selection);
            links.Links[0].AssignGeometryCommand.Execute(null);

            var runner = new FakeExportRunner(result);
            var review = new ReviewStepViewModel(mode, targets, output, links, runner,
                jointCount: 3, previewModel: preview);
            return (review, runner);
        }

        [Fact]
        public void SummaryCountsAreCorrect()
        {
            var (review, _) = BuildReady(new ExportResult(true, 0, new string[0]), MinimalModel());

            Assert.Equal("Robot Package", review.ModeSummary);
            Assert.Equal("Jazzy + Harmonic", review.TargetSummary);
            Assert.Equal("three_dof_arm", review.PackageName);
            Assert.Equal(1, review.LinkCount);
            Assert.Equal(3, review.JointCount);
            Assert.Equal(1, review.AssignedGeometryCount);
            Assert.Equal(0, review.ValidationErrorCount);
        }

        [Fact]
        public void FinishExportCallsRunnerAndStoresResult()
        {
            var result = new ExportResult(true, 0, new[] { "ok" });
            RobotModel model = MinimalModel();
            var (review, runner) = BuildReady(result, model);

            Assert.True(review.CanExport);
            review.FinishExportCommand.Execute(null);

            Assert.Equal(1, runner.CallCount);
            Assert.Same(model, runner.LastModel);
            Assert.Equal(@"D:\ros2_ws\src", runner.LastOutputDir);
            Assert.Equal(ExportMode.RobotPackage, runner.LastMode);
            Assert.Same(result, review.LastResult);
            Assert.True(review.IsComplete);
        }

        [Fact]
        public void ValidationErrorCountReflectsResult()
        {
            var result = new ExportResult(false, 2, new[] { "e1", "e2" });
            var (review, _) = BuildReady(result, MinimalModel());

            review.FinishExportCommand.Execute(null);

            Assert.Equal(2, review.ValidationErrorCount);
            Assert.False(review.IsComplete); // export failed
        }

        [Fact]
        public void CannotExportWithoutPreviewModel()
        {
            var (review, runner) = BuildReady(new ExportResult(true, 0, new string[0]), null);

            Assert.False(review.CanExport);
            review.FinishExportCommand.Execute(null);
            Assert.Equal(0, runner.CallCount); // guarded
        }

        [Fact]
        public void SettingPreviewModelEnablesExport()
        {
            var (review, _) = BuildReady(new ExportResult(true, 0, new string[0]), null);
            Assert.False(review.CanExport);

            review.PreviewModel = MinimalModel();
            Assert.True(review.CanExport);
        }
    }
}
