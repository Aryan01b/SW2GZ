using System;

namespace SW2GZ.Math
{
    public readonly struct Matrix3
    {
        public double M11 { get; } public double M12 { get; } public double M13 { get; }
        public double M21 { get; } public double M22 { get; } public double M23 { get; }
        public double M31 { get; } public double M32 { get; } public double M33 { get; }

        public Matrix3(double m11, double m12, double m13,
                       double m21, double m22, double m23,
                       double m31, double m32, double m33)
        {
            M11 = m11; M12 = m12; M13 = m13;
            M21 = m21; M22 = m22; M23 = m23;
            M31 = m31; M32 = m32; M33 = m33;
        }

        public static Matrix3 Identity =>
            new Matrix3(1, 0, 0, 0, 1, 0, 0, 0, 1);

        public static Matrix3 operator +(Matrix3 a, Matrix3 b) =>
            new Matrix3(a.M11 + b.M11, a.M12 + b.M12, a.M13 + b.M13,
                        a.M21 + b.M21, a.M22 + b.M22, a.M23 + b.M23,
                        a.M31 + b.M31, a.M32 + b.M32, a.M33 + b.M33);

        // Transpose: row<->column swap. P3 needs Rᵀ for the R I Rᵀ inertia rotation.
        public Matrix3 Transpose() =>
            new Matrix3(M11, M21, M31,
                        M12, M22, M32,
                        M13, M23, M33);

        // Standard 3x3 matmul. Used by P3 inertia rotation.
        public static Matrix3 operator *(Matrix3 a, Matrix3 b) =>
            new Matrix3(
                a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
                a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
                a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,

                a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
                a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
                a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,

                a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
                a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
                a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33);

        // Cheap sanity helper used by CoordinateConvention.Validate to reject the
        // obvious "all-zero matrix" mistake. Strict orthonormality lives in P3.
        public bool IsZero() =>
            M11 == 0 && M12 == 0 && M13 == 0 &&
            M21 == 0 && M22 == 0 && M23 == 0 &&
            M31 == 0 && M32 == 0 && M33 == 0;
    }
}
