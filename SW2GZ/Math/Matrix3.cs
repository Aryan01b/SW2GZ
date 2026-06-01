using System;
using System.Numerics;

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
        // obvious "all-zero matrix" mistake. Orthonormality is checked separately
        // via IsApproximatelyOrthonormal (P3).
        public bool IsZero() =>
            M11 == 0 && M12 == 0 && M13 == 0 &&
            M21 == 0 && M22 == 0 && M23 == 0 &&
            M31 == 0 && M32 == 0 && M33 == 0;

        // Multiply this matrix (treated as row-by-column) by a column vector.
        // Used by P3 InertialAggregator to rotate per-part COM offsets into
        // the assembly frame before applying parallel-axis. Vector3 components
        // are float, but the multiply is performed in double precision and
        // narrowed at the end to match Vector3's storage type.
        public Vector3 Mul(Vector3 v)
        {
            double x = M11 * v.X + M12 * v.Y + M13 * v.Z;
            double y = M21 * v.X + M22 * v.Y + M23 * v.Z;
            double z = M31 * v.X + M32 * v.Y + M33 * v.Z;
            return new Vector3((float)x, (float)y, (float)z);
        }

        // Build a 3x3 rotation matrix from a unit quaternion (x, y, z, w).
        // Right-handed, column-vector convention (matches System.Numerics).
        // Quaternion is normalized defensively — many sources return unit
        // quaternions but rounding errors creep in. Throws if |q| == 0
        // because there is no meaningful rotation to extract.
        public static Matrix3 FromQuaternion(Quaternion q)
        {
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            double n2 = x * x + y * y + z * z + w * w;
            if (n2 == 0.0)
                throw new ArgumentException("Cannot derive rotation matrix from a zero quaternion.", nameof(q));
            double inv = 1.0 / System.Math.Sqrt(n2);
            x *= inv; y *= inv; z *= inv; w *= inv;

            double xx = x * x, yy = y * y, zz = z * z;
            double xy = x * y, xz = x * z, yz = y * z;
            double wx = w * x, wy = w * y, wz = w * z;

            return new Matrix3(
                1.0 - 2.0 * (yy + zz),       2.0 * (xy - wz),             2.0 * (xz + wy),
                2.0 * (xy + wz),             1.0 - 2.0 * (xx + zz),       2.0 * (yz - wx),
                2.0 * (xz - wy),             2.0 * (yz + wx),             1.0 - 2.0 * (xx + yy));
        }

        // Returns true iff R Rᵀ is within `tolerance` of the 3x3 identity
        // (per-entry absolute deviation summed). Used by CoordinateConvention.Validate
        // to reject scale-only or skew transforms that pretend to be rotations.
        public bool IsApproximatelyOrthonormal(double tolerance = 1e-6)
        {
            var p = this * this.Transpose();
            double d = 0.0;
            d += System.Math.Abs(p.M11 - 1.0);
            d += System.Math.Abs(p.M12);
            d += System.Math.Abs(p.M13);
            d += System.Math.Abs(p.M21);
            d += System.Math.Abs(p.M22 - 1.0);
            d += System.Math.Abs(p.M23);
            d += System.Math.Abs(p.M31);
            d += System.Math.Abs(p.M32);
            d += System.Math.Abs(p.M33 - 1.0);
            return d < tolerance;
        }
    }
}
