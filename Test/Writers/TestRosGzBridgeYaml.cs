/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Gz;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestRosGzBridgeYaml
    {
        [Fact]
        public void Write_UsesPackageNameInJointStateTopic_Bug6()
        {
            var yaml = RosGzBridgeYaml.Write("my_pkg");
            Assert.Contains("/world/empty/model/my_pkg/joint_state", yaml);
        }

        [Fact]
        public void Write_DifferentPackageNameFlowsThrough()
        {
            var yaml = RosGzBridgeYaml.Write("arm_2dof_description");
            Assert.Contains("/world/empty/model/arm_2dof_description/joint_state", yaml);
            Assert.DoesNotContain("/world/empty/model/robot/", yaml);
        }

        [Fact]
        public void Write_BridgesClock()
        {
            var yaml = RosGzBridgeYaml.Write("my_pkg");
            Assert.Contains("/clock", yaml);
            Assert.Contains("rosgraph_msgs/msg/Clock", yaml);
            Assert.Contains("gz.msgs.Clock", yaml);
        }

        [Fact]
        public void Write_BridgesJointStates()
        {
            var yaml = RosGzBridgeYaml.Write("my_pkg");
            Assert.Contains("/joint_states", yaml);
            Assert.Contains("sensor_msgs/msg/JointState", yaml);
        }

        [Fact]
        public void Write_BridgesTf()
        {
            var yaml = RosGzBridgeYaml.Write("my_pkg");
            Assert.Contains("/tf", yaml);
            Assert.Contains("tf2_msgs/msg/TFMessage", yaml);
            Assert.Contains("gz.msgs.Pose_V", yaml);
        }

        [Fact]
        public void Write_DirectionsGzToRos()
        {
            var yaml = RosGzBridgeYaml.Write("my_pkg");
            Assert.Contains("direction: GZ_TO_ROS", yaml);
        }

        [Fact]
        public void Write_StartsWithCommentHeader()
        {
            var yaml = RosGzBridgeYaml.Write("my_pkg");
            Assert.StartsWith("# ", yaml.TrimStart());
        }

        [Fact]
        public void Write_NullPackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => RosGzBridgeYaml.Write(null));
        }

        [Fact]
        public void Write_WhitespacePackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => RosGzBridgeYaml.Write("  "));
        }
    }
}
