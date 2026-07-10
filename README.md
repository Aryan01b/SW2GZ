<div align="center">

# SW2GZ

### SolidWorks → ROS 2 + Gz Sim Exporter

No more hand-writing URDF — export straight from your SolidWorks feature tree.

Turn a SolidWorks assembly into a ready-to-launch ROS 2 package or Gz Sim model —
meshes, URDF/Xacro, SDF, and launch files generated for you.

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/Aryan01b/SW2GZ?include_prereleases&label=release)](https://github.com/Aryan01b/SW2GZ/releases/latest)
[![Stars](https://img.shields.io/github/stars/Aryan01b/SW2GZ?style=flat&color=yellow)](https://github.com/Aryan01b/SW2GZ/stargazers)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows&logoColor=white)](https://github.com/Aryan01b/SW2GZ/releases/latest)
[![ROS 2](https://img.shields.io/badge/ROS%202-Jazzy-22314E?logo=ros&logoColor=white)](#supported-targets)

</div>

## Why SW2GZ

- **No manual URDF authoring** — masses, inertias, joint frames, and mates are read straight from the SolidWorks model, not typed by hand.
- **ROS 2 + Gz Sim, not just URDF** — the original `sw2urdf` stops at a URDF file; SW2GZ also emits SDF, launch files, and `ros2_control` config.
- **Three export modes** — a full robot package, a standalone SDF model, or an SDF world, depending on what you need.

## Export modes

| Mode | What you get |
|---|---|
| **Robot Package** | Full ROS 2 package: URDF/Xacro, launch files, `ros2_control` config, RViz, a world |
| **SDF Model** | Standalone Gz model: `model.config`, `model.sdf`, meshes |
| **SDF World** | Standalone Gz world with physics, sun, and ground plane |

## Supported targets

ROS 2 distro and Gz version are paired automatically:

| ROS 2 Distro | Gz Version |
|---|---|
| Humble | Fortress |
| **Jazzy** | **Harmonic** *(default)* |
| Kilted | Ionic |
| Rolling | Harmonic |

## Install

1. Download the latest **`SW2GZ-Setup`** installer from the
   [Releases](https://github.com/Aryan01b/SW2GZ/releases/latest) page.
2. Run it **as administrator**.
3. In SolidWorks, open `Tools → Add-Ins` and enable **SW2GZ**.

Installs alongside the original SW2URDF add-in — both can run together.

## Usage

1. Open an assembly in SolidWorks.
2. `Tools → SW2GZ → Export to ROS 2 / Gz`.
3. Pick a mode, ROS 2 distro, Gz version, and output folder.
4. Configure the link tree, then **Finish Export**.
5. Build and launch:

```bash
cd <output>/<robot>_ws && colcon build
ros2 launch <robot>_description gz_sim.launch.py
```

## More

- **Release history:** [CHANGELOG.md](CHANGELOG.md)
- **Build from source:** [BUILD.md](BUILD.md)
- **Contributing & internals:** [CONTRIBUTING.md](CONTRIBUTING.md)

## Credits

A modernized derivative of
[`solidworks_urdf_exporter`](https://github.com/ros/solidworks_urdf_exporter) by Stephen
Brawner. The ROS 2 + Gz Sim port, new writers, installer, and tooling are by **Aryan Arlikar**.

## License

MIT — see [LICENSE](LICENSE). Third-party components: [THIRD-PARTY-LICENSES.md](THIRD-PARTY-LICENSES.md).

> "SolidWorks" is a trademark of Dassault Systèmes; SW2GZ is independent and not affiliated
> with or endorsed by Dassault Systèmes.
