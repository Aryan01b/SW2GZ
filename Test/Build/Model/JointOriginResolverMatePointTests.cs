/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Unit tests for the mate-point math path of JointOriginResolver.Compute.
The legacy (matePoint == null) path is already covered by
LinkAnchorAndJointTests; here we exercise the mate-point branch added
to fix the joint-pivots-around-wrong-fulcrum bug.
*/
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class JointOriginResolverMatePointTests
    {
        private const float Eps = 1e-5f;

        [Fact]
        [Trait("Category", "Unit")]
        public void NullMatePoint_FallsBackToLegacyByteIdentical()
        {
            // The 3-arg overload and the 4-arg overload-with-null must produce
            // the same Origin + Axis. ChildAnchorOffset must be zero in both.
            var parent = new Pose(new Vector3(1, 0, 0), Quaternion.Identity);
            var child  = new Pose(new Vector3(3, 4, 0), Quaternion.Identity);
            var axis   = new Vector3(0, 0, 1);

            JointOriginResolver.Resolved a = JointOriginResolver.Compute(parent, child, axis);
            JointOriginResolver.Resolved b = JointOriginResolver.Compute(parent, child, axis, null);

            Assert.Equal(a.Origin.Position, b.Origin.Position);
            Assert.Equal(a.Origin.Rotation, b.Origin.Rotation);
            Assert.Equal(a.AxisInJointFrame, b.AxisInJointFrame);
            Assert.Equal(Vector3.Zero, a.ChildAnchorOffset);
            Assert.Equal(Vector3.Zero, b.ChildAnchorOffset);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void MatePoint_OriginPositionEqualsMatePointMinusParentInParentFrame()
        {
            // Parent at origin, no rotation. Child sits anywhere (mesh moves with
            // it). Mate point at (5, 0, 0). The joint origin in parent frame must
            // sit at the mate point.
            var parent = new Pose(Vector3.Zero, Quaternion.Identity);
            var child  = new Pose(new Vector3(10, 0, 0), Quaternion.Identity);
            var mp     = new Vector3(5, 0, 0);

            JointOriginResolver.Resolved r =
                JointOriginResolver.Compute(parent, child, new Vector3(0, 0, 1), mp);

            Assert.InRange(r.Origin.Position.X, 5f - Eps, 5f + Eps);
            Assert.InRange(r.Origin.Position.Y, -Eps, Eps);
            Assert.InRange(r.Origin.Position.Z, -Eps, Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void MatePoint_RotatedParent_PositionExpressedInParentLocalFrame()
        {
            // Parent rotated +90° about Z; mate point sits at world +X = 1.
            // In parent's local frame, world +X is parent -Y. So origin.pos.y == -1.
            Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2));
            var parent = new Pose(Vector3.Zero, q);
            var child  = new Pose(new Vector3(0.5f, 0.5f, 0), Quaternion.Identity);
            var mp     = new Vector3(1, 0, 0);

            JointOriginResolver.Resolved r =
                JointOriginResolver.Compute(parent, child, new Vector3(0, 0, 1), mp);

            Assert.InRange(r.Origin.Position.X, -Eps, Eps);
            Assert.InRange(r.Origin.Position.Y, -1f - Eps, -1f + Eps);
            Assert.InRange(r.Origin.Position.Z, -Eps, Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void MatePoint_ChildAnchorOffset_IsPartOriginMinusMateInChildFrame()
        {
            // Child at (10, 0, 0), no rotation. Mate point at (5, 0, 0). The
            // child part's design origin in the child's local frame sits at
            // (childPos - matePoint) rotated by R_child⁻¹ = (5, 0, 0).
            var parent = new Pose(Vector3.Zero, Quaternion.Identity);
            var child  = new Pose(new Vector3(10, 0, 0), Quaternion.Identity);
            var mp     = new Vector3(5, 0, 0);

            JointOriginResolver.Resolved r =
                JointOriginResolver.Compute(parent, child, new Vector3(0, 0, 1), mp);

            Assert.InRange(r.ChildAnchorOffset.X, 5f - Eps, 5f + Eps);
            Assert.InRange(r.ChildAnchorOffset.Y, -Eps, Eps);
            Assert.InRange(r.ChildAnchorOffset.Z, -Eps, Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void MatePoint_RotatedChild_OffsetExpressedInChildFrame()
        {
            // Child rotated +90° about Z at world (0, 1, 0). Mate point at (0,0,0).
            // (childPos - mp) = (0, 1, 0). In child's local frame, world +Y is
            // child +X (since rotating world by +90° about Z gives X←Y → so
            // inverse rotates Y to +X). Expect ChildAnchorOffset == (1, 0, 0).
            Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2));
            var parent = new Pose(Vector3.Zero, Quaternion.Identity);
            var child  = new Pose(new Vector3(0, 1, 0), q);
            var mp     = Vector3.Zero;

            JointOriginResolver.Resolved r =
                JointOriginResolver.Compute(parent, child, new Vector3(0, 0, 1), mp);

            Assert.InRange(r.ChildAnchorOffset.X, 1f - Eps, 1f + Eps);
            Assert.InRange(r.ChildAnchorOffset.Y, -Eps, Eps);
            Assert.InRange(r.ChildAnchorOffset.Z, -Eps, Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void MatePoint_RotationStillRelativeParentToChild()
        {
            // Mate-point branch must NOT alter origin.Rotation — it still
            // equals parentAnchor.Rot⁻¹ * childAnchor.Rot (rigid-body
            // relative orientation).
            Quaternion qP = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f);
            Quaternion qC = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.5f);
            var parent = new Pose(new Vector3(1, 2, 3), qP);
            var child  = new Pose(new Vector3(4, 5, 6), qC);
            var mp     = new Vector3(7, 8, 9);

            JointOriginResolver.Resolved withMp =
                JointOriginResolver.Compute(parent, child, Vector3.UnitZ, mp);
            JointOriginResolver.Resolved legacy =
                JointOriginResolver.Compute(parent, child, Vector3.UnitZ);

            // Rotations must match between the two paths (only position differs).
            Quaternion a = withMp.Origin.Rotation;
            Quaternion b = legacy.Origin.Rotation;
            Assert.InRange(a.X, b.X - Eps, b.X + Eps);
            Assert.InRange(a.Y, b.Y - Eps, b.Y + Eps);
            Assert.InRange(a.Z, b.Z - Eps, b.Z + Eps);
            Assert.InRange(a.W, b.W - Eps, b.W + Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void MatePoint_AxisStillReExpressedInChildFrame()
        {
            // Axis re-expression is independent of mate point.
            Quaternion qC = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2));
            var parent = new Pose(Vector3.Zero, Quaternion.Identity);
            var child  = new Pose(new Vector3(10, 0, 0), qC);

            JointOriginResolver.Resolved withMp = JointOriginResolver.Compute(
                parent, child, new Vector3(0, 1, 0), new Vector3(5, 0, 0));

            // Same logic as JointOriginResolver_RotatedChild_AxisExpressedInChildFrame
            Assert.InRange(withMp.AxisInJointFrame.X, 1f - Eps, 1f + Eps);
            Assert.InRange(withMp.AxisInJointFrame.Y, -Eps, Eps);
            Assert.InRange(withMp.AxisInJointFrame.Z, -Eps, Eps);
        }
    }
}
