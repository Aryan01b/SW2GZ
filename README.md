# SW2GZ — SolidWorks to ROS 2 + Gz Sim Exporter

Fork of the upstream `solidworks_urdf_exporter` by Stephen Brawner,
modernized for ROS 2 and the new Gazebo (Gz Sim).

## What it does

Exports SolidWorks assemblies/parts into ready-to-launch ROS 2 packages
targeting Gz Sim. Three export modes:

| Mode | Output |
|---|---|
| **Robot Package** | Full ament package: `package.xml`, `CMakeLists.txt`, `urdf/*.urdf.xacro`, `launch/*.launch.py`, `config/controllers.yaml`, `worlds/empty.sdf`, RViz config |
| **SDF Model** | Standalone Gz asset: `model.config` + `model.sdf` + `meshes/` |
| **SDF World** | Standalone Gz world (.world) with physics, sun, ground plane |

Selectable target matrix (auto-paired per OSRF distro):

| ROS 2 Distro | Gz Version |
|---|---|
| Humble  | Fortress |
| Jazzy   | Harmonic (default) |
| Kilted  | Ionic |
| Rolling | Harmonic |

## Install

See [BUILD.md](BUILD.md) for build instructions (requires Windows + Visual
Studio 2022 + SolidWorks API SDK). Output is `SW2GZ-Setup-1.0.0.exe` in
`installer/Output/`; run as administrator, then enable in SolidWorks
`Tools → Add-Ins`. Installs side-by-side with the original SW2URDF
add-in (separate COM GUID + ProgId `SwAddin.SW2GZ.Addin`).

## Usage

1. Open an assembly in SolidWorks
2. Tools → SW2GZ → Export to ROS 2 / Gz
3. Pick Mode + ROS 2 distro + Gz version + output folder
4. Configure link tree
5. Finish Export
6. `cd <output>/<robot>_description && colcon build && ros2 launch <robot>_description gz_sim.launch.py`

## Example output

[examples/three_dof_arm_ros2/](examples/three_dof_arm_ros2/) is the
package SW2GZ produces from the bundled 3-DOF arm fixture (Jazzy +
Harmonic).

## Develop

Pure-C# writer tests run anywhere:

```bash
dotnet test Test/SW2GZ.Writers.Test.csproj --filter "Category=Unit"
# 50/50 passing
```

The full SolidWorks add-in csproj requires:
- Windows + Visual Studio 2022 (or VS Build Tools)
- SolidWorks 2022+ installed (for COM interop DLLs)
- .NET Framework 4.8 dev pack

```powershell
msbuild SW2GZ.sln /t:Restore;Build /p:Configuration=Release
```

See [CONTRIBUTING.md](CONTRIBUTING.md) for license-header conventions
and contribution flow.

## Status (v1.0)

| Phase | Status |
|---|---|
| 0  Rebrand + rip ROS 1 / Gazebo Classic | ✅ done |
| 1  TargetProfile (Gz/ROS2 lookup tables) | ✅ done, 9 tests |
| 2  Ros2 writers (PackageXml v3, AmentCMake, LaunchPy, Xacro, Ros2Control, Rviz, Readme) | ✅ done, 19 tests |
| 3  Gz writers (ModelConfig, PluginTags, PhysicsBlock, SdfWorld, SdfModel, RosGzBridge) | ✅ done, 13 tests |
| 4  Ros2Package orchestrator + ExportHelper branching | ✅ done, 1 orchestrator test (orchestrator verified; ExportHelper edits await VS build) |
| 5  UI selectors (radio + combos + textboxes) | ⏳ partial (binding helpers committed; WinForms Designer changes deferred to VS) |
| 6  v1.0 bugs (B18 OutputValidator + F15 PreExportReport) | ✅ done, 5 tests |
| 6  v1.0 bugs (B7 resx, B10 Path.Combine sweep, B13 logger) | ⏳ deferred to v1.1 |
| 7  Golden-file tests | ✅ done, 3 profiles |
| 8  Inno Setup installer | ✅ scaffold written |
| 9  Example output package | ⏳ pending (copy harmonic_jazzy golden to examples/) |
| 10 GitHub Actions CI | ✅ build + ros2-validate + release workflows |
| 11 README + CHANGELOG | ✅ done |
| 12 Acceptance: VM build + spawn into Gz | ⏳ manual — needs SolidWorks workstation |

## Credits

SW2GZ is a modernized derivative of the [`solidworks_urdf_exporter`](https://github.com/ros/solidworks_urdf_exporter)
by **Stephen Brawner**, which made this project possible. The ROS 2 + Gz Sim
port, new writers, installer, and tooling are by **Aryan Arlikar**.

## License

MIT — see [LICENSE](LICENSE).

- Original `solidworks_urdf_exporter`: Copyright (c) 2015-2020 Stephen Brawner
- SW2GZ (ROS 2 + Gz Sim modifications): Copyright (c) 2026 Aryan Arlikar

Bundled third-party components and their licenses are listed in
[THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md). "SolidWorks" is a
trademark of Dassault Systèmes; SW2GZ is independent and not affiliated with
or endorsed by Dassault Systèmes.
