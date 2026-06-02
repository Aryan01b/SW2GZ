/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — lightweight transport for extracted joint data into the wizard. The
COM layer (WizardModelComposer) builds these from the UrdfJoint list it
already produces; JointsStepViewModel turns each into a JointEditViewModel.
Mirrors LinkDto: keeps the VM layer free of any COM/SW types.
*/
using System.Numerics;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.UI.ViewModels
{
    public sealed record JointDto(
        string Name,
        UrdfJointType Type,
        string ParentLink,
        string ChildLink,
        Pose Origin,
        Vector3 Axis,
        double? LimitLower,
        double? LimitUpper,
        double LimitEffort,
        double LimitVelocity,
        UrdfCmdInterface Interface)
    {
        /// Convenience projection straight from a domain UrdfJoint.
        public static JointDto From(UrdfJoint j) =>
            new JointDto(j.Name, j.Type, j.ParentLink, j.ChildLink, j.Origin, j.Axis,
                         j.LimitLower, j.LimitUpper, j.LimitEffort, j.LimitVelocity, j.Interface);
    }
}
