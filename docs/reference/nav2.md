# Nav2 Mobile-Robot Requirements Reference

> **Scope:** This document is a reference for the SW2GZ exporter team.
> It captures exactly what Nav2 (ROS 2 Jazzy, Nav2 1.0.0) needs from a
> robot model so that an exported mobile base is **plug-and-play** with
> the navigation stack.  Arms / static manipulators do **not** need Nav2;
> every item in this document is a **mobile-base-only** concern unless
> noted otherwise.

---

## 1. TF Frame Tree (REP-105)

### 1.1 Required frame hierarchy

```
earth  (optional – GPS/multi-robot only)
  └── map              ← global, world-fixed, may jump discretely
        └── odom       ← local, world-fixed, drift-prone but continuous
              └── base_link (or base_footprint → base_link)
                    ├── laser_frame   (rigid, from URDF)
                    ├── camera_frame  (rigid, from URDF)
                    ├── imu_link      (rigid, from URDF, optional)
                    ├── left_wheel    (revolute, from URDF + joint states)
                    └── right_wheel   (revolute, from URDF + joint states)
```

Source: [REP-105](https://www.ros.org/reps/rep-0105.html)

### 1.2 Frame definitions

| Frame | Character | Who publishes it |
|---|---|---|
| `map` | World-fixed global frame; Z up; may jump on localisation update | AMCL / SLAM (publishes `map → odom`) |
| `odom` | World-fixed local frame; continuous, no discrete jumps; drifts long-term | diff_drive_controller or Gz plugin (publishes `odom → base_link`) |
| `base_link` | Rigidly attached to robot chassis; position/orientation defined by hardware | robot_state_publisher (from URDF) |
| `base_footprint` | Virtual; projection of `base_link` onto ground plane; no geometry/collision | robot_state_publisher (fixed joint in URDF) |
| `laser_frame` | Where the lidar is mounted | robot_state_publisher (fixed joint in URDF) |
| `earth` | ECEF origin; only needed for GPS/multi-map | Optional external node |

### 1.3 base_link vs base_footprint

AMCL defaults to `base_footprint` as its `base_frame_id`.  Nav2 costmaps
default to `base_link`.  Best practice:

- Define both in the URDF.
- Connect them with a **fixed joint** offset only in Z (height of chassis centre above floor).
- The exporter should emit `base_footprint` as the root virtual link and
  `base_link` as its only child, separated by the chassis ground-clearance.

```xml
<!-- in URDF -->
<link name="base_footprint"/>          <!-- no geometry -->
<link name="base_link">
  <visual>...</visual>
  <collision>...</collision>
  <inertial>...</inertial>
</link>
<joint name="base_footprint_to_base_link" type="fixed">
  <parent link="base_footprint"/>
  <child  link="base_link"/>
  <origin xyz="0 0 0.096" rpy="0 0 0"/>  <!-- Z = chassis height above ground -->
</joint>
```

---

## 2. Coordinate Conventions (REP-103)

Source: [REP-103](https://www.ros.org/reps/rep-0103.html)

| Rule | Value |
|---|---|
| Body frame axes | X forward, Y left, Z up |
| All systems | Right-handed |
| Angles | Radians |
| Length | Metres |
| Mass | Kilograms |
| Rotation representation (preferred) | Quaternion → rotation matrix → fixed-axis RPY |
| Yaw zero direction | Pointing east (geographic) / forward (body) |
| Camera optical frames | Z forward, X right, Y down; suffix `_optical` |

The SolidWorks→URDF export must reorient geometry so X is forward and Z
is up on the exported `base_link`, or emit a corrective fixed joint.

---

## 3. URDF / Model Requirements

### 3.1 Mandatory links and joints

| Element | Type | Notes |
|---|---|---|
| `base_footprint` | link (no geometry) | Root of kinematic tree for Nav2 |
| `base_link` | link | Main chassis; must have `<inertial>`, `<collision>`, `<visual>` |
| `left_wheel` / `right_wheel` | links | Cylindrical collision geometry |
| `left_wheel_joint` / `right_wheel_joint` | joints, type `continuous` | Rotation axis = Y (wheel spins around Y in body frame) |
| `laser_link` (or `base_scan`) | link | No geometry needed; pose must match physical mount |
| `laser_joint` | joint, type `fixed` | Connects laser_link to base_link (or a bracket link) |

### 3.2 Collision geometry quality

- Use **simplified primitives** (cylinder, box, sphere) for `<collision>`,
  not the raw STL mesh.  Nav2 costmaps do not use URDF collision directly
  but Gazebo physics and `robot_state_publisher` use it for footprint
  checks during simulation.
- The chassis `<collision>` bounding box must accurately represent the
  robot's width/length — these values feed directly into `robot_radius`
  and the costmap `footprint` polygon.

### 3.3 ros2_control hardware-interface block (Gazebo Sim)

For simulation the URDF needs an embedded `<ros2_control>` block:

```xml
<ros2_control name="GazeboSystem" type="system">
  <hardware>
    <plugin>gz_ros2_control/GazeboSimSystem</plugin>
  </hardware>
  <joint name="left_wheel_joint">
    <command_interface name="velocity"/>
    <state_interface name="velocity"/>
    <state_interface name="position"/>
  </joint>
  <joint name="right_wheel_joint">
    <command_interface name="velocity"/>
    <state_interface name="velocity"/>
    <state_interface name="position"/>
  </joint>
</ros2_control>
```

And a Gazebo plugin tag:

```xml
<gazebo>
  <plugin filename="gz_ros2_control-system"
          name="gz_ros2_control::GazeboSimROS2ControlPlugin">
    <parameters>$(find my_robot_description)/config/controllers.yaml</parameters>
  </plugin>
</gazebo>
```

Source: [gz_ros2_control docs](https://control.ros.org/humble/doc/gz_ros2_control/doc/index.html)

### 3.4 Sensor SDF / Gazebo plugin (lidar)

```xml
<gazebo reference="laser_link">
  <sensor name="lidar" type="ray">
    <always_on>true</always_on>
    <visualize>true</visualize>
    <update_rate>10</update_rate>
    <ray>
      <scan>
        <horizontal>
          <samples>360</samples>
          <resolution>1</resolution>
          <min_angle>-3.14159</min_angle>
          <max_angle>3.14159</max_angle>
        </horizontal>
      </scan>
      <range>
        <min>0.12</min>
        <max>30.0</max>
        <resolution>0.015</resolution>
      </range>
    </ray>
    <plugin name="scan" filename="libgazebo_ros_ray_sensor.so">
      <ros>
        <remapping>~/out:=scan</remapping>
      </ros>
      <output_type>sensor_msgs/LaserScan</output_type>
      <frame_name>laser_link</frame_name>
    </plugin>
  </sensor>
</gazebo>
```

The plugin publishes `sensor_msgs/LaserScan` on `/scan`.
The `frame_name` must match the URDF link name exactly.

Source: [Nav2 Sensor Setup (Gazebo)](https://docs.nav2.org/setup_guides/sensors/setup_sensors_gz.html)

---

## 4. Odometry + Velocity Command Path

### 4.1 Data flow

```
Gazebo physics
    │ joint velocities / positions
    ▼
gz_ros2_control  ←→  diff_drive_controller
    │ publishes
    ├─► /odom          (nav_msgs/Odometry)
    ├─► TF: odom → base_link
    └─► /joint_states  (sensor_msgs/JointState)

Operator / Nav2 controller_server
    │
    └─► /cmd_vel       (geometry_msgs/Twist)
             │ subscribed by
             └── diff_drive_controller
```

### 4.2 diff_drive_controller configuration (controllers.yaml)

```yaml
controller_manager:
  ros__parameters:
    update_rate: 100  # Hz
    joint_state_broadcaster:
      type: joint_state_broadcaster/JointStateBroadcaster
    diff_drive_controller:
      type: diff_drive_controller/DiffDriveController

diff_drive_controller:
  ros__parameters:
    left_wheel_names:  ["left_wheel_joint"]
    right_wheel_names: ["right_wheel_joint"]

    wheel_separation: 0.287     # metres  ← comes from robot model
    wheel_radius:     0.033     # metres  ← comes from robot model

    use_stamped_vel: false
    publish_rate: 50.0          # Hz for /odom
    odom_frame_id:  odom
    base_frame_id:  base_link
    enable_odom_tf: true        # publishes odom → base_link TF

    # velocity + acceleration limits  ← must match physical robot
    linear.x.max_velocity:   0.5
    linear.x.min_velocity:  -0.5
    angular.z.max_velocity:  2.0
    angular.z.min_velocity: -2.0
    linear.x.max_acceleration:  2.5
    angular.z.max_acceleration: 3.2
```

Key model-derived values the exporter must pass or document:
- `wheel_separation` — measured across wheel contact centrelines
- `wheel_radius` — measured at tyre contact patch
- Joint names — must match the URDF joint names exactly

Source: [diff_drive_controller package](https://index.ros.org/p/diff_drive_controller/)

### 4.3 Required topics summary

| Topic | Type | Direction | Publisher |
|---|---|---|---|
| `/odom` | `nav_msgs/Odometry` | out | diff_drive_controller |
| `/cmd_vel` | `geometry_msgs/Twist` | in | Nav2 controller_server |
| `/scan` | `sensor_msgs/LaserScan` | out | Gazebo lidar plugin |
| `/joint_states` | `sensor_msgs/JointState` | out | joint_state_broadcaster |
| `/robot_description` | `std_msgs/String` (latched) | out | robot_state_publisher |
| `/tf` | TF2 tree | out | robot_state_publisher + diff_drive_controller |

---

## 5. Nav2 Parameter Sections Driven by the Robot Model

Below are the `nav2_params.yaml` sections whose values **must be changed**
to match the exported robot.  All other parameters can start from the
Nav2 default file at
[nav2_bringup/params/nav2_params.yaml](https://github.com/ros-navigation/navigation2/blob/main/nav2_bringup/params/nav2_params.yaml).

### 5.1 Costmap (local and global) — robot shape

```yaml
local_costmap:
  local_costmap:
    ros__parameters:
      global_frame: odom          # fixed — local costmap uses odom
      robot_base_frame: base_link # matches URDF link name
      # Option A — circular robot:
      robot_radius: 0.22          # ← half the robot's widest dimension (m)
      # Option B — rectangular robot (comment out robot_radius):
      # footprint: "[[0.21,0.195],[0.21,-0.195],[-0.21,-0.195],[-0.21,0.195]]"
      footprint_padding: 0.03     # safety margin (m)

global_costmap:
  global_costmap:
    ros__parameters:
      global_frame: map
      robot_base_frame: base_link
      robot_radius: 0.22          # typically use circular for global planner
```

### 5.2 Costmap obstacle layer — sensor topic

```yaml
      obstacle_layer:
        enabled: true
        observation_sources: scan
        scan:
          topic: /scan                    # ← must match lidar plugin remap
          max_obstacle_height: 2.0
          clearing: true
          marking: true
          data_type: LaserScan
          raytrace_max_range: 3.0
          raytrace_min_range: 0.0
          obstacle_max_range: 2.5
          obstacle_min_range: 0.0
```

### 5.3 Velocity smoother — robot kinematics

```yaml
velocity_smoother:
  ros__parameters:
    max_velocity:     [0.5, 0.0, 2.0]   # [vx, vy, wz] m/s, rad/s
    min_velocity:    [-0.5, 0.0,-2.0]   # ← derived from motor limits
    max_accel:        [2.5, 0.0, 3.2]   # m/s², rad/s²
    max_decel:       [-2.5, 0.0,-3.2]
    deadband_velocity: [0.0, 0.0, 0.0]
    # Differential drive: vy always 0.0
```

### 5.4 MPPI controller — robot-specific limits

```yaml
controller_server:
  ros__parameters:
    controller_plugins: ["FollowPath"]
    FollowPath:
      plugin: "nav2_mppi_controller::MPPIController"
      motion_model: DiffDrive         # ← DiffDrive | Ackermann | Omni
      vx_max:  0.5                    # ← from robot model
      vx_min: -0.35
      wz_max:  1.9
      vx_std:  0.2
      wz_std:  0.4
```

### 5.5 AMCL — frame names

```yaml
amcl:
  ros__parameters:
    base_frame_id: base_footprint   # ← default; change if no base_footprint
    odom_frame_id: odom
    global_frame_id: map
    scan_topic: scan                # ← must match lidar topic
    laser_model_type: likelihood_field
    max_beams: 60
    laser_max_range: 100.0
```

### 5.6 BT Navigator — frame names

```yaml
bt_navigator:
  ros__parameters:
    global_frame: map
    robot_base_frame: base_link
    odom_topic: /odom               # ← must match diff_drive output
    default_nav_to_pose_bt_xml: ""  # uses built-in default when empty
```

---

## 6. Sensor Requirements

### 6.1 Minimum sensor set for Nav2

| Sensor | Purpose | Required? |
|---|---|---|
| 2D lidar (360° preferred) | Obstacle layer, AMCL localisation, SLAM | **Yes** |
| Wheel encoders (via diff_drive) | Odometry / `odom → base_link` TF | **Yes** |
| IMU | Fuse with wheel odom via `robot_localization` for better state estimation | Recommended |
| Depth camera / 3D lidar | Voxel layer for 3D obstacles | Optional |
| GPS | `navsat_transform` + `robot_localization` for outdoor global localisation | Optional |

### 6.2 Lidar placement rules

- Mount **above** all obstacles the robot will encounter (avoids self-hits).
- Laser scan `frame_id` in the published message must match the URDF link
  name for `robot_state_publisher` to publish the correct TF.
- Typical 2D lidar frame: `laser_link` or `base_scan`; both are
  conventional — pick one and be consistent across URDF, Gazebo plugin,
  and `nav2_params.yaml` obstacle layer.

### 6.3 Depth camera (optional voxel layer)

```yaml
      voxel_layer:
        enabled: true
        observation_sources: pointcloud
        pointcloud:
          topic: /camera/depth/points
          data_type: PointCloud2
          max_obstacle_height: 2.0
          min_obstacle_height: 0.1
          obstacle_max_range: 2.5
          raytrace_max_range: 3.0
```

---

## 7. Minimal Nav2 Bringup Checklist

Before calling `ros2 launch nav2_bringup bringup_launch.py`:

- [ ] `robot_state_publisher` running with the exported URDF
- [ ] `diff_drive_controller` (or Gz plugin) publishing `/odom` and
      `odom → base_link` TF
- [ ] Lidar publishing `sensor_msgs/LaserScan` on `/scan`
- [ ] `map_server` providing a `nav_msgs/OccupancyGrid` on `/map`
      (or SLAM toolbox building it live)
- [ ] `amcl` (or `slam_toolbox`) publishing `map → odom` TF
- [ ] `nav2_params.yaml` updated with robot's `robot_radius` (or
      `footprint`), `wheel_separation`, `wheel_radius`, velocity limits,
      and correct frame names

---

## 8. What SW2GZ Must Produce for a Nav2-Ready Mobile Robot

The following items are **not** emitted by a static-arm URDF exporter and
must be added when the export target is a mobile base.

### 8.1 URDF additions (mobile-only)

| Item | Static-arm exporter | Mobile Nav2 exporter |
|---|---|---|
| `base_footprint` virtual root link | No | **Yes** — AMCL default frame |
| Fixed joint `base_footprint → base_link` (Z offset = ground clearance) | No | **Yes** |
| Left/right wheel links with cylindrical collision | No | **Yes** |
| Continuous wheel joints with correct rotation axis (Y) | No | **Yes** |
| `laser_link` + fixed joint at measured mount pose | No | **Yes** |
| `<inertial>` on every physics-relevant link | Rarely | **Yes** — needed by Gz physics |
| Simplified `<collision>` primitives (not raw STL) for chassis + wheels | Rarely | **Yes** |
| `<ros2_control>` hardware block with velocity command + state interfaces | No | **Yes** |
| Gazebo plugin tag for `gz_ros2_control` | No | **Yes** |
| Gazebo lidar sensor block on `laser_link` | No | **Yes** |

### 8.2 Configuration files (mobile-only)

| File | Purpose |
|---|---|
| `config/controllers.yaml` | `diff_drive_controller` with correct `wheel_separation`, `wheel_radius`, joint names, velocity limits |
| `config/nav2_params.yaml` | Pre-filled with robot `robot_radius`/`footprint`, `robot_base_frame`, lidar topic, velocity limits |
| `launch/nav2_bringup.launch.py` | Wires robot_state_publisher, controller_manager, Nav2 bringup, AMCL/SLAM |

### 8.3 Values the exporter must extract from the SolidWorks model

| Value | Where used |
|---|---|
| `wheel_separation` (centre-to-centre) | `controllers.yaml`, nav2 kinematics |
| `wheel_radius` | `controllers.yaml` |
| Chassis bounding box (width × length) | `robot_radius` or `footprint` polygon in `nav2_params.yaml` |
| Chassis ground clearance (Z of `base_link` above floor) | Fixed joint Z offset `base_footprint → base_link` |
| Lidar mount position + orientation (XYZ + RPY) | Fixed joint for `laser_joint` |
| Robot max linear velocity, max angular velocity | `velocity_smoother`, MPPI controller params |

---

## 9. Gaps vs a Static-Arm URDF Exporter

A static-arm exporter (e.g., exporting a KUKA or UR arm) produces:

- Kinematic chain of links + revolute/prismatic joints
- Visual and collision geometry per link
- `<inertial>` per link
- Possibly Gazebo effort controllers

It does **not** produce — and Nav2 requires — all of the following:

| Gap | Impact if missing |
|---|---|
| No `base_footprint` | AMCL uses wrong default frame; localisation fails |
| No `odom` frame / odometry publisher | `odom → base_link` TF missing; costmaps cannot update; Nav2 refuses to start |
| No diff_drive controller wiring | Robot does not move in response to `/cmd_vel`; controller_server cannot execute paths |
| No lidar link/joint in URDF | TF lookup for sensor data fails; obstacle layer gets no data; costmap empty |
| No lidar Gazebo plugin | `/scan` topic never published; AMCL starves |
| No `robot_radius` / `footprint` in nav2_params | Costmap uses default 0.22 m radius regardless of real robot size; collision unsafe |
| No `wheel_separation` / `wheel_radius` | Odometry scale wrong; controller_server sends robot in wrong direction |
| No `controllers.yaml` | ros2_control has no controller to load; no odom, no cmd_vel subscriber |
| No `nav2_params.yaml` | Nav2 fails to launch without a parameters file |
| No `velocity limits` | velocity_smoother passes unsafe commands to motors |

---

## 10. Source Links

| Document | URL |
|---|---|
| REP-103 Standard Units and Coordinate Conventions | https://www.ros.org/reps/rep-0103.html |
| REP-105 Coordinate Frames for Mobile Platforms | https://www.ros.org/reps/rep-0105.html |
| Nav2 Concepts | https://docs.nav2.org/concepts/index.html |
| Nav2 Getting Started | https://docs.nav2.org/getting_started/index.html |
| Nav2 URDF Setup | https://docs.nav2.org/setup_guides/urdf/setup_urdf.html |
| Nav2 TF / Transforms Setup | https://docs.nav2.org/setup_guides/transformation/setup_transforms.html |
| Nav2 Odometry Setup (Gazebo Sim) | https://docs.nav2.org/setup_guides/odom/setup_odom_gz.html |
| Nav2 Sensor Setup (Gazebo Sim) | https://docs.nav2.org/setup_guides/sensors/setup_sensors_gz.html |
| Nav2 Footprint Setup | https://docs.nav2.org/setup_guides/footprint/setup_footprint.html |
| Nav2 Costmap 2D Configuration | https://docs.nav2.org/configuration/packages/configuring-costmaps.html |
| Nav2 AMCL Configuration | https://docs.nav2.org/configuration/packages/configuring-amcl.html |
| Nav2 Tuning Guide | https://docs.nav2.org/tuning/index.html |
| Nav2 Mapping and Localisation | https://docs.nav2.org/setup_guides/sensors/mapping_localization.html |
| Nav2 default nav2_params.yaml | https://github.com/ros-navigation/navigation2/blob/main/nav2_bringup/params/nav2_params.yaml |
| nav2_tutorials sam_bot nav2_params.yaml | https://github.com/ros-navigation/navigation2_tutorials/blob/master/sam_bot_description/config/nav2_params.yaml |
| gz_ros2_control documentation | https://control.ros.org/humble/doc/gz_ros2_control/doc/index.html |
| diff_drive_controller package | https://index.ros.org/p/diff_drive_controller/ |
