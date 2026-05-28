/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.IO;

namespace SW2GZ.Ros2
{
    public class RvizConfigWriter
    {
        public void Write(string outputDir, string fileName = "rviz.rviz")
        {
            Directory.CreateDirectory(outputDir);
            string yaml =
@"Panels:
  - Class: rviz_common/Displays
    Name: Displays
Visualization Manager:
  Class: """"
  Displays:
    - Class: rviz_default_plugins/Grid
      Enabled: true
      Name: Grid
    - Alpha: 1
      Class: rviz_default_plugins/RobotModel
      Description Source: Topic
      Description Topic:
        Value: /robot_description
      Enabled: true
      Name: RobotModel
    - Class: rviz_default_plugins/TF
      Enabled: true
      Name: TF
  Global Options:
    Fixed Frame: base_link
    Frame Rate: 30
  Tools:
    - Class: rviz_default_plugins/MoveCamera
    - Class: rviz_default_plugins/Select
  Value: true
";
            File.WriteAllText(Path.Combine(outputDir, fileName), yaml);
        }
    }
}
