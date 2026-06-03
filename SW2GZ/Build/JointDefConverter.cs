/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — converts the wizard's JointDef checkpoint records into domain UrdfJoints
for the export model. Pure / COM-free + unit-tested.

Joint origin is emitted as Pose.Identity: SolidWorks→ROS coordinate conversion
of the joint frame is a later increment. The axis is the cached reference-axis
direction. Effort/velocity/interface are not part of the structural data we
save; the export increment supplies real values, so neutral defaults are used.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.Build
{
    public static class JointDefConverter
    {
        public static UrdfJoint ToUrdfJoint(JointDef def)
        {
            return new UrdfJoint(
                Name:          def.Name,
                Type:          def.Type,
                ParentLink:    def.ParentLink,
                ChildLink:     def.ChildLink,
                Origin:        Pose.Identity,           // SW→ROS conversion deferred
                Axis:          new Vector3((float)def.AxisX, (float)def.AxisY, (float)def.AxisZ),
                LimitLower:    def.LimitLower,
                LimitUpper:    def.LimitUpper,
                LimitEffort:   0.0,
                LimitVelocity: 0.0,
                Interface:     UrdfCmdInterface.Position);
        }

        public static List<UrdfJoint> ToUrdfJoints(IReadOnlyList<JointDef> defs)
        {
            var joints = new List<UrdfJoint>();
            if (defs == null) return joints;
            foreach (JointDef d in defs) joints.Add(ToUrdfJoint(d));
            return joints;
        }
    }
}
