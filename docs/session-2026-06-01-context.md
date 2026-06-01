# SW2GZ — Session Context / Handoff (2026-06-01)

Resume point for this working session. Captures what was done, decided, and what's next. Pairs with the detailed [change log](session-2026-06-01-changes.md), [current architecture](architecture.md), and [roadmap](superpowers/specs/2026-06-01-robust-exporter-architecture.md).

> Repo is **not** git-tracked — all edits live in the working tree. Main `SW2GZ.csproj` needs SolidWorks COM to build; the net8 `Test/SW2GZ.Writers.Test.csproj` is the verifiable loop.

---

## 1. Outcome snapshot
- **Tests: 254/254 green** (`dotnet test Test\SW2GZ.Writers.Test.csproj`).
- Code: bug fixes + dead-code removal + a safe feature (RosNameSanitizer), all verified.
- Docs: current-state architecture, future roadmap, UI mockups, and a research reference cache.
- Stage: **planning/architecture locked; implementation deferred** by user ("enhance architecture for now, implement later").

## 2. Code changes (this session) — see [change log](session-2026-06-01-changes.md) for detail
| Area | Change | Verified |
|---|---|---|
| csproj | Release `OutputPath` → `bin\Release` | build-config |
| Sw2gzPipeline | URDF XML value escaping (`SecurityElement.Escape`) | ✅ |
| Logger | `AppendToFile=true` (keep log history) | inspection |
| StlWriter | vertex-index bounds guard | ✅ |
| Writers | XML escaping in XacroWriter/Ros2ControlWriter/SdfWorldWriter/GzPluginTags | ✅ |
| Sw2gzPipeline | transactional write (rollback on failure) | ✅ |
| SwSurface | COM release (MeshTessellator, MassProperties) | ⚠ inspection-only (`#if SW_INTEROP`) |
| MathOPS | divide-by-zero guard | ⚠ inspection-only |
| Build | **new `RosNameSanitizer`** (case-preserving link/joint names) at LinkBuilder chokepoint + tests | ✅ |
| Deleted | dead `SW2GZ/URDFWriter.cs` (1001 LOC), orphaned `SW2GZ/Test/` (12 files) | ✅ |

⚠ = `#if SW_INTEROP` / non-test code; must compile-verify on a SolidWorks workstation.

## 3. Artifact index (all created this session)
| File | Purpose |
|---|---|
| [docs/architecture.md](architecture.md) | **Current** state (as-is) contributor reference |
| [docs/superpowers/specs/2026-06-01-robust-exporter-architecture.md](superpowers/specs/2026-06-01-robust-exporter-architecture.md) | **Future** roadmap: `RobotModel` keystone, 9 phases, resolved decisions |
| [docs/session-2026-06-01-changes.md](session-2026-06-01-changes.md) | Detailed change log + legacy-cleanup audit |
| [docs/ui-mockups/wizard-flow.html](ui-mockups/wizard-flow.html), [wizard-flow-v2.html](ui-mockups/wizard-flow-v2.html) | UI wizard mockups (v2 = SW-aligned, professional, sensors step) |
| [docs/reference/ros2-control.md](reference/ros2-control.md) | ros2_control + gz_ros2_control requirements |
| [docs/reference/gz-harmonic.md](reference/gz-harmonic.md) | Gz Harmonic SDF/sensors/plugins/bridge |
| [docs/reference/nav2.md](reference/nav2.md) | Nav2 mobile-base model requirements |
| [docs/reference/moveit2.md](reference/moveit2.md) | MoveIt 2 SRDF + config-bundle requirements |
| ⛔ `docs/reference/urdf-sdf-conventions.md` | **NOT written** — research agent interrupted; finish later |

## 4. Decisions log
- **UI approach:** Unified **WPF + MVVM**; `SW2GZ.UI.Core` = **netstandard2.0** (net48 add-in + net8 tests consume). UI scope = **everything** (full replacement). Drivers: dated look + confusing flow.
- **Architecture keystone:** one immutable **`RobotModel`** aggregate; all serializers consume it; resolves the dual-pipeline.
- **Coordinate system:** first-class **SW (Y-up) → ROS/Gz REP-103 (Z-up)** change-of-basis on `RobotModel.Meta`, applied uniformly (poses/axes/COM/inertia/mesh/sensors). Sensors page sets pose in target frame.
- **Collision:** convex hull only first (no VHACD).
- **Control:** ros2_control; default `joint_state_broadcaster` + `joint_trajectory_controller`; others later.
- **Legacy:** **deleted**, not retained.
- **Sensors:** dedicated wizard page (assign→link, pose/orientation, preview).

## 5. Research findings — plug-and-play gaps (from §3 reference docs)
What a robust exporter must EMIT beyond today's skeleton:
- **ros2_control:** gz_ros2_control plugin tag + `<parameters>`; per-joint command/state interfaces + limits + `initial_value`; controllers.yaml with real joint lists; `joint_state_broadcaster` first; spawner chaining in launch; mobile-base controller selection; mimic-joint rules.
- **Gz Harmonic:** inertial + visual + **collision** + friction per link; joint axis/limits/poses; **sensors** (imu/lidar/camera/contact/ft/navsat) + world system plugins + `gz_frame_id`; full bridge YAML (tf_static, cmd_vel, odom, per-sensor); spawn via `ros_gz_sim create`.
- **Nav2 (mobile mode only):** `base_footprint`, odom + `odom→base_link` TF, diff_drive wiring, `laser_link` + lidar plugin, `robot_radius`/footprint, `wheel_separation`/`radius`, `nav2_params.yaml` + bringup. Gate behind a "Mobile Base" mode.
- **MoveIt 2 (arm mode):** generate full `*_moveit_config`: **SRDF** (planning group from chain, virtual joint, named poses, self-collision matrix from adjacency), `kinematics.yaml`, `joint_limits.yaml`, `moveit_controllers.yaml`, `ros2_controllers.yaml`; name invariance across all files.
- **Baseline (urdf-sdf-conventions — TODO, doc missing):** REP-103 units/frames, robot_state_publisher, package:// mesh resolution, visual vs simplified collision, valid naming/namespacing.

**Implication for roadmap:** add target-specific export "profiles/modes" — **Manipulator (MoveIt)** vs **Mobile Base (Nav2)** vs **plain Gz** — each emitting its config bundle. Fold into roadmap phases (P2 joints → P6 sensors → new P10 MoveIt bundle, P11 Nav2 bundle).

## 6. Open task
- `#22` (task list): transition to **writing-plans for P1 (`RobotModel` + `RobotModelBuilder` + URDF serializer)** — pending, on user go.

## 7. Next steps (resume here)
1. Finish the missing [urdf-sdf-conventions reference](reference/) doc (re-run the interrupted research agent).
2. Update the roadmap with target-profile phases (MoveIt bundle, Nav2 bundle) from §5.
3. When ready to build: `writing-plans` → **P1 RobotModel** (keystone), then **P2 joints** (spike SW mate→joint extraction on a SolidWorks workstation — biggest unknown, COM, unverifiable off-box).
4. Verify `#if SW_INTEROP` edits (COM release) + `MathOPS` compile on a SolidWorks machine.

## 8. Key facts to remember
- Build/test loop: `dotnet test Test\SW2GZ.Writers.Test.csproj` (net8, no SolidWorks) = 254 tests.
- Add-in not buildable here (needs SolidWorks interop DLLs); COM code behind `#if SW_INTEROP` compiles as throwing skeletons in the test build.
- Test project **cherry-picks** sources via `<Compile Include="..\SW2GZ\…">` — new pure-C# source must be added there too.
- Target lock: ROS 2 **Jazzy** + Gz **Harmonic** (SDF 1.10).
