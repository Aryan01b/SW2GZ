/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits worlds/<name>.sdf for Gz Sim Harmonic. Fixes v1.0 export bug 4 —
previous writer pulled plugin filenames from TargetProfile.SimPluginLib
which for Garden returned `gz-sim8-*` (versioned). Harmonic loads ONLY
the unversioned `gz-sim-*-system` plugins.

Locked to Harmonic — no profile parameter. SDF version 1.10.

P6-data — adds a Write(input, sensors) overload that splices the family
plugins from SdfSensorPlugins before </world>. For empty/null sensors the
output is byte-identical to the single-arg overload, so golden tests
keep passing.
*/
using System;
using System.Collections.Generic;
using System.Security;
using System.Text;
using SW2GZ.Build.Model;

namespace SW2GZ.Gz
{
    public sealed record SdfWorldInput(string WorldName);

    public static class SdfWorldWriter
    {
        public static string Write(SdfWorldInput input) => Write(input, null);

        public static string Write(SdfWorldInput input, IReadOnlyList<SensorDef> sensors)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(input.WorldName))
                throw new ArgumentException("WorldName must not be null or whitespace.", nameof(input));

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<sdf version=\"1.10\">");
            sb.AppendLine($"  <world name=\"{SecurityElement.Escape(input.WorldName)}\">");
            sb.AppendLine("    <plugin filename=\"gz-sim-physics-system\"           name=\"gz::sim::systems::Physics\"/>");
            sb.AppendLine("    <plugin filename=\"gz-sim-user-commands-system\"     name=\"gz::sim::systems::UserCommands\"/>");
            sb.AppendLine("    <plugin filename=\"gz-sim-scene-broadcaster-system\" name=\"gz::sim::systems::SceneBroadcaster\"/>");
            sb.AppendLine("    <plugin filename=\"gz-sim-sensors-system\"           name=\"gz::sim::systems::Sensors\"/>");
            sb.AppendLine("    <plugin filename=\"gz-sim-imu-system\"               name=\"gz::sim::systems::Imu\"/>");
            sb.Append(SdfPhysicsBlock.Default());
            sb.Append(SdfPhysicsBlock.Sun());
            sb.Append(SdfPhysicsBlock.GroundPlane());
            // P6-data — only contact/forcetorque/navsat families add real
            // value here (imu/sensors families already covered by the always-
            // emitted defaults above). For an empty sensors list the helper
            // returns "" so this overload stays byte-identical to the legacy
            // Write(input).
            string extra = SdfSensorPlugins.WritePluginBlock(sensors);
            if (!string.IsNullOrEmpty(extra))
                sb.Append(extra);
            sb.AppendLine("  </world>");
            sb.AppendLine("</sdf>");
            return sb.ToString();
        }
    }
}
