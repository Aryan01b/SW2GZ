/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Unit tests for PoseMath.TwistAngle and PoseMath.SlideDistance — the
swing-twist decomposition + axis-projection helpers the URDF export
pipeline uses to shift SW raw mate limits into URDF-relative limits.

The shift makes URDF joint=0 correspond to the SW current assembly
pose: without it, a hinge whose SW current state happens to sit at the
lower limit ships URDF lower=upper=raw-SW-values + origin.rpy.x =
current-angle, so sliding the joint to its URDF lower drives the child
PAST the real SW lower limit by exactly the current angle.
*/
using System;
using System.Numerics;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Math
{
    public class PoseMathTwistAngleTests
    {
        private const double Eps = 1e-5;

        // ── TwistAngle ────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void TwistAngle_Identity_ReturnsZero()
        {
            Assert.Equal(0.0, PoseMath.TwistAngle(Quaternion.Identity, Vector3.UnitX), Eps);
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(-0.5)]
        [InlineData(1.4646)]   // exact value from full_arm joint 1
        [InlineData(-1.4646)]
        [InlineData(2.5)]
        [InlineData(-2.5)]
        [Trait("Category", "Unit")]
        public void TwistAngle_PureXRotation_ReturnsAngle(double theta)
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)theta);
            Assert.Equal(theta, PoseMath.TwistAngle(q, Vector3.UnitX), 4);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TwistAngle_PureYRotation_ReturnsAngleAroundY()
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
            Assert.Equal(0.7, PoseMath.TwistAngle(q, Vector3.UnitY), 4);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TwistAngle_PureZRotation_ReturnsAngleAroundZ()
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -1.1f);
            Assert.Equal(-1.1, PoseMath.TwistAngle(q, Vector3.UnitZ), 4);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TwistAngle_SwingAroundPerpendicularAxis_ReturnsZero()
        {
            // Rotate around Y, ask for twist around X — the twist component
            // of a pure Y-rotation around X is exactly zero.
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.2f);
            Assert.Equal(0.0, PoseMath.TwistAngle(q, Vector3.UnitX), Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TwistAngle_CombinedSwingTwist_IsolatesTwist()
        {
            // Build q = R_Y(swing) * R_X(twist). The twist component
            // around X should round-trip back exactly.
            var rTwist = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.9f);
            var rSwing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);
            var q = rSwing * rTwist;
            Assert.Equal(0.9, PoseMath.TwistAngle(q, Vector3.UnitX), 3);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TwistAngle_NonNormalizedAxis_NormalisesInternally()
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.6f);
            var fatAxis = new Vector3(3.7f, 0, 0); // same direction, big magnitude
            Assert.Equal(0.6, PoseMath.TwistAngle(q, fatAxis), 4);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TwistAngle_ZeroAxis_ReturnsZero()
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
            Assert.Equal(0.0, PoseMath.TwistAngle(q, Vector3.Zero), Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TwistAngle_NegativeAxis_FlipsSign()
        {
            // Rotation by +θ around +X is the same physical rotation
            // as -θ around -X. TwistAngle should reflect the axis convention.
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
            double around_neg_X = PoseMath.TwistAngle(q, -Vector3.UnitX);
            Assert.Equal(-0.5, around_neg_X, 4);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TwistAngle_Around_45_Degree_Axis_RecoversRotation()
        {
            // Rotation around an oblique axis (1,1,0)/sqrt(2) by π/4.
            var axis = Vector3.Normalize(new Vector3(1, 1, 0));
            float theta = 0.785398f; // π/4
            var q = Quaternion.CreateFromAxisAngle(axis, theta);
            Assert.Equal(theta, PoseMath.TwistAngle(q, axis), 4);
        }

        // ── SlideDistance ─────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void SlideDistance_AlignedWithAxis_ReturnsMagnitude()
        {
            Assert.Equal(2.5, PoseMath.SlideDistance(new Vector3(2.5f, 0, 0), Vector3.UnitX), Eps);
            Assert.Equal(-1.3, PoseMath.SlideDistance(new Vector3(0, -1.3f, 0), Vector3.UnitY), Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void SlideDistance_PerpendicularToAxis_ReturnsZero()
        {
            Assert.Equal(0.0, PoseMath.SlideDistance(new Vector3(0, 4.2f, 0), Vector3.UnitX), Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void SlideDistance_MixedComponents_ReturnsAlongAxisOnly()
        {
            // Position has 0.6 along X and 5.0 perpendicular → result = 0.6.
            var pos = new Vector3(0.6f, 5.0f, 2.0f);
            Assert.Equal(0.6, PoseMath.SlideDistance(pos, Vector3.UnitX), Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void SlideDistance_NonNormalizedAxis_NormalisesInternally()
        {
            // 2.5 m along +X, axis with magnitude 3 → still 2.5 m.
            var pos = new Vector3(2.5f, 0, 0);
            var fatAxis = new Vector3(3, 0, 0);
            Assert.Equal(2.5, PoseMath.SlideDistance(pos, fatAxis), Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void SlideDistance_ZeroAxis_ReturnsZero()
        {
            Assert.Equal(0.0, PoseMath.SlideDistance(new Vector3(1, 2, 3), Vector3.Zero), Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void SlideDistance_NegativeAxis_FlipsSign()
        {
            // 2.5 m along +X, asking for projection onto -X → -2.5.
            Assert.Equal(-2.5, PoseMath.SlideDistance(new Vector3(2.5f, 0, 0), -Vector3.UnitX), Eps);
        }

        // ── End-to-end: the full_arm joint-1 scenario ─────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void FullArm_Joint1_TwistShift_LinesUpLimits()
        {
            // Reproduces the exported full_arm.urdf joint 1 state:
            //   origin.rpy.x = -1.464562 (SW assembly modelled at lower limit)
            //   axis ≈ +X
            //   SW raw limits: lower = -1.464562, upper = +1.677030
            //
            // After the limit shift, URDF should hand the user joint=0 at the
            // SW current pose and a range that maps back to SW's true limits.
            double swLower = -1.46456264909661;
            double swUpper =  1.67703000449407;
            float  swCurrent = -1.464562703846f;

            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, swCurrent);
            double twist = PoseMath.TwistAngle(q, Vector3.UnitX);
            Assert.Equal(swCurrent, twist, 5);

            double urdfLower = swLower - twist;
            double urdfUpper = swUpper - twist;

            // joint = 0 at SW current pose
            Assert.Equal(0.0, urdfLower, 4);
            // joint = π takes child to SW upper limit
            Assert.Equal(System.Math.PI, urdfUpper, 3);
        }
    }
}
