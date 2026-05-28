# Changelog

All notable changes documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased] / v1.0.0-rc

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

### Deferred to v1.1
- B7 (delete empty `Resources.resx`)
- B10 (full `Path.Combine` sweep across SW-dependent files)
- B13 (log4net → Microsoft.Extensions.Logging + Serilog file sink)
- T4 (Hashtable → Dictionary in `SW/EventHandling.cs`)
- B1 limit-mate axis validation, B12 god-class split, B14 collision mesh decimation, B15 inertia PD check, B16 RPY gimbal-lock fix
- F1 CLI / headless mode, F7 mimic-joint UI, F8 per-link material override, F9 inertia source picker, F18 diagnostic bundle
- UI WinForms Designer changes (radio + combos for ExportMode/Distro/Gz) — binding helpers committed; controls must be added in VS Designer

### Notes
- Forked from the upstream `solidworks_urdf_exporter` by Stephen Brawner. Original MIT license retained.
- `DataContract` namespace URIs still use `.../SW2URDF` for round-trip back-compat with files saved by upstream add-in. Override planned for v1.1.
