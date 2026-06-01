# Gz Harmonic Reference — SW2GZ Plug-and-Play Target

> Scope: Gazebo Sim **Harmonic** (gz-sim 8.x), SDF **1.10**, ROS 2 **Jazzy**.
> Everything an auto-generated package must emit so `ros2 launch <pkg> gz_sim.launch.py` works
> without manual editing.
>
> Sources consulted:
> - https://gazebosim.org/docs/harmonic/sdf_worlds/
> - https://gazebosim.org/docs/harmonic/building_robot/
> - https://gazebosim.org/docs/harmonic/sensors/
> - https://gazebosim.org/docs/harmonic/ros2_integration/
> - https://gazebosim.org/docs/harmonic/ros2_spawn_model/
> - https://github.com/gazebosim/ros_gz/blob/jazzy/ros_gz_bridge/README.md
> - https://github.com/gazebosim/ros_gz_project_template (example bridge YAML)
> - https://raw.githubusercontent.com/gazebosim/gz-sim/gz-sim8/examples/worlds/camera_sensor.sdf
> - https://raw.githubusercontent.com/gazebosim/gz-sim/gz-sim8/examples/worlds/depth_camera_sensor.sdf
> - https://raw.githubusercontent.com/gazebosim/gz-sim/gz-sim8/examples/worlds/gpu_lidar_sensor.sdf
> - http://sdformat.org/spec?ver=1.10&elem=collision

---

## 1. SDF Model Essentials

### 1.1 Top-level structure

```xml
<?xml version="1.0"?>
<sdf version="1.10">
  <model name="my_robot" canonical_link="base_link">
    <pose relative_to="world">0 0 0 0 0 0</pose>
    <self_collide>false</self_collide>
    <static>false</static>
    <!-- links -->
    <!-- joints -->
  </model>
</sdf>
```

`canonical_link` defaults to the first link if omitted; setting it explicitly silences Gz warnings.
`self_collide` — `true` enables collision between links of the same model (default `false`).

### 1.2 Link

```xml
<link name="base_link">
  <!-- inertial (required for dynamic simulation) -->
  <inertial>
    <mass>2.0</mass>
    <inertia>
      <ixx>0.095</ixx>  <ixy>0</ixy>  <ixz>0</ixz>
      <iyy>0.381</iyy>  <iyz>0</iyz>
      <izz>0.476</izz>
    </inertia>
  </inertial>

  <!-- visual -->
  <visual name="visual">
    <geometry>
      <mesh><uri>model://my_robot/meshes/base_link.dae</uri></mesh>
    </geometry>
    <material>
      <ambient>0.2 0.2 0.8 1</ambient>
      <diffuse>0.2 0.2 0.8 1</diffuse>
      <specular>0.1 0.1 0.1 1</specular>
    </material>
  </visual>

  <!-- collision -->
  <collision name="collision">
    <geometry>
      <mesh><uri>model://my_robot/meshes/base_link_collision.stl</uri></mesh>
    </geometry>
    <surface>
      <friction>
        <ode>
          <mu>0.8</mu>     <!-- primary friction coefficient -->
          <mu2>0.8</mu2>   <!-- secondary friction coefficient -->
        </ode>
      </friction>
      <contact>
        <ode>
          <kp>1e6</kp>     <!-- contact stiffness -->
          <kd>1.0</kd>     <!-- contact damping -->
        </ode>
      </contact>
    </surface>
  </collision>
</link>
```

**Notes:**
- Gz uses the minimum `mu`/`mu2` of the two colliding surfaces.
- Default when omitted: `mu=mu2=1` (high friction).
- STL or DAE meshes both work; DAE preserves material colour.
- `<pose relative_to="parent_link">x y z roll pitch yaw</pose>` sets link origin.

### 1.3 Joint types

| SolidWorks mate       | SDF `type=`     | Notes |
|-----------------------|-----------------|-------|
| Fixed / Rigid         | `fixed`         | No axis needed |
| Revolute / Hinge      | `revolute`      | Requires `<axis>`, `<limit>` |
| Continuous / spinning | `continuous`    | No limits (infinite revolute) |
| Prismatic / slider    | `prismatic`     | Requires `<axis>`, `<limit>` |
| Ball socket           | `ball`          | 3-DOF, no axis |

```xml
<!-- revolute example -->
<joint name="wheel_joint" type="revolute">
  <pose relative_to="wheel_link"/>
  <parent>chassis</parent>
  <child>wheel_link</child>
  <axis>
    <xyz expressed_in="__model__">0 1 0</xyz>
    <limit>
      <lower>-1.79769e+308</lower>  <!-- -inf for continuous -->
      <upper>1.79769e+308</upper>
    </limit>
    <dynamics>
      <damping>0.1</damping>
      <friction>0.01</friction>
    </dynamics>
  </axis>
</joint>

<!-- prismatic example -->
<joint name="lift_joint" type="prismatic">
  <parent>base</parent>
  <child>carriage</child>
  <axis>
    <xyz expressed_in="__model__">0 0 1</xyz>
    <limit><lower>0.0</lower><upper>0.5</upper></limit>
  </axis>
</joint>
```

---

## 2. World File Essentials

### 2.1 Minimal world skeleton

```xml
<?xml version="1.0"?>
<sdf version="1.10">
  <world name="empty">

    <!-- Required system plugins (order matters for Harmonic) -->
    <plugin filename="gz-sim-physics-system"
            name="gz::sim::systems::Physics"/>
    <plugin filename="gz-sim-user-commands-system"
            name="gz::sim::systems::UserCommands"/>
    <plugin filename="gz-sim-scene-broadcaster-system"
            name="gz::sim::systems::SceneBroadcaster"/>
    <!-- Enables all camera/lidar/depth/rgbd sensors + rendering -->
    <plugin filename="gz-sim-sensors-system"
            name="gz::sim::systems::Sensors">
      <render_engine>ogre2</render_engine>
    </plugin>
    <!-- Enables IMU sensor -->
    <plugin filename="gz-sim-imu-system"
            name="gz::sim::systems::Imu"/>
    <!-- Enables Contact sensor -->
    <plugin filename="gz-sim-contact-system"
            name="gz::sim::systems::Contact"/>
    <!-- Enables NavSat/GPS sensor; also needs <spherical_coordinates> -->
    <plugin filename="gz-sim-navsat-system"
            name="gz::sim::systems::NavSat"/>
    <!-- Enables Force/Torque sensor -->
    <plugin filename="gz-sim-forcetorque-system"
            name="gz::sim::systems::ForceTorque"/>

    <!-- Physics -->
    <physics name="1ms" type="ignored">
      <max_step_size>0.001</max_step_size>
      <real_time_factor>1.0</real_time_factor>
    </physics>

    <!-- Gravity (default; explicit for clarity) -->
    <gravity>0 0 -9.8</gravity>

    <!-- Sun -->
    <light name="sun" type="directional">
      <cast_shadows>true</cast_shadows>
      <pose>0 0 10 0 0 0</pose>
      <diffuse>0.8 0.8 0.8 1</diffuse>
      <specular>0.2 0.2 0.2 1</specular>
      <direction>-0.5 0.1 -0.9</direction>
    </light>

    <!-- Ground plane -->
    <model name="ground_plane">
      <static>true</static>
      <link name="link">
        <collision name="collision">
          <geometry><plane><normal>0 0 1</normal><size>100 100</size></plane></geometry>
        </collision>
        <visual name="visual">
          <geometry><plane><normal>0 0 1</normal><size>100 100</size></plane></geometry>
          <material>
            <ambient>0.8 0.8 0.8 1</ambient>
            <diffuse>0.8 0.8 0.8 1</diffuse>
          </material>
        </visual>
      </link>
    </model>

    <!-- Optional: GPS/NavSat reference coordinates -->
    <spherical_coordinates>
      <surface_model>EARTH_WGS84</surface_model>
      <latitude_deg>37.7749</latitude_deg>
      <longitude_deg>-122.4194</longitude_deg>
      <elevation>0.0</elevation>
      <heading_deg>0</heading_deg>
    </spherical_coordinates>

    <!-- Robot model will be spawned at runtime via ros_gz_sim create -->
  </world>
</sdf>
```

**Plugin filename convention for Harmonic:**
Use `gz-sim-*-system` (unversioned). Garden/Ionic used `gz-sim8-*-system` (versioned). Wrong filenames silently fail to load.

---

## 3. Sensors — SDF Blocks

All sensors are children of `<link>`. The world must have the matching system plugin loaded.

### 3.1 IMU

World plugin: `gz-sim-imu-system`

```xml
<sensor name="imu_sensor" type="imu">
  <always_on>1</always_on>
  <update_rate>100</update_rate>
  <visualize>true</visualize>
  <topic>imu</topic>
  <gz_frame_id>imu_link</gz_frame_id>
  <imu>
    <angular_velocity>
      <x><noise type="gaussian"><mean>0</mean><stddev>0.0002</stddev></noise></x>
      <y><noise type="gaussian"><mean>0</mean><stddev>0.0002</stddev></noise></y>
      <z><noise type="gaussian"><mean>0</mean><stddev>0.0002</stddev></noise></z>
    </angular_velocity>
    <linear_acceleration>
      <x><noise type="gaussian"><mean>0</mean><stddev>0.017</stddev></noise></x>
      <y><noise type="gaussian"><mean>0</mean><stddev>0.017</stddev></noise></y>
      <z><noise type="gaussian"><mean>0</mean><stddev>0.017</stddev></noise></z>
    </linear_acceleration>
  </imu>
</sensor>
```

Publishes: `gz.msgs.IMU` on `<topic>` → bridge to `sensor_msgs/msg/Imu`.

### 3.2 GPU Lidar — 2D

World plugin: `gz-sim-sensors-system` (with `<render_engine>ogre2</render_engine>`)

```xml
<sensor name="gpu_lidar_2d" type="gpu_lidar">
  <pose relative_to="lidar_frame">0 0 0 0 0 0</pose>
  <topic>scan</topic>
  <update_rate>10</update_rate>
  <always_on>1</always_on>
  <visualize>true</visualize>
  <ray>
    <scan>
      <horizontal>
        <samples>720</samples>
        <resolution>1</resolution>
        <min_angle>-3.14159</min_angle>
        <max_angle>3.14159</max_angle>
      </horizontal>
      <vertical>
        <samples>1</samples>
        <resolution>0.01</resolution>
        <min_angle>0</min_angle>
        <max_angle>0</max_angle>
      </vertical>
    </scan>
    <range>
      <min>0.08</min>
      <max>10.0</max>
      <resolution>0.01</resolution>
    </range>
    <noise><type>gaussian</type><mean>0.0</mean><stddev>0.01</stddev></noise>
  </ray>
</sensor>
```

Publishes: `gz.msgs.LaserScan` → bridge to `sensor_msgs/msg/LaserScan`.

### 3.3 GPU Lidar — 3D (Velodyne-style)

Same plugin. Add vertical samples:

```xml
<sensor name="gpu_lidar_3d" type="gpu_lidar">
  <topic>lidar/points</topic>
  <update_rate>10</update_rate>
  <always_on>1</always_on>
  <ray>
    <scan>
      <horizontal>
        <samples>1800</samples><resolution>1</resolution>
        <min_angle>-3.14159</min_angle><max_angle>3.14159</max_angle>
      </horizontal>
      <vertical>
        <samples>16</samples><resolution>1</resolution>
        <min_angle>-0.261799</min_angle><max_angle>0.261799</max_angle>
      </vertical>
    </scan>
    <range><min>0.08</min><max>100.0</max><resolution>0.01</resolution></range>
    <noise><type>gaussian</type><mean>0.0</mean><stddev>0.01</stddev></noise>
  </ray>
</sensor>
```

Publishes: `gz.msgs.LaserScan` (with points) → bridge to `sensor_msgs/msg/PointCloud2` using `gz.msgs.PointCloudPacked`.

### 3.4 Camera (RGB)

World plugin: `gz-sim-sensors-system`

```xml
<sensor name="camera" type="camera">
  <pose relative_to="camera_frame">0 0 0 0 0 0</pose>
  <always_on>1</always_on>
  <update_rate>30</update_rate>
  <visualize>true</visualize>
  <topic>camera/image_raw</topic>
  <gz_frame_id>camera_optical_frame</gz_frame_id>
  <camera name="camera">
    <horizontal_fov>1.047</horizontal_fov>
    <image>
      <width>640</width>
      <height>480</height>
      <format>R8G8B8</format>
    </image>
    <clip><near>0.1</near><far>100.0</far></clip>
    <camera_info_topic>camera/camera_info</camera_info_topic>
    <noise><type>gaussian</type><mean>0.0</mean><stddev>0.007</stddev></noise>
  </camera>
</sensor>
```

Publishes:
- `gz.msgs.Image` on `camera/image_raw` → `sensor_msgs/msg/Image`
- `gz.msgs.CameraInfo` on `camera/camera_info` → `sensor_msgs/msg/CameraInfo`

### 3.5 Depth Camera

World plugin: `gz-sim-sensors-system`

```xml
<sensor name="depth_camera" type="depth_camera">
  <pose relative_to="depth_camera_frame">0 0 0 0 0 0</pose>
  <always_on>1</always_on>
  <update_rate>30</update_rate>
  <topic>depth_camera</topic>
  <gz_frame_id>depth_camera_optical_frame</gz_frame_id>
  <camera name="depth_camera">
    <horizontal_fov>1.05</horizontal_fov>
    <image>
      <width>640</width>
      <height>480</height>
      <format>R_FLOAT32</format>  <!-- depth format -->
    </image>
    <clip><near>0.1</near><far>10.0</far></clip>
    <camera_info_topic>depth_camera/camera_info</camera_info_topic>
  </camera>
</sensor>
```

Publishes: `gz.msgs.Image` (float32 depth) → `sensor_msgs/msg/Image`.

### 3.6 RGBD Camera

World plugin: `gz-sim-sensors-system`

```xml
<sensor name="rgbd_camera" type="rgbd_camera">
  <pose relative_to="rgbd_camera_frame">0 0 0 0 0 0</pose>
  <always_on>1</always_on>
  <update_rate>30</update_rate>
  <topic>rgbd_camera</topic>
  <gz_frame_id>rgbd_camera_optical_frame</gz_frame_id>
  <camera name="rgbd_camera">
    <horizontal_fov>1.047</horizontal_fov>
    <image>
      <width>640</width>
      <height>480</height>
      <format>R8G8B8</format>
    </image>
    <clip><near>0.1</near><far>10.0</far></clip>
    <!-- Depth clip (separate from image clip) -->
    <depth_camera>
      <clip><near>0.1</near><far>10.0</far></clip>
    </depth_camera>
    <camera_info_topic>rgbd_camera/camera_info</camera_info_topic>
    <!-- Optional intrinsics for calibrated camera -->
    <!--
    <lens>
      <intrinsics>
        <fx>343.159</fx><fy>343.159</fy>
        <cx>319.5</cx><cy>179.5</cy>
        <s>0</s>
      </intrinsics>
    </lens>
    -->
  </camera>
</sensor>
```

Publishes:
- `gz.msgs.Image` (RGB) on `rgbd_camera/image`
- `gz.msgs.Image` (float32 depth) on `rgbd_camera/depth_image`
- `gz.msgs.CameraInfo` on `rgbd_camera/camera_info`
- `gz.msgs.PointCloudPacked` on `rgbd_camera/points`

### 3.7 Contact Sensor

World plugin: `gz-sim-contact-system`

```xml
<sensor name="contact_sensor" type="contact">
  <always_on>1</always_on>
  <update_rate>100</update_rate>
  <topic>/contact_example</topic>
  <contact>
    <collision>collision</collision>  <!-- name of the <collision> on this link -->
  </contact>
</sensor>
```

Publishes: `gz.msgs.Contacts` on topic. No standard ros_gz_bridge mapping; use `ros_gz_interfaces/msg/Contacts`.

### 3.8 Force/Torque Sensor

World plugin: `gz-sim-forcetorque-system`

Placed on a **joint** (not a link):

```xml
<joint name="ft_joint" type="fixed">
  <parent>arm_link</parent>
  <child>wrist_link</child>
  <sensor name="force_torque" type="force_torque">
    <always_on>1</always_on>
    <update_rate>100</update_rate>
    <topic>ft_sensor</topic>
    <gz_frame_id>wrist_link</gz_frame_id>
    <force_torque>
      <frame>child</frame>
      <measure_direction>child_to_parent</measure_direction>
    </force_torque>
  </sensor>
</joint>
```

Publishes: `gz.msgs.Wrench` → bridge to `geometry_msgs/msg/Wrench`.

### 3.9 NavSat / GPS

World plugin: `gz-sim-navsat-system`
World must also include `<spherical_coordinates>` (see Section 2.1).

```xml
<sensor name="navsat_sensor" type="navsat">
  <always_on>1</always_on>
  <update_rate>1</update_rate>
  <topic>navsat</topic>
  <gz_frame_id>navsat_link</gz_frame_id>
</sensor>
```

Publishes: `gz.msgs.NavSat` → bridge to `sensor_msgs/msg/NavSatFix`.

---

## 4. System Plugin Summary Table

| Sensor type           | World `<plugin filename=...>`          | `name=` class                        |
|-----------------------|----------------------------------------|--------------------------------------|
| Camera, Depth, RGBD, Lidar | `gz-sim-sensors-system`           | `gz::sim::systems::Sensors`          |
| IMU                   | `gz-sim-imu-system`                    | `gz::sim::systems::Imu`              |
| Contact               | `gz-sim-contact-system`                | `gz::sim::systems::Contact`          |
| Force/Torque          | `gz-sim-forcetorque-system`            | `gz::sim::systems::ForceTorque`      |
| NavSat/GPS            | `gz-sim-navsat-system`                 | `gz::sim::systems::NavSat`           |
| Thermal Camera        | `gz-sim-thermal-sensor-system`         | `gz::sim::systems::ThermalSensor`    |
| Air Pressure          | `gz-sim-air-pressure-system`           | `gz::sim::systems::AirPressure`      |
| Magnetometer          | `gz-sim-magnetometer-system`           | `gz::sim::systems::Magnetometer`     |
| (always required)     | `gz-sim-physics-system`                | `gz::sim::systems::Physics`          |
| (always required)     | `gz-sim-user-commands-system`          | `gz::sim::systems::UserCommands`     |
| (always required)     | `gz-sim-scene-broadcaster-system`      | `gz::sim::systems::SceneBroadcaster` |

---

## 5. Spawning — ros_gz_sim create

### 5.1 From robot_description topic (standard launch pattern)

```python
# in gz_sim.launch.py
spawn = Node(
    package='ros_gz_sim',
    executable='create',
    arguments=[
        '-topic', 'robot_description',   # reads from /robot_description topic
        '-name',  package_name,           # MUST match model name in SDF
        '-x', '0.0', '-y', '0.0', '-z', '0.05',
        '-R', '0.0', '-P', '0.0', '-Y', '0.0',
    ],
    output='screen'
)
```

### 5.2 From SDF file directly

```python
spawn = Node(
    package='ros_gz_sim',
    executable='create',
    arguments=[
        '-file', sdf_path,
        '-name', 'my_robot',
        '-x', '0.0', '-y', '0.0', '-z', '0.05',
    ],
    output='screen'
)
```

### 5.3 Using gz_spawn_model.launch.py (Jazzy+)

```python
gz_spawn = IncludeLaunchDescription(
    PythonLaunchDescriptionSource(os.path.join(
        get_package_share_directory('ros_gz_sim'), 'launch', 'gz_spawn_model.launch.py')),
    launch_arguments={
        'world':       'empty',
        'file':        sdf_path,
        'entity_name': 'my_robot',
        'x': '0.0', 'y': '0.0', 'z': '0.05',
        'R': '0.0',  'P': '0.0', 'Y': '0.0',
    }.items()
)
```

**Key constraint:** The `-name` / `entity_name` value must exactly match the `<model name="...">` in the SDF and the model-name fragment used in the `joint_state` gz topic path:
`/world/<worldname>/model/<name>/joint_state`.

---

## 6. ros_gz_bridge — YAML Config

### 6.1 YAML schema (all fields)

```yaml
- ros_topic_name:   "/ros_side_topic"   # required
  gz_topic_name:    "/gz_side_topic"    # required
  ros_type_name:    "pkg/msg/Type"      # required
  gz_type_name:     "gz.msgs.TypeName"  # required
  direction:        GZ_TO_ROS          # GZ_TO_ROS | ROS_TO_GZ | BIDIRECTIONAL
  subscriber_queue: 10                  # optional
  publisher_queue:  10                  # optional
  lazy:             false               # optional; defer subscription until ROS subscriber appears
  qos_profile:      SENSOR_DATA        # optional: DEFAULT | SENSOR_DATA | CLOCK
  frame_id:         "base_link"        # optional; overrides header.frame_id
```

### 6.2 Complete skeleton for a diff-drive robot with sensors

```yaml
# ---- Infrastructure ----
- ros_topic_name: "/clock"
  gz_topic_name:  "/clock"
  ros_type_name:  "rosgraph_msgs/msg/Clock"
  gz_type_name:   "gz.msgs.Clock"
  direction:      GZ_TO_ROS
  qos_profile:    CLOCK

# ---- Robot state ----
- ros_topic_name: "/joint_states"
  gz_topic_name:  "/world/empty/model/<ROBOT_NAME>/joint_state"
  ros_type_name:  "sensor_msgs/msg/JointState"
  gz_type_name:   "gz.msgs.Model"
  direction:      GZ_TO_ROS

- ros_topic_name: "/tf"
  gz_topic_name:  "/model/<ROBOT_NAME>/pose"
  ros_type_name:  "tf2_msgs/msg/TFMessage"
  gz_type_name:   "gz.msgs.Pose_V"
  direction:      GZ_TO_ROS

- ros_topic_name: "/tf_static"
  gz_topic_name:  "/model/<ROBOT_NAME>/pose_static"
  ros_type_name:  "tf2_msgs/msg/TFMessage"
  gz_type_name:   "gz.msgs.Pose_V"
  direction:      GZ_TO_ROS

# ---- Motion commands ----
- ros_topic_name: "/cmd_vel"
  gz_topic_name:  "/model/<ROBOT_NAME>/cmd_vel"
  ros_type_name:  "geometry_msgs/msg/Twist"
  gz_type_name:   "gz.msgs.Twist"
  direction:      ROS_TO_GZ

# ---- Odometry ----
- ros_topic_name: "/odom"
  gz_topic_name:  "/model/<ROBOT_NAME>/odometry"
  ros_type_name:  "nav_msgs/msg/Odometry"
  gz_type_name:   "gz.msgs.Odometry"
  direction:      GZ_TO_ROS

# ---- 2D Lidar ----
- ros_topic_name: "/scan"
  gz_topic_name:  "/scan"
  ros_type_name:  "sensor_msgs/msg/LaserScan"
  gz_type_name:   "gz.msgs.LaserScan"
  direction:      GZ_TO_ROS
  qos_profile:    SENSOR_DATA

# ---- 3D Lidar (PointCloud) ----
- ros_topic_name: "/lidar/points"
  gz_topic_name:  "/lidar/points"
  ros_type_name:  "sensor_msgs/msg/PointCloud2"
  gz_type_name:   "gz.msgs.PointCloudPacked"
  direction:      GZ_TO_ROS
  qos_profile:    SENSOR_DATA

# ---- RGB Camera ----
- ros_topic_name: "/camera/image_raw"
  gz_topic_name:  "/camera/image_raw"
  ros_type_name:  "sensor_msgs/msg/Image"
  gz_type_name:   "gz.msgs.Image"
  direction:      GZ_TO_ROS
  qos_profile:    SENSOR_DATA

- ros_topic_name: "/camera/camera_info"
  gz_topic_name:  "/camera/camera_info"
  ros_type_name:  "sensor_msgs/msg/CameraInfo"
  gz_type_name:   "gz.msgs.CameraInfo"
  direction:      GZ_TO_ROS
  qos_profile:    SENSOR_DATA

# ---- Depth Camera ----
- ros_topic_name: "/depth_camera/image"
  gz_topic_name:  "/depth_camera"
  ros_type_name:  "sensor_msgs/msg/Image"
  gz_type_name:   "gz.msgs.Image"
  direction:      GZ_TO_ROS
  qos_profile:    SENSOR_DATA

# ---- IMU ----
- ros_topic_name: "/imu"
  gz_topic_name:  "/imu"
  ros_type_name:  "sensor_msgs/msg/Imu"
  gz_type_name:   "gz.msgs.IMU"
  direction:      GZ_TO_ROS
  qos_profile:    SENSOR_DATA

# ---- NavSat / GPS ----
- ros_topic_name: "/navsat"
  gz_topic_name:  "/navsat"
  ros_type_name:  "sensor_msgs/msg/NavSatFix"
  gz_type_name:   "gz.msgs.NavSat"
  direction:      GZ_TO_ROS
  qos_profile:    SENSOR_DATA

# ---- Force/Torque ----
- ros_topic_name: "/ft_sensor"
  gz_topic_name:  "/ft_sensor"
  ros_type_name:  "geometry_msgs/msg/Wrench"
  gz_type_name:   "gz.msgs.Wrench"
  direction:      GZ_TO_ROS
  qos_profile:    SENSOR_DATA
```

### 6.3 Command-line @ syntax (for debugging / one-off bridging)

```bash
# direction symbols:  [ = GZ_TO_ROS,  ] = ROS_TO_GZ,  @ = BIDIRECTIONAL
ros2 run ros_gz_bridge parameter_bridge \
  /clock@rosgraph_msgs/msg/Clock[gz.msgs.Clock \
  /scan@sensor_msgs/msg/LaserScan[gz.msgs.LaserScan \
  /camera/image_raw@sensor_msgs/msg/Image[gz.msgs.Image \
  /imu@sensor_msgs/msg/Imu[gz.msgs.IMU \
  /cmd_vel@geometry_msgs/msg/Twist]gz.msgs.Twist \
  /tf@tf2_msgs/msg/TFMessage[gz.msgs.Pose_V \
  /joint_states@sensor_msgs/msg/JointState[gz.msgs.Model
```

---

## 7. Type Mapping Quick Reference

| ROS 2 type                          | Gz Harmonic type              | Typical direction |
|-------------------------------------|-------------------------------|-------------------|
| `rosgraph_msgs/msg/Clock`           | `gz.msgs.Clock`               | GZ→ROS            |
| `sensor_msgs/msg/JointState`        | `gz.msgs.Model`               | GZ→ROS            |
| `tf2_msgs/msg/TFMessage`            | `gz.msgs.Pose_V`              | GZ→ROS            |
| `geometry_msgs/msg/Twist`           | `gz.msgs.Twist`               | ROS→GZ            |
| `nav_msgs/msg/Odometry`             | `gz.msgs.Odometry`            | GZ→ROS            |
| `sensor_msgs/msg/LaserScan`         | `gz.msgs.LaserScan`           | GZ→ROS            |
| `sensor_msgs/msg/PointCloud2`       | `gz.msgs.PointCloudPacked`    | GZ→ROS            |
| `sensor_msgs/msg/Image`             | `gz.msgs.Image`               | GZ→ROS            |
| `sensor_msgs/msg/CameraInfo`        | `gz.msgs.CameraInfo`          | GZ→ROS            |
| `sensor_msgs/msg/Imu`               | `gz.msgs.IMU`                 | GZ→ROS            |
| `sensor_msgs/msg/NavSatFix`         | `gz.msgs.NavSat`              | GZ→ROS            |
| `geometry_msgs/msg/Wrench`          | `gz.msgs.Wrench`              | GZ→ROS            |
| `sensor_msgs/msg/FluidPressure`     | `gz.msgs.FluidPressure`       | GZ→ROS            |
| `sensor_msgs/msg/MagneticField`     | `gz.msgs.Magnetometer`        | GZ→ROS            |
| `std_msgs/msg/Float64`              | `gz.msgs.Double`              | BIDIRECTIONAL     |
| `geometry_msgs/msg/Pose`            | `gz.msgs.Pose`                | BIDIRECTIONAL     |

---

## 8. gz_ros2_control Integration

For motor-controlled joints, the robot also needs the `gz_ros2_control` plugin in its URDF/xacro:

```xml
<!-- in robot.urdf.xacro or gz.xacro -->
<gazebo>
  <plugin filename="gz_ros2_control-system"
          name="gz_ros2_control::GazeboSimROS2ControlPlugin">
    <parameters>$(find my_robot_pkg)/config/controllers.yaml</parameters>
  </plugin>
</gazebo>
```

The `GZ_SIM_SYSTEM_PLUGIN_PATH` env var must point to the `gz_ros2_control` lib directory:

```python
SetEnvironmentVariable(
    name='GZ_SIM_SYSTEM_PLUGIN_PATH',
    value=os.path.join(get_package_prefix('gz_ros2_control'), 'lib'))
```

---

## 9. SW2GZ: Features to Emit vs Current Status

### 9.1 Features the exporter MUST emit for plug-and-play Gz Harmonic

**World SDF (worlds/\*.sdf):**
- [x] SDF version 1.10 declaration
- [x] `gz-sim-physics-system` plugin
- [x] `gz-sim-user-commands-system` plugin
- [x] `gz-sim-scene-broadcaster-system` plugin
- [x] `gz-sim-sensors-system` plugin (with `<render_engine>ogre2</render_engine>`)
- [x] `gz-sim-imu-system` plugin
- [ ] `gz-sim-contact-system` plugin (when contact sensors selected)
- [ ] `gz-sim-navsat-system` plugin + `<spherical_coordinates>` (when GPS selected)
- [ ] `gz-sim-forcetorque-system` plugin (when FT sensors selected)
- [x] `<physics>` block (max_step_size, real_time_factor)
- [x] `<gravity>` (implicit default present)
- [x] `<light>` (sun directional)
- [x] `<ground_plane>` model
- [ ] `<gravity>` explicit element (currently implicit only)

**Model SDF (model.sdf):**
- [x] `<model name="...">` with SDF 1.10
- [x] `<link name="...">` stubs (empty links — no inertial/visual/collision)
- [x] `<joint>` with type, parent, child
- [ ] `<inertial>` with `<mass>` and `<inertia>` matrix (currently MISSING — links are empty stubs)
- [ ] `<visual>` with `<geometry><mesh>` URI pointing to exported STL/DAE (MISSING)
- [ ] `<visual><material>` with ambient/diffuse/specular RGBA from SW appearance (MISSING)
- [ ] `<collision>` with `<geometry>` (MISSING — no collision shapes)
- [ ] `<collision><surface><friction><ode>` with `<mu>` / `<mu2>` (MISSING)
- [ ] `<collision><surface><contact><ode>` with `<kp>` / `<kd>` (MISSING)
- [ ] `<self_collide>` flag on model (MISSING)
- [ ] `<static>` flag on model (MISSING)
- [ ] `<link><pose relative_to="...">` for correct spatial positioning (MISSING)
- [ ] `<joint><axis><xyz>` and `<limit>` for revolute/prismatic joints (currently only type/parent/child emitted)
- [ ] `<joint><axis><dynamics>` (damping, friction) (MISSING)
- [ ] Sensor `<sensor>` blocks inside links (entire sensor system MISSING)

**ros_gz_bridge YAML (config/ros_gz_bridge.yaml):**
- [x] `/clock` bridge (GZ_TO_ROS)
- [x] `/joint_states` bridge (GZ_TO_ROS)
- [x] `/tf` bridge (GZ_TO_ROS)
- [ ] `/tf_static` bridge (MISSING)
- [ ] `/cmd_vel` bridge (ROS_TO_GZ) (MISSING)
- [ ] `/odom` bridge (GZ_TO_ROS) (MISSING)
- [ ] Per-sensor bridge entries (scan, image, camera_info, imu, navsat, etc.) (MISSING)

**Launch files (launch/gz_sim.launch.py):**
- [x] `GZ_SIM_SYSTEM_PLUGIN_PATH` env var
- [x] `GZ_SIM_RESOURCE_PATH` env var
- [x] `gz_sim.launch.py` include with world file argument
- [x] `robot_state_publisher` node with `use_sim_time: True`
- [x] `ros_gz_sim create` spawn node (from robot_description topic, -name matching model)
- [x] `parameter_bridge` node with YAML config
- [ ] `joint_state_broadcaster` / `joint_trajectory_controller` spawner coordination (in separate launch but not auto-linked to sensor-equipped robots)

**Model config (model.config):**
- [x] `<model>` with name, version, sdf reference

### 9.2 MISSING vs current exporter (gap analysis)

The current exporter (`SdfModelWriter.cs`) emits:
```xml
<model name="X">
  <link name="L1"/>
  <link name="L2"/>
  <joint name="J" type="fixed"><parent>L1</parent><child>L2</child></joint>
</model>
```

Missing entirely from this output:
1. **Inertial data** — simulation will treat all links as massless/infinitely rigid; physics will be wrong or Gz will reject the model.
2. **Visual geometry** — robot is invisible in Gz scene.
3. **Material colours** — no appearance even if geometry added.
4. **Collision geometry** — no collision response; robot falls through ground.
5. **Surface friction** — floor/wheel contact will be incorrect.
6. **Joint axis + limits** — revolute/prismatic joints have no motion axis defined; they will default to Z-axis or refuse to simulate.
7. **Link poses** — all links pile up at origin; robot has no correct spatial structure.
8. **Sensors** — no sensor `<sensor>` blocks; no sensor data published.
9. **`<self_collide>`, `<static>`** flags.
10. **Bridge entries** for tf_static, cmd_vel, odom, and all sensor topics.
11. **World-level plugins** for contact, FT, navsat (conditionally needed).
12. **`<spherical_coordinates>`** block for GPS use cases.

---

## 10. Source Links

| Resource | URL |
|----------|-----|
| Gz Harmonic SDF worlds tutorial | https://gazebosim.org/docs/harmonic/sdf_worlds/ |
| Gz Harmonic building_robot tutorial | https://gazebosim.org/docs/harmonic/building_robot/ |
| Gz Harmonic sensors tutorial | https://gazebosim.org/docs/harmonic/sensors/ |
| Gz Harmonic ROS 2 integration | https://gazebosim.org/docs/harmonic/ros2_integration/ |
| Gz Harmonic spawn model (Jazzy) | https://gazebosim.org/docs/harmonic/ros2_spawn_model/ |
| ros_gz_bridge README (jazzy branch) | https://github.com/gazebosim/ros_gz/blob/jazzy/ros_gz_bridge/README.md |
| ros_gz_project_template bridge YAML | https://github.com/gazebosim/ros_gz_project_template/blob/main/ros_gz_example_bringup/config/ros_gz_example_bridge.yaml |
| gz-sim8 camera_sensor.sdf example | https://raw.githubusercontent.com/gazebosim/gz-sim/gz-sim8/examples/worlds/camera_sensor.sdf |
| gz-sim8 depth_camera_sensor.sdf | https://raw.githubusercontent.com/gazebosim/gz-sim/gz-sim8/examples/worlds/depth_camera_sensor.sdf |
| gz-sim8 gpu_lidar_sensor.sdf | https://raw.githubusercontent.com/gazebosim/gz-sim/gz-sim8/examples/worlds/gpu_lidar_sensor.sdf |
| SDFormat 1.10 collision spec | http://sdformat.org/spec?ver=1.10&elem=collision |
| MOGI-ROS Gazebo Harmonic sensors repo | https://github.com/MOGI-ROS/Week-5-6-Gazebo-sensors |
