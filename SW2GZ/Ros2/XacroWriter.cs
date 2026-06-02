/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits the top-level <package>.urdf.xacro string. Includes inc/materials.xacro,
inc/ros2_control.xacro (owned by Ros2ControlWriter, T11), and inc/gz.xacro
(owned by GzPluginTags.WriteGzRos2ControlXacro, T10). This writer no longer
emits placeholder copies of those include files — the caller must invoke
the dedicated writers and Ros2ControlWriter/GzPluginTags own their inc/* file.

UrdfSerializer no longer emits empty-name <material name=""/> tags (material
names are sanitized to non-empty and the material element is only written when
MaterialName != null), so the body XML is passed through verbatim.
*/
using System;
using System.Security;
using System.Text;

namespace SW2GZ.Ros2
{
    public static class XacroWriter
    {
        public static string Write(string robotName, string urdfBodyXml)
        {
            if (string.IsNullOrWhiteSpace(robotName))
                throw new ArgumentException("robotName must not be null or whitespace.", nameof(robotName));
            if (urdfBodyXml == null)
                throw new ArgumentNullException(nameof(urdfBodyXml));

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine($"<robot name=\"{SecurityElement.Escape(robotName)}\" xmlns:xacro=\"http://www.ros.org/wiki/xacro\">");
            sb.AppendLine("  <xacro:arg name=\"prefix\" default=\"\"/>");
            sb.AppendLine("  <xacro:arg name=\"use_sim\" default=\"false\"/>");
            sb.AppendLine("  <xacro:arg name=\"use_ros2_control\" default=\"true\"/>");
            sb.AppendLine("  <xacro:include filename=\"inc/materials.xacro\"/>");
            sb.AppendLine("  <xacro:include filename=\"inc/ros2_control.xacro\"/>");
            sb.AppendLine("  <xacro:include filename=\"inc/gz.xacro\"/>");
            sb.AppendLine(urdfBodyXml);
            sb.AppendLine("</robot>");
            return sb.ToString();
        }
    }
}
