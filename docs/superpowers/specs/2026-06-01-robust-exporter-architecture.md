# SW2GZ — Robust Exporter Architecture & Roadmap

**Status:** Draft for review · **Date:** 2026-06-01 · **Type:** architecture plan (design only — implement later)

Goal: take SW2GZ from "exports a single-link RobotPackage skeleton" to a **robust SolidWorks → ROS 2 + Gz Harmonic exporter** that produces simulation-ready, control-ready packages. This doc enhances the architecture to cover the full feature scope, verified against current Gz Harmonic / ros2_control / URDF docs, and decomposes the work into sequenced phases. Each phase gets its own spec → plan → implementation later.

---

## 1. Verified target feature set

Sources: gz_ros2_control (Jazzy), ros2_control hardware-interface docs, Gazebo Harmonic sensors, URDF spec (see §8).

### 1.1 Kinematics
- Joint types: **fixed, revolute, continuous, prismatic** (planar/floating rare).
- Per joint: `origin` (parent→child transform), `axis`, `<limit>` (lower/upper/effort/velocity), `<dynamics>` (damping/friction), `<mimic>`, `<safety_controller>`.
- Tree: single root link, no cycles, every child reached by exactly one joint.

### 1.2 Inertial
- `mass`, `origin` (COM), inertia tensor `ixx…izz` **expressed about the COM in the link frame**.
- Multi-body links: combine via **parallel-axis theorem** at real part offsets (not identity).

### 1.3 Geometry
- Visual mesh (DAE/STL), `<material>`/color from SW appearances.
- Collision: real geometry — **convex hull** or convex **decomposition**, or primitive fallback; AABB is a stopgap only.
- Correct **units**: URDF is **meters / kg / radians**. Must confirm SW API unit system and scale if needed.

### 1.4 ros2_control (drives the robot in sim)
- Gazebo plugin: `<plugin filename="gz_ros2_control-system" name="gz_ros2_control::GazeboSimROS2ControlPlugin"><parameters>…controllers.yaml</parameters></plugin>`.
- `<ros2_control name="GazeboSimSystem" type="system"><hardware><plugin>gz_ros2_control/GazeboSimSystem</plugin></hardware>`.
- Per **non-fixed** joint: `<command_interface>` (position|velocity|effort, optional `<param name="min/max">`) + `<state_interface>` (position, velocity, effort), optional `<param name="initial_value">`.
- Mimic joints: **no** command_interface, state only.
- `controllers.yaml`: `joint_state_broadcaster` + chosen controller(s) (joint_trajectory / position / velocity / effort / diff_drive…).

### 1.5 Gz Harmonic / SDF
- Per-link gazebo tags: material, friction (`mu`/`mu2`), `self_collide`, sensor blocks.
- **Sensors**: imu (`gz-sim-imu-system`), gpu_lidar (`gz-sim-sensors-system`), camera/depth/rgbd, contact (`gz-sim-contact-system`), force_torque, navsat. Each: SDF `<sensor type=…>` on a link, `<pose>`, `<topic>`, `<gz_frame_id>`, **world-level system plugin**, and a **ros_gz_bridge** entry.
- World: physics engine + params, gravity, sun, ground, optional includes.

### 1.6 ROS 2 bridging & scaffolding
- `ros_gz_bridge` yaml: clock, joint_states, tf (have) **+ auto per-sensor entries**.
- Launch: spawn entity, controller spawners per controller, robot_state_publisher, RViz.
- `package.xml` deps reflect enabled features (sensor msgs, ros2_controllers, ros_gz_*).

### 1.7 Coordinate convention (SW → ROS/Gz)
SolidWorks default frame (X-right, **Y-up**, Z-toward-viewer) differs from ROS/Gz REP-103 (right-handed, **Z-up, X-forward, Y-left**). A configurable change-of-basis `R(sw→ros)` — derived from a chosen assembly reference coordinate system / up-axis — must be applied **uniformly** to link origins, joint origins + axes, COM, **inertia tensor** (`I' = R·I·Rᵀ`), exported mesh geometry, and sensor poses. Wrong handedness silently mirrors/rotates the whole robot.

---

## 2. Current state vs target (gap analysis)

| Capability | Current SW2GZ | Gap |
|---|---|---|
| Links | ✓ single body, name sanitized | multi-body per link |
| Joints | ✗ **empty list** (`JointBuilder` exists, unused) | **derive from SW mates**: type, axis, origin, limits, dynamics, mimic |
| Inertial | ✓ mass/COM/tensor from SW | aggregated **at identity** → parallel-axis; verify units |
| Collision | ✗ **AABB box** (`ConvexHullCollider` misnamed) | convex hull / decomposition / primitives |
| Materials | ✗ empty `materials.xacro` | SW appearance → `<material>` |
| ros2_control | partial (writer exists, **no joints fed**) | per-joint interfaces, controller selection |
| Sensors | ✗ none | imu/lidar/camera/ft/contact + bridge + world plugins |
| World | ✓ empty (physics/sun/ground) | options (gravity, engine, includes) |
| Bridge | ✓ clock/jointstate/tf | per-sensor auto entries |
| Validation | ✓ XML well-formed only | structural: tree, control↔joint, inertia PD, mesh exists, units |
| **Coordinate frame** | implicit / unverified | **SW→ROS·Gz (REP-103) change-of-basis**, configurable, applied to all poses/axes/inertia/sensors |
| Pipeline | RobotPackage only (new); SdfModel/World on **legacy** path | unify; **legacy deleted** (see §9) |
| UI | 3 mixed stacks (WinForms/WPF/PMP) | unified WPF wizard (separate design doc) |

---

## 3. Enhanced architecture

### 3.1 Central idea — a `RobotModel` aggregate
Today `Sw2gzPipeline` builds links inline and hand-concatenates URDF strings; the legacy path builds a separate `URDFRobot` tree. **Introduce one immutable domain aggregate** assembled once and consumed by all serializers:

```
RobotModel
 ├─ Links[]      (name, inertial, visuals[], collisions[], material, gazebo props)
 ├─ Joints[]     (name, type, parent, child, origin, axis, limit, dynamics, mimic)
 ├─ Sensors[]    (type, link, pose, params, topic, frame, bridge spec)
 ├─ Control      (per-joint interfaces, controllers[])
 └─ Meta         (pkg, profile=distro+gz, author/email/license, units, frame convention)
```

This is the seam that **resolves the dual pipeline (ADR-001)**: both RobotPackage and SDF modes serialize the same `RobotModel`; legacy `URDFRobot`/`ExportHelper` is retired once serializers reach parity.

### 3.2 Layer changes (build on existing structure)

**SwSurface (read side) — expand abstractions:**
- `IAssemblyWalker` → also surface **mates** (joint relationships + geometry): new `MateSpec` (exists as stub) carrying type/axis/origin/limits.
- New `IMaterialSource` (per-body appearance/color), `IUnitsContext` (document → meter scale), and for UI `ISelectionService` (viewport picks), `IThemeService` (SW light/dark).
- All COM-only, behind interfaces, mocked in tests (existing pattern).

**Build (domain) — assemble `RobotModel`:**
- Wire `JointBuilder` (exists, tested) into assembly: mates → `UrdfJoint`.
- Fix `InertialAggregator` to apply parallel-axis at real poses; unit-scale.
- Replace AABB with `ConvexHullCollider` (real hull) + primitive option; keep AABB as explicit fallback (rename current type honestly).
- New `Sensor` model + builders.
- `RobotModelBuilder` orchestrates links+joints+sensors+control+meta; applies the SW→ROS/Gz change-of-basis + unit scale once, centrally (never per-writer).

**Write (serialize) — one model, many emitters:**
- `UrdfSerializer` (RobotModel → URDF/Xacro) using a **safe XML model** (consolidate on the `URDF/` element classes' `XmlWriter` approach or `XElement`, not string concat) — supersedes pipeline `BuildUrdfBodyXml` and legacy.
- Expand writers: materials, gazebo per-link, ros2_control per-joint, sensor SDF blocks, per-sensor bridge, controllers.yaml per controller, world options.
- Keep stateless-writer pattern.

**Validate — structural pre-flight (compose into `ValidationReport`):**
- Tree connectivity / single root / acyclic; every non-fixed joint present in ros2_control; inertia positive-definite + non-zero mass; mesh files exist; name uniqueness; unit sanity. Reuse `OutputValidator` for XML well-formedness.

**Pipeline — unified orchestration:**
`Sw2gzPipeline.Run`: SwSurface read → `RobotModelBuilder` → validate(model) → serialize per mode → write tree (transactional, already built) → validate(output). Mode dispatch unified; legacy branch removed at the end.

**UI — `SW2GZ.UI.Core` (netstandard2.0) + WPF views (net48):**
- Wizard edits a `RobotModel` (override names, joint type/limits, collision choice, materials, add/configure sensors, pick controllers). MVVM VMs unit-tested; SW services behind interfaces. (Full UI design: `docs/ui-mockups/` + forthcoming UI design doc.)

### 3.3 Cross-cutting
- **Units:** confirm SW API returns meters/kg; add `IUnitsContext` scale + validation. URDF/SDF require SI.
- **Coordinate frame:** a single `CoordinateConvention` (on `RobotModel.Meta`) holds `R(sw→ros)`; `RobotModelBuilder` applies it uniformly to every pose/axis/COM/inertia/mesh/sensor. Explicit handedness/orthonormality validation + a UI axis-preview, because errors here are silent. Sensors page sets sensor pose in the **target (ROS/Gz) frame**.
- **Naming:** `RosNameSanitizer` (done) for links; extend to joints/sensors/frames.
- **Injection safety:** keep `SecurityElement.Escape` defense-in-depth even with sanitization (done).
- **Testing:** every builder/serializer/validator unit-tested off pure `RobotModel`; golden packages per profile + per feature (joints, sensors, materials).

---

## 4. Phased roadmap (each phase = own spec → plan → implement)

| Phase | Status | Deliverable | Depends on | Notes |
|---|---|---|---|---|
| **P1** | ✅ | `RobotModel` aggregate + `RobotModelBuilder` + `UrdfSerializer` (links only) replacing string-concat | — | foundation; behavior-parity with today, fully tested |
| **P2** | ⬜ | **Joints**: mates→joints (type/axis/origin/limits/dynamics/mimic) + ros2_control per-joint + controllers.yaml | P1 | the #1 functional gap; needs SW mate extraction (COM) — SW workstation required |
| **P3** | 🟡 | Inertia parallel-axis + multi-body links + **units** verification/scaling | P1 | correctness; **P3-math shipped** (R·I·Rᵀ + `UnitsScaler` schema); **P3-units wiring deferred** (needs SW reader) |
| **P4** | ✅ | Collision: **convex hull** + primitive option (no external lib; decomposition deferred) | P1 | sim fidelity — real `QuickHull3D` + AABB fallback |
| **P5** | 🟡 | Materials/visual color from SW appearances | P1 | domain + writers shipped (`MaterialDef`, `IAppearanceSource`, `inc/materials.xacro`); **SW COM appearance reader deferred** (P5-COM) |
| **P6** | 🟡 | **Sensors** (imu/lidar/camera/ft/contact) + world system plugins + per-sensor ros_gz_bridge; **dedicated sensors page** (assign sensor→link, pose/orientation in target frame, axis preview) | P1, P2 | biggest new surface; UI-heavy — **data path shipped** (7 sensor records + `SdfSensorBlocks` + `SdfSensorPlugins` + bridge entries); **UI + SW COM sensor reader deferred** |
| **P7** | ⬜ | **SDF serializer** (SdfModel/SdfWorld via `RobotModel`) + **delete legacy** `ExportHelper`/`URDFRobot` (legacy not retained — decision §9.4) | P1–P6 | all 3 modes on one path; ~3000 LOC deleted |
| **P8** | ⬜ | UI: `SW2GZ.UI.Core` + WPF wizard editing `RobotModel` (separate UI design doc) | P1+ | can start after P1 model exists |
| **P9** | ✅ | Structural validators + golden tests per feature | each phase | runs alongside — `RobotModelValidator` (12 structural checks) wired pre-write |

Sequencing logic: **P1 is the keystone** (the model everything else reads/writes). P2 (joints) is highest user value. P8 (UI) can proceed in parallel once P1 lands the model. P7 retires legacy only after serializers reach parity.

### 4.1 Implementation status

As of `v2.1-revamp` (HEAD `550486e`), six phases have shipped in the test suite (413/413 green): **P1** `RobotModel` keystone, **P3-math** `InertialAggregator` rotation + `UnitsScaler`, **P4** `QuickHull3D`, **P5** materials/appearances, **P6-data** sensors, **P9** `RobotModelValidator`. Phases marked 🟡 or ⬜ remain open; **P2 / P3-units / P5-COM / P6-COM** require a SolidWorks workstation to verify the COM boundary.

---

## 5. Risks
- **SW mate → joint extraction** is COM-heavy and unverifiable off a SolidWorks workstation; biggest unknown (P2). Spike first.
- **Units**: silent mm/m error corrupts every export — verify early (P3), add validation.
- **Legacy retirement (P7)** must wait for serializer parity or SdfModel/World regress.
- COM lifetime: extend the `Marshal.ReleaseComObject` discipline (started this session) to new read services.
- Collision decomposition may need a third-party lib (VHACD) — license check.

## 6. Out of scope (for now)
- Closed-loop/parallel mechanisms (URDF is a tree); document as limitation.
- Live SW↔sim sync; gazebo classic.

## 7. Testing strategy
- Pure-domain unit tests on `RobotModel` builders/serializers/validators (net8 test project, no COM).
- Golden packages per ROS/Gz profile and per feature flag (joints on/off, sensors, materials).
- Mate-extraction + selection behind mockable interfaces.

## 8. Sources
- [gz_ros2_control (Jazzy)](https://control.ros.org/jazzy/doc/gz_ros2_control/doc/index.html)
- [ros2_control hardware interface types (Jazzy)](https://control.ros.org/jazzy/doc/ros2_control/hardware_interface/doc/hardware_interface_types_userdoc.html)
- [Gazebo Harmonic sensors](https://gazebosim.org/docs/harmonic/sensors/)
- [Use ROS 2 to interact with Gazebo](https://gazebosim.org/docs/latest/ros2_integration/)
- [Describing robots with URDF — Articulated Robotics](https://articulatedrobotics.xyz/tutorials/ready-for-ros/urdf/)

---

## 9. Resolved decisions (2026-06-01)
1. **Collision** → **convex hull only** first (no external lib). Minimal + recommended. Convex decomposition (VHACD) deferred as a later option.
2. **Sensors UI** → **dedicated sensors page**: assign sensor → link, set pose/orientation **in the target (ROS/Gz) frame** with axis preview. Drives the §1.7 coordinate-convention work — SW frame and Gz (Harmonic) frame are both tracked and the SW→Gz change-of-basis is applied so sensor alignment is correct.
3. **Control** → **ros2_control**, default `joint_state_broadcaster` + `joint_trajectory_controller` (commands any N joints generically). Position/velocity/effort controllers and `diff_drive` selectable later. Recommended default — easiest to mobilize arbitrary joints.
4. **Legacy** → **not retained**. The `RobotModel` pipeline is the only implementation; all three modes (RobotPackage / SdfModel / SdfWorld) are served by `RobotModel` serializers, and `ExportHelper`/`URDFRobot`/related legacy is deleted (P7). No compatibility flag.
5. **`SW2GZ.UI.Core` TFM** → **netstandard2.0** (consumed by the net48 add-in and the net8 test project). Recommended.
