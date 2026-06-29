/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits the world-level <plugin> tags the user enabled in the "Sensors" panel.
Unlike the robot path (SdfSensorPlugins, which derives plugins from a sensor
list), World mode just toggles support on/off so spawned models can use it.

Two outputs:
  WriteWorldPlugins  → the <world>-level <plugin> lines (4-space indent), in a
                       fixed order: user-commands, scene-broadcaster, then the
                       sensor-family systems, then keyboard-teleop
                       TriggeredPublisher blocks.
  WriteGuiKeyPublisher → the KeyPublisher GUI plugin line (6-space indent) to
                       splice inside <gui>, since KeyPublisher is a GUI plugin.

Plugin filenames follow the Harmonic unversioned convention (gz-sim-*-system);
wrong names silently fail to load (see docs/reference/gz-harmonic.md §2).

The user-commands + scene-broadcaster lines are byte-identical to the ones
SdfWorldWriter previously emitted unconditionally, so a default world (those two
on, everything else off) is unchanged.
*/
using System.Text;

namespace SW2GZ.Gz
{
    // Pure on/off flags for the world's support plugins (maps from
    // Sw2gzWorldSensorsConfig). Baseline runtime plugins default on.
    public sealed record SdfWorldPlugins(
        bool Sensors = false,
        bool Imu = false,
        bool Contact = false,
        bool ForceTorque = false,
        bool Navsat = false,
        bool UserCommands = true,
        bool SceneBroadcaster = true,
        bool KeyPublisher = false,
        bool TriggeredPublisher = false);

    public static class SdfWorldPluginsWriter
    {
        // Qt key codes published by KeyPublisher on /keyboard/keypress.
        private const int KeyLeft  = 16777234;
        private const int KeyUp    = 16777235;
        private const int KeyRight = 16777236;
        private const int KeyDown  = 16777237;

        public static string WriteWorldPlugins(SdfWorldPlugins p)
        {
            if (p == null) return string.Empty;
            var sb = new StringBuilder();

            // Baseline runtime systems (alignment matches the legacy fixed lines).
            if (p.UserCommands)
                sb.AppendLine("    <plugin filename=\"gz-sim-user-commands-system\"     name=\"gz::sim::systems::UserCommands\"/>");
            if (p.SceneBroadcaster)
                sb.AppendLine("    <plugin filename=\"gz-sim-scene-broadcaster-system\" name=\"gz::sim::systems::SceneBroadcaster\"/>");

            // Sensor-family systems.
            if (p.Imu)
                sb.AppendLine("    <plugin filename=\"gz-sim-imu-system\" name=\"gz::sim::systems::Imu\"/>");
            if (p.Sensors)
                sb.AppendLine("    <plugin filename=\"gz-sim-sensors-system\" name=\"gz::sim::systems::Sensors\"><render_engine>ogre2</render_engine></plugin>");
            if (p.Contact)
                sb.AppendLine("    <plugin filename=\"gz-sim-contact-system\" name=\"gz::sim::systems::Contact\"/>");
            if (p.ForceTorque)
                sb.AppendLine("    <plugin filename=\"gz-sim-forcetorque-system\" name=\"gz::sim::systems::ForceTorque\"/>");
            if (p.Navsat)
                sb.AppendLine("    <plugin filename=\"gz-sim-navsat-system\" name=\"gz::sim::systems::NavSat\"/>");

            // Keyboard teleop — map arrow keys to a Twist on /cmd_vel. Pairs with
            // the KeyPublisher GUI plugin (WriteGuiKeyPublisher). Generic
            // /cmd_vel topic; the user bridges it to their spawned robot.
            if (p.TriggeredPublisher)
            {
                AppendArrowTwist(sb, KeyUp,    "linear: {x: 0.5}");
                AppendArrowTwist(sb, KeyDown,  "linear: {x: -0.5}");
                AppendArrowTwist(sb, KeyLeft,  "angular: {z: 0.5}");
                AppendArrowTwist(sb, KeyRight, "angular: {z: -0.5}");
            }

            return sb.ToString();
        }

        // The KeyPublisher GUI plugin (6-space indent, to sit inside <gui>).
        // Empty when not enabled so callers can append unconditionally.
        public static string WriteGuiKeyPublisher(SdfWorldPlugins p)
        {
            if (p == null || !p.KeyPublisher) return string.Empty;
            return "      <plugin filename=\"KeyPublisher\" name=\"Key Publisher\"/>\n";
        }

        private static void AppendArrowTwist(StringBuilder sb, int keyCode, string twistBody)
        {
            sb.AppendLine("    <plugin filename=\"gz-sim-triggered-publisher-system\" name=\"gz::sim::systems::TriggeredPublisher\">");
            sb.AppendLine("      <input type=\"gz.msgs.Int32\" topic=\"/keyboard/keypress\">");
            sb.AppendLine("        <match field=\"data\">" + keyCode + "</match>");
            sb.AppendLine("      </input>");
            sb.AppendLine("      <output type=\"gz.msgs.Twist\" topic=\"/cmd_vel\">");
            sb.AppendLine("        " + twistBody);
            sb.AppendLine("      </output>");
            sb.AppendLine("    </plugin>");
        }
    }
}
