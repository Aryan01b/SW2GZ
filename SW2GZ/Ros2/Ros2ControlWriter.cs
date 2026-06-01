/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits inc/ros2_control.xacro — the <ros2_control> declarations (hardware
plugin + per-joint interfaces) plus the <parameters> reference to the
controllers.yaml. The <gazebo><plugin gz_ros2_control-system> block lives
in inc/gz.xacro and is owned by GzPluginTags.WriteGzRos2ControlXacro.

Fixes v1.0 export bug 3 ($(arg pkg) was referenced but never declared,
breaking xacro parsing). The package name is interpolated directly into
$(find ...) at write time.

Locked to Harmonic — uses gz_ros2_control/GazeboSimSystem.
*/
using System;
using System.Collections.Generic;
using System.Security;
using System.Text;

namespace SW2GZ.Ros2
{
    public static class Ros2ControlWriter
    {
        public static string Write(string packageName, IReadOnlyList<string> jointNames)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("packageName must not be null or whitespace.", nameof(packageName));
            if (jointNames == null)
                throw new ArgumentNullException(nameof(jointNames));

            const string HardwarePlugin = "gz_ros2_control/GazeboSimSystem";

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<robot xmlns:xacro=\"http://www.ros.org/wiki/xacro\">");
            sb.AppendLine("  <ros2_control name=\"GzSystem\" type=\"system\">");
            sb.AppendLine("    <hardware>");
            sb.AppendLine($"      <plugin>{HardwarePlugin}</plugin>");
            sb.AppendLine($"      <parameters>$(find {SecurityElement.Escape(packageName)})/config/controllers.yaml</parameters>");
            sb.AppendLine("    </hardware>");
            foreach (var j in jointNames)
            {
                sb.AppendLine($"    <joint name=\"{SecurityElement.Escape(j)}\">");
                sb.AppendLine("      <command_interface name=\"position\"/>");
                sb.AppendLine("      <state_interface name=\"position\"/>");
                sb.AppendLine("      <state_interface name=\"velocity\"/>");
                sb.AppendLine("      <state_interface name=\"effort\"/>");
                sb.AppendLine("    </joint>");
            }
            sb.AppendLine("  </ros2_control>");
            sb.AppendLine("</robot>");
            return sb.ToString();
        }
    }
}
