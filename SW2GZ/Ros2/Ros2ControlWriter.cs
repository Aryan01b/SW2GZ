/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SW2GZ.Ros2
{
    public class Ros2ControlWriter
    {
        public class Input
        {
            public IReadOnlyList<string> JointNames { get; set; }
            public TargetProfile Profile { get; set; }
            public string ControllersYamlFileName { get; set; } = "controllers.yaml";
        }

        private readonly Input _in;
        public Ros2ControlWriter(Input input) { _in = input; }

        public void Write(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            Directory.CreateDirectory(Path.Combine(outputDir, "inc"));

            string hwPlugin = _in.Profile.Gz == GzVersion.Fortress
                ? "ign_ros2_control/IgnitionSystem"
                : "gz_ros2_control/GazeboSimSystem";
            string sysPluginLib = TargetProfile.Ros2ControlPlugin[_in.Profile.Gz];

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<robot xmlns:xacro=\"http://www.ros.org/wiki/xacro\">");
            sb.AppendLine("  <ros2_control name=\"GzSystem\" type=\"system\">");
            sb.AppendLine("    <hardware>");
            sb.AppendLine($"      <plugin>{hwPlugin}</plugin>");
            sb.AppendLine("    </hardware>");
            foreach (var j in _in.JointNames)
            {
                sb.AppendLine($"    <joint name=\"{j}\">");
                sb.AppendLine("      <command_interface name=\"position\"/>");
                sb.AppendLine("      <state_interface name=\"position\"/>");
                sb.AppendLine("      <state_interface name=\"velocity\"/>");
                sb.AppendLine("      <state_interface name=\"effort\"/>");
                sb.AppendLine("    </joint>");
            }
            sb.AppendLine("  </ros2_control>");
            sb.AppendLine("  <gazebo>");
            sb.AppendLine($"    <plugin filename=\"{sysPluginLib}\" name=\"{sysPluginLib.Replace("-", "::")}\">");
            sb.AppendLine($"      <parameters>$(find-pkg-share $(arg pkg))/config/{_in.ControllersYamlFileName}</parameters>");
            sb.AppendLine("    </plugin>");
            sb.AppendLine("  </gazebo>");
            sb.AppendLine("</robot>");
            File.WriteAllText(Path.Combine(outputDir, "inc", "ros2_control.xacro"), sb.ToString());

            var yaml = new StringBuilder();
            yaml.AppendLine("controller_manager:");
            yaml.AppendLine("  ros__parameters:");
            yaml.AppendLine("    update_rate: 100");
            yaml.AppendLine("    joint_state_broadcaster:");
            yaml.AppendLine("      type: joint_state_broadcaster/JointStateBroadcaster");
            yaml.AppendLine("    joint_trajectory_controller:");
            yaml.AppendLine("      type: joint_trajectory_controller/JointTrajectoryController");
            yaml.AppendLine();
            yaml.AppendLine("joint_trajectory_controller:");
            yaml.AppendLine("  ros__parameters:");
            yaml.AppendLine("    joints:");
            foreach (var j in _in.JointNames) yaml.AppendLine($"      - {j}");
            yaml.AppendLine("    command_interfaces:");
            yaml.AppendLine("      - position");
            yaml.AppendLine("    state_interfaces:");
            yaml.AppendLine("      - position");
            yaml.AppendLine("      - velocity");
            File.WriteAllText(Path.Combine(outputDir, _in.ControllersYamlFileName), yaml.ToString());
        }
    }
}
