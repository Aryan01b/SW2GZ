# Changelog

All notable changes documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
