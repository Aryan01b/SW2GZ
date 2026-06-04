/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Unit tests for SwToRosRotation — the SolidWorks→ROS rotation matrix
builder used by Sw2gzPipeline to align the robot's world anchor.
*/
using System;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class SwToRosRotationTests
    {
        private const double Eps = 1e-9;
        private const double HalfPi = System.Math.PI / 2.0;

        [Fact]
        [Trait("Category", "Unit")]
        public void DefaultSwTemplate_PlusYUp_PlusZForward_YieldsExpectedRotation()
        {
            // +Y up, +Z forward is the stock SW template (Aryan-confirmed
            // assembly convention). The rotation should map:
            //   SW +Y → ROS +Z   (up ↔ up)
            //   SW +Z → ROS +X   (forward ↔ forward)
            //   SW +X → ROS +Y   (right ↔ left, so +X_sw is "left" in ROS)
            Matrix3 R = SwToRosRotation.Build(AxisDirection.PlusY, AxisDirection.PlusZ);

            AssertVecEq(0, 0, 1, R.Mul(0, 1, 0));   // up
            AssertVecEq(1, 0, 0, R.Mul(0, 0, 1));   // forward
            AssertVecEq(0, 1, 0, R.Mul(1, 0, 0));   // SW +X → ROS +Y
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void DefaultSwTemplate_RpyMatchesExpected()
        {
            // Roll = 90° (from atan2(M32, M33) = atan2(1, 0))
            // Pitch = 0  (from asin(-M31) = asin(0))
            // Yaw = 90°  (from atan2(M21, M11) = atan2(1, 0))
            (double roll, double pitch, double yaw) =
                SwToRosRotation.BuildRpy(AxisDirection.PlusY, AxisDirection.PlusZ);

            Assert.Equal(HalfPi, roll, 9);
            Assert.Equal(0.0,    pitch, 9);
            Assert.Equal(HalfPi, yaw, 9);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AlreadyRosNative_PlusZUp_PlusXForward_YieldsIdentity()
        {
            // User who modelled directly in REP-103: no rotation needed.
            Matrix3 R = SwToRosRotation.Build(AxisDirection.PlusZ, AxisDirection.PlusX);
            Assert.True(R.IsApproximatelyOrthonormal());

            // Identity → all three input axes map to themselves.
            AssertVecEq(1, 0, 0, R.Mul(1, 0, 0));
            AssertVecEq(0, 1, 0, R.Mul(0, 1, 0));
            AssertVecEq(0, 0, 1, R.Mul(0, 0, 1));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void EveryValidCombinationIsOrthonormalAndRightHanded()
        {
            foreach (AxisDirection up in System.Enum.GetValues(typeof(AxisDirection)))
                foreach (AxisDirection fwd in System.Enum.GetValues(typeof(AxisDirection)))
                {
                    if (up.IsParallelTo(fwd)) continue;
                    Matrix3 R = SwToRosRotation.Build(up, fwd);
                    Assert.True(R.IsApproximatelyOrthonormal(),
                        $"R for up={up.ToShortString()} fwd={fwd.ToShortString()} not orthonormal");
                    Assert.True(R.Determinant() > 0.5,
                        $"R for up={up.ToShortString()} fwd={fwd.ToShortString()} not right-handed");
                }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ParallelUpAndForward_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                SwToRosRotation.Build(AxisDirection.PlusY, AxisDirection.PlusY));
            Assert.Throws<ArgumentException>(() =>
                SwToRosRotation.Build(AxisDirection.PlusZ, AxisDirection.MinusZ));
        }

        private static void AssertVecEq(double x, double y, double z, (double X, double Y, double Z) actual)
        {
            Assert.Equal(x, actual.X, 9);
            Assert.Equal(y, actual.Y, 9);
            Assert.Equal(z, actual.Z, 9);
        }
    }
}
