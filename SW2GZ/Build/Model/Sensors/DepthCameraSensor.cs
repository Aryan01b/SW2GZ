/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — Depth camera sensor. Same intrinsics as CameraSensor but
emits SDF type="depth_camera" with R_FLOAT32 image format.
*/
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public sealed record DepthCameraSensor(
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
        : SensorDef(Name, SensorKind.DepthCamera, AttachedLink, Pose, Topic, GzFrameId, UpdateRate);
}
