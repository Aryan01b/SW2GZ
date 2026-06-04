/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Runs the full export pipeline to a temp directory so the user can review the
generated URDF/SDF, launch file, and pre-write report BEFORE committing to a
real export. Same code path as Sw2gzModelExporter — the preview is a faithful
copy of what Export would write, not a re-implemented summary.

Trade-off: a real Sw2gzPipeline.Run is performed against the live assembly
(mass + mesh tessellation), so preview takes the same wall-clock as export.
For the typical wizard-saved-and-export flow this is a small price for an
honest preview; double-execution on accept is acceptable for v1.
*/
#if SW_INTEROP
using System;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;
using SW2GZ.Build;
using SW2GZ.Ros2;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzModelPreviewer
    {
        public sealed class PreviewResult
        {
            public string TempDir { get; }
            public string WorkspaceDir { get; }
            public ExportMode Mode { get; }
            public string UrdfOrSdfText { get; }
            public string UrdfOrSdfFileName { get; }
            public string LaunchText { get; }
            public string LaunchFileName { get; }
            public string LogText { get; }
            public string SummaryText { get; }
            public SW2GZ.Validate.ValidationReport Report { get; }

            public PreviewResult(string tempDir, string workspaceDir, ExportMode mode,
                string urdfOrSdfText, string urdfOrSdfFileName,
                string launchText, string launchFileName,
                string logText, string summaryText,
                SW2GZ.Validate.ValidationReport report)
            {
                TempDir = tempDir;
                WorkspaceDir = workspaceDir;
                Mode = mode;
                UrdfOrSdfText = urdfOrSdfText ?? string.Empty;
                UrdfOrSdfFileName = urdfOrSdfFileName ?? string.Empty;
                LaunchText = launchText ?? string.Empty;
                LaunchFileName = launchFileName ?? string.Empty;
                LogText = logText ?? string.Empty;
                SummaryText = summaryText ?? string.Empty;
                Report = report;
            }
        }

        public static PreviewResult RunPreview(SldWorks swApp, ModelDoc2 model, Sw2gzExportConfig config)
        {
            if (swApp == null) throw new ArgumentNullException(nameof(swApp));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (config == null) throw new ArgumentNullException(nameof(config));

            string tempBase = Path.Combine(Path.GetTempPath(),
                "sw2gz_preview_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempBase);

            SW2GZ.Validate.ValidationReport report;
            try
            {
                report = Sw2gzModelExporter.RunCore(swApp, model, config, tempBase);
            }
            catch
            {
                // Failed-run cleanup: the pipeline itself rolls back its workspace,
                // but our tempBase wrapper may persist — remove it so /tmp doesn't
                // accumulate failed-preview clutter.
                try { if (Directory.Exists(tempBase)) Directory.Delete(tempBase, recursive: true); }
                catch { /* best-effort */ }
                throw;
            }

            string pkg = PackageNameSanitizer.Sanitize(config.PackageName).Value;
            string workspace = Path.Combine(tempBase, pkg + "_ws");
            string root = Path.Combine(workspace, "src", pkg);

            // Per-mode file selection. The URDF (Robot Package) and SDF (gz
            // model / world) paths use different filenames; pick the most
            // representative artifact for each.
            string urdfOrSdfPath, urdfOrSdfRel, launchPath, launchRel;
            switch (config.Mode)
            {
                case ExportMode.SdfModel:
                case ExportMode.SdfWorld:
                    urdfOrSdfPath = Path.Combine(root, "models", pkg, "model.sdf");
                    urdfOrSdfRel = "models/" + pkg + "/model.sdf";
                    launchPath = Path.Combine(root, "launch", pkg + ".launch.py");
                    launchRel = "launch/" + pkg + ".launch.py";
                    break;
                default: // RobotPackage
                    urdfOrSdfPath = Path.Combine(root, "urdf", pkg + ".urdf.xacro");
                    urdfOrSdfRel = "urdf/" + pkg + ".urdf.xacro";
                    launchPath = Path.Combine(root, "launch", "gz_sim.launch.py");
                    launchRel = "launch/gz_sim.launch.py";
                    break;
            }

            string urdfText = SafeReadAll(urdfOrSdfPath);
            string launchText = SafeReadAll(launchPath);
            string logText = SafeReadAll(Path.Combine(workspace, "sw2gz_export.log"));
            string summary = BuildSummary(config, workspace, report);

            return new PreviewResult(tempBase, workspace, config.Mode,
                urdfText, urdfOrSdfRel, launchText, launchRel, logText, summary, report);
        }

        private static string SafeReadAll(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : "(file not produced)"; }
            catch (Exception e) { return "(read failed: " + e.Message + ")"; }
        }

        private static string BuildSummary(Sw2gzExportConfig config, string workspace,
            SW2GZ.Validate.ValidationReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Mode:           " + config.Mode);
            sb.AppendLine("Package:        " + config.PackageName);
            sb.AppendLine("Author:         " + (string.IsNullOrEmpty(config.Author) ? "(unset)" : config.Author));
            sb.AppendLine("License:        " + (string.IsNullOrEmpty(config.License) ? "(unset)" : config.License));
            sb.AppendLine("Links:          " + (config.Links?.Count ?? 0));
            sb.AppendLine("Joints:         " + (config.Joints?.Count ?? 0));
            sb.AppendLine("SW up axis:     " + config.SwUpAxis);
            sb.AppendLine("SW fwd axis:    " + config.SwForwardAxis);
            sb.AppendLine();
            sb.AppendLine("Real export target: " + config.OutputFolder);
            sb.AppendLine("Preview workspace:  " + workspace);
            sb.AppendLine();
            if (report != null)
            {
                int err = 0; foreach (var _ in report.Errors) err++;
                int warn = 0; foreach (var _ in report.Warnings) warn++;
                sb.AppendLine("Warnings:       " + warn);
                sb.AppendLine("Errors:         " + err);
            }
            return sb.ToString();
        }
    }
}
#endif
