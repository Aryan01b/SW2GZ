/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — NavSat (GNSS / GPS) sensor. Emits the SDF <navsat> block with
horizontal/vertical position noise. Same Gaussian stddev applies to
both axes for v2.1.
*/
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public sealed record NavsatSensor(
        string Name,
        string AttachedLink,
        Pose Pose,
        string Topic,
        string GzFrameId,
        double UpdateRate,
        double GaussianNoiseStdDev)
        : SensorDef(Name, SensorKind.Navsat, AttachedLink, Pose, Topic, GzFrameId, UpdateRate);
}
