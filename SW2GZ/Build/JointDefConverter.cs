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

        // Snaps an arbitrary axis direction (e.g. read from a mate) to the nearest
        // principal-axis preset by dominant component + sign. A (near-)zero vector
        // maps to None.
        public static JointAxisPreset SnapToPreset(Vector3 axis)
        {
            float ax = System.Math.Abs(axis.X);
            float ay = System.Math.Abs(axis.Y);
            float az = System.Math.Abs(axis.Z);
            if (ax < 1e-6f && ay < 1e-6f && az < 1e-6f) return JointAxisPreset.None;

            if (ax >= ay && ax >= az) return axis.X >= 0 ? JointAxisPreset.PlusX : JointAxisPreset.MinusX;
            if (ay >= ax && ay >= az) return axis.Y >= 0 ? JointAxisPreset.PlusY : JointAxisPreset.MinusY;
            return axis.Z >= 0 ? JointAxisPreset.PlusZ : JointAxisPreset.MinusZ;
        }

        public static UrdfJoint ToUrdfJoint(JointDef def)
        {
            // Effort/velocity/interface are not part of the structural data we save;
            // the export increment supplies real values. Pass neutral defaults here.
            return new UrdfJoint(
                Name:          def.Name,
                Type:          def.Type,
                ParentLink:    def.ParentLink,
                ChildLink:     def.ChildLink,
                Origin:        Pose.Identity,           // SW→ROS conversion deferred
                Axis:          AxisVector(def.Axis),
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
