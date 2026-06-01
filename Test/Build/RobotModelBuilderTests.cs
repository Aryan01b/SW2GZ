/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModelBuilder tests. Covers: happy path, null/empty guard rails,
package-name sanitization, defaulted Materials/Sensors/Control collections.
*/
using System;
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class RobotModelBuilderTests
    {
        private static UrdfLink MakeLink(string name = "base_link") =>
            new UrdfLink(name, 1.0, Vector3.Zero, Matrix3.Identity, null, null, "", "");

        private static RobotMeta Meta(string pkg = "test_pkg") =>
            new RobotMeta(pkg, "Aryan", "a@b.test", "Apache-2.0", CoordinateConvention.Identity);

        [Fact]
        public void Build_HappyPath_SingleLinkNoJoints()
        {
            var model = RobotModelBuilder.Build(Meta(), new[] { MakeLink() }, Array.Empty<UrdfJoint>());

            Assert.Equal("test_pkg", model.Meta.PackageName);
            Assert.Single(model.Links);
            Assert.Equal("base_link", model.Links[0].Link.Name);
            Assert.Null(model.Links[0].MaterialName);
            Assert.Null(model.Links[0].Gazebo);
            Assert.Empty(model.Joints);
        }

        [Fact]
        public void Build_RejectsNullMeta()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RobotModelBuilder.Build(null!, new[] { MakeLink() }, Array.Empty<UrdfJoint>()));
        }

        [Fact]
        public void Build_RejectsNullLinks()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RobotModelBuilder.Build(Meta(), null!, Array.Empty<UrdfJoint>()));
        }

        [Fact]
        public void Build_RejectsEmptyLinks()
        {
            Assert.Throws<ArgumentException>(() =>
                RobotModelBuilder.Build(Meta(), Array.Empty<UrdfLink>(), Array.Empty<UrdfJoint>()));
        }

        [Fact]
        public void Build_RejectsNullJoints()
        {
            Assert.Throws<ArgumentNullException>(() =>
                RobotModelBuilder.Build(Meta(), new[] { MakeLink() }, null!));
        }

        [Fact]
        public void Build_SanitizesPackageName()
        {
            // PackageNameSanitizer lowercases and replaces non-[a-z0-9_] with '_',
            // collapses repeats and strips trailing underscores. "My Robot!" → "my_robot".
            var meta = Meta("My Robot!");
            var model = RobotModelBuilder.Build(meta, new[] { MakeLink() }, Array.Empty<UrdfJoint>());

            Assert.Equal("my_robot", model.Meta.PackageName);
        }

        [Fact]
        public void Build_DefaultsMaterialsAndSensorsToEmpty()
        {
            var model = RobotModelBuilder.Build(Meta(), new[] { MakeLink() }, Array.Empty<UrdfJoint>());

            Assert.Empty(model.Materials);
            Assert.Empty(model.Sensors);
        }

        [Fact]
        public void Build_DefaultControl_ListsAllJointsAndUsesJointStateBroadcaster()
        {
            var joints = new[]
            {
                new UrdfJoint("j1", UrdfJointType.Revolute, "base_link", "arm1",
                    Pose.Identity, Vector3.UnitZ, -1.0, 1.0, 10.0, 1.0, UrdfCmdInterface.Position),
                new UrdfJoint("j2", UrdfJointType.Revolute, "arm1", "arm2",
                    Pose.Identity, Vector3.UnitZ, -1.0, 1.0, 10.0, 1.0, UrdfCmdInterface.Position),
            };

            var model = RobotModelBuilder.Build(Meta(),
                new[] { MakeLink("base_link"), MakeLink("arm1"), MakeLink("arm2") },
                joints);

            Assert.Equal(2, model.Control.JointNames.Count);
            Assert.Equal("j1", model.Control.JointNames[0]);
            Assert.Equal("j2", model.Control.JointNames[1]);
            Assert.Equal(ControlSpec.DefaultJointStateBroadcaster, model.Control.DefaultController);
        }

        [Fact]
        public void Build_AcceptsExplicitMaterialsSensorsAndControl()
        {
            var mats = new[] { new MaterialDef("steel", 0.5, 0.5, 0.5, 1.0) };
            // P6-data: SensorDef is now abstract; use a concrete subtype.
            var sens = new SensorDef[]
            {
                new CameraSensor("cam", "base_link", Pose.Identity, "/cam", "cam_frame",
                    UpdateRate: 30.0, Width: 640, Height: 480, HorizontalFovRad: 1.047,
                    NearClip: 0.1, FarClip: 100.0),
            };
            var ctrl = new ControlSpec(new List<string> { "j1" }, "custom_controller");

            var model = RobotModelBuilder.Build(Meta(),
                new[] { MakeLink() },
                Array.Empty<UrdfJoint>(),
                mats, sens, ctrl);

            Assert.Single(model.Materials);
            Assert.Equal("steel", model.Materials[0].Name);
            Assert.Single(model.Sensors);
            Assert.Equal("cam", model.Sensors[0].Name);
            Assert.Equal("custom_controller", model.Control.DefaultController);
        }

        [Fact]
        public void CoordinateConvention_Identity_Validates()
        {
            Assert.True(CoordinateConvention.Identity.Validate());
        }

        [Fact]
        public void CoordinateConvention_ZeroMatrix_FailsValidate()
        {
            var bad = new CoordinateConvention(new Matrix3(0,0,0,0,0,0,0,0,0), 1.0);
            Assert.False(bad.Validate());
        }

        [Fact]
        public void CoordinateConvention_NonPositiveScale_FailsValidate()
        {
            var bad = new CoordinateConvention(Matrix3.Identity, 0);
            Assert.False(bad.Validate());
            var bad2 = new CoordinateConvention(Matrix3.Identity, -1);
            Assert.False(bad2.Validate());
        }
    }
}
