# W1 — World launch + ros_gz bridge (standalone)

**Date:** 2026-06-30
**Mode:** World
**Status:** Design approved (packaging = standalone, no ament; launch scope =
world + bridge only; target convention = match Robot mode / ROS 2 Jazzy + Gz
Harmonic).

## Problem

World mode exports a self-contained `<pkg>/<pkg>.sdf` + `meshes/` and stops
there. To actually run it the user types `gz sim <pkg>.sdf` by hand, with no
ROS 2 bridge — so `/clock` never advances under `use_sim_time` and a ROS teleop
node can't reach the in-sim `/cmd_vel`. Robot mode already emits launch + bridge;
World mode is the gap. This makes World output runnable from ROS 2.

## Scope (locked)

- **World + bridge only.** No robot spawn, no `robot_state_publisher`, no
  controllers.
- **Standalone, no ament.** Keep World's "one folder, no colcon" character. The
  launch resolves paths relative to its own location (`os.path.dirname(__file__)`),
  not `get_package_share_directory`.
- **Convention matches Robot mode:** `ros_gz_sim/gz_sim.launch.py` include +
  `ros_gz_bridge parameter_bridge`, Jazzy + Harmonic.
- **Additive only.** Existing `.sdf` + mesh output stays byte-identical.

Out of scope: ament packaging, robot spawning, sensor-topic bridging (World
places no sensors), per-engine physics params, any UI change.

## Output layout

```
<outputDir>/<pkg>/
  <pkg>.sdf            (existing, unchanged)
  meshes/*.dae         (existing, unchanged)
  launch_world.py      ← NEW
  ros_gz_bridge.yaml   ← NEW
```

Run: `ros2 launch <pkg>/launch_world.py` (no build step).

## launch_world.py

- `SetEnvironmentVariable('GZ_SIM_RESOURCE_PATH', <dir of this launch file>)` so
  the relative `meshes/<x>.dae` URIs in the `.sdf` resolve.
- `IncludeLaunchDescription(ros_gz_sim/gz_sim.launch.py)` with
  `gz_args = '-r <here>/<pkg>.sdf'`.
- `parameter_bridge` node with `config_file = <here>/ros_gz_bridge.yaml`.
- `<here> = os.path.dirname(os.path.realpath(__file__))`.

`ros_gz_sim` share dir is still resolved via `get_package_share_directory`
(it's an installed ROS package — always available); only **our** files use the
relative path.

## ros_gz_bridge.yaml — world-shaped (new writer, NOT the robot one)

The existing `RosGzBridgeYaml` is robot-shaped (`/joint_states`, `/tf` keyed to
the spawned robot name) — wrong for a world with no robot. New writer:

- **`/clock`** GZ→ROS — always (sim time).
- **`/cmd_vel`** ROS→GZ — only when teleop is enabled (`KeyPublisher` or
  `TriggeredPublisher`), so an external ROS teleop can also drive the in-sim
  `/cmd_vel` that the keyboard plugin publishes to.

Nothing else: World places no sensors; spawned models bring their own bridges.

## Code seams

| Action | File | Responsibility |
|---|---|---|
| NEW | `SW2GZ/Ros2/WorldLaunchPyWriter.cs` | `Write(string worldFileName, bool teleop)` → path-relative launch string. Pure. |
| NEW | `SW2GZ/Gz/WorldBridgeYaml.cs` | `Write(bool teleop)` → 1–2 entry world bridge YAML. Pure. |
| EDIT | `SW2GZ/URDFExport/Sw2gzWorldExporter.cs` | After writing `.sdf`, write the two files into `<pkg>/`. `teleop = WorldSensorPlugins?.KeyPublisher == true || .TriggeredPublisher == true`. |
| EDIT | both `.csproj` | Source-link the two new pure files into the test project; add to addin csproj. |

Both new writers are pure (no COM), so they source-link into the test project
and are fully unit-testable — same pattern as `LaunchPyWriter` / `RosGzBridgeYaml`.

`WorldLaunchPyWriter.Write` takes the world **file name** (`<pkg>.sdf`), not the
package name, because the standalone launch references the sibling file directly.

## Error handling

- Both writers guard their string args (mirror `LaunchPyWriter.Guard` /
  `RosGzBridgeYaml` null/whitespace checks).
- Exporter wiring is best-effort consistent with the existing `.sdf` write: if
  the `.sdf` writes, the two siblings write in the same block (no partial state
  that leaves a runnable-looking folder without a bridge).

## Testing

- `WorldLaunchPyWriterTests` — golden string; teleop on vs off (off still emits
  the bridge node, just a clock-only yaml); arg guard.
- `WorldBridgeYamlTests` — clock-only (teleop off) vs clock + `/cmd_vel`
  (teleop on); exact YAML shape.
- `Sw2gzWorldExporterTests` — extend the fake-tessellator test to assert
  `launch_world.py` and `ros_gz_bridge.yaml` land in `<pkg>/`, and that the
  `/cmd_vel` entry appears only when a teleop plugin flag is set.

## Definition of done

- New + existing tests green (target ~806–809).
- Existing world `.sdf` / mesh goldens byte-identical (additive-only verified).
- Commit on `feat/world-sensors`; no live SW deploy needed for this cut (pure
  writers + exporter; COM path unchanged).
