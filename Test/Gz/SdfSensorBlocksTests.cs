/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — coverage for SdfSensorBlocks.Write across all 7 sensor kinds.
Asserts the right top-level <sensor type="..."> tag, the type-specific
inner block, locale-safe float formatting, and XML escaping for special
characters in names.
*/
using System.Globalization;
using System.Numerics;
using System.Threading;
using SW2GZ.Build.Model;
using SW2GZ.Gz;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Gz
{
    public class SdfSensorBlocksTests
    {
        [Fact]
        public void Write_Imu_ContainsImuTypeAndNoise()
        {
            var s = new ImuSensor("imu1", "base", Pose.Identity, "/imu", "base", 100.0, 0.02);
            string xml = SdfSensorBlocks.Write(s);
            Assert.Contains("<sensor name=\"imu1\" type=\"imu\">", xml);
            Assert.Contains("<imu>", xml);
            Assert.Contains("<angular_velocity>", xml);
            Assert.Contains("<linear_acceleration>", xml);
            Assert.Contains("<stddev>0.02</stddev>", xml);
            Assert.Contains("<topic>/imu</topic>", xml);
            Assert.Contains("<gz_frame_id>base</gz_frame_id>", xml);
        }

        [Fact]
        public void Write_GpuLidar_ContainsRayScanAndRange()
        {
            var s = new GpuLidarSensor("lidar1", "base", Pose.Identity, "/scan", "base", 20.0,
                360, -3.14, 3.14, 0.1, 30.0);
            string xml = SdfSensorBlocks.Write(s);
            Assert.Contains("<sensor name=\"lidar1\" type=\"gpu_lidar\">", xml);
            Assert.Contains("<ray>", xml);
            Assert.Contains("<scan>", xml);
            Assert.Contains("<samples>360</samples>", xml);
            Assert.Contains("<range>", xml);
            Assert.Contains("<min>0.1</min>", xml);
            Assert.Contains("<max>30</max>", xml);
        }

        [Fact]
        public void Write_Camera_ContainsR8G8B8Format()
        {
            var s = new CameraSensor("cam1", "base", Pose.Identity, "/cam", "base", 30.0,
                640, 480, 1.5, 0.1, 100.0);
            string xml = SdfSensorBlocks.Write(s);
            Assert.Contains("<sensor name=\"cam1\" type=\"camera\">", xml);
            Assert.Contains("<format>R8G8B8</format>", xml);
            Assert.Contains("<width>640</width>", xml);
            Assert.Contains("<height>480</height>", xml);
            Assert.Contains("<horizontal_fov>1.5</horizontal_fov>", xml);
            Assert.DoesNotContain("R_FLOAT32", xml);
        }

        [Fact]
        public void Write_DepthCamera_ContainsRFloat32Format()
        {
            var s = new DepthCameraSensor("dcam", "base", Pose.Identity, "/depth", "base", 30.0,
                320, 240, 1.047, 0.1, 100.0);
            string xml = SdfSensorBlocks.Write(s);
            Assert.Contains("<sensor name=\"dcam\" type=\"depth_camera\">", xml);
            Assert.Contains("<format>R_FLOAT32</format>", xml);
            Assert.DoesNotContain("R8G8B8", xml);
        }

        [Fact]
        public void Write_ForceTorque_NoPoseOrGzFrameId()
        {
            var s = new ForceTorqueSensor("ft1", "base", Pose.Identity, "/ft", "base", 100.0, "j1");
            string xml = SdfSensorBlocks.Write(s);
            Assert.Contains("<sensor name=\"ft1\" type=\"force_torque\">", xml);
            Assert.Contains("<force_torque>", xml);
            Assert.Contains("<frame>child</frame>", xml);
            Assert.Contains("<measure_direction>child_to_parent</measure_direction>", xml);
            Assert.DoesNotContain("<pose>", xml);
            Assert.DoesNotContain("<gz_frame_id>", xml);
        }

        [Fact]
        public void Write_Contact_ContainsCollisionElement()
        {
            var s = new ContactSensor("c1", "base", Pose.Identity, "/c", "base", 50.0, "base_collision");
            string xml = SdfSensorBlocks.Write(s);
            Assert.Contains("<sensor name=\"c1\" type=\"contact\">", xml);
            Assert.Contains("<contact>", xml);
            Assert.Contains("<collision>base_collision</collision>", xml);
        }

        [Fact]
        public void Write_Navsat_ContainsNavsatBlock()
        {
            var s = new NavsatSensor("gps1", "base", Pose.Identity, "/gps", "base", 10.0, 0.5);
            string xml = SdfSensorBlocks.Write(s);
            Assert.Contains("<sensor name=\"gps1\" type=\"navsat\">", xml);
            Assert.Contains("<navsat>", xml);
            Assert.Contains("<position_sensing>", xml);
            Assert.Contains("<horizontal>", xml);
            Assert.Contains("<vertical>", xml);
            Assert.Contains("<stddev>0.5</stddev>", xml);
        }

        [Fact]
        public void Write_FloatFormatting_UsesInvariantCulture()
        {
            // Hop into a comma-locale to verify Fmt does not switch to comma-decimals.
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var s = new GpuLidarSensor("lidar", "base", Pose.Identity, "/scan", "base", 20.5,
                    360, -3.14, 3.14, 0.1, 10.0);
                string xml = SdfSensorBlocks.Write(s);
                Assert.Contains("<update_rate>20.5</update_rate>", xml);
                Assert.Contains("<min>0.1</min>", xml);
                Assert.DoesNotContain("0,1", xml);
                Assert.DoesNotContain("20,5", xml);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Write_NameWithSpecialChars_XmlEscaped()
        {
            // RosNameSanitizer prevents these usually; we still defend in the
            // writer. SecurityElement.Escape converts & -> &amp;.
            var s = new ImuSensor("a&b", "base", Pose.Identity, "/imu", "base", 100.0, 0.0);
            string xml = SdfSensorBlocks.Write(s);
            Assert.Contains("name=\"a&amp;b\"", xml);
        }

        [Fact]
        public void Write_PoseFormatted_AsSixSpaceSeparatedFloats()
        {
            var pose = new Pose(new Vector3(1, 2, 3), Quaternion.Identity);
            var s = new ImuSensor("imu", "base", pose, "/imu", "base", 100.0, 0.0);
            string xml = SdfSensorBlocks.Write(s);
            // Identity quaternion → rpy 0 0 0.
            Assert.Contains("<pose>1 2 3 0 0 0</pose>", xml);
        }

        [Fact]
        public void Write_PoseWithPitch90Deg_RpyContainsHalfPi()
        {
            // Build a quaternion that drives sinp = 2*(W*Y - Z*X) past 1.0 so
            // QuatToRpy must fall into the CopySign(π/2, sinp) gimbal-lock
            // branch (asin would NaN at |sinp| > 1). W=Y=0.9 → sinp = 1.62 ≥ 1
            // → pitch = +π/2 ≈ 1.5707963.
            // (Quaternion.CreateFromAxisAngle(UnitY, π/2) doesn't hit the branch
            // because float roundoff leaves sinp ≈ 0.99999, asin → 1.57045…)
            var q = new System.Numerics.Quaternion(0f, 0.9f, 0f, 0.9f);
            var sensor = new ImuSensor(
                Name: "gimbal_test_imu",
                AttachedLink: "base_link",
                Pose: new Pose(System.Numerics.Vector3.Zero, q),
                Topic: "/gimbal/imu",
                GzFrameId: "base_link",
                UpdateRate: 30.0,
                GaussianNoiseStdDev: 0.0);

            string xml = SdfSensorBlocks.Write(sensor);

            // pitch should be approximately π/2 (1.5707963...) — verify the CopySign(π/2, sinp) branch
            Assert.Contains("1.5707", xml);
            Assert.DoesNotContain("NaN", xml);
        }
    }
}
