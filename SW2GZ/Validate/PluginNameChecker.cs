/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Static lint over emitted SDF / xacro strings for two plugin-name bugs:

  PLG001 — Garden-versioned gz-simN-* plugin filename detected.
           Harmonic loads only the unversioned gz-sim-*-system plugins.
           Bug 4 from v1.0 export.

  PLG002 — gz_ros2_control plugin block present but the class name is wrong.
           Harmonic requires gz_ros2_control::GazeboSimROS2ControlPlugin.
           Bug 8 from v1.0 export.
*/
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SW2GZ.Validate
{
    public static class PluginNameChecker
    {
        // Matches gz-simN- where N is one or more digits, e.g. gz-sim8-physics-system
        private static readonly Regex GardenVersioned = new Regex(@"gz-sim\d+-", RegexOptions.Compiled);

        public static IReadOnlyList<ValidationIssue> Check(string xml)
        {
            var issues = new List<ValidationIssue>();
            if (string.IsNullOrEmpty(xml)) return issues;

            if (GardenVersioned.IsMatch(xml))
                issues.Add(new ValidationIssue(IssueSeverity.Error, "PLG001",
                    "Garden-versioned plugin filename detected (gz-simN-…). Harmonic uses unversioned gz-sim-*-system.",
                    "sdf/xacro"));

            if (xml.Contains("gz_ros2_control-system")
                && !xml.Contains("gz_ros2_control::GazeboSimROS2ControlPlugin"))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, "PLG002",
                    "gz_ros2_control plugin present but class name is wrong. Expected gz_ros2_control::GazeboSimROS2ControlPlugin.",
                    "xacro"));
            }

            return issues;
        }
    }
}
