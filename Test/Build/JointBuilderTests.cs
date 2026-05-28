using System;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class JointBuilderTests
    {
        private static UrdfLink L(string n) =>
            new UrdfLink(n, 1, Vector3.Zero, Matrix3.Identity, null, null, "", "");

        [Fact]
        public void Build_RevoluteWithLimits_FillsFields()
        {
            var mate = new MateSpec("j1", MateKind.Revolute,
                Pose.Identity, Vector3.UnitZ, -1.0, 1.0, 10, 1.0, UrdfCmdInterface.Position);

            var (joint, warnings) = JointBuilder.Build(mate, L("a"), L("b"));

            Assert.Equal(UrdfJointType.Revolute, joint.Type);
            Assert.Equal(-1.0, joint.LimitLower);
            Assert.Equal(1.0, joint.LimitUpper);
            Assert.Empty(warnings);
        }

        [Fact]
        public void Build_ContinuousWithPositionInterface_WarnsBug10()
        {
            var mate = new MateSpec("j1", MateKind.Continuous,
                Pose.Identity, Vector3.UnitZ, null, null, 10, 1.0, UrdfCmdInterface.Position);

            var (_, warnings) = JointBuilder.Build(mate, L("a"), L("b"));

            Assert.Contains(warnings, w => w.Contains("continuous") && w.Contains("position"));
        }

        [Fact]
        public void Build_PlumbsParentChildNames()
        {
            var mate = new MateSpec("shoulder", MateKind.Fixed,
                Pose.Identity, Vector3.UnitX, null, null, 0, 0, UrdfCmdInterface.Effort);

            var (joint, _) = JointBuilder.Build(mate, L("base"), L("upper_arm"));

            Assert.Equal("base",      joint.ParentLink);
            Assert.Equal("upper_arm", joint.ChildLink);
            Assert.Equal("shoulder",  joint.Name);
        }

        [Fact]
        public void Build_PlumbsAxisAndOrigin()
        {
            var origin = new Pose(new Vector3(0.1f, 0.2f, 0.3f), Quaternion.Identity);
            var axis   = new Vector3(0, 1, 0);
            var mate   = new MateSpec("elbow", MateKind.Revolute,
                origin, axis, -2.0, 2.0, 5, 0.5, UrdfCmdInterface.Velocity);

            var (joint, _) = JointBuilder.Build(mate, L("upper"), L("lower"));

            Assert.Equal(origin, joint.Origin);
            Assert.Equal(axis,   joint.Axis);
            Assert.Equal(5.0,    joint.LimitEffort);
            Assert.Equal(0.5,    joint.LimitVelocity);
            Assert.Equal(UrdfCmdInterface.Velocity, joint.Interface);
        }

        [Fact]
        public void Build_ContinuousWithVelocityInterface_NoWarning()
        {
            var mate = new MateSpec("wheel", MateKind.Continuous,
                Pose.Identity, Vector3.UnitZ, null, null, 10, 5.0, UrdfCmdInterface.Velocity);

            var (_, warnings) = JointBuilder.Build(mate, L("chassis"), L("wheel_link"));

            Assert.Empty(warnings);
        }

        [Fact]
        public void Build_ThrowsOnNullMate()
        {
            Assert.Throws<ArgumentNullException>(() =>
                JointBuilder.Build(null, L("a"), L("b")));
        }

        [Fact]
        public void Build_ThrowsOnNullParent()
        {
            var mate = new MateSpec("j", MateKind.Fixed, Pose.Identity, Vector3.UnitZ,
                null, null, 0, 0, UrdfCmdInterface.Effort);
            Assert.Throws<ArgumentNullException>(() =>
                JointBuilder.Build(mate, null, L("b")));
        }

        [Fact]
        public void Build_ThrowsOnNullChild()
        {
            var mate = new MateSpec("j", MateKind.Fixed, Pose.Identity, Vector3.UnitZ,
                null, null, 0, 0, UrdfCmdInterface.Effort);
            Assert.Throws<ArgumentNullException>(() =>
                JointBuilder.Build(mate, L("a"), null));
        }
    }
}
