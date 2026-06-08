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
    //
    // MatePointAssembly (nullable) is the mate's GEOMETRIC reference point in the
    // assembly frame — e.g. the axis origin of a concentric mate's cylindrical
    // face. When set, JointOriginResolver anchors the joint's <origin> at this
    // point in the parent frame (the URDF link frame becomes the mate point, not
    // the part's design origin). When null, the resolver falls back to the legacy
    // parentAnchor⁻¹ ∘ childAnchor behavior so existing goldens stay byte-stable.
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
        string ChildLink,
        Vector3? MatePointAssembly = null);
}
