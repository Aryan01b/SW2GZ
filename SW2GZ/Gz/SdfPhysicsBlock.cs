/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Security;
using System.Text;

namespace SW2GZ.Gz
{
    // An extra scene light beyond the sun (point/spot/directional fill lights).
    // Pose is in the ROS (Z-up) world frame. Range drives point/spot attenuation
    // (ignored for directional). DirX/Y/Z is the beam direction for spot/
    // directional lights (ignored for point). Pure value record.
    public sealed record SdfLight(
        string Name,
        string Type = "point",            // point | spot | directional
        double X = 0, double Y = 0, double Z = 2,
        double R = 1, double G = 1, double B = 1,
        double Intensity = 1.0,
        double Range = 10.0,
        bool CastShadows = false,
        double DirX = 0, double DirY = 0, double DirZ = -1);

    public static class SdfPhysicsBlock
    {
        public static string Default()
        {
            var sb = new StringBuilder();
            sb.AppendLine("    <physics name=\"1ms\" type=\"ignored\">");
            sb.AppendLine("      <max_step_size>0.001</max_step_size>");
            sb.AppendLine("      <real_time_factor>1.0</real_time_factor>");
            sb.AppendLine("    </physics>");
            return sb.ToString();
        }

        // World-mode overload — engine type + step + RTF from the Create-World
        // wizard. Gz Sim mostly ignores the `type` attribute (engine is picked
        // by the Physics system plugin), but we echo the user's choice so the
        // emitted world documents the intended engine.
        public static string Default(string engineType, double maxStepSize, double realTimeFactor)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("    <physics name=\"1ms\" type=\"" +
                SecurityElement.Escape(string.IsNullOrWhiteSpace(engineType) ? "ignored" : engineType) + "\">");
            sb.AppendLine("      <max_step_size>" + maxStepSize.ToString("0.######", ci) + "</max_step_size>");
            sb.AppendLine("      <real_time_factor>" + realTimeFactor.ToString("0.######", ci) + "</real_time_factor>");
            sb.AppendLine("    </physics>");
            return sb.ToString();
        }

        public static string Sun()
        {
            return
@"    <light name=""sun"" type=""directional"">
      <cast_shadows>true</cast_shadows>
      <pose>0 0 10 0 0 0</pose>
      <diffuse>0.8 0.8 0.8 1</diffuse>
      <specular>0.2 0.2 0.2 1</specular>
      <direction>-0.5 0.1 -0.9</direction>
    </light>
";
        }

        // World-mode overload — sun from azimuth/elevation (degrees) with an
        // intensity scale and a shadow toggle. The light direction points FROM
        // the sun TO the scene, so it is the negated sun bearing. Diffuse scales
        // with intensity; specular is a quarter of diffuse for a soft highlight.
        public static string Sun(double azimuthDeg, double elevationDeg, double intensity, bool castShadows)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            double az = azimuthDeg * System.Math.PI / 180.0;
            double el = elevationDeg * System.Math.PI / 180.0;
            double dx = -(System.Math.Cos(el) * System.Math.Cos(az));
            double dy = -(System.Math.Cos(el) * System.Math.Sin(az));
            double dz = -System.Math.Sin(el);
            double d = System.Math.Max(0.0, intensity) * 0.8;     // diffuse level
            double s = d * 0.25;                                   // specular level
            string F(double v) => v.ToString("0.######", ci);
            var sb = new StringBuilder();
            sb.AppendLine("    <light name=\"sun\" type=\"directional\">");
            sb.AppendLine("      <cast_shadows>" + (castShadows ? "true" : "false") + "</cast_shadows>");
            sb.AppendLine("      <pose>0 0 10 0 0 0</pose>");
            sb.AppendLine("      <diffuse>" + F(d) + " " + F(d) + " " + F(d) + " 1</diffuse>");
            sb.AppendLine("      <specular>" + F(s) + " " + F(s) + " " + F(s) + " 1</specular>");
            sb.AppendLine("      <direction>" + F(dx) + " " + F(dy) + " " + F(dz) + "</direction>");
            sb.AppendLine("    </light>");
            return sb.ToString();
        }

        // Emit one extra <light> (point/spot/directional fill light). Diffuse
        // scales with intensity; specular is a quarter of diffuse. Point lights
        // get an <attenuation><range>; spot/directional get a <direction>.
        public static string Light(SdfLight light)
        {
            if (light == null) throw new System.ArgumentNullException(nameof(light));
            if (string.IsNullOrWhiteSpace(light.Name))
                throw new System.ArgumentException("Light Name must not be null or whitespace.", nameof(light));

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string F(double v) => v.ToString("0.######", ci);
            string type = string.IsNullOrWhiteSpace(light.Type) ? "point" : light.Type.Trim().ToLowerInvariant();
            double k = System.Math.Max(0.0, light.Intensity);
            double dr = light.R * 0.8 * k, dg = light.G * 0.8 * k, db = light.B * 0.8 * k;

            var sb = new StringBuilder();
            sb.AppendLine("    <light name=\"" + SecurityElement.Escape(light.Name) + "\" type=\"" + type + "\">");
            sb.AppendLine("      <cast_shadows>" + (light.CastShadows ? "true" : "false") + "</cast_shadows>");
            sb.AppendLine("      <pose>" + F(light.X) + " " + F(light.Y) + " " + F(light.Z) + " 0 0 0</pose>");
            sb.AppendLine("      <diffuse>" + F(dr) + " " + F(dg) + " " + F(db) + " 1</diffuse>");
            sb.AppendLine("      <specular>" + F(dr * 0.25) + " " + F(dg * 0.25) + " " + F(db * 0.25) + " 1</specular>");
            if (type == "point" || type == "spot")
            {
                sb.AppendLine("      <attenuation>");
                sb.AppendLine("        <range>" + F(light.Range) + "</range>");
                sb.AppendLine("        <constant>0.2</constant><linear>0.1</linear><quadratic>0.0</quadratic>");
                sb.AppendLine("      </attenuation>");
            }
            if (type == "spot" || type == "directional")
                sb.AppendLine("      <direction>" + F(light.DirX) + " " + F(light.DirY) + " " + F(light.DirZ) + "</direction>");
            sb.AppendLine("    </light>");
            return sb.ToString();
        }

        public static string GroundPlane()
        {
            return
@"    <model name=""ground_plane"">
      <static>true</static>
      <link name=""link"">
        <collision name=""collision"">
          <geometry><plane><normal>0 0 1</normal><size>100 100</size></plane></geometry>
        </collision>
        <visual name=""visual"">
          <geometry><plane><normal>0 0 1</normal><size>100 100</size></plane></geometry>
          <material><ambient>0.8 0.8 0.8 1</ambient><diffuse>0.8 0.8 0.8 1</diffuse></material>
        </visual>
      </link>
    </model>
";
        }
    }
}
