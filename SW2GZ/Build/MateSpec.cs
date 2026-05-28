using System;
using System.Numerics;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.Build
{
    public enum MateKind { Fixed, Revolute, Continuous, Prismatic }

    public sealed record MateSpec(
        string Name,
        MateKind Kind,
        Pose Origin,
        Vector3 Axis,
        double? LimitLower,
        double? LimitUpper,
        double LimitEffort,
        double LimitVelocity,
        UrdfCmdInterface Interface);
}
