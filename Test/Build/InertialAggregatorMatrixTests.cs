using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class InertialAggregatorMatrixTests
    {
        private static Matrix3 RotZ(double radians)
        {
            double c = System.Math.Cos(radians), s = System.Math.Sin(radians);
            return new Matrix3(c, -s, 0, s, c, 0, 0, 0, 1);
        }

        [Fact]
        public void Combine_Matrix3Overload_MatchesQuaternionOverload_IdentityRotation()
        {
            var p = new MassProps(1.0, Vector3.Zero, Matrix3.Identity);
            var posA = new Vector3(-1, 0, 0);
            var posB = new Vector3(1, 0, 0);

            var quaternionParts = new List<(MassProps, Pose)>
            {
                (p, new Pose(posA, Quaternion.Identity)),
                (p, new Pose(posB, Quaternion.Identity)),
            };
            var matrixParts = new List<(MassProps, Matrix3, Vector3)>
            {
                (p, Matrix3.Identity, posA),
                (p, Matrix3.Identity, posB),
            };

            MassProps viaQuaternion = InertialAggregator.Combine(quaternionParts);
            MassProps viaMatrix3 = InertialAggregator.Combine(matrixParts);

            Assert.Equal(2.0, viaMatrix3.Mass);
            Assert.Equal(viaQuaternion.Mass, viaMatrix3.Mass);
            Assert.Equal(viaQuaternion.ComLocal.X, viaMatrix3.ComLocal.X, 9);
            Assert.Equal(viaQuaternion.ComLocal.Y, viaMatrix3.ComLocal.Y, 9);
            Assert.Equal(viaQuaternion.ComLocal.Z, viaMatrix3.ComLocal.Z, 9);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M11, viaMatrix3.InertiaAtComLocal.M11, 9);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M22, viaMatrix3.InertiaAtComLocal.M22, 9);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M33, viaMatrix3.InertiaAtComLocal.M33, 9);
        }

        [Fact]
        public void Combine_Matrix3Overload_MatchesQuaternionOverload_NonIdentityRotation()
        {
            var inertia = new Matrix3(1.5, 0, 0, 0, 2.0, 0, 0, 0, 2.5);
            var qA = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f);
            var qB = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.7f);
            var posA = new Vector3(0.3f, 0.2f, -0.1f);
            var posB = new Vector3(-0.2f, 0.5f, 0.4f);

            var quaternionParts = new List<(MassProps, Pose)>
            {
                (new MassProps(1.0, Vector3.Zero, inertia), new Pose(posA, qA)),
                (new MassProps(2.0, Vector3.Zero, inertia), new Pose(posB, qB)),
            };
            var matrixParts = new List<(MassProps, Matrix3, Vector3)>
            {
                (new MassProps(1.0, Vector3.Zero, inertia), Matrix3.FromQuaternion(qA), posA),
                (new MassProps(2.0, Vector3.Zero, inertia), Matrix3.FromQuaternion(qB), posB),
            };

            MassProps viaQuaternion = InertialAggregator.Combine(quaternionParts);
            MassProps viaMatrix3 = InertialAggregator.Combine(matrixParts);

            Assert.Equal(viaQuaternion.Mass, viaMatrix3.Mass, 9);
            Assert.Equal(viaQuaternion.ComLocal.X, viaMatrix3.ComLocal.X, 6);
            Assert.Equal(viaQuaternion.ComLocal.Y, viaMatrix3.ComLocal.Y, 6);
            Assert.Equal(viaQuaternion.ComLocal.Z, viaMatrix3.ComLocal.Z, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M11, viaMatrix3.InertiaAtComLocal.M11, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M22, viaMatrix3.InertiaAtComLocal.M22, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M33, viaMatrix3.InertiaAtComLocal.M33, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M12, viaMatrix3.InertiaAtComLocal.M12, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M13, viaMatrix3.InertiaAtComLocal.M13, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M23, viaMatrix3.InertiaAtComLocal.M23, 6);
        }

        [Fact]
        public void CombineWithAnchor_Matrix3Overload_PartAtAnchor_RebasesBackToPartLocal()
        {
            // Mirrors InertialAggregatorTests.CombineWithLinkAnchor_SinglePartAtAnchor_RebasesBackToPartLocal
            // for the Matrix3 overload: when a part's own frame equals the
            // rebase anchor, the two transforms must cancel exactly,
            // regardless of what that shared rotation actually is.
            var partLocalCom = new Vector3(0f, 0f, 0.15f);
            var partInertia = new Matrix3(0.003, 0, 0, 0, 0.003, 0, 0, 0, 0.0001);
            var p = new MassProps(0.5, partLocalCom, partInertia);

            Matrix3 anchorR = RotZ(0.5);
            Vector3 anchorT = new Vector3(1.0f, -2.0f, 0.4f);

            var parts = new List<(MassProps, Matrix3, Vector3)> { (p, anchorR, anchorT) };
            MassProps rebased = InertialAggregator.Combine(parts, anchorR, anchorT);

            Assert.Equal(0.5, rebased.Mass, 6);
            Assert.Equal(partLocalCom.X, rebased.ComLocal.X, 5);
            Assert.Equal(partLocalCom.Y, rebased.ComLocal.Y, 5);
            Assert.Equal(partLocalCom.Z, rebased.ComLocal.Z, 5);
            Assert.Equal(partInertia.M11, rebased.InertiaAtComLocal.M11, 5);
            Assert.Equal(partInertia.M22, rebased.InertiaAtComLocal.M22, 5);
            Assert.Equal(partInertia.M33, rebased.InertiaAtComLocal.M33, 5);
        }

        [Fact]
        public void Combine_Matrix3Overload_Null_ReturnsIdentity()
        {
            var result = InertialAggregator.Combine((List<(MassProps, Matrix3, Vector3)>)null);
            Assert.Equal(0.0, result.Mass);
        }

        [Fact]
        public void Combine_Matrix3Overload_EmptyList_ReturnsIdentity()
        {
            var result = InertialAggregator.Combine(new List<(MassProps, Matrix3, Vector3)>());
            Assert.Equal(0.0, result.Mass);
        }
    }
}
