/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestLaunchPyWriter : WriterTestBase
    {
        private LaunchPyWriter MakeWriter(GzVersion gz = GzVersion.Harmonic) =>
            new LaunchPyWriter(new LaunchPyWriter.Input
            {
                PackageName = "my_robot_description",
                XacroFileName = "my_robot.urdf.xacro",
                WorldFileName = "empty.sdf",
                Profile = new TargetProfile { Gz = gz },
            });

        [Fact]
        [Trait("Category", "Unit")]
        public void WritesThreeLaunchFiles()
        {
            MakeWriter().Write(TempDir);
            Assert.True(Exists("display.launch.py"));
            Assert.True(Exists("gz_sim.launch.py"));
            Assert.True(Exists("ros2_control.launch.py"));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void DisplayLaunchUsesXacroAndStartsRsp()
        {
            MakeWriter().Write(TempDir);
            var txt = ReadAllText("display.launch.py");
            Assert.Contains("import xacro", txt);
            Assert.Contains("robot_state_publisher", txt);
            Assert.Contains("joint_state_publisher_gui", txt);
            Assert.Contains("rviz2", txt);
            Assert.Contains("my_robot.urdf.xacro", txt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GzSimLaunchUsesRosGzSimForHarmonic()
        {
            MakeWriter(GzVersion.Harmonic).Write(TempDir);
            var txt = ReadAllText("gz_sim.launch.py");
            Assert.Contains("ros_gz_sim", txt);
            Assert.Contains("empty.sdf", txt);
            Assert.Contains("create", txt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GzSimLaunchUsesRosIgnGazeboForFortress()
        {
            MakeWriter(GzVersion.Fortress).Write(TempDir);
            var txt = ReadAllText("gz_sim.launch.py");
            Assert.Contains("ros_ign_gazebo", txt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Ros2ControlLaunchSpawnsBroadcasterAndTrajectoryController()
        {
            MakeWriter().Write(TempDir);
            var txt = ReadAllText("ros2_control.launch.py");
            Assert.Contains("joint_state_broadcaster", txt);
            Assert.Contains("joint_trajectory_controller", txt);
        }
    }
}
