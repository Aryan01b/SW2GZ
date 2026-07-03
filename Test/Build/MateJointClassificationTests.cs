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

        // Only ParentOriginLocal/ParentAxisLocal set (ChildOriginLocal/
        // ChildAxisLocal null) — matches a mate where only one side's face
        // was extractable, same as most existing tests exercised before
        // the dual-cylinder agreement check was added.
        private static MateJointClassification.CylinderPair ParentOnly(
            Vector3 originLocal, Vector3 axisLocal, Matrix3 rotation, Vector3 translation) =>
            new MateJointClassification.CylinderPair(
                parentOriginLocal: originLocal, parentAxisLocal: axisLocal,
                parentRotation: rotation, parentTranslation: translation,
                childOriginLocal: null, childAxisLocal: null,
                childRotation: Matrix3.Identity, childTranslation: Vector3.Zero);

        [Fact]
        public void Classify_ConcentricNoLimit_IsContinuous()
        {
            var result = MateJointClassification.Classify(
                mateType: SwMateTypeCode.Concentric,
                limitLower: null, limitUpper: null,
                cylinderGeometry: ParentOnly(new Vector3(0, 0, 0), new Vector3(0, 0, 1), Matrix3.Identity, Vector3.Zero),
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
                cylinderGeometry: ParentOnly(new Vector3(0, 0, 0), new Vector3(0, 0, 1), Matrix3.Identity, Vector3.Zero),
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
                cylinderGeometry: ParentOnly(new Vector3(1, 0, 0), new Vector3(1, 0, 0), RotZ90(), new Vector3(5, 0, 0)),
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
        public void Classify_BothCylindersAgree_ReportsHighAgreementDot_AndZeroPerpDistance()
        {
            // Parent and child cylinders both describe the exact same
            // assembly-frame line — axis +Z through (0,0,0) — same as a
            // genuinely satisfied Concentric mate. Different local points
            // along the same line (0,0,0) vs (0,0,5) confirm the check
            // tolerates "different point, same line", not just identical
            // numbers.
            var pair = new MateJointClassification.CylinderPair(
                parentOriginLocal: new Vector3(0, 0, 0), parentAxisLocal: new Vector3(0, 0, 1),
                parentRotation: Matrix3.Identity, parentTranslation: Vector3.Zero,
                childOriginLocal: new Vector3(0, 0, 5), childAxisLocal: new Vector3(0, 0, 1),
                childRotation: Matrix3.Identity, childTranslation: Vector3.Zero);

            var result = MateJointClassification.Classify(
                SwMateTypeCode.Concentric, null, null, pair, null);

            Assert.True(result.AxisAgreementDot.HasValue);
            Assert.Equal(1.0, result.AxisAgreementDot.Value, 4);
            Assert.True(result.OriginPerpendicularDistance.HasValue);
            Assert.Equal(0.0, result.OriginPerpendicularDistance.Value, 4);
        }

        [Fact]
        public void Classify_CylindersDisagree_ReportsLowAgreementDot_AndNonZeroPerpDistance()
        {
            // Child's axis/origin describe a PARALLEL but offset line
            // (shifted +1 in X) — exactly the bug this check exists to
            // catch: a real Concentric mate can never produce this, so a
            // caller sees a non-zero perpendicular distance here as a
            // signal something upstream (wrong entity/pose) is broken.
            var pair = new MateJointClassification.CylinderPair(
                parentOriginLocal: new Vector3(0, 0, 0), parentAxisLocal: new Vector3(0, 0, 1),
                parentRotation: Matrix3.Identity, parentTranslation: Vector3.Zero,
                childOriginLocal: new Vector3(1, 0, 0), childAxisLocal: new Vector3(0, 0, 1),
                childRotation: Matrix3.Identity, childTranslation: Vector3.Zero);

            var result = MateJointClassification.Classify(
                SwMateTypeCode.Concentric, null, null, pair, null);

            Assert.Equal(1.0, result.AxisAgreementDot.Value, 4);
            Assert.Equal(1.0, result.OriginPerpendicularDistance.Value, 4);
        }

        [Fact]
        public void Classify_OnlyOneCylinderSide_AgreementFieldsStayNull()
        {
            var result = MateJointClassification.Classify(
                SwMateTypeCode.Concentric, null, null,
                ParentOnly(new Vector3(0, 0, 0), new Vector3(0, 0, 1), Matrix3.Identity, Vector3.Zero),
                null);

            Assert.False(result.AxisAgreementDot.HasValue);
            Assert.False(result.OriginPerpendicularDistance.HasValue);
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
                cylinderGeometry: null,
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
                cylinderGeometry: null,
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
                cylinderGeometry: null,
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
                cylinderGeometry: null,
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
                cylinderGeometry: null,
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
                cylinderGeometry: null,
                planeGeometry: null);

            Assert.Equal(UrdfJointType.Fixed, result.Type);
        }

        [Fact]
        public void ChooseBest_PrefersLimitBearingCandidate_OverPlainContinuous()
        {
            var continuous = MateJointClassification.Classify(
                SwMateTypeCode.Concentric, null, null,
                ParentOnly(new Vector3(0, 0, 0), new Vector3(0, 0, 1), Matrix3.Identity, Vector3.Zero), null);
            var revolute = MateJointClassification.Classify(
                SwMateTypeCode.Concentric, -1.0, 1.0,
                ParentOnly(new Vector3(0, 0, 0), new Vector3(0, 0, 1), Matrix3.Identity, Vector3.Zero), null);

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
