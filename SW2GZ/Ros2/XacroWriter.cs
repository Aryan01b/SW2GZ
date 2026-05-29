/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits the top-level <package>.urdf.xacro string. Includes inc/materials.xacro,
inc/ros2_control.xacro (owned by Ros2ControlWriter, T11), and inc/gz.xacro
(owned by GzPluginTags.WriteGzRos2ControlXacro, T10). This writer no longer
emits placeholder copies of those include files — the caller must invoke
the dedicated writers and Ros2ControlWriter/GzPluginTags own their inc/* file.

v1 also passed through empty-name <material name=""/> tags which produced
xacro warnings. T15 strips any such tag from the urdfBodyXml before emission.
*/
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace SW2GZ.Ros2
{
    public static class XacroWriter
    {
        // Matches <material name="" .../> and <material name="   " ...> (self-closing or open)
        private static readonly Regex EmptyNameMaterial = new Regex(
            @"<material\s+name=""\s*""\s*/>(\s*\r?\n)?",
            RegexOptions.Compiled);

        public static string Write(string robotName, string urdfBodyXml)
        {
            if (string.IsNullOrWhiteSpace(robotName))
                throw new ArgumentException("robotName must not be null or whitespace.", nameof(robotName));
            if (urdfBodyXml == null)
                throw new ArgumentNullException(nameof(urdfBodyXml));

            string filteredBody = EmptyNameMaterial.Replace(urdfBodyXml, string.Empty);

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine($"<robot name=\"{robotName}\" xmlns:xacro=\"http://www.ros.org/wiki/xacro\">");
            sb.AppendLine("  <xacro:arg name=\"prefix\" default=\"\"/>");
            sb.AppendLine("  <xacro:arg name=\"use_sim\" default=\"false\"/>");
            sb.AppendLine("  <xacro:arg name=\"use_ros2_control\" default=\"true\"/>");
            sb.AppendLine("  <xacro:include filename=\"inc/materials.xacro\"/>");
            sb.AppendLine("  <xacro:include filename=\"inc/ros2_control.xacro\"/>");
            sb.AppendLine("  <xacro:include filename=\"inc/gz.xacro\"/>");
            sb.AppendLine(filteredBody);
            sb.AppendLine("</robot>");
            return sb.ToString();
        }
    }
}
