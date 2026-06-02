/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Sensors step VM tests: Add seeds a default IMU; switching Kind builds
the matching concrete SensorDef; pose xyz+rpy round-trips into SensorDef.Pose;
Remove works; duplicate names + non-positive update rates are flagged.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class SensorsStepViewModelTests
    {
        private static SensorsStepViewModel Build() =>
            new SensorsStepViewModel(new List<string> { "base_link", "link1" });

        [Fact]
        public void AddCreatesDefaultImuOnFirstLink()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            Assert.Equal(1, vm.SensorCount);
            SensorEditViewModel s = vm.Sensors[0];
            Assert.Equal(SensorKind.Imu, s.Kind);
            Assert.Equal("base_link", s.AttachedLink);
            Assert.Same(s, vm.SelectedSensor);
            Assert.True(vm.CanAdvance());

            SensorDef def = s.BuildSensor();
            Assert.IsType<ImuSensor>(def);
        }

        [Fact]
        public void ChangingKindToGpuLidarBuildsLidarSensor()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            vm.Sensors[0].Kind = SensorKind.GpuLidar;
            vm.Sensors[0].HorizontalSamples = 720;
            var def = Assert.IsType<GpuLidarSensor>(vm.Sensors[0].BuildSensor());
            Assert.Equal(720, def.HorizontalSamples);
        }

        [Fact]
        public void CameraKindBuildsCameraSensor()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            vm.Sensors[0].Kind = SensorKind.Camera;
            vm.Sensors[0].Width = 1280;
            vm.Sensors[0].Height = 720;
            var def = Assert.IsType<CameraSensor>(vm.Sensors[0].BuildSensor());
            Assert.Equal(1280, def.Width);
            Assert.Equal(720, def.Height);
        }

        [Fact]
        public void DepthCameraKindBuildsDepthCameraSensor()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            vm.Sensors[0].Kind = SensorKind.DepthCamera;
            Assert.IsType<DepthCameraSensor>(vm.Sensors[0].BuildSensor());
        }

        [Fact]
        public void PoseXyzRpyRoundTripsIntoSensorPose()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            SensorEditViewModel s = vm.Sensors[0];
            s.PoseX = 0.1; s.PoseY = 0.2; s.PoseZ = 0.3;
            s.PoseRoll = 0.0; s.PosePitch = 0.0; s.PoseYaw = 0.5;

            Pose pose = s.BuildSensor().Pose;
            Assert.Equal(0.1, pose.Position.X, 4);
            Assert.Equal(0.2, pose.Position.Y, 4);
            Assert.Equal(0.3, pose.Position.Z, 4);

            (double roll, double pitch, double yaw) = Matrix3.FromQuaternion(pose.Rotation).ToRpy();
            Assert.Equal(0.0, roll, 4);
            Assert.Equal(0.0, pitch, 4);
            Assert.Equal(0.5, yaw, 4);
        }

        [Fact]
        public void PoseRpyFullRoundTrip()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            SensorEditViewModel s = vm.Sensors[0];
            s.PoseRoll = 0.3; s.PosePitch = -0.2; s.PoseYaw = 1.1;

            (double roll, double pitch, double yaw) = Matrix3.FromQuaternion(s.BuildSensor().Pose.Rotation).ToRpy();
            Assert.Equal(0.3, roll, 4);
            Assert.Equal(-0.2, pitch, 4);
            Assert.Equal(1.1, yaw, 4);
        }

        [Fact]
        public void RemoveWorks()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            vm.AddSensorCommand.Execute(null);
            Assert.Equal(2, vm.SensorCount);

            vm.SelectedSensor = vm.Sensors[0];
            vm.RemoveSensorCommand.Execute(null);
            Assert.Equal(1, vm.SensorCount);
            Assert.NotNull(vm.SelectedSensor);
        }

        [Fact]
        public void DuplicateNamesAreFlagged()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            vm.AddSensorCommand.Execute(null);
            Assert.False(vm.HasDuplicateNames); // unique by default (imu, imu_2)

            vm.Sensors[1].Name = vm.Sensors[0].Name;
            Assert.True(vm.HasDuplicateNames);
            Assert.Contains(vm.Sensors[0].Name, vm.DuplicateNames);
            Assert.True(vm.CanAdvance()); // warn-not-block
        }

        [Fact]
        public void NonPositiveUpdateRateIsFlagged()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            Assert.Equal(0, vm.InvalidSensorCount);

            vm.Sensors[0].UpdateRate = 0.0;
            Assert.Equal(1, vm.InvalidSensorCount);
            Assert.False(vm.Sensors[0].UpdateRateValid);
        }

        [Fact]
        public void BuildSensorsMaterializesAll()
        {
            var vm = Build();
            vm.AddSensorCommand.Execute(null);
            vm.Sensors[0].Kind = SensorKind.Navsat;
            vm.AddSensorCommand.Execute(null);
            vm.Sensors[1].Kind = SensorKind.ForceTorque;

            IReadOnlyList<SensorDef> defs = vm.BuildSensors();
            Assert.Equal(2, defs.Count);
            Assert.IsType<NavsatSensor>(defs[0]);
            Assert.IsType<ForceTorqueSensor>(defs[1]);
        }
    }
}
