# Robot-mode Mate-Based Joint Suggestion (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Auto-suggest each robot joint's Type/Axis/pivot-origin from the SolidWorks mates already used to build the assembly, so the user doesn't have to guess values by hand — and fix the real pivot-location bug Phase 1 surfaced (joint rotates around the child part's own design origin, not the actual mechanical hinge).

**Architecture:** A pure, unit-testable classifier (`MateJointClassification`) takes mate type + limit values + already-extracted local face geometry + component poses and produces a `UrdfJointType` + assembly-frame axis + assembly-frame pivot point — no COM in this layer, fully TDD-able with fakes, mirroring `JointDefReconciler`'s role in Phase 1. A separate, `#if SW_INTEROP`-gated `SwMateJointResolver` walks the assembly's mates via COM, extracts local face geometry (`ISurface.CylinderParams`/`PlaneParams`), and calls the pure classifier — reusing the already-proven `IComponentPoses`/`Matrix3` column-major-correct pose reader for all local-to-assembly-frame transforms (not the column-major-buggy raw `Transform2.ArrayData` reads recoverable from the pre-gut `AutoJointResolver.cs`, which explicitly predates that fix — see "Why not reuse `AutoJointResolver.cs`/`CylinderTransform.cs` verbatim" below). `Sw2gzCreateRobotPmp` runs the resolver once per untouched joint on Joints-step entry; `Sw2gzRobotExporter` gains one new behavior (origin position override when a mate pivot point exists). A visual pivot-axis-line spike and a reference-geometry override picker round out the UI.

**Tech Stack:** C# / .NET Framework (SW COM interop, `#if SW_INTEROP`), xUnit (net8.0 test project), SolidWorks `Mate2`/`MateEntity2`/`IFace2`/`ISurface` COM APIs.

**Spec:** [`docs/superpowers/specs/2026-07-03-robot-mate-joint-suggestion-design.md`](../specs/2026-07-03-robot-mate-joint-suggestion-design.md)

---

## Why not reuse `AutoJointResolver.cs`/`CylinderTransform.cs` verbatim

A pre-gut implementation of exactly this feature exists in git history
(`git show cfe95e3~1:SW2GZ/SwSurface/AutoJointResolver.cs`, recovered during
this plan's research) — a complete, well-documented mate walker with the
same classification rules this plan uses (Concentric→cylinder axis,
Angle→cross-product of plane normals, Distance→plane normal, "limited" test
`abs(Min)>1e-9 || abs(Max)>1e-9`, priority when multiple mates span a pair).
**Its geometry-transform code must NOT be copied as-is.** Memory
`sw-mathtransform-column-major` explicitly documents that
`Component2.Transform2.ArrayData`'s 3×3 rotation block is column-major, not
row-major — reading it naively (row-major) silently applies the *inverse*
rotation for any non-identity component orientation, a real bug already
found and fixed in `SolidWorksMeshTessellator.cs`/`SolidWorksAssemblyWalker.cs`
on 2026-07-01. That same memory explicitly flags `AutoJointResolver.cs` and
`CylinderTransform.cs` as **dead code from before that fix**, and says:
*"Whoever rebuilds real joint-origin/mate-axis detection for Robot mode v3
MUST apply the same column-major fix to these files before trusting their
output — do not assume they're already correct."* Confirmed today:
`AutoJointResolver.TryExtractPlane`'s inline transform
(`nx = d[0]*pp[0] + d[1]*pp[1] + d[2]*pp[2]`, reading `d[0..2]` as a row) is
exactly the buggy row-major pattern.

Instead, this plan's `SwMateJointResolver` reuses:
- `AutoJointResolver`'s **COM-walking mechanics** (MateGroup traversal,
  `Marshal.ReleaseComObject` hygiene, entity-to-component matching via
  `MateEntity2.ReferenceComponent`, walking to a component's top-level name)
  — this part is COM plumbing, not math, and has no column-major exposure.
- The already-proven, already-column-major-correct
  `IComponentPoses.GetPose(componentPathName)` (implemented by
  `SolidWorksComponentPoses`, the same interface `Sw2gzRobotExporter` already
  depends on) for every local-to-assembly-frame transform, instead of
  `CylinderTransform.cs` or any hand-rolled `Transform2.ArrayData` read.
- `ISurface.CylinderParams`/`PlaneParams` for the mated FACE's own
  part-local geometry (unaffected by the column-major issue — that's a
  property of the *component's* transform, not the face's own surface
  params) — then transforms that local geometry into assembly frame via
  `Matrix3.Mul` using the pose from `IComponentPoses`.

---

## File Structure

- **Create** `SW2GZ/Build/MateJointClassification.cs` — pure, COM-free. Takes mate type/limits/local face geometry/component poses (all plain data, no COM types), returns a joint suggestion (`UrdfJointType`, assembly-frame axis, assembly-frame pivot point, limits). Unit-tested with plain fixtures, no SolidWorks needed — mirrors `JointDefReconciler`'s role.
- **Create** `Test/Build/MateJointClassificationTests.cs` — unit tests for the above.
- **Create** `SW2GZ/SwSurface/SwMateJointResolver.cs` — `#if SW_INTEROP`-gated. Walks assembly mates via COM, extracts local face geometry, calls `MateJointClassification`. Not unit-testable; verified by build + live SolidWorks check.
- **Modify** `SW2GZ/Build/Model/JointDef.cs` — add `IsSuggested` bool.
- **Modify** `SW2GZ/URDFExport/Sw2gzRobotExporter.cs` — origin position override when `JointDef.HasMatePoint`.
- **Modify** `Test/URDFExport/Sw2gzRobotExporterTests.cs` — new tests for the above.
- **Modify** `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.Joints.cs` — run the resolver on Joints-step entry for untouched joints; mark `IsSuggested` on any panel edit; pivot-axis-line spike; reference-geometry override picker.
- **Modify** `SW2GZ/SW2GZ.csproj`, `Test/SW2GZ.Writers.Test.csproj` — register new files.

---

### Task 1: `MateJointClassification` — pure mate → joint-suggestion classifier

**Files:**
- Create: `SW2GZ/Build/MateJointClassification.cs`
- Create: `Test/Build/MateJointClassificationTests.cs`
- Modify: `SW2GZ/SW2GZ.csproj`
- Modify: `Test/SW2GZ.Writers.Test.csproj`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter MateJointClassificationTests`
Expected: compile error — `MateJointClassification`/`SwMateTypeCode` do not exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure (COM-free) classifier: given a SolidWorks mate's type, its limit range,
and already-extracted LOCAL (part-frame) face geometry + the owning
component's pose, decides the resulting UrdfJointType and computes the
assembly-frame axis/pivot-point. No SolidWorks types appear here — the
COM-facing SwMateJointResolver extracts local geometry (ISurface.
CylinderParams/PlaneParams) and component poses (IComponentPoses, already
column-major-correct) and hands them to this pure layer, exactly the same
split JointDefReconciler established for link-tree reconciliation.

Classification:
  Lock                    → Fixed
  Concentric, no limit    → Continuous
  Concentric, limited     → Revolute
  Angle,  limited         → Revolute
  Distance, limited       → Prismatic
  anything else           → Fixed (no fabricated axis)
"Limited" means abs(lower) > 1e-9 || abs(upper) > 1e-9 — a plain Concentric
mate always reports 0/0 (it never carries its own limit; confirmed against
FULL_ARM.SLDASM's real mates during design — see
docs/superpowers/specs/2026-07-03-robot-mate-joint-suggestion-design.md).

Axis + pivot geometry, once local geometry is transformed into assembly
frame via Matrix3.Mul (rotation) / Matrix3.Mul + translation (points) —
NEVER via raw Transform2.ArrayData reads, see the plan's "Why not reuse
AutoJointResolver.cs verbatim" section:
  Concentric → cylinder axis direction + a point on that axis.
  Angle      → cross product of the two mated planes' normals; origin =
               parent plane's point (the actual hinge sits on the shared
               edge between the two faces — the parent point is a fair
               anchor, good enough for the URDF, not required to be exact
               to the millimeter for a first suggestion the user can edit).
  Distance   → parent plane's normal as slide direction; origin = that
               plane's point.
  Lock       → no geometry needed.

A movable classification (Continuous/Revolute/Prismatic) with no
extractable geometry (e.g. a Concentric mate whose face somehow wasn't a
real cylinder) demotes to Fixed rather than emit a zero-axis joint — same
defensive rule the pre-gut AutoJointResolver used.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.Build
{
    public enum SwMateTypeCode { Lock, Concentric, Angle, Distance, Other }

    public static class MateJointClassification
    {
        public sealed class PlanePair
        {
            public Vector3 ParentNormalLocal, ParentPointLocal, ParentTranslation;
            public Matrix3 ParentRotation;
            public Vector3 ChildNormalLocal, ChildPointLocal, ChildTranslation;
            public Matrix3 ChildRotation;

            public PlanePair(
                Vector3 parentNormalLocal, Vector3 parentPointLocal, Matrix3 parentRotation, Vector3 parentTranslation,
                Vector3 childNormalLocal, Vector3 childPointLocal, Matrix3 childRotation, Vector3 childTranslation)
            {
                ParentNormalLocal = parentNormalLocal; ParentPointLocal = parentPointLocal;
                ParentRotation = parentRotation; ParentTranslation = parentTranslation;
                ChildNormalLocal = childNormalLocal; ChildPointLocal = childPointLocal;
                ChildRotation = childRotation; ChildTranslation = childTranslation;
            }
        }

        public sealed class Result
        {
            public bool Found;
            public UrdfJointType Type = UrdfJointType.Fixed;
            public Vector3 AxisAssembly = Vector3.Zero;
            public Vector3? OriginAssembly;
            public double? LimitLower;
            public double? LimitUpper;
        }

        public static Result Classify(
            SwMateTypeCode mateType,
            double? limitLower, double? limitUpper,
            Vector3? cylinderLocalOrigin, Vector3? cylinderLocalAxis,
            Matrix3 cylinderComponentRotation, Vector3 cylinderComponentTranslation,
            PlanePair planeGeometry)
        {
            bool hasLimit = (limitLower.HasValue && System.Math.Abs(limitLower.Value) > 1e-9)
                         || (limitUpper.HasValue && System.Math.Abs(limitUpper.Value) > 1e-9);

            UrdfJointType type = mateType switch
            {
                SwMateTypeCode.Lock => UrdfJointType.Fixed,
                SwMateTypeCode.Concentric => hasLimit ? UrdfJointType.Revolute : UrdfJointType.Continuous,
                SwMateTypeCode.Angle => UrdfJointType.Revolute,
                SwMateTypeCode.Distance => UrdfJointType.Prismatic,
                _ => UrdfJointType.Fixed,
            };

            Vector3 axis = Vector3.Zero;
            Vector3? origin = null;
            bool geometryOk = false;

            if (mateType == SwMateTypeCode.Concentric && cylinderLocalOrigin.HasValue && cylinderLocalAxis.HasValue)
            {
                axis = NormalizeOrZero(cylinderComponentRotation.Mul(cylinderLocalAxis.Value));
                origin = cylinderComponentRotation.Mul(cylinderLocalOrigin.Value) + cylinderComponentTranslation;
                geometryOk = axis != Vector3.Zero;
            }
            else if (mateType == SwMateTypeCode.Angle && planeGeometry != null)
            {
                Vector3 parentNormalAsm = NormalizeOrZero(planeGeometry.ParentRotation.Mul(planeGeometry.ParentNormalLocal));
                Vector3 childNormalAsm = NormalizeOrZero(planeGeometry.ChildRotation.Mul(planeGeometry.ChildNormalLocal));
                Vector3 cross = Vector3.Cross(parentNormalAsm, childNormalAsm);
                if (cross.LengthSquared() > 1e-12f)
                {
                    axis = Vector3.Normalize(cross);
                    origin = planeGeometry.ParentRotation.Mul(planeGeometry.ParentPointLocal) + planeGeometry.ParentTranslation;
                    geometryOk = true;
                }
            }
            else if (mateType == SwMateTypeCode.Distance && planeGeometry != null)
            {
                axis = NormalizeOrZero(planeGeometry.ParentRotation.Mul(planeGeometry.ParentNormalLocal));
                origin = planeGeometry.ParentRotation.Mul(planeGeometry.ParentPointLocal) + planeGeometry.ParentTranslation;
                geometryOk = axis != Vector3.Zero;
            }

            if (!geometryOk && type != UrdfJointType.Fixed)
            {
                // Movable kind with no extractable geometry → would write a
                // zero-axis joint. Demote to Fixed so the user can add a
                // cleaner mate and re-trigger suggestion.
                type = UrdfJointType.Fixed;
                axis = Vector3.Zero;
                origin = null;
            }

            return new Result
            {
                Found = true,
                Type = type,
                AxisAssembly = axis,
                OriginAssembly = geometryOk ? origin : null,
                LimitLower = hasLimit ? limitLower : null,
                LimitUpper = hasLimit ? limitUpper : null,
            };
        }

        // Prefer a limit-bearing candidate (→ Revolute/Prismatic) over a
        // plain Continuous one when multiple mates span the same (parent,
        // child) pair; first-seen wins within a tie, same rank the pre-gut
        // AutoJointResolved.ChooseBest used.
        public static Result ChooseBest(IReadOnlyList<Result> candidates)
        {
            if (candidates == null || candidates.Count == 0) return new Result { Found = false };
            Result firstAny = null, firstLimit = null;
            foreach (Result c in candidates)
            {
                if (c == null || !c.Found) continue;
                if (firstAny == null) firstAny = c;
                if (firstLimit == null && (c.LimitLower.HasValue || c.LimitUpper.HasValue)) firstLimit = c;
            }
            return firstLimit ?? firstAny ?? new Result { Found = false };
        }

        private static Vector3 NormalizeOrZero(Vector3 v) =>
            v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.Zero;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter MateJointClassificationTests`
Expected: 9 passed.

- [ ] **Step 5: Add the new files to both csproj files**

In `SW2GZ/SW2GZ.csproj`, next to `Build\JointDefReconciler.cs`:

```xml
    <Compile Include="Build\JointDefReconciler.cs" />
    <Compile Include="Build\MateJointClassification.cs" />
```

In `Test/SW2GZ.Writers.Test.csproj`, next to the `JointDefReconciler.cs` link entry:

```xml
    <Compile Include="..\SW2GZ\Build\JointDefReconciler.cs"     Link="Sources\Build\JointDefReconciler.cs" />
    <Compile Include="..\SW2GZ\Build\MateJointClassification.cs" Link="Sources\Build\MateJointClassification.cs" />
```

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: all pass, count up by 9 from the current baseline (confirm current baseline with `dotnet test` before this task if it has drifted from 490).

- [ ] **Step 7: Commit**

```bash
git add SW2GZ/Build/MateJointClassification.cs Test/Build/MateJointClassificationTests.cs SW2GZ/SW2GZ.csproj Test/SW2GZ.Writers.Test.csproj
git commit -m "feat(robot): add MateJointClassification, pure mate-to-joint classifier"
```

---

### Task 2: `JointDef.IsSuggested` + exporter origin-position override

**Files:**
- Modify: `SW2GZ/Build/Model/JointDef.cs`
- Modify: `SW2GZ/URDFExport/Sw2gzRobotExporter.cs`
- Test: `Test/URDFExport/Sw2gzRobotExporterTests.cs`

- [ ] **Step 1: Add `IsSuggested` to `JointDef`**

In `SW2GZ/Build/Model/JointDef.cs`, add next to the existing `HasMatePoint` field:

```csharp
        // True once a mate-based suggestion has been accepted or the user
        // has edited this joint by hand (Type/Axis/Limit/Name) — permanently
        // opts the joint out of future auto-suggestion, including a
        // deliberate choice to leave it Fixed with no axis (otherwise
        // indistinguishable from "never analyzed"). Defaults false so
        // legacy payloads round-trip unchanged.
        [DataMember] public bool IsSuggested { get; set; }
```

- [ ] **Step 2: Write the failing exporter tests**

Add to `Test/URDFExport/Sw2gzRobotExporterTests.cs`, after the existing `Export_FixedType_EmitsNoAxisOrLimitElements` test:

```csharp
        [Fact]
        public void Export_UsesMatePointForOrigin_WhenHasMatePoint()
        {
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute, AxisX = 0, AxisY = 0, AxisZ = 1 },
            };
            cfg.RobotJoints[0].SetMatePoint(new Vector3(2, 3, 4));

            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(2.0, double.Parse(xyz[0]), 3);
            Assert.Equal(3.0, double.Parse(xyz[1]), 3);
            Assert.Equal(4.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_MatePoint_IsExpressedRelativeToParentFrame_LikeOrdinaryOrigin()
        {
            // Parent link translated to (10,0,0) in assembly frame, both
            // identity rotation. A mate point at assembly (12,0,0) should
            // read as (2,0,0) relative to the parent — same parent-relative
            // convention ordinary (non-override) origins already use.
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, new Vector3(10, 0, 0)),
                ["arm-1@asm"]  = (Matrix3.Identity, new Vector3(10, 0, 0)),
            };
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute, AxisX = 0, AxisY = 0, AxisZ = 1 },
            };
            cfg.RobotJoints[0].SetMatePoint(new Vector3(12, 0, 0));

            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(2.0, double.Parse(xyz[0]), 3);
            Assert.Equal(0.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_NoMatePoint_OriginStaysLinkPoseDerived_AsBefore()
        {
            // Regression guard: a joint with no mate point must be totally
            // unaffected by this task — same as the existing (pre-Phase-2)
            // origin behavior.
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute, AxisX = 0, AxisY = 0, AxisZ = 1 },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(0.0, double.Parse(xyz[0]), 3);
            Assert.Equal(0.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter Sw2gzRobotExporterTests`
Expected: `Export_UsesMatePointForOrigin_WhenHasMatePoint` and
`Export_MatePoint_IsExpressedRelativeToParentFrame_LikeOrdinaryOrigin` FAIL
(origin ignores `MatePoint` today); `Export_NoMatePoint_OriginStaysLinkPoseDerived_AsBefore`
passes already (nothing to change for that case).

- [ ] **Step 4: Implement the exporter change**

In `SW2GZ/URDFExport/Sw2gzRobotExporter.cs`, inside the per-link loop's
existing joint-origin block (the one already extended by Phase 1 Task 2 —
look for the `jointOrigins[link.Name] = tJoint;` line), change the
assignment to check for a mate-point override:

```csharp
                    Vector3 tJoint = parentPose.R.Transpose().Mul(linkT - parentPose.T);
                    jointOrigins[link.Name] = tJoint;
                    jointRpys[link.Name] = rJoint.ToRpy();
```

becomes:

```csharp
                    Vector3 tJointGeometric = parentPose.R.Transpose().Mul(linkT - parentPose.T);
                    // A mate-derived pivot point (from mate-based joint
                    // suggestion) overrides ORIGIN POSITION only — never
                    // orientation, which stays the proven parent-relative
                    // rotation computed above. MatePoint is stored in
                    // assembly frame (same convention as Axis); express it
                    // relative to the parent exactly like the geometric
                    // origin already is.
                    if (jointByChild.TryGetValue(link.Name, out JointDef jdOrigin) && jdOrigin.HasMatePoint)
                    {
                        var matePointAssembly = new Vector3((float)jdOrigin.MatePointX, (float)jdOrigin.MatePointY, (float)jdOrigin.MatePointZ);
                        jointOrigins[link.Name] = parentPose.R.Transpose().Mul(matePointAssembly - parentPose.T);
                    }
                    else
                    {
                        jointOrigins[link.Name] = tJointGeometric;
                    }
                    jointRpys[link.Name] = rJoint.ToRpy();
```

(`jointByChild` already exists from Phase 1 Task 2 — this reuses the same
dictionary already built earlier in `Export()`.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter Sw2gzRobotExporterTests`
Expected: all pass (16 existing + 3 new = 19).

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: all pass, count up by 3 from Task 1's total.

- [ ] **Step 7: Commit**

```bash
git add SW2GZ/Build/Model/JointDef.cs SW2GZ/URDFExport/Sw2gzRobotExporter.cs Test/URDFExport/Sw2gzRobotExporterTests.cs
git commit -m "feat(robot): JointDef.IsSuggested + exporter honors mate-derived pivot point"
```

---

### Task 3: `SwMateJointResolver` — COM mate walker (live-tested increment)

**Files:**
- Create: `SW2GZ/SwSurface/SwMateJointResolver.cs`
- Modify: `SW2GZ/SW2GZ.csproj`

This class is `#if SW_INTEROP`-gated and not unit-testable — verified by a
clean addin build and a live SolidWorks check at the end of this task,
before Task 4 wires it into the wizard. Per this codebase's own history
(memory `robot-mode-dev`), get a live checkpoint on the smallest wired
piece before building more on top.

- [ ] **Step 1: Write the resolver**

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Walks the active assembly's mates via COM, extracts local (part-frame)
cylinder/plane geometry for mates spanning a given (parent, child) component
pair, and delegates classification to the pure MateJointClassification —
mirrors the recovered pre-gut AutoJointResolver's COM-walking mechanics
(MateGroup traversal, Marshal.ReleaseComObject hygiene, entity-to-component
matching, walk-to-top-level-name) but NEVER reads Component2.Transform2.
ArrayData directly — that 3x3 rotation block is column-major, and the
pre-gut code's raw reads predate the fix that already bit
SolidWorksMeshTessellator/SolidWorksAssemblyWalker once (memory
sw-mathtransform-column-major). Every local-to-assembly-frame transform here
goes through IComponentPoses.GetPose (already column-major-correct, the
same interface Sw2gzRobotExporter already depends on) and Matrix3.Mul — see
the plan's "Why not reuse AutoJointResolver.cs verbatim" section.
*/
#if SW_INTEROP
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2GZ.Build;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;

namespace SW2GZ.SwSurface
{
    public sealed class SwMateJointResolver
    {
        private readonly AssemblyDoc _doc;
        private readonly IComponentPoses _poses;

        public SwMateJointResolver(AssemblyDoc doc, IComponentPoses poses)
        {
            _doc = doc;
            _poses = poses;
        }

        // Finds the best joint suggestion for a (parentComponentName,
        // childComponentName) pair — both TOP-LEVEL Component2.Name2
        // values, matching LinkDef.ComponentIds[0]'s own convention. Returns
        // a not-found Result if the assembly has no mate spanning that pair,
        // or if _doc/_poses is null.
        public MateJointClassification.Result Resolve(string parentComponentName, string childComponentName)
        {
            if (_doc == null || _poses == null ||
                string.IsNullOrEmpty(parentComponentName) || string.IsNullOrEmpty(childComponentName))
                return new MateJointClassification.Result { Found = false };

            var candidates = new List<MateJointClassification.Result>();
            var modelDoc = (IModelDoc2)_doc;
            Feature feat = (Feature)modelDoc.FirstFeature();
            try
            {
                while (feat != null)
                {
                    if (feat.GetTypeName2() == "MateGroup")
                    {
                        Feature sub = (Feature)feat.GetFirstSubFeature();
                        try
                        {
                            while (sub != null)
                            {
                                MateJointClassification.Result hit =
                                    TryResolveMate(sub, parentComponentName, childComponentName);
                                if (hit != null) candidates.Add(hit);
                                Feature nextSub = (Feature)sub.GetNextSubFeature();
                                Marshal.ReleaseComObject(sub);
                                sub = nextSub;
                            }
                        }
                        finally { if (sub != null) Marshal.ReleaseComObject(sub); }
                    }
                    Feature next = (Feature)feat.GetNextFeature();
                    Marshal.ReleaseComObject(feat);
                    feat = next;
                }
            }
            finally { if (feat != null) Marshal.ReleaseComObject(feat); }

            return MateJointClassification.ChooseBest(candidates);
        }

        private MateJointClassification.Result TryResolveMate(
            Feature feat, string parentName, string childName)
        {
            object specific = feat.GetSpecificFeature2();
            var mate = specific as Mate2;
            if (mate == null)
            {
                if (specific != null) Marshal.ReleaseComObject(specific);
                return null;
            }

            try
            {
                int parentEntIdx = -1, childEntIdx = -1;
                int n = mate.GetMateEntityCount();
                for (int i = 0; i < n; i++)
                {
                    MateEntity2 ent = mate.MateEntity(i);
                    if (ent == null) continue;
                    try
                    {
                        Component2 comp = ent.ReferenceComponent;
                        if (comp == null) continue;
                        try
                        {
                            string name = TopLevelName(comp);
                            if (parentEntIdx < 0 && name == parentName) parentEntIdx = i;
                            else if (childEntIdx < 0 && name == childName) childEntIdx = i;
                        }
                        finally { Marshal.ReleaseComObject(comp); }
                    }
                    finally { Marshal.ReleaseComObject(ent); }
                }
                if (parentEntIdx < 0 || childEntIdx < 0) return null;

                double? lower = null, upper = null;
                try
                {
                    double max = mate.MaximumVariation, min = mate.MinimumVariation;
                    if (System.Math.Abs(max) > 1e-9 || System.Math.Abs(min) > 1e-9) { lower = min; upper = max; }
                }
                catch { /* some mate types don't expose variation — leave null */ }

                SwMateTypeCode code = (swMateType_e)mate.Type switch
                {
                    swMateType_e.swMateLOCK => SwMateTypeCode.Lock,
                    swMateType_e.swMateCONCENTRIC => SwMateTypeCode.Concentric,
                    swMateType_e.swMateANGLE => SwMateTypeCode.Angle,
                    swMateType_e.swMateDISTANCE => SwMateTypeCode.Distance,
                    _ => SwMateTypeCode.Other,
                };

                Vector3? cylOrigin = null, cylAxis = null;
                Matrix3 cylRot = Matrix3.Identity;
                Vector3 cylTrans = Vector3.Zero;
                MateJointClassification.PlanePair planes = null;

                if (code == SwMateTypeCode.Concentric)
                {
                    var cyl = TryExtractCylinderLocal(mate, parentEntIdx, parentName)
                              ?? TryExtractCylinderLocal(mate, childEntIdx, childName);
                    if (cyl.HasValue)
                    {
                        cylOrigin = cyl.Value.origin;
                        cylAxis = cyl.Value.axis;
                        cylRot = cyl.Value.rotation;
                        cylTrans = cyl.Value.translation;
                    }
                }
                else if (code == SwMateTypeCode.Angle || code == SwMateTypeCode.Distance)
                {
                    var parentPlane = TryExtractPlaneLocal(mate, parentEntIdx, parentName);
                    var childPlane = TryExtractPlaneLocal(mate, childEntIdx, childName);
                    if (parentPlane.HasValue && childPlane.HasValue)
                    {
                        planes = new MateJointClassification.PlanePair(
                            parentPlane.Value.normal, parentPlane.Value.point, parentPlane.Value.rotation, parentPlane.Value.translation,
                            childPlane.Value.normal, childPlane.Value.point, childPlane.Value.rotation, childPlane.Value.translation);
                    }
                }

                return MateJointClassification.Classify(
                    code, lower, upper, cylOrigin, cylAxis, cylRot, cylTrans, planes);
            }
            finally { Marshal.ReleaseComObject(mate); }
        }

        private (Vector3 origin, Vector3 axis, Matrix3 rotation, Vector3 translation)? TryExtractCylinderLocal(
            Mate2 mate, int entityIdx, string componentName)
        {
            if (entityIdx < 0) return null;
            MateEntity2 ent = mate.MateEntity(entityIdx);
            if (ent == null) return null;

            object refObj = null, surfObj = null;
            try
            {
                try { refObj = ent.Reference; } catch { refObj = null; }
                if (!(refObj is IFace2 face)) return null;
                surfObj = face.GetSurface();
                if (!(surfObj is ISurface surf) || !surf.IsCylinder()) return null;
                if (!(surf.CylinderParams is double[] cp) || cp.Length < 6) return null;

                (Matrix3 r, Vector3 t) = _poses.GetPose(componentName);
                return (new Vector3((float)cp[0], (float)cp[1], (float)cp[2]),
                        new Vector3((float)cp[3], (float)cp[4], (float)cp[5]), r, t);
            }
            catch { return null; }
            finally
            {
                if (surfObj != null) try { Marshal.ReleaseComObject(surfObj); } catch { }
                if (refObj != null) try { Marshal.ReleaseComObject(refObj); } catch { }
                try { Marshal.ReleaseComObject(ent); } catch { }
            }
        }

        private (Vector3 normal, Vector3 point, Matrix3 rotation, Vector3 translation)? TryExtractPlaneLocal(
            Mate2 mate, int entityIdx, string componentName)
        {
            if (entityIdx < 0) return null;
            MateEntity2 ent = mate.MateEntity(entityIdx);
            if (ent == null) return null;

            object refObj = null, surfObj = null;
            try
            {
                try { refObj = ent.Reference; } catch { refObj = null; }
                if (!(refObj is IFace2 face)) return null;
                surfObj = face.GetSurface();
                if (!(surfObj is ISurface surf) || !surf.IsPlane()) return null;
                if (!(surf.PlaneParams is double[] pp) || pp.Length < 6) return null;

                (Matrix3 r, Vector3 t) = _poses.GetPose(componentName);
                return (new Vector3((float)pp[0], (float)pp[1], (float)pp[2]),
                        new Vector3((float)pp[3], (float)pp[4], (float)pp[5]), r, t);
            }
            catch { return null; }
            finally
            {
                if (surfObj != null) try { Marshal.ReleaseComObject(surfObj); } catch { }
                if (refObj != null) try { Marshal.ReleaseComObject(refObj); } catch { }
                try { Marshal.ReleaseComObject(ent); } catch { }
            }
        }

        // Walk up the component owner chain to its top-level instance name
        // — same convention SolidWorksAssemblyWalker.TopLevelName uses, so
        // the ids line up with LinkDef.ComponentIds. Pure COM identity
        // logic, unrelated to (and unaffected by) the column-major issue.
        private static string TopLevelName(Component2 comp)
        {
            string name = comp.Name2;
            Component2 parent = (Component2)comp.GetParent();
            while (parent != null)
            {
                name = parent.Name2;
                Component2 next = (Component2)parent.GetParent();
                Marshal.ReleaseComObject(parent);
                parent = next;
            }
            return name;
        }
    }
}
#endif
```

- [ ] **Step 2: Add the new file to the addin csproj**

In `SW2GZ/SW2GZ.csproj`, next to `SwSurface\SolidWorksComponentPoses.cs` (or any neighboring `SwSurface` entry):

```xml
    <Compile Include="SwSurface\SwMateJointResolver.cs" />
```

(This file is `#if SW_INTEROP`-gated and has no COM-free parts of its own —
unlike `MateJointClassification.cs`, it is NOT added to
`Test/SW2GZ.Writers.Test.csproj`.)

- [ ] **Step 3: Build the addin**

Build via the project's MSBuild path (memory `sw2gz-build-deploy`). Confirm
no errors/warnings.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: unchanged pass count from Task 2 (this task touches no
test-project code).

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/SwSurface/SwMateJointResolver.cs SW2GZ/SW2GZ.csproj
git commit -m "feat(robot): add SwMateJointResolver, COM mate walker for joint suggestion"
```

- [ ] **Step 6: Live checkpoint (manual, in SolidWorks) — not yet wired to any UI**

This class has no caller yet (Task 4 wires it in) — this checkpoint is
purely "does it build and not crash the addin load," not a functional
check. Deploy, open SolidWorks with any assembly loaded, confirm the addin
loads without error (check the SW add-in manager / Immediate window for
load exceptions). **Do not proceed to Task 4 until this is confirmed** —
per this project's history, an inert new COM-touching class occasionally
still surfaces a load-time issue (e.g. a missing interop reference) that a
build alone won't catch.

---

### Task 4: Wire the resolver into the Joints step (auto-suggest on entry)

**Files:**
- Modify: `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.Joints.cs`

- [ ] **Step 1: Add a resolver field and construct it alongside the doc**

In `Sw2gzCreateRobotPmp.cs` (the main partial file, where `_modelDoc`/`_liveDoc` are already fields), add:

```csharp
        private readonly SwMateJointResolver _mateResolver;
```

and in the constructor, after `_liveDoc` is assigned:

```csharp
            _mateResolver = new SwMateJointResolver(
                (AssemblyDoc)_modelDoc,
                new SolidWorksComponentPoses((AssemblyDoc)_modelDoc));
```

- [ ] **Step 2: Auto-suggest untouched joints on list refresh**

In `Sw2gzCreateRobotPmp.Joints.cs`, modify `RefreshJointsList()` to run
suggestion for any joint still at its default state before populating the
listbox:

```csharp
        private void RefreshJointsList()
        {
            if (_jointsList == null) return;
            foreach (JointDef j in _liveDoc.Robot.Joints)
            {
                if (j.IsSuggested) continue;
                LinkDef parentLink = _liveDoc.Robot.Links.FirstOrDefault(l => l.Name == j.ParentLink);
                LinkDef childLink = _liveDoc.Robot.Links.FirstOrDefault(l => l.Name == j.ChildLink);
                string parentPrimary = parentLink?.ComponentIds?.FirstOrDefault();
                string childPrimary = childLink?.ComponentIds?.FirstOrDefault();
                if (string.IsNullOrEmpty(parentPrimary) || string.IsNullOrEmpty(childPrimary)) continue;

                MateJointClassification.Result suggestion;
                try { suggestion = _mateResolver.Resolve(parentPrimary, childPrimary); }
                catch (Exception ex) { logger.Warn("Mate suggestion failed for " + j.Name, ex); continue; }

                if (!suggestion.Found || suggestion.Type == UrdfJointType.Fixed) continue;

                j.Type = suggestion.Type;
                j.SetAxis(suggestion.AxisAssembly);
                if (suggestion.OriginAssembly.HasValue) j.SetMatePoint(suggestion.OriginAssembly.Value);
                if (suggestion.LimitLower.HasValue) j.LimitLower = suggestion.LimitLower;
                if (suggestion.LimitUpper.HasValue) j.LimitUpper = suggestion.LimitUpper;
                j.IsSuggested = true;
            }

            _jointsList.Clear();
            foreach (JointDef j in _liveDoc.Robot.Joints) _jointsList.AddItems(j.Name);
            if (_liveDoc.Robot.Joints.Count > 0)
            {
                _jointsList.CurrentSelection = 0;
                _selectedJointIndex = 0;
                LoadJointIntoControls(_liveDoc.Robot.Joints[0]);
            }
            else
            {
                _selectedJointIndex = -1;
                ClearJointControls();
            }
        }
```

`Sw2gzCreateRobotPmp.Joints.cs` currently imports only `System`,
`SolidWorks.Interop.sldworks/swconst/swpublished`, `SW2GZ.Build.Model`, and
`SW2GZ.Build.Urdf` — it does NOT yet import `System.Linq` (needed for
`FirstOrDefault`) or `SW2GZ.Build` (needed for `MateJointClassification`,
the return type of `_mateResolver.Resolve(...)`). Add both to its `using`
block:

```csharp
using System;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
```

(The main `Sw2gzCreateRobotPmp.cs` file already imports both `SW2GZ.Build`
and `SW2GZ.SwSurface` — no changes needed there beyond the field/constructor
lines in Step 1.)

Note the deliberate choice here: a mate that classifies to Fixed leaves
`IsSuggested` false (so a later SW edit adding a real mate can still
trigger suggestion next visit), matching the design spec exactly. A mate
that DOES produce a real (non-Fixed) suggestion sets `IsSuggested = true`
immediately — this is the auto-accept behavior implied by "no button, just
happens" from the design's trigger decision; the user can still edit any
suggested value afterward (which is a no-op on `IsSuggested`, already true).

- [ ] **Step 3: Mark `IsSuggested` on any manual panel edit**

In `CommitSelectedJointFromControls()` (already existing from Phase 1), add
`j.IsSuggested = true;` as the first line inside the
`if (_selectedJointIndex < 0 ...) return;` guard's else-path — i.e. right
after fetching `JointDef j`:

```csharp
        private void CommitSelectedJointFromControls()
        {
            if (_selectedJointIndex < 0 || _selectedJointIndex >= _liveDoc.Robot.Joints.Count) return;
            JointDef j = _liveDoc.Robot.Joints[_selectedJointIndex];
            j.IsSuggested = true;
```

(rest of the method unchanged.) This ensures ANY edit through the panel —
including a deliberate re-confirmation of a suggested value, or setting
Fixed with no axis — permanently opts the joint out of re-suggestion,
exactly as the design decided.

- [ ] **Step 4: Build the addin**

Build via the project's MSBuild path. Confirm no errors/warnings.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: unchanged pass count from Task 3.

- [ ] **Step 6: Commit**

```bash
git add SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.Joints.cs
git commit -m "feat(robot): auto-suggest joint type/axis/pivot from mates on step entry"
```

- [ ] **Step 7: Live checkpoint (manual, in SolidWorks)**

Deploy. Open Create Robot on `FULL_ARM.SLDASM` (has real Concentric +
LimitAngle mates, verified during design). Build the link tree so at least
one non-root link's parent/child primary components share a mate. Reach
the Joints step. Confirm: the joint that has a real spanning mate shows
Type/Axis pre-filled (not left at Fixed), a joint with no relevant mate
stays Fixed. Export and inspect the URDF's `<origin>` for that joint — it
should differ from what Phase 1 alone would have produced (mate pivot
point instead of link-pose-derived position). **Do not proceed to Task 5
until this is confirmed** — same incremental-live-test discipline as every
other COM-touching task in this project.

---

### Task 5: Pivot-axis-line visual feedback (research spike)

**Files:**
- Modify: `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.Joints.cs`

The design spec explicitly leaves the exact SolidWorks rendering mechanism
open — this task is a spike with a primary candidate and a documented
fallback, not a locked implementation. Budget extra live-check time here.

- [ ] **Step 1: Try the primary candidate — native Temporary Axis select+highlight**

Cylindrical faces in SolidWorks have an associated system-generated
"temporary axis" entity (the same one shown/hidden via View > Temporary
Axes) — selecting and coloring THAT existing entity avoids creating any new
geometry, closest in spirit to how `HighlightLinkMesh` already works via
plain selection. Add a method to `Sw2gzCreateRobotPmp.Joints.cs`:

```csharp
        // Highlights the selected joint's pivot axis as a yellow (pending)
        // or neutral (confirmed) line in the SW viewport, reusing the same
        // "only the active selection" pattern as HighlightLinkMesh. Uses
        // SW's own system-generated Temporary Axis on the cylindrical face
        // that produced the suggestion, rather than creating new geometry —
        // spike: verify this actually selects/colors as expected against
        // FULL_ARM.SLDASM before trusting it; see Step 2 for the fallback
        // if it doesn't render visibly.
        private void HighlightJointPivotAxis(JointDef j)
        {
            try
            {
                _modelDoc.ClearSelection2(true);
                if (j == null || j.Type == UrdfJointType.Fixed || !j.HasMatePoint) return;

                // Placeholder selection call for the spike to validate against
                // a real temporary axis entity — swSelTEMPAXES via SelectByID2
                // on the owning component's name, colored yellow while
                // IsSuggested, a neutral color once confirmed. Exact
                // component/entity targeting to be confirmed live in Step 2.
                System.Drawing.Color lineColor = j.IsSuggested
                    ? System.Drawing.Color.Yellow
                    : System.Drawing.Color.FromArgb(180, 180, 180);
                // TODO(spike): call _modelDoc.Extension.SelectByID2 with the
                // temporary axis entity name for j.ChildLink's primary
                // component, swSelTEMPAXES, then set selection color via
                // ModelDocExtension.SetUserPreferenceIntegerValue for
                // swColors_e.swColorSelectedFeature or per-selection color
                // API — confirm exact call live before relying on this.
            }
            catch (Exception ex) { logger.Warn("HighlightJointPivotAxis failed", ex); }
        }
```

- [ ] **Step 2: Live-test the candidate, in SolidWorks, against `FULL_ARM.SLDASM`**

Wire `HighlightJointPivotAxis(_liveDoc.Robot.Joints[Item])` into
`OnListboxSelectionChanged` (after `LoadJointIntoControls`) and into
`LoadJointIntoControls` itself (so it also fires on initial list load, not
just explicit re-selection). Deploy, open Create Robot on `FULL_ARM.SLDASM`,
select a joint with a real mate-derived suggestion, and look for a visible
highlighted axis line in the graphics area.

- **If the Temporary Axis approach renders visibly and selectably:** finish
  wiring the exact `SelectByID2` call (replace the `TODO` above with the
  real, working call discovered during this live test), confirm the color
  actually changes between pending/confirmed, then proceed to Step 3.
- **If it does not render, or the entity can't be selected/colored this
  way:** fall back to a transient 3D sketch line — `_modelDoc.SketchManager.
  Insert3DSketch(true)`, `InsertLine` from the mate point along the axis
  direction (scaled to a fixed visible length, e.g. the link's own bounding
  box diagonal), color set via the sketch segment's `ILineProperties` (or
  equivalent), then `Insert3DSketch(false)` to close it, and explicitly
  `FeatureManager.DeleteFeature2` it when selection changes or the step is
  left (never let it persist in the saved document). Document whichever
  path actually works in a comment replacing the `TODO`, and note the other
  as "tried, didn't work, here's why" for future reference — don't leave
  both half-implemented.

- [ ] **Step 3: Clear the line when leaving the Joints step or clearing selection**

Extend the existing `ShowStep()` Links-exit `ClearSelection2` pattern (from
the Phase 1 follow-up fix) with an equivalent for Joints: when leaving the
Joints step, remove whatever pivot-line mechanism Step 2 landed on (either
clear the temporary-axis selection, or delete the transient sketch if that
path was taken).

- [ ] **Step 4: Build, test, commit**

Build via MSBuild, confirm clean. `dotnet test Test/SW2GZ.Writers.Test.csproj`
unchanged. Commit:

```bash
git add SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.Joints.cs
git commit -m "feat(robot): pivot-axis-line visual feedback for pending joint suggestions"
```

- [ ] **Step 5: Live checkpoint** — confirm the line shows yellow for a
  pending suggestion, changes to neutral after accepting/editing that
  joint, and disappears on step exit or joint-selection change.

---

### Task 6: Reference-geometry axis override

**Files:**
- Modify: `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.Joints.cs`

- [ ] **Step 1: Add a geometry-pick control to the Joints detail form**

Add a new `PropertyManagerPageSelectionbox` (same control type and
`SetSelectionFilters` pattern as the Links step's `_meshPicker`, filtered to
edges/cylindrical faces instead of components) below the Axis X/Y/Z boxes,
with its own control ID and a "Pick axis from geometry" button in the
existing WinForms bar pattern. On pick, read the selected entity's own
`ISurface.CylinderParams` (for a cylindrical face) or edge direction (for a
linear edge), transform into assembly frame via the SAME `IComponentPoses`
+ `Matrix3` pattern `SwMateJointResolver` uses (reuse, don't reinvent), and
write the result into `_jointAxisXBox/YBox/ZBox.Value` — then mark
`IsSuggested = true` via the existing `CommitSelectedJointFromControls`
path (picking geometry counts as a manual edit, same as typing numbers).

- [ ] **Step 2: Build, test, commit**

Build via MSBuild, confirm clean. `dotnet test Test/SW2GZ.Writers.Test.csproj`
unchanged (this touches no test-project code). Commit:

```bash
git add SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.Joints.cs
git commit -m "feat(robot): pick joint axis from reference geometry, overriding suggestion"
```

- [ ] **Step 3: Live checkpoint** — pick an edge/cylindrical face for a
  joint's axis, confirm the numeric boxes update to match, confirm the
  joint's pivot-axis line (Task 5) updates/clears appropriately, confirm
  export reflects the picked axis.

---

### Task 7: Manual live verification in SolidWorks (full Phase 2 pass)

**Files:** none (verification only). Executed by the user, not the agent —
no tool in this environment drives the SolidWorks PMP UI.

- [ ] **Step 1:** Open Create Robot on `FULL_ARM.SLDASM`, build the full
  3-link tree so both real mates (Concentric+LimitAngle pair, plain
  Concentric) are exercised.
- [ ] **Step 2:** On the Joints step, confirm both movable joints arrive
  pre-suggested (one Revolute with real limits, one Continuous or Revolute
  depending on which pair has the LimitAngle mate), the yellow pivot line
  shows for whichever is selected, and editing either flips it to the
  neutral/confirmed state.
- [ ] **Step 3:** Override one joint's axis via reference-geometry pick;
  confirm the numeric boxes and pivot line both update.
- [ ] **Step 4:** Go back to Links, add/remove an unrelated link, return to
  Joints — confirm both suggested/confirmed joints keep their values
  (merge-preserve from Phase 1 still holds).
- [ ] **Step 5:** Export, inspect the URDF — confirm the mate-suggested
  joint's `<origin>` now sits at the mate's pivot point, not the child's own
  design origin (the bug this phase set out to fix), and that axis/limits
  match what SolidWorks' own mate shows.
- [ ] **Step 6:** Load in RViz/`jsp_gui` (memory `wsl-ros-test-env`), confirm
  the joint visibly rotates/slides around the correct physical point.
- [ ] **Step 7:** Report back with a specific symptom if anything breaks —
  per memory `robot-mode-dev`, a vague report is what caused the full
  revert of the previous mate-detection attempt; a precise one (which step,
  which mate, expected vs. actual) is what lets a fix land instead.

---

## Self-Review Notes

**Spec coverage:** every design decision maps to a task — auto-suggest
trigger + primary-component matching (Task 4), classification rules +
"limited" heuristic verified against real `FULL_ARM` data (Task 1),
axis+pivot geometry via proven column-major-correct transforms instead of
the dead pre-gut code (Tasks 1 & 3, with an explicit rationale section),
origin-position-only override (Task 2), `IsSuggested` state tracking
(Tasks 2 & 4), yellow pivot-line visual feedback with an honest
research-spike framing (Task 5), reference-geometry override (Task 6),
live-tested incremental sequencing throughout (checkpoints after Tasks 3,
4, 5, 6, full pass in Task 7).

**Placeholder scan:** the one intentional exception is Task 5's `TODO`
inside the spike's own code — flagged in the plan text as deliberate
(the spec itself says the exact SW mechanism is unresolved and needs a
live investigation, not a guess), with concrete next-step instructions for
both the success and failure path, not a vague "figure it out later."

**Type consistency:** `MateJointClassification.Result`/`PlanePair`/
`SwMateTypeCode` are defined once in Task 1 and used with the same names/
shapes in Task 3's `SwMateJointResolver` and Task 4's wizard wiring.
`JointDef.IsSuggested`/`HasMatePoint`/`SetMatePoint` are used consistently
across Tasks 2, 4, and 6. `IComponentPoses.GetPose` signature matches its
existing (Phase 1) usage exactly — no new abstraction introduced for pose
reading, reusing what's already proven.
