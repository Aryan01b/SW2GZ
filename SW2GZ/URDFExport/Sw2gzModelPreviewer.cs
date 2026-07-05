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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;
using SW2GZ.SwSurface;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzModelPreviewer
    {
        public sealed class PreviewResult
        {
            public string TempDir { get; }
            public string WorkspaceDir { get; }
            public string MeshesDir { get; }
            public ExportMode Mode { get; }
            public string UrdfOrSdfText { get; }
            public string UrdfOrSdfFileName { get; }
            public string LaunchText { get; }
            public string LaunchFileName { get; }
            public string LogText { get; }
            public string SummaryText { get; }
            public string TfTreeText { get; }
            public SW2GZ.Validate.ValidationReport Report { get; }
            /// Reads live joint values from SW each call. PreviewServer hands
            /// this to /joint_states. Returns empty dict if unavailable.
            public Func<IReadOnlyDictionary<string, double>> JointSampler { get; }

            public PreviewResult(string tempDir, string workspaceDir, string meshesDir, ExportMode mode,
                string urdfOrSdfText, string urdfOrSdfFileName,
                string launchText, string launchFileName,
                string logText, string summaryText, string tfTreeText,
                SW2GZ.Validate.ValidationReport report,
                Func<IReadOnlyDictionary<string, double>> jointSampler)
            {
                TempDir = tempDir;
                WorkspaceDir = workspaceDir;
                MeshesDir = meshesDir ?? string.Empty;
                Mode = mode;
                UrdfOrSdfText = urdfOrSdfText ?? string.Empty;
                UrdfOrSdfFileName = urdfOrSdfFileName ?? string.Empty;
                LaunchText = launchText ?? string.Empty;
                LaunchFileName = launchFileName ?? string.Empty;
                LogText = logText ?? string.Empty;
                SummaryText = summaryText ?? string.Empty;
                TfTreeText = tfTreeText ?? string.Empty;
                Report = report;
                JointSampler = jointSampler ?? (() => new Dictionary<string, double>());
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

            // Robot mode's exporter always bakes the SW→ROS rotation directly
            // into base_link's mesh (matching Asset/World mode), so preview
            // needs no special-case override — it renders the exact same
            // already-Z-up URDF a real export produces.
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
            string urdfOrSdfPath, urdfOrSdfRel, launchPath, launchRel, meshesDir;
            switch (config.Mode)
            {
                case ExportMode.SdfModel:
                case ExportMode.SdfWorld:
                    urdfOrSdfPath = Path.Combine(root, "models", pkg, "model.sdf");
                    urdfOrSdfRel = "models/" + pkg + "/model.sdf";
                    launchPath = Path.Combine(root, "launch", pkg + ".launch.py");
                    launchRel = "launch/" + pkg + ".launch.py";
                    meshesDir = Path.Combine(root, "models", pkg, "meshes");
                    break;
                default: // RobotPackage
                    urdfOrSdfPath = Path.Combine(root, "urdf", pkg + ".urdf.xacro");
                    urdfOrSdfRel = "urdf/" + pkg + ".urdf.xacro";
                    launchPath = Path.Combine(root, "launch", "gz_sim.launch.py");
                    launchRel = "launch/gz_sim.launch.py";
                    meshesDir = Path.Combine(root, "meshes");
                    break;
            }

            string urdfText = SafeReadAll(urdfOrSdfPath);
            string launchText = SafeReadAll(launchPath);
            string logText = SafeReadAll(Path.Combine(workspace, "sw2gz_export.log"));
            string summary = BuildSummary(config, workspace, report);
            string tfTree = config.Mode == ExportMode.RobotPackage
                ? TfTreeFormatter.FormatUrdf(urdfText)
                : TfTreeFormatter.FormatSdf(urdfText);

            // Build the live joint sampler so the PreviewServer's /joint_states
            // endpoint streams real SW mate angles to the browser. Failures here
            // are non-fatal: the preview just renders with all joints at zero.
            // Robot live joint sync removed for the v2 rebuild — preview renders
            // with all joints at zero (World/Asset are static anyway).
            Func<IReadOnlyDictionary<string, double>> sampler = () => new Dictionary<string, double>();

            return new PreviewResult(tempBase, workspace, meshesDir, config.Mode,
                urdfText, urdfOrSdfRel, launchText, launchRel, logText, summary, tfTree, report,
                sampler);
        }

        // Minimal world preview. World mode has no URDF/robot — it's a set of
        // static CAD meshes. Rather than build a second three.js viewer, we run
        // the real world export to a temp dir (meshes recentered, with normals)
        // and synthesize a throwaway URDF of fixed links (one per mesh) so the
        // EXISTING robot viewer renders the scene unchanged. The SW→ROS rotation
        // rides each fixed joint's rpy (the browser scene is Z-up; the assembly
        // is Y-up), mirroring the robot preview's EmitWorldLink trick.
        public static PreviewResult RunWorldPreview(SldWorks swApp, ModelDoc2 model, Sw2gzExportConfig config)
        {
            if (swApp == null) throw new ArgumentNullException(nameof(swApp));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (config == null) throw new ArgumentNullException(nameof(config));

            string tempBase = Path.Combine(Path.GetTempPath(),
                "sw2gz_wpreview_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempBase);

            SW2GZ.Validate.ValidationReport report;
            try
            {
                var tess = new SolidWorksMeshTessellator(swApp, (AssemblyDoc)model);
                // rpy 0 → meshes stay assembly-frame (recentered, unrotated); the
                // preview applies the SW→ROS rotation on the URDF joints instead.
                report = Sw2gzWorldExporter.Export(tess, config, tempBase, 0, 0, 0);
            }
            catch
            {
                try { if (Directory.Exists(tempBase)) Directory.Delete(tempBase, recursive: true); }
                catch { /* best-effort */ }
                throw;
            }

            string pkg = PackageNameSanitizer.Sanitize(config.PackageName).Value;
            string root = Path.Combine(tempBase, pkg);
            string meshesDir = Path.Combine(root, "meshes");
            string[] daeFiles = Directory.Exists(meshesDir)
                ? Directory.GetFiles(meshesDir, "*.dae").Select(Path.GetFileName).OrderBy(f => f).ToArray()
                : Array.Empty<string>();

            (double r, double p, double y) = new CoordinateConvention(
                SwToRosRotation.Build(config.SwUpAxis, config.SwForwardAxis), LengthScale: 1.0)
                .SwToRos.ToRpy();

            string urdf = BuildWorldPreviewUrdf(pkg, daeFiles, r, p, y);
            string sdfText = SafeReadAll(Path.Combine(root, pkg + ".sdf"));
            string summary = BuildSummary(config, root, report) +
                System.Environment.NewLine + "World models: " + daeFiles.Length;

            return new PreviewResult(tempBase, root, meshesDir, ExportMode.SdfWorld,
                urdf, "robot.urdf", sdfText, pkg + ".sdf", "", summary, "", report,
                () => new Dictionary<string, double>());
        }

        // Asset preview — run the asset export to temp, then render its single
        // mesh in the existing viewer via a 1-link URDF. The asset exporter bakes
        // the SW->ROS rotation into the verts (Z-up already), so the URDF needs
        // no rpy.
        public static PreviewResult RunAssetPreview(SldWorks swApp, ModelDoc2 model, Sw2gzExportConfig config)
        {
            if (swApp == null) throw new ArgumentNullException(nameof(swApp));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (config == null) throw new ArgumentNullException(nameof(config));

            string tempBase = Path.Combine(Path.GetTempPath(),
                "sw2gz_apreview_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempBase);

            SW2GZ.Validate.ValidationReport report;
            try
            {
                bool isPart = model.GetType() ==
                    (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART;
                var tess = isPart
                    ? new SolidWorksMeshTessellator(swApp, (PartDoc)model)
                    : new SolidWorksMeshTessellator(swApp, (AssemblyDoc)model);
                if (isPart && string.IsNullOrWhiteSpace(config.AssetBodyPart))
                    config.AssetBodyPart = "part";
                var rot = SwToRosRotation.Build(config.SwUpAxis, config.SwForwardAxis);
                report = Sw2gzAssetExporter.Export(tess, config, tempBase, rot);
            }
            catch
            {
                try { if (Directory.Exists(tempBase)) Directory.Delete(tempBase, recursive: true); }
                catch { /* best-effort */ }
                throw;
            }

            string name = PackageNameSanitizer.Sanitize(config.PackageName).Value;
            string root = Path.Combine(tempBase, name);
            string meshesDir = Path.Combine(root, "meshes");
            string[] daeFiles = { name + ".dae" };

            string urdf = BuildWorldPreviewUrdf(name, daeFiles, 0, 0, 0);
            string sdfText = SafeReadAll(Path.Combine(root, "model.sdf"));
            string summary = BuildSummary(config, root, report) +
                System.Environment.NewLine + "Asset part: " + config.AssetBodyPart;

            return new PreviewResult(tempBase, root, meshesDir, ExportMode.SdfModel,
                urdf, "robot.urdf", sdfText, "model.sdf", "", summary, "", report,
                () => new Dictionary<string, double>());
        }

        // Throwaway URDF: base_link + one fixed-jointed child link per world mesh.
        // Placement is already baked + recentered into the verts, so each origin
        // is xyz=0; the shared rpy rotates SW's up onto ROS Z for the viewport.
        private static string BuildWorldPreviewUrdf(string pkg, IReadOnlyList<string> daeFiles,
            double roll, double pitch, double yaw)
        {
            string rpy = roll.ToString("0.######", CultureInfo.InvariantCulture) + " " +
                         pitch.ToString("0.######", CultureInfo.InvariantCulture) + " " +
                         yaw.ToString("0.######", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<robot name=\"" + pkg + "\">");
            sb.AppendLine("  <link name=\"base_link\"/>");
            for (int i = 0; i < daeFiles.Count; i++)
            {
                string link = "model_" + i;
                sb.AppendLine("  <link name=\"" + link + "\">");
                sb.AppendLine("    <visual><geometry>");
                sb.AppendLine("      <mesh filename=\"package://" + pkg + "/meshes/" + daeFiles[i] + "\"/>");
                sb.AppendLine("    </geometry></visual>");
                sb.AppendLine("  </link>");
                sb.AppendLine("  <joint name=\"" + link + "_fixed\" type=\"fixed\">");
                sb.AppendLine("    <parent link=\"base_link\"/><child link=\"" + link + "\"/>");
                sb.AppendLine("    <origin xyz=\"0 0 0\" rpy=\"" + rpy + "\"/>");
                sb.AppendLine("  </joint>");
            }
            sb.AppendLine("</robot>");
            return sb.ToString();
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
