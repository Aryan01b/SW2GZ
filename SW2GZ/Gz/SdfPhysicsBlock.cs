/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Text;

namespace SW2GZ.Gz
{
    public static class SdfPhysicsBlock
    {
        public static string Default()
        {
            var sb = new StringBuilder();
            sb.AppendLine("    <physics name=\"1ms\" type=\"ignored\">");
            sb.AppendLine("      <max_step_size>0.001</max_step_size>");
            sb.AppendLine("      <real_time_factor>1.0</real_time_factor>");
            sb.AppendLine("    </physics>");
            return sb.ToString();
        }

        public static string Sun()
        {
            return
@"    <light name=""sun"" type=""directional"">
      <cast_shadows>true</cast_shadows>
      <pose>0 0 10 0 0 0</pose>
      <diffuse>0.8 0.8 0.8 1</diffuse>
      <specular>0.2 0.2 0.2 1</specular>
      <direction>-0.5 0.1 -0.9</direction>
    </light>
";
        }

        public static string GroundPlane()
        {
            return
@"    <model name=""ground_plane"">
      <static>true</static>
      <link name=""link"">
        <collision name=""collision"">
          <geometry><plane><normal>0 0 1</normal><size>100 100</size></plane></geometry>
        </collision>
        <visual name=""visual"">
          <geometry><plane><normal>0 0 1</normal><size>100 100</size></plane></geometry>
          <material><ambient>0.8 0.8 0.8 1</ambient><diffuse>0.8 0.8 0.8 1</diffuse></material>
        </visual>
      </link>
    </model>
";
        }
    }
}
