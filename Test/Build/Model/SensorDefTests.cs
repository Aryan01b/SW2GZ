/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — instantiation + record-equality coverage for each concrete
SensorDef subtype. Confirms `with`-expressions round-trip the concrete
type (and not the abstract base).
*/
using SW2GZ.Build.Model;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class SensorDefTests
    {
        [Fact]
        public void Imu_Construct_AndKindIsImu()
        {
            var s = new ImuSensor("imu", "base", Pose.Identity, "/imu", "base", 100.0, 0.01);
            Assert.Equal(SensorKind.Imu, s.Kind);
            Assert.Equal("imu", s.Name);
            Assert.Equal(0.01, s.GaussianNoiseStdDev);
        }

        [Fact]
        public void GpuLidar_Construct_AndKindIsGpuLidar()
        {
            var s = new GpuLidarSensor("lidar", "base", Pose.Identity, "/scan", "base", 20.0,
                640, -3.14, 3.14, 0.1, 10.0);
            Assert.Equal(SensorKind.GpuLidar, s.Kind);
            Assert.Equal(640, s.HorizontalSamples);
        }

        [Fact]
        public void Camera_Construct_AndKindIsCamera()
        {
            var s = new CameraSensor("cam", "base", Pose.Identity, "/cam", "base", 30.0,
                640, 480, 1.047, 0.1, 100.0);
            Assert.Equal(SensorKind.Camera, s.Kind);
            Assert.Equal(480, s.Height);
        }

        [Fact]
        public void DepthCamera_Construct_AndKindIsDepthCamera()
        {
            var s = new DepthCameraSensor("d", "base", Pose.Identity, "/depth", "base", 30.0,
                320, 240, 1.047, 0.1, 100.0);
            Assert.Equal(SensorKind.DepthCamera, s.Kind);
            Assert.Equal(320, s.Width);
        }

        [Fact]
        public void ForceTorque_Construct_AndKindIsForceTorque()
        {
            var s = new ForceTorqueSensor("ft", "base", Pose.Identity, "/ft", "base", 100.0, "j1");
            Assert.Equal(SensorKind.ForceTorque, s.Kind);
            Assert.Equal("j1", s.ChildJointName);
        }

        [Fact]
        public void Contact_Construct_AndKindIsContact()
        {
            var s = new ContactSensor("c", "base", Pose.Identity, "/c", "base", 50.0, "base_collision");
            Assert.Equal(SensorKind.Contact, s.Kind);
            Assert.Equal("base_collision", s.CollisionName);
        }

        [Fact]
        public void Navsat_Construct_AndKindIsNavsat()
        {
            var s = new NavsatSensor("gps", "base", Pose.Identity, "/gps", "base", 10.0, 0.5);
            Assert.Equal(SensorKind.Navsat, s.Kind);
            Assert.Equal(0.5, s.GaussianNoiseStdDev);
        }

        [Fact]
        public void RecordEquality_HoldsForIdenticalImu()
        {
            var a = new ImuSensor("imu", "base", Pose.Identity, "/imu", "base", 100.0, 0.01);
            var b = new ImuSensor("imu", "base", Pose.Identity, "/imu", "base", 100.0, 0.01);
            Assert.Equal(a, b);
        }

        [Fact]
        public void WithExpression_PreservesConcreteType()
        {
            var a = new ImuSensor("imu", "base", Pose.Identity, "/imu", "base", 100.0, 0.01);
            ImuSensor b = a with { Name = "renamed" };
            Assert.Equal("renamed", b.Name);
            Assert.IsType<ImuSensor>(b);
            Assert.Equal(SensorKind.Imu, b.Kind);
        }

        [Fact]
        public void DifferentSubtypes_NotEqual_EvenWithSameSharedFields()
        {
            SensorDef a = new ImuSensor("s", "base", Pose.Identity, "/s", "base", 30.0, 0);
            SensorDef b = new NavsatSensor("s", "base", Pose.Identity, "/s", "base", 30.0, 0);
            Assert.NotEqual(a, b);
        }
    }
}
