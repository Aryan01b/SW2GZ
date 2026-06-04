/*
Copyright (c) 2026 Aryan Arlikar. MIT License â€” see CONTRIBUTING.md.

P6-data â€” coverage for XacroGenerator.SerializeGazeboSensorBlocks +
its integration into SerializeBody. Byte-parity check confirms no
output when Sensors empty (golden tests pass unmodified).
*/
using System;
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.Write.Urdf;
using Xunit;

namespace SW2GZ.Test.Write.Urdf
{
    public class UrdfSerializerSensorBlocksTests
    {
        private static UrdfLink Link(string name = "base_link") =>
            new UrdfLink(name, 1.0, Vector3.Zero, Matrix3.Identity, null, null,
                $"{name}_visual.dae", $"{name}_collision.stl");

        private static RobotMeta Meta() =>
            new RobotMeta("pkg", "a", "a@b", "MIT", CoordinateConvention.Identity);

        private static RobotModel ModelWith(params SensorDef[] sensors) =>
            new RobotModel(
                Meta(),
                new[] { new ModelLink(Link(), null, null) },
                Array.Empty<UrdfJoint>(),
                Array.Empty<MaterialDef>(),
                sensors,
                new ControlSpec(new List<string>(), ControlSpec.DefaultJointStateBroadcaster));

        [Fact]
        public void SerializeBody_NoSensors_NoGazeboBlocks()
        {
            string body = XacroGenerator.SerializeBody(ModelWith());
            Assert.DoesNotContain("<gazebo", body);
        }

        [Fact]
        public void SerializeBody_OneImu_EmitsGazeboReferenceBlock()
        {
            var imu = new ImuSensor("imu1", "base_link", Pose.Identity, "/imu", "base_link", 100.0, 0.01);
            string body = XacroGenerator.SerializeBody(ModelWith(imu));
            Assert.Contains("<gazebo reference=\"base_link\">", body);
            Assert.Contains("<sensor name=\"imu1\" type=\"imu\">", body);
            Assert.Contains("</gazebo>", body);
        }

        [Fact]
        public void SerializeBody_TwoSensorsSameLink_GroupedIntoOneGazeboBlock()
        {
            var imu = new ImuSensor("imu1", "base_link", Pose.Identity, "/imu", "base_link", 100.0, 0.0);
            var cam = new CameraSensor("cam1", "base_link", Pose.Identity, "/cam", "base_link", 30.0,
                640, 480, 1.047, 0.1, 100.0);
            string body = XacroGenerator.SerializeBody(ModelWith(imu, cam));
            // Single gazebo wrapper for base_link
            int gazeboCount = 0;
            int idx = 0;
            while ((idx = body.IndexOf("<gazebo reference=\"base_link\">", idx, StringComparison.Ordinal)) >= 0)
            {
                gazeboCount++;
                idx++;
            }
            Assert.Equal(1, gazeboCount);
            Assert.Contains("imu1", body);
            Assert.Contains("cam1", body);
        }

        [Fact]
        public void SerializeGazeboSensorBlocks_EmptyOrNull_EmptyString()
        {
            Assert.Equal(string.Empty, XacroGenerator.SerializeGazeboSensorBlocks(Array.Empty<SensorDef>()));
            Assert.Equal(string.Empty, XacroGenerator.SerializeGazeboSensorBlocks(null));
        }
    }
}
