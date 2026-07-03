/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class MateJointClassificationTests
    {
        // A parent-frame identity pose, a child rotated 90deg about Z —
        // matches the convention already proven in Sw2gzRobotExporterTests
        // (RotZ helper produces [[0,-1,0],[1,0,0],[0,0,1]]).
        private static Matrix3 RotZ90() => new Matrix3(0, -1, 0, 1, 0, 0, 0, 0, 1);

        [Fact]
        public void Classify_ConcentricNoLimit_IsContinuous()
        {
            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Concentric,
                limitLower: null, limitUpper: null,
                cylinderLocalOrigin: new Vector3(0, 0, 0),
                cylinderLocalAxis: new Vector3(0, 0, 1),
                cylinderComponentRotation: Matrix3.Identity,
                cylinderComponentTranslation: Vector3.Zero,
                planeGeometry: null);

            Assert.True(result.Found);
            Assert.Equal(UrdfJointType.Continuous, result.Type);
            Assert.Equal(0.0, result.AxisAssembly.X, 3);
            Assert.Equal(0.0, result.AxisAssembly.Y, 3);
            Assert.Equal(1.0, result.AxisAssembly.Z, 3);
        }

        [Fact]
        public void Classify_ConcentricWithLimit_IsRevolute_WithLimitsCarriedThrough()
        {
            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Concentric,
                limitLower: -1.2, limitUpper: 0.5,
                cylinderLocalOrigin: new Vector3(0, 0, 0),
                cylinderLocalAxis: new Vector3(0, 0, 1),
                cylinderComponentRotation: Matrix3.Identity,
                cylinderComponentTranslation: Vector3.Zero,
                planeGeometry: null);

            Assert.Equal(UrdfJointType.Revolute, result.Type);
            Assert.Equal(-1.2, result.LimitLower);
            Assert.Equal(0.5, result.LimitUpper);
        }

        [Fact]
        public void Classify_CylinderAxisAndOrigin_TransformedIntoAssemblyFrame()
        {
            // Cylinder sits at part-local (1,0,0) with axis +X; its component
            // is rotated 90deg about Z and translated by (5,0,0). Assembly-
            // frame axis should be R*localAxis = (0,1,0); assembly-frame
            // origin should be R*localOrigin + t = (5,1,0).
            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Concentric,
                limitLower: null, limitUpper: null,
                cylinderLocalOrigin: new Vector3(1, 0, 0),
                cylinderLocalAxis: new Vector3(1, 0, 0),
                cylinderComponentRotation: RotZ90(),
                cylinderComponentTranslation: new Vector3(5, 0, 0),
                planeGeometry: null);

            Assert.Equal(0.0, result.AxisAssembly.X, 3);
            Assert.Equal(1.0, result.AxisAssembly.Y, 3);
            Assert.Equal(0.0, result.AxisAssembly.Z, 3);
            Assert.True(result.OriginAssembly.HasValue);
            Assert.Equal(5.0, result.OriginAssembly.Value.X, 3);
            Assert.Equal(1.0, result.OriginAssembly.Value.Y, 3);
            Assert.Equal(0.0, result.OriginAssembly.Value.Z, 3);
        }

        [Fact]
        public void Classify_LimitedAngleMate_UsesPlaneCrossProduct_NoCylinder()
        {
            // No cylindrical face on an Angle mate — axis comes from the
            // cross product of the two mated planes' normals instead.
            // Parent plane normal +X, child plane normal +Y (both identity
            // component pose) → cross product = +Z.
            var planes = new MateJointClassification.PlanePair(
                parentNormalLocal: new Vector3(1, 0, 0),
                parentPointLocal: new Vector3(0, 0, 0),
                parentRotation: Matrix3.Identity,
                parentTranslation: Vector3.Zero,
                childNormalLocal: new Vector3(0, 1, 0),
                childPointLocal: new Vector3(0, 0, 0),
                childRotation: Matrix3.Identity,
                childTranslation: Vector3.Zero);

            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Angle,
                limitLower: -0.3, limitUpper: 0.3,
                cylinderLocalOrigin: null, cylinderLocalAxis: null,
                cylinderComponentRotation: Matrix3.Identity, cylinderComponentTranslation: Vector3.Zero,
                planeGeometry: planes);

            Assert.Equal(UrdfJointType.Revolute, result.Type);
            Assert.Equal(0.0, result.AxisAssembly.X, 3);
            Assert.Equal(0.0, result.AxisAssembly.Y, 3);
            Assert.Equal(1.0, result.AxisAssembly.Z, 3);
        }

        [Fact]
        public void Classify_LimitedDistanceMate_UsesPlaneNormalAsSlideDirection()
        {
            var planes = new MateJointClassification.PlanePair(
                parentNormalLocal: new Vector3(0, 0, 1),
                parentPointLocal: new Vector3(2, 0, 0),
                parentRotation: Matrix3.Identity,
                parentTranslation: Vector3.Zero,
                childNormalLocal: new Vector3(0, 0, 1),
                childPointLocal: new Vector3(0, 0, 0),
                childRotation: Matrix3.Identity,
                childTranslation: Vector3.Zero);

            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Distance,
                limitLower: -0.1, limitUpper: 0.1,
                cylinderLocalOrigin: null, cylinderLocalAxis: null,
                cylinderComponentRotation: Matrix3.Identity, cylinderComponentTranslation: Vector3.Zero,
                planeGeometry: planes);

            Assert.Equal(UrdfJointType.Prismatic, result.Type);
            Assert.Equal(0.0, result.AxisAssembly.X, 3);
            Assert.Equal(0.0, result.AxisAssembly.Y, 3);
            Assert.Equal(1.0, result.AxisAssembly.Z, 3);
            Assert.True(result.OriginAssembly.HasValue);
            Assert.Equal(2.0, result.OriginAssembly.Value.X, 3);
        }

        [Fact]
        public void Classify_MovableTypeWithNoExtractableGeometry_DemotesToFixed()
        {
            // A Concentric mate whose face wasn't actually a cylinder (no
            // local origin/axis supplied) — would otherwise write a
            // zero-axis joint. Demote to Fixed rather than emit garbage.
            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Concentric,
                limitLower: null, limitUpper: null,
                cylinderLocalOrigin: null, cylinderLocalAxis: null,
                cylinderComponentRotation: Matrix3.Identity, cylinderComponentTranslation: Vector3.Zero,
                planeGeometry: null);

            Assert.Equal(UrdfJointType.Fixed, result.Type);
            Assert.False(result.OriginAssembly.HasValue);
        }

        [Fact]
        public void Classify_AngleWithNullPlaneGeometry_DemotesToFixed()
        {
            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Angle,
                limitLower: -0.3, limitUpper: 0.3,
                cylinderLocalOrigin: null, cylinderLocalAxis: null,
                cylinderComponentRotation: Matrix3.Identity, cylinderComponentTranslation: Vector3.Zero,
                planeGeometry: null);

            Assert.Equal(UrdfJointType.Fixed, result.Type);
            Assert.False(result.OriginAssembly.HasValue);
        }

        [Fact]
        public void Classify_DistanceWithNullPlaneGeometry_DemotesToFixed()
        {
            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Distance,
                limitLower: -0.1, limitUpper: 0.1,
                cylinderLocalOrigin: null, cylinderLocalAxis: null,
                cylinderComponentRotation: Matrix3.Identity, cylinderComponentTranslation: Vector3.Zero,
                planeGeometry: null);

            Assert.Equal(UrdfJointType.Fixed, result.Type);
            Assert.False(result.OriginAssembly.HasValue);
        }

        [Fact]
        public void Classify_LockMate_IsFixed_NoGeometryNeeded()
        {
            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Lock,
                limitLower: null, limitUpper: null,
                cylinderLocalOrigin: null, cylinderLocalAxis: null,
                cylinderComponentRotation: Matrix3.Identity, cylinderComponentTranslation: Vector3.Zero,
                planeGeometry: null);

            Assert.Equal(UrdfJointType.Fixed, result.Type);
        }

        [Fact]
        public void ChooseBest_PrefersLimitBearingCandidate_OverPlainContinuous()
        {
            var continuous = MateJointClassification.Classify(
                SwMateTypeCode.Concentric, null, null,
                new Vector3(0, 0, 0), new Vector3(0, 0, 1), Matrix3.Identity, Vector3.Zero, null);
            var revolute = MateJointClassification.Classify(
                SwMateTypeCode.Concentric, -1.0, 1.0,
                new Vector3(0, 0, 0), new Vector3(0, 0, 1), Matrix3.Identity, Vector3.Zero, null);

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
