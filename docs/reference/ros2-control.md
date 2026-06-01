# ros2_control + gz_ros2_control Reference

**Scope:** ROS 2 Jazzy + Gazebo Harmonic (`gz_ros2_control` jazzy branch)
**Purpose:** Canonical tags, YAML skeletons, and controller table for the SW2GZ plug-and-play exporter.

Sources:
- https://control.ros.org/jazzy/doc/gz_ros2_control/doc/index.html
- https://control.ros.org/jazzy/doc/ros2_controllers/doc/controllers_index.html
- https://control.ros.org/jazzy/doc/ros2_control/controller_manager/doc/userdoc.html
- https://control.ros.org/jazzy/doc/getting_started/getting_started.html
- https://github.com/ros-controls/gz_ros2_control (jazzy branch)
- https://github.com/ros-controls/ros2_control_demos (jazzy branch)

---

## 1. `<ros2_control>` URDF Block

### 1.1 Hardware Plugin (Gazebo Simulation)

The hardware plugin class is `gz_ros2_control/GazeboSimSystem`. One `<ros2_control>` block per robot is typical; multiple are valid for multi-arm or modular systems.

```xml
<ros2_control name="MyRobot" type="system">
  <hardware>
    <plugin>gz_ros2_control/GazeboSimSystem</plugin>
    <!-- Optional: hold joints in place when no controller claims them (default: true) -->
    <param name="hold_joints">true</param>
    <!-- Optional: position command → velocity gain (default: 0.1) -->
    <!-- velocity_cmd = position_proportional_gain * position_error * update_rate -->
    <param name="position_proportional_gain">0.1</param>
  </hardware>

  <!-- === Revolute / prismatic joint === -->
  <joint name="joint1">
    <!-- Command interface: what the controller writes -->
    <command_interface name="position">
      <param name="min">-3.14159</param>
      <param name="max"> 3.14159</param>
    </command_interface>
    <!-- Alternatively: velocity or effort command -->
    <!-- <command_interface name="velocity"><param name="min">-10</param><param name="max">10</param></command_interface> -->
    <!-- <command_interface name="effort"><param name="min">-100</param><param name="max">100</param></command_interface> -->

    <!-- State interfaces: what the controller reads back -->
    <state_interface name="position">
      <param name="initial_value">0.0</param>   <!-- joint initial position (rad or m) -->
    </state_interface>
    <state_interface name="velocity"/>
    <state_interface name="effort"/>
  </joint>

  <!-- === Continuous wheel joint (velocity-controlled) === -->
  <joint name="left_wheel_joint">
    <command_interface name="velocity">
      <param name="min">-20.0</param>
      <param name="max"> 20.0</param>
    </command_interface>
    <state_interface name="position"/>
    <state_interface name="velocity"/>
  </joint>

  <!-- === Mimic joint (no command interface; state only) === -->
  <!-- URDF must also have a <mimic joint="..."> tag on this joint -->
  <joint name="right_finger_joint">
    <!-- NO command_interface here -->
    <state_interface name="position"/>
    <state_interface name="velocity"/>
  </joint>

  <!-- === IMU sensor === -->
  <sensor name="imu_sensor">
    <state_interface name="orientation.x"/>
    <state_interface name="orientation.y"/>
    <state_interface name="orientation.z"/>
    <state_interface name="orientation.w"/>
    <state_interface name="angular_velocity.x"/>
    <state_interface name="angular_velocity.y"/>
    <state_interface name="angular_velocity.z"/>
    <state_interface name="linear_acceleration.x"/>
    <state_interface name="linear_acceleration.y"/>
    <state_interface name="linear_acceleration.z"/>
  </sensor>

  <!-- === Force-torque sensor === -->
  <sensor name="fts_sensor">
    <state_interface name="force.x"/>
    <state_interface name="force.y"/>
    <state_interface name="force.z"/>
    <state_interface name="torque.x"/>
    <state_interface name="torque.y"/>
    <state_interface name="torque.z"/>
  </sensor>
</ros2_control>
```

### 1.2 Interface Type Summary

| Tag name | Accepted values | Notes |
|---|---|---|
| `<command_interface name="…">` | `position`, `velocity`, `effort`, `acceleration` | Per joint, one or more. Must match controller expectations. |
| `<state_interface name="…">` | `position`, `velocity`, `effort`, `acceleration` | Per joint. `position` + `velocity` are the minimum for most controllers. |
| `<param name="min/max">` | float string | Enforced only if `enforce_command_limits: true` in controller_manager. |
| `<param name="initial_value">` | float string | Sets the joint's starting position/velocity when Gazebo starts. Particularly important for arm home poses. |

### 1.3 Mimic Joint Rules

- In URDF: `<mimic joint="parent_joint" multiplier="1.0" offset="0.0"/>` on the mimicking joint link.
- In `<ros2_control>`: the mimicking joint must have **no** `<command_interface>`. It may have `<state_interface>`.
- Known limitation in Gz Harmonic: `dart` physics engine does not support mimic constraints natively; the plugin falls back to a software implementation. See [issue #340](https://github.com/ros-controls/gz_ros2_control/issues/340).

---

## 2. Gazebo Plugin Tag

Add this block **outside** the `<ros2_control>` tag, anywhere in the URDF top level (typically after the last `<link>` / `<joint>`):

```xml
<gazebo>
  <plugin filename="libgz_ros2_control-system.so"
          name="gz_ros2_control::GazeboSimROS2ControlPlugin">
    <!-- Required: path to controllers YAML (use $(find ...) or absolute) -->
    <parameters>$(find my_robot_bringup)/config/controllers.yaml</parameters>

    <!-- Optional: load multiple YAML files (repeat tag) -->
    <!-- <parameters>$(find my_robot_bringup)/config/extra_controllers.yaml</parameters> -->

    <!-- Optional overrides (defaults shown) -->
    <robot_param>robot_description</robot_param>               <!-- parameter name for URDF -->
    <robot_param_node>robot_state_publisher</robot_param_node> <!-- node that owns robot_param -->
    <controller_manager_name>controller_manager</controller_manager_name>
  </plugin>
</gazebo>
```

**Key facts:**
- `filename` must be `libgz_ros2_control-system.so` (not `.so.1` or `.dylib`).
- The `<parameters>` tag is the bridge between the plugin and the controller YAML; without it, no controllers are loaded.
- `hold_joints` (in `<hardware>`) defaults to `true`: joints not claimed by any controller hold their last commanded position.

---

## 3. `controllers.yaml` Structure

### 3.1 Top-level layout

```yaml
# All controller types are declared under controller_manager
controller_manager:
  ros__parameters:
    update_rate: 1000   # Hz — control loop frequency; must be >= all controller update_rates

    # Each controller registered here (type only; params go in their own top-level key)
    joint_state_broadcaster:
      type: joint_state_broadcaster/JointStateBroadcaster

    joint_trajectory_controller:
      type: joint_trajectory_controller/JointTrajectoryController

    forward_position_controller:
      type: forward_command_controller/ForwardCommandController

# Controller-specific params live at top level under the controller's name
joint_trajectory_controller:
  ros__parameters:
    joints:
      - joint1
      - joint2
    command_interfaces: [position]
    state_interfaces:   [position, velocity]
```

> **Important:** The `type` field must be inside `controller_manager.ros__parameters.<name>.type`, NOT inside the controller's own `ros__parameters` block. Both locations were valid in Humble; Jazzy standardised on the `controller_manager` block.

---

## 4. Controllers Catalog

### 4.1 joint_state_broadcaster

**Plugin type:** `joint_state_broadcaster/JointStateBroadcaster`
**Role:** Reads all registered state interfaces and publishes `/joint_states` (`sensor_msgs/msg/JointState`) and `/dynamic_joint_states`. Must always be active — robot_state_publisher depends on it.
**Hardware required:** Any `position`, `velocity`, or `effort` state interfaces.

```yaml
joint_state_broadcaster:
  ros__parameters: {}   # No required params; publishes all available state interfaces
```

---

### 4.2 joint_trajectory_controller

**Plugin type:** `joint_trajectory_controller/JointTrajectoryController`
**Role:** Executes multi-joint trajectories via `FollowJointTrajectory` action (`control_msgs/action/FollowJointTrajectory`). The standard controller for robotic arms and manipulators.

```yaml
joint_trajectory_controller:
  ros__parameters:
    joints:
      - joint1
      - joint2
      - joint3

    # command_interfaces: one of the valid combinations below
    # position | position+velocity | position+velocity+acceleration | velocity | effort
    command_interfaces: [position]
    state_interfaces:   [position, velocity]   # minimum required

    # Trajectory following
    action_monitor_rate: 20.0          # Hz — how often goal status is checked
    allow_partial_joints_goal: false   # accept goals that name only a subset of joints
    interpolate_from_desired_state: true

    # Tolerances
    constraints:
      stopped_velocity_tolerance: 0.01   # rad/s — considered "stopped"
      goal_time: 0.0                     # 0 = no time limit
      joint1:
        trajectory: 0.05    # max allowed position error during execution (rad)
        goal: 0.03          # max allowed position error at goal

    # PID gains (required when command_interfaces = velocity or effort)
    # gains:
    #   joint1: {p: 100.0, i: 0.01, d: 10.0, i_clamp: 1.0}
```

**Hardware interface requirements:**
- `command_interfaces: [position]` → joints need `position` command interface
- `command_interfaces: [velocity]` → joints need `velocity` command interface + PID gains configured
- `command_interfaces: [effort]` → joints need `effort` command interface + PID gains configured

---

### 4.3 forward_command_controller

**Plugin type:** `forward_command_controller/ForwardCommandController`
**Role:** Directly forwards a `std_msgs/msg/Float64MultiArray` to a set of joints on one interface type. Lightweight; no trajectory interpolation.
**Subscribes:** `~/<controller_name>/commands` (`std_msgs/msg/Float64MultiArray`)

```yaml
# Position variant
forward_position_controller:
  ros__parameters:
    joints:
      - joint1
      - joint2
    interface_name: position   # one of: position, velocity, effort

# Velocity variant
forward_velocity_controller:
  ros__parameters:
    joints:
      - left_wheel_joint
      - right_wheel_joint
    interface_name: velocity

# Effort variant
forward_effort_controller:
  ros__parameters:
    joints:
      - joint1
    interface_name: effort
```

Convenience aliases (same plugin, pre-named):
- `position_controllers/JointGroupPositionController`
- `velocity_controllers/JointGroupVelocityController`
- `effort_controllers/JointGroupEffortController`

---

### 4.4 diff_drive_controller

**Plugin type:** `diff_drive_controller/DiffDriveController`
**Role:** Differential drive mobile base. Subscribes to `~/cmd_vel` (`geometry_msgs/msg/TwistStamped`), publishes odometry to `/odom` and optionally broadcasts `odom→base_link` TF.
**Hardware required:** `velocity` command + `position` (default) or `velocity` state interfaces on wheel joints.

```yaml
diffbot_base_controller:
  ros__parameters:
    left_wheel_names:  ["left_wheel_joint"]
    right_wheel_names: ["right_wheel_joint"]

    wheel_separation: 0.287       # metres (track width)
    wheel_radius:     0.033       # metres

    wheel_separation_multiplier:   1.0   # calibration corrector
    left_wheel_radius_multiplier:  1.0
    right_wheel_radius_multiplier: 1.0

    publish_rate:       50.0      # Hz
    odom_frame_id:      odom
    base_frame_id:      base_link
    enable_odom_tf:     true
    open_loop:          false     # true = use cmd_vel integration instead of encoder feedback
    position_feedback:  true      # false = use velocity state instead of position state
    cmd_vel_timeout:    0.5       # sec — stop wheels if no cmd_vel received

    pose_covariance_diagonal:  [0.001, 0.001, 0.001, 0.001, 0.001, 0.01]
    twist_covariance_diagonal: [0.001, 0.001, 0.001, 0.001, 0.001, 0.01]

    linear.x.max_velocity:  1.0
    linear.x.min_velocity: -1.0
    linear.x.max_acceleration: 1.0
    angular.z.max_velocity:  1.0
    angular.z.min_velocity: -1.0
    angular.z.max_acceleration: 1.0
```

---

### 4.5 ackermann_steering_controller

**Plugin type:** `ackermann_steering_controller/AckermannSteeringController`
**Role:** Four-wheel Ackermann (fixed rear traction + steerable front). Subscribes to `~/cmd_vel`.
**Hardware required:** Traction joints → `velocity` command+state. Steering joints → `position` command+state.

```yaml
ackermann_controller:
  ros__parameters:
    traction_right_joint: rear_right_wheel_joint
    traction_left_joint:  rear_left_wheel_joint
    steering_right_joint: front_right_steering_joint
    steering_left_joint:  front_left_steering_joint

    wheelbase:             2.5    # metres (front-to-rear axle)
    traction_track_width:  1.6    # metres (rear axle width)
    traction_wheels_radius: 0.33  # metres

    linear.x.has_velocity_limits:  true
    linear.x.max_velocity:         1.0
    angular.z.has_velocity_limits: true
    angular.z.max_velocity:        1.0
```

---

### 4.6 tricycle_controller

**Plugin type:** `tricycle_controller/TricycleController`
**Role:** Single front wheel that both steers and drives (or: single rear drive + two passive front). Subscribes to `~/cmd_vel` (`geometry_msgs/msg/TwistStamped`).
**Hardware required:** Traction joint → `velocity`. Steering joint → `position`.

```yaml
tricycle_controller:
  ros__parameters:
    traction_joint_name: traction_joint
    steering_joint_name: steering_joint
    wheel_radius:  0.1     # metres
    wheelbase:     0.5     # metres (front-to-rear)
    odom_frame_id: odom
    base_frame_id: base_link
    enable_odom_tf:  false
    cmd_vel_timeout: 500   # ms
    open_loop:       false
```

---

### 4.7 mecanum_drive_controller

**Plugin type:** `mecanum_drive_controller/MecanumDriveController`
**Role:** Four mecanum wheels (omnidirectional). Subscribes to `<controller_name>/reference` (`geometry_msgs/msg/TwistStamped`, linear x/y + angular z).
**Hardware required:** All four wheel joints → `velocity` command + `velocity` state.

```yaml
mecanum_controller:
  ros__parameters:
    front_left_wheel_command_joint_name:  front_left_wheel_joint
    front_right_wheel_command_joint_name: front_right_wheel_joint
    rear_right_wheel_command_joint_name:  rear_right_wheel_joint
    rear_left_wheel_command_joint_name:   rear_left_wheel_joint

    kinematics:
      base_frame_offset: {x: 0.0, y: 0.0, theta: 0.0}
      wheels_radius: 0.05
      sum_of_robot_center_projection_on_X_Y_axis: 0.4  # (half_wheelbase + half_track)

    odom_frame_id:     odom
    base_frame_id:     base_link
    enable_odom_tf:    true
    reference_timeout: 0.9   # sec

    linear.x.max_velocity: 1.0
    linear.y.max_velocity: 1.0
    angular.z.max_velocity: 1.0
```

---

### 4.8 imu_sensor_broadcaster

**Plugin type:** `imu_sensor_broadcaster/IMUSensorBroadcaster`
**Role:** Publishes `sensor_msgs/msg/Imu` on `~/<controller_name>/imu`.
**Hardware required:** sensor named `<sensor_name>` with orientation/angular_velocity/linear_acceleration state interfaces (see Section 1.1 sensor block).

```yaml
imu_broadcaster:
  ros__parameters:
    sensor_name: imu_sensor          # must match <sensor name="..."> in ros2_control URDF
    frame_id:    imu_sensor_frame
    static_covariance_orientation:          [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0]
    static_covariance_angular_velocity:     [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0]
    static_covariance_linear_acceleration:  [0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0]
```

---

### 4.9 force_torque_sensor_broadcaster

**Plugin type:** `force_torque_sensor_broadcaster/ForceTorqueSensorBroadcaster`
**Role:** Publishes `geometry_msgs/msg/WrenchStamped` on `~/<controller_name>/wrench`. Optional `_filtered` topic when filtering is enabled.
**Hardware required:** sensor named `<sensor_name>` with force/torque state interfaces.

```yaml
fts_broadcaster:
  ros__parameters:
    sensor_name: fts_sensor          # must match <sensor name="..."> in ros2_control URDF
    frame_id:    fts_sensor_frame
    # Optional: custom interface name overrides (if sensor uses non-standard naming)
    # interface_names:
    #   force:  {x: "fts/force.x", y: "fts/force.y", z: "fts/force.z"}
    #   torque: {x: "fts/torque.x", y: "fts/torque.y", z: "fts/torque.z"}
    offset:
      force:  {x: 0.0, y: 0.0, z: 0.0}
      torque: {x: 0.0, y: 0.0, z: 0.0}
    multiplier:
      force:  {x: 1.0, y: 1.0, z: 1.0}
      torque: {x: 1.0, y: 1.0, z: 1.0}
```

---

## 5. Controller Manager YAML (complete update_rate block)

The `controller_manager` stanza always lives at the top of `controllers.yaml`:

```yaml
controller_manager:
  ros__parameters:
    update_rate: 1000   # Hz — main control loop; 100–1000 Hz typical

    # Declare every controller that will be spawned (type field is mandatory)
    joint_state_broadcaster:
      type: joint_state_broadcaster/JointStateBroadcaster

    arm_controller:
      type: joint_trajectory_controller/JointTrajectoryController

    base_controller:
      type: diff_drive_controller/DiffDriveController

    imu_broadcaster:
      type: imu_sensor_broadcaster/IMUSensorBroadcaster

# Then controller-specific params follow at top level:
arm_controller:
  ros__parameters:
    joints: [shoulder_pan, shoulder_lift, elbow, wrist_1, wrist_2, wrist_3]
    command_interfaces: [position]
    state_interfaces:   [position, velocity]
    ...
```

---

## 6. Launch File Pattern

Canonical Python launch pattern (Jazzy, from ros2_control_demos):

```python
from launch import LaunchDescription
from launch.actions import DeclareLaunchArgument, RegisterEventHandler
from launch.event_handlers import OnProcessExit
from launch.substitutions import Command, LaunchConfiguration, PathSubstitution
from launch_ros.actions import Node
from launch_ros.substitutions import FindPackageShare
from launch_ros.parameter_descriptions import ParameterFile

def generate_launch_description():
    # 1. Process URDF/Xacro → string
    robot_description = {
        "robot_description": Command([
            "xacro ",
            PathSubstitution(FindPackageShare("my_robot_description"))
            / "urdf" / "robot.urdf.xacro",
        ])
    }

    # 2. robot_state_publisher — listens to /joint_states from joint_state_broadcaster
    robot_state_pub = Node(
        package="robot_state_publisher",
        executable="robot_state_publisher",
        parameters=[robot_description],
        output="both",
    )

    # 3. Controller Manager (ros2_control_node) — Gazebo provides its own;
    #    for non-Gazebo use: explicitly launch ros2_control_node.
    #    With gz_ros2_control plugin, the plugin itself starts controller_manager
    #    inside the Gazebo process — do NOT also launch ros2_control_node.

    # 4. Spawners — each spawner loads and activates one controller.
    #    Always spawn joint_state_broadcaster FIRST (others may depend on it).
    #    Use RegisterEventHandler(OnProcessExit) to chain spawners sequentially.
    spawn_jsb = Node(
        package="controller_manager",
        executable="spawner",
        arguments=[
            "joint_state_broadcaster",
            "--param-file",
            PathSubstitution(FindPackageShare("my_robot_bringup"))
            / "config" / "controllers.yaml",
        ],
    )

    spawn_arm = Node(
        package="controller_manager",
        executable="spawner",
        arguments=[
            "arm_controller",
            "--param-file",
            PathSubstitution(FindPackageShare("my_robot_bringup"))
            / "config" / "controllers.yaml",
        ],
    )

    # Optional: delay arm_controller until joint_state_broadcaster is up
    spawn_arm_after_jsb = RegisterEventHandler(
        event_handler=OnProcessExit(
            target_action=spawn_jsb,
            on_exit=[spawn_arm],
        )
    )

    return LaunchDescription([
        robot_state_pub,
        spawn_jsb,
        spawn_arm_after_jsb,
    ])
```

**Key rule for Gazebo:** The `gz_ros2_control::GazeboSimROS2ControlPlugin` starts a `controller_manager` node embedded in the Gazebo process. Do **not** also launch a standalone `ros2_control_node`; that creates two competing controller managers.

---

## 7. Controller Selection Decision Table

The SW2GZ exporter should inspect joint topology and emit the appropriate controller.

| Robot type / joint pattern | Primary controller | Secondary | Notes |
|---|---|---|---|
| Fixed-base arm (revolute/prismatic joints, 1–N DOF) | `joint_trajectory_controller` | `joint_state_broadcaster` | Default for any articulated chain without a mobile base |
| Fixed-base arm, teleoperation / simple commanding | `forward_command_controller` | `joint_state_broadcaster` | Use when only streaming raw position/velocity commands |
| Differential drive (exactly 2 driven wheels detected) | `diff_drive_controller` | `joint_state_broadcaster` | Detect by: exactly 2 continuous joints tagged as drive wheels in model metadata |
| Ackermann (4 wheels: 2 drive + 2 steer) | `ackermann_steering_controller` | `joint_state_broadcaster` | Requires wheelbase + track width from model |
| Tricycle (1 drive + 1 steer, or 1 combined) | `tricycle_controller` | `joint_state_broadcaster` | Requires wheelbase + wheel radius |
| Mecanum / omni (4 mecanum wheels) | `mecanum_drive_controller` | `joint_state_broadcaster` | Requires wheel radius + (half_wheelbase + half_track) sum |
| IMU sensor link present | `imu_sensor_broadcaster` | — | `<sensor>` block in ros2_control + Gazebo IMU sensor plugin in `<gazebo>` block |
| Force-torque sensor at wrist/flange | `force_torque_sensor_broadcaster` | — | `<sensor>` block + `<preserveFixedJoint>true</preserveFixedJoint>` in `<gazebo>` |
| Gripper (1–2 prismatic finger joints) | `forward_command_controller` | `joint_state_broadcaster` | Or `gripper_controller/GripperActionController` for action interface |

---

## 8. Metadata the Exporter Must Collect from the SW Model

### 8.1 Per joint
- **Name** (must be valid URDF/XML NCName, no spaces)
- **Type**: revolute, prismatic, continuous, fixed
- **Axis**: (not directly in ros2_control but needed for correct URDF)
- **Limits**: lower, upper (for `<param name="min/max">`)
- **Initial position**: home/rest pose value → `<param name="initial_value">`
- **Control mode**: position / velocity / effort → sets `command_interface`

### 8.2 Per robot / sub-assembly
- **Base mobility type**: fixed / diff_drive / ackermann / tricycle / mecanum / other
- **Drive wheel joint names** (for diff/mecanum): left/right or FL/FR/RL/RR
- **Steer joint names** (for ackermann/tricycle)
- **Wheel geometry**: radius (m), separation/track (m), wheelbase (m)
- **IMU link presence**: link name + sensor frame
- **FTS joint presence**: joint name + sensor frame
- **Mimic joint pairs**: (mimicker, mimicked, multiplier, offset)

### 8.3 Package-level
- **Package name** (for `$(find ...)` substitutions)
- **Preferred update_rate** (default: 1000 Hz for simulation)

---

## 9. Quick Reference: Plugin Type Strings

| Controller | type value in YAML |
|---|---|
| Joint State Broadcaster | `joint_state_broadcaster/JointStateBroadcaster` |
| Joint Trajectory Controller | `joint_trajectory_controller/JointTrajectoryController` |
| Forward Command Controller | `forward_command_controller/ForwardCommandController` |
| Diff Drive Controller | `diff_drive_controller/DiffDriveController` |
| Ackermann Steering Controller | `ackermann_steering_controller/AckermannSteeringController` |
| Tricycle Controller | `tricycle_controller/TricycleController` |
| Mecanum Drive Controller | `mecanum_drive_controller/MecanumDriveController` |
| IMU Sensor Broadcaster | `imu_sensor_broadcaster/IMUSensorBroadcaster` |
| Force Torque Sensor Broadcaster | `force_torque_sensor_broadcaster/ForceTorqueSensorBroadcaster` |
| Gripper Action Controller | `gripper_controllers/GripperActionController` |
| PID Controller | `pid_controller/PidController` |

---

## 10. Common Pitfalls / Exporter Checklist

1. **`<ros2_control>` must be in the URDF before the `<gazebo>` plugin block** — order matters for some parsers.
2. **Plugin filename is `libgz_ros2_control-system.so`** — not `libgz_ros2_control.so` or any variant.
3. **`<parameters>` path must be resolvable at runtime** — use `$(find pkg)` or an absolute path injected by the launch file.
4. **controller_manager `type` declarations must be inside `controller_manager.ros__parameters`** — not in the controller's own stanza.
5. **joint_state_broadcaster must always be spawned** — robot_state_publisher will not publish TF without it.
6. **Mimic joints: no `<command_interface>` in ros2_control URDF block** — the plugin handles mimicking internally; adding a command interface causes a conflict.
7. **`initial_value` goes on `<state_interface name="position">`, not on `<command_interface>`**.
8. **Sensor `<state_interface>` names must exactly match the sensor semantic component convention** — for IMU: `orientation.x/.y/.z/.w`, `angular_velocity.x/.y/.z`, `linear_acceleration.x/.y/.z`. The imu_sensor_broadcaster uses `<sensor_name>/` prefix when claiming interfaces.
9. **diff_drive_controller subscribes to `TwistStamped`, not `Twist`** (changed in Jazzy from Humble). If your nav stack publishes plain `Twist`, remap with `--controller-ros-args -r ~/cmd_vel:=/cmd_vel`.
10. **Do not launch `ros2_control_node` alongside Gazebo** — the gz_ros2_control plugin owns the controller_manager. Launching both creates dual managers and duplicated topics.
