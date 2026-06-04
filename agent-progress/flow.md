# SW2GZ flow (one page)

SolidWorks COM add-in (.NET 4.8, `SW_INTEROP`). Exports assemblies to ROS 2
Jazzy + Gz Sim Harmonic packages.

## Ribbon (assembly docs only)

Two buttons on the **SW2GZ** tab:

1. **Create Model** → opens `Sw2gzExportPmp` (native PropertyManagerPage wizard).
2. **Export** → loads saved model, opens `ExportDialog`, runs the pipeline.

Stacks buttons (Actuation/Sensors/Gazebo/Bridge) are temporarily removed
(commit `b7d82c7`).

## Create-Model wizard

Steps: **Mode → Links → Joints → Review**.

- `RobotPackage` mode shows all four steps.
- `SdfModel` / `SdfWorld` modes skip Links + Joints (Mode → Review).
- Finish writes structure-only (links + joints + inertia) to the doc checkpoint.
  No plugins, no files written here.

## Export

`ExportDialog` collects: output dir, package name, author, email, license.
Runs `Sw2gzPipeline.Run(out, pkg, author, email, license, sensors, stacks, mode)`.

## Pipeline write stage (branches on `ExportMode`)

| Mode          | Output                                          | Status |
|---------------|-------------------------------------------------|--------|
| `RobotPackage`| URDF/xacro package, colcon-buildable            | done   |
| `SdfWorld`    | gz Harmonic model dir + world with `<include>`  | done   |
| `SdfModel`    | gz Harmonic model dir, spawned into empty world | done   |

`StackProfile` (Actuation/Sensors/Gazebo/Bridge gating) is threaded
independently of `ExportMode`.

## Build / deploy

See memory `sw2gz-build-deploy` for MSBuild path, `SolutionDir` requirement,
and the regasm + SolidWorks-lock gotchas. Test suite: 542 green.
