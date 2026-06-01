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

        [Fact]
        public void Transpose_SwapsRowsAndColumns()
        {
            var a = new Matrix3(1, 2, 3, 4, 5, 6, 7, 8, 9);
            var t = a.Transpose();
            Assert.Equal(1.0, t.M11); Assert.Equal(4.0, t.M12); Assert.Equal(7.0, t.M13);
            Assert.Equal(2.0, t.M21); Assert.Equal(5.0, t.M22); Assert.Equal(8.0, t.M23);
            Assert.Equal(3.0, t.M31); Assert.Equal(6.0, t.M32); Assert.Equal(9.0, t.M33);
        }

        [Fact]
        public void Transpose_OfIdentity_IsIdentity()
        {
            var t = Matrix3.Identity.Transpose();
            Assert.Equal(1.0, t.M11); Assert.Equal(1.0, t.M22); Assert.Equal(1.0, t.M33);
            Assert.Equal(0.0, t.M12); Assert.Equal(0.0, t.M21);
        }

        [Fact]
        public void Multiply_IdentityIsIdentityElement()
        {
            var a = new Matrix3(1, 2, 3, 4, 5, 6, 7, 8, 9);
            var ai = a * Matrix3.Identity;
            var ia = Matrix3.Identity * a;
            Assert.Equal(a.M11, ai.M11); Assert.Equal(a.M22, ai.M22); Assert.Equal(a.M33, ai.M33);
            Assert.Equal(a.M12, ai.M12); Assert.Equal(a.M21, ai.M21);
            Assert.Equal(a.M11, ia.M11); Assert.Equal(a.M33, ia.M33);
        }

        [Fact]
        public void Multiply_KnownValues_StandardRowByColumn()
        {
            // [[1,2,3],[4,5,6],[7,8,9]] * [[9,8,7],[6,5,4],[3,2,1]]
            var a = new Matrix3(1, 2, 3, 4, 5, 6, 7, 8, 9);
            var b = new Matrix3(9, 8, 7, 6, 5, 4, 3, 2, 1);
            var c = a * b;
            // row0 * col0 = 1*9 + 2*6 + 3*3 = 30
            Assert.Equal(30.0, c.M11);
            // row0 * col1 = 1*8 + 2*5 + 3*2 = 24
            Assert.Equal(24.0, c.M12);
            // row1 * col0 = 4*9 + 5*6 + 6*3 = 84
            Assert.Equal(84.0, c.M21);
            // row2 * col2 = 7*7 + 8*4 + 9*1 = 90
            Assert.Equal(90.0, c.M33);
        }

        [Fact]
        public void IsZero_TrueForAllZeros_FalseOtherwise()
        {
            var z = new Matrix3(0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.True(z.IsZero());
            Assert.False(Matrix3.Identity.IsZero());
            var almost = new Matrix3(0, 0, 0, 0, 0, 0, 0, 0, 1e-300);
            Assert.False(almost.IsZero());
        }
    }
}
