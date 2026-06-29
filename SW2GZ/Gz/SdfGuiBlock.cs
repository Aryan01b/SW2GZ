/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

World-mode <gui> block for Gz Sim Harmonic. Without it `gz sim` opens the world
with its default camera parked at the origin looking down -X, so an assembly
that was modeled away from origin (or simply tall) frequently lands off-screen.

We emit the standard Harmonic GUI panels — the 3D scene (MinimalScene) plus the
scene manager / view-control / world-control / stats / entity-tree plugins — and
seed MinimalScene's <camera_pose> with a pose the exporter computes from the
scene bounds so the world opens framed on the assets.

Pure string builder (no COM) → golden-testable. Emitted only when a camera is
supplied; the robot/asset paths never call this, keeping their goldens intact.
*/
using System.Globalization;
using System.Text;

namespace SW2GZ.Gz
{
    // Initial 3D-view camera, expressed in the world (ROS, Z-up) frame:
    // position (X,Y,Z) metres and orientation (Roll,Pitch,Yaw) radians.
    public sealed record SdfCamera(
        double X, double Y, double Z,
        double Roll, double Pitch, double Yaw);

    public static class SdfGuiBlock
    {
        // Emits the <gui> block. When cam is non-null MinimalScene opens framed
        // at that pose; otherwise Gz's default origin view is used. keyPublisher
        // appends the KeyPublisher GUI plugin (keyboard teleop) before </gui>.
        public static string Default(SdfCamera cam) => Default(cam, false);

        public static string Default(SdfCamera cam, bool keyPublisher)
        {
            var ci = CultureInfo.InvariantCulture;
            string N(double d) => d.ToString("0.######", ci);

            var sb = new StringBuilder();
            sb.AppendLine("    <gui fullscreen=\"false\">");

            // 3D scene — the viewport. ogre2 is Harmonic's default render engine.
            sb.AppendLine("      <plugin filename=\"MinimalScene\" name=\"3D View\">");
            sb.AppendLine("        <gz-gui>");
            sb.AppendLine("          <title>3D View</title>");
            sb.AppendLine("          <property type=\"bool\" key=\"showTitleBar\">false</property>");
            sb.AppendLine("          <property type=\"string\" key=\"state\">docked</property>");
            sb.AppendLine("        </gz-gui>");
            sb.AppendLine("        <engine>ogre2</engine>");
            sb.AppendLine("        <scene>scene</scene>");
            sb.AppendLine("        <ambient_light>0.4 0.4 0.4</ambient_light>");
            sb.AppendLine("        <background_color>0.8 0.85 0.9</background_color>");
            if (cam != null)
                sb.AppendLine("        <camera_pose>" +
                    N(cam.X) + " " + N(cam.Y) + " " + N(cam.Z) + " " +
                    N(cam.Roll) + " " + N(cam.Pitch) + " " + N(cam.Yaw) +
                    "</camera_pose>");
            sb.AppendLine("      </plugin>");

            // Scene plumbing — render the world entities and let the mouse orbit.
            sb.AppendLine("      <plugin filename=\"GzSceneManager\" name=\"Scene Manager\"/>");
            sb.AppendLine("      <plugin filename=\"InteractiveViewControl\" name=\"Interactive view control\"/>");
            sb.AppendLine("      <plugin filename=\"CameraTracking\" name=\"Camera Tracking\"/>");

            // Play / pause / step toolbar.
            sb.AppendLine("      <plugin filename=\"WorldControl\" name=\"World control\">");
            sb.AppendLine("        <gz-gui>");
            sb.AppendLine("          <title>World control</title>");
            sb.AppendLine("          <property type=\"bool\" key=\"showTitleBar\">false</property>");
            sb.AppendLine("          <property type=\"bool\" key=\"resizable\">false</property>");
            sb.AppendLine("          <property type=\"double\" key=\"height\">72</property>");
            sb.AppendLine("          <property type=\"string\" key=\"state\">floating</property>");
            sb.AppendLine("        </gz-gui>");
            sb.AppendLine("        <play_pause>true</play_pause>");
            sb.AppendLine("        <step>true</step>");
            sb.AppendLine("        <start_paused>true</start_paused>");
            sb.AppendLine("      </plugin>");

            // Sim-time / real-time / RTF readout.
            sb.AppendLine("      <plugin filename=\"WorldStats\" name=\"World stats\">");
            sb.AppendLine("        <gz-gui>");
            sb.AppendLine("          <title>World stats</title>");
            sb.AppendLine("          <property type=\"bool\" key=\"showTitleBar\">false</property>");
            sb.AppendLine("          <property type=\"bool\" key=\"resizable\">false</property>");
            sb.AppendLine("          <property type=\"double\" key=\"height\">110</property>");
            sb.AppendLine("          <property type=\"double\" key=\"width\">290</property>");
            sb.AppendLine("          <property type=\"string\" key=\"state\">floating</property>");
            sb.AppendLine("        </gz-gui>");
            sb.AppendLine("        <sim_time>true</sim_time>");
            sb.AppendLine("        <real_time>true</real_time>");
            sb.AppendLine("        <real_time_factor>true</real_time_factor>");
            sb.AppendLine("      </plugin>");

            // Hierarchical entity list.
            sb.AppendLine("      <plugin filename=\"EntityTree\" name=\"Entity tree\"/>");

            // Keyboard teleop publisher (publishes keystrokes on
            // /keyboard/keypress; paired with TriggeredPublisher at world level).
            if (keyPublisher)
                sb.Append(SdfWorldPluginsWriter.WriteGuiKeyPublisher(new SdfWorldPlugins(KeyPublisher: true)));

            sb.AppendLine("    </gui>");
            return sb.ToString();
        }
    }
}
