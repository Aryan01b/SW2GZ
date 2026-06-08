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

        // Reference-geometry path (mirrors upstream solidworks_urdf_exporter):
        // a Reference Coordinate System feature drives joint origin xyz+rpy,
        // and a Reference Axis feature drives joint axis direction. Both are
        // looked up by name on the child component at export time
        // (Component2.Extension.GetCoordinateSystemTransformByName /
        //  SelectByID2 "AXIS"). Empty string = unset; the legacy mate-based
        // path takes over so existing assemblies still export.
        [DataMember] public string RefCsName { get; set; } = string.Empty;
        [DataMember] public string RefAxisName { get; set; } = string.Empty;

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
