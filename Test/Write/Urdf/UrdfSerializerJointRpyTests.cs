/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P2 — joint origin rpy emission. AppendJoint now converts the joint's
Origin.Rotation (quaternion) to roll/pitch/yaw via the single source of
truth (Matrix3.FromQuaternion(q).ToRpy()). Identity rotation must still
emit "0 0 0" byte-identically; a non-identity rotation must emit the
computed rpy in InvariantCulture.
*/
using System;
using System.Globalization;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.Write.Urdf;
using Xunit;

namespace SW2GZ.Write.Urdf.Tests
{
    public class UrdfSerializerJointRpyTests
    {
        private static RobotMeta Meta(string pkg = "test_pkg") =>
            new RobotMeta(pkg, "A", "a@b", "Apache-2.0", CoordinateConvention.Identity);

        private static UrdfLink Link(string name) =>
            new UrdfLink(name, 1.0, Vector3.Zero, Matrix3.Identity, null, null,
                name + ".dae", name + "_collision.stl");

        [Fact]
        public void IdentityRotation_EmitsZeroRpy()
        {
            var joint = new UrdfJoint("j", UrdfJointType.Revolute, "base_link", "arm1",
                Pose.Identity, Vector3.UnitZ, -1.0, 1.0, 10.0, 1.0, UrdfCmdInterface.Position);
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1") }, new[] { joint });

            string xml = UrdfSerializer.SerializeBody(model);

            Assert.Contains("<origin xyz=\"0 0 0\" rpy=\"0 0 0\"/>", xml);
        }

        [Fact]
        public void NinetyDegreesAboutZ_EmitsComputedRpy()
        {
            // 90 deg about Z.
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2.0));
            var origin = new Pose(new Vector3(0.1f, 0.2f, 0.3f), q);

            var joint = new UrdfJoint("j", UrdfJointType.Revolute, "base_link", "arm1",
                origin, Vector3.UnitZ, -1.0, 1.0, 10.0, 1.0, UrdfCmdInterface.Position);
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1") }, new[] { joint });

            string xml = UrdfSerializer.SerializeBody(model);

            // Expected rpy computed via the same single source of truth the
            // serializer uses, so the test stays in lockstep with ToRpy().
            var (roll, pitch, yaw) = Matrix3.FromQuaternion(q).ToRpy();
            string expected = string.Format(CultureInfo.InvariantCulture,
                "<origin xyz=\"{0} {1} {2}\" rpy=\"{3} {4} {5}\"/>",
                0.1f, 0.2f, 0.3f, roll, pitch, yaw);

            Assert.Contains(expected, xml);
            // yaw should be ~pi/2 for a +90 deg Z rotation.
            Assert.True(System.Math.Abs(yaw - System.Math.PI / 2.0) < 1e-6);
        }

        [Fact]
        public void RotationAboutX_EmitsRollOnly()
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(System.Math.PI / 2.0));
            var joint = new UrdfJoint("j", UrdfJointType.Fixed, "base_link", "arm1",
                new Pose(Vector3.Zero, q), Vector3.UnitZ, null, null, 0, 0, UrdfCmdInterface.Position);
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1") }, new[] { joint });

            string xml = UrdfSerializer.SerializeBody(model);

            var (roll, pitch, yaw) = Matrix3.FromQuaternion(q).ToRpy();
            Assert.True(System.Math.Abs(roll - System.Math.PI / 2.0) < 1e-6);
            string expected = string.Format(CultureInfo.InvariantCulture,
                "rpy=\"{0} {1} {2}\"", roll, pitch, yaw);
            Assert.Contains(expected, xml);
        }
    }
}
