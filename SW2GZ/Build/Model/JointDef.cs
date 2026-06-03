/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Structural-only joint data (what Gz needs to simulate the joint): type, the
axis it moves about, and the lower/upper motion range. Actuation/control
concerns (command interface, effort, velocity) are not stored here.

The user maps each joint to a parent and child link and assigns a SolidWorks
mate that defines it: MateName is the assigned mate, and Type / AxisX-Y-Z /
limits are taken from that mate when assigned. A fixed mate → Fixed; a
concentric mate → Continuous; a limit-angle mate → Revolute; a limit-distance
mate → Prismatic.

Joint origin is not stored here — SolidWorks→ROS coordinate conversion is a
later increment, so the converter emits Pose.Identity for now.
*/
using System.Runtime.Serialization;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build.Model
{
    [DataContract(Name = "JointDef", Namespace = "")]
    public sealed class JointDef
    {
        [DataMember] public string Name { get; set; } = string.Empty;
        [DataMember] public string ParentLink { get; set; } = string.Empty;
        [DataMember] public string ChildLink { get; set; } = string.Empty;
        [DataMember] public UrdfJointType Type { get; set; } = UrdfJointType.Fixed;

        // The assigned mate (defines type/axis/limits), and the cached axis
        // direction (assembly frame) read from that mate.
        [DataMember] public string MateName { get; set; } = string.Empty;
        [DataMember] public double AxisX { get; set; }
        [DataMember] public double AxisY { get; set; }
        [DataMember] public double AxisZ { get; set; }

        [DataMember] public double? LimitLower { get; set; }
        [DataMember] public double? LimitUpper { get; set; }

        // True when a non-zero axis direction has been set (from a mate, a selected
        // reference axis, or a generated one).
        public bool HasAxis =>
            System.Math.Abs(AxisX) > 1e-9 || System.Math.Abs(AxisY) > 1e-9 || System.Math.Abs(AxisZ) > 1e-9;

        public void SetAxis(System.Numerics.Vector3 dir)
        {
            float len = dir.Length();
            if (len > 1e-9f) { dir /= len; }
            AxisX = dir.X; AxisY = dir.Y; AxisZ = dir.Z;
        }
    }
}
