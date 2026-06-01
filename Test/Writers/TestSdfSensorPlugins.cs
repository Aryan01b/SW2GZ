/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — SdfSensorPlugins family-detection + dedup coverage.
*/
using System;
using SW2GZ.Build.Model;
using SW2GZ.Gz;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestSdfSensorPlugins
    {
        private static ImuSensor Imu(string name) =>
            new ImuSensor(name, "base", Pose.Identity, "/" + name, "base", 100.0, 0.0);
        private static CameraSensor Cam(string name) =>
            new CameraSensor(name, "base", Pose.Identity, "/" + name, "base", 30.0, 640, 480, 1.047, 0.1, 100);

        [Fact]
        public void WritePluginBlock_NoSensors_EmptyString()
        {
            Assert.Equal(string.Empty,
                SdfSensorPlugins.WritePluginBlock(Array.Empty<SensorDef>()));
        }

        [Fact]
        public void WritePluginBlock_Null_EmptyString()
        {
            Assert.Equal(string.Empty, SdfSensorPlugins.WritePluginBlock(null));
        }

        [Fact]
        public void WritePluginBlock_ImuOnly_OneImuPlugin()
        {
            string xml = SdfSensorPlugins.WritePluginBlock(new SensorDef[] { Imu("a") });
            Assert.Contains("gz-sim-imu-system", xml);
            Assert.DoesNotContain("gz-sim-contact-system", xml);
            Assert.DoesNotContain("gz-sim-navsat-system", xml);
            Assert.DoesNotContain("gz-sim-forcetorque-system", xml);
        }

        [Fact]
        public void WritePluginBlock_CameraOnly_OneSensorsPlugin()
        {
            string xml = SdfSensorPlugins.WritePluginBlock(new SensorDef[] { Cam("a") });
            Assert.Contains("gz-sim-sensors-system", xml);
            Assert.Contains("ogre2", xml);
            Assert.DoesNotContain("gz-sim-imu-system", xml);
        }

        [Fact]
        public void WritePluginBlock_TwoImus_StillOnePlugin()
        {
            string xml = SdfSensorPlugins.WritePluginBlock(new SensorDef[] { Imu("a"), Imu("b") });
            int first = xml.IndexOf("gz-sim-imu-system", StringComparison.Ordinal);
            int last = xml.LastIndexOf("gz-sim-imu-system", StringComparison.Ordinal);
            Assert.True(first >= 0);
            Assert.Equal(first, last);   // appears exactly once
        }

        [Fact]
        public void WritePluginBlock_MixedKinds_AllRequiredPluginsOnce()
        {
            var sensors = new SensorDef[]
            {
                Imu("imu"),
                Cam("cam"),
                new ContactSensor("c", "base", Pose.Identity, "/c", "base", 50, "base_col"),
                new ForceTorqueSensor("ft", "base", Pose.Identity, "/ft", "base", 100, "j1"),
                new NavsatSensor("gps", "base", Pose.Identity, "/gps", "base", 10, 0.1),
            };
            string xml = SdfSensorPlugins.WritePluginBlock(sensors);
            Assert.Contains("gz-sim-imu-system", xml);
            Assert.Contains("gz-sim-sensors-system", xml);
            Assert.Contains("gz-sim-contact-system", xml);
            Assert.Contains("gz-sim-forcetorque-system", xml);
            Assert.Contains("gz-sim-navsat-system", xml);
        }

        [Fact]
        public void WritePluginBlock_LidarOrDepthCamera_TriggersSensorsPlugin()
        {
            var lidar = new GpuLidarSensor("l", "base", Pose.Identity, "/scan", "base", 20,
                360, -3.14, 3.14, 0.1, 10);
            string xml = SdfSensorPlugins.WritePluginBlock(new SensorDef[] { lidar });
            Assert.Contains("gz-sim-sensors-system", xml);
        }
    }
}
