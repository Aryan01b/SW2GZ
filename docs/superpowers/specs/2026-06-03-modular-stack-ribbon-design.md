# Modular ROS 2 Stack Ribbon — Design Spec

**Date:** 2026-06-03
**Branch:** `v2.1-revamp`
**Checkpoint (revert point):** tag `checkpoint/pre-modular-ribbon` @ `946e7af`
**Status:** approved, implementing

---

## Goal

Turn the SolidWorks ribbon into a modular ROS 2 ecosystem configurator. The user composes a robot à-la-carte: pick the actuation backend, enable sensing, tune the bridge and Gazebo world — all per-assembly, all surfaced on the ribbon. Replace the coarse `modelOnly` boolean with a real, persisted, validated configuration object.

## Chosen UX — Option D (flyout + Configure PMP)

- **Flyout** for fast on/off of each stack (one ribbon slot, reuses reserved `flyoutGroupID = 91`).
- **Configure… PMP** for the detail parameters of each enabled stack (controller types, sensor rates, world params).
- Ribbon groups: **Model** (Create Model) · **Stacks** (Stacks ▼ flyout + Configure…) · **Output** (Export).
- State is **per-assembly**: on document activation the flyout repaints from the active assembly's saved `StackProfile`; toggling writes back.

Rejected: à-la-carte-only ribbon toggles (no home for detail params), preset profiles (user wants explicit modular control), single capability matrix panel (heavier UX than needed for v1).

## Validated Stack Model

**Actuation is a single-choice backend (radio), not independent checkboxes.** In Gz Harmonic two actuation backends both drive the same joint and conflict:
- **Gz native plugins** (`DiffDrive`, `JointController`, `JointPositionController`) — command over Gz topics, bridged to ROS.
- **`gz_ros2_control`** — embeds ros2_control in sim; command via `controller_manager` + controllers.

Therefore actuation ∈ `{ None, GzPlugin, Ros2Control }` — exactly one. Mutual-exclusion is structural (single enum), not a runtime check.

**Sensing is a fixed path, not a gz-vs-control fork.** In sim, sensors are simulated by Gz's sensor system and reach ROS via `ros_gz_bridge`, regardless of actuation choice. ros2_control sensor-interfaces in sim are niche and out of scope. So: **Sensors → Gz sensor blocks → ros_gz_bridge.**

**Note:** `gz_ros2_control` is itself a Gz plugin, so "Gazebo sim" and "ros2_control" are not orthogonal toggles. The real axis is the actuation backend; "Gazebo sim" is the master "build for Gz simulation" switch (world + gz system + plugin scaffold).

## Config Model — `StackProfile`

New record `SW2GZ/Ros2/StackProfile.cs`. Persisted inside `Sw2gzExportConfig` (assembly attribute) next to `Links`/`Joints` so it travels with the model. Threaded into `Sw2gzPipeline.Run(profile)`, replacing `modelOnly`.

```
enum ActuationBackend { None, GzPlugin, Ros2Control }   // default Ros2Control

record StackProfile {
    bool GzSim;                       // master: build for Gz (world + gz system + plugin scaffold)
    ActuationBackend Actuation;       // single-choice actuation backend
    Ros2ControlPlan Control;          // controller picks (when Actuation == Ros2Control)
    GzActuationPlan GzActuation;      // native plugin picks (when Actuation == GzPlugin)
    bool SensorsEnabled;
    IReadOnlyList<SensorDef> Sensors; // populated by extraction (D4) or wizard
    BridgePlan Bridge;                // topic checklist (clock/tf/joint_states/cmd_vel/odom), auto-seeded
    GzSimOptions GzSimOpts;           // world: sun/ground/physics, self-collide, friction
}
```

Supporting types (`Ros2ControlPlan`, `GzActuationPlan`, `BridgePlan`, `GzSimOptions`) are small records defined alongside. Defaults reproduce today's full-stack output exactly (GzSim=true, Actuation=Ros2Control, Bridge=clock+tf+joint_states, SensorsEnabled=false), so a no-UI build is behavior-preserving.

## Pipeline Wiring

`Sw2gzPipeline.Run(profile)` dispatches on the profile; existing writer gates are rewired from `!modelOnly` to profile checks (writers are already cleanly separated and conditionally gated):

- `GzSim` → world + `gz.xacro` system plugin + Gz sensor system scaffold.
- `Actuation == Ros2Control` → `ros2_control.xacro` + `controllers.yaml` + `gz_ros2_control` plugin tag.
- `Actuation == GzPlugin` → Gz native plugin tags in `gz.xacro`; **no** ros2_control.
- `Actuation == None` → neither actuation path.
- `SensorsEnabled` → per-sensor SDF blocks + sensor family plugins + sensor bridge entries.
- `Bridge` → `bridge.yaml` from the plan.

## Validation

- Mutual-exclusion: free — actuation is one enum.
- `Actuation == Ros2Control` requires ≥1 movable (non-fixed) joint.
- A `cmd_vel` bridge entry requires a `diff_drive` producer (ros2_control diff_drive or Gz `DiffDrive`).
- `SensorsEnabled` sensors require a valid parent frame.
- All findings feed the existing structured warning/error report.

## Phasing (each phase ships working, testable software)

- **D1 — Foundation.** `StackProfile` + supporting records; persistence in `Sw2gzExportConfig` + serialization; rewire `Sw2gzPipeline.Run` from `modelOnly` to `profile`. No UI. Defaults reproduce current output (regression-guarded by existing tests). Pure refactor.
- **D2 — Ribbon flyout.** Stacks ▼ flyout (on/off + actuation radio), `Configure…` button stub, document-activation sync, per-assembly persistence wired to flyout state.
- **D3 — Configure PMP.** Detail panels: actuation (controller list / gz plugin params), bridge topic checklist, Gz world options.
- **D4 — Sensors.** SW COM sensor extraction → populate `Sensors` → blocks + bridge entries. Largest new build, isolated last so the architecture lands first.

## Cross-Cutting Requirements

- **Commenting:** every new type, method, and non-obvious branch gets clear XML-doc / inline comments explaining intent (why, not just what). Required by user.
- **No AI attribution** in commits (per project memory): credit Aryan Arlikar + upstream only.
- **Determinism + idempotent re-export** preserved.
- **Checkpoint:** `checkpoint/pre-modular-ribbon` is the revert point if the effort is abandoned.

## Testing

- Unit: `StackProfile` defaults reproduce legacy output; each `ActuationBackend` value drives the right writer set; bridge plan → yaml; validation rules.
- Serialization round-trip: `StackProfile` ↔ assembly attribute.
- Integration smoke: each phase keeps `colcon build` / headless `gz sim` green.
- No SW COM in tests (D4 extraction tested via a seam/fake).
