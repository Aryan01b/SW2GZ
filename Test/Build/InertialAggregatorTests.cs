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
        public void Combine_SinglePart_PassesThrough()
        {
            var props = new MassProps(2.0, Vector3.Zero, Matrix3.Identity);
            var result = InertialAggregator.Combine(new List<(MassProps, Pose)>
            {
                (props, Pose.Identity)
            });
            Assert.Equal(2.0, result.Mass);
        }

        [Fact]
        public void Combine_TwoEqualPartsOffset_SumsMassAndShiftsCom()
        {
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
        }
    }
}
