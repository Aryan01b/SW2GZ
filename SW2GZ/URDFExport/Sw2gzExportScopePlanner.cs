/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure (COM-free) planner that turns a Sw2gzDoc + a chosen output folder /
package name into a human-readable "what will be exported" scope summary:
mode label, link / joint counts, target workspace path, and a flat list of
relative output paths that mirror what Sw2gzPipeline.Run actually writes.

Drives the wizard's Scope page (Sw2gzExportScopePage) without coupling it to
the pipeline implementation, and is source-linked into the test project for
schema-locking tests so changes here are caught by unit tests rather than
needing a live SolidWorks run.
*/
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.Build.Model;

namespace SW2GZ.URDFExport
{
    public sealed class ExportScope
    {
        public string ModeLabel { get; set; } = string.Empty;
        public int LinkCount { get; set; }
        public int JointCount { get; set; }
        public string PackageName { get; set; } = string.Empty;
        public string WorkspaceRoot { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new List<string>();
    }

    public static class Sw2gzExportScopePlanner
    {
        /// Plan the export scope. outputFolder / rawPkgName are the user's
        /// meta-page values; this method sanitises pkgName via
        /// PackageNameSanitizer for tree-path consistency with the pipeline.
        public static ExportScope Plan(Sw2gzDoc doc, string outputFolder, string rawPkgName)
        {
            var scope = new ExportScope();
            if (doc == null) return scope;

            string pkg = PackageNameSanitizer.Sanitize(rawPkgName ?? "package").Value;
            scope.PackageName = pkg;

            switch (doc.Mode)
            {
                case Sw2gzMode.Robot: PlanRobot(doc, outputFolder, pkg, scope); break;
                case Sw2gzMode.World: PlanWorld(doc, outputFolder, pkg, scope); break;
                case Sw2gzMode.Asset: PlanAsset(doc, outputFolder, pkg, scope); break;
            }
            return scope;
        }

        private static void PlanRobot(Sw2gzDoc doc, string outputFolder, string pkg, ExportScope scope)
        {
            scope.ModeLabel  = "Robot package (URDF/Xacro)";
            scope.LinkCount  = doc.Robot?.Links?.Count ?? 0;
            scope.JointCount = doc.Robot?.Joints?.Count ?? 0;
            scope.WorkspaceRoot = Combine(outputFolder, pkg + "_ws");

            string src = "src/" + pkg + "/";
            scope.Files.Add(pkg + "_ws/");
            scope.Files.Add(src);
            scope.Files.Add(src + "package.xml");
            scope.Files.Add(src + "CMakeLists.txt");
            scope.Files.Add(src + "README.md");
            scope.Files.Add(src + "urdf/" + pkg + ".urdf.xacro");
            scope.Files.Add(src + "launch/gz_sim.launch.py");
            scope.Files.Add(src + "launch/rsp.launch.py");
            scope.Files.Add(src + "config/controllers.yaml");
            scope.Files.Add(src + "rviz/" + pkg + ".rviz");
            if (scope.LinkCount > 0)
                scope.Files.Add(src + "meshes/  (" + scope.LinkCount + " STL)");
        }

        private static void PlanWorld(Sw2gzDoc doc, string outputFolder, string pkg, ExportScope scope)
        {
            scope.ModeLabel = "Gz world (SDF world)";
            int assets      = doc.World?.Assets?.Count ?? 0;
            scope.LinkCount = assets;
            scope.WorkspaceRoot = Combine(outputFolder, pkg);

            scope.Files.Add(pkg + "/");
            scope.Files.Add(pkg + "/worlds/" + pkg + ".sdf");
            scope.Files.Add(pkg + "/launch/world.launch.py");
            if (!string.IsNullOrEmpty(doc.World?.Ground))
                scope.Files.Add(pkg + "/models/ground/  (from " + doc.World.Ground + ")");
            if (assets > 0)
                scope.Files.Add(pkg + "/models/  (" + assets + " assets)");
        }

        private static void PlanAsset(Sw2gzDoc doc, string outputFolder, string pkg, ExportScope scope)
        {
            scope.ModeLabel = "Gz asset (SDF model)";
            scope.WorkspaceRoot = Combine(outputFolder, pkg);
            scope.Files.Add(pkg + "/");
            scope.Files.Add(pkg + "/model.config");
            scope.Files.Add(pkg + "/model.sdf");
            if (!string.IsNullOrEmpty(doc.Asset?.BodyPart))
                scope.Files.Add(pkg + "/meshes/  (from " + doc.Asset.BodyPart + ")");
        }

        private static string Combine(string folder, string child)
        {
            if (string.IsNullOrEmpty(folder)) return child;
            char sep = folder.Contains("/") ? '/' : '\\';
            return folder.TrimEnd('/', '\\') + sep + child;
        }
    }
}
