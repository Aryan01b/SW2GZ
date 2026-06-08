/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

D3 guards the row-major + Shepperd's-quaternion conversion that turns a
16-double SW MathTransform.ArrayData payload into a Pose. The same routine
backs both WizardAssemblyWalker.GetComponentPose (component Transform2) and
SwJointPoseReader.GetCsTransform (Reference Coordinate System feature).
*/
using System;
using System.Numerics;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Math
{
    public class MathTransformPoseTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void NullOrShortArray_ReturnsIdentity()
        {
            Assert.Equal(Pose.Identity, MathTransformPose.FromArrayData(null));
            Assert.Equal(Pose.Identity, MathTransformPose.FromArrayData(new double[3]));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void IdentityRotation_TranslationOnly()
        {
            // Row-major identity + (1, 2, 3) translation.
            var data = new double[16] {
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
                1, 2, 3,
                1, 0, 0, 0,
            };
            Pose p = MathTransformPose.FromArrayData(data);
            Assert.Equal(new Vector3(1, 2, 3), p.Position);
            Assert.True(NearlyEqual(p.Rotation, Quaternion.Identity, 1e-5f),
                $"rotation should be identity, got {p.Rotation}");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Rotation90DegAboutZ_ProducesExpectedQuaternion()
        {
            // Row-major Rz(+90°):  [ 0 -1 0 ; 1 0 0 ; 0 0 1 ].
            // Equivalent to SW's row-major Transform2 layout, which is what
            // WizardAssemblyWalker has consumed since v2.1.
            var data = new double[16] {
                0, -1, 0,
                1,  0, 0,
                0,  0, 1,
                0, 0, 0,
                1, 0, 0, 0,
            };
            Pose p = MathTransformPose.FromArrayData(data);

            // Quaternion for +90° about +Z: (0, 0, sin45°, cos45°).
            var expected = new Quaternion(0f, 0f, (float)System.Math.Sin(System.Math.PI / 4),
                                          (float)System.Math.Cos(System.Math.PI / 4));
            Assert.True(NearlyEqual(p.Rotation, expected, 1e-5f),
                $"expected {expected}, got {p.Rotation}");
            Assert.Equal(Vector3.Zero, p.Position);
        }

        // Quaternions q and -q represent the same rotation — accept either.
        private static bool NearlyEqual(Quaternion a, Quaternion b, float eps)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            return System.Math.Abs(System.Math.Abs(dot) - 1f) < eps;
        }
    }
}
