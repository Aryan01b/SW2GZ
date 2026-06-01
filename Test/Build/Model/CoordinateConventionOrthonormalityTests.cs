using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Model.Tests
{
    public class CoordinateConventionOrthonormalityTests
    {
        [Fact]
        public void Validate_Identity_True()
        {
            Assert.True(CoordinateConvention.Identity.Validate());
        }

        [Fact]
        public void Validate_NonOrthonormalMatrix_False()
        {
            // Scale-by-2 is not orthonormal (R Rᵀ = 4I, not I).
            var scale2 = new Matrix3(2, 0, 0, 0, 2, 0, 0, 0, 2);
            var conv = new CoordinateConvention(scale2, 1.0);
            Assert.False(conv.Validate());
        }

        [Fact]
        public void Validate_OrthonormalRotation_True()
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f);
            var R = Matrix3.FromQuaternion(q);
            var conv = new CoordinateConvention(R, 1.0);
            Assert.True(conv.Validate());
        }

        [Fact]
        public void Validate_ZeroLengthScale_False()
        {
            var conv = new CoordinateConvention(Matrix3.Identity, 0.0);
            Assert.False(conv.Validate());
        }

        [Fact]
        public void Validate_ZeroMatrix_False()
        {
            var zero = new Matrix3(0, 0, 0, 0, 0, 0, 0, 0, 0);
            var conv = new CoordinateConvention(zero, 1.0);
            Assert.False(conv.Validate());
        }
    }
}
