/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: minimal sensor placeholder. Real fields
(camera/imu/lidar specifics, update_rate, intrinsics, etc.) land in P6.
For v2.1 the RobotModel always carries an empty Sensors list.
*/
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public sealed record SensorDef(
        string Name,
        string Type,
        string AttachedLink,
        Pose Pose,
        string Topic,
        string GzFrameId);
}
