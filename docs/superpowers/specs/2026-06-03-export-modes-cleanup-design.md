# Export Modes Cleanup — Robot / GZ Asset / GZ World

**Date:** 2026-06-03
**Status:** Approved design
**Refs:** [robust-exporter-architecture](2026-06-01-robust-exporter-architecture.md) · [roadmap](../../../STUDY/roadmap.md)

## Problem

SW2GZ offers three export modes (`ExportMode` enum): `RobotPackage`, `SdfModel`, `SdfWorld`.
Only `RobotPackage` is real: it runs the `Sw2gzPipeline` (walk assembly → tessellate
meshes → build links/joints/materials/inertials → emit a full ROS 2 package).

The two gz modes go through a **dead legacy path** in `ExportHelper.ExportRobot`:

- `SdfModel` ("gz asset") → `SdfModelWriter` emits a **name-only** `model.sdf` — no
  geometry, no visuals, no meshes (`SdfModelWriter.cs` only writes `<link name=…/>`).
- `SdfWorld` ("gz world") → `SdfWorldWriter.Write` emits an **empty** world with no
  model in it at all.

Both walk the legacy `URDFRobot` tree via `BuildSdfModelInput`, not the real pipeline.
Result: the gz modes produce useless output.

## Goal

Make all three modes produce genuinely distinct, working output, all routed through the
one real pipeline. Each mode emits an **ament ROS 2 package** under
`<pkg>_ws/src/<pkg>/`. The gz modes use **standard gz Harmonic packaging** (model
directory + world that includes models, resolved via `GZ_SIM_RESOURCE_PATH`).

## Design

### Shared front-half (unchanged)

`Sw2gzPipeline` already walks the assembly and builds the immutable `RobotModel`
(`Links[]` with visual+collision `MeshData`, `Materials[]`, `Joints[]`, per-link mass/COM/
inertia). All three modes reuse this. Only the **write stage** branches by mode.

### The shared gz artifact: a standard gz model directory

Both gz modes emit the active assembly as a standard gz Harmonic **model directory**:

```
models/<name>/
  model.config        # gz model manifest (ModelConfigWriter — already exists)
  model.sdf           # REAL geometry (new SdfModelWriter)
  meshes/*.dae|stl    # visual (DAE+color) + collision (STL convex hull)
```

`model.sdf` carries, per link: `<inertial>` (mass, `<pose>` at COM, `<inertia>` tensor),
`<visual>` (`<geometry><mesh><uri>model://<name>/meshes/<file></uri></mesh>` + material
color), `<collision>` (convex-hull mesh). Joints emit as SDF `<joint>` with
`<parent>/<child>/<axis>/<limit>`. Mesh URIs use `model://<name>/…` (NOT `package://`),
so the model resolves under `GZ_SIM_RESOURCE_PATH`.

### Per-mode output

All three are ament packages under `<pkg>_ws/src/<pkg>/` with `package.xml`,
`CMakeLists.txt`, `README.md`, `launch/`.

| Aspect | **Robot** (`RobotPackage`) | **GZ Asset** (`SdfModel`) | **GZ World** (`SdfWorld`) |
|---|---|---|---|
| Geometry | `urdf/<pkg>.urdf.xacro` + `meshes/` | `models/<name>/` model dir | `models/<name>/` model dir |
| `ros2_control` + `gz.xacro` plugin + `controllers.yaml` + `ros_gz_bridge.yaml` | yes | no | no |
| World | `worlds/empty.sdf` (robot spawned) | `worlds/empty.sdf` (bare) | `worlds/<name>.sdf` with `<include>` of `model://<name>` over ground/sun/physics |
| Launch | gz + spawn + rsp + bridge + controllers | gz empty world + **spawn the gz model** (`ros_gz_sim create`) | **load the world** (`gz sim <name>.sdf`); model already in it |
| `package.xml` deps | full (control, bridge, …) | lean (`ros_gz_sim`) | lean (`ros_gz_sim`) |

**Asset** = a reusable visual model staged into a bare empty world and spawned at launch.
**World** = the same model composed *into* a world SDF, launched by loading that world.
The `models/<name>/` dir is the shared artifact; asset vs world is only how it is staged.
World mode is structured for composition — adding walls/props later is more `<include>`
lines + more model dirs, no code change.

### Code changes

1. **`SdfModelWriter` (rewrite).** New input: `RobotModel` (replaces the name-only
   `SdfModelInput`). Emits `model.sdf` with real visual/collision/inertial/joint geometry.
   Follows the `UrdfSerializer` StringBuilder style (InvariantCulture floats,
   `SecurityElement.Escape` on dynamic strings) for golden-test stability.
2. **`SdfWorldWriter`.** Add an overload that wraps the existing ground/sun/physics world
   with an `<include><uri>model://<name></uri><name><name></name></include>` block.
   Keep the existing empty-world overloads for Robot/Asset modes.
3. **`LaunchPyWriter`.** Add `GzAsset(pkg, modelName)` — sets `GZ_SIM_RESOURCE_PATH`
   to the package share dir, starts `gz sim empty.sdf`, spawns the model via
   `ros_gz_sim create` with `-name <name>` and `-file <pkg_share>/models/<name>/model.sdf`
   (absolute path to the installed model.sdf). Add `GzWorld(pkg, worldName)` — sets
   `GZ_SIM_RESOURCE_PATH`, loads `worlds/<name>.sdf`. Drop the `GzSimModelOnly` URDF-spawn
   path (superseded).
4. **`Sw2gzPipeline.Run`.** Replace the `bool modelOnly` param with `ExportMode mode`.
   Branch the write stage: `RobotPackage` = current full output; `SdfModel`/`SdfWorld` =
   write `models/<name>/` (config + model.sdf + meshes) + the mode's world + the mode's
   launch; skip `urdf/`, `ros2_control.xacro`, `gz.xacro`, `controllers.yaml`,
   `ros_gz_bridge.yaml`.
5. **`AmentCMakeWriter` / `PackageXmlV3Writer`.** Make mode-aware: install `models/` for
   gz modes; drop control/bridge deps from `package.xml` for gz modes.
6. **`ExportHelper.ExportRobot`.** Route **all three** modes through
   `pipeline.Run(SavePath, pkg, author, email, license, sensors, Profile.Mode)`. Delete
   the legacy per-mode `switch`, `BuildSdfModelInput`, and the `URDFRobot`-tree gz path.
7. **Delete** the obsolete name-only `SdfModelInput`/`SdfLinkData`/`SdfJointData` POCOs.

### Scope boundaries (YAGNI)

- World emits **one** model (the active assembly); structured for many, but no
  multi-assembly selection UI now.
- Materials/color via DAE as today; **texture maps stay out** (already roadmapped later).
- Joints carried into SDF; no mimic/ball joint work here.
- The Robot-package output is unchanged in behavior (byte-parity preserved by existing
  golden tests).

## Testing

- Golden `model.sdf` test (links + visual mesh URI + collision + inertial + joints).
- Golden world-with-include test (asserts `<include>`/`<uri>model://<name>` present).
- `LaunchPyWriter` string-assertion tests for `GzAsset` / `GzWorld` (resource path,
  spawn vs load).
- `Sw2gzPipeline` file-tree test per mode: Robot → `urdf/` present, no `models/`; Asset →
  `models/<name>/{model.config,model.sdf}` + empty world + spawn launch, no `urdf/`/
  control files; World → world with `<include>` + load launch.
- No SolidWorks COM in tests — POCO/`RobotModel` inputs, consistent with the existing
  552-test suite.
</content>
</invoke>
