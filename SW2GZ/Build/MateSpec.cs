using System;
using System.Numerics;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.Build
{
    public enum MateKind { Fixed, Revolute, Continuous, Prismatic, Planar, Floating }

    // ParentLink / ChildLink are sanitized link names matching LinkSpec.Name.
    // They identify which two links the mate couples; JointGraphBuilder resolves
    // them to UrdfLink instances before calling JointBuilder. (P2)
    public sealed record MateSpec(
        string Name,
        MateKind Kind,
        Pose Origin,
        Vector3 Axis,
        double? LimitLower,
        double? LimitUpper,
        double LimitEffort,
        double LimitVelocity,
        UrdfCmdInterface Interface,
        string ParentLink,
        string ChildLink);
}
