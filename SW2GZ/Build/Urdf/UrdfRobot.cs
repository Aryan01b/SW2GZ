using System.Collections.Generic;

namespace SW2GZ.Build.Urdf
{
    public sealed record UrdfRobot(
        string PackageName,
        IReadOnlyList<UrdfLink> Links,
        IReadOnlyList<UrdfJoint> Joints);
}
