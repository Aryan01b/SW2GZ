# SW2GZ Roadmap — Plug-and-Play SolidWorks → ROS 2 Jazzy + Gazebo Harmonic

**Goal (definition of done):** User clicks **Export** in SolidWorks → gets an output folder → `colcon build` → `ros2 launch <pkg> bringup.launch.py` → robot spawns in **Gz Harmonic**, TF tree correct, `ros2_control` controllers active, robot drivable/movable. **Zero manual file editing.**

Refs: [ros2/index.md](ros2/index.md) · [gz/index.md](gz/index.md)

> **Status as of 2026-06-03 (v2.1.0).** Reality is well ahead of the original phase plan. The real `Sw2gzPipeline` already walks the assembly, tessellates visual + convex-hull collision meshes, aggregates mass/inertia, builds joints from mates, emits ros2_control + gz Harmonic + launch, and validates pre/post. Status legend below: ✅ done · 🟡 partial · 🔴 stub/none.
>
> **Snapshot:**
>
> | Ph | Area | Status |
> |----|------|--------|
> | 1 | Skeleton export | ✅ done |
> | 2 | Geometry & mesh | ✅ done — DAE(color)+STL, convex-hull collision |
> | 3 | Inertial | ✅ done — SW mass props, aggregator, PD-check |
> | 4 | Kinematics | 🟡 mostly — Fixed/Revolute/Continuous/Prismatic/Planar/Floating done; **mimic + ball(SDF) missing** |
> | 5 | Package | ✅ done — package.xml, CMakeLists, modular xacro |
> | 6 | Gz Harmonic | ✅ done — gz.xacro plugin, world(sun/ground/physics), bridge yaml |
> | 7 | ros2_control | 🟡 partial — joint_trajectory + state_broadcaster only; **no diff_drive/ackermann, no auto robot-type pick** |
> | 8 | Sensors | 🔴 stub — models exist, extraction returns empty (P6-COM TODO) |
> | 9 | Bringup | ✅ done — gz_sim.launch.py + display.launch.py |
> | 10 | Nav2/MoveIt | 🔴 none |
> | 11 | Validate | 🟡 pre+post validators + smoke; preview = legacy tree UI only |
> | 12 | Packaging | 🟡 README+gitignore done; **no zip/Docker/idempotent re-export** |
>
> **Real remaining work (not phase-ordered):**
> 1. **materials.xacro = empty stub** (`Ros2Package.cs:49` TODO) — color lives in DAE but URDF `<material>` not populated.
> 2. **P4 pipeline not default** — gated on `Profile.Mode==RobotPackage`; legacy URDFRobot-tree path still primary (`ExportHelper.cs:175` TODO remove legacy).
> 3. **Sensor extraction stub** — `Sw2gzPipeline.cs:66` returns empty sensor list.
> 4. **New wizard UI ExportRunner = stub** — `Sw2gzPipelineExportRunner` (P8-COM).
> 5. **Control auto-type** — detect wheels→diff_drive/ackermann, arm→trajectory, gripper→gripper_controller.
> 6. **mimic joint** + **ball joint** (SDF-only, belongs with gz asset/world export modes).
>
> Tests: 552 xUnit (builders + writers + validators + integration smoke). No SW COM in tests.

---

## Output Contract (what a finished export must contain)

```
<robot>_description/         ament package
  urdf/<robot>.urdf.xacro    links, joints, inertials, ros2_control, gazebo tags
  meshes/visual/*.dae|stl    visual geometry
  meshes/collision/*.stl     simplified/convex collision geometry
  config/controllers.yaml    controller_manager + controllers
  config/bridge.yaml         ros_gz_bridge topic map
  config/<robot>.rviz        rviz layout
  launch/bringup.launch.py   gz + spawn + rsp + bridge + controllers + rviz
  worlds/empty.sdf           ground + sun + physics
  package.xml  CMakeLists.txt
  README.md
```

---

## Phases

### Phase 1 — Skeleton export ✅ DONE
Export button, base link, joint model.
- Export entry point (`Sw2gzPipeline`), `RobotModel`, base/root link emit, `JointDef` model + seed/convert/validate, URDF/xacro/launch writers (skeleton).
**Acceptance:** export produces a base-link URDF + a stub joint.

---

### Phase 2 — Geometry & mesh export ✅ DONE
Per-link visual + collision meshes from SolidWorks.
- Export each link body → mesh file (DAE preferred for color, STL fallback).
- Collision mesh: convex hull or decimated copy (separate, lighter).
- Mesh origin/scale: SW mm → ROS m; align body coord → link frame.
- Material/color from SW appearance → URDF `<material>` / DAE.
- Mesh folder layout + `package://` URIs.
**Scope out:** texture maps (later), exact CAD-grade collision.
**Acceptance:** link shows correct geometry + color in RViz, no scale/offset error.

---

### Phase 3 — Inertial & physical properties ✅ DONE
Real mass properties per link.
- Pull SW mass props: mass, center-of-mass, inertia tensor.
- Write `<inertial>`: `<mass>`, `<origin>` at COM, `<inertia>` tensor.
- Unit + frame conversion (inertia expressed about COM, ROS axes).
- Validate: mass > 0, inertia positive-definite, COM inside bbox.
**Acceptance:** robot does not explode / sink in Gz physics; stable under gravity.

---

### Phase 4 — Full kinematics from assembly mates 🟡 MOSTLY
Complete joint model + kinematic tree.
- Mate → joint mapping: axis, origin from mate geometry. ✅
- Joint types: fixed, revolute, continuous, prismatic ✅; planar, floating ✅ (v2.1.0); **mimic 🔴, ball 🔴 (SDF-only)**.
- Limits: lower/upper/effort/velocity (from mate limits or defaults).
- Tree builder: assembly graph → single-root link tree.
- Detect/handle: multiple roots, kinematic loops, orphan links → user prompt.
**Acceptance:** `check_urdf` clean tree; joints move correctly in joint_state GUI.

---

### Phase 5 — ROS 2 package scaffolding ✅ DONE
Emit a real, buildable ament package.
- `package.xml` deps: robot_state_publisher, xacro, ros_gz_sim, ros_gz_bridge, ros2_control, controller_manager, joint_state_broadcaster (+ type-specific).
- `CMakeLists.txt` install rules (urdf/meshes/config/launch/worlds).
- Xacro refactor: macros, property params (use `xacro/urdf` from ros2 index).
- Naming sanitize (link/joint/pkg names → valid ROS identifiers).
**Acceptance:** `colcon build` succeeds; `xacro` expands with no error.

---

### Phase 6 — Gazebo Harmonic integration ✅ DONE
Robot loads + simulates in Gz Harmonic.
- `<gazebo>` per-link blocks: material, collision (mu, kp/kd), self-collide.
- World file: ground plane, sun, physics (DART), `/clock`.
- `ros_gz_sim` create/spawn; `ros_gz_bridge` yaml (clock, tf, joint_states, cmd_vel, odom).
- Use sdformat/`ros_gz` stack (see gz index).
**Acceptance:** `gz sim` + spawn shows robot upright; bridge topics flow into ROS.

---

### Phase 7 — ros2_control wiring 🟡 PARTIAL
Robot is commandable.
- `ros2_control` xacro block: joints + command/state interfaces. ✅
- `gz_ros2_control` plugin tag (SystemInterface in sim). ✅
- `controllers.yaml`: `joint_state_broadcaster` ✅ + `joint_trajectory_controller` ✅. **Auto-select 🔴:**
  - wheels detected → `diff_drive_controller` (or ackermann). 🔴
  - serial arm → `joint_trajectory_controller`. (default emitted, not auto-detected)
  - gripper → `gripper_controller`. 🔴
- Controller spawners in launch. ✅
**Acceptance:** `ros2 control list_controllers` active; `/cmd_vel` or trajectory moves robot.

---

### Phase 8 — Sensors 🔴 STUB
Mount + simulate sensors from SW. (SensorDef/Imu/Camera/Lidar models exist; extraction returns empty — `Sw2gzPipeline.cs:66`.)
- Map SW coordinate systems / reference geometry → sensor frames.
- Types: RGB camera, depth/RGBD, 2D/3D lidar, IMU (+ GPS, contact later).
- Emit gz-sensors `<gazebo><sensor>` + bridge entries + optical frames.
- Wizard UI: add/place/configure sensor, set rate/FOV/range.
**Acceptance:** sensor topics publish in ROS; data visible in RViz.

---

### Phase 9 — Launch & one-command bringup ✅ DONE
Single launch starts everything.
- `bringup.launch.py`: gz_sim → spawn → robot_state_publisher → bridge → controller spawners → rviz (optional).
- Args: world, gui on/off, use_sim_time.
**Acceptance:** one `ros2 launch` → fully running, drivable robot, no extra steps.

---

### Phase 10 — Application enablement (optional/advanced) 🔴 NONE
Make export usable by Nav2 / MoveIt out of box.
- Mobile path: Nav2 params + map + `slam_toolbox` config + `robot_localization` (EKF odom+imu).
- Arm path: SRDF gen (groups, end-effector, collisions) + MoveIt2 configs (kinematics, OMPL, controllers) — Setup-Assistant-equivalent automation.
**Acceptance:** mobile → Nav2 reaches a goal in sim; arm → MoveIt plans + executes.

---

### Phase 11 — Validation, preview, self-test 🟡 PARTIAL
Trust the output.
- Validators: tree integrity, units, inertia, mesh-path existence, name collisions, duplicate joints. ✅
- In-wizard preview (3D dry-run of tree/links). 🟡 legacy tree UI only.
- Round-trip smoke test: export → `colcon build` → headless `gz sim` launch → assert robot present + controllers active. ✅
- Structured error/warning report in UI. ✅
**Acceptance:** bad assemblies fail early with clear message; good ones pass smoke test.

---

### Phase 12 — Packaging & distribution 🟡 PARTIAL
Ship the result.
- Output as folder + zip; auto `README.md` (build/run steps). 🟡 README ✅, zip 🔴.
- Version stamp; optional Dockerfile / devcontainer for reproducible build. 🔴
- Re-export / update existing package without clobbering user edits (diff-aware). 🔴
**Acceptance:** fresh machine: unzip → build → launch works from README alone.

---

## Cross-Cutting Concerns (every phase)
- **Units & frames:** consistent mm→m, SW axes → ROS REP-103 (x fwd, z up).
- **Naming:** sanitize once, reuse everywhere (`PackageNameSanitizer` pattern).
- **Determinism:** same assembly → same output (stable IDs/ordering).
- **Idempotent re-export:** don't destroy user changes.
- **Error surfacing:** validators feed one UI report, not silent failures.

## Suggested Build Order (remaining work, v2.1.0 onward)
Original 2–9 path largely landed. Real next order:
1. **materials.xacro stub** — fastest correctness win (color in URDF, not just DAE).
2. **Make P4 pipeline default** — retire legacy URDFRobot-tree path; one export path to maintain.
3. **Sensors extraction** (Phase 8) — wire SW COM detection into existing SensorDef models.
4. **Control auto-type** (Phase 7) — wheel/arm/gripper detection → correct controller.
5. **mimic + ball joints + gz asset/world export modes** — finish kinematics edge types alongside SdfModel/SdfWorld routing.
6. **Apps + packaging** (10, 12) — Nav2/MoveIt, zip/Docker, idempotent re-export.

## Dependency Graph (phase → needs)
```
2 → 1        5 → 2,3,4
3 → 1        6 → 5
4 → 1        7 → 6
8 → 6        9 → 7,8
10 → 9       11 → 9
12 → 11
```
