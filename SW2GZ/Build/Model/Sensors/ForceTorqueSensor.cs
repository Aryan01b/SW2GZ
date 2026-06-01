/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — Force/torque sensor. Attaches to a joint, not a link's frame,
so SdfSensorBlocks omits <pose>/<gz_frame_id>. ChildJointName must
resolve to a UrdfJoint in the RobotModel.
*/
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public sealed record ForceTorqueSensor(
        string Name,
        string AttachedLink,
        Pose Pose,
        string Topic,
        string GzFrameId,
        double UpdateRate,
        string ChildJointName)
        : SensorDef(Name, SensorKind.ForceTorque, AttachedLink, Pose, Topic, GzFrameId, UpdateRate);
}
