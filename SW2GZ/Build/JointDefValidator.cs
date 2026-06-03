/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — advisory (warn-not-block) checks for the Step 4 joints. Editing joints is
optional and the Fixed default always yields a valid model, so these never
block the wizard; the PMP surfaces them in a validation label. The structural
validators on the export path remain the hard gate. Pure / COM-free.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public static class JointDefValidator
    {
        public static List<string> Validate(IReadOnlyList<JointDef> joints)
        {
            var warnings = new List<string>();
            if (joints == null) return warnings;

            foreach (JointDef j in joints)
            {
                // Axis is meaningful for revolute/continuous/prismatic and for
                // planar (the plane normal). Fixed and floating carry no axis.
                bool needsAxis = j.Type == UrdfJointType.Revolute
                              || j.Type == UrdfJointType.Continuous
                              || j.Type == UrdfJointType.Prismatic
                              || j.Type == UrdfJointType.Planar;
                if (needsAxis && !j.HasAxis)
                    warnings.Add($"Joint '{j.Name}' is {j.Type.ToString().ToLowerInvariant()} " +
                                 "but has no axis — select or generate a reference axis.");

                // Limits only apply to revolute/prismatic (continuous is unlimited).
                bool limited = j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Prismatic;
                if (limited && j.LimitLower.HasValue && j.LimitUpper.HasValue &&
                    j.LimitLower > j.LimitUpper)
                    warnings.Add($"Joint '{j.Name}' lower limit exceeds upper limit.");
            }

            return warnings;
        }
    }
}
