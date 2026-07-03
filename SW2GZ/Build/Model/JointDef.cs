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

        // LEGACY back-compat fields (D2 of the auto-detect plan deprecates them).
        // The Joints step no longer exposes Reference-CS / Reference-Axis pickers
        // and WizardAssemblyWalker.WalkMates() no longer reads these — joints
        // are now driven by AutoJointResolver-populated OriginXYZ + AxisXYZ.
        // Kept as DataMembers so older saved Sw2gzDoc payloads still round-trip.
        [DataMember] public string RefCsName { get; set; } = string.Empty;
        [DataMember] public string RefAxisName { get; set; } = string.Empty;

        // Auto-detected joint origin in the ASSEMBLY frame (e.g. a concentric
        // mate's cylindrical-axis origin). When HasOrigin is true, the wizard
        // walker emits this as MateSpec.Origin / MatePointAssembly and the
        // pipeline anchors the URDF joint origin here in the parent frame
        // (Origin.Position - parentAnchor.Position, rotated by parentAnchor⁻¹).
        // Defaults to (0, 0, 0) / false so legacy payloads load unchanged.
        [DataMember] public double OriginX { get; set; }
        [DataMember] public double OriginY { get; set; }
        [DataMember] public double OriginZ { get; set; }
        [DataMember] public bool HasOrigin { get; set; }

        public void SetOrigin(System.Numerics.Vector3 p)
        {
            OriginX = p.X; OriginY = p.Y; OriginZ = p.Z;
            HasOrigin = true;
        }

        public void ClearOrigin()
        {
            OriginX = 0; OriginY = 0; OriginZ = 0;
            HasOrigin = false;
        }

        [DataMember] public double? LimitLower { get; set; }
        [DataMember] public double? LimitUpper { get; set; }

        // Mate-reference geometric point in the assembly frame (e.g. the axis
        // origin of the assigned concentric mate's cylindrical face). When
        // HasMatePoint is true the joint's URDF <origin> is anchored here in
        // the parent frame instead of at the child part's design origin —
        // fixes hinges that pivot around the wrong fulcrum. Defaults to
        // (0,0,0) / false so legacy JointDefs round-trip unchanged.
        [DataMember] public double MatePointX { get; set; }
        [DataMember] public double MatePointY { get; set; }
        [DataMember] public double MatePointZ { get; set; }
        [DataMember] public bool HasMatePoint { get; set; }

        // True once a mate-based suggestion has been accepted or the user
        // has edited this joint by hand (Type/Axis/Limit/Name) — permanently
        // opts the joint out of future auto-suggestion, including a
        // deliberate choice to leave it Fixed with no axis (otherwise
        // indistinguishable from "never analyzed"). Defaults false so
        // legacy payloads round-trip unchanged.
        [DataMember] public bool IsSuggested { get; set; }

        public void SetMatePoint(System.Numerics.Vector3 p)
        {
            MatePointX = p.X; MatePointY = p.Y; MatePointZ = p.Z;
            HasMatePoint = true;
        }

        public void ClearMatePoint()
        {
            MatePointX = 0; MatePointY = 0; MatePointZ = 0;
            HasMatePoint = false;
        }

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

        // DataContractSerializer leaves missing string members as null. Legacy
        // checkpoints predate RefCsName / RefAxisName, so coerce nulls to ""
        // post-deserialization to keep downstream consumers single-pathed.
        [OnDeserialized]
        internal void OnDeserializedHook(StreamingContext _)
        {
            if (Name        == null) Name        = string.Empty;
            if (ParentLink  == null) ParentLink  = string.Empty;
            if (ChildLink   == null) ChildLink   = string.Empty;
            if (MateName    == null) MateName    = string.Empty;
            if (RefCsName   == null) RefCsName   = string.Empty;
            if (RefAxisName == null) RefAxisName = string.Empty;
        }
    }
}
