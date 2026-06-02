/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — persisted wizard state for the Step 4 (Joints) PropertyManagerPage. One
JointDef per non-root link edge (parent→child) derived from the Step 3 link
tree. Pure / COM-free and DataContract-serializable so it round-trips in the
assembly checkpoint alongside the link list (see Sw2gzExportConfig).

Minimal v2.1 scope: type + principal-axis preset + limits + command interface.
Joint origin is not stored here — SolidWorks→ROS coordinate conversion is a
later increment, so the converter emits Pose.Identity for now.
*/
using System.Runtime.Serialization;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build.Model
{
    // Principal-axis presets for a joint's rotation/translation axis. Keeps the
    // PMP a single dropdown and avoids free-form numeric entry while coordinate
    // conversion is deferred. None = no axis (the Fixed default).
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
        [DataMember] public double LimitEffort { get; set; } = 100.0;
        [DataMember] public double LimitVelocity { get; set; } = 1.0;
        [DataMember] public UrdfCmdInterface Interface { get; set; } = UrdfCmdInterface.Position;
    }
}
