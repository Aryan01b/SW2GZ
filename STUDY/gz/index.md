# Gazebo Harmonic (gz-harmonic) — Ecosystem Briefing & Features

Harmonic = LTS release (Sep 2023, supported to Sep 2028). The modern "Gazebo Sim" (formerly Ignition), successor to Gazebo Classic 11. Pairs with ROS 2 Jazzy via `ros_gz`. Command prefix: `gz`.

## Architecture
- **Modular libraries** (each independently usable), tied together by a transport layer.
- **Entity-Component-System (ECS)** simulation core: entities + components, behavior via systems/plugins.
- **Plugin-driven**: physics, sensors, GUI, and world logic are loadable plugins.

## Core Libraries
- **gz-sim** — the simulator: ECS world runtime, server + GUI, system plugins, levels/distributed sim.
- **gz-transport** — pub/sub + service messaging (ZeroMQ + Protobuf), discovery, topic introspection.
- **gz-msgs** — Protobuf message definitions.
- **gz-physics** — physics abstraction; engines: **DART** (default), **Bullet**, Bullet-featherstone, TPE (Trivial Physics Engine, kinematic).
- **gz-rendering** — rendering abstraction; engines: **OGRE 2** (PBR), OGRE 1, optional OptiX.
- **gz-sensors** — sensor simulation backend.
- **gz-gui** — Qt-based dockable GUI plugin framework.
- **gz-common** — utilities: mesh/material loading, events, console, profiler.
- **gz-math** — geometry/linear algebra.
- **gz-plugin** — plugin loading/registration.
- **gz-fuel-tools** — fetch models/worlds from **Fuel** online library.
- **gz-utils, gz-tools, gz-launch** — config, CLI, orchestration.
- **sdformat (SDF)** — world/model/robot description format (v1.11 in Harmonic).

## Sensors (gz-sensors)
- Cameras: RGB, depth, **RGBD**, thermal, segmentation (semantic/instance), wide-angle/fisheye, boundingbox.
- **Lidar / GPU lidar**, 2D/3D.
- **IMU**, **magnetometer**, **altimeter**, **air pressure / air speed**.
- **Contact**, **force-torque**, **navsat (GPS)**, logical camera.
- Noise models, sensor update rates, optical frames.

## GUI / Visualization Plugins
- 3D scene, component inspector, entity tree, transform control (translate/rotate).
- Playback controls (play/pause/step), world stats, view angle.
- Plotting, image display, teleop, camera tracking/following.
- Visualize lidar/contacts, marker manager, grid config.

## Simulation Features
- World/model **SDF** authoring; includes from Fuel; nested models.
- **System plugins**: physics, sensors, user-commands, scene-broadcaster, imu, contact, joint-controller, diff-drive, ackermann-steering, thruster, buoyancy, lift-drag, wind, wheel-slip, joint-state-publisher, pose-publisher, odometry-publisher, **TriggeredPublisher**, LogRecord/Playback, detachable-joint, apply-link-wrench.
- **Levels & distributed simulation** for large worlds; performance via parallel ECS.
- **Logging/playback** of simulation state.
- Deterministic stepping, real-time factor control, headless (`-s` server) mode.
- Heightmaps, DEM terrain, particle emitters, actors/animations, lights, materials (PBR).

## ROS 2 Integration (`ros_gz`)
- **ros_gz_bridge** — bidirectional gz-transport ↔ ROS 2 topic/service bridge (per-type mapping).
- **ros_gz_sim** — launch Gazebo from ROS, spawn entities (`create`).
- **ros_gz_image** — efficient image transport bridge.
- **ros_gz_interfaces** — ROS msgs mirroring gz types.
- **gz_ros2_control** — ros2_control hardware plugin running inside Gazebo.
- **sdformat_urdf / xacro** — use URDF robots in SDF worlds.

## Tooling & CLI
- `gz sim` (run worlds, `-r` run, `-s` server, `-g` gui, `-v` verbose).
- `gz topic`, `gz service`, `gz model`, `gz sdf` (validate/print), `gz fuel`, `gz log`.
- **Fuel** web library: app.gazebosim.org — download/upload models & worlds.

## Domains / Use Cases
- Ground robots (diff-drive, ackermann), manipulators, drones (multicopter via thrust/lift-drag), underwater (buoyancy/hydrodynamics), maritime, legged.
- Integrates with Nav2, MoveIt 2, ros2_control through the bridge.
