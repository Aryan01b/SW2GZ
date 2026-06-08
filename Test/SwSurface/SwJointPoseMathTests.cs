/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

D3 — the pure half of SwJointPoseReader: Localize(parentGlobal, childGlobal)
expresses the child's global pose in the parent's frame, i.e. the URDF
joint <origin> that maps parent link frame to child link frame.

Hand-computed cases here pin the math against parent-frame translations,
parent-frame rotations, and combinations of the two — covering the
parent-CS-world flow that ports upstream LocalizeJoint into the SW2GZ
pipeline.
*/
using System;
using System.Numerics;
using SW2GZ.Math;
using SW2GZ.SwSurface;
using Xunit;

namespace SW2GZ.Test.SwSurface
{
    public class SwJointPoseMathTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void IdentityParent_ReturnsChildVerbatim()
        {
            var child = new Pose(new Vector3(0.3f, 0f, 0f), Quaternion.Identity);
            Pose r = SwJointPoseMath.Localize(Pose.Identity, child);

            Assert.Equal(child.Position, r.Position);
            Assert.True(QuatNearlyEqual(child.Rotation, r.Rotation, 1e-5f));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TranslatedParent_SubtractsTranslation()
        {
            // Parent at (1, 0, 0), child at (1.3, 0, 0). In parent's frame the
            // child sits at (0.3, 0, 0) — that is the URDF joint <origin> xyz.
            var parent = new Pose(new Vector3(1f, 0f, 0f), Quaternion.Identity);
            var child  = new Pose(new Vector3(1.3f, 0f, 0f), Quaternion.Identity);
            Pose r = SwJointPoseMath.Localize(parent, child);

            Assert.Equal(new Vector3(0.3f, 0f, 0f), r.Position, EqualityComparer);
            Assert.True(QuatNearlyEqual(Quaternion.Identity, r.Rotation, 1e-5f));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ParentRotated90DegZ_ChildAt100inWorldIs010inParent()
        {
            // Parent frame is rotated +90° about world Z (i.e. parent's +X axis
            // points along world +Y). A child at world (1, 0, 0) therefore sits
            // at parent-frame (0, -1, 0): going back from world to parent-frame
            // means the inverse rotation, which sends world +X to parent's -Y.
            var rz90 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2));
            var parent = new Pose(Vector3.Zero, rz90);
            var child  = new Pose(new Vector3(1f, 0f, 0f), Quaternion.Identity);

            Pose r = SwJointPoseMath.Localize(parent, child);

            Assert.Equal(new Vector3(0f, -1f, 0f), r.Position, EqualityComparer);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void RoundTripCompose_RecoversChild()
        {
            // Compose(parent, localize(parent, child)) == child for any inputs.
            var parent = new Pose(new Vector3(0.1f, 0.2f, 0.3f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f));
            var child = new Pose(new Vector3(1.5f, -0.4f, 0.9f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f));

            Pose rel = SwJointPoseMath.Localize(parent, child);
            Pose recovered = PoseMath.Compose(parent, rel);

            Assert.Equal(child.Position.X, recovered.Position.X, 4);
            Assert.Equal(child.Position.Y, recovered.Position.Y, 4);
            Assert.Equal(child.Position.Z, recovered.Position.Z, 4);
            Assert.True(QuatNearlyEqual(child.Rotation, recovered.Rotation, 1e-4f));
        }

        private static bool QuatNearlyEqual(Quaternion a, Quaternion b, float eps)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            return System.Math.Abs(System.Math.Abs(dot) - 1f) < eps;
        }

        // Vector3 equality with a small tolerance.
        private static readonly System.Collections.Generic.IEqualityComparer<Vector3> EqualityComparer =
            new Vec3Eq();

        private sealed class Vec3Eq : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            public bool Equals(Vector3 a, Vector3 b) =>
                System.Math.Abs(a.X - b.X) < 1e-4f &&
                System.Math.Abs(a.Y - b.Y) < 1e-4f &&
                System.Math.Abs(a.Z - b.Z) < 1e-4f;
            public int GetHashCode(Vector3 v) => v.GetHashCode();
        }
    }
}
