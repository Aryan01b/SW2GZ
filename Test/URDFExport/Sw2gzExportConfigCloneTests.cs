/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Tests for `Sw2gzExportConfig.WithEmitWorldLink` — the shallow clone
helper used by `Sw2gzModelPreviewer.RunPreview` to bake the SW→ROS
rotation into the preview's temp-workspace URDF without mutating the
user's saved config. Real exports must continue to honour the user's
own `EmitWorldLink` choice; the preview override only affects the
copy of the URDF that PreviewServer serves to the browser.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Test.URDFExport
{
    public class Sw2gzExportConfigCloneTests
    {
        private static Sw2gzExportConfig BuildSample()
        {
            return new Sw2gzExportConfig
            {
                Mode          = ExportMode.RobotPackage,
                SwUpAxis      = AxisDirection.PlusY,
                SwForwardAxis = AxisDirection.PlusZ,
                EmitWorldLink = false,
                OutputFolder  = @"C:\some\path",
                PackageName   = "full_arm",
                Author        = "Test User",
                Email         = "test@example.com",
                License       = "MIT",
                LastStep      = 3,
            };
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WithEmitWorldLink_True_FlipsFlag_PreservesAllOtherFields()
        {
            var src = BuildSample();
            var clone = src.WithEmitWorldLink(true);

            Assert.True(clone.EmitWorldLink);

            // All other public fields preserved exactly.
            Assert.Equal(src.Mode,          clone.Mode);
            Assert.Equal(src.SwUpAxis,      clone.SwUpAxis);
            Assert.Equal(src.SwForwardAxis, clone.SwForwardAxis);
            Assert.Equal(src.OutputFolder,  clone.OutputFolder);
            Assert.Equal(src.PackageName,   clone.PackageName);
            Assert.Equal(src.Author,        clone.Author);
            Assert.Equal(src.Email,         clone.Email);
            Assert.Equal(src.License,       clone.License);
            Assert.Equal(src.LastStep,      clone.LastStep);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WithEmitWorldLink_False_PreservesOriginalFalse()
        {
            var src = BuildSample();
            Assert.False(src.EmitWorldLink);

            var clone = src.WithEmitWorldLink(false);
            Assert.False(clone.EmitWorldLink);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WithEmitWorldLink_DoesNotMutateOriginal()
        {
            var src = BuildSample();
            Assert.False(src.EmitWorldLink);

            _ = src.WithEmitWorldLink(true);
            Assert.False(src.EmitWorldLink);   // source untouched

            _ = src.WithEmitWorldLink(false);
            Assert.False(src.EmitWorldLink);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WithEmitWorldLink_ReturnsNewInstance()
        {
            var src = BuildSample();
            var clone = src.WithEmitWorldLink(true);
            Assert.NotSame(src, clone);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WithEmitWorldLink_TogglingTwice_RoundTrips()
        {
            var src = BuildSample();
            var a = src.WithEmitWorldLink(true);
            var b = a.WithEmitWorldLink(false);
            Assert.False(b.EmitWorldLink);
            Assert.Equal(src.PackageName, b.PackageName);
            Assert.Equal(src.SwUpAxis,    b.SwUpAxis);
        }
    }
}
