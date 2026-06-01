/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — Sensor data model. Abstract base record with one concrete
subtype per SensorKind under Build/Model/Sensors/. Concrete records
add type-specific parameters via positional syntax. Records preserve
value-based equality across the hierarchy so RobotModel equality keeps
working for free.

Common fields apply to every sensor:
  - Name           sanitized identifier (RosNameSanitizer)
  - Kind           discriminator (SensorKind)
  - AttachedLink   name of the link this sensor mounts on
  - Pose           pose in the attached link's frame
  - Topic          ROS topic name (always starts with '/')
  - GzFrameId      <gz_frame_id> emitted into the SDF block
  - UpdateRate     Hz; must be > 0
*/
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public abstract record SensorDef(
        string Name,
        SensorKind Kind,
        string AttachedLink,
        Pose Pose,
        string Topic,
        string GzFrameId,
        double UpdateRate);
}
