/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Builds tiny on-disk packages for OutputValidator integration tests.
*/
using System;
using System.IO;

namespace SW2GZ.Validate.Tests
{
    internal static class SyntheticPackage
    {
        public static string GoodMinimal(string packageName = "good_pkg")
        {
            var dir = Path.Combine(Path.GetTempPath(), $"sw2gz_pkg_{Guid.NewGuid()}");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "urdf"));
            Directory.CreateDirectory(Path.Combine(dir, "meshes"));

            File.WriteAllText(Path.Combine(dir, "meshes", "base_link.dae"), "<COLLADA/>");

            string urdf =
                "<?xml version=\"1.0\"?>\n" +
                $"<robot name=\"{packageName}\">\n" +
                "  <link name=\"base_link\">\n" +
                "    <visual><geometry><mesh filename=\"package://" + packageName + "/meshes/base_link.dae\"/></geometry></visual>\n" +
                "  </link>\n" +
                "  <gazebo>\n" +
                "    <plugin filename=\"gz-sim-physics-system\" name=\"gz::sim::systems::Physics\"/>\n" +
                "    <plugin filename=\"gz_ros2_control-system\" name=\"gz_ros2_control::GazeboSimROS2ControlPlugin\"/>\n" +
                "  </gazebo>\n" +
                "</robot>\n";
            File.WriteAllText(Path.Combine(dir, "urdf", $"{packageName}.urdf.xacro"), urdf);
            return dir;
        }
    }
}
