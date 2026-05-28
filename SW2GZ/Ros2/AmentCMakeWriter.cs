/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.IO;
using System.Text;

namespace SW2GZ.Ros2
{
    public class AmentCMakeWriter
    {
        private readonly string _pkgName;
        public AmentCMakeWriter(string pkgName) { _pkgName = pkgName; }

        public void Write(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var sb = new StringBuilder();
            sb.AppendLine("cmake_minimum_required(VERSION 3.8)");
            sb.AppendLine($"project({_pkgName})");
            sb.AppendLine();
            sb.AppendLine("find_package(ament_cmake REQUIRED)");
            sb.AppendLine();
            sb.AppendLine("install(DIRECTORY urdf launch config worlds meshes");
            sb.AppendLine("  DESTINATION share/${PROJECT_NAME}");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine("ament_package()");
            File.WriteAllText(Path.Combine(outputDir, "CMakeLists.txt"), sb.ToString());
        }
    }
}
