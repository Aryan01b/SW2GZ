# ROS 2 Jazzy Jalisco — Module Index & Feature Summary

LTS release (May 2024, supported to May 2029). Targets Ubuntu 24.04, Windows 10, RHEL 9. Python 3.12, DDS middleware (Fast DDS default, Cyclone DDS option).

## Core (rcl / rclcpp / rclpy)
- Nodes, topics (pub/sub), services, actions, parameters.
- Executors: single-threaded, multi-threaded, static; callback groups.
- QoS profiles: reliability, durability, history, deadline, liveliness.
- Lifecycle (managed) nodes: configure→activate→deactivate→cleanup.
- Components / composition: multiple nodes in one process (intra-process zero-copy).
- Time: ROS time vs system time, simulated `/clock`.
- Parameters: declare, get/set, callbacks, YAML param files.

## Build & Tooling
- **ament** build system, **colcon** build tool.
- **ros2 CLI**: `node`, `topic`, `service`, `action`, `param`, `bag`, `launch`, `pkg`, `interface`, `doctor`.
- **Launch**: Python/XML/YAML launch files, substitutions, event handlers, conditions.
- **rosbag2**: record/play, multiple storage plugins (sqlite3, mcap).
- **rosidl**: msg/srv/action interface definition & generation.

## Common Libraries
- **tf2**: coordinate transforms, `tf2_ros`, static/dynamic broadcasters, buffer/listener.
- **robot_state_publisher**: reads URDF + `/joint_states` → publishes TF tree of links.
- **joint_state_publisher**: publishes `/joint_states` (GUI variant for manual jogging).
- **urdf / xacro**: robot description (links, joints, inertials, visuals, collisions).
- **geometry_msgs, sensor_msgs, nav_msgs, std_msgs, vision_msgs**: standard interfaces.
- **image_transport, cv_bridge**: image streaming & OpenCV bridge.
- **diagnostics**: aggregator, analyzers, updater.

## Visualization & Simulation
- **RViz2**: 3D visualization of TF, robot model, sensors, costmaps, paths.
- **rqt**: plugin GUI (graph, console, plot, tf_tree, reconfigure).
- **Gazebo bridge**: `ros_gz` (ros_gz_bridge, ros_gz_sim, ros_gz_image) — ROS↔Gazebo Harmonic.

## ros2_control (Control Framework)
- **controller_manager**: loads/activates controllers, runs update loop.
- **Hardware interfaces**: SystemInterface / ActuatorInterface / SensorInterface; command & state interfaces; read()/write() lifecycle.
- **Resource manager**: claims & arbitrates hardware interfaces.
- **ros2_controllers** (stock): joint_trajectory_controller, diff_drive_controller, joint_state_broadcaster, forward_command_controller, position/velocity/effort controllers, admittance_controller, tricycle/ackermann, imu_sensor_broadcaster, force_torque_sensor_broadcaster, gripper_controller.
- **Hardware plugins** via pluginlib; **mock/test** hardware for sim.
- **controller_manager CLI / spawner** for launch.

## Nav2 (Navigation 2)
- **BT Navigator**: behavior-tree orchestration of navigation tasks.
- **Planner server**: global path (NavFn, Smac 2D/Hybrid-A*/State-Lattice, Theta*).
- **Controller server**: local control (DWB, RPP—Regulated Pure Pursuit, MPPI, TEB ext.).
- **Smoother server**: path smoothing.
- **Costmap 2D**: layered (static, obstacle, voxel, inflation) global & local maps.
- **Behavior server**: spin, backup, wait, drive-on-heading recoveries.
- **AMCL**: particle-filter localization on a static map.
- **Map server**: load/save/serve occupancy grids.
- **Waypoint follower**, **velocity smoother**, **collision monitor**, **docking** (opennav_docking).
- **lifecycle manager**: brings the whole stack up/down deterministically.

## MoveIt 2 (Manipulation)
- **move_group**: central node aggregating planning, kinematics, collision, execution.
- **Motion planners**: OMPL (sampling), Pilz industrial (LIN/PTP/CIRC), CHOMP, STOMP.
- **Planning Scene**: world & collision representation; **collision checking** (FCL).
- **Kinematics**: KDL, IKFast, bio_ik plugins.
- **Trajectory execution**: via ros2_control joint_trajectory_controller; time parameterization (TOTG).
- **MoveIt Setup Assistant**: generate SRDF, config, controllers from URDF.
- **Servo**: real-time Cartesian/joint jogging.
- **MoveGroupInterface / MoveItPy**: C++/Python programmatic API.
- **Perception**: octomap from depth sensors for collision avoidance.

## Perception & Sensing
- **image_pipeline**: rectification, debayer, stereo.
- **pointcloud_to_laserscan**, **laser_filters**, **depthimage_to_laserscan**.
- **robot_localization**: EKF/UKF sensor fusion (IMU + odom + GPS), navsat_transform.
- **slam_toolbox**: 2D SLAM (online/offline, lifelong mapping, serialization).
- **cartographer**: real-time 2D/3D SLAM (alt.).

## Other Notable Modules
- **diagnostics, rosbridge_suite** (web/JSON), **micro-ROS** (MCU/embedded).
- **ros2_controllers demos, gazebo_ros2_control / gz_ros2_control** (sim hardware).
- **teleop_twist_keyboard / joy** (manual driving), **twist_mux** (cmd_vel arbitration).
