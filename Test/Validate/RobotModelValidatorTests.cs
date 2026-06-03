/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — RobotModelValidator unit tests. One test per rule (V1–V12). Each
test hand-builds a tiny RobotModel that exercises exactly one failure
path so the error code is unambiguous.
*/
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.Validate;
using Xunit;

namespace SW2GZ.Validate.Tests
{
    public class RobotModelValidatorTests
    {
        // ─── Helpers ──────────────────────────────────────────────────────────

        private static MeshData TrivialMesh() =>
            new MeshData(new[] { Vector3.Zero }, new[] { 0, 0, 0 }, null);

        private static UrdfLink Link(
            string name,
            double mass = 1.0,
            Matrix3? inertia = null) =>
            new UrdfLink(
                name,
                mass,
                Vector3.Zero,
                inertia ?? Matrix3.Identity,
                TrivialMesh(),
                TrivialMesh(),
                $"{name}.dae",
                $"{name}_c.stl");

        private static UrdfJoint FixedJoint(string name, string parent, string child) =>
            new UrdfJoint(
                name, UrdfJointType.Fixed, parent, child,
                Pose.Identity, new Vector3(1, 0, 0),
                null, null, 0, 0, UrdfCmdInterface.Position);

        private static UrdfJoint RevoluteJoint(string name, string parent, string child, Vector3 axis) =>
            new UrdfJoint(
                name, UrdfJointType.Revolute, parent, child,
                Pose.Identity, axis,
                -1.0, 1.0, 10, 1, UrdfCmdInterface.Position);

        private static RobotModel ModelWith(
            IReadOnlyList<UrdfLink> links,
            IReadOnlyList<UrdfJoint> joints,
            IReadOnlyList<MaterialDef>? materials = null,
            IReadOnlyList<SensorDef>? sensors = null,
            ControlSpec? control = null)
        {
            var meta = new RobotMeta("pkg", "a", "a@b", "MIT", CoordinateConvention.Identity);
            return RobotModelBuilder.Build(meta, links, joints, materials, sensors, control);
        }

        // Direct primary-ctor build bypassing RobotModelBuilder's sanitization.
        // Needed for V1 (empty links) and V12 (empty package name) which the
        // builder rejects before the validator ever sees the model.
        private static RobotModel RawModel(
            string packageName,
            IReadOnlyList<ModelLink> modelLinks,
            IReadOnlyList<UrdfJoint>? joints = null,
            IReadOnlyList<MaterialDef>? materials = null,
            IReadOnlyList<SensorDef>? sensors = null,
            ControlSpec? control = null,
            CoordinateConvention? frame = null)
        {
            var meta = new RobotMeta(packageName, "a", "a@b", "MIT",
                frame ?? CoordinateConvention.Identity);
            return new RobotModel(
                meta,
                modelLinks,
                joints ?? new List<UrdfJoint>(),
                materials ?? new List<MaterialDef>(),
                sensors ?? new List<SensorDef>(),
                control ?? new ControlSpec(new List<string>(), ControlSpec.DefaultJointStateBroadcaster));
        }

        // ─── V1 — Links non-empty ─────────────────────────────────────────────

        [Fact]
        public void Validate_EmptyLinks_ReportsLinksEmpty()
        {
            var model = RawModel("pkg", new List<ModelLink>());
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors, e => e.Code == "P9.E.LINKS_EMPTY");
        }

        // ─── V2 — Link name uniqueness ────────────────────────────────────────

        [Fact]
        public void Validate_DuplicateLinkNames_ReportsLinkDupe()
        {
            var links = new[] { Link("a"), Link("a") };
            var model = ModelWith(links, new List<UrdfJoint>());
            ValidationReport r = RobotModelValidator.Validate(model);
            var dupe = r.Errors.FirstOrDefault(e => e.Code == "P9.E.LINK_NAME_DUPE");
            Assert.NotNull(dupe);
            Assert.Contains("'a'", dupe.Message);
        }

        // ─── V3 — Joint name uniqueness ───────────────────────────────────────

        [Fact]
        public void Validate_DuplicateJointNames_ReportsJointDupe()
        {
            var links = new[] { Link("a"), Link("b") };
            var joints = new[]
            {
                FixedJoint("j1", "a", "b"),
                FixedJoint("j1", "a", "b"),
            };
            var model = ModelWith(links, joints);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors, e => e.Code == "P9.E.JOINT_NAME_DUPE");
        }

        // ─── V4 — Joint references unknown link ───────────────────────────────

        [Fact]
        public void Validate_JointReferencesUnknownLink_ReportsJointUnknownLink()
        {
            var links = new[] { Link("a") };
            var joints = new[] { FixedJoint("j1", "a", "ghost") };
            var model = ModelWith(links, joints);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors,
                e => e.Code == "P9.E.JOINT_UNKNOWN_LINK" && e.Message.Contains("ghost"));
        }

        // ─── V5 — Tree structure ──────────────────────────────────────────────

        [Fact]
        public void Validate_SingleLinkNoJoints_NoErrors()
        {
            var model = ModelWith(new[] { Link("a") }, new List<UrdfJoint>());
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.False(r.HasErrors);
        }

        [Fact]
        public void Validate_TwoLinksNoJoints_OneWarningPerDisconnected()
        {
            var model = ModelWith(new[] { Link("a"), Link("b") }, new List<UrdfJoint>());
            ValidationReport r = RobotModelValidator.Validate(model);
            var warns = r.Warnings.Where(w => w.Code == "P9.W.DISCONNECTED_LINK").ToList();
            Assert.Equal(2, warns.Count);
            Assert.False(r.HasErrors);
        }

        [Fact]
        public void Validate_TwoRoots_ReportsMultipleRoots()
        {
            // Three links a, b, c — joint b→c, leaving both a and b as roots.
            var links = new[] { Link("a"), Link("b"), Link("c") };
            var joints = new[] { FixedJoint("j1", "b", "c") };
            var model = ModelWith(links, joints);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors, e => e.Code == "P9.E.MULTIPLE_ROOTS");
        }

        [Fact]
        public void Validate_ChildWithTwoParents_ReportsMultiParent()
        {
            var links = new[] { Link("a"), Link("b"), Link("c") };
            var joints = new[]
            {
                FixedJoint("j1", "a", "c"),
                FixedJoint("j2", "b", "c"),
            };
            var model = ModelWith(links, joints);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors,
                e => e.Code == "P9.E.MULTI_PARENT" && e.Message.Contains("'c'"));
        }

        [Fact]
        public void Validate_Cycle_ReportsCycle()
        {
            // 3-link cycle: a→b→c→a (every link has exactly one parent — no roots
            // → "no root link found" plus a cycle finding).
            var links = new[] { Link("a"), Link("b"), Link("c") };
            var joints = new[]
            {
                FixedJoint("j1", "a", "b"),
                FixedJoint("j2", "b", "c"),
                FixedJoint("j3", "c", "a"),
            };
            var model = ModelWith(links, joints);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors, e => e.Code == "P9.E.CYCLE");
        }

        // ─── V6 — Mass + inertia ──────────────────────────────────────────────

        [Fact]
        public void Validate_NonPositiveMass_ReportsMassNonpositive()
        {
            var model = ModelWith(new[] { Link("a", mass: 0) }, new List<UrdfJoint>());
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors, e => e.Code == "P9.E.MASS_NONPOSITIVE");
        }

        [Fact]
        public void Validate_InertiaTriangleViolation_WarnsTriangle()
        {
            // Ixx=10, Iyy=1, Izz=1 → Iyy+Izz=2 < 10=Ixx, triangle violated.
            // Leading 2x2 minor Ixx*Iyy=10>0 so still passes PD check.
            var bad = new Matrix3(10, 0, 0, 0, 1, 0, 0, 0, 1);
            var model = ModelWith(new[] { Link("a", inertia: bad) }, new List<UrdfJoint>());
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Warnings, w => w.Code == "P9.W.INERTIA_TRIANGLE");
        }

        [Fact]
        public void Validate_NonPDInertia_ReportsInertiaNotPD()
        {
            // Ixx=1, Iyy=1, Ixy=2 → leading 2x2 det = 1 - 4 = -3 < 0 → not PD.
            var bad = new Matrix3(1, 2, 0, 2, 1, 0, 0, 0, 1);
            var model = ModelWith(new[] { Link("a", inertia: bad) }, new List<UrdfJoint>());
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors, e => e.Code == "P9.E.INERTIA_NOT_PD");
        }

        // ─── V7 — Joint axis non-zero ─────────────────────────────────────────

        [Fact]
        public void Validate_RevoluteZeroAxis_ReportsAxisZero()
        {
            var links = new[] { Link("a"), Link("b") };
            var joints = new[] { RevoluteJoint("j1", "a", "b", Vector3.Zero) };
            var model = ModelWith(links, joints);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors, e => e.Code == "P9.E.JOINT_AXIS_ZERO");
        }

        [Fact]
        public void Validate_FloatingZeroAxis_NoAxisError()
        {
            // Floating joints are 6-DOF and carry no axis; a zero axis must NOT
            // be flagged (unlike revolute/continuous/prismatic/planar).
            var links = new[] { Link("a"), Link("b") };
            var joints = new[]
            {
                new UrdfJoint("free", UrdfJointType.Floating, "a", "b",
                    Pose.Identity, Vector3.Zero, null, null, 0, 0, UrdfCmdInterface.Position),
            };
            var model = ModelWith(links, joints);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.DoesNotContain(r.Errors, e => e.Code == "P9.E.JOINT_AXIS_ZERO");
        }

        // ─── V8 — Material refs ───────────────────────────────────────────────

        [Fact]
        public void Validate_MaterialRefMissing_ReportsMaterialRefUnknown()
        {
            var link = Link("a");
            var modelLink = new ModelLink(link, "ghost_material", null);
            var model = RawModel("pkg", new[] { modelLink });
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors,
                e => e.Code == "P9.E.MATERIAL_REF_UNKNOWN" && e.Message.Contains("ghost_material"));
        }

        // ─── V9 — Sensor refs ─────────────────────────────────────────────────

        [Fact]
        public void Validate_SensorOnUnknownLink_ReportsSensorLinkUnknown()
        {
            // Inject sensor pointing at a non-existent link via primary-ctor
            // bypass (AssembleSensors would otherwise reject it upstream).
            var link = Link("a");
            var modelLink = new ModelLink(link, null, null);
            var imu = new ImuSensor(
                "imu1", "ghost_link", Pose.Identity, "/imu1", "imu1_frame", 100.0, 0.0);
            var model = RawModel("pkg", new[] { modelLink }, sensors: new SensorDef[] { imu });
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors,
                e => e.Code == "P9.E.SENSOR_LINK_UNKNOWN" && e.Message.Contains("ghost_link"));
        }

        [Fact]
        public void Validate_ForceTorqueOnUnknownJoint_ReportsSensorJointUnknown()
        {
            var link = Link("a");
            var modelLink = new ModelLink(link, null, null);
            var ft = new ForceTorqueSensor(
                "ft1", "a", Pose.Identity, "/ft1", "ft1_frame", 100.0, "ghost_joint");
            var model = RawModel("pkg", new[] { modelLink }, sensors: new SensorDef[] { ft });
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors,
                e => e.Code == "P9.E.SENSOR_JOINT_UNKNOWN" && e.Message.Contains("ghost_joint"));
        }

        // ─── V10 — Control refs ───────────────────────────────────────────────

        [Fact]
        public void Validate_ControlJointMissing_ReportsControlJointUnknown()
        {
            var links = new[] { Link("a"), Link("b") };
            var joints = new[] { FixedJoint("j1", "a", "b") };
            var control = new ControlSpec(
                new List<string> { "j1", "ghost_joint" },
                ControlSpec.DefaultJointStateBroadcaster);
            var model = ModelWith(links, joints, control: control);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors,
                e => e.Code == "P9.E.CONTROL_JOINT_UNKNOWN" && e.Message.Contains("ghost_joint"));
        }

        // ─── V11 — Coordinate convention ──────────────────────────────────────

        [Fact]
        public void Validate_NonOrthonormalFrame_ReportsFrameInvalid()
        {
            // Pure scale (2x identity) — fails IsApproximatelyOrthonormal.
            var bad = new CoordinateConvention(
                new Matrix3(2, 0, 0, 0, 2, 0, 0, 0, 2), 1.0);
            var link = Link("a");
            var modelLink = new ModelLink(link, null, null);
            var model = RawModel("pkg", new[] { modelLink }, frame: bad);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors, e => e.Code == "P9.E.FRAME_INVALID");
        }

        // ─── V12 — Package name non-empty ─────────────────────────────────────

        [Fact]
        public void Validate_EmptyPackageName_ReportsPackageNameEmpty()
        {
            var link = Link("a");
            var modelLink = new ModelLink(link, null, null);
            var model = RawModel("   ", new[] { modelLink });
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Contains(r.Errors, e => e.Code == "P9.E.PACKAGE_NAME_EMPTY");
        }

        // ─── Happy path sanity ────────────────────────────────────────────────

        [Fact]
        public void Validate_WellFormedTree_NoIssues()
        {
            var links = new[] { Link("base"), Link("arm"), Link("hand") };
            var joints = new[]
            {
                RevoluteJoint("j1", "base", "arm", new Vector3(0, 0, 1)),
                FixedJoint("j2", "arm", "hand"),
            };
            var model = ModelWith(links, joints);
            ValidationReport r = RobotModelValidator.Validate(model);
            Assert.Empty(r.Issues);
        }
    }
}
