/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Tests that XacroGenerator emits compensating <origin> blocks on
<visual>, <collision>, and <inertial> when UrdfLink.FrameOffset is
non-zero (mate-point joint-origin path), and stays byte-identical
when FrameOffset is Vector3.Zero (legacy / no-mate path).
*/
using System;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.Write.Urdf;
using Xunit;

namespace SW2GZ.Test.Write.Urdf
{
    public class XacroGeneratorFrameOffsetTests
    {
        private static RobotMeta Meta() =>
            new RobotMeta("test_pkg", "A", "a@b", "Apache-2.0", CoordinateConvention.Identity);

        private static UrdfLink Link(string name, Vector3 offset) =>
            new UrdfLink(
                Name: name,
                Mass: 1.0,
                ComLocal: Vector3.Zero,
                InertiaAtComLocal: Matrix3.Identity,
                VisualMesh: null,
                CollisionMesh: null,
                VisualMeshFile: name + ".dae",
                CollisionMeshFile: name + "_collision.stl",
                FrameOffset: offset);

        [Fact]
        [Trait("Category", "Unit")]
        public void ZeroOffset_VisualHasNoOrigin_LegacyByteStable()
        {
            // Vector3.Zero / default keeps the legacy single-line emit:
            //   <visual><geometry>...</geometry></visual>
            // No <origin> inside <visual> or <collision>.
            var link = Link("base_link", Vector3.Zero);
            var model = RobotModelBuilder.Build(Meta(), new[] { link }, Array.Empty<UrdfJoint>());
            string xml = XacroGenerator.SerializeBody(model);

            Assert.Contains("<visual><geometry>", xml);
            Assert.Contains("<collision><geometry>", xml);
            // Only the <inertial><origin> is allowed (one occurrence).
            int originCount = System.Text.RegularExpressions.Regex.Matches(xml, "<origin ").Count;
            Assert.Equal(1, originCount);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void NonZeroOffset_EmitsOriginOnVisualAndCollision()
        {
            var off = new Vector3(0.1f, 0.2f, 0.3f);
            var link = Link("arm_link", off);
            var model = RobotModelBuilder.Build(Meta(), new[] { link }, Array.Empty<UrdfJoint>());
            string xml = XacroGenerator.SerializeBody(model);

            // Three <origin> blocks now: inertial (already had one), visual, collision.
            Assert.Contains("<visual>", xml);
            Assert.DoesNotContain("<visual><geometry>", xml);
            Assert.Contains("<collision>", xml);
            Assert.DoesNotContain("<collision><geometry>", xml);
            // The same offset coords should appear at least twice (visual + collision)
            // and the inertial origin should equal ComLocal + offset = (0.1, 0.2, 0.3)
            // since ComLocal is Vector3.Zero.
            int xyzHits = System.Text.RegularExpressions.Regex.Matches(
                xml, "<origin xyz=\"0\\.1 0\\.2 0\\.3\" rpy=\"0 0 0\"/>").Count;
            Assert.Equal(3, xyzHits);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void NonZeroOffset_ShiftsInertialCom()
        {
            // ComLocal = (1, 0, 0), offset = (5, 0, 0) → inertial origin = (6, 0, 0).
            var link = new UrdfLink(
                Name: "link",
                Mass: 1.0,
                ComLocal: new Vector3(1, 0, 0),
                InertiaAtComLocal: Matrix3.Identity,
                VisualMesh: null,
                CollisionMesh: null,
                VisualMeshFile: "link.dae",
                CollisionMeshFile: "link_collision.stl",
                FrameOffset: new Vector3(5, 0, 0));

            var model = RobotModelBuilder.Build(Meta(), new[] { link }, Array.Empty<UrdfJoint>());
            string xml = XacroGenerator.SerializeBody(model);

            // Inertial origin = ComLocal + FrameOffset = (6, 0, 0).
            Assert.Contains("<origin xyz=\"6 0 0\" rpy=\"0 0 0\"/>", xml);
            // Visual / collision origin = FrameOffset = (5, 0, 0).
            Assert.Contains("<origin xyz=\"5 0 0\" rpy=\"0 0 0\"/>", xml);
        }
    }
}
