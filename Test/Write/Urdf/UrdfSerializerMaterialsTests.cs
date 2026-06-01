/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P5 — Materials emission tests for UrdfSerializer. Covers both the per-link
<material name="..."/> reference in the body XML and the SerializeMaterialsXacro
helper that produces inc/materials.xacro.
*/
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.Write.Urdf;
using Xunit;

namespace SW2GZ.Test.Write.Urdf
{
    public class UrdfSerializerMaterialsTests
    {
        private static UrdfLink MakeLink(string name) =>
            new UrdfLink(
                name,
                Mass: 1.0,
                ComLocal: Vector3.Zero,
                InertiaAtComLocal: Matrix3.Identity,
                VisualMesh: null,
                CollisionMesh: null,
                VisualMeshFile: $"{name}.dae",
                CollisionMeshFile: $"{name}_collision.stl");

        private static RobotMeta MakeMeta() =>
            new RobotMeta("test_pkg", "a", "a@b", "MIT", CoordinateConvention.Identity);

        [Fact]
        public void SerializeBody_LinkWithMaterial_EmitsNamedMaterialRef()
        {
            var ml = new ModelLink(MakeLink("base"), "steel", null);
            var model = new RobotModel(
                MakeMeta(),
                new[] { ml },
                System.Array.Empty<UrdfJoint>(),
                System.Array.Empty<MaterialDef>(),
                System.Array.Empty<SensorDef>(),
                new ControlSpec(new List<string>(), ControlSpec.DefaultJointStateBroadcaster));

            string xml = UrdfSerializer.SerializeBody(model);

            Assert.Contains("<material name=\"steel\"/>", xml);
            // The material ref lives inside the <visual> block, before </visual>.
            int matIdx = xml.IndexOf("<material name=\"steel\"/>");
            int visualEnd = xml.IndexOf("</visual>");
            Assert.True(matIdx > 0 && matIdx < visualEnd,
                "<material> ref must appear inside the <visual> block.");
        }

        [Fact]
        public void SerializeBody_LinkWithoutMaterial_NoMaterialTag()
        {
            var ml = new ModelLink(MakeLink("base"), null, null);
            var model = new RobotModel(
                MakeMeta(),
                new[] { ml },
                System.Array.Empty<UrdfJoint>(),
                System.Array.Empty<MaterialDef>(),
                System.Array.Empty<SensorDef>(),
                new ControlSpec(new List<string>(), ControlSpec.DefaultJointStateBroadcaster));

            string xml = UrdfSerializer.SerializeBody(model);

            Assert.DoesNotContain("<material", xml);
        }

        [Fact]
        public void SerializeBody_MaterialNameWithSpecialChars_Escaped()
        {
            // Note: in production the name flows through RosNameSanitizer first,
            // but the serializer must still escape defensively.
            var ml = new ModelLink(MakeLink("base"), "ABS&PLA", null);
            var model = new RobotModel(
                MakeMeta(),
                new[] { ml },
                System.Array.Empty<UrdfJoint>(),
                System.Array.Empty<MaterialDef>(),
                System.Array.Empty<SensorDef>(),
                new ControlSpec(new List<string>(), ControlSpec.DefaultJointStateBroadcaster));

            string xml = UrdfSerializer.SerializeBody(model);

            Assert.Contains("<material name=\"ABS&amp;PLA\"/>", xml);
        }

        [Fact]
        public void SerializeMaterialsXacro_Empty_EmitsPlaceholderComment()
        {
            string xml = UrdfSerializer.SerializeMaterialsXacro("pkg",
                System.Array.Empty<MaterialDef>());

            Assert.Contains("<!-- No named materials defined. -->", xml);
            Assert.Contains("<?xml version=\"1.0\"?>", xml);
            Assert.Contains("<robot xmlns:xacro=\"http://www.ros.org/wiki/xacro\">", xml);
            Assert.Contains("</robot>", xml);
        }

        [Fact]
        public void SerializeMaterialsXacro_SingleMaterial_FormatsCorrectly()
        {
            var mats = new[] { new MaterialDef("red", 1.0, 0.0, 0.0, 1.0) };

            string xml = UrdfSerializer.SerializeMaterialsXacro("pkg", mats);

            Assert.Contains("<material name=\"red\">", xml);
            Assert.Contains("<color rgba=\"1 0 0 1\"/>", xml);
            Assert.Contains("</material>", xml);
        }

        [Fact]
        public void SerializeMaterialsXacro_MultipleMaterials_PreservesOrder()
        {
            var mats = new[]
            {
                new MaterialDef("alpha", 0.1, 0.2, 0.3, 1.0),
                new MaterialDef("beta",  0.4, 0.5, 0.6, 1.0),
                new MaterialDef("gamma", 0.7, 0.8, 0.9, 1.0),
            };

            string xml = UrdfSerializer.SerializeMaterialsXacro("pkg", mats);

            int ia = xml.IndexOf("name=\"alpha\"");
            int ib = xml.IndexOf("name=\"beta\"");
            int ig = xml.IndexOf("name=\"gamma\"");
            Assert.True(ia > 0 && ib > ia && ig > ib,
                "Materials must appear in the same order as the input list.");
        }

        [Fact]
        public void SerializeMaterialsXacro_RgbaInvariantCulture_NoCommas()
        {
            // Save the ambient locale; pin to de-DE (comma decimal) for the duration.
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var mats = new[] { new MaterialDef("mid", 0.5, 0.25, 0.75, 1.0) };

                string xml = UrdfSerializer.SerializeMaterialsXacro("pkg", mats);

                Assert.Contains("0.5", xml);
                Assert.Contains("0.25", xml);
                Assert.Contains("0.75", xml);
                Assert.DoesNotContain("0,5", xml);
                Assert.DoesNotContain("0,25", xml);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = prev;
            }
        }
    }
}
