/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure-math tests for SW2GZ.Math.CylinderTransform.

The 16-double layout that SolidWorks Component2.Transform2.ArrayData uses:
  [0..8]  rotation 3x3 row-major   ( m00 m01 m02   m10 m11 m12   m20 m21 m22 )
  [9..11] translation              ( tx  ty  tz )
  [12]    scale  (ignored here)
  [13..15] padding

cp[0..2] is a part-local point on the cylinder axis; cp[3..5] is the unit
axis direction in the same frame. After TransformCylinderToAssembly:
  origin_asm = R · cp_origin + t
  dir_asm    = R · cp_dir         (rotation only — directions don't translate)
*/
using System;
using System.Numerics;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Math
{
    public class CylinderTransformTests
    {
        private const float Tol = 1e-5f;

        private static double[] Identity16()
        {
            return new double[]
            {
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
                0, 0, 0,
                1, 0, 0, 0,
            };
        }

        // Row-major rotation about Z by `radians`, translation `t`.
        private static double[] Make(double[] rot3x3, double tx, double ty, double tz)
        {
            return new double[]
            {
                rot3x3[0], rot3x3[1], rot3x3[2],
                rot3x3[3], rot3x3[4], rot3x3[5],
                rot3x3[6], rot3x3[7], rot3x3[8],
                tx, ty, tz,
                1, 0, 0, 0,
            };
        }

        private static double[] RotZ(double rad)
        {
            double c = System.Math.Cos(rad), s = System.Math.Sin(rad);
            return new double[]
            {
                c, -s, 0,
                s,  c, 0,
                0,  0, 1,
            };
        }

        private static double[] RotX(double rad)
        {
            double c = System.Math.Cos(rad), s = System.Math.Sin(rad);
            return new double[]
            {
                1, 0, 0,
                0, c, -s,
                0, s,  c,
            };
        }

        private static double[] RotY(double rad)
        {
            double c = System.Math.Cos(rad), s = System.Math.Sin(rad);
            return new double[]
            {
                 c, 0, s,
                 0, 1, 0,
                -s, 0, c,
            };
        }

        private static void AssertVec(Vector3 expected, Vector3 actual)
        {
            Assert.True(System.Math.Abs(expected.X - actual.X) < Tol, $"X expected {expected.X} got {actual.X}");
            Assert.True(System.Math.Abs(expected.Y - actual.Y) < Tol, $"Y expected {expected.Y} got {actual.Y}");
            Assert.True(System.Math.Abs(expected.Z - actual.Z) < Tol, $"Z expected {expected.Z} got {actual.Z}");
        }

        [Fact]
        public void Identity_PassesOriginAndDirThrough()
        {
            var cp = new double[] { 0.1, 0.2, 0.3, 0, 0, 1, 0.005 };
            var (o, d) = CylinderTransform.TransformCylinderToAssembly(Identity16(), cp);
            AssertVec(new Vector3(0.1f, 0.2f, 0.3f), o);
            AssertVec(new Vector3(0, 0, 1), d);
        }

        [Fact]
        public void TranslationOnly_OffsetsOriginNotDirection()
        {
            var xf = Make(new double[] { 1,0,0, 0,1,0, 0,0,1 }, 0.5, -0.25, 1.0);
            var cp = new double[] { 0.1, 0.2, 0.3, 1, 0, 0, 0 };
            var (o, d) = CylinderTransform.TransformCylinderToAssembly(xf, cp);
            AssertVec(new Vector3(0.6f, -0.05f, 1.3f), o);
            AssertVec(new Vector3(1, 0, 0), d);
        }

        [Fact]
        public void RotateZ_90deg_RotatesXAxisToY()
        {
            var xf = Make(RotZ(System.Math.PI / 2), 0, 0, 0);
            var cp = new double[] { 1, 0, 0, 1, 0, 0, 0 };
            var (o, d) = CylinderTransform.TransformCylinderToAssembly(xf, cp);
            AssertVec(new Vector3(0, 1, 0), o);
            AssertVec(new Vector3(0, 1, 0), d);
        }

        [Fact]
        public void RotateX_90deg_RotatesYAxisToZ()
        {
            var xf = Make(RotX(System.Math.PI / 2), 0, 0, 0);
            var cp = new double[] { 0, 1, 0, 0, 1, 0, 0 };
            var (o, d) = CylinderTransform.TransformCylinderToAssembly(xf, cp);
            AssertVec(new Vector3(0, 0, 1), o);
            AssertVec(new Vector3(0, 0, 1), d);
        }

        [Fact]
        public void RotateY_90deg_RotatesZAxisToX()
        {
            var xf = Make(RotY(System.Math.PI / 2), 0, 0, 0);
            var cp = new double[] { 0, 0, 1, 0, 0, 1, 0 };
            var (o, d) = CylinderTransform.TransformCylinderToAssembly(xf, cp);
            AssertVec(new Vector3(1, 0, 0), o);
            AssertVec(new Vector3(1, 0, 0), d);
        }

        [Fact]
        public void CombinedRotationAndTranslation()
        {
            // Rotate part by 90° about Z, then translate (1, 2, 3). Part-local
            // axis point (1, 0, 0) → after R becomes (0, 1, 0) → + t = (1, 3, 3).
            // Direction (1, 0, 0) → (0, 1, 0) (rotation only).
            var xf = Make(RotZ(System.Math.PI / 2), 1, 2, 3);
            var cp = new double[] { 1, 0, 0, 1, 0, 0, 0 };
            var (o, d) = CylinderTransform.TransformCylinderToAssembly(xf, cp);
            AssertVec(new Vector3(1, 3, 3), o);
            AssertVec(new Vector3(0, 1, 0), d);
        }

        [Fact]
        public void NullOrShortArrays_ReturnZero()
        {
            var cp = new double[] { 0, 0, 0, 0, 0, 1, 0 };
            var (o1, d1) = CylinderTransform.TransformCylinderToAssembly(null, cp);
            AssertVec(Vector3.Zero, o1);
            AssertVec(Vector3.Zero, d1);

            var (o2, d2) = CylinderTransform.TransformCylinderToAssembly(Identity16(), null);
            AssertVec(Vector3.Zero, o2);
            AssertVec(Vector3.Zero, d2);

            var (o3, d3) = CylinderTransform.TransformCylinderToAssembly(new double[] { 1, 0, 0 }, cp);
            AssertVec(Vector3.Zero, o3);
            AssertVec(Vector3.Zero, d3);
        }

        [Fact]
        public void NonUnitDirection_IsNormalized()
        {
            // Spec says direction should be a unit vector but defensively
            // CylinderTransform normalises so downstream consumers don't have
            // to. Verify here.
            var cp = new double[] { 0, 0, 0, 3, 4, 0, 0 };
            var (_, d) = CylinderTransform.TransformCylinderToAssembly(Identity16(), cp);
            Assert.True(System.Math.Abs(d.Length() - 1f) < Tol);
        }
    }
}
