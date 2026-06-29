# World mode — sensors & actuators roadmap

Status: **planned, not started** (2026-06-29). Successor track to the World
Settings work (v2.5.0). This turns World mode from "static, review-only" into a
*live environment* — sensors that publish, props that move and can be commanded.

## Guiding principle — reuse, don't rebuild

SW2GZ already has the machinery this needs, built for Robot mode. The job is
mostly **UI to attach it to world models** + threading config + splicing the
existing emit into the world writer. Confirmed existing pieces:

| capability | existing code |
|---|---|
| Sensor models (camera, depth, lidar/GPU-lidar, imu, contact, force-torque, navsat) | `Build/Model/Sensors/*`, `Build/Model/SensorDef.cs`, `SensorKind.cs` |
| Sensor SDF emit + family system plugins | `Gz/SdfSensorBlocks.cs`, `Gz/SdfSensorPlugins.cs` |
| Sensor edit UI (MVVM) | `UI/ViewModels/SensorEditViewModel.cs`, `SensorsStepViewModel.cs` |
| ros2_control | `Ros2/Ros2ControlWriter.cs`, `Ros2/ControllersYaml.cs`, `Build/Model/ControlSpec.cs`, `URDF/SafetyController.cs` |
| ROS↔Gz bridge + launch | `Gz/RosGzBridgeYaml.cs`, `Ros2/LaunchPyWriter.cs` |
| Joints / inertia (from robot pipeline) | `WizardAssemblyWalker`, `InertialAggregator`, joint pipeline |

**Key insight:** by the articulation phase a "world prop" ≈ a mini-robot (links
+ joints + sensors + control). The world model `<model>` we already inline is
the natural host.

## Phases

### S1 — Sensors on world models  *(effort M, low risk)*
Attach a sensor to any world model (camera on a wall, lidar on a pole, contact
on the floor) and have it publish in Gz.
- **UX:** "Sensors" entry — either a step in the Create-World wizard or a small
  dialog from the World tab (mirror the World Settings pattern). Pick a world
  model + sensor type + topic + rate + mount pose. List/add/remove.
- **Backend:** store `List<SensorDef>` per world model on `Sw2gzDoc.World`;
  `Sw2gzWorldExporter` splices `SdfSensorBlocks` into that model's `<link>` and
  passes the sensor list to the world writer so `SdfSensorPlugins` adds the
  family system plugins (the `WriteScene` path needs the sensor-aware overload
  that `Write(input, sensors)` already demonstrates). Optional `RosGzBridgeYaml`
  output for the picked topics.
- **Milestone:** `gz sim` shows the sensor; `gz topic -l` lists its topic.

### A0 — Dynamic (non-static) props  *(effort S, low risk)*
Toggle a world asset non-static so it obeys gravity/collision.
- **UX:** a per-asset "Static / Dynamic" toggle in the asset list (+ optional
  mass override).
- **Backend:** when dynamic, emit `<static>false</static>` + `<inertial>` from
  `InertialAggregator` (already used by robot links) instead of the static
  block. Friction surface from the existing asset settings.
- **Milestone:** press play in `gz sim` — the prop falls / rests under physics.

### Refactor checkpoint — unify the model builder  *(do before A1)*
Before adding joints to props, collapse the parallel robot/world emit paths onto
one `Sw2gzModel` (links + joints + sensors + control) so A1 reuses the robot
joint pipeline verbatim instead of duplicating it. Scope this as its own small
plan; it pays for itself across A1–A3.

### A1 — Articulated props  *(effort L, medium risk)*
Give a prop joints — a door hinge, drawer slide, turntable, elevator.
- **UX:** a mini joints step for the selected prop (reuse the Create-Robot
  Joints UI: mate-driven auto-detect + axis/limits).
- **Backend:** reuse `WizardAssemblyWalker` + the joint pipeline to emit
  `<link>`/`<joint>` inside the world model.
- **Milestone:** the door swings on its hinge in `gz sim` (passive).
- **Scope flag:** this breaks the "all static / review-only" lock — a product
  decision to confirm first.

### A2 — Joint control  *(effort M, medium risk)*
Make articulated joints commandable in Gz.
- **Backend:** emit a Gz `JointController` / `JointPositionController` system
  plugin per actuated joint. Command via `gz topic`.
- **Milestone:** publish a command → the joint moves.

### A3 — ROS 2 control + bridge  *(effort L, higher risk)*
Make props/sensors first-class ROS 2 citizens.
- **Backend:** reuse `Ros2ControlWriter` + `ControllersYaml` for the props and
  `RosGzBridgeYaml` + `LaunchPyWriter` for topics/commands. This is the spec
  §7.1 plug-and-play gap, applied to world props.
- **Milestone:** drive a prop joint and read a sensor from ROS 2.

## Test strategy
Every writer stays COM-free and golden/Contains-tested in the Writers project
(as today). Per phase: a writer test for the new SDF block + an exporter test
through the fake tessellator. COM/UI paths are live-tested in SW (not unit
covered), same as the existing wizards.

## Recommended order
**S1 → A0** first — both low-risk, high-value, and they barely dent review-only.
Pause for the **refactor checkpoint**, then A1 → A2 → A3 only once the move from
review-only to dynamic worlds is explicitly approved.

## Open decisions (need user pick before A1)
1. Is moving World mode beyond "static review-only" desired now, or keep S1+A0
   only for this round?
2. Sensors UX: extra **wizard step** vs a **separate dialog** (like World
   Settings)?
3. Bridge/launch output: emit `ros_gz_bridge` yaml + launch in World mode, or
   keep World exports self-contained SDF-only?
