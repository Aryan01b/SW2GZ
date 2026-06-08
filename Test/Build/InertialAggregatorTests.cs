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
        public void CombineWithLinkAnchor_Identity_MatchesLegacyOverload()
        {
            var p = new MassProps(1.0, new Vector3(0.1f, 0.2f, 0.3f),
                new Matrix3(2, 0, 0, 0, 3, 0, 0, 0, 4));
            var parts = new List<(MassProps, Pose)> { (p, Pose.Identity) };

            MassProps legacy = InertialAggregator.Combine(parts);
            MassProps rebased = InertialAggregator.Combine(parts, Pose.Identity);

            Assert.Equal(legacy.Mass, rebased.Mass);
            Assert.Equal(legacy.ComLocal.X, rebased.ComLocal.X, 6);
            Assert.Equal(legacy.ComLocal.Y, rebased.ComLocal.Y, 6);
            Assert.Equal(legacy.ComLocal.Z, rebased.ComLocal.Z, 6);
            Assert.Equal(legacy.InertiaAtComLocal.M11, rebased.InertiaAtComLocal.M11, 6);
            Assert.Equal(legacy.InertiaAtComLocal.M22, rebased.InertiaAtComLocal.M22, 6);
            Assert.Equal(legacy.InertiaAtComLocal.M33, rebased.InertiaAtComLocal.M33, 6);
        }

        [Fact]
        public void CombineWithLinkAnchor_SinglePartAtAnchor_RebasesBackToPartLocal()
        {
            // Pipeline usage pattern: pass the single part at the link anchor pose,
            // then rebase by the same anchor. The two transforms cancel and the
            // result MUST be the part's own (local) COM + inertia — that's what
            // URDF's <inertial> block wants.
            var partLocalCom = new Vector3(0f, 0f, 0.15f);
            var partInertia = new Matrix3(0.003f, 0, 0,  0, 0.003f, 0,  0, 0, 0.0001f);
            var p = new MassProps(0.5, partLocalCom, partInertia);

            var anchor = new Pose(
                new Vector3(0.0f, 0.3584f, -0.0297f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitX, -1.4646f));

            MassProps rebased = InertialAggregator.Combine(
                new List<(MassProps, Pose)> { (p, anchor) },
                anchor);

            Assert.Equal(0.5, rebased.Mass, 6);
            Assert.Equal(partLocalCom.X, rebased.ComLocal.X, 5);
            Assert.Equal(partLocalCom.Y, rebased.ComLocal.Y, 5);
            Assert.Equal(partLocalCom.Z, rebased.ComLocal.Z, 5);
            Assert.Equal(partInertia.M11, rebased.InertiaAtComLocal.M11, 5);
            Assert.Equal(partInertia.M22, rebased.InertiaAtComLocal.M22, 5);
            Assert.Equal(partInertia.M33, rebased.InertiaAtComLocal.M33, 5);
        }

        [Fact]
        public void CombineWithLinkAnchor_TwoPartsAtAnchor_ComAndInertiaInLinkFrame()
        {
            // Two unit-mass parts colocated at the link anchor with offset COMs.
            // Each part contributes its own local COM directly; assembly COM after
            // Combine() = anchor.Position + R_anchor · ((partA.com + partB.com) / 2).
            // Rebased into the link frame, the answer must be the mean of the
            // two part-local COMs (because each contributes equally).
            var partA = new MassProps(1.0, new Vector3(1, 0, 0), Matrix3.Identity);
            var partB = new MassProps(1.0, new Vector3(-1, 0, 0), Matrix3.Identity);
            var anchor = new Pose(
                new Vector3(10f, 20f, 30f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 4)));

            MassProps result = InertialAggregator.Combine(
                new List<(MassProps, Pose)> { (partA, anchor), (partB, anchor) },
                anchor);

            Assert.Equal(2.0, result.Mass, 6);
            // Mean of (1,0,0) and (-1,0,0) is the origin — in the LINK frame.
            Assert.Equal(0.0, result.ComLocal.X, 5);
            Assert.Equal(0.0, result.ComLocal.Y, 5);
            Assert.Equal(0.0, result.ComLocal.Z, 5);
            // Tensor must be symmetric in the link frame.
            Assert.Equal(result.InertiaAtComLocal.M12, result.InertiaAtComLocal.M21, 5);
            Assert.Equal(result.InertiaAtComLocal.M13, result.InertiaAtComLocal.M31, 5);
            Assert.Equal(result.InertiaAtComLocal.M23, result.InertiaAtComLocal.M32, 5);
            // Two unit masses at +/- (1,0,0) in the LINK frame, identity local
            // inertia each. Parallel-axis from a distance of 1 along link-X
            // gives I = diag(2, 4, 4) regardless of the world-anchor rotation.
            Assert.Equal(2.0, result.InertiaAtComLocal.M11, 5);
            Assert.Equal(4.0, result.InertiaAtComLocal.M22, 5);
            Assert.Equal(4.0, result.InertiaAtComLocal.M33, 5);
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
