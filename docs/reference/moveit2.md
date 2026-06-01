# MoveIt 2 (ROS 2 Jazzy) — Exporter Reference

**Purpose:** Concrete specification of what SW2GZ must produce so an exported manipulator
is plug-and-play with MoveIt 2 — ideally without running the Setup Assistant by hand.

Sources consulted:
- https://moveit.picknik.ai/main/doc/examples/urdf_srdf/urdf_srdf_tutorial.html
- https://moveit.picknik.ai/main/doc/examples/setup_assistant/setup_assistant_tutorial.html
- https://moveit.picknik.ai/main/doc/how_to_guides/moveit_configuration/moveit_configuration_tutorial.html
- https://moveit.picknik.ai/humble/doc/examples/controller_configuration/controller_configuration_tutorial.html
- https://github.com/moveit/moveit_resources/blob/ros2/panda_moveit_config/config/panda.srdf
- https://control.ros.org/rolling/doc/ros2_control/hardware_interface/doc/mock_components_userdoc.html
- https://automaticaddison.com/configure-moveit-2-for-a-simulated-robot-arm-ros-2-jazzy/

---

## 1. What MoveIt Setup Assistant Consumes and Produces

### Input required from the URDF

| URDF element | What MoveIt reads |
|---|---|
| `<link>` elements | Collision geometry, visual geometry, inertia |
| `<joint>` elements | Type (`revolute`/`prismatic`/`fixed`), parent/child links |
| `<limit>` on each joint | `lower`, `upper` (position), `velocity`, `effort` |
| `<ros2_control>` block | Hardware plugin, command/state interfaces per joint |

The Setup Assistant is launched with:
```
ros2 run moveit_setup_assistant moveit_setup_assistant \
  --urdf <path_to_urdf_or_xacro> \
  --srdf <path_to_existing_srdf>   # optional, for incremental edits
```

### Output package generated (14-step wizard)

```
<robot>_moveit_config/
├── config/
│   ├── <robot>.srdf                  # planning groups, collision matrix, poses
│   ├── joint_limits.yaml             # per-joint vel/acc/jerk overrides
│   ├── kinematics.yaml               # IK solver plugin + params per group
│   ├── moveit_controllers.yaml       # MoveIt → ros2_control bridge
│   ├── ros2_controllers.yaml         # ros2_control controller definitions
│   ├── ompl_planning.yaml            # OMPL planner settings
│   ├── pilz_industrial_motion_planner_planning.yaml
│   ├── sensors_3d.yaml               # optional 3D perception
│   └── moveit_cpp.yaml               # optional MoveItCpp API params
├── launch/
│   ├── demo.launch.py                # single-command demo (RViz + move_group)
│   ├── move_group.launch.py
│   ├── moveit_rviz.launch.py
│   ├── rsp.launch.py                 # robot_state_publisher
│   ├── spawn_controllers.launch.py
│   ├── static_virtual_joint_tfs.launch.py
│   └── warehouse_db.launch.py
├── .setup_assistant                  # wizard state (re-open later)
├── CMakeLists.txt
└── package.xml
```

---

## 2. SRDF — Complete Structure

The SRDF is the key file MoveIt adds on top of the URDF. It lives at
`config/<robot>.srdf` and is loaded alongside the URDF at runtime.

### 2.1 Skeleton with all element types

```xml
<?xml version="1.0"?>
<robot name="my_arm">

  <!-- ── Virtual joint: anchors robot base to world ──────────────────── -->
  <!-- type="fixed" for a bolted-down industrial manipulator             -->
  <!-- type="floating" / type="planar" for mobile bases                 -->
  <virtual_joint
      name="virtual_joint"
      type="fixed"
      parent_frame="world"
      child_link="base_link"/>

  <!-- ── Planning group: serial chain ────────────────────────────────── -->
  <!-- base_link = first URDF link in chain (robot base)                -->
  <!-- tip_link  = child link of last active joint (e.g. flange)        -->
  <group name="arm">
    <chain base_link="base_link" tip_link="link6"/>
  </group>

  <!-- Alternative: explicit joint list (use when chain is non-serial)  -->
  <group name="arm_joints">
    <joint name="joint1"/>
    <joint name="joint2"/>
    <joint name="joint3"/>
    <joint name="joint4"/>
    <joint name="joint5"/>
    <joint name="joint6"/>
  </group>

  <!-- Composite group: arm + gripper together                          -->
  <group name="arm_with_gripper">
    <group name="arm"/>
    <group name="gripper"/>
  </group>

  <!-- Gripper group (link list or chain)                               -->
  <group name="gripper">
    <link  name="gripper_base"/>
    <link  name="finger_left"/>
    <link  name="finger_right"/>
    <joint name="finger_joint"/>
  </group>

  <!-- ── Named robot poses (group_state) ─────────────────────────────── -->
  <!-- Each joint value is in radians (revolute) or metres (prismatic)  -->
  <group_state name="home" group="arm">
    <joint name="joint1" value="0"/>
    <joint name="joint2" value="0"/>
    <joint name="joint3" value="0"/>
    <joint name="joint4" value="0"/>
    <joint name="joint5" value="0"/>
    <joint name="joint6" value="0"/>
  </group_state>

  <group_state name="ready" group="arm">
    <joint name="joint1" value="0"/>
    <joint name="joint2" value="-0.785"/>
    <joint name="joint3" value="0"/>
    <joint name="joint4" value="-2.356"/>
    <joint name="joint5" value="0"/>
    <joint name="joint6" value="1.571"/>
  </group_state>

  <!-- ── End effector ─────────────────────────────────────────────────── -->
  <!-- parent_group = planning group that moves the arm                 -->
  <!-- parent_link  = tip of the arm chain (attachment point)           -->
  <!-- group        = the gripper planning group                        -->
  <end_effector
      name="hand"
      parent_group="arm"
      parent_link="link6"
      group="gripper"/>

  <!-- ── Passive joints (unactuated; excluded from planning) ─────────── -->
  <passive_joint name="passive_caster_wheel"/>

  <!-- ── Self-collision disable pairs ────────────────────────────────── -->
  <!-- reason values:                                                    -->
  <!--   "Adjacent"  – directly connected by a joint (always skipped)  -->
  <!--   "Never"     – geometry never overlaps in any configuration     -->
  <!--   "Default"   – colliding in the default (zero) pose             -->
  <!--   "Always"    – always colliding; checked at runtime             -->
  <disable_collisions link1="base_link"  link2="link1"  reason="Adjacent"/>
  <disable_collisions link1="link1"      link2="link2"  reason="Adjacent"/>
  <disable_collisions link1="link2"      link2="link3"  reason="Adjacent"/>
  <disable_collisions link1="link3"      link2="link4"  reason="Adjacent"/>
  <disable_collisions link1="link4"      link2="link5"  reason="Adjacent"/>
  <disable_collisions link1="link5"      link2="link6"  reason="Adjacent"/>
  <!-- Non-adjacent pairs that geometry analysis proves never collide:  -->
  <disable_collisions link1="base_link"  link2="link3"  reason="Never"/>
  <disable_collisions link1="link1"      link2="link4"  reason="Never"/>
  <!-- ... (Setup Assistant generates ~20-60 pairs via random sampling) -->

</robot>
```

### 2.2 Group definition methods

| Method | SRDF element | When to use |
|---|---|---|
| Serial chain | `<chain base_link="…" tip_link="…"/>` | Standard serial arm |
| Joint list | `<joint name="…"/>` repeated | Non-serial, selected joints only |
| Link list | `<link name="…"/>` repeated | Gripper/hand (includes child links) |
| Sub-groups | `<group name="…"/>` nested | Composite arm+gripper group |

### 2.3 Collision matrix derivation rules

The Setup Assistant samples the joint space (default 10 000 trials) and produces
`disable_collisions` pairs by four criteria:

1. **Adjacent** — links connected directly by a URDF joint (auto-derivable from URDF graph)
2. **Never** — never found in collision across all sampled poses (statistical)
3. **Default** — colliding in the all-zeros default pose (geometry artifact)
4. **Always** — always colliding (geometry error or intentional)

For a SW2GZ export the minimum safe set is: emit **Adjacent** pairs for every
parent→child link pair in the kinematic chain. The Never/Default pairs require
the sampling pass that only the Setup Assistant or offline collision checking performs.

---

## 3. Configuration Files — Full Schemas

### 3.1 `kinematics.yaml`

One entry per planning group. Key must exactly match the group name in the SRDF.

```yaml
arm:
  kinematics_solver: kdl_kinematics_plugin/KDLKinematicsPlugin
  kinematics_solver_search_resolution: 0.005
  kinematics_solver_timeout: 0.05       # seconds; increase for complex robots
  kinematics_solver_attempts: 3
  position_only_ik: false               # true = ignore orientation in IK

gripper:
  kinematics_solver: kdl_kinematics_plugin/KDLKinematicsPlugin
  kinematics_solver_search_resolution: 0.005
  kinematics_solver_timeout: 0.05
  kinematics_solver_attempts: 3
```

Alternative solvers (drop-in replacements):
- `trac_ik_kinematics_plugin/TRAC_IKKinematicsPlugin` — faster, more reliable
- `bio_ik/BioIKKinematicsPlugin` — handles redundant arms well
- `pick_ik/PickIKPlugin` — recommended for Jazzy/Rolling

### 3.2 `joint_limits.yaml`

Overrides URDF `<limit>` values specifically for MoveIt planning. The URDF limits
are still enforced by ros2_control; these add MoveIt-level constraints.

```yaml
default_velocity_scaling_factor: 1.0
default_acceleration_scaling_factor: 1.0

joint_limits:
  joint1:
    has_velocity_limits: true
    max_velocity: 2.094395          # rad/s  (overrides URDF if stricter)
    has_acceleration_limits: true
    max_acceleration: 5.0           # rad/s²
    has_deceleration_limits: false
    has_jerk_limits: false
    has_effort_limits: true
    max_effort: 80.0                # N·m
  joint2:
    has_velocity_limits: true
    max_velocity: 2.094395
    has_acceleration_limits: true
    max_acceleration: 5.0
  # ... one entry per actuated joint
```

### 3.3 `moveit_controllers.yaml`

Tells MoveIt's trajectory execution manager which ros2_control controllers to use
and which joints they own.  **Joint names must exactly match the URDF and SRDF.**

```yaml
moveit_controller_manager: moveit_simple_controller_manager/MoveItSimpleControllerManager

moveit_simple_controller_manager:
  controller_names:
    - arm_controller
    - gripper_action_controller

  arm_controller:
    action_ns: follow_joint_trajectory        # appended to controller namespace
    type: FollowJointTrajectory               # or GripperCommand for grippers
    default: true
    joints:
      - joint1
      - joint2
      - joint3
      - joint4
      - joint5
      - joint6

  gripper_action_controller:
    action_ns: gripper_cmd
    type: GripperCommand
    default: true
    joints:
      - finger_joint
```

### 3.4 `ros2_controllers.yaml`

Loaded by the `controller_manager` node (separate from MoveIt). Defines the actual
ros2_control controllers that accept trajectory goals.

```yaml
controller_manager:
  ros__parameters:
    update_rate: 100  # Hz

    arm_controller:
      type: joint_trajectory_controller/JointTrajectoryController

    gripper_action_controller:
      type: gripper_controllers/GripperActionController

    joint_state_broadcaster:
      type: joint_state_broadcaster/JointStateBroadcaster

arm_controller:
  ros__parameters:
    joints:
      - joint1
      - joint2
      - joint3
      - joint4
      - joint5
      - joint6
    command_interfaces:
      - position
    state_interfaces:
      - position
      - velocity
    allow_partial_joints_goal: false

gripper_action_controller:
  ros__parameters:
    joint: finger_joint
    action_monitor_rate: 20.0
    allow_stalling: false
    stall_velocity_threshold: 0.001
    stall_timeout: 1.0
```

### 3.5 `ros2_control` block inside the URDF

The Setup Assistant **modifies the URDF** (step 8) to inject this block. The exporter
should emit it directly so no post-processing is needed.

```xml
<ros2_control name="<RobotName>Hardware" type="system">
  <hardware>
    <!-- For simulation / Gazebo:  -->
    <plugin>gz_ros2_control/GazeboSimSystem</plugin>
    <!-- For mock hardware (no real robot, no Gazebo): -->
    <!-- <plugin>mock_components/GenericSystem</plugin> -->
    <!-- For real hardware: replace with vendor plugin  -->
  </hardware>

  <joint name="joint1">
    <command_interface name="position"/>
    <state_interface name="position">
      <param name="initial_value">0.0</param>
    </state_interface>
    <state_interface name="velocity"/>
  </joint>

  <joint name="joint2">
    <command_interface name="position"/>
    <state_interface name="position">
      <param name="initial_value">0.0</param>
    </state_interface>
    <state_interface name="velocity"/>
  </joint>

  <!-- repeat for each actuated joint -->
</ros2_control>
```

**Plugin choice by target:**

| Target | Plugin |
|---|---|
| Gazebo Harmonic (gz-sim) | `gz_ros2_control/GazeboSimSystem` |
| No simulator (rviz only) | `mock_components/GenericSystem` |
| Real hardware | Vendor-specific (e.g. `ur_robot_driver/URPositionHardwareInterface`) |

---

## 4. Naming Consistency Requirements

All four artifacts must use **identical joint names**. A mismatch causes silent
planning failures or trajectory rejection at runtime.

```
URDF <joint name="joint1">
        ↓ same string ↓
SRDF  <group> → <chain> resolves joint1 into arm group
        ↓ same string ↓
moveit_controllers.yaml  arm_controller.joints: [joint1, ...]
        ↓ same string ↓
ros2_controllers.yaml    arm_controller.joints: [joint1, ...]
        ↓ same string ↓
joint_limits.yaml        joint_limits.joint1: ...
        ↓ same string ↓
kinematics.yaml          arm: (group name must match SRDF group name)
```

Additionally:
- `kinematics.yaml` top-level keys = SRDF `<group name="…">` values
- `moveit_controllers.yaml` `joints:` list = ros2_control `<joint name="…">` names
- `end_effector parent_link` = the `tip_link` of the arm chain in SRDF

---

## 5. MoveIt 2 ↔ ros2_control Data Flow

```
User / MoveIt API
       │  MotionPlanRequest (start state, goal constraints)
       ▼
move_group node  (loads URDF + SRDF + all config yamls)
       │  runs OMPL / Pilz planner
       ▼
trajectory_execution_manager
       │  reads moveit_controllers.yaml → finds arm_controller
       │  sends FollowJointTrajectory action goal to:
       ▼
/arm_controller/follow_joint_trajectory  (action server)
       │  implemented by JointTrajectoryController in ros2_control
       ▼
hardware_interface::SystemInterface
       │  writes position commands to HW or simulator
       ▼
robot / Gazebo joint actuators
```

The `joint_state_broadcaster` continuously publishes `/joint_states` → `robot_state_publisher`
→ `/tf` tree, which MoveIt reads for collision checking.

---

## 6. What SW2GZ Can Auto-Derive vs. What Needs User Input

### Auto-derivable from the SolidWorks kinematic chain

| Item | How derived |
|---|---|
| SRDF `<virtual_joint type="fixed">` | Always "fixed" for a bolted manipulator; `child_link` = URDF root link |
| SRDF `<group name="arm"><chain …/>` | `base_link` = root link of SW assembly; `tip_link` = last active link (flange) |
| SRDF `<disable_collisions reason="Adjacent">` | Walk the URDF parent→child joint graph; every adjacent pair gets an entry |
| `moveit_controllers.yaml` arm_controller joints list | Ordered list of all non-fixed joint names from the URDF |
| `ros2_controllers.yaml` arm_controller joints list | Same ordered list |
| `joint_limits.yaml` entries | Copy `velocity` and `effort` from URDF `<limit>`, set default acceleration |
| `kinematics.yaml` arm entry | Use `kdl_kinematics_plugin/KDLKinematicsPlugin` with sensible defaults |
| URDF `<ros2_control>` block | Generate for each non-fixed joint with `position` command + `position`/`velocity` state |
| `ros2_control` hardware plugin | `gz_ros2_control/GazeboSimSystem` for Gazebo export, `mock_components/GenericSystem` for URDF-only |

### Requires user input (cannot be derived from SW geometry alone)

| Item | Why manual |
|---|---|
| SRDF `<group_state>` named poses | Arbitrary robot configurations; at minimum emit a "home" pose at all-zeros |
| SRDF `<disable_collisions reason="Never">` beyond adjacent pairs | Requires geometry sampling / collision checking not in SW API |
| SRDF `<end_effector>` + gripper `<group>` | Gripper may be a separate assembly; user must identify it |
| `kinematics_solver_timeout` tuning | Depends on robot DoF and workspace complexity |
| `kinematics_solver` choice (KDL vs pick_ik vs trac_ik) | User preference / performance requirement |
| `default_velocity_scaling_factor` in joint_limits.yaml | Application-specific safety setting |
| Real hardware `ros2_control` plugin | Vendor-specific; exporter cannot know the driver |
| OMPL / Pilz planner settings | Application-specific |

### Minimum viable auto-generated set for a serial arm (no gripper)

```
<robot>_moveit_config/
  config/
    <robot>.srdf               ← virtual_joint + chain group + adjacent disable_collisions
    joint_limits.yaml          ← from URDF limits + default scaling factors
    kinematics.yaml            ← KDL with defaults, keyed by group name
    moveit_controllers.yaml    ← arm_controller with all non-fixed joints
    ros2_controllers.yaml      ← JointTrajectoryController + JointStateBroadcaster
  (URDF already contains <ros2_control> block)
```

With these five files plus the URDF, `ros2 launch <robot>_moveit_config demo.launch.py`
will start a fully functional MoveIt 2 demo in RViz with interactive motion planning.

---

## 7. Panda Reference — Annotated SRDF Excerpt

The Franka Panda is the canonical MoveIt 2 reference robot. Its SRDF pattern is the
template SW2GZ should follow:

```xml
<robot name="panda">
  <!-- Fixed manipulator bolted to floor -->
  <virtual_joint name="virtual_joint" type="fixed"
                 parent_frame="world" child_link="panda_link0"/>

  <!-- 7-DOF arm: chain from base to flange -->
  <group name="panda_arm">
    <chain base_link="panda_link0" tip_link="panda_link8"/>
  </group>

  <!-- 2-finger gripper defined by links + joints -->
  <group name="hand">
    <link  name="panda_hand"/>
    <link  name="panda_leftfinger"/>
    <link  name="panda_rightfinger"/>
    <joint name="panda_finger_joint1"/>
    <joint name="panda_finger_joint2"/>
  </group>

  <!-- Composite group for whole-arm planning including gripper -->
  <group name="panda_arm_hand">
    <group name="panda_arm"/>
    <group name="hand"/>
  </group>

  <!-- Named poses for the arm group -->
  <group_state name="ready" group="panda_arm">
    <joint name="panda_joint2" value="-0.785398"/>
    <joint name="panda_joint4" value="-2.356194"/>
    <joint name="panda_joint6" value=" 1.570796"/>
    <joint name="panda_joint7" value=" 0.785398"/>
  </group_state>

  <!-- End effector: hand attaches at panda_link8 -->
  <end_effector name="hand" parent_group="panda_arm"
                parent_link="panda_link8" group="hand"/>

  <!-- Adjacent collisions (sample — full file has all adjacent pairs) -->
  <disable_collisions link1="panda_link0" link2="panda_link1" reason="Adjacent"/>
  <disable_collisions link1="panda_link1" link2="panda_link2" reason="Adjacent"/>
  <!-- Never-colliding pairs found by Setup Assistant sampling -->
  <disable_collisions link1="panda_link0" link2="panda_link3" reason="Never"/>
  <disable_collisions link1="panda_link0" link2="panda_link4" reason="Never"/>
</robot>
```

Source: https://github.com/moveit/moveit_resources/blob/ros2/panda_moveit_config/config/panda.srdf

---

## 8. Quick-Reference Checklist for SW2GZ MoveIt Output

- [ ] URDF: `<ros2_control>` block with hardware plugin + per-joint command/state interfaces
- [ ] URDF: all `<joint>` elements have `<limit lower upper velocity effort/>`
- [ ] SRDF: `<virtual_joint type="fixed" …>` world→base_link
- [ ] SRDF: `<group name="arm"><chain base_link="…" tip_link="…"/></group>`
- [ ] SRDF: at minimum one `<group_state name="home" …>` with all joints at 0
- [ ] SRDF: `<disable_collisions reason="Adjacent">` for every parent→child link pair
- [ ] `kinematics.yaml`: one entry per planning group, KDL solver, 0.05 s timeout
- [ ] `joint_limits.yaml`: one entry per actuated joint with vel/acc limits
- [ ] `moveit_controllers.yaml`: arm_controller listing all actuated joint names
- [ ] `ros2_controllers.yaml`: JointTrajectoryController + JointStateBroadcaster
- [ ] All joint names consistent across URDF, SRDF, both controller YAMLs, joint_limits.yaml
- [ ] Group names in kinematics.yaml match group names in SRDF exactly
