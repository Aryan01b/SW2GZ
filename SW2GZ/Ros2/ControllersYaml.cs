/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits config/controllers.yaml — the controller_manager configuration
loaded by gz_ros2_control inside the simulated robot (via T10's
inc/gz.xacro <parameters> reference). Includes a joint_state_broadcaster
and a joint_trajectory_controller bound to the package's joints.

Locked to Jazzy + Harmonic. Command interface = position; state
interfaces = position + velocity (matches T11 Ros2ControlWriter).
*/
using System;
using System.Collections.Generic;
using System.Text;

namespace SW2GZ.Ros2
{
    public sealed record ControllersInput(string PackageName, IReadOnlyList<string> JointNames);

    public static class ControllersYaml
    {
        public static string Write(ControllersInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(input.PackageName))
                throw new ArgumentException("PackageName must not be null or whitespace.", nameof(input));
            if (input.JointNames == null) throw new ArgumentNullException(nameof(input), "JointNames must not be null.");

            var sb = new StringBuilder();
            sb.AppendLine("controller_manager:");
            sb.AppendLine("  ros__parameters:");
            sb.AppendLine("    update_rate: 100");
            sb.AppendLine("    joint_state_broadcaster:");
            sb.AppendLine("      type: joint_state_broadcaster/JointStateBroadcaster");
            sb.AppendLine("    joint_trajectory_controller:");
            sb.AppendLine("      type: joint_trajectory_controller/JointTrajectoryController");
            sb.AppendLine();
            sb.AppendLine("joint_trajectory_controller:");
            sb.AppendLine("  ros__parameters:");
            sb.AppendLine("    joints:");
            foreach (var j in input.JointNames) sb.AppendLine($"      - {j}");
            sb.AppendLine("    command_interfaces:");
            sb.AppendLine("      - position");
            sb.AppendLine("    state_interfaces:");
            sb.AppendLine("      - position");
            sb.AppendLine("      - velocity");
            return sb.ToString();
        }
    }
}
