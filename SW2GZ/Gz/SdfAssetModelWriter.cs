/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Asset mode — emits a standalone Gz Harmonic model.sdf for a single part so it
can be dropped into a world via <include><uri>model://name</uri>. One link:
visual + collision share the mesh; <material> carries the SW part colour;
collision gets a friction surface. Static by default (props/furniture); a
dynamic asset emits a placeholder inertial (see Sw2gzAssetExporter).

SDF 1.10. Pure / COM-free so the test project source-links it.
*/
using System;
using System.Globalization;
using System.Security;
using System.Text;
using SW2GZ.Build.Model;

namespace SW2GZ.Gz
{
    public sealed record SdfAssetModelInput(
        string ModelName,
        string MeshFile,
        bool IsStatic = true,
        double FrictionMu = 0.8,
        double[] Rgba = null,
        double Mass = 0.0,    // >0 emits an <inertial> (dynamic assets only)
        // A1 — optional 1-DOF joint anchoring the link to the world frame.
        // "none" (default) emits no joint. Any other type makes the model an
        // articulated prop (door/lift/wheel/lever). A joint to world is invalid
        // on a static model, so callers pass IsStatic=false alongside.
        string JointType = "none",   // none | fixed | revolute | continuous | prismatic
        double JointAxisX = 0, double JointAxisY = 0, double JointAxisZ = 1,
        double JointLower = -1.5708, double JointUpper = 1.5708,
        // A2 — optional sensor mounted on the asset link (camera/lidar/imu, etc.).
        // Null emits no sensor. The host world must run the matching sensor system
        // (gz-sim-sensors / -imu) for it to produce data — World mode's "Sensors"
        // panel enables exactly those.
        SensorDef Sensor = null);

    public static class SdfAssetModelWriter
    {
        public static string Write(SdfAssetModelInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(input.ModelName))
                throw new ArgumentException("ModelName must not be null or whitespace.", nameof(input));
            if (string.IsNullOrWhiteSpace(input.MeshFile))
                throw new ArgumentException("MeshFile must not be null or whitespace.", nameof(input));

            var ci = CultureInfo.InvariantCulture;
            string F(double d) => d.ToString("0.######", ci);
            string nameEsc = SecurityElement.Escape(input.ModelName);
            string meshEsc = SecurityElement.Escape(input.MeshFile);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<sdf version=\"1.10\">");
            sb.AppendLine($"  <model name=\"{nameEsc}\">");
            if (input.IsStatic)
                sb.AppendLine("    <static>true</static>");
            sb.AppendLine("    <link name=\"link\">");

            if (!input.IsStatic && input.Mass > 0)
            {
                // Placeholder inertial: a sphere-ish diagonal so a dynamic asset
                // is simulable. Real mass-properties wiring is the upgrade path.
                double i = System.Math.Max(1e-4, input.Mass * 0.01);
                sb.AppendLine("      <inertial>");
                sb.AppendLine($"        <mass>{F(input.Mass)}</mass>");
                sb.AppendLine($"        <inertia><ixx>{F(i)}</ixx><ixy>0</ixy><ixz>0</ixz><iyy>{F(i)}</iyy><iyz>0</iyz><izz>{F(i)}</izz></inertia>");
                sb.AppendLine("      </inertial>");
            }

            sb.AppendLine("      <visual name=\"visual\">");
            sb.AppendLine($"        <geometry><mesh><uri>meshes/{meshEsc}</uri></mesh></geometry>");
            if (input.Rgba != null && input.Rgba.Length == 4)
            {
                double r = input.Rgba[0], g = input.Rgba[1], b = input.Rgba[2], a = input.Rgba[3];
                sb.AppendLine("        <material>");
                sb.AppendLine($"          <ambient>{F(r * 0.6)} {F(g * 0.6)} {F(b * 0.6)} {F(a)}</ambient>");
                sb.AppendLine($"          <diffuse>{F(r)} {F(g)} {F(b)} {F(a)}</diffuse>");
                sb.AppendLine($"          <specular>0.1 0.1 0.1 {F(a)}</specular>");
                sb.AppendLine("        </material>");
            }
            sb.AppendLine("      </visual>");

            sb.AppendLine("      <collision name=\"collision\">");
            sb.AppendLine($"        <geometry><mesh><uri>meshes/{meshEsc}</uri></mesh></geometry>");
            sb.AppendLine("        <surface><friction><ode>");
            sb.AppendLine($"          <mu>{F(input.FrictionMu)}</mu><mu2>{F(input.FrictionMu)}</mu2>");
            sb.AppendLine("        </ode></friction></surface>");
            sb.AppendLine("      </collision>");

            // A2 — sensor mounted on the link (link children indent = 6 spaces).
            if (input.Sensor != null)
                sb.Append(SdfSensorBlocks.Write(input.Sensor, 6));

            sb.AppendLine("    </link>");

            // A1 — optional joint anchoring the link to the world frame.
            string jt = string.IsNullOrWhiteSpace(input.JointType)
                ? "none" : input.JointType.Trim().ToLowerInvariant();
            if (jt != "none")
            {
                // SDF has no "continuous"; it's a revolute with no <limit>.
                string sdfType = jt == "continuous" ? "revolute" : jt;
                sb.AppendLine($"    <joint name=\"joint\" type=\"{sdfType}\">");
                sb.AppendLine("      <parent>world</parent>");
                sb.AppendLine("      <child>link</child>");
                if (jt == "revolute" || jt == "continuous" || jt == "prismatic")
                {
                    sb.AppendLine("      <axis>");
                    sb.AppendLine($"        <xyz>{F(input.JointAxisX)} {F(input.JointAxisY)} {F(input.JointAxisZ)}</xyz>");
                    // continuous = free spin → omit the limit.
                    if (jt != "continuous")
                    {
                        sb.AppendLine("        <limit>");
                        sb.AppendLine($"          <lower>{F(input.JointLower)}</lower><upper>{F(input.JointUpper)}</upper>");
                        sb.AppendLine("        </limit>");
                    }
                    sb.AppendLine("      </axis>");
                }
                sb.AppendLine("    </joint>");
            }

            sb.AppendLine("  </model>");
            sb.AppendLine("</sdf>");
            return sb.ToString();
        }
    }
}
