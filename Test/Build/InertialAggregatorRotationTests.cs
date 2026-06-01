using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class InertialAggregatorRotationTests
    {
        [Fact]
        public void Combine_IdentityRotation_MatchesLegacyTranslationOnly()
        {
            // Two unit-mass parts at +/- 1 on X axis, identity tensors at identity rotation.
            // This is the same scenario as the pre-P3 test; rotated and unrotated paths must
            // produce byte-equivalent results when rotation is identity.
            var p = new MassProps(1.0, Vector3.Zero, Matrix3.Identity);
            var poseA = new Pose(new Vector3(-1, 0, 0), Quaternion.Identity);
            var poseB = new Pose(new Vector3( 1, 0, 0), Quaternion.Identity);

            var combined = InertialAggregator.Combine(new List<(MassProps, Pose)>
            {
                (p, poseA), (p, poseB)
            });

            Assert.Equal(2.0, combined.Mass);
            Assert.Equal(0.0, combined.ComLocal.X, 6);
            Assert.Equal(0.0, combined.ComLocal.Y, 6);
            Assert.Equal(0.0, combined.ComLocal.Z, 6);

            // Same expected tensor as legacy parallel-axis from the pre-existing
            // InertialAggregatorTests.Combine_TwoEqualPartsOffsetOnX_... test.
            Assert.Equal(2.0, combined.InertiaAtComLocal.M11, 6);
            Assert.Equal(4.0, combined.InertiaAtComLocal.M22, 6);
            Assert.Equal(4.0, combined.InertiaAtComLocal.M33, 6);
            Assert.Equal(0.0, combined.InertiaAtComLocal.M12, 6);
            Assert.Equal(0.0, combined.InertiaAtComLocal.M13, 6);
            Assert.Equal(0.0, combined.InertiaAtComLocal.M21, 6);
            Assert.Equal(0.0, combined.InertiaAtComLocal.M23, 6);
            Assert.Equal(0.0, combined.InertiaAtComLocal.M31, 6);
            Assert.Equal(0.0, combined.InertiaAtComLocal.M32, 6);
        }

        [Fact]
        public void Combine_SinglePartRotated90AboutZ_IxxAndIyySwap()
        {
            // diag(2, 3, 4) inertia; 90° rotation about Z should swap the X and Y
            // components, yielding diag(3, 2, 4) in the assembly frame.
            var inertia = new Matrix3(2, 0, 0, 0, 3, 0, 0, 0, 4);
            var props = new MassProps(1.0, Vector3.Zero, inertia);

            var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2.0));
            var pose = new Pose(Vector3.Zero, q);

            var result = InertialAggregator.Combine(new List<(MassProps, Pose)>
            {
                (props, pose)
            });

            Assert.Equal(1.0, result.Mass);
            Assert.Equal(0.0, result.ComLocal.X, 6);
            Assert.Equal(0.0, result.ComLocal.Y, 6);
            Assert.Equal(0.0, result.ComLocal.Z, 6);

            Assert.Equal(3.0, result.InertiaAtComLocal.M11, 5);
            Assert.Equal(2.0, result.InertiaAtComLocal.M22, 5);
            Assert.Equal(4.0, result.InertiaAtComLocal.M33, 5);
            Assert.Equal(0.0, result.InertiaAtComLocal.M12, 5);
            Assert.Equal(0.0, result.InertiaAtComLocal.M13, 5);
            Assert.Equal(0.0, result.InertiaAtComLocal.M21, 5);
            Assert.Equal(0.0, result.InertiaAtComLocal.M23, 5);
            Assert.Equal(0.0, result.InertiaAtComLocal.M31, 5);
            Assert.Equal(0.0, result.InertiaAtComLocal.M32, 5);
        }

        [Fact]
        public void Combine_TwoRotatedParts_TensorPositiveDefiniteAndSymmetric()
        {
            var inertia = new Matrix3(1.5, 0, 0, 0, 2.0, 0, 0, 0, 2.5);
            var qA = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f);
            var qB = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.7f);

            var a = (new MassProps(1.0, Vector3.Zero, inertia),
                     new Pose(new Vector3(0.3f, 0.2f, -0.1f), qA));
            var b = (new MassProps(2.0, Vector3.Zero, inertia),
                     new Pose(new Vector3(-0.2f, 0.5f, 0.4f), qB));

            var combined = InertialAggregator.Combine(new List<(MassProps, Pose)> { a, b });

            // Diagonals positive
            Assert.True(combined.InertiaAtComLocal.M11 > 0);
            Assert.True(combined.InertiaAtComLocal.M22 > 0);
            Assert.True(combined.InertiaAtComLocal.M33 > 0);

            // Symmetric within float tolerance
            Assert.Equal(combined.InertiaAtComLocal.M12, combined.InertiaAtComLocal.M21, 5);
            Assert.Equal(combined.InertiaAtComLocal.M13, combined.InertiaAtComLocal.M31, 5);
            Assert.Equal(combined.InertiaAtComLocal.M23, combined.InertiaAtComLocal.M32, 5);

            Assert.Equal(3.0, combined.Mass, 6);
        }

        [Fact]
        public void Combine_EmptyList_ReturnsZeroMass()
        {
            var result = InertialAggregator.Combine(new List<(MassProps, Pose)>());
            Assert.Equal(0.0, result.Mass);
        }
    }
}
