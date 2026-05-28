using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class InertialAggregatorTests
    {
        [Fact]
        public void Combine_Null_ReturnsIdentity()
        {
            var result = InertialAggregator.Combine(null);
            Assert.Equal(0.0, result.Mass);
        }

        [Fact]
        public void Combine_EmptyList_ReturnsIdentity()
        {
            var result = InertialAggregator.Combine(new List<(MassProps, Pose)>());
            Assert.Equal(0.0, result.Mass);
        }

        [Fact]
        public void Combine_SinglePartAtOrigin_PassesThroughMassComAndInertia()
        {
            var props = new MassProps(2.0, Vector3.Zero, Matrix3.Identity);
            var result = InertialAggregator.Combine(new List<(MassProps, Pose)>
            {
                (props, Pose.Identity)
            });

            Assert.Equal(2.0, result.Mass);
            Assert.Equal(0.0, result.ComLocal.X, 6);
            Assert.Equal(0.0, result.ComLocal.Y, 6);
            Assert.Equal(0.0, result.ComLocal.Z, 6);

            // d = 0, parallel-axis contribution is zero → tensor unchanged from identity
            Assert.Equal(1.0, result.InertiaAtComLocal.M11, 6);
            Assert.Equal(1.0, result.InertiaAtComLocal.M22, 6);
            Assert.Equal(1.0, result.InertiaAtComLocal.M33, 6);
            Assert.Equal(0.0, result.InertiaAtComLocal.M12, 6);
            Assert.Equal(0.0, result.InertiaAtComLocal.M13, 6);
            Assert.Equal(0.0, result.InertiaAtComLocal.M21, 6);
            Assert.Equal(0.0, result.InertiaAtComLocal.M23, 6);
            Assert.Equal(0.0, result.InertiaAtComLocal.M31, 6);
            Assert.Equal(0.0, result.InertiaAtComLocal.M32, 6);
        }

        [Fact]
        public void Combine_TwoEqualPartsOffsetOnX_SumsMass_CombinedComAtOrigin_TensorMatchesParallelAxis()
        {
            // Two unit-mass parts at +/- 1 on X axis, each with identity inertia tensor at its own COM.
            // Total mass = 2. Combined COM at origin.
            // Parallel-axis at d=(±1, 0, 0): contributes m*(|d|² δ - d⊗d)
            //   |d|² = 1
            //   I[0,0] += 1 * (1 - 1) = 0          ⇒  diag X total = 1 + 0 + 1 + 0 = 2
            //   I[1,1] += 1 * (1 - 0) = 1          ⇒  diag Y total = 1 + 1 + 1 + 1 = 4
            //   I[2,2] += 1 * (1 - 0) = 1          ⇒  diag Z total = 1 + 1 + 1 + 1 = 4
            //   off-diagonals all zero (d only has X component)

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
    }
}
