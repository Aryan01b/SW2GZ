namespace SW2GZ.Build.Urdf
{
    public enum UrdfJointType { Fixed, Revolute, Continuous, Prismatic, Planar, Floating }
    public enum UrdfCmdInterface { Position, Velocity, Effort }

    public sealed record UrdfJoint(
        string Name,
        UrdfJointType Type,
        string ParentLink,
        string ChildLink,
        SW2GZ.Math.Pose Origin,
        System.Numerics.Vector3 Axis,
        double? LimitLower,
        double? LimitUpper,
        double LimitEffort,
        double LimitVelocity,
        UrdfCmdInterface Interface);
}
