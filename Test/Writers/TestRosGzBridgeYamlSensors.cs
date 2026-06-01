/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — RosGzBridgeYaml.Write(packageName, sensors) overload coverage.
Verifies (a) byte-parity with the single-arg overload when sensors are
empty, and (b) the right ROS↔Gz type pair appears for each SensorKind.
*/
using System;
using SW2GZ.Build.Model;
using SW2GZ.Gz;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestRosGzBridgeYamlSensors
    {
        [Fact]
        public void Write_NoSensors_ByteIdenticalToSingleArgOverload()
        {
            string legacy = RosGzBridgeYaml.Write("my_pkg");
            string @new = RosGzBridgeYaml.Write("my_pkg", Array.Empty<SensorDef>());
            Assert.Equal(legacy, @new);
        }

        [Fact]
        public void Write_OneImu_AppendsImuBridgeEntry()
        {
            var imu = new ImuSensor("imu1", "base", Pose.Identity, "/imu", "base", 100.0, 0.0);
            string yaml = RosGzBridgeYaml.Write("my_pkg", new SensorDef[] { imu });
            Assert.Contains("/imu", yaml);
            Assert.Contains("sensor_msgs/msg/Imu", yaml);
            Assert.Contains("gz.msgs.IMU", yaml);
        }

        [Fact]
        public void Write_GpuLidar_AppendsLaserScanEntry()
        {
            var s = new GpuLidarSensor("l", "base", Pose.Identity, "/scan", "base", 20.0,
                360, -3.14, 3.14, 0.1, 10);
            string yaml = RosGzBridgeYaml.Write("p", new SensorDef[] { s });
            Assert.Contains("sensor_msgs/msg/LaserScan", yaml);
            Assert.Contains("gz.msgs.LaserScan", yaml);
        }

        [Fact]
        public void Write_Camera_AppendsImageEntry()
        {
            var s = new CameraSensor("c", "base", Pose.Identity, "/cam", "base", 30, 640, 480, 1.047, 0.1, 100);
            string yaml = RosGzBridgeYaml.Write("p", new SensorDef[] { s });
            Assert.Contains("sensor_msgs/msg/Image", yaml);
            Assert.Contains("gz.msgs.Image", yaml);
        }

        [Fact]
        public void Write_DepthCamera_AppendsImageEntry()
        {
            var s = new DepthCameraSensor("d", "base", Pose.Identity, "/depth", "base", 30, 320, 240, 1.047, 0.1, 100);
            string yaml = RosGzBridgeYaml.Write("p", new SensorDef[] { s });
            Assert.Contains("sensor_msgs/msg/Image", yaml);
            Assert.Contains("gz.msgs.Image", yaml);
        }

        [Fact]
        public void Write_ForceTorque_AppendsWrenchEntry()
        {
            var s = new ForceTorqueSensor("ft", "base", Pose.Identity, "/ft", "base", 100, "j1");
            string yaml = RosGzBridgeYaml.Write("p", new SensorDef[] { s });
            Assert.Contains("geometry_msgs/msg/WrenchStamped", yaml);
            Assert.Contains("gz.msgs.Wrench", yaml);
        }

        [Fact]
        public void Write_Contact_AppendsContactsEntry()
        {
            var s = new ContactSensor("c", "base", Pose.Identity, "/contact", "base", 50, "base_col");
            string yaml = RosGzBridgeYaml.Write("p", new SensorDef[] { s });
            Assert.Contains("ros_gz_interfaces/msg/Contacts", yaml);
            Assert.Contains("gz.msgs.Contacts", yaml);
        }

        [Fact]
        public void Write_Navsat_AppendsNavSatFixEntry()
        {
            var s = new NavsatSensor("gps", "base", Pose.Identity, "/gps", "base", 10, 0.1);
            string yaml = RosGzBridgeYaml.Write("p", new SensorDef[] { s });
            Assert.Contains("sensor_msgs/msg/NavSatFix", yaml);
            Assert.Contains("gz.msgs.NavSat", yaml);
        }

        [Fact]
        public void Write_NullSensors_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RosGzBridgeYaml.Write("p", null));
        }
    }
}
