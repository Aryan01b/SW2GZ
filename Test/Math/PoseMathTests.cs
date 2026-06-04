/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Unit tests for the Pose composition / inversion / transform helpers
that drive the URDF link-anchor pipeline.
*/
using System;
using System.Numerics;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Math
{
    public class PoseMathTests
    {
        private const float Eps = 1e-5f;

        private static void Equal(Vector3 a, Vector3 b, float eps = Eps)
        {
            Assert.InRange(a.X, b.X - eps, b.X + eps);
            Assert.InRange(a.Y, b.Y - eps, b.Y + eps);
            Assert.InRange(a.Z, b.Z - eps, b.Z + eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Identity_RoundTrip_NoOp()
        {
            Pose id = Pose.Identity;
            Vector3 p = new Vector3(1.2f, -3.4f, 5.6f);

            Equal(p, PoseMath.TransformPoint(id, p));
            Equal(p, PoseMath.TransformVector(id, p));
            Equal(p, PoseMath.TransformPoint(PoseMath.Inverse(id), p));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Inverse_CancelsCompose_OverArbitraryPose()
        {
            var pose = new Pose(
                new Vector3(2, -1, 3),
                Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), (float)(System.Math.PI / 6)));
            Vector3 p = new Vector3(4, 5, 6);

            Vector3 forward = PoseMath.TransformPoint(pose, p);
            Vector3 back = PoseMath.TransformPoint(PoseMath.Inverse(pose), forward);
            Equal(p, back, eps: 1e-4f);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Compose_LeftIsAppliedSecond()
        {
            var a = new Pose(new Vector3(1, 0, 0),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2)));
            var b = new Pose(new Vector3(0, 2, 0), Quaternion.Identity);

            Vector3 p = Vector3.Zero;
            Vector3 expected = PoseMath.TransformPoint(a, PoseMath.TransformPoint(b, p));
            Vector3 actual = PoseMath.TransformPoint(PoseMath.Compose(a, b), p);
            Equal(expected, actual);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Relative_EqualsInverseTimesB()
        {
            var a = new Pose(new Vector3(1, 2, 3),
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.7f));
            var b = new Pose(new Vector3(4, 5, 6),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.3f));

            // Relative(a, b) takes a point and: first applies b, then a⁻¹.
            Pose rel = PoseMath.Relative(a, b);
            Vector3 p = new Vector3(0.5f, -0.5f, 0.25f);
            Vector3 viaRel = PoseMath.TransformPoint(rel, p);
            Vector3 expected = PoseMath.TransformPoint(PoseMath.Inverse(a),
                                    PoseMath.TransformPoint(b, p));
            Equal(viaRel, expected, eps: 1e-4f);
        }
    }
}
