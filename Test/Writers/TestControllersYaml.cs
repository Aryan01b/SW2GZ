/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestControllersYaml
    {
        [Fact]
        public void Write_EmitsControllerManagerSection()
        {
            var yaml = ControllersYaml.Write(new ControllersInput("arm_2dof", new[] { "j1", "j2" }));
            Assert.Contains("controller_manager:", yaml);
            Assert.Contains("ros__parameters:", yaml);
            Assert.Contains("update_rate:", yaml);
        }

        [Fact]
        public void Write_DeclaresJointStateBroadcaster()
        {
            var yaml = ControllersYaml.Write(new ControllersInput("arm_2dof", new[] { "j1" }));
            Assert.Contains("joint_state_broadcaster:", yaml);
            Assert.Contains("joint_state_broadcaster/JointStateBroadcaster", yaml);
        }

        [Fact]
        public void Write_DeclaresJointTrajectoryController()
        {
            var yaml = ControllersYaml.Write(new ControllersInput("arm_2dof", new[] { "j1" }));
            Assert.Contains("joint_trajectory_controller:", yaml);
            Assert.Contains("joint_trajectory_controller/JointTrajectoryController", yaml);
        }

        [Fact]
        public void Write_ListsJointsInTrajectoryControllerParams()
        {
            var yaml = ControllersYaml.Write(new ControllersInput("arm_2dof", new[] { "j1", "j2", "j3" }));
            Assert.Contains("- j1", yaml);
            Assert.Contains("- j2", yaml);
            Assert.Contains("- j3", yaml);
        }

        [Fact]
        public void Write_EmitsCommandAndStateInterfaces()
        {
            var yaml = ControllersYaml.Write(new ControllersInput("arm_2dof", new[] { "j1" }));
            Assert.Contains("command_interfaces:", yaml);
            Assert.Contains("- position", yaml);
            Assert.Contains("state_interfaces:", yaml);
            // state interfaces: position + velocity (matches T11 Ros2ControlWriter & ros2_control xacro)
            Assert.Contains("- position", yaml);
            Assert.Contains("- velocity", yaml);
        }

        [Fact]
        public void Write_NoJoints_StillProducesControllerManagerSkeleton()
        {
            var yaml = ControllersYaml.Write(new ControllersInput("arm_2dof", new string[0]));
            Assert.Contains("controller_manager:", yaml);
            Assert.Contains("joint_trajectory_controller:", yaml);
        }

        [Fact]
        public void Write_NullInput_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => ControllersYaml.Write(null));
        }

        [Fact]
        public void Write_NullPackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                ControllersYaml.Write(new ControllersInput(null, new[] { "j1" })));
        }

        [Fact]
        public void Write_NullJoints_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                ControllersYaml.Write(new ControllersInput("arm_2dof", null)));
        }
    }
}
