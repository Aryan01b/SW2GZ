/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Build;
using SW2GZ.Build.Urdf;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class MateJointClassificationTests
    {
        [Fact]
        public void Classify_Lock_IsFixed()
        {
            var result = MateJointClassification.Classify(SwMateTypeCode.Lock, null, null);
            Assert.True(result.Found);
            Assert.Equal(UrdfJointType.Fixed, result.Type);
        }

        [Fact]
        public void Classify_ConcentricNoLimit_IsContinuous()
        {
            var result = MateJointClassification.Classify(SwMateTypeCode.Concentric, null, null);
            Assert.Equal(UrdfJointType.Continuous, result.Type);
            Assert.Null(result.LimitLower);
            Assert.Null(result.LimitUpper);
        }

        [Fact]
        public void Classify_ConcentricWithLimit_IsRevolute_WithLimitsCarriedThrough()
        {
            var result = MateJointClassification.Classify(SwMateTypeCode.Concentric, -1.2, 0.5);
            Assert.Equal(UrdfJointType.Revolute, result.Type);
            Assert.Equal(-1.2, result.LimitLower);
            Assert.Equal(0.5, result.LimitUpper);
        }

        [Fact]
        public void Classify_Angle_IsRevolute_WithLimitsCarriedThrough()
        {
            var result = MateJointClassification.Classify(SwMateTypeCode.Angle, -0.3, 0.3);
            Assert.Equal(UrdfJointType.Revolute, result.Type);
            Assert.Equal(-0.3, result.LimitLower);
            Assert.Equal(0.3, result.LimitUpper);
        }

        [Fact]
        public void Classify_Distance_IsPrismatic_WithLimitsCarriedThrough()
        {
            var result = MateJointClassification.Classify(SwMateTypeCode.Distance, -0.1, 0.1);
            Assert.Equal(UrdfJointType.Prismatic, result.Type);
            Assert.Equal(-0.1, result.LimitLower);
            Assert.Equal(0.1, result.LimitUpper);
        }

        [Fact]
        public void Classify_Other_IsFixed()
        {
            var result = MateJointClassification.Classify(SwMateTypeCode.Other, null, null);
            Assert.Equal(UrdfJointType.Fixed, result.Type);
        }

        [Fact]
        public void ChooseBest_PrefersLimitBearingCandidate_OverPlainContinuous()
        {
            var continuous = MateJointClassification.Classify(SwMateTypeCode.Concentric, null, null);
            var revolute = MateJointClassification.Classify(SwMateTypeCode.Concentric, -1.0, 1.0);

            var chosen = MateJointClassification.ChooseBest(new[] { continuous, revolute });

            Assert.Equal(UrdfJointType.Revolute, chosen.Type);
        }

        [Fact]
        public void ChooseBest_EmptyOrNullCandidates_ReturnsNotFound()
        {
            Assert.False(MateJointClassification.ChooseBest(new MateJointClassification.Result[0]).Found);
            Assert.False(MateJointClassification.ChooseBest(null).Found);
        }
    }
}
