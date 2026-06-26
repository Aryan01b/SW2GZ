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

    // World-mode scene: each picked SolidWorks component becomes one inlined
    // static <model>. Name is cosmetic (sanitized); MeshFile is the .dae the
    // exporter wrote under worlds/meshes/. Visual and collision share the mesh.
    // Rgba (0..1, length 4) is the SolidWorks part color → an explicit SDF
    // <material> that overrides the mesh's own material for a clean, even
    // per-asset color in Gz. Null → no material (mesh color used as-is).
    public sealed record SdfSceneModel(string Name, string MeshFile, double[] Rgba = null);

    public sealed record SdfSceneInput(
        string WorldName,
        IReadOnlyList<SdfSceneModel> Models,
        bool IncludeGroundPlane = false,
        string PhysicsEngine = "ode",
        double MaxStepSize = 0.001,
        double RealTimeFactor = 1.0,
        double Roll = 0.0,
        double Pitch = 0.0,
        double Yaw = 0.0);

    public static class SdfWorldWriter
    {
        // World-mode writer — emits a single self-contained world: physics +
        // sun, an optional default ground_plane (only when no ground component
        // was picked), then one inlined static <model> per scene model. The
        // whole-scene SW→ROS rotation rides on each model's <pose> (same rpy
        // for all → preserves relative placement while rotating SW's up onto
        // ROS Z). Placement is baked into the mesh vertices (assembly-frame
        // tessellation), so the position part of every pose is 0 0 0.
        public static string WriteScene(SdfSceneInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(input.WorldName))
                throw new ArgumentException("WorldName must not be null or whitespace.", nameof(input));

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<sdf version=\"1.10\">");
            sb.AppendLine($"  <world name=\"{SecurityElement.Escape(input.WorldName)}\">");
            sb.AppendLine("    <plugin filename=\"gz-sim-physics-system\"           name=\"gz::sim::systems::Physics\"/>");
            sb.AppendLine("    <plugin filename=\"gz-sim-user-commands-system\"     name=\"gz::sim::systems::UserCommands\"/>");
            sb.AppendLine("    <plugin filename=\"gz-sim-scene-broadcaster-system\" name=\"gz::sim::systems::SceneBroadcaster\"/>");
            sb.Append(SdfPhysicsBlock.Default(input.PhysicsEngine, input.MaxStepSize, input.RealTimeFactor));
            // Scene ambient so faces whose tessellated normal points away from
            // the sun still read as their material color instead of going black
            // (CAD tessellation winding isn't guaranteed outward).
            sb.AppendLine("    <scene>");
            sb.AppendLine("      <ambient>0.5 0.5 0.5 1</ambient>");
            sb.AppendLine("      <background>0.8 0.85 0.9 1</background>");
            sb.AppendLine("    </scene>");
            sb.Append(SdfPhysicsBlock.Sun());
            if (input.IncludeGroundPlane)
                sb.Append(SdfPhysicsBlock.GroundPlane());

            string rpy = input.Roll.ToString("0.######", ci) + " " +
                         input.Pitch.ToString("0.######", ci) + " " +
                         input.Yaw.ToString("0.######", ci);
            bool poseNeeded = input.Roll != 0 || input.Pitch != 0 || input.Yaw != 0;

            if (input.Models != null)
            {
                foreach (SdfSceneModel m in input.Models)
                {
                    if (m == null) continue;
                    string nameEsc = SecurityElement.Escape(m.Name);
                    string meshEsc = SecurityElement.Escape(m.MeshFile);
                    sb.AppendLine($"    <model name=\"{nameEsc}\">");
                    sb.AppendLine("      <static>true</static>");
                    if (poseNeeded)
                        sb.AppendLine("      <pose>0 0 0 " + rpy + "</pose>");
                    sb.AppendLine("      <link name=\"link\">");
                    sb.AppendLine("        <visual name=\"visual\">");
                    sb.AppendLine($"          <geometry><mesh><uri>meshes/{meshEsc}</uri></mesh></geometry>");
                    if (m.Rgba != null && m.Rgba.Length == 4)
                    {
                        string F(double d) => d.ToString("0.###", ci);
                        double r = m.Rgba[0], g = m.Rgba[1], b = m.Rgba[2], a = m.Rgba[3];
                        sb.AppendLine("          <material>");
                        // Ambient lifted off the diffuse so the part reads as its
                        // SW color evenly; specular kept low for a matte CAD look.
                        sb.AppendLine($"            <ambient>{F(r * 0.6)} {F(g * 0.6)} {F(b * 0.6)} {F(a)}</ambient>");
                        sb.AppendLine($"            <diffuse>{F(r)} {F(g)} {F(b)} {F(a)}</diffuse>");
                        sb.AppendLine($"            <specular>0.1 0.1 0.1 {F(a)}</specular>");
                        sb.AppendLine("          </material>");
                    }
                    sb.AppendLine("        </visual>");
                    sb.AppendLine("        <collision name=\"collision\">");
                    sb.AppendLine($"          <geometry><mesh><uri>meshes/{meshEsc}</uri></mesh></geometry>");
                    sb.AppendLine("        </collision>");
                    sb.AppendLine("      </link>");
                    sb.AppendLine("    </model>");
                }
            }

            sb.AppendLine("  </world>");
            sb.AppendLine("</sdf>");
            return sb.ToString();
        }

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
