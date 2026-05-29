/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using System.IO;
using SW2GZ.Gz;
using SW2GZ.Ros2;

namespace SW2GZ.URDFExport
{
    public class Ros2Package
    {
        public class Options
        {
            public string PackageName { get; set; }
            public string Maintainer { get; set; } = "TODO";
            public string MaintainerEmail { get; set; } = "TODO@example.com";
            public string License { get; set; } = "Apache-2.0";
            public IReadOnlyList<string> JointNames { get; set; }
            public TargetProfile Profile { get; set; }
            public string UrdfBodyXml { get; set; }
        }

        private readonly Options _opt;
        public Ros2Package(Options opt) { _opt = opt; }

        public void Write(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            foreach (var d in new[] { "urdf", "urdf/inc", "config", "launch", "worlds", "meshes/visual", "meshes/collision" })
                Directory.CreateDirectory(Path.Combine(outputDir, d));

            new PackageXmlV3Writer(new PackageXmlV3Writer.Input
            {
                PackageName = _opt.PackageName,
                Maintainer = _opt.Maintainer,
                MaintainerEmail = _opt.MaintainerEmail,
                License = _opt.License,
                Profile = _opt.Profile,
            }).Write(outputDir);

            new AmentCMakeWriter(_opt.PackageName).Write(outputDir);

            new XacroWriter(_opt.PackageName, _opt.UrdfBodyXml).Write(Path.Combine(outputDir, "urdf"));

            string ros2CtrlXacro = Ros2ControlWriter.Write(_opt.PackageName, _opt.JointNames);
            File.WriteAllText(Path.Combine(outputDir, "urdf", "inc", "ros2_control.xacro"), ros2CtrlXacro);

            // TODO(T18): replace with dedicated ControllersYaml writer.
            // Inline placeholder so config/controllers.yaml exists for launch files.
            var yaml = new System.Text.StringBuilder();
            yaml.AppendLine("controller_manager:");
            yaml.AppendLine("  ros__parameters:");
            yaml.AppendLine("    update_rate: 100");
            yaml.AppendLine("    joint_state_broadcaster:");
            yaml.AppendLine("      type: joint_state_broadcaster/JointStateBroadcaster");
            yaml.AppendLine("    joint_trajectory_controller:");
            yaml.AppendLine("      type: joint_trajectory_controller/JointTrajectoryController");
            yaml.AppendLine();
            yaml.AppendLine("joint_trajectory_controller:");
            yaml.AppendLine("  ros__parameters:");
            yaml.AppendLine("    joints:");
            foreach (var j in _opt.JointNames) yaml.AppendLine($"      - {j}");
            yaml.AppendLine("    command_interfaces:");
            yaml.AppendLine("      - position");
            yaml.AppendLine("    state_interfaces:");
            yaml.AppendLine("      - position");
            yaml.AppendLine("      - velocity");
            File.WriteAllText(Path.Combine(outputDir, "config", "controllers.yaml"), yaml.ToString());

            new RvizConfigWriter().Write(Path.Combine(outputDir, "config"), "rviz.rviz");
            new RosGzBridgeYaml().Write(Path.Combine(outputDir, "config"), "ros_gz_bridge.yaml");

            new LaunchPyWriter(new LaunchPyWriter.Input
            {
                PackageName = _opt.PackageName,
                XacroFileName = _opt.PackageName + ".urdf.xacro",
                WorldFileName = "empty.sdf",
                Profile = _opt.Profile,
            }).Write(Path.Combine(outputDir, "launch"));

            string worldSdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            File.WriteAllText(Path.Combine(outputDir, "worlds", "empty.sdf"), worldSdf);

            new ReadmeWriter(_opt.PackageName, _opt.Profile).Write(outputDir);

            File.WriteAllText(Path.Combine(outputDir, ".gitignore"),
                "build/\ninstall/\nlog/\n*.user\n");
        }
    }
}
