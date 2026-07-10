# SW2GZ — Mode × Feature Matrix

Last verified 2026-07-10 against `main` (tag `v2.8.0`).

Maps every Gazebo SDF / sim-developer construct (the sim-dev-side modules:
Sdformat, Sim, Sensors, Physics, Rendering, Gui, Transport, Launch) against the
three SW2GZ export **modes**, and decides where each missing piece *should*
live.

Modes recap:
- **Robot** — assembly → URDF (links/joints/inertials) + ros2_control + launch +
  ros_gz bridge. Articulated, drivable. Spawns into a generated Gz world.
- **World** — assembly → one self-contained static `<world>.sdf` + meshes +
  scene/GUI. Review-only environment.
- **Asset** — single part / sub-assembly → reusable Gz `model://` (model.config +
  model.sdf + mesh). A drop-in building block.

Legend: ✅ implemented · ⚠️ partial · ❌ absent · ➖ N/A by design ·
**➕ = recommended to add (and where)**

---

## A. Geometry & appearance

| Component | Robot | World | Asset | Decision / where it belongs |
|---|:--:|:--:|:--:|---|
| Mesh visual (DAE export) | ✅ | ✅ | ✅ | Core to all three. Done. |
| SW color → `<material>` | ⚠️ neutral | ✅ | ✅ | ➕ carry SW color into Robot visuals (currently neutralized). |
| Smooth/welded normals | ⚠️ | ✅ | ✅ | ➕ enable on Robot path too. |
| Primitive geometry (box/sphere/cyl) — **collision** | ❌ | ❌ | ✅ | Asset done (fit to mesh AABB). ➕ World next. |
| Primitive geometry (box/sphere/cyl) — **visual** | ❌ | ❌ | ❌ | Not started anywhere — visual is always mesh. Low priority. |
| PBR / textures / UV | ❌ | ❌ | ❌ | Deferred everywhere. Low priority. |

## B. Physics & collision

| Component | Robot | World | Asset | Decision / where it belongs |
|---|:--:|:--:|:--:|---|
| Collision (mesh) | ✅ | ✅ | ✅ | Done. |
| `<surface><friction>` (μ) | ⚠️ | ✅ | ✅ | World done (tunable `WorldFriction`, incl. ground plane). |
| Inertial (mass/COM/tensor) | ✅ | ➖ | ⚠️ dynamic-only | World is static → N/A. Asset emits inertial only when dynamic. |
| Physics block (engine/step/RTF) | ✅ | ✅ | ➖ | Asset is a model fragment → no world physics. Done. |
| Solver / contact params | ❌ | ❌ | ❌ | ➕ **World** advanced panel (low priority). |

## C. Articulation & actuation

| Component | Robot | World | Asset | Decision / where it belongs |
|---|:--:|:--:|:--:|---|
| `<joint>` articulation | ✅ URDF | ❌ static | ✅ | Asset done (1-DOF fixed/revolute/continuous/prismatic to world). |
| Dynamic (non-static) model | ✅ | ❌ | ✅ | Asset done — a joint forces the model dynamic. |
| DiffDrive / Ackermann | ❌ | ❌ | ➖ | ➕ **Robot** mode (it *is* the robot). |
| JointController / PositionController | ❌ | ❌ | ➖ | ➕ **Robot** mode. |
| ros2_control | ✅ | ➖ | ➖ | Robot only. Done. |

## D. Sensors

| Component | Robot | World | Asset | Decision / where it belongs |
|---|:--:|:--:|:--:|---|
| Per-link `<sensor>` blocks | ✅ | ❌ | ❌ | Robot done (camera/lidar/imu on links). |
| Sensor-family **systems** (toggles) | ⚠️ | ✅ | ➖ | World toggles the systems; done. |
| Placed sensor on a world/asset model | ❌ | ❌ | ✅ | Asset done (optional camera/gpu_lidar/imu on the asset link). |
| `<noise>` model config | ⚠️ | ❌ | ❌ | ➕ surface noise in **Robot** sensor UI. |

## E. World environment

| Component | Robot | World | Asset | Decision / where it belongs |
|---|:--:|:--:|:--:|---|
| Gravity / wind | ⚠️ default | ✅ | ➖ | World owns it. Done. |
| Spherical coords (geo) | ❌ | ✅ | ➖ | World only. Done. |
| Scene (ambient/bg/grid/shadows/sky/fog) | ⚠️ default | ✅ | ➖ | World owns it. Done. |
| Sun (parametric) | ✅ default | ✅ | ➖ | Done. |
| Extra lights (point/spot/dir) | ❌ | ✅ | ➖ | World done (2 configurable fill-light slots in Settings). |
| Ground plane | ✅ | ✅ | ➖ | Done. |

## F. GUI / visualization

| Component | Robot | World | Asset | Decision / where it belongs |
|---|:--:|:--:|:--:|---|
| `<gui>` block + framed camera | ⚠️ | ✅ | ➖ | World done; ➕ give Robot's spawn-world a framed camera too. |
| Standard panels (Scene/Control/Stats/Tree) | ⚠️ | ✅ | ➖ | World done. |
| KeyPublisher teleop | ❌ | ✅ | ➖ | World done. |
| Extra GUI plugins (Inspector/Transform/ViewAngle/ImageDisplay) | ❌ | ❌ | ➖ | ➕ **World** (optional toggles, low priority). |
| In-app three.js preview | ✅ | ✅ | ✅ | Done across all modes. |

## G. Sim systems (world `<plugin>`)

| Component | Robot | World | Asset | Decision / where it belongs |
|---|:--:|:--:|:--:|---|
| Physics / SceneBroadcaster / UserCommands | ✅ | ✅ | ➖ | Baseline. Done. |
| Sensor systems (imu/sensors/contact/ft/navsat) | ⚠️ | ✅ | ➖ | World toggles. Done. |
| TriggeredPublisher (teleop) | ❌ | ✅ | ➖ | World done. |
| PosePublisher / OdometryPublisher | ❌ | ❌ | ➖ | ➕ **Robot** (odometry out). |
| LogRecord / LogPlayback | ❌ | ❌ | ➖ | ➕ **World** (optional, low priority). |

## H. ROS 2 / runtime integration

| Component | Robot | World | Asset | Decision / where it belongs |
|---|:--:|:--:|:--:|---|
| URDF | ✅ | ➖ | ➖ | Robot only. Done. |
| Launch file (ros_gz_sim) | ✅ | ✅ | ➖ | World done — standalone `launch_world.py`, no colcon needed. |
| ros_gz bridge (YAML) | ✅ | ✅ | ➖ | World done — `ros_gz_bridge.yaml` (`/clock` always, `/cmd_vel` when teleop on). |
| Clock bridge | ⚠️ | ✅ | ➖ | World done — folded into the launch+bridge above. |
| Package layout (model.config) | ➖ | ➖ | ✅ | Asset only. Done. |

---

## Summary — what's left, by mode

**Robot** (make the robot a first-class sim citizen)
1. ➕ Actuation systems: DiffDrive / JointController / JointPositionController
   (`ActuationBackend.GzPlugin` — unimplemented enum value today).
2. ➕ PosePublisher / OdometryPublisher.
3. ➕ Sensor `<noise>` config in the sensor UI.

**World** (largely done — launch+bridge, friction, and lights all shipped)
1. ➕ Solver / contact params advanced panel (low priority).
2. ➕ Extra GUI plugins (Inspector/Transform/ViewAngle/ImageDisplay), optional.
3. ➕ LogRecord / LogPlayback (optional, low priority).

**Asset** (largely done — joints, sensors, and collision primitives all shipped)
1. ➕ Primitive **visual** geometry (today only collision gets a primitive override; visual stays mesh).

### Design guardrails
- **World stays static-environment**; articulation/sensors enter via **Asset**
  models spawned into the world, not by mutating World's own models.
- **Robot owns articulation + control + ROS**; World owns environment + scene;
  Asset is the shared, reusable unit that can be static, dynamic, or
  sensor-bearing.
