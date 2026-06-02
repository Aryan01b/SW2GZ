/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — converts the wizard's JointDef checkpoint records into domain UrdfJoints
for the export model. Pure / COM-free + unit-tested.

Joint origin is emitted as Pose.Identity: SolidWorks→ROS coordinate conversion
of the joint frame is a later increment. The principal-axis preset maps to a
unit vector in the link's local frame.
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
        public static Vector3 AxisVector(JointAxisPreset preset)
        {
            switch (preset)
            {
                case JointAxisPreset.PlusX:  return new Vector3(1, 0, 0);
                case JointAxisPreset.MinusX: return new Vector3(-1, 0, 0);
                case JointAxisPreset.PlusY:  return new Vector3(0, 1, 0);
                case JointAxisPreset.MinusY: return new Vector3(0, -1, 0);
                case JointAxisPreset.PlusZ:  return new Vector3(0, 0, 1);
                case JointAxisPreset.MinusZ: return new Vector3(0, 0, -1);
                default:                     return Vector3.Zero;
            }
        }

        public static UrdfJoint ToUrdfJoint(JointDef def)
        {
            return new UrdfJoint(
                Name:          def.Name,
                Type:          def.Type,
                ParentLink:    def.ParentLink,
                ChildLink:     def.ChildLink,
                Origin:        Pose.Identity,           // SW→ROS conversion deferred
                Axis:          AxisVector(def.Axis),
                LimitLower:    def.LimitLower,
                LimitUpper:    def.LimitUpper,
                LimitEffort:   def.LimitEffort,
                LimitVelocity: def.LimitVelocity,
                Interface:     def.Interface);
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
