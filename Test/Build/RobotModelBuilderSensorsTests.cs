/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — coverage for RobotModelBuilder.AssembleSensors.
*/
using System;
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Build
{
    public class RobotModelBuilderSensorsTests
    {
        private static ModelLink Link(string name) =>
            new ModelLink(
                new UrdfLink(name, 1.0, Vector3.Zero, Matrix3.Identity, null, null, "", ""),
                null, null);

        private static UrdfJoint Joint(string name, string parent = "a", string child = "b") =>
            new UrdfJoint(name, UrdfJointType.Fixed, parent, child, Pose.Identity,
                Vector3.UnitZ, null, null, 0, 0, UrdfCmdInterface.Position);

        private static ImuSensor Imu(string name = "imu1", string link = "base_link",
            string topic = "/imu", double rate = 100.0) =>
            new ImuSensor(name, link, Pose.Identity, topic, link, rate, 0.0);

        [Fact]
        public void AssembleSensors_EmptyList_ReturnsEmpty()
        {
            var result = RobotModelBuilder.AssembleSensors(
                Array.Empty<SensorDef>(),
                new[] { Link("base_link") },
                Array.Empty<UrdfJoint>());
            Assert.Empty(result);
        }

        [Fact]
        public void AssembleSensors_ImuOnExistingLink_Passes()
        {
            var result = RobotModelBuilder.AssembleSensors(
                new SensorDef[] { Imu() },
                new[] { Link("base_link") },
                Array.Empty<UrdfJoint>());
            Assert.Single(result);
            Assert.IsType<ImuSensor>(result[0]);
            Assert.Equal("imu1", result[0].Name);
        }

        [Fact]
        public void AssembleSensors_SensorOnUnknownLink_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                RobotModelBuilder.AssembleSensors(
                    new SensorDef[] { Imu(link: "missing_link") },
                    new[] { Link("base_link") },
                    Array.Empty<UrdfJoint>()));
            Assert.Contains("missing_link", ex.Message);
        }

        [Fact]
        public void AssembleSensors_DuplicateSensorNames_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                RobotModelBuilder.AssembleSensors(
                    new SensorDef[] { Imu("dup"), Imu("dup", topic: "/other") },
                    new[] { Link("base_link") },
                    Array.Empty<UrdfJoint>()));
            Assert.Contains("dup", ex.Message);
        }

        [Fact]
        public void AssembleSensors_TopicWithoutSlash_PrefixedWithSlash()
        {
            var result = RobotModelBuilder.AssembleSensors(
                new SensorDef[] { Imu(topic: "imu_raw") },
                new[] { Link("base_link") },
                Array.Empty<UrdfJoint>());
            Assert.Equal("/imu_raw", result[0].Topic);
        }

        [Fact]
        public void AssembleSensors_NonPositiveUpdateRate_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                RobotModelBuilder.AssembleSensors(
                    new SensorDef[] { Imu(rate: 0.0) },
                    new[] { Link("base_link") },
                    Array.Empty<UrdfJoint>()));

            Assert.Throws<ArgumentException>(() =>
                RobotModelBuilder.AssembleSensors(
                    new SensorDef[] { Imu(rate: -5.0) },
                    new[] { Link("base_link") },
                    Array.Empty<UrdfJoint>()));
        }

        [Fact]
        public void AssembleSensors_ForceTorqueOnUnknownJoint_Throws()
        {
            var ft = new ForceTorqueSensor("ft1", "base_link", Pose.Identity,
                "/ft", "base_link", 100.0, ChildJointName: "missing_joint");
            var ex = Assert.Throws<ArgumentException>(() =>
                RobotModelBuilder.AssembleSensors(
                    new SensorDef[] { ft },
                    new[] { Link("base_link") },
                    new[] { Joint("real_joint") }));
            Assert.Contains("missing_joint", ex.Message);
        }

        [Fact]
        public void AssembleSensors_ForceTorqueNoJointsAtAll_Throws()
        {
            var ft = new ForceTorqueSensor("ft1", "base_link", Pose.Identity,
                "/ft", "base_link", 100.0, "j1");
            Assert.Throws<ArgumentException>(() =>
                RobotModelBuilder.AssembleSensors(
                    new SensorDef[] { ft },
                    new[] { Link("base_link") },
                    Array.Empty<UrdfJoint>()));
        }

        [Fact]
        public void AssembleSensors_ForceTorqueOnKnownJoint_Passes()
        {
            var ft = new ForceTorqueSensor("ft1", "base_link", Pose.Identity,
                "/ft", "base_link", 100.0, "j1");
            var result = RobotModelBuilder.AssembleSensors(
                new SensorDef[] { ft },
                new[] { Link("base_link") },
                new[] { Joint("j1") });
            Assert.Single(result);
            Assert.IsType<ForceTorqueSensor>(result[0]);
        }

        [Fact]
        public void AssembleSensors_OrderPreserved()
        {
            var sensors = new SensorDef[]
            {
                Imu("a"),
                Imu("b"),
                Imu("c"),
            };
            var result = RobotModelBuilder.AssembleSensors(
                sensors,
                new[] { Link("base_link") },
                Array.Empty<UrdfJoint>());
            Assert.Equal(new[] { "a", "b", "c" }, new[] { result[0].Name, result[1].Name, result[2].Name });
        }

        [Fact]
        public void AssembleSensors_TopicGetsSegmentSanitized()
        {
            var result = RobotModelBuilder.AssembleSensors(
                new SensorDef[] { Imu(topic: "/bad name/with spaces") },
                new[] { Link("base_link") },
                Array.Empty<UrdfJoint>());
            // segments get sanitized — spaces become underscores
            Assert.Equal("/bad_name/with_spaces", result[0].Topic);
        }

        [Fact]
        public void AssembleSensors_NullList_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RobotModelBuilder.AssembleSensors(
                    null!,
                    new[] { Link("base_link") },
                    Array.Empty<UrdfJoint>()));
        }
    }
}
