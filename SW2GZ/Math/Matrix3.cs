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
    }
}
