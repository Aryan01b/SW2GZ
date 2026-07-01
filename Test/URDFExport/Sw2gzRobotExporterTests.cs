/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class Sw2gzRobotExporterTests : IDisposable
    {
        private readonly string _dir;
        public Sw2gzRobotExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sw2gz_robot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private sealed class FakeTess : IMeshTessellator
        {
            public MeshData Tessellate(string n, TessellationLod lod) => new MeshData(
                new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
                new[] { 0, 1, 2 }, null);
        }

        private sealed class FakeMassProps : IMassProperties
        {
            public bool ThrowOnGet;
            public MassProps Get(string componentPathName)
            {
                if (ThrowOnGet) throw new InvalidOperationException("no material");
                return new MassProps(2.5, Vector3.Zero, Matrix3.Identity);
            }
        }

        // Per-component (rotation, translation), so tests can assert the
        // exporter's real relative-joint-pose math against a known answer.
        private sealed class FakePoses : IComponentPoses
        {
            private readonly Dictionary<string, (Matrix3, Vector3)> _poses;
            public FakePoses(Dictionary<string, (Matrix3, Vector3)> poses = null) => _poses = poses;
            public (Matrix3 Rotation, Vector3 Translation) GetPose(string componentPathName) =>
                _poses != null && _poses.TryGetValue(componentPathName, out var p) ? p : (Matrix3.Identity, Vector3.Zero);
        }

        private static Matrix3 RotZ(double radians)
        {
            double c = System.Math.Cos(radians), s = System.Math.Sin(radians);
            return new Matrix3(c, -s, 0, s, c, 0, 0, 0, 1);
        }

        private static List<LinkDef> TwoLinks() => new List<LinkDef>
        {
            new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
            new LinkDef { Name = "arm_link",  ComponentIds = { "arm-1@asm" },  ParentName = "base_link" },
        };

        private Sw2gzExportConfig Cfg() => new Sw2gzExportConfig
        {
            Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
            PackageName = "my_robot",
            RobotLinks = TwoLinks(),
        };

        private XElement UrdfRoot() =>
            XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));

        [Fact]
        public void Export_WritesUrdfWithLinksMeshesAndFixedJoint()
        {
            var rep = Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps(), new FakePoses(), Cfg(), _dir, Matrix3.Identity);
            Assert.False(rep.HasErrors);

            string meshesDir = Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "meshes");
            Assert.True(File.Exists(Path.Combine(meshesDir, "base_link.dae")));
            Assert.True(File.Exists(Path.Combine(meshesDir, "arm_link.dae")));

            XElement root = UrdfRoot();
            Assert.Equal("robot", root.Name.LocalName);
            Assert.Equal("my_robot", (string)root.Attribute("name"));

            var linkNames = root.Elements("link").Select(l => (string)l.Attribute("name")).ToList();
            Assert.Contains("base_link", linkNames);
            Assert.Contains("arm_link", linkNames);

            XElement joint = root.Elements("joint").Single();
            Assert.Equal("base_link_to_arm_link", (string)joint.Attribute("name"));
            Assert.Equal("fixed", (string)joint.Attribute("type"));
            Assert.Equal("base_link", (string)joint.Element("parent").Attribute("link"));
            Assert.Equal("arm_link", (string)joint.Element("child").Attribute("link"));

            XElement armLink = root.Elements("link").Single(l => (string)l.Attribute("name") == "arm_link");
            Assert.Equal("2.5", (string)armLink.Element("inertial").Element("mass").Attribute("value"));
        }

        [Fact]
        public void Export_JointOriginIsRealTranslationDelta_NotIdentity()
        {
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, new Vector3(10, 20, 30)),
                ["arm-1@asm"]  = (Matrix3.Identity, new Vector3(11, 22, 33)),
            };
            Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps(), new FakePoses(poses), Cfg(), _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(1.0, double.Parse(xyz[0]), 3);
            Assert.Equal(2.0, double.Parse(xyz[1]), 3);
            Assert.Equal(3.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_JointRotationIsRealRelativeRotation_NotIdentity()
        {
            // Base identity, arm rotated 90 deg about Z in the assembly — the
            // joint <origin> rpy must carry that ~90 deg yaw, not 0 0 0.
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, Vector3.Zero),
                ["arm-1@asm"]  = (RotZ(System.Math.PI / 2), Vector3.Zero),
            };
            Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps(), new FakePoses(poses), Cfg(), _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] rpy = ((string)joint.Element("origin").Attribute("rpy")).Split(' ');
            Assert.Equal(0.0, double.Parse(rpy[0]), 3);
            Assert.Equal(0.0, double.Parse(rpy[1]), 3);
            Assert.Equal(System.Math.PI / 2, double.Parse(rpy[2]), 3);
        }

        [Fact]
        public void Export_NoLinks_Throws()
        {
            var cfg = Cfg(); cfg.RobotLinks = new List<LinkDef>();
            Assert.Throws<SW2GZ.Exceptions.Sw2gzExportException>(
                () => Sw2gzRobotExporter.Export(
                    new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity));
        }

        [Fact]
        public void Export_MissingMaterial_FallsBackToPlaceholderMassAndWarns()
        {
            var rep = Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps { ThrowOnGet = true }, new FakePoses(), Cfg(), _dir, Matrix3.Identity);

            XElement root = UrdfRoot();
            XElement armLink = root.Elements("link").Single(l => (string)l.Attribute("name") == "arm_link");
            Assert.Equal("0.1", (string)armLink.Element("inertial").Element("mass").Attribute("value"));

            Assert.True(rep.Warnings.Any());
        }

        [Fact]
        public void Export_EmitWorldLink_AddsWorldJointWithRotation()
        {
            var cfg = Cfg(); cfg.EmitWorldLink = true;
            Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir,
                SwToRosRotation.Build(SW2GZ.Build.Model.AxisDirection.PlusY, SW2GZ.Build.Model.AxisDirection.PlusZ));

            XElement root = UrdfRoot();
            Assert.Contains("world", root.Elements("link").Select(l => (string)l.Attribute("name")));
            XElement worldJoint = root.Elements("joint").Single(j => (string)j.Attribute("name") == "world_to_base_link");
            Assert.Equal("fixed", (string)worldJoint.Attribute("type"));
            Assert.Equal("world", (string)worldJoint.Element("parent").Attribute("link"));
            Assert.Equal("base_link", (string)worldJoint.Element("child").Attribute("link"));
        }
    }
}
