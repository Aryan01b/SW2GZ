/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — Contact sensor. CollisionName is the <collision> element name
to monitor. For v2.1 the caller usually sets this to the AttachedLink's
primary collision name (the SDF model writer emits each link's collision
with a fixed name).
*/
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public sealed record ContactSensor(
        string Name,
        string AttachedLink,
        Pose Pose,
        string Topic,
        string GzFrameId,
        double UpdateRate,
        string CollisionName)
        : SensorDef(Name, SensorKind.Contact, AttachedLink, Pose, Topic, GzFrameId, UpdateRate);
}
