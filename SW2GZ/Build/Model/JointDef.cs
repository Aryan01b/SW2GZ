/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — persisted wizard state for the Step 4 (Joints) PropertyManagerPage. One
JointDef per non-root link edge (parent→child) derived from the Step 3 link
tree. Pure / COM-free and DataContract-serializable so it round-trips in the
assembly checkpoint alongside the link list (see Sw2gzExportConfig).

Structural-only scope (what Gz needs to simulate the joint): type, the axis it
moves about, and the lower/upper motion range. Actuation/control concerns
(command interface, effort, velocity) are intentionally NOT stored here. Type
and axis are seeded from the SolidWorks mates; the user reviews each joint.

Joint origin is not stored here — SolidWorks→ROS coordinate conversion is a
later increment, so the converter emits Pose.Identity for now.
*/
using System.Runtime.Serialization;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build.Model
{
    // Principal-axis presets for a joint's rotation/translation axis. Keeps the
    // PMP a single dropdown; mate-derived axes snap to the nearest one. None =
    // no axis (the Fixed default).
    public enum JointAxisPreset { None, PlusX, MinusX, PlusY, MinusY, PlusZ, MinusZ }

    [DataContract(Name = "JointDef", Namespace = "")]
    public sealed class JointDef
    {
        [DataMember] public string Name { get; set; } = string.Empty;
        [DataMember] public string ParentLink { get; set; } = string.Empty;
        [DataMember] public string ChildLink { get; set; } = string.Empty;
        [DataMember] public UrdfJointType Type { get; set; } = UrdfJointType.Fixed;
        [DataMember] public JointAxisPreset Axis { get; set; } = JointAxisPreset.None;
        [DataMember] public double? LimitLower { get; set; }
        [DataMember] public double? LimitUpper { get; set; }
    }
}
