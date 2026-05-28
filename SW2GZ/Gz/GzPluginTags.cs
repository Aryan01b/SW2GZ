/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Text;
using SW2GZ.Ros2;

namespace SW2GZ.Gz
{
    public static class GzPluginTags
    {
        public static string WorldSystemBlock(TargetProfile profile)
        {
            string lib = TargetProfile.SimPluginLib[profile.Gz];
            string ns  = profile.Gz == GzVersion.Fortress ? "ignition::gazebo" : "gz::sim";
            var sb = new StringBuilder();
            sb.AppendLine($"    <plugin filename=\"{lib}-physics-system\" name=\"{ns}::systems::Physics\"/>");
            sb.AppendLine($"    <plugin filename=\"{lib}-user-commands-system\" name=\"{ns}::systems::UserCommands\"/>");
            sb.AppendLine($"    <plugin filename=\"{lib}-scene-broadcaster-system\" name=\"{ns}::systems::SceneBroadcaster\"/>");
            sb.AppendLine($"    <plugin filename=\"{lib}-sensors-system\" name=\"{ns}::systems::Sensors\"/>");
            return sb.ToString();
        }

        public static string Ros2ControlPluginBlock(TargetProfile profile, string controllersYaml)
        {
            string lib = TargetProfile.Ros2ControlPlugin[profile.Gz];
            string ns  = lib.Replace("-system", "").Replace("_", "::");
            var sb = new StringBuilder();
            sb.AppendLine("  <gazebo>");
            sb.AppendLine($"    <plugin filename=\"{lib}\" name=\"{ns}::system\">");
            sb.AppendLine($"      <parameters>{controllersYaml}</parameters>");
            sb.AppendLine("    </plugin>");
            sb.AppendLine("  </gazebo>");
            return sb.ToString();
        }
    }
}
