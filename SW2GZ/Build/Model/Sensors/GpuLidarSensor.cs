/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — GPU lidar sensor. v2.1 emits a single horizontal scan;
vertical multi-channel (3D lidar) deferred until UI exposes the
fields. Defaults match a single-plane 360° scanner.
*/
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public sealed record GpuLidarSensor(
        string Name,
        string AttachedLink,
        Pose Pose,
        string Topic,
        string GzFrameId,
        double UpdateRate,
        int HorizontalSamples,
        double HorizontalMinAngle,
        double HorizontalMaxAngle,
        double RangeMin,
        double RangeMax)
        : SensorDef(Name, SensorKind.GpuLidar, AttachedLink, Pose, Topic, GzFrameId, UpdateRate);
}
