/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Gz;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestWorldBridgeYaml
    {
        [Fact]
        public void Write_AlwaysBridgesClock_GzToRos()
        {
            string yaml = WorldBridgeYaml.Write(false);
            Assert.Contains("ros_topic_name: \"/clock\"", yaml);
            Assert.Contains("rosgraph_msgs/msg/Clock", yaml);
            Assert.Contains("gz.msgs.Clock", yaml);
            Assert.Contains("direction: GZ_TO_ROS", yaml);
        }

        [Fact]
        public void Write_NoTeleop_OmitsCmdVel()
        {
            string yaml = WorldBridgeYaml.Write(false);
            Assert.DoesNotContain("/cmd_vel", yaml);
            Assert.DoesNotContain("ROS_TO_GZ", yaml);
        }

        [Fact]
        public void Write_Teleop_BridgesCmdVel_RosToGz()
        {
            string yaml = WorldBridgeYaml.Write(true);
            Assert.Contains("ros_topic_name: \"/cmd_vel\"", yaml);
            Assert.Contains("geometry_msgs/msg/Twist", yaml);
            Assert.Contains("gz.msgs.Twist", yaml);
            Assert.Contains("direction: ROS_TO_GZ", yaml);
            // clock is still present.
            Assert.Contains("/clock", yaml);
        }

        [Fact]
        public void Write_StartsWithHeaderComment()
        {
            Assert.StartsWith("# ", WorldBridgeYaml.Write(false));
        }
    }
}
