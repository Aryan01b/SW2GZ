using System.Numerics;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Math.Tests
{
    public class Matrix3Tests
    {
        [Fact]
        public void Identity_HasOnesOnDiagonal()
        {
            var m = Matrix3.Identity;
            Assert.Equal(1.0, m.M11); Assert.Equal(1.0, m.M22); Assert.Equal(1.0, m.M33);
            Assert.Equal(0.0, m.M12); Assert.Equal(0.0, m.M13); Assert.Equal(0.0, m.M21);
        }

        [Fact]
        public void Add_ComponentWise()
        {
            var a = new Matrix3(1, 2, 3, 4, 5, 6, 7, 8, 9);
            var b = Matrix3.Identity;
            var sum = a + b;
            Assert.Equal(2.0, sum.M11);
            Assert.Equal(6.0, sum.M22);
            Assert.Equal(10.0, sum.M33);
        }
    }
}
