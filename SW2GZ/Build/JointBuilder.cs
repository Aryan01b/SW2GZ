using System;
using System.Collections.Generic;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public static class JointBuilder
    {
        public static (UrdfJoint Joint, IReadOnlyList<string> Warnings) Build(
            MateSpec mate, UrdfLink parent, UrdfLink child)
        {
            if (mate   == null) throw new ArgumentNullException(nameof(mate));
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (child  == null) throw new ArgumentNullException(nameof(child));

            var warnings = new List<string>();

            UrdfJointType type = mate.Kind switch
            {
                MateKind.Fixed      => UrdfJointType.Fixed,
                MateKind.Revolute   => UrdfJointType.Revolute,
                MateKind.Continuous => UrdfJointType.Continuous,
                MateKind.Prismatic  => UrdfJointType.Prismatic,
                _                   => UrdfJointType.Fixed,
            };

            if (type == UrdfJointType.Continuous && mate.Interface == UrdfCmdInterface.Position)
                warnings.Add($"Joint '{mate.Name}' is continuous but uses position interface — " +
                             "no limits enforced. Consider switching to revolute with explicit limits.");

            var joint = new UrdfJoint(
                Name:          mate.Name,
                Type:          type,
                ParentLink:    parent.Name,
                ChildLink:     child.Name,
                Origin:        mate.Origin,
                Axis:          mate.Axis,
                LimitLower:    mate.LimitLower,
                LimitUpper:    mate.LimitUpper,
                LimitEffort:   mate.LimitEffort,
                LimitVelocity: mate.LimitVelocity,
                Interface:     mate.Interface);

            return (joint, warnings);
        }
    }
}
