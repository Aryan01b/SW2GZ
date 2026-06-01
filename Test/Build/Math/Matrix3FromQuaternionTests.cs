using System;
using System.Numerics;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Math.Tests
{
    public class Matrix3FromQuaternionTests
    {
        private const double Tol = 1e-9;

        [Fact]
        public void FromQuaternion_Identity_ReturnsIdentity()
        {
            var m = Matrix3.FromQuaternion(Quaternion.Identity);
            Assert.Equal(1.0, m.M11, 9); Assert.Equal(0.0, m.M12, 9); Assert.Equal(0.0, m.M13, 9);
            Assert.Equal(0.0, m.M21, 9); Assert.Equal(1.0, m.M22, 9); Assert.Equal(0.0, m.M23, 9);
            Assert.Equal(0.0, m.M31, 9); Assert.Equal(0.0, m.M32, 9); Assert.Equal(1.0, m.M33, 9);
        }

        [Fact]
        public void FromQuaternion_Rotation90AboutZ_RotatesXto_PlusY()
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2.0));
            var R = Matrix3.FromQuaternion(q);
            var v = R.Mul(new Vector3(1, 0, 0));
            Assert.Equal(0.0, v.X, 5);
            Assert.Equal(1.0, v.Y, 5);
            Assert.Equal(0.0, v.Z, 5);
        }

        [Fact]
        public void FromQuaternion_ZeroQuaternion_Throws()
        {
            var zero = new Quaternion(0, 0, 0, 0);
            Assert.Throws<ArgumentException>(() => Matrix3.FromQuaternion(zero));
        }

        [Fact]
        public void IsApproximatelyOrthonormal_Identity_True()
        {
            Assert.True(Matrix3.Identity.IsApproximatelyOrthonormal());
        }

        [Fact]
        public void IsApproximatelyOrthonormal_TwoX_False()
        {
            // Scale-by-2 matrix: R Rᵀ = diag(4,4,4), clearly not identity.
            var twoI = new Matrix3(2, 0, 0, 0, 2, 0, 0, 0, 2);
            Assert.False(twoI.IsApproximatelyOrthonormal());
        }

        [Fact]
        public void IsApproximatelyOrthonormal_NonIdentityRotation_True()
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
            var R = Matrix3.FromQuaternion(q);
            Assert.True(R.IsApproximatelyOrthonormal());
        }

        [Fact]
        public void Mul_Vector3_Identity_PreservesVector()
        {
            var v = new Vector3(1.5f, -2.25f, 3.0f);
            var r = Matrix3.Identity.Mul(v);
            Assert.Equal(v.X, r.X, 6);
            Assert.Equal(v.Y, r.Y, 6);
            Assert.Equal(v.Z, r.Z, 6);
        }

        [Fact]
        public void Mul_Vector3_RotationApplied()
        {
            // 90deg about Z maps (0,1,0) → (-1, 0, 0)
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2.0));
            var R = Matrix3.FromQuaternion(q);
            var v = R.Mul(new Vector3(0, 1, 0));
            Assert.Equal(-1.0, v.X, 5);
            Assert.Equal(0.0, v.Y, 5);
            Assert.Equal(0.0, v.Z, 5);
        }
    }
}
