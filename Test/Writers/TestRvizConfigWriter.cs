/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestRvizConfigWriter : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void WritesRvizFileContainingRobotModelAndTfDisplays()
        {
            new RvizConfigWriter().Write(TempDir, "rviz.rviz");
            Assert.True(Exists("rviz.rviz"));
            var txt = ReadAllText("rviz.rviz");
            Assert.Contains("Class: rviz_default_plugins/RobotModel", txt);
            Assert.Contains("Class: rviz_default_plugins/TF", txt);
            Assert.Contains("Fixed Frame: base_link", txt);
        }
    }
}
