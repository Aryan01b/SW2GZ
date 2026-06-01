/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — RGB camera sensor. Emits SDF type="camera" with R8G8B8.
Depth variant lives in DepthCameraSensor; same intrinsics shape so
both can share a renderer in SdfSensorBlocks.
*/
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public sealed record CameraSensor(
        string Name,
        string AttachedLink,
        Pose Pose,
        string Topic,
        string GzFrameId,
        double UpdateRate,
        int Width,
        int Height,
        double HorizontalFovRad,
        double NearClip,
        double FarClip)
        : SensorDef(Name, SensorKind.Camera, AttachedLink, Pose, Topic, GzFrameId, UpdateRate);
}
