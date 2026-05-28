/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestRos2ControlWriter : WriterTestBase
    {
        private Ros2ControlWriter MakeWriter(GzVersion gz = GzVersion.Harmonic) =>
            new Ros2ControlWriter(new Ros2ControlWriter.Input
            {
                JointNames = new List<string> { "joint1", "joint2", "joint3" },
                Profile = new TargetProfile { Gz = gz },
            });

        [Fact]
        [Trait("Category", "Unit")]
        public void WritesXacroIncludeAndControllersYaml()
        {
            MakeWriter().Write(TempDir);
            Assert.True(Exists("inc/ros2_control.xacro"));
            Assert.True(Exists("controllers.yaml"));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void XacroContainsRos2ControlTagWithGzHardwareForHarmonic()
        {
            MakeWriter(GzVersion.Harmonic).Write(TempDir);
            var txt = ReadAllText("inc/ros2_control.xacro");
            Assert.Contains("<ros2_control name=\"GzSystem\" type=\"system\">", txt);
            Assert.Contains("<plugin>gz_ros2_control/GazeboSimSystem</plugin>", txt);
            Assert.Contains("<joint name=\"joint1\">", txt);
            Assert.Contains("<joint name=\"joint3\">", txt);
            Assert.Contains("<command_interface name=\"position\"/>", txt);
            Assert.Contains("<state_interface name=\"position\"/>", txt);
            Assert.Contains("<state_interface name=\"velocity\"/>", txt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void XacroContainsIgnHardwareForFortress()
        {
            MakeWriter(GzVersion.Fortress).Write(TempDir);
            var txt = ReadAllText("inc/ros2_control.xacro");
            Assert.Contains("<plugin>ign_ros2_control/IgnitionSystem</plugin>", txt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void XacroIncludesGzPluginLoadForControllerManager()
        {
            MakeWriter(GzVersion.Harmonic).Write(TempDir);
            var txt = ReadAllText("inc/ros2_control.xacro");
            Assert.Contains("<gazebo>", txt);
            Assert.Contains("filename=\"gz_ros2_control-system\"", txt);
            Assert.Contains("controllers.yaml", txt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ControllersYamlHasBroadcasterAndTrajectoryController()
        {
            MakeWriter().Write(TempDir);
            var yaml = ReadAllText("controllers.yaml");
            Assert.Contains("controller_manager:", yaml);
            Assert.Contains("joint_state_broadcaster:", yaml);
            Assert.Contains("joint_trajectory_controller:", yaml);
            Assert.Contains("- joint1", yaml);
            Assert.Contains("- joint3", yaml);
        }
    }
}
