# Wizard Step 4 — Joints (minimal, link-tree-derived)

Date: 2026-06-03
Status: approved (brainstorm) → implementation

## Goal

Replace the placeholder "Joints" step (index 3) of the native PMP export wizard
(`Sw2gzExportPmp`) with a working step that lets the user review and edit the
joints implied by the link tree built in the Links step. Scope is deliberately
minimal: no SolidWorks mate reading, no coordinate conversion.

## Decisions (locked)

- **Joint source:** derived from the link tree. Every non-root `LinkDef` (an edge
  parent→child) becomes exactly one joint. No `WalkMates()` dependency.
- **Default type:** `Fixed` (matches the codebase's conservative philosophy; the
  model assembles/exports validly with zero edits).
- **Axis input:** principal-axis presets — `None`, `±X`, `±Y`, `±Z`.
- **Architecture:** config-direct, mirroring the Links step. State lives in
  `Sw2gzExportConfig.Joints`; seeding/editing/validation happen in
  `Sw2gzExportPmp`. The dormant `JointsStepViewModel`/`JointEditViewModel` MVVM
  classes are **not** used (left for a separate cleanup).
- **Validation:** advisory (warn-not-block). Joints never block `Next`.
- **Defaults when promoted:** `LimitEffort = 100.0`, `LimitVelocity = 1.0`.

## Out of scope (future increments)

- SolidWorks→ROS coordinate conversion of joint axis/origin (origin stays
  `Pose.Identity`). *(the user's parked "figure it out later" item)*
- `SolidWorksAssemblyWalker.WalkMates()` real mate extraction.
- ros2_control per-joint interface emission + fixed-joint filtering.
- The Finish/export backend that actually emits the model.
- Retiring the unused `*StepViewModel` MVVM layer.

## Data model

New pure, COM-free, `DataContract` types under `SW2GZ/Build/Model/`:

```csharp
public enum JointAxisPreset { None, PlusX, MinusX, PlusY, MinusY, PlusZ, MinusZ }

[DataContract(Name = "JointDef", Namespace = "")]
public sealed class JointDef
{
    [DataMember] public string Name { get; set; } = "";
    [DataMember] public string ParentLink { get; set; } = "";
    [DataMember] public string ChildLink { get; set; } = "";
    [DataMember] public UrdfJointType Type { get; set; } = UrdfJointType.Fixed;
    [DataMember] public JointAxisPreset Axis { get; set; } = JointAxisPreset.None;
    [DataMember] public double? LimitLower { get; set; }
    [DataMember] public double? LimitUpper { get; set; }
    [DataMember] public double LimitEffort { get; set; } = 100.0;
    [DataMember] public double LimitVelocity { get; set; } = 1.0;
    [DataMember] public UrdfCmdInterface Interface { get; set; } = UrdfCmdInterface.Position;
}
```

`Sw2gzExportConfig` gains `[DataMember] public List<JointDef> Joints { get; set; } = new();`.

## Pure helpers (test-first)

1. **`JointSeeder.Sync(IReadOnlyList<LinkDef> links, IReadOnlyList<JointDef> existing) → List<JointDef>`**
   - One joint per non-root link (root = empty/unknown parent, same rule as
     `LinkHierarchy.Roots`).
   - Match existing joints by `ChildLink`: preserve user edits, refresh
     `ParentLink` (handles re-parenting in the Links step).
   - New edge → default `JointDef` (`Name = sanitize("<child>_joint")`, Fixed,
     axis None, Position, 100/1.0).
   - Drop joints whose child is no longer a non-root link.
   - Output preserves `links` order (final parents-first ordering is the
     export-time job of `JointGraphBuilder`).

2. **`JointDefConverter.ToUrdfJoint(JointDef) → UrdfJoint`** (+ list overload)
   - `Origin = Pose.Identity` (conversion deferred).
   - `Axis = AxisVector(preset)` (`None → Vector3.Zero`).
   - limits/interface mapped straight through.

3. **`JointDefValidator.Validate(IReadOnlyList<JointDef>) → List<string>`** (advisory)
   - non-fixed joint with `Axis == None`.
   - revolute/prismatic with both limits set and `lower > upper`.
   - continuous with `Position` interface (bug-10 parity).

## PMP step (COM-bound, `#if SW_INTEROP`, mirrors Links step)

Step group index 3 (`StepIdBase + 3*20 = 160`):

- Joint selector combobox (joint names) → loads the fields below.
- Read-only `Parent → Child` label.
- Type combobox (Fixed/Revolute/Continuous/Prismatic).
- Axis combobox (None/±X/±Y/±Z).
- Limit numberboxes: Lower, Upper, Effort, Velocity.
- Interface combobox (Position/Velocity/Effort).
- Advisory validation label.

Behaviour:
- On entering step 3, `config.Joints = JointSeeder.Sync(config.Links, config.Joints)`,
  then refresh the selector + fields.
- Combobox/numberbox handlers write straight into the selected `JointDef`.
- `Next` always advances (advisory only); the validation label shows warnings.
- `SaveCheckpoint` already round-trips `config`, so joints persist for free.

This PMP code only compiles in the SolidWorks add-in build (SW_INTEROP). The
net8 test project compiles and tests the pure helpers + config round-trip.

## Testing

net8 (`SW2GZ.Writers.Test`), source-linking the new files:
- `JointSeederTests` — new edge seeds default; re-parent updates ParentLink &
  preserves edits; removed/re-rooted link drops its joint; root has no joint;
  null-safe.
- `JointDefConverterTests` — each axis preset → vector; None → Zero; Identity
  origin; limits/interface pass-through; list overload.
- `JointDefValidatorTests` — each warning fires; clean set returns empty;
  Fixed-with-no-axis does **not** warn.
- `Sw2gzExportConfig` round-trip with a populated `Joints` list (extend existing
  config codec test coverage).

PMP handler stays untested (COM-bound), consistent with the Links step.
