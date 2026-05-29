/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Emits CMakeLists.txt for ament_cmake. The meshes/ directory is included
in the install(DIRECTORY ...) line only when hasMeshes is true — fixes
the v1.0 minor bug where colcon build failed with "directory not found"
on packages exported without meshes.
*/
using System;
using System.Collections.Generic;
using System.Text;

namespace SW2GZ.Ros2
{
    public sealed record AmentCMakeInput(string PackageName, bool hasMeshes);

    public static class AmentCMakeWriter
    {
        public static string Write(AmentCMakeInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(input.PackageName))
                throw new ArgumentException("PackageName must not be null or whitespace.", nameof(input));

            var dirs = new List<string> { "urdf", "launch", "config", "worlds" };
            if (input.hasMeshes) dirs.Add("meshes");

            var sb = new StringBuilder();
            sb.AppendLine("cmake_minimum_required(VERSION 3.8)");
            sb.AppendLine($"project({input.PackageName})");
            sb.AppendLine();
            sb.AppendLine("find_package(ament_cmake REQUIRED)");
            sb.AppendLine();
            sb.AppendLine($"install(DIRECTORY {string.Join(" ", dirs)}");
            sb.AppendLine("  DESTINATION share/${PROJECT_NAME}");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("ament_package()");
            return sb.ToString();
        }
    }
}
