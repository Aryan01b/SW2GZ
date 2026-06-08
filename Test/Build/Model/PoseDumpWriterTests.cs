/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Unit tests for PoseDumpWriter — the export-time diagnostic dump that
captures link anchors, joint origins, raw Transform2.ArrayData, and
the assembly-frame vs child-frame axis pair for every joint.
*/
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class PoseDumpWriterTests
    {
        private sealed class FakeRaw : IComponentRawTransformSource
        {
            private readonly Dictionary<string, double[]> _map = new Dictionary<string, double[]>();
            public FakeRaw Set(string p, double[] d) { _map[p] = d; return this; }
            public double[] GetComponentRawTransform(string partPath) =>
                _map.TryGetValue(partPath, out double[] d) ? d : null;
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Build_LinkSection_IncludesAnchorAndRawArrayData()
        {
            var specs = new[]
            {
                new LinkSpec("base", new[] { "base-1@asm" }),
                new LinkSpec("arm",  new[] { "arm-1@asm" }),
            };
            var anchors = new Dictionary<string, Pose>
            {
                ["base"] = new Pose(new Vector3(0.1f, 0.2f, 0.3f), Quaternion.Identity),
                ["arm"]  = new Pose(new Vector3(1, 2, 3),
                              new Quaternion(0, 0, 0.7071068f, 0.7071068f)),
            };
            var raw = new FakeRaw()
                .Set("base-1@asm", new double[]
                {
                    1, 0, 0,   0, 1, 0,   0, 0, 1,
                    0.1, 0.2, 0.3,
                    1, 0, 0, 0
                })
                .Set("arm-1@asm", new double[]
                {
                    0, -1, 0,  1, 0, 0,  0, 0, 1,
                    1.0, 2.0, 3.0,
                    1, 0, 0, 0
                });

            string s = PoseDumpWriter.Build("my_pkg", specs, anchors,
                joints: System.Array.Empty<UrdfJoint>(),
                jointAxesAssembly: new Dictionary<string, Vector3>(),
                rawSource: raw);

            Assert.Contains("Package: my_pkg", s);
            Assert.Contains("- base", s);
            Assert.Contains("FirstPart: base-1@asm", s);
            Assert.Contains("- arm", s);
            Assert.Contains("FirstPart: arm-1@asm", s);
            // Anchor xyz appears.
            Assert.Contains("0.1", s);
            // Raw 16-double payload appears.
            Assert.Contains("RawArrayData16: [", s);
            // The arm rotation block (0, -1, 0, ...) appears.
            Assert.Contains("0, -1, 0", s);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Build_RawArrayDataUnavailable_DumpsPlaceholder()
        {
            var specs = new[] { new LinkSpec("base", new[] { "base-1@asm" }) };
            var anchors = new Dictionary<string, Pose> { ["base"] = Pose.Identity };

            string s = PoseDumpWriter.Build("p", specs, anchors,
                joints: System.Array.Empty<UrdfJoint>(),
                jointAxesAssembly: new Dictionary<string, Vector3>(),
                rawSource: null);

            Assert.Contains("RawArrayData16: (unavailable)", s);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Build_JointSection_EmitsParentChildAnchorsOriginAxisPair()
        {
            var specs = new[]
            {
                new LinkSpec("a", new[] { "a-1@asm" }),
                new LinkSpec("b", new[] { "b-1@asm" }),
            };
            var anchors = new Dictionary<string, Pose>
            {
                ["a"] = new Pose(new Vector3(0, 0, 0), Quaternion.Identity),
                ["b"] = new Pose(new Vector3(0.3f, 0, 0), Quaternion.Identity),
            };
            var joint = new UrdfJoint(
                Name: "a_b_joint",
                Type: UrdfJointType.Revolute,
                ParentLink: "a",
                ChildLink: "b",
                Origin: new Pose(new Vector3(0.3f, 0, 0), Quaternion.Identity),
                Axis: new Vector3(0, 0, 1),
                LimitLower: -1.5,
                LimitUpper:  1.5,
                LimitEffort: 100,
                LimitVelocity: 1,
                Interface: UrdfCmdInterface.Position);

            var axesAssembly = new Dictionary<string, Vector3>
            {
                ["a_b_joint"] = new Vector3(0, 0, 1),
            };

            string s = PoseDumpWriter.Build("p", specs, anchors,
                joints: new[] { joint },
                jointAxesAssembly: axesAssembly,
                rawSource: null);

            Assert.Contains("a_b_joint [Revolute]", s);
            Assert.Contains("Parent: a", s);
            Assert.Contains("Child:  b", s);
            Assert.Contains("ParentAnchor.xyz:", s);
            Assert.Contains("ChildAnchor.xyz:", s);
            Assert.Contains("Origin.xyz:", s);
            Assert.Contains("Origin.rpy:", s);
            Assert.Contains("AxisAssembly:", s);
            Assert.Contains("AxisChildFrame:", s);
            Assert.Contains("LimitLower: -1.5", s);
            Assert.Contains("LimitUpper: 1.5", s);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Write_CreatesFileWithExpectedContent()
        {
            string tmpDir = Path.Combine(Path.GetTempPath(),
                "sw2gz_posedump_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(tmpDir, "sw2gz_pose_dump.dbg.txt");

                var specs = new[] { new LinkSpec("base", new[] { "base-1@asm" }) };
                var anchors = new Dictionary<string, Pose> { ["base"] = Pose.Identity };

                PoseDumpWriter.Write(path, "pkg", specs, anchors,
                    joints: System.Array.Empty<UrdfJoint>(),
                    jointAxesAssembly: new Dictionary<string, Vector3>(),
                    rawSource: null);

                Assert.True(File.Exists(path));
                string body = File.ReadAllText(path);
                Assert.Contains("SW2GZ Pose Dump", body);
                Assert.Contains("Package: pkg", body);
                Assert.Contains("- base", body);
            }
            finally
            {
                try { if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true); }
                catch { /* best-effort */ }
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Build_MatePointPresent_PrintsAssemblyFramePoint()
        {
            var specs = new[]
            {
                new LinkSpec("a", new[] { "a-1@asm" }),
                new LinkSpec("b", new[] { "b-1@asm" }),
            };
            var anchors = new Dictionary<string, Pose>
            {
                ["a"] = Pose.Identity,
                ["b"] = new Pose(new Vector3(1, 0, 0), Quaternion.Identity),
            };
            var joint = new UrdfJoint(
                "a_b_joint", UrdfJointType.Revolute, "a", "b",
                Pose.Identity, new Vector3(0, 0, 1),
                null, null, 0, 0, UrdfCmdInterface.Position);

            var mp = new Dictionary<string, Vector3?>
            {
                ["a_b_joint"] = new Vector3(0.5f, 0, 0),
            };

            string s = PoseDumpWriter.Build("p", specs, anchors,
                joints: new[] { joint },
                jointAxesAssembly: new Dictionary<string, Vector3>(),
                rawSource: null,
                matePointsByJoint: mp);

            Assert.Contains("MatePointAssembly: 0.5 0 0", s);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Build_MatePointAbsent_PrintsNonePlaceholder()
        {
            var specs = new[]
            {
                new LinkSpec("a", new[] { "a-1@asm" }),
                new LinkSpec("b", new[] { "b-1@asm" }),
            };
            var anchors = new Dictionary<string, Pose>
            {
                ["a"] = Pose.Identity,
                ["b"] = Pose.Identity,
            };
            var joint = new UrdfJoint(
                "a_b_joint", UrdfJointType.Fixed, "a", "b",
                Pose.Identity, new Vector3(0, 0, 1),
                null, null, 0, 0, UrdfCmdInterface.Position);

            string s = PoseDumpWriter.Build("p", specs, anchors,
                joints: new[] { joint },
                jointAxesAssembly: new Dictionary<string, Vector3>(),
                rawSource: null);

            Assert.Contains("MatePointAssembly: (none)", s);
        }
    }
}
