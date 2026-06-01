/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — IMU sensor. GaussianNoiseStdDev applies to all six channels
(angular_velocity x/y/z, linear_acceleration x/y/z); 0 means perfect.
*/
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public sealed record ImuSensor(
        string Name,
        string AttachedLink,
        Pose Pose,
        string Topic,
        string GzFrameId,
        double UpdateRate,
        double GaussianNoiseStdDev)
        : SensorDef(Name, SensorKind.Imu, AttachedLink, Pose, Topic, GzFrameId, UpdateRate);
}
