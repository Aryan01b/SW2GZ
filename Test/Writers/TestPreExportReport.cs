/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestPreExportReport
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void ReportsLinkAndJointCountsAndMass()
        {
            var r = PreExportReport.Generate("my_robot", linkCount: 3, jointCount: 2, totalMassKg: 1.25);
            Assert.Equal("my_robot", r.RobotName);
            Assert.Equal(3, r.LinkCount);
            Assert.Equal(2, r.JointCount);
            Assert.Equal(1.25, r.TotalMassKg);
            Assert.Empty(r.Warnings);
            Assert.Contains("3 link(s), 2 joint(s)", r.Summary);
            Assert.Contains("1.250", r.Summary);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WarnsOnEmptyRobot()
        {
            var r = PreExportReport.Generate("empty", linkCount: 0, jointCount: 0, totalMassKg: 0);
            Assert.Contains("Robot has no links.", r.Warnings);
            Assert.Contains("Total mass is zero or negative.", r.Warnings);
            Assert.Contains("Warnings:", r.Summary);
        }
    }
}
