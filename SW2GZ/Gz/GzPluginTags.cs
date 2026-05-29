/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits the <gazebo><plugin .../></gazebo> include block for the top-level
xacro so gz sim instantiates gz_ros2_control inside the simulated robot.
Fixes v1.0 export bugs 1 (gz.xacro was empty placeholder) and 8 (wrong
plugin class name).
*/
using System;
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

        /// <summary>
        /// Writes the gz.xacro include file content that wires gz_ros2_control
        /// into the simulated robot.  Fixes bugs 1 (was empty placeholder) and
        /// 8 (wrong plugin class name).
        /// </summary>
        /// <param name="packageName">ROS 2 package name used in the $(find ...) macro.</param>
        public static string WriteGzRos2ControlXacro(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("packageName must not be null or whitespace.", nameof(packageName));

            return
$@"<?xml version=""1.0""?>
<robot xmlns:xacro=""http://www.ros.org/wiki/xacro"">
  <gazebo>
    <plugin filename=""gz_ros2_control-system""
            name=""gz_ros2_control::GazeboSimROS2ControlPlugin"">
      <parameters>$(find {packageName})/config/controllers.yaml</parameters>
    </plugin>
  </gazebo>
</robot>
";
        }
    }
}
