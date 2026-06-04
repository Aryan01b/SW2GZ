/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Task 29: Sw2gzPipeline — top-level orchestrator that stitches together all four
layers built in Phases 0-5:
  1. SwSurface   — abstract SW I/O (IMassProperties, IAssemblyWalker, IMeshTessellator)
  2. Build       — link / joint POCOs + aggregators
  3. Write       — DaeWriter, StlWriter, Ros2Package file tree
  4. Validate    — OutputValidator, ValidationReport

Pre-export failures (MaterialMissingException, etc.) propagate before any
output directory is created. Post-write, ValidationReport is returned to the
caller for display.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Gz;
using SW2GZ.Math;
using SW2GZ.Ros2;
using SW2GZ.SwSurface;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.Write.Mesh;
using SW2GZ.Write.Urdf;

namespace SW2GZ.URDFExport
{
    public sealed class Sw2gzPipeline
    {
        private readonly IMassProperties _mass;
        private readonly IAssemblyWalker _walker;
        private readonly IMeshTessellator _tess;
        private readonly IAppearanceSource _appearances;

        // P5 — full ctor: IAppearanceSource is the 4th SW boundary service.
        public Sw2gzPipeline(IMassProperties mass, IAssemblyWalker walker, IMeshTessellator tess, IAppearanceSource appearances)
        {
            _mass        = mass        ?? throw new ArgumentNullException(nameof(mass));
            _walker      = walker      ?? throw new ArgumentNullException(nameof(walker));
            _tess        = tess        ?? throw new ArgumentNullException(nameof(tess));
            _appearances = appearances ?? throw new ArgumentNullException(nameof(appearances));
        }

        // Back-compat 3-arg ctor — defaults to the no-op DefaultAppearanceSource
        // so callers that haven't been updated still produce the same (no-material)
        // output as before P5.
        public Sw2gzPipeline(IMassProperties mass, IAssemblyWalker walker, IMeshTessellator tess)
            : this(mass, walker, tess, new DefaultAppearanceSource()) { }

        // Runs SwSurface → Build → Write → Validate. Throws Sw2gz*Exception on
        // pre-export failures (material missing, geometry corrupt). Returns
        // ValidationReport after writing files. No output directory is created
        // if pre-export fails.
        //
        // P6-data — 5-arg overload delegates to the 6-arg one with an empty
        // sensor list (back-compat for v2.1 callers that don't yet carry
        // sensor specs from the SW boundary).
        public SW2GZ.Validate.ValidationReport Run(string outputDir, string packageName,
                                    string author, string email, string license) =>
            Run(outputDir, packageName, author, email, license, System.Array.Empty<SensorDef>());

        // Back-compat overload — the old coarse boolean maps onto the new profile:
        //   modelOnly:false → full stack (Default)   modelOnly:true → bare model (ModelOnly)
        // Existing callers/tests keep working byte-for-byte.
        public SW2GZ.Validate.ValidationReport Run(string outputDir, string packageName,
                                    string author, string email, string license,
                                    IReadOnlyList<SensorDef> sensors, bool modelOnly = false) =>
            Run(outputDir, packageName, author, email, license, sensors,
                modelOnly ? StackProfile.ModelOnly() : StackProfile.Default());

        // TODO P6-COM: replace caller-supplied `sensors` with a SW-COM source once the
        // workstation session lands an ISensorSource boundary similar to IAppearanceSource.
        // The StackProfile selects which stacks the export emits. A ModelOnly()
        // profile emits the bare robot package — links + joints + materials, a
        // Gz-spawn launch and an empty world — with NO ros2_control / Gazebo
        // plugin files. It still builds in colcon and spawns the robot.
        //
        // `mode` selects the artifact dimension: ExportMode.RobotPackage (default)
        // emits today's URDF/ros2 package exactly as before. ExportMode.SdfModel /
        // SdfWorld emit a standard gz Harmonic model directory package instead and
        // intentionally IGNORE the StackProfile actuation (gz model/world packages
        // carry no ros2_control).
        public SW2GZ.Validate.ValidationReport Run(string outputDir, string packageName,
                                    string author, string email, string license,
                                    IReadOnlyList<SensorDef> sensors, StackProfile profile,
                                    ExportMode mode = ExportMode.RobotPackage)
        {
            if (sensors == null) throw new ArgumentNullException(nameof(sensors));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            // D1: actuation == Ros2Control reproduces the legacy full-stack output; every other backend (incl. GzPlugin, whose own writer lands in D3) falls through to the legacy model-only output. This single flag == today's !modelOnly.
            bool fullStack = profile.Actuation == ActuationBackend.Ros2Control;
            // ── Step 1: Sanitize ──────────────────────────────────────────────
            string pkg = PackageNameSanitizer.Sanitize(packageName).Value;

            // ── Step 1.5: Preflight — validate output path BEFORE any work ────
            // Catches MAX_PATH overrun, missing parent, and non-writable targets
            // up front so the user gets a friendly error instead of a half-written
            // package after several seconds of mesh tessellation.
            ValidatePreflight(outputDir, pkg);

            // ── Step 2: PreExport — walk + mass-check (no I/O yet) ────────────
            IReadOnlyList<LinkSpec> specs = _walker.WalkActive();
            var massCache = new Dictionary<string, MassProps>(specs.Count);

            foreach (LinkSpec spec in specs)
            {
                foreach (string partPath in spec.FlattenedPartPaths)
                {
                    // Throws MaterialMissingException / Sw2gzExportException on failure.
                    // Those propagate out — no output directory is written.
                    MassProps mp = _mass.Get(partPath);
                    massCache[partPath] = mp;
                }
            }

            // ── Step 3: Build links ───────────────────────────────────────────
            var links = new List<UrdfLink>(specs.Count);
            // P5 — parallel list of (link, primary part path) for appearance lookup.
            // Multi-body links: first-part-wins for v2.1; richer schema deferred.
            var linksWithPaths = new List<(UrdfLink Link, string PartPath)>(specs.Count);

            foreach (LinkSpec spec in specs)
            {
                // Tessellate primary part (first in list) for the visual mesh.
                // v2.0: use first part path as the visual source; multi-body flattening deferred.
                string primaryPath = spec.FlattenedPartPaths[0];
                MeshData visual = _tess.Tessellate(primaryPath, TessellationLod.Fine);

                // Build real convex hull (QuickHull) collision mesh from the visual mesh.
                MeshData collision = ConvexHullCollider.Build(visual, ColliderStrategy.ConvexHull);

                // Aggregate mass over all flattened parts at identity pose (v2.0 limitation —
                // inter-part transforms deferred to v2.1).
                var partsForAgg = new List<(MassProps, Pose)>(spec.FlattenedPartPaths.Count);
                foreach (string partPath in spec.FlattenedPartPaths)
                    partsForAgg.Add((massCache[partPath], Pose.Identity));

                MassProps agg = InertialAggregator.Combine(partsForAgg);

                UrdfLink link = LinkBuilder.Build(spec.Name, agg, visual, collision);
                links.Add(link);
                linksWithPaths.Add((link, primaryPath));
            }

            // ── Step 4: Joints (P2) ──────────────────────────────────────────
            // Walk the assembly mates and assemble the joint tree. WalkMates may
            // return an empty list (no mates / skeleton walker) — joints then stay
            // empty, identical to the pre-P2 behaviour (links export at origin).
            IReadOnlyList<MateSpec> mates = _walker.WalkMates();
            var (graphJoints, rootLink, jointWarnings) = JointGraphBuilder.Build(links, mates);
            var joints = new List<UrdfJoint>(graphJoints);

            // JointGraphBuilder warnings are non-fatal — collect them as
            // ValidationIssue warnings to merge into the final report below.
            var jointIssues = new List<SW2GZ.Validate.ValidationIssue>();
            foreach (string w in jointWarnings)
                jointIssues.Add(new SW2GZ.Validate.ValidationIssue(
                    SW2GZ.Validate.IssueSeverity.Warning, "P2.W.JOINT", w, "JointGraphBuilder"));

            // Build the immutable RobotModel keystone (P1) BEFORE any I/O begins,
            // so the P9 pre-write validator runs without creating output dirs.
            //
            // P5: query IAppearanceSource per primary part, get back the tagged
            // ModelLinks + deduped Materials, then build the model via the
            // ModelLink overload so the material refs survive.
            RobotMeta meta = new RobotMeta(pkg, author, email, license, CoordinateConvention.Identity);
            var (modelLinks, materials) =
                RobotModelBuilder.AssembleLinksWithMaterials(linksWithPaths, _appearances);

            // P6-data — validate the caller-supplied sensors against the
            // assembled link/joint sets. Sanitizes names + topics. Empty list
            // passes through as Array.Empty so RobotModel.Sensors stays a stable
            // singleton (record equality friendly).
            IReadOnlyList<SensorDef> validatedSensors =
                RobotModelBuilder.AssembleSensors(sensors, modelLinks, joints);

            RobotModel model = RobotModelBuilder.Build(meta, modelLinks, joints, materials, validatedSensors);

            // ── Step 4.5: Pre-write structural validation (P9) ────────────────
            // Fail-fast: any structural error throws here, BEFORE the output
            // directory is touched. Warnings are surfaced alongside the
            // post-write OutputValidator findings further down.
            SW2GZ.Validate.ValidationReport preWrite =
                SW2GZ.Validate.RobotModelValidator.Validate(model);
            if (preWrite.HasErrors)
            {
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Pre-write validation failed: " + preWrite.Errors.First().Message);
            }

            // ── Step 5: Write package tree (atomic) ───────────────────────────
            // v2.0 layout: <outputDir>/<pkg>_ws/src/<pkg>/...
            // Ready for `cd <pkg>_ws && colcon build` without further restructuring.
            //
            // Atomicity: all writes go to a sibling staging directory; on success
            // we swap it into place via Directory.Move so any prior successful
            // export survives a mid-run failure intact. Any leftover staging dir
            // from a previous crash is cleared before we start.
            string workspaceDir = Path.Combine(outputDir, $"{pkg}_ws");
            string stageDir = workspaceDir + ".sw2gz.tmp";
            string srcDir = Path.Combine(stageDir, "src");
            string root = Path.Combine(srcDir, pkg);
            try
            {
                if (Directory.Exists(stageDir))
                    Directory.Delete(stageDir, recursive: true);
                Directory.CreateDirectory(root);
                bool gz = mode != ExportMode.RobotPackage;
                if (gz)
                {
                    // GZ Asset / GZ World: a standard gz Harmonic model directory
                    // (models/<pkg>/{model.config, model.sdf, meshes/}) plus a world and a
                    // launch. ExportMode picks the artifact; the StackProfile actuation is
                    // intentionally ignored here — gz model/world packages carry no
                    // ros2_control (actuation is effectively None).
                    Directory.CreateDirectory(Path.Combine(root, "worlds"));
                    Directory.CreateDirectory(Path.Combine(root, "launch"));
                    string modelDir = Path.Combine(root, "models", pkg);
                    string gzMeshesDir = Path.Combine(modelDir, "meshes");
                    Directory.CreateDirectory(gzMeshesDir);

                    foreach (UrdfLink link in links)
                    {
                        DaeWriter.Write(link.VisualMesh,    Path.Combine(gzMeshesDir, link.VisualMeshFile));
                        StlWriter.Write(link.CollisionMesh, Path.Combine(gzMeshesDir, link.CollisionMeshFile));
                    }

                    File.WriteAllText(Path.Combine(root, "package.xml"),
                        PackageXmlV3Writer.Write(new PackageXmlInput(pkg, "0.1.0",
                            "Auto-generated by SW2GZ", author, email, license) { GzMode = true }));

                    File.WriteAllText(Path.Combine(root, "CMakeLists.txt"),
                        AmentCMakeWriter.Write(new AmentCMakeInput(pkg, hasMeshes: false,
                            hasModels: true, hasUrdf: false, hasConfig: false)));

                    new ModelConfigWriter(new ModelConfigWriter.Input
                    {
                        Name = pkg, Author = author, Email = email,
                    }).Write(modelDir);

                    SdfModelWriter.Write(model, modelDir);

                    if (mode == ExportMode.SdfModel)
                    {
                        File.WriteAllText(Path.Combine(root, "worlds", "empty.sdf"),
                            SdfWorldWriter.Write(new SdfWorldInput("empty"), model.Sensors));
                        File.WriteAllText(Path.Combine(root, "launch", $"{pkg}.launch.py"),
                            LaunchPyWriter.GzAsset(pkg));
                    }
                    else // ExportMode.SdfWorld
                    {
                        File.WriteAllText(Path.Combine(root, "worlds", $"{pkg}.sdf"),
                            SdfWorldWriter.WriteWithModel(new SdfWorldInput(pkg), pkg));
                        File.WriteAllText(Path.Combine(root, "launch", $"{pkg}.launch.py"),
                            LaunchPyWriter.GzWorld(pkg, pkg));
                    }
                }
                else
                {
                    foreach (string subdir in new[] { "urdf", "urdf/inc", "worlds", "launch", "config", "meshes" })
                        Directory.CreateDirectory(Path.Combine(root, subdir));

                    string meshesDir = Path.Combine(root, "meshes");

                    // Write per-link mesh files.
                    foreach (UrdfLink link in links)
                    {
                        DaeWriter.Write(link.VisualMesh,    Path.Combine(meshesDir, link.VisualMeshFile));
                        StlWriter.Write(link.CollisionMesh, Path.Combine(meshesDir, link.CollisionMeshFile));
                    }

                    // Full-stack body: prepend world link + fixed joint to root
                    // (so the robot doesn't fall in Gz) and apply nonzero defaults
                    // to zero-effort/velocity joint limits. Model-only export keeps
                    // the legacy bare-bones SerializeBody.
                    string bodyXml = fullStack
                        ? XacroGenerator.SerializeBodyForRobot(model, rootLink)
                        : XacroGenerator.SerializeBody(model);

                    // package.xml
                    File.WriteAllText(
                        Path.Combine(root, "package.xml"),
                        PackageXmlV3Writer.Write(new PackageXmlInput(pkg, "0.1.0",
                            "Auto-generated by SW2GZ", author, email, license)));

                    // CMakeLists.txt
                    File.WriteAllText(
                        Path.Combine(root, "CMakeLists.txt"),
                        AmentCMakeWriter.Write(new AmentCMakeInput(pkg, hasMeshes: true)));

                    // urdf/<pkg>.urdf.xacro — model-only drops the control/plugin includes.
                    // gated by actuation backend (D1: Ros2Control == full stack)
                    File.WriteAllText(
                        Path.Combine(root, "urdf", $"{pkg}.urdf.xacro"),
                        fullStack ? XacroWriter.Write(pkg, bodyXml) : XacroWriter.WriteModelOnly(pkg, bodyXml));

                    // urdf/inc/materials.xacro (P5: real material defs from RobotModel.Materials;
                    // empty list emits a placeholder comment so the file still parses).
                    File.WriteAllText(
                        Path.Combine(root, "urdf", "inc", "materials.xacro"),
                        XacroGenerator.SerializeMaterialsXacro(model.Materials));

                    var jointNames = new List<string>();
                    foreach (UrdfJoint j in joints) jointNames.Add(j.Name);

                    // Control + Gazebo-plugin files — skipped entirely in model-only mode.
                    // gated by actuation backend (D1: Ros2Control == full stack)
                    if (fullStack)
                    {
                        File.WriteAllText(
                            Path.Combine(root, "urdf", "inc", "ros2_control.xacro"),
                            Ros2ControlWriter.Write(pkg, jointNames));

                        File.WriteAllText(
                            Path.Combine(root, "urdf", "inc", "gz.xacro"),
                            GzPluginTags.WriteGzRos2ControlXacro(pkg));
                    }

                    // worlds/empty.sdf — P6-data: sensor list drives plugin injection.
                    File.WriteAllText(
                        Path.Combine(root, "worlds", "empty.sdf"),
                        SdfWorldWriter.Write(new SdfWorldInput("empty"), model.Sensors));

                    // launch/ — single gz_sim.launch.py is the only entry point.
                    // No rviz (Gz is the viewport), no separate display launch, no
                    // standalone ros2_control launch (controller spawners chain
                    // off the spawn action inside gz_sim.launch.py).
                    string launchDir = Path.Combine(root, "launch");
                    File.WriteAllText(Path.Combine(launchDir, "gz_sim.launch.py"),
                        fullStack ? LaunchPyWriter.GzSim(pkg) : LaunchPyWriter.GzSimModelOnly(pkg));

                    if (fullStack)
                    {
                        File.WriteAllText(
                            Path.Combine(root, "config", "controllers.yaml"),
                            ControllersYaml.Write(new ControllersInput(pkg, jointNames)));

                        File.WriteAllText(
                            Path.Combine(root, "config", "ros_gz_bridge.yaml"),
                            RosGzBridgeYaml.Write(pkg, model.Sensors));
                    }

                    new ReadmeWriter(pkg, new TargetProfile { Mode = mode }).Write(root);
                }

                // ── Step 6: Validate ──────────────────────────────────────────────
                // Merge P9 pre-write warnings with post-write OutputValidator
                // issues so the caller sees a single report. Pre-write errors
                // never reach here (they throw above).
                SW2GZ.Validate.ValidationReport postWrite =
                    new SW2GZ.Validate.OutputValidator().Run(root, pkg);
                var finalReport = new SW2GZ.Validate.ValidationReport(
                    preWrite.Warnings.Concat(jointIssues).Concat(postWrite.Issues).ToList());

                // ── Step 7: Per-run summary log ───────────────────────────────────
                // Written inside the staging dir so it survives the swap and ships
                // with the workspace. Best-effort: a log-write failure must not
                // sink an otherwise-successful export.
                try
                {
                    File.WriteAllText(
                        Path.Combine(stageDir, "sw2gz_export.log"),
                        BuildSummaryLog(mode, pkg, author, email, license, outputDir,
                            links.Count, joints.Count, sensors.Count, finalReport));
                }
                catch (Exception logEx)
                {
                    // Surface as a warning but don't fail the export.
                    System.Diagnostics.Debug.WriteLine(
                        "sw2gz_export.log write failed: " + logEx.Message);
                }

                // ── Step 8: Atomic swap stage → workspace ─────────────────────────
                // Prior workspace (from a successful earlier export) is held in
                // <ws>.sw2gz.bak until the new workspace is in place, then deleted.
                // If the swap itself fails, the prior workspace is restored.
                string bakDir = workspaceDir + ".sw2gz.bak";
                if (Directory.Exists(bakDir))
                    Directory.Delete(bakDir, recursive: true);
                bool hadPrior = Directory.Exists(workspaceDir);
                if (hadPrior)
                    Directory.Move(workspaceDir, bakDir);
                try
                {
                    Directory.Move(stageDir, workspaceDir);
                }
                catch
                {
                    if (hadPrior && Directory.Exists(bakDir) && !Directory.Exists(workspaceDir))
                        Directory.Move(bakDir, workspaceDir);
                    throw;
                }
                if (Directory.Exists(bakDir))
                {
                    try { Directory.Delete(bakDir, recursive: true); }
                    catch { /* best-effort — the new workspace is already in place */ }
                }

                return finalReport;
            }
            catch
            {
                // Stage-dir-only cleanup: the prior workspace (if any) was never
                // touched until the swap, so a mid-export failure leaves the last
                // good export intact.
                if (Directory.Exists(stageDir))
                {
                    try { Directory.Delete(stageDir, recursive: true); }
                    catch { /* best-effort cleanup; surface the original failure */ }
                }
                throw;
            }
        }

        // Conservative MAX_PATH ceiling for the workspace base. The deepest
        // in-package file the pipeline writes is something like
        //   <ws>/src/<pkg>/urdf/inc/ros2_control.xacro  (≈ 45 chars after <ws>)
        // We add a generous safety margin so long link names ("base_link_revolute_drive.dae")
        // don't blow MAX_PATH=260 on Windows.
        private const int InPackageReserveChars = 90;

        private static void ValidatePreflight(string outputDir, string sanitizedPkg)
        {
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Output folder is empty — set a target directory in the Export dialog.");

            string fullOut;
            try
            {
                fullOut = Path.GetFullPath(outputDir);
            }
            catch (Exception e)
            {
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Output folder is not a valid path: " + outputDir + " (" + e.Message + ")");
            }

            string parent = Path.GetDirectoryName(fullOut);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Output folder parent does not exist: " + parent +
                    " — create it (or pick a different output folder) and retry.");

            string workspaceDir = Path.Combine(fullOut, sanitizedPkg + "_ws");
            int budget = 260 - InPackageReserveChars;
            if (workspaceDir.Length > budget)
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Workspace path is too long (" + workspaceDir.Length + " chars; " +
                    "max " + budget + " to keep package files under Windows MAX_PATH=260). " +
                    "Choose a shorter output folder or package name.");

            try
            {
                Directory.CreateDirectory(fullOut);
                string probe = Path.Combine(fullOut, ".sw2gz_writeprobe");
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            catch (Exception e)
            {
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Output folder is not writable: " + fullOut + " (" + e.Message + ")");
            }
        }

        private static string BuildSummaryLog(ExportMode mode, string pkg, string author,
            string email, string license, string outputDir,
            int linkCount, int jointCount, int sensorCount,
            SW2GZ.Validate.ValidationReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SW2GZ Export Log");
            sb.AppendLine("================");
            sb.AppendLine("Timestamp:    " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Mode:         " + mode);
            sb.AppendLine("Package:      " + pkg);
            sb.AppendLine("Author:       " + (string.IsNullOrEmpty(author) ? "(unset)" : author));
            sb.AppendLine("Email:        " + (string.IsNullOrEmpty(email) ? "(unset)" : email));
            sb.AppendLine("License:      " + (string.IsNullOrEmpty(license) ? "(unset)" : license));
            sb.AppendLine("Output:       " + outputDir);
            sb.AppendLine("Links:        " + linkCount);
            sb.AppendLine("Joints:       " + jointCount);
            sb.AppendLine("Sensors:      " + sensorCount);
            sb.AppendLine();
            sb.AppendLine("Warnings (" + report.Warnings.Count() + "):");
            if (report.Warnings.Any())
            {
                foreach (SW2GZ.Validate.ValidationIssue w in report.Warnings)
                    sb.AppendLine("  - [" + w.Code + "] " + w.Message);
            }
            else
            {
                sb.AppendLine("  (none)");
            }
            sb.AppendLine();
            sb.AppendLine("Errors (" + report.Errors.Count() + "):");
            if (report.Errors.Any())
            {
                foreach (SW2GZ.Validate.ValidationIssue e in report.Errors)
                    sb.AppendLine("  - [" + e.Code + "] " + e.Message);
            }
            else
            {
                sb.AppendLine("  (none)");
            }
            sb.AppendLine();
            sb.AppendLine("Status: " + (report.HasErrors ? "ERRORS (see above)" : "SUCCESS"));
            return sb.ToString();
        }
    }
}
