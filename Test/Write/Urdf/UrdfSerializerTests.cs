/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — UrdfSerializer tests. Coverage:
  * single empty link → expected fragment
  * link with inertia + mesh files → expected fragment
  * joint serialization (fixed / revolute / continuous)
  * round-trip well-formedness via XDocument.Parse
  * package name with special chars gets XML-escaped
  * byte-parity proof: hand-constructed reference matches SerializeBody output
*/
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Xml.Linq;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.Write.Urdf;
using Xunit;

namespace SW2GZ.Write.Urdf.Tests
{
    public class UrdfSerializerTests
    {
        private static RobotMeta Meta(string pkg = "test_pkg") =>
            new RobotMeta(pkg, "A", "a@b", "Apache-2.0", CoordinateConvention.Identity);

        private static UrdfLink Link(string name, double mass = 1.0,
            string visualFile = "base_link.dae", string collisionFile = "base_link_collision.stl") =>
            new UrdfLink(name, mass, Vector3.Zero, Matrix3.Identity, null, null, visualFile, collisionFile);

        private static RobotModel ModelWith(params UrdfLink[] links) =>
            RobotModelBuilder.Build(Meta(), links, Array.Empty<UrdfJoint>());

        [Fact]
        public void SerializeBody_SingleLink_ProducesExpectedFragment()
        {
            var model = ModelWith(Link("base_link"));
            string xml = UrdfSerializer.SerializeBody(model);

            // Substrings cover every required element. Whitespace/order verified
            // by the parity test below.
            Assert.Contains("<link name=\"base_link\">", xml);
            Assert.Contains("<inertial>", xml);
            Assert.Contains("<origin xyz=\"0 0 0\" rpy=\"0 0 0\"/>", xml);
            Assert.Contains("<mass value=\"1\"/>", xml);
            Assert.Contains("<inertia ixx=\"1\" ixy=\"0\" ixz=\"0\" iyy=\"1\" iyz=\"0\" izz=\"1\"/>", xml);
            Assert.Contains("<mesh filename=\"package://test_pkg/meshes/base_link.dae\"/>", xml);
            Assert.Contains("<mesh filename=\"package://test_pkg/meshes/base_link_collision.stl\"/>", xml);
            Assert.Contains("</link>", xml);
        }

        [Fact]
        public void SerializeBody_LinkWithInertia_FormatsFloatsInvariantly()
        {
            // Confirm CultureInfo.InvariantCulture is applied: use values that
            // would format differently in e.g. de-DE (comma decimal separator).
            var inertia = new Matrix3(1.5, 0.25, 0.0, 0.25, 2.5, 0.0, 0.0, 0.0, 3.5);
            var link = new UrdfLink("arm",
                Mass: 4.25,
                ComLocal: new Vector3(0.1f, 0.2f, 0.3f),
                InertiaAtComLocal: inertia,
                VisualMesh: null,
                CollisionMesh: null,
                VisualMeshFile: "arm.dae",
                CollisionMeshFile: "arm_collision.stl");

            var model = RobotModelBuilder.Build(Meta(), new[] { link }, Array.Empty<UrdfJoint>());
            string xml = UrdfSerializer.SerializeBody(model);

            Assert.Contains("<mass value=\"4.25\"/>", xml);
            Assert.Contains("ixx=\"1.5\"", xml);
            Assert.Contains("ixy=\"0.25\"", xml);
            // Float3 from Vector3 widens cleanly; just verify no commas leaked.
            Assert.DoesNotContain(",", xml);
        }

        [Fact]
        public void SerializeBody_FixedJoint_EmitsExpectedXml()
        {
            var joint = new UrdfJoint("j_fixed", UrdfJointType.Fixed, "base_link", "arm1",
                Pose.Identity, Vector3.UnitZ, null, null, 10.0, 1.0, UrdfCmdInterface.Position);
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1") },
                new[] { joint });
            string xml = UrdfSerializer.SerializeBody(model);

            Assert.Contains("<joint name=\"j_fixed\" type=\"fixed\">", xml);
            Assert.Contains("<parent link=\"base_link\"/>", xml);
            Assert.Contains("<child link=\"arm1\"/>", xml);
            Assert.DoesNotContain("<axis", xml);
            // Fixed joints don't get a <limit> block.
            Assert.DoesNotContain("<limit", xml);
            Assert.Contains("</joint>", xml);
        }

        [Fact]
        public void SerializeBody_RevoluteJoint_EmitsLimitBlock()
        {
            var joint = new UrdfJoint("j_rev", UrdfJointType.Revolute, "base_link", "arm1",
                Pose.Identity, Vector3.UnitZ, -1.5, 1.5, 12.0, 2.0, UrdfCmdInterface.Position);
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1") },
                new[] { joint });
            string xml = UrdfSerializer.SerializeBody(model);

            Assert.Contains("<joint name=\"j_rev\" type=\"revolute\">", xml);
            Assert.Contains("<limit lower=\"-1.5\" upper=\"1.5\" effort=\"12\" velocity=\"2\"/>", xml);
        }

        [Fact]
        public void SerializeBody_ContinuousJoint_OmitsLowerUpperFromLimit()
        {
            var joint = new UrdfJoint("j_cont", UrdfJointType.Continuous, "base_link", "arm1",
                Pose.Identity, Vector3.UnitZ, null, null, 8.0, 4.0, UrdfCmdInterface.Velocity);
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1") },
                new[] { joint });
            string xml = UrdfSerializer.SerializeBody(model);

            Assert.Contains("<joint name=\"j_cont\" type=\"continuous\">", xml);
            Assert.Contains("<limit effort=\"8\" velocity=\"4\"/>", xml);
            // No lower / upper bounds for continuous.
            Assert.DoesNotContain("lower=", xml);
            Assert.DoesNotContain("upper=", xml);
        }

        [Fact]
        public void SerializeBody_RoundTripsAsValidXml()
        {
            var joint = new UrdfJoint("j1", UrdfJointType.Revolute, "base_link", "arm1",
                Pose.Identity, Vector3.UnitZ, -1.0, 1.0, 10.0, 1.0, UrdfCmdInterface.Position);
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1") },
                new[] { joint });
            string body = UrdfSerializer.SerializeBody(model);

            // Wrap in <robot> so it's a single rooted document.
            string doc = "<?xml version=\"1.0\"?>\n<robot name=\"test\">\n" + body + "</robot>\n";
            var parsed = XDocument.Parse(doc);
            Assert.Equal("robot", parsed.Root!.Name.LocalName);
        }

        [Fact]
        public void SerializeBody_PackageNameWithSpecialChars_EscapesProperly()
        {
            // Builder sanitizes most exotic chars away, but the serializer
            // itself must escape whatever survives. Force a name with chars
            // that survive sanitization but still need escaping: there
            // aren't any after sanitization in practice, so instead we
            // pass a meta with a name PackageNameSanitizer leaves alone
            // (already-clean) and a link name containing '&' which the
            // sanitizer doesn't touch (UrdfLink names are not sanitized).
            var link = new UrdfLink("a&b", 1.0, Vector3.Zero, Matrix3.Identity, null, null, "x.dae", "x.stl");
            var model = RobotModelBuilder.Build(Meta(), new[] { link }, Array.Empty<UrdfJoint>());
            string xml = UrdfSerializer.SerializeBody(model);

            // SecurityElement.Escape turns '&' into '&amp;'.
            Assert.Contains("name=\"a&amp;b\"", xml);
            Assert.DoesNotContain("name=\"a&b\"", xml);
        }

        [Fact]
        public void SerializeBody_ByteParity_WithLegacyExpectedBytes()
        {
            // Hand-rolled reference matching the legacy Sw2gzPipeline.BuildUrdfBodyXml
            // output. If this test fails, the serializer's formatting drifted
            // from the byte-identical contract — golden tests will break too.
            var link = Link("base_link", mass: 1.0,
                visualFile: "base_link.dae", collisionFile: "base_link_collision.stl");
            var model = RobotModelBuilder.Build(Meta("test_pkg"),
                new[] { link }, Array.Empty<UrdfJoint>());

            string nl = Environment.NewLine;
            var expected = new StringBuilder()
                .Append("  <link name=\"base_link\">").Append(nl)
                .Append("    <inertial>").Append(nl)
                .Append("      <origin xyz=\"0 0 0\" rpy=\"0 0 0\"/>").Append(nl)
                .Append("      <mass value=\"1\"/>").Append(nl)
                .Append("      <inertia ixx=\"1\" ixy=\"0\" ixz=\"0\" iyy=\"1\" iyz=\"0\" izz=\"1\"/>").Append(nl)
                .Append("    </inertial>").Append(nl)
                .Append("    <visual><geometry>").Append(nl)
                .Append("      <mesh filename=\"package://test_pkg/meshes/base_link.dae\"/>").Append(nl)
                .Append("    </geometry></visual>").Append(nl)
                .Append("    <collision><geometry>").Append(nl)
                .Append("      <mesh filename=\"package://test_pkg/meshes/base_link_collision.stl\"/>").Append(nl)
                .Append("    </geometry></collision>").Append(nl)
                .Append("  </link>").Append(nl)
                .ToString();

            string actual = UrdfSerializer.SerializeBody(model);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SerializeBody_MultipleLinks_OrderPreserved()
        {
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1"), Link("arm2") },
                Array.Empty<UrdfJoint>());
            string xml = UrdfSerializer.SerializeBody(model);

            int i1 = xml.IndexOf("name=\"base_link\"", StringComparison.Ordinal);
            int i2 = xml.IndexOf("name=\"arm1\"", StringComparison.Ordinal);
            int i3 = xml.IndexOf("name=\"arm2\"", StringComparison.Ordinal);
            Assert.True(i1 >= 0 && i2 > i1 && i3 > i2,
                "Links must serialize in input order.");
        }
    }
}
