/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Gz;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestRosGzBridgeYaml : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void EmitsDefaultBridgeEntriesForClockJointStatesAndTf()
        {
            new RosGzBridgeYaml().Write(TempDir, "ros_gz_bridge.yaml");
            Assert.True(Exists("ros_gz_bridge.yaml"));
            var txt = ReadAllText("ros_gz_bridge.yaml");
            Assert.Contains("/clock", txt);
            Assert.Contains("rosgraph_msgs/msg/Clock", txt);
            Assert.Contains("/joint_states", txt);
            Assert.Contains("sensor_msgs/msg/JointState", txt);
            Assert.Contains("/tf", txt);
            Assert.Contains("tf2_msgs/msg/TFMessage", txt);
        }
    }
}
