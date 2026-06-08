/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — pure, COM-free description of one SolidWorks mate, listed in the Joints
step so the user can assign a mate to a joint. Kind is the joint type the mate
implies (fixed → Fixed, concentric → Continuous, limit-angle → Revolute,
limit-distance → Prismatic); Axis is a best-effort direction; Lower/Upper come
from a limit mate's range. Produced by SolidWorksAssemblyWalker.WalkAllMates.
*/
using System.Numerics;

namespace SW2GZ.Build
{
    public sealed record MateInfo(
        string Name,
        MateKind Kind,
        Vector3 Axis,
        double? LimitLower,
        double? LimitUpper,
        string LinkA = null,
        string LinkB = null,
        // Geometric mate-reference point in the assembly frame (e.g. the axis
        // origin of a concentric mate's cylindrical face). Nullable — fallback
        // (null) means "use the legacy part-anchor as the joint frame".
        Vector3? MatePointAssembly = null);
}
