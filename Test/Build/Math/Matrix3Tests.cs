using System.Numerics;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Math.Tests
{
    public class Matrix3Tests
    {
        [Fact]
        public void Identity_HasOnesOnDiagonal_AndZerosOffDiagonal()
        {
            var m = Matrix3.Identity;
            Assert.Equal(1.0, m.M11); Assert.Equal(1.0, m.M22); Assert.Equal(1.0, m.M33);
            Assert.Equal(0.0, m.M12); Assert.Equal(0.0, m.M13);
            Assert.Equal(0.0, m.M21); Assert.Equal(0.0, m.M23);
            Assert.Equal(0.0, m.M31); Assert.Equal(0.0, m.M32);
        }

        [Fact]
        public void Add_ComponentWise_AllNineElements()
        {
            var a = new Matrix3(1, 2, 3, 4, 5, 6, 7, 8, 9);
            var b = Matrix3.Identity;
            var sum = a + b;
            // diagonal increments by 1
            Assert.Equal(2.0,  sum.M11); Assert.Equal(6.0,  sum.M22); Assert.Equal(10.0, sum.M33);
            // off-diagonal unchanged
            Assert.Equal(2.0,  sum.M12); Assert.Equal(3.0,  sum.M13);
            Assert.Equal(4.0,  sum.M21); Assert.Equal(6.0,  sum.M23);
            Assert.Equal(7.0,  sum.M31); Assert.Equal(8.0,  sum.M32);
        }
    }
}
