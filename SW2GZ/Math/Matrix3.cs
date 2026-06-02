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

        // Double-precision variant of Mul used by InertialAggregator to keep
        // sub-mm COM offsets accurate. Avoids the float narrowing in the
        // Vector3 overload — callers can convert at the very end.
        public (double X, double Y, double Z) Mul(double vx, double vy, double vz)
        {
            double x = M11 * vx + M12 * vy + M13 * vz;
            double y = M21 * vx + M22 * vy + M23 * vz;
            double z = M31 * vx + M32 * vy + M33 * vz;
            return (x, y, z);
        }

        // Standard 3x3 determinant by cofactor expansion along the first row.
        // Used by IsApproximatelyOrthonormal to reject reflections (det ≈ -1).
        public double Determinant() =>
            M11 * (M22 * M33 - M23 * M32)
          - M12 * (M21 * M33 - M23 * M31)
          + M13 * (M21 * M32 - M22 * M31);

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

        // Extract (roll, pitch, yaw) from this rotation matrix using the ZYX
        // Tait-Bryan convention (intrinsic Z-Y-X / extrinsic X-Y-Z) with a single
        // gimbal-lock guard at pitch = ±90°. This is the single source of truth
        // for quaternion→rpy: callers compose FromQuaternion(q).ToRpy(). The
        // formula is algebraically identical to the classic quaternion→rpy used by
        // SDF/URDF pose emission:
        //   roll  = atan2(2(wx+yz), 1-2(xx+yy))  ≡ atan2(M32, M33)
        //   pitch = asin(2(wy-xz))               ≡ asin(-M31)
        //   yaw   = atan2(2(wz+xy), 1-2(yy+zz))  ≡ atan2(M21, M11)
        public (double Roll, double Pitch, double Yaw) ToRpy()
        {
            double roll = System.Math.Atan2(M32, M33);

            // sinp ≡ 2(wy - zx) from the classic quaternion formula. Written as
            // -M31 here; adding 0.0 collapses IEEE negative zero to +0.0 so the
            // emitted text matches the prior inlined formula byte-for-byte
            // (e.g. identity rotation yields "0", never "-0").
            double sinp = -M31 + 0.0;
            double pitch = System.Math.Abs(sinp) >= 1
                ? System.Math.CopySign(System.Math.PI / 2, sinp)
                : System.Math.Asin(sinp);

            double yaw = System.Math.Atan2(M21, M11);
            return (roll, pitch, yaw);
        }

        // Returns true iff R Rᵀ is within `tolerance` of the 3x3 identity
        // (per-entry absolute deviation does not exceed tolerance) AND the
        // determinant is positive — rejecting reflections (det ≈ -1) as well
        // as scale-only or skew transforms that pretend to be rotations.
        // Used by CoordinateConvention.Validate.
        public bool IsApproximatelyOrthonormal(double tolerance = 1e-6)
        {
            var p = this * this.Transpose();
            double d = 0.0;
            d = System.Math.Max(d, System.Math.Abs(p.M11 - 1.0));
            d = System.Math.Max(d, System.Math.Abs(p.M12));
            d = System.Math.Max(d, System.Math.Abs(p.M13));
            d = System.Math.Max(d, System.Math.Abs(p.M21));
            d = System.Math.Max(d, System.Math.Abs(p.M22 - 1.0));
            d = System.Math.Max(d, System.Math.Abs(p.M23));
            d = System.Math.Max(d, System.Math.Abs(p.M31));
            d = System.Math.Max(d, System.Math.Abs(p.M32));
            d = System.Math.Max(d, System.Math.Abs(p.M33 - 1.0));
            return d < tolerance && Determinant() > 0.5;
        }
    }
}
