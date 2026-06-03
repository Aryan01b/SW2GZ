# Spec A — Planar + Floating joint types

**Date:** 2026-06-03
**Branch:** `v2.1-revamp`
**Status:** approved, ready for implementation plan

## Goal

Extend the mate-driven joint pipeline (currently Fixed / Revolute / Continuous /
Prismatic only) with the two URDF joint types it is missing: **planar** and
**floating**. Mimic is explicitly deferred; ball is not a URDF type and is
out of scope here (it belongs to the SDF gz-asset/world modes — a later spec).

URDF joint types after this spec: `fixed, revolute, continuous, prismatic,
planar, floating`. (URDF has no `ball`/`mimic`-as-type; mimic is a sub-element,
deferred.)

## Behavior

### Floating
- A joint whose child link has **no mate assigned** (empty `MateName`) becomes a
  **floating** (6-DOF) joint.
- **Behavior change from Phase 1:** unassigned joints previously defaulted to
  `Fixed`. They now default to `Floating`. To get a rigid weld the user assigns
  a **LOCK** mate (→ `Fixed`).
- Floating joints emit **no** `<axis>` and **no** `<limit>`.

### Planar
- Derived from a **coincident planar-face mate** (`swMateCOINCIDENT` where both
  mate references are planar faces): the two faces may slide in-plane and rotate
  about the shared face normal — exactly URDF `planar` semantics.
- Axis = the face **normal**. Planar joints emit `<axis>` but **no** `<limit>`
  (v1 — 2-D limits are not modelled).
- A `swMateCOINCIDENT` whose references are not planar faces falls back to
  `Fixed`.

## Components & changes

Each unit keeps its existing single responsibility; this spec only widens the
type vocabulary and the mate→type mapping.

1. **Enums** — `UrdfJointType` (`Build/Urdf/UrdfJoint.cs`) and `MateKind`
   (`Build/MateSpec.cs`) each gain `Planar, Floating`. They stay in lockstep.

2. **Mate walking (COM, `#if SW_INTEROP`)** —
   `SolidWorksAssemblyWalker.WalkAllMates`/`TryAddMateInfo`
   (`SwSurface/SolidWorksAssemblyWalker.cs` ~line 276):
   - Add `case swMateType_e.swMateCOINCIDENT:` → if both mate-entity references
     resolve to **planar faces**, kind = `Planar` and the axis is set to the
     face normal; otherwise kind = `Fixed`.
   - Planar-face detection uses the mate-entity geometry (`Face2.GetSurface` →
     `Surface.IsPlane` / plane normal). This path is COM-only and verifiable
     only on the SolidWorks workstation.

3. **Joint seeding** — `JointSeeder` (`Build/JointSeeder.cs`):
   - `ToJointType(MateKind)` gains `Planar → Planar`, `Floating → Floating`.
   - `Sync(...)`: a seeded joint with **no assigned mate** (empty `MateName`)
     resolves to `Floating` (replacing the old `Fixed` default). A joint with an
     assigned mate keeps deriving its type from that mate.

4. **URDF serialization** — `UrdfSerializer` (`Write/Urdf/UrdfSerializer.cs`):
   - `JointTypeString`: `Planar → "planar"`, `Floating → "floating"`.
   - `AppendJoint`: emit `<axis>` for `Revolute|Continuous|Prismatic|Planar`,
     **not** for `Fixed|Floating`. Emit `<limit>` only for `Revolute|Prismatic`
     (lower/upper) and `Continuous` (effort/velocity); `Planar` and `Floating`
     get no `<limit>`.

5. **SDF type switch (graceful)** — `SdfModelWriter` (`Gz/SdfModelWriter.cs`)
   and any SDF joint-type string switch are made **exhaustive** so a
   `Planar`/`Floating` value cannot throw. Full, correct SDF joint mapping
   (SDF has no `planar`; `floating` ≈ omit joint) is **Spec B's** concern — here
   we only guarantee no crash.

6. **Validation** — joint validator (`Build/JointDefValidator.cs` /
   `RobotModelValidator`):
   - `Planar` without an axis → advisory **warning**.
   - `Floating` expects no axis/limit → no warning when absent; a stray
     axis/limit on a floating joint is silently ignored (not an error).

7. **Wizard type display (minor)** — the Joints-step detail panel
   (`URDFExport/Sw2gzExportPmp.cs`) already shows the derived joint type; ensure
   `Planar` and `Floating` render in any human-readable type label/`Fmt` helper.
   No new controls.

## Data flow (unchanged shape)

`SW mates → WalkAllMates → MateInfo(kind, axis, limits) → user assigns mate to a
JointDef → JointSeeder/JointDefConverter → UrdfJoint → UrdfSerializer →
urdf/xacro`. This spec only adds two enum values flowing through the existing
path, plus the no-mate→Floating rule at the seeder and the planar-face detection
at the walker.

## Testing

Pure-code (no COM), added to **both** `SW2GZ.csproj` and
`Test/SW2GZ.Writers.Test.csproj`:
- `JointSeeder`: `MateKind.Planar→Planar`, `MateKind.Floating→Floating`;
  unassigned joint → `Floating`.
- `JointDefConverter`: planar/floating round-trip preserves type + axis.
- `UrdfSerializer` golden: a `planar` joint emits `type="planar"` + `<axis>` and
  **no** `<limit>`; a `floating` joint emits `type="floating"` with **no**
  `<axis>`/`<limit>`.
- Validator: planar-without-axis warns; floating clean.

COM-only and therefore **not** unit-tested (workstation verification by the
user): the `swMateCOINCIDENT` planar-face detection in `WalkAllMates`.

## Out of scope (explicit)

- **Mimic** (`<mimic>` + gear/rack-mate ratio extraction) — deferred.
- **Ball** joint — not URDF; belongs to Spec B (SDF gz-asset/world modes).
- Full SDF joint-type semantics for planar/floating — Spec B.
- 2-D planar limits.

## Risks / notes

- The **unassigned → Floating** default is a user-visible change from Phase 1
  (was Fixed); confirmed wanted. Worth a CHANGELOG note.
- Planar-face detection is the only COM-side logic and the only part not covered
  by unit tests.
