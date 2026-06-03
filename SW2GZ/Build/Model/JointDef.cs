/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Structural-only joint data (what Gz needs to simulate the joint): type, the
axis it moves about, and the lower/upper motion range. Actuation/control
concerns (command interface, effort, velocity) are not stored here.

The axis is a SolidWorks reference axis: AxisRef is the reference-axis feature
name (empty until the user selects or generates one) and AxisX/Y/Z is its cached
direction in the assembly frame. Type is seeded from the mate between the two
links; the user reviews each joint.

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

        // Reference axis: feature name (or empty) + cached direction (assembly frame).
        [DataMember] public string AxisRef { get; set; } = string.Empty;
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
