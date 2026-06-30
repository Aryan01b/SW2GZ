# Changelog

All notable changes documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [2.6.0] — 2026-06-30

**World & Asset feature build-out** — completes the Robot/World/Asset
mode×feature matrix and ships the wizard UI + persistence for the new knobs.
Live-tested in SOLIDWORKS 2025.

### Added

- **World mode — runnable & richer.**
  - Standalone launch + ros_gz bridge: every World export writes
    `<pkg>/launch_world.py` + `<pkg>/ros_gz_bridge.yaml` (path-relative, no
    colcon) so the world runs via `ros2 launch <pkg>/launch_world.py`. Bridge =
    `/clock` always + `/cmd_vel` when keyboard teleop is on.
  - Explicit collision friction (`<surface><friction>`, μ tunable, default 1.0)
    so spawned robots grip the floor — exposed as **Ground friction μ** in the
    World Settings panel.
  - Extra fill lights (point/spot/directional) beyond the sun — **Lights**
    section in World Settings (2 configurable slots).
- **Asset mode — articulated / sensor-bearing / cheap-collision.**
  - 1-DOF joint to the world (fixed/revolute/continuous/prismatic) → door/lift/
    wheel/lever props.
  - Optional sensor on the asset link (camera/gpu_lidar/imu), reusing the
    robot-side sensor writer.
  - Primitive collision override (box/sphere/cylinder fit to the mesh AABB);
    visual stays mesh.
  - All three exposed in the Create-Asset wizard Surface step; persisted in
    `Sw2gzDoc`.

### Fixed

- Export-complete dialog showed the Robot ament-workspace run instructions
  (`<pkg>_ws`, `colcon build`, `gz_sim.launch.py`) for **all** modes; World/Asset
  now show their real self-contained run command.

### Notes

- 801 → 849 tests. All new knobs are opt-in/default-safe (unset = byte-identical
  output). Pure writers/config source-linked into the net8 test project.

## [2.1.1] — 2026-06-04

**Stabilization release** — bundles all Phase-1 work shipped in 2.1.0 with a
focused round of hardening on the boundary code paths most likely to bite a
real user: ribbon callbacks, the Export dialog, and the export pipeline.
No new features, no API breaks.

### Fixed (post-tag patches before release)

- **SolidWorks→ROS coordinate frame.** The `CoordinateConvention.SwToRos`
  field existed but was never applied: every export wrote SW-native
  coordinates straight into URDF/SDF, leaving the robot on its side in
  Gz / RViz (SW Y-up vs ROS Z-up). Now configurable per-assembly via
  `Sw2gzExportConfig.SwUpAxis` / `SwForwardAxis` (defaults: `+Y` up,
  `+Z` forward — the stock SW template), built into a rotation matrix by
  the new pure `SwToRosRotation` helper, threaded through
  `Sw2gzPipeline.Run`, and applied on the `world_to_<root>` fixed joint
  (URDF) or `ros_gz_sim create -R -P -Y` + world-include `<pose>`
  (SDF Model / SDF World). One rotation at the world anchor — the rest of
  the robot stays in its native SW frame so meshes / link poses / joint
  axes remain self-consistent.
- **Export preview.** New "Preview…" button on the Export dialog runs the
  full pipeline against a temp directory and opens a modal showing the
  generated URDF/xacro (or `model.sdf` for gz modes), the launch.py, the
  `sw2gz_export.log`, and a summary (mode, link/joint counts, coordinate
  convention). Approve → real export runs against the chosen output
  folder; "Back to edit" → returns to the dialog with fields preserved.
  Temp workspace is cleaned up on close (best-effort).
- **TF frames tab in preview.** New `TfTreeFormatter` parses the
  generated URDF (or SDF) and renders an ASCII tree of every link frame
  with its joint type, `xyz`, `rpy` (radians AND degrees), axis, and
  limits — so the SW→ROS rotation on `world_to_<root>` is visible at a
  glance. Pulled into the PreviewDialog as a new "TF frames" tab.
- **Browser-backed 3D preview with live SW sync.** WPF Viewport3D removed.
  Pipeline still writes to a temp dir; new `PreviewServer` (HttpListener
  on 127.0.0.1:&lt;random&gt;) serves the workspace + a three.js scene that
  loads DAE visual meshes via `urdf-loader`. Move a mate in SW → browser
  updates within ~100 ms via /joint_states poll (live read of
  Component2.Transform2 → swing-twist angle around joint axis). Toggles
  for visual / collision / axes / grid / live-poll. `PreviewDialog` is
  now the control center (Open temp folder · Reopen browser · Back ·
  Looks good). New: `PreviewServer`, `SwJointStateSampler`, `SwingTwist`,
  `UI/PreviewWeb/index.html`. Deleted: `Robot3DViewport.cs` (WPF).
- **Link anchors + per-joint URDF origins.** Per-link anchor =
  first part's `Component2.Transform2` (assembly-frame pose). Mesh
  vertices rebased into link-local frame; joint `<origin xyz rpy>` =
  parentAnchor⁻¹ ∘ childAnchor; joint `<axis xyz>` expressed in child
  (joint) frame. New pure helpers: `PoseMath` (compose/inverse/relative),
  `LinkAnchorMap`, `MeshRebase`, `JointOriginResolver`. New optional
  `IComponentPoseSource` boundary (separate interface so existing
  Mock&lt;IAssemblyWalker&gt; tests stay green); `WizardAssemblyWalker`
  implements it via Component2.Transform2 → Pose with quaternion
  extracted by Shepperd's method. Walkers that don't implement the new
  interface fall back to identity anchors → byte-identical legacy output.
  Hinges in Gz now rotate about the child's first-part origin instead
  of the world origin. Mate-geometry pivot extraction (so the hinge sits
  on the actual mate axis) is the next layer — separate change.
- **Mesh export: assembly-frame vertices + multi-part link union.**
  Two long-standing bugs caused the 3D preview to render every link as a
  small blob at world origin:
  - `SolidWorksMeshTessellator` returned vertices in each part's **local**
    origin, ignoring the component's placement in the assembly
    (`Component2.Transform2`). Now baked in via the same row-major
    `MathTransform.ArrayData` convention used by
    `SolidWorksAssemblyWalker.RotateByComponent`.
  - `Sw2gzPipeline` tessellated only the **first** part of a multi-part
    link (`spec.FlattenedPartPaths[0]`), silently dropping every other
    part's geometry. Now loops all parts and unions vertices + indices.
  Joint origins still emit identity until the wizard captures them from
  the selected mate's reference geometry — that's the follow-up that
  makes Gz articulation correct. For now spawn-pose visual is correct.
- **3D render tab in preview.** New WPF `Robot3DViewport`
  (`UrdfTransforms` + `StlBinaryParser`) loads each link's collision STL,
  applies the URDF joint-chain transforms to position it in the world
  frame, and renders the assembled robot via `System.Windows.Media.Media3D`.
  Z-up world axes drawn as colored cylinders (X red, Y lime, Z deep sky).
  Mouse: left-drag orbits, wheel zooms. Embedded as a new "3D" tab in
  the PreviewDialog via `ElementHost`. No external 3D dependency.
- **Wizard Links step — only one part assignable + cross-step reassignment.**
  Two regressions in one fix:
  - `OnFunnelChanged` → `linkTree.Rebuild()` was triggering
    `ActiveLinkChanged` → `LoadLinkSelection` → `model.ClearSelection2(true)`
    on every viewport pick, racing the user's next click and breaking
    accumulating multi-part picks.
  - `OnSelectionboxListChanged` kept firing on Joints/Review (the funnel
    handler still ran on any viewport pick), silently reassigning parts to
    the cached `activeLink`.
  Now `OnFunnelChanged` early-returns when `currentStep != StepLinks`,
  `LoadLinkSelection` is suppressed during Rebuild-triggered ActiveLinkChanged,
  and `ShowStep` clears the viewport selection + nulls `activeLink` when
  leaving the Links step.

### Added

- **`Sw2gz_export.log`** written into every successful workspace
  (`<output>/<pkg>_ws/sw2gz_export.log`). Captures mode, package meta, link
  / joint / sensor counts, and the merged pre-write + post-write validation
  warnings — a bug-report artifact users can ship without re-running.
- **Cross-assembly user defaults** for the Export dialog. Author / Email /
  License / last-used output folder persist in
  `HKCU\Software\SW2GZ\UserDefaults` so a brand-new assembly inherits the
  user's identity instead of starting blank. Per-doc checkpoint still wins.
- **Pre-flight path validation** in `Sw2gzPipeline.Run` — fails fast with a
  friendly `Sw2gzExportException` on empty / invalid output folder, missing
  parent directory, non-writable target, or a workspace path that would
  exceed Windows `MAX_PATH=260`. Replaces silent mid-export IOException.
- **Per-run sw2gz_export.log + structured summary** generated by the new
  `BuildSummaryLog` helper.

### Changed

- **Atomic re-export with rollback.** When re-exporting on top of an existing
  workspace, the pipeline writes to a sibling staging directory and swaps it
  in via `Directory.Move` only on success. A mid-run failure leaves the prior
  successful export intact. Fresh exports keep writing directly (no rename
  overhead).
- **Export dialog header** lists link names and joint edges
  (`parent → child (type)`) with `+N more` truncation, not just counts.
  Same fixed dialog footprint.
- **Ribbon `WizardEnable` callback** is null-safe (handles SolidWorks polls
  during connect/disconnect when `SwApp` may briefly be null) and now wraps
  its body in catch-all. Shared assembly-doc precondition moved to a private
  `TryGetActiveAssembly` helper used by both `LaunchWizard` and `LaunchExport`.
- **Precondition popup** ("Open an assembly first") downgraded from the
  default caution-triangle to the information icon — it's user guidance, not
  an error condition.
- **`FileOpenPostNotify` / `OnFileNew`** event-notify handlers now catch and
  log exceptions instead of letting them escape into SolidWorks (a thrown
  notify can destabilise the host).

### Fixed

- **Stale test references** — `UrdfSerializer` was renamed to `XacroGenerator`
  in 2.1.0 but four test files and the test csproj still pointed at the old
  name; build is now green.
- **Stale assertions** on `display.launch.py` / `ros2_control.launch.py` (the
  v2.1.0 launch consolidation dropped both files) and a stale
  `--packages-select` README assertion.
- **Golden snapshot** refreshed for the new `gz_sim.launch.py` and `README.md`.
- **Race in pre-flight write probe** under parallel test runs — probe file
  name now includes `Path.GetRandomFileName()` so concurrent exports into a
  shared parent don't collide.

### Internal

- New `agent-progress/` scratchpad for the coding agent + project-local
  `CLAUDE.md` pointing at it. Captures the v2.1.1 plan and a one-page flow
  diagram of the add-in for future sessions.

## [2.1.0] — 2026-06-03

**Phase 1 complete** — interactive robot-model export. An assembly now goes
from the SW2GZ ribbon to a turn-key, `colcon build`-able ROS 2 Jazzy + Gz Sim
Harmonic package without leaving SolidWorks: define the structure in the
Create-Model wizard, then one-click Export. (Phases 2–3 — Gz world and asset
modes — are next.)

### Added — Phase 1 wizard + robot-model export (`#if SW_INTEROP`)
- **Create-Model wizard** (`Sw2gzExportPmp`) — native PropertyManagerPage, 4
  steps: Mode → base-model structure (links) → Joints → Review. The 3D viewport
  stays live throughout. Finish **saves the structure only** (links + joints +
  inertia, no plugins).
- **Mate-driven joints** — joints are seeded one-per-edge from the link
  hierarchy tree (`JointSeeder`, named `<parent>_<child>_joint`). Selecting a
  SolidWorks mate from the live mate list assigns the joint its type + axis +
  limits (`WalkAllMates`: LOCK→fixed, CONCENTRIC→revolute/continuous,
  ANGLE/DISTANCE→limited revolute/prismatic). Selecting a mate **highlights its
  reference geometry in the viewport** to verify. Compact per-joint metadata
  panel.
- **Structured Review step** — compact metadata labels + separate link/joint
  listboxes (replaces the prior paragraph-style summary).
- **Separate Export command** — a second ribbon button loads the saved model,
  confirms what is implemented (`ExportDialog`: N links / M joints, bare model),
  collects package meta (output / package / author / email / license), then runs
  the export. Create-Model and Export are now distinct concerns.
- **Model-only export pipeline** — `Sw2gzModelExporter` + `WizardAssemblyWalker`
  drive `Sw2gzPipeline.Run(..., modelOnly: true)`: emits xacro + launch + meshes
  for the bare model with **no ros2_control / Gazebo plugins** (raw model you can
  spawn and view).
- **Ribbon polish** — Create Model and Export get distinct glyphs (isometric
  cube vs. cube + arrow) via a horizontal sprite strip in `ICommandGroup.IconList`;
  glyphs are drawn from scratch in `scripts\GenerateIcons.ps1` (GDI+, no source
  asset, fully original). Export success now uses an information icon, not the
  caution triangle.
- **Joint types** — added URDF `planar` (from a coincident planar-face mate,
  axis = plane normal) and `floating` joint types. An unassigned joint now
  defaults to **floating** (6-DOF) instead of fixed; assign a LOCK mate for a
  rigid weld. (Mimic deferred; ball is SDF-only — future gz-asset/world modes.)

### Added — pipeline groundwork (P1–P9)
- **P1**: `RobotModel` immutable aggregate (`Build/Model/`) + `RobotModelBuilder` + `UrdfSerializer` replaces inline string-concat in pipeline
- **P3-math**: `InertialAggregator` applies `R·I·Rᵀ` per part; `Matrix3.FromQuaternion`/`Transpose`/`Mul`/`IsApproximatelyOrthonormal`/`Determinant`; `IUnitsContext` + `IdentityUnitsContext` + `UnitsScaler` (schema only — pipeline wiring deferred to P3-units)
- **P4**: Real `QuickHull3D` convex hull collider; `ColliderStrategy { ConvexHull, Aabb }` enum; AABB retained as explicit opt-in fallback
- **P5**: `IAppearanceSource` + `DefaultAppearanceSource` stub; `MaterialDef` with RGBA validation + name dedup; per-link `<material name>` URDF emit; `inc/materials.xacro` generated from `RobotModel.Materials`
- **P6-data**: 7 sensor record types (`ImuSensor`/`GpuLidarSensor`/`CameraSensor`/`DepthCameraSensor`/`ForceTorqueSensor`/`ContactSensor`/`NavsatSensor`) + `SdfSensorBlocks` per-type SDF emit + `SdfSensorPlugins` world-level family plugin dedup + per-sensor `ros_gz_bridge` entries + `<gazebo reference>` URDF wrappers; `Sw2gzPipeline.Run(...,sensors)` 6-arg overload
- **P9**: `RobotModelValidator` — 12 structural checks (link/joint name uniqueness, tree connectivity, inertia PD, material/sensor/control ref resolution, frame orthonormality, ...); runs pre-write in `Sw2gzPipeline.Run`; errors throw `Sw2gzExportException`, warnings flow into the returned `ValidationReport`
- Tests: 254 → 542 green

### Changed
- `Sw2gzPipeline` constructor: 3-arg (`mass`/`walker`/`tess`) → 4-arg (+`IAppearanceSource`); 3-arg kept as back-compat
- `Sw2gzPipeline.Run`: 5-arg → 6-arg (+`IReadOnlyList<SensorDef> sensors`) → 7th `modelOnly` flag for the bare-model wizard path; older arities kept as back-compat
- `PackageNameSanitizer` — digit-prefixed names (e.g. `3r_arm`) now get a `pkg_` prefix instead of an ament-invalid leading underscore
- `SdfWorldWriter` no longer unconditionally emits `gz-sim-imu-system` / `gz-sim-sensors-system` plugins; `SdfSensorPlugins` is now the single source of truth for sensor-family plugins

### Removed
- Placeholder `assets/` design PNGs and the unused `ros_logo` image set

### Deferred to Phase 2 / 3 (v2.2)
- **Gz world mode** (`ExportMode.SdfWorld`) and **asset mode** (`ExportMode.SdfModel`) — wizard branches by Mode, reusing `SdfWorldWriter` / `SdfModelWriter`
- Multi-component links currently mesh only the first component's visual
- **P3-units**, **P5-COM**, **P6-COM**: require further SolidWorks-workstation validation
- **P7**: SDF serializer + legacy `ExportHelper` / `URDFRobot` retirement
- **P8**: WPF wizard UI (`SW2GZ.UI.Core` extraction)

### Notes
- `SW2GZ.dll` v2.1.0; installer `SW2GZ-Setup-2.1.0.exe`

## [v2.0.1] — 2026-05-30

### Changed

- **README** simplified for end users — short install/usage flow, one diagram, less clutter.
  Deep material (pipeline architecture, internals, project status, test commands) moved to
  `CONTRIBUTING.md`.
- Install instructions now point to the Releases page instead of a hard-coded installer
  filename.
- Bumped installer version define to `2.0.1`.

## [v2.0.0] — 2026-05-29

End-to-end correctness pass. Exported package now `colcon build`-able and
`ros2 launch`-able on ROS 2 Jazzy + Gz Sim Harmonic without manual edits.

### Added

- **Sw2gzPipeline** — top-level orchestrator chaining SwSurface → Build → Write → Validate.
  Triggered from `ExportHelper.ExportRobot` when `Profile.Mode == RobotPackage`.
- **SwSurface layer** — abstracted SolidWorks I/O behind interfaces:
  `IMassProperties`, `IAssemblyWalker`, `IMeshTessellator`. Concrete `SolidWorks*`
  impls under `#if SW_INTEROP`; Moq-able for unit tests.
- **Build layer**:
  - `PackageNameSanitizer` — normalises arbitrary names to ament regex
    `^[a-z][a-z0-9_]*[a-z0-9]$` (lowercase, no hyphens / spaces / digit-prefix issues).
  - `InertialAggregator` — parallel-axis theorem combine over flattened sub-assembly parts.
  - `ConvexHullCollider` — AABB fallback for v2.0 (real QuickHull deferred).
  - `LinkBuilder` / `JointBuilder` — POCO record builders for `UrdfLink` / `UrdfJoint`.
- **Mesh I/O**:
  - `DaeWriter` — Collada 1.4.1, `Z_UP`, `meter=1`, embedded `library_effects` colour,
    locale-stable (`InvariantCulture`) float formatting.
  - `StlWriter` — binary STL collision meshes.
- **Validate layer** — static lint checkers + orchestrator:
  - `PackageNameChecker` (PKG001 ament regex)
  - `UrdfXmlValidator` (URDF001 well-formedness, URDF002 empty `<geometry>`)
  - `PluginNameChecker` (PLG001 Garden `gz-simN-*`, PLG002 wrong `gz_ros2_control` class)
  - `MeshFileChecker` (MSH001 dangling `package://` mesh URI)
  - `OutputValidator` — aggregates issues into `ValidationReport`.
- **Workspace layout** — pipeline emits `<outputDir>/<pkg>_ws/src/<pkg>/...` so the
  output is a turn-key colcon workspace (`cd <pkg>_ws && colcon build`).
- **Sw2gzProfileDialog** — programmatic WinForms dialog opened from Finish-Export
  to capture `ExportMode` + Author / Email / License.
- **Sw2gzExportException family** — `MaterialMissingException`,
  `Sw2gzMeshException`, `Sw2gzGeometryException`, `Sw2gzValidationException`
  for narrow catches replacing generic exception swallows.
- **11-bug acceptance suite** — `Test/Acceptance/BugAcceptanceTests.cs` runs every
  known v1 export bug through `Sw2gzPipeline` with Moq SW Surface and asserts
  on the emitted files.
- **HTML walkthrough** — `docs/sw2gz-ui-walkthrough.html` self-contained dark-theme
  guide with inline SVG mockups of Tools → Add-Ins, menu bar, Profile dialog.
- **`IsExternalInit` polyfill** under `SW2GZ.Compat/` so C# 9 `record` types
  compile against net48.1.

### Changed

- **Distro lock** — `Ros2Distro` / `GzVersion` / `Pairing` / `SimPluginLib` /
  `Ros2ControlPlugin` / `SdfVersion` enums + dictionaries deleted. Writers now
  hard-code Jazzy + Harmonic strings.
- **Fixed all 11 v1 export bugs:**
  1. `inc/gz.xacro` was an empty placeholder — now emits full
     `<gazebo><plugin filename="gz_ros2_control-system">` block.
  2. Package name carried hyphens → ament-invalid — now sanitized via
     `PackageNameSanitizer` to lowercase underscore-separated.
  3. `$(arg pkg)` was referenced but never declared → xacro parse error.
     Now emits literal `$(find <pkg>)`.
  4. World plugin filenames used Garden-versioned `gz-sim8-*-system`.
     Now unversioned Harmonic `gz-sim-*-system` (physics, sensors, imu,
     user-commands, scene-broadcaster).
  5. `GZ_SIM_SYSTEM_PLUGIN_PATH` env var was never set →
     `libgz_ros2_control-system.so` not found. Launch now sets it to
     `get_package_prefix('gz_ros2_control') + '/lib'`.
  6. Spawn `-name` mismatched bridge `gz_topic_name`. Both now use sanitized
     package name.
  7. `ros_gz_bridge.yaml` was emitted but no `parameter_bridge` node ever
     launched → `/clock` un-bridged. Launch now starts `parameter_bridge`
     with the config file.
  8. `gz_ros2_control` plugin class name was wrong (`gz_ros2_control::system`).
     Correct: `gz_ros2_control::GazeboSimROS2ControlPlugin`.
  9. URDF `<visual>` and `<collision>` had no `<geometry>` child → invisible /
     collision-less links. Now emits `<mesh filename="package://<pkg>/meshes/<link>.dae"/>`
     for visual and `<mesh filename="..._collision.stl"/>` for collision.
  10. Continuous joints with `position` command interface had no limits.
      `JointBuilder.Build` now surfaces a PreExportReport warning.
  11. `gz_sim.launch.py` was partial — no spawner / bridge / RSP composition.
      Now self-contained: one `ros2 launch` brings everything up.
- **MSBuild SW2GZ.csproj** — `<LangVersion>` bumped to `9.0` and `SW_INTEROP`
  unconditional `<DefineConstants>` moved AFTER per-config groups so the
  `#if SW_INTEROP` branch in `ExportHelper.ExportRobot` actually compiles in
  for `Release|x64`.
- **ComUnregisterFunction** — uses `DeleteSubKey(name, throwOnMissingSubKey:false)`
  for silent idempotent uninstall; no more "Cannot delete a subkey tree"
  dialog during `/SILENT` uninstall. Logger path rebranded
  `~/sw2urdf_logs/sw2urdf.log` → `~/sw2gz_logs/sw2gz.log`.
- **package.xml** — drops `TargetProfile` input; hard-codes
  `ros_gz_sim`, `ros_gz_bridge`, `gz_ros2_control` as Harmonic deps.
- **CMakeLists.txt** — `install(DIRECTORY meshes ...)` now gated on a
  `hasMeshes` flag (no more colcon failure when meshes dir is empty).
- **Golden snapshots** — collapsed to a single `Test/Golden/expected/jazzy_harmonic/`
  directory. `fortress_humble/` + `ionic_kilted/` removed.
- **Test count** — 50/50 (v1.0-rc) → 241/241 across Build / Mesh I/O / Write /
  Validate / SwSurface / Integration / Acceptance / Golden.

### Removed

- `Ros2Distro`, `GzVersion`, `TargetProfile.Pairing`, `TargetProfile.SimPluginLib`,
  `TargetProfile.Ros2ControlPlugin`, `TargetProfile.SdfVersion`.
- ROS 2 distro / Gz version combo boxes from `Sw2gzProfileDialog`.
- Two `harmonic_jazzy` / two other golden-profile fixtures.
- Dead `GzPluginTags.WorldSystemBlock` + `GzPluginTags.Ros2ControlPluginBlock`
  methods (replaced by hard-coded Harmonic strings inline in `SdfWorldWriter`
  + by T10's `WriteGzRos2ControlXacro`).
- `Test/Writers/TestTargetProfile.cs` (11 tests exercising removed APIs).
- Debug-leftover `Test/TestXml.cs` + `Test/check_xml.cs` from T16
  PackageXmlV3Writer implementation.

### Deferred to v2.1

- **Joint extraction from SolidWorks limit-mates** in `Sw2gzPipeline`. v2.0 pipeline
  emits links only; users must hand-edit joints into the xacro (or use the legacy
  upstream URDF tree path which DOES emit joints but bypasses the new pipeline).
- **Real QuickHull** for `ConvexHullCollider` — v2.0 ships AABB fallback only.
- **Sub-assembly inter-part transforms** in `InertialAggregator` — currently
  combines at identity pose.
- **WSL-side `xacro` / `check_urdf` / `gz sdf -k` external validators**
  alongside the static lint.
- **Ribbon-bar UI integration**, **dark mode**, **modern + intuitive
  link/joint adding flow** — UI overhaul spec (Spec #2).
- **MJCF / USD exporters** (still v1.2 backlog).

### Notes

- Forked from the upstream `solidworks_urdf_exporter` by Stephen Brawner. Original
  MIT license retained.
- `SW2GZ.dll` v2.0.0.0; installer `SW2GZ-Setup-2.0.0.exe`.

---

## [v1.0.0-rc] — 2026-05-27

### Added

- ROS 2 + Gz Sim export pipeline (`Ros2Package`, `SdfModelWriter`, `SdfWorldWriter`)
- `TargetProfile` with Gz Fortress/Harmonic/Ionic and ROS 2 Humble/Jazzy/Kilted/Rolling, with auto-pairing
- `<ros2_control>` tag emit + `controllers.yaml` for `joint_state_broadcaster` + `joint_trajectory_controller`
- `ros_gz_bridge` topic config (clock / joint_states / tf) (F13)
- Pre-export summary report (link/joint count, total mass, warnings) (F15)
- Post-write XML well-formedness validation (B18)
- Author / email / license fields in `Ros2Package.Options`
- GitHub Actions: Windows build (best-effort), Linux writer-tests, ROS 2 docker validation matrix, tag-triggered installer (F17)
- Inno Setup installer scaffold with new GUIDs for side-by-side install with SW2URDF
- Standalone SDK-style `SW2GZ.Writers.Test.csproj` (`net8.0` + xUnit) verifiable without SolidWorks install
- Golden-file regression tests across 3 target profiles (harmonic_jazzy, fortress_humble, ionic_kilted)

### Changed

- Solution / namespaces renamed `SW2URDF` → `SW2GZ`
- New assembly Guid, COM Guid, ProgId `SwAddin.SW2GZ.Addin`, registry keys distinct from upstream
- `ExportHelper.ExportRobot` narrow catches: `COMException` / `IOException` / `XmlException` with rethrow; generic Exception no longer swallowed (B11)
- Output `CMakeLists.txt` bumped to `cmake_minimum_required(VERSION 3.8)` + `ament_cmake` (B9)
- Output `package.xml` is format 3 with `ament_cmake` buildtool

### Removed

- ROS 1 / catkin emitter (`SW2URDF/ROS/`, `URDFPackage.cs`, `PackageXMLWriter.cs`)
- Gazebo Classic plugin tags (Gz Sim plugins now emitted per-profile via `GzPluginTags` + `Ros2ControlWriter`)
- `SW2URDF/Legacy/SerialNode.cs` (unused, isolated island per graphify)
- Three ROS 1 catkin example packages under `examples/`
- `GlobalSuppressions.cs` files (lint via `.editorconfig`)

### Notes

- Forked from the upstream `solidworks_urdf_exporter` by Stephen Brawner. Original MIT license retained.
- `DataContract` namespace URIs still use `.../SW2URDF` for round-trip back-compat with files saved by upstream add-in. Override planned for v1.1.
