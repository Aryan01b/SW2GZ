/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestReadmeWriter : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void WritesReadmeWithBuildAndLaunchCommands()
        {
            new ReadmeWriter("my_robot_description", new TargetProfile { Ros2 = Ros2Distro.Jazzy, Gz = GzVersion.Harmonic }).Write(TempDir);
            Assert.True(Exists("README.md"));
            var txt = ReadAllText("README.md");
            Assert.Contains("# my_robot_description", txt);
            Assert.Contains("ros2 launch my_robot_description gz_sim.launch.py", txt);
            Assert.Contains("colcon build --packages-select my_robot_description", txt);
            Assert.Contains("Jazzy", txt);
            Assert.Contains("Harmonic", txt);
        }
    }
}
