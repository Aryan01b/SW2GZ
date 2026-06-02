using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class JointGraphBuilderTests
    {
        private static UrdfLink L(string n) =>
            new UrdfLink(n, 1, Vector3.Zero, Matrix3.Identity, null, null, "", "");

        private static MateSpec Revolute(string name, string parent, string child) =>
            new MateSpec(name, MateKind.Revolute, Pose.Identity, Vector3.UnitZ,
                -1.0, 1.0, 10, 1.0, UrdfCmdInterface.Position, parent, child);

        private static MateSpec Fixed(string name, string parent, string child) =>
            new MateSpec(name, MateKind.Fixed, Pose.Identity, Vector3.UnitZ,
                null, null, 0, 0, UrdfCmdInterface.Effort, parent, child);

        [Fact]
        public void Build_TwoLinksOneRevolute_OneJointCorrectRootAndConnectivity()
        {
            var links = new[] { L("base"), L("arm") };
            var mates = new[] { Revolute("j1", "base", "arm") };

            var (joints, root, warnings) = JointGraphBuilder.Build(links, mates);

            Assert.Single(joints);
            Assert.Equal("base", joints[0].ParentLink);
            Assert.Equal("arm", joints[0].ChildLink);
            Assert.Equal(UrdfJointType.Revolute, joints[0].Type);
            Assert.Equal("base", root);
            Assert.Empty(warnings);
        }

        [Fact]
        public void Build_ThreeLinkChain_TwoJointsOrderedBaseFirst()
        {
            var links = new[] { L("base"), L("l1"), L("l2") };
            // Intentionally out of order to prove parents-first ordering.
            var mates = new[]
            {
                Revolute("j2", "l1", "l2"),
                Revolute("j1", "base", "l1"),
            };

            var (joints, root, warnings) = JointGraphBuilder.Build(links, mates);

            Assert.Equal(2, joints.Count);
            Assert.Equal("base", root);
            // base->l1 must precede l1->l2.
            Assert.Equal("j1", joints[0].Name);
            Assert.Equal("j2", joints[1].Name);
            Assert.Empty(warnings);
        }

        [Fact]
        public void Build_UnknownLink_SkippedWithWarning()
        {
            var links = new[] { L("base"), L("arm") };
            var mates = new[] { Revolute("j1", "base", "ghost") };

            var (joints, root, warnings) = JointGraphBuilder.Build(links, mates);

            Assert.Empty(joints);
            Assert.Contains(warnings, w => w.Contains("unknown") && w.Contains("ghost"));
            // base and arm both childless -> multiple roots warning expected too.
            Assert.Contains(warnings, w => w.Contains("Multiple root"));
        }

        [Fact]
        public void Build_SelfLoop_SkippedWithWarning()
        {
            var links = new[] { L("base"), L("arm") };
            var mates = new[] { Revolute("loop", "base", "base") };

            var (joints, _root, warnings) = JointGraphBuilder.Build(links, mates);

            Assert.Empty(joints);
            Assert.Contains(warnings, w => w.Contains("itself"));
        }

        [Fact]
        public void Build_MultipleRoots_WarningSurfaced()
        {
            // Two disconnected pairs -> two distinct roots.
            var links = new[] { L("a"), L("b"), L("c"), L("d") };
            var mates = new[]
            {
                Revolute("j1", "a", "b"),
                Revolute("j2", "c", "d"),
            };

            var (joints, root, warnings) = JointGraphBuilder.Build(links, mates);

            Assert.Equal(2, joints.Count);
            Assert.Contains(warnings, w => w.Contains("Multiple root"));
            // Root must be one of the two parent links.
            Assert.True(root == "a" || root == "c");
        }

        [Fact]
        public void Build_FixedMate_ProducesFixedJoint()
        {
            var links = new[] { L("base"), L("plate") };
            var mates = new[] { Fixed("weld", "base", "plate") };

            var (joints, root, warnings) = JointGraphBuilder.Build(links, mates);

            Assert.Single(joints);
            Assert.Equal(UrdfJointType.Fixed, joints[0].Type);
            Assert.Equal("base", root);
            Assert.Empty(warnings);
        }

        [Fact]
        public void Build_NoMates_NoJointsAndDeterministicRoot()
        {
            var links = new[] { L("only") };
            var (joints, root, warnings) = JointGraphBuilder.Build(links, System.Array.Empty<MateSpec>());

            Assert.Empty(joints);
            Assert.Equal("only", root);
            Assert.Empty(warnings);
        }

        [Fact]
        public void Build_NullInputs_GracefulEmpty()
        {
            var (joints, root, warnings) = JointGraphBuilder.Build(null, null);
            Assert.Empty(joints);
            Assert.Equal(string.Empty, root);
        }
    }
}
