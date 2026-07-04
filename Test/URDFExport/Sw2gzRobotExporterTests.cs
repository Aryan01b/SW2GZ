/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
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

        private sealed class FakeMultiTess : IMeshTessellator
        {
            private readonly Dictionary<string, MeshData> _meshes;
            public FakeMultiTess(Dictionary<string, MeshData> meshes) => _meshes = meshes;
            public MeshData Tessellate(string n, TessellationLod lod) =>
                _meshes.TryGetValue(n, out MeshData m) ? m : new MeshData(Array.Empty<Vector3>(), Array.Empty<int>(), null);
        }

        private sealed class FakeMultiMassProps : IMassProperties
        {
            private readonly Dictionary<string, MassProps> _masses;
            public FakeMultiMassProps(Dictionary<string, MassProps> masses) => _masses = masses;
            public MassProps Get(string componentPathName) =>
                _masses.TryGetValue(componentPathName, out MassProps m) ? m : new MassProps(0.1, Vector3.Zero, Matrix3.Identity);
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
        public void Export_GrandchildJointOrigin_IsRelativeToItsOwnParent_NotRoot()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
                new LinkDef { Name = "mid_link",  ComponentIds = { "mid-1@asm" },  ParentName = "base_link" },
                new LinkDef { Name = "leaf_link", ComponentIds = { "leaf-1@asm" }, ParentName = "mid_link" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, new Vector3(0, 0, 0)),
                ["mid-1@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
                ["leaf-1@asm"] = (Matrix3.Identity, new Vector3(1, 5, 0)),
            };

            Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement root = XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));
            XElement leafJoint = root.Elements("joint").Single(j => (string)j.Attribute("name") == "mid_link_to_leaf_link");
            Assert.Equal("mid_link", (string)leafJoint.Element("parent").Attribute("link"));

            // leaf is at (1,5,0), its real parent mid_link is at (1,0,0) — the
            // relative offset is (0,5,0). If this were still computed relative
            // to ROOT (0,0,0) instead of mid_link, it would wrongly read (1,5,0).
            string[] xyz = ((string)leafJoint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(0.0, double.Parse(xyz[0]), 3);
            Assert.Equal(5.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_RootDetectedByTreeStructure_NotListPosition()
        {
            // Simulates a post-reroot doc: mid_link is now the actual root
            // (ParentName == ""), but sits at list position [1], not [0] —
            // exactly what LinkTreeView's "Set as base link" produces (it
            // edits ParentName pointers, never reorders Robot.Links).
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "leaf_link", ComponentIds = { "leaf-1@asm" }, ParentName = "mid_link" },
                new LinkDef { Name = "mid_link",  ComponentIds = { "mid-1@asm" },  ParentName = "" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["mid-1@asm"]  = (Matrix3.Identity, new Vector3(5, 0, 0)),
                ["leaf-1@asm"] = (Matrix3.Identity, new Vector3(5, 2, 0)),
            };

            Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement root = XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));
            XElement joint = root.Elements("joint").Single();
            Assert.Equal("mid_link", (string)joint.Element("parent").Attribute("link"));
            Assert.Equal("leaf_link", (string)joint.Element("child").Attribute("link"));

            // leaf (5,2,0) relative to its real parent mid_link (5,0,0) = (0,2,0).
            // If root were still wrongly detected as leaf_link (list position
            // [0]), this would never be computed at all (falls back to 0 0 0).
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(0.0, double.Parse(xyz[0]), 3);
            Assert.Equal(2.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_DanglingParentReference_DoesNotCrash_AndWarns()
        {
            // arm_link's ParentName points at a link name that isn't in
            // RobotLinks at all (e.g. the parent link was deleted but this
            // sibling's ParentName pointer was left stale).
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
                new LinkDef { Name = "arm_link",  ComponentIds = { "arm-1@asm" },  ParentName = "deleted_link" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };

            var rep = Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            Assert.True(rep.Warnings.Any(w => w.Code == "ROBOT.PARENT"));

            XElement root = XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));
            XElement joint = root.Elements("joint").Single(j => (string)j.Attribute("name") == "deleted_link_to_arm_link");
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(0.0, double.Parse(xyz[0]), 3);
            Assert.Equal(0.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
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

        [Fact]
        public void Export_MultiComponentLink_UnionsAllMeshesInLinkReferenceFrame()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
                new LinkDef { Name = "arm_link",  ComponentIds = { "arm-a@asm", "arm-b@asm" }, ParentName = "base_link" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, Vector3.Zero),
                ["arm-a@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
                ["arm-b@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
            };
            var meshA = new MeshData(
                new[] { new Vector3(1, 0, 0), new Vector3(2, 0, 0), new Vector3(1, 1, 0) },
                new[] { 0, 1, 2 }, null);
            var meshB = new MeshData(
                new[] { new Vector3(1, 0, 5), new Vector3(2, 0, 5), new Vector3(1, 1, 5) },
                new[] { 0, 1, 2 }, null);
            var tess = new FakeMultiTess(new Dictionary<string, MeshData>
            {
                ["base-1@asm"] = new MeshData(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) }, new[] { 0, 1, 2 }, null),
                ["arm-a@asm"]  = meshA,
                ["arm-b@asm"]  = meshB,
            });

            Sw2gzRobotExporter.Export(tess, new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            string daePath = Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "meshes", "arm_link.dae");
            Assert.True(File.Exists(daePath));

            XNamespace ns = "http://www.collada.org/2005/11/COLLADASchema";
            XDocument dae = XDocument.Load(daePath);
            XElement posArray = dae.Descendants(ns + "float_array")
                .Single(e => (string)e.Attribute("id") == "g0-pos-array");
            int floatCount = int.Parse((string)posArray.Attribute("count"));

            // Both components' triangles survive the union: 3 verts each, 3
            // floats per vert = 18 total (not 9 — which is what a
            // "first component only" regression would silently produce).
            Assert.Equal(18, floatCount);

            // arm-b's vertices sit at z=5 in its own (identity-rotation,
            // translation (1,0,0)) frame; arm_link's reference frame is
            // arm-a's pose (also (1,0,0), identity) — so after un-baking,
            // arm-b's local vertices should still carry that z=5 offset
            // (proves it was folded into the SAME shared frame as arm-a,
            // not silently dropped or mis-transformed).
            string[] floats = posArray.Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var zValues = new List<double>();
            for (int i = 2; i < floats.Length; i += 3)
                zValues.Add(double.Parse(floats[i], CultureInfo.InvariantCulture));
            Assert.Contains(zValues, z => System.Math.Abs(z - 5.0) < 1e-3);
        }

        [Fact]
        public void Export_MultiComponentLink_CombinesMassOfAllAssignedComponents()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
                new LinkDef { Name = "arm_link",  ComponentIds = { "arm-a@asm", "arm-b@asm" }, ParentName = "base_link" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, Vector3.Zero),
                ["arm-a@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
                ["arm-b@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
            };
            var massProps = new FakeMultiMassProps(new Dictionary<string, MassProps>
            {
                ["base-1@asm"] = new MassProps(9.0, Vector3.Zero, Matrix3.Identity),
                ["arm-a@asm"]  = new MassProps(1.5, Vector3.Zero, Matrix3.Identity),
                ["arm-b@asm"]  = new MassProps(2.5, Vector3.Zero, Matrix3.Identity),
            });

            Sw2gzRobotExporter.Export(new FakeTess(), massProps, new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement root = XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));
            XElement armLink = root.Elements("link").Single(l => (string)l.Attribute("name") == "arm_link");
            double mass = double.Parse((string)armLink.Element("inertial").Element("mass").Attribute("value"), CultureInfo.InvariantCulture);

            // 1.5 + 2.5, not just arm-a's 1.5 (a "first component only"
            // regression would report 1.5, silently dropping arm-b).
            Assert.Equal(4.0, mass, 3);
        }

        [Fact]
        public void Export_MultiComponentLink_UsesEachComponentsOwnPose_NotSharedLinkFrame()
        {
            // arm-a and arm-b sit at DIFFERENT positions (unlike
            // Export_MultiComponentLink_CombinesMassOfAllAssignedComponents
            // above, where both parts share arm_link's own reference pose
            // and so can't distinguish a correct per-part TryGetPose call
            // inside CombineMass from a bug that reused the shared
            // linkR/linkT for every part — both produce the same answer
            // when every part's pose already equals the link frame).
            //
            // <inertial><origin> is hardcoded to "0 0 0" in WriteUrdf (a
            // separate, pre-existing simplification — COM is not written),
            // and mass is pose-invariant, so the only pose-sensitive value
            // that reaches the URDF is the combined inertia tensor: with
            // both parts spread away from arm_link's own frame (arm-a's
            // pose), the parallel-axis contribution is nonzero. A
            // shared-frame bug would instead evaluate every part AT
            // arm_link's own frame (d = 0 for every part), so parallel-axis
            // contributes nothing and izz stays at the parts' own identity
            // inertia (1.0) instead of growing with the offset.
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
                new LinkDef { Name = "arm_link",  ComponentIds = { "arm-a@asm", "arm-b@asm" }, ParentName = "base_link" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, Vector3.Zero),
                ["arm-a@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
                ["arm-b@asm"]  = (Matrix3.Identity, new Vector3(1, 4, 0)),
            };
            var unitInertia = new Matrix3(1, 0, 0, 0, 1, 0, 0, 0, 1);
            var massProps = new FakeMultiMassProps(new Dictionary<string, MassProps>
            {
                ["base-1@asm"] = new MassProps(9.0, Vector3.Zero, Matrix3.Identity),
                ["arm-a@asm"]  = new MassProps(1.0, Vector3.Zero, unitInertia),
                ["arm-b@asm"]  = new MassProps(1.0, Vector3.Zero, unitInertia),
            });

            Sw2gzRobotExporter.Export(new FakeTess(), massProps, new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement root = XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));
            XElement armLink = root.Elements("link").Single(l => (string)l.Attribute("name") == "arm_link");
            double izz = double.Parse((string)armLink.Element("inertial").Element("inertia").Attribute("izz"), CultureInfo.InvariantCulture);

            // arm-a at (1,0,0) == arm_link's own frame (d=0); arm-b at
            // (1,4,0) is offset by (0,4,0) from the combined COM (0,2,0) in
            // world space, i.e. d=2 on each side. izz picks up
            // m*(dx^2+dy^2) = 1*(0+4) = 4 from EACH part's offset from the
            // shared COM at (0,2,0): arm-a contributes 1*(0+4)=4, arm-b
            // contributes 1*(0+4)=4, so combined izz = 1 + 1 + 4 + 4 = 10.
            // A shared-frame bug (both parts evaluated at arm_link's own
            // pose, d=0 for both) would instead report izz = 1 + 1 = 2.
            Assert.True(izz > 5.0, "izz=" + izz + " — expected > 5 (parallel-axis from per-component pose); a shared-frame bug would report izz=2.");
        }

        [Fact]
        public void Export_UsesJointDefType_InsteadOfHardcodedFixed()
        {
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "shoulder", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Continuous },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            Assert.Equal("shoulder", (string)joint.Attribute("name"));
            Assert.Equal("continuous", (string)joint.Attribute("type"));
        }

        [Fact]
        public void Export_WritesAxisRotatedIntoChildLocalFrame()
        {
            // Child rotated 90deg about Z in the assembly; axis set to
            // assembly +X. R_child^T expresses that same world direction in
            // the child's own (locally un-rotated) frame — since the
            // child's local +Y maps to world -X under its own rotation,
            // world +X reads as local -Y here.
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, Vector3.Zero),
                ["arm-1@asm"]  = (RotZ(System.Math.PI / 2), Vector3.Zero),
            };
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute, AxisX = 1, AxisY = 0, AxisZ = 0 },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] xyz = ((string)joint.Element("axis").Attribute("xyz")).Split(' ');
            Assert.Equal(0.0, double.Parse(xyz[0]), 3);
            Assert.Equal(-1.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_WritesLimitElement_ForRevoluteAndPrismatic()
        {
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef
                {
                    Name = "j1", ParentLink = "base_link", ChildLink = "arm_link",
                    Type = UrdfJointType.Prismatic, AxisX = 0, AxisY = 0, AxisZ = 1,
                    LimitLower = -0.5, LimitUpper = 0.5,
                },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            Assert.Equal(-0.5, double.Parse((string)joint.Element("limit").Attribute("lower"), CultureInfo.InvariantCulture), 3);
            Assert.Equal(0.5, double.Parse((string)joint.Element("limit").Attribute("upper"), CultureInfo.InvariantCulture), 3);
        }

        [Fact]
        public void Export_FixedType_EmitsNoAxisOrLimitElements()
        {
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Fixed },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            Assert.Null(joint.Element("axis"));
            Assert.Null(joint.Element("limit"));
        }

        [Fact]
        public void Export_UsesMatePointForOrigin_WhenHasMatePoint()
        {
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute, AxisX = 0, AxisY = 0, AxisZ = 1 },
            };
            cfg.RobotJoints[0].SetMatePoint(new Vector3(2, 3, 4));

            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(2.0, double.Parse(xyz[0]), 3);
            Assert.Equal(3.0, double.Parse(xyz[1]), 3);
            Assert.Equal(4.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_MatePoint_IsExpressedRelativeToParentFrame_LikeOrdinaryOrigin()
        {
            // Parent link translated to (10,0,0) in assembly frame, both
            // identity rotation. A mate point at assembly (12,0,0) should
            // read as (2,0,0) relative to the parent — same parent-relative
            // convention ordinary (non-override) origins already use.
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, new Vector3(10, 0, 0)),
                ["arm-1@asm"]  = (Matrix3.Identity, new Vector3(10, 0, 0)),
            };
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute, AxisX = 0, AxisY = 0, AxisZ = 1 },
            };
            cfg.RobotJoints[0].SetMatePoint(new Vector3(12, 0, 0));

            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(2.0, double.Parse(xyz[0]), 3);
            Assert.Equal(0.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_MeshRebasesAroundMatePoint_WhenHasMatePoint()
        {
            // Regression for the "pivot renders at the wrong point" bug:
            // overriding only the joint <origin> position left the mesh
            // un-baked around the link's raw pose, so mesh and joint frame
            // pointed at two different places in space. FakeTess's first
            // vertex is world (0,0,0); arm_link's own pose (FakePoses
            // default) is identity at the world origin. Un-baked around a
            // mate point at assembly (1,0,0), that vertex must read local
            // (-1,0,0) — proof the mesh follows frameOrigin, not linkT.
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute, AxisX = 0, AxisY = 0, AxisZ = 1 },
            };
            cfg.RobotJoints[0].SetMatePoint(new Vector3(1, 0, 0));

            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            string daePath = Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "meshes", "arm_link.dae");
            XNamespace ns = "http://www.collada.org/2005/11/COLLADASchema";
            XDocument dae = XDocument.Load(daePath);
            XElement posArray = dae.Descendants(ns + "float_array")
                .Single(e => (string)e.Attribute("id") == "g0-pos-array");
            double[] vals = posArray.Value.Split(' ')
                .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(-1.0, vals[0], 3);
            Assert.Equal(0.0, vals[1], 3);
            Assert.Equal(0.0, vals[2], 3);
        }

        [Fact]
        public void Export_NoMatePoint_OriginStaysLinkPoseDerived_AsBefore()
        {
            // Regression guard: a joint with no mate point must be totally
            // unaffected by this task — same as the existing (pre-Phase-2)
            // origin behavior.
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute, AxisX = 0, AxisY = 0, AxisZ = 1 },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(0.0, double.Parse(xyz[0]), 3);
            Assert.Equal(0.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_GrandchildOrigin_IsRelativeToParentsFrameOrigin_NotParentsRawPose()
        {
            // Regression for the live bug where end_link rendered on top of
            // the base_link joint: mid_link has a mate point (1,0,0) that
            // differs from its own raw pose (5,0,0) — its ESTABLISHED frame
            // sits at the mate point. leaf_link (mid_link's child, no mate
            // point of its own) must compute its origin relative to THAT
            // frame, not mid_link's raw pose — (5,3,0) - (1,0,0) = (4,3,0),
            // not the buggy (5,3,0) - (5,0,0) = (0,3,0).
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
                new LinkDef { Name = "mid_link",  ComponentIds = { "mid-1@asm" },  ParentName = "base_link" },
                new LinkDef { Name = "leaf_link", ComponentIds = { "leaf-1@asm" }, ParentName = "mid_link" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
                RobotJoints = new List<JointDef>
                {
                    new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "mid_link", Type = UrdfJointType.Revolute, AxisX = 0, AxisY = 0, AxisZ = 1 },
                    new JointDef { Name = "j2", ParentLink = "mid_link", ChildLink = "leaf_link", Type = UrdfJointType.Revolute, AxisX = 0, AxisY = 0, AxisZ = 1 },
                },
            };
            cfg.RobotJoints[0].SetMatePoint(new Vector3(1, 0, 0));
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, new Vector3(0, 0, 0)),
                ["mid-1@asm"]  = (Matrix3.Identity, new Vector3(5, 0, 0)),
                ["leaf-1@asm"] = (Matrix3.Identity, new Vector3(5, 3, 0)),
            };

            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement root = XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));
            XElement leafJoint = root.Elements("joint").Single(j => (string)j.Attribute("name") == "j2");
            string[] xyz = ((string)leafJoint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(4.0, double.Parse(xyz[0], CultureInfo.InvariantCulture), 3);
            Assert.Equal(3.0, double.Parse(xyz[1], CultureInfo.InvariantCulture), 3);
            Assert.Equal(0.0, double.Parse(xyz[2], CultureInfo.InvariantCulture), 3);
        }
    }
}
