/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits the <gazebo><plugin .../></gazebo> include block for the top-level
xacro so gz sim instantiates gz_ros2_control inside the simulated robot.
Fixes v1.0 export bugs 1 (gz.xacro was empty placeholder) and 8 (wrong
plugin class name).
*/
using System;
using System.Text;

namespace SW2GZ.Gz
{
    public static class GzPluginTags
    {
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
