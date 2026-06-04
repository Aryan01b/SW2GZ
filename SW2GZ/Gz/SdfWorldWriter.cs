/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits worlds/<name>.sdf for Gz Sim Harmonic. Fixes v1.0 export bug 4 —
previous writer pulled plugin filenames from TargetProfile.SimPluginLib
which for Garden returned `gz-sim8-*` (versioned). Harmonic loads ONLY
the unversioned `gz-sim-*-system` plugins.

Locked to Harmonic — no profile parameter. SDF version 1.10.

P6-data — adds a Write(input, sensors) overload that splices the family
plugins from SdfSensorPlugins before </world>. SdfSensorPlugins is the
single source of truth for sensor-family plugins (imu/sensors/contact/
forcetorque/navsat) — the world writer no longer emits them
unconditionally, so 1-arg callers with no sensors get a sensor-free world.
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
            sb.Append(SdfPhysicsBlock.Default());
            sb.Append(SdfPhysicsBlock.Sun());
            sb.Append(SdfPhysicsBlock.GroundPlane());
            // P6-data — SdfSensorPlugins is the single source of truth for
            // sensor-family plugins (imu/sensors/contact/forcetorque/navsat).
            // For an empty/null sensors list the helper returns "" so the
            // 1-arg overload simply emits no sensor-family plugins (correct —
            // no sensors means no sensor systems needed).
            string extra = SdfSensorPlugins.WritePluginBlock(sensors);
            if (!string.IsNullOrEmpty(extra))
                sb.Append(extra);
            sb.AppendLine("  </world>");
            sb.AppendLine("</sdf>");
            return sb.ToString();
        }

        public static string WriteWithModel(SdfWorldInput input, string modelName,
            double roll = 0, double pitch = 0, double yaw = 0)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(input.WorldName))
                throw new ArgumentException("WorldName must not be null or whitespace.", nameof(input));
            if (string.IsNullOrWhiteSpace(modelName))
                throw new ArgumentException("modelName must not be null or whitespace.", nameof(modelName));

            string baseWorld = Write(input);                  // ground + sun + physics, no model
            string nameEsc = SecurityElement.Escape(modelName);
            string nl = Environment.NewLine;
            // Pose only when the rotation is non-identity. Keeps the legacy
            // identity case byte-identical for the golden test.
            string poseLine = (roll == 0 && pitch == 0 && yaw == 0)
                ? string.Empty
                : "      <pose>0 0 0 " +
                  roll.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)  + " " +
                  pitch.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + " " +
                  yaw.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)   + "</pose>" + nl;
            string include =
                "    <include>" + nl +
                $"      <uri>model://{nameEsc}</uri>" + nl +
                $"      <name>{nameEsc}</name>" + nl +
                poseLine +
                "    </include>" + nl;
            // Splice the include immediately before the closing </world>.
            return baseWorld.Replace("  </world>", include + "  </world>");
        }
    }
}
