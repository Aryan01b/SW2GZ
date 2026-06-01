// TODO P7: dead code — kept for legacy ExportHelper callers; delete when SdfSerializer + ExportHelper retirement land.
using System.Collections.Generic;

namespace SW2GZ.Build.Urdf
{
    public sealed record UrdfRobot(
        string PackageName,
        IReadOnlyList<UrdfLink> Links,
        IReadOnlyList<UrdfJoint> Joints);
}
