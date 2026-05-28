/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.IO;
using System.Text;

namespace SW2GZ.Ros2
{
    public class XacroWriter
    {
        private readonly string _robotName;
        private readonly string _urdfBodyXml;

        public XacroWriter(string robotName, string urdfBodyXml)
        {
            _robotName = robotName;
            _urdfBodyXml = urdfBodyXml;
        }

        public void Write(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            Directory.CreateDirectory(Path.Combine(outputDir, "inc"));

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine($"<robot name=\"{_robotName}\" xmlns:xacro=\"http://www.ros.org/wiki/xacro\">");
            sb.AppendLine("  <xacro:arg name=\"prefix\" default=\"\"/>");
            sb.AppendLine("  <xacro:arg name=\"use_sim\" default=\"false\"/>");
            sb.AppendLine("  <xacro:arg name=\"use_ros2_control\" default=\"true\"/>");
            sb.AppendLine("  <xacro:include filename=\"inc/materials.xacro\"/>");
            sb.AppendLine("  <xacro:include filename=\"inc/ros2_control.xacro\"/>");
            sb.AppendLine("  <xacro:include filename=\"inc/gz.xacro\"/>");
            sb.AppendLine(_urdfBodyXml);
            sb.AppendLine("</robot>");
            File.WriteAllText(Path.Combine(outputDir, $"{_robotName}.urdf.xacro"), sb.ToString());

            // Placeholder content for include files; Ros2ControlWriter and GzPluginTags
            // overwrite ros2_control.xacro and gz.xacro respectively in Phase 4.
            File.WriteAllText(Path.Combine(outputDir, "inc", "materials.xacro"),
                "<?xml version=\"1.0\"?>\n<robot xmlns:xacro=\"http://www.ros.org/wiki/xacro\">\n  <!-- Named materials populated by SW2GZ. -->\n</robot>\n");
            File.WriteAllText(Path.Combine(outputDir, "inc", "ros2_control.xacro"),
                "<?xml version=\"1.0\"?>\n<robot xmlns:xacro=\"http://www.ros.org/wiki/xacro\">\n  <!-- ros2_control tags populated by Ros2ControlWriter. -->\n</robot>\n");
            File.WriteAllText(Path.Combine(outputDir, "inc", "gz.xacro"),
                "<?xml version=\"1.0\"?>\n<robot xmlns:xacro=\"http://www.ros.org/wiki/xacro\">\n  <!-- Gz plugin tags populated by GzPluginTags. -->\n</robot>\n");
        }
    }
}
