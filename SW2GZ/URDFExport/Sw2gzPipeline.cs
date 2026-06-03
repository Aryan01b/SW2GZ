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
        public SW2GZ.Validate.ValidationReport Run(string outputDir, string packageName,
                                    string author, string email, string license,
                                    IReadOnlyList<SensorDef> sensors, StackProfile profile)
        {
            if (sensors == null) throw new ArgumentNullException(nameof(sensors));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            // D1: actuation == Ros2Control reproduces the legacy full-stack output; every other backend (incl. GzPlugin, whose own writer lands in D3) falls through to the legacy model-only output. This single flag == today's !modelOnly.
            bool fullStack = profile.Actuation == ActuationBackend.Ros2Control;
            // ── Step 1: Sanitize ──────────────────────────────────────────────
            string pkg = PackageNameSanitizer.Sanitize(packageName).Value;

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
            var (graphJoints, _root, jointWarnings) = JointGraphBuilder.Build(links, mates);
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

            // ── Step 5: Write package tree ────────────────────────────────────
            // v2.0 layout: <outputDir>/<pkg>_ws/src/<pkg>/...
            // Ready for `cd <pkg>_ws && colcon build` without further restructuring.
            string workspaceDir = Path.Combine(outputDir, $"{pkg}_ws");
            bool createdWorkspace = !Directory.Exists(workspaceDir);
            string srcDir = Path.Combine(workspaceDir, "src");
            string root = Path.Combine(srcDir, pkg);
            try
            {
                Directory.CreateDirectory(root);

                foreach (string subdir in new[] { "urdf", "urdf/inc", "worlds", "launch", "config", "meshes" })
                    Directory.CreateDirectory(Path.Combine(root, subdir));

                string meshesDir = Path.Combine(root, "meshes");

                // Write per-link mesh files.
                foreach (UrdfLink link in links)
                {
                    DaeWriter.Write(link.VisualMesh,    Path.Combine(meshesDir, link.VisualMeshFile));
                    StlWriter.Write(link.CollisionMesh, Path.Combine(meshesDir, link.CollisionMeshFile));
                }

                // Body XML routed through the dedicated serializer. The legacy
                // BuildUrdfBodyXml helper is gone — UrdfSerializer reproduces
                // its bytes exactly for the same input.
                string bodyXml = UrdfSerializer.SerializeBody(model);

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
                    UrdfSerializer.SerializeMaterialsXacro(model.Materials));

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

                // launch/
                string launchDir = Path.Combine(root, "launch");
                File.WriteAllText(Path.Combine(launchDir, "display.launch.py"), LaunchPyWriter.Display(pkg));
                // gated by actuation backend (D1: Ros2Control == full stack)
                File.WriteAllText(Path.Combine(launchDir, "gz_sim.launch.py"),
                    fullStack ? LaunchPyWriter.GzSim(pkg) : LaunchPyWriter.GzSimModelOnly(pkg));

                // gated by actuation backend (D1: Ros2Control == full stack)
                if (fullStack)
                {
                    File.WriteAllText(Path.Combine(launchDir, "ros2_control.launch.py"),
                        LaunchPyWriter.Ros2Control(pkg));

                    // config/
                    File.WriteAllText(
                        Path.Combine(root, "config", "controllers.yaml"),
                        ControllersYaml.Write(new ControllersInput(pkg, jointNames)));

                    File.WriteAllText(
                        Path.Combine(root, "config", "ros_gz_bridge.yaml"),
                        RosGzBridgeYaml.Write(pkg, model.Sensors));
                }

                // ── Step 6: Validate ──────────────────────────────────────────────
                // Merge P9 pre-write warnings with post-write OutputValidator
                // issues so the caller sees a single report. Pre-write errors
                // never reach here (they throw above).
                SW2GZ.Validate.ValidationReport postWrite =
                    new SW2GZ.Validate.OutputValidator().Run(root, pkg);
                return new SW2GZ.Validate.ValidationReport(
                    preWrite.Warnings.Concat(jointIssues).Concat(postWrite.Issues).ToList());
            }
            catch
            {
                if (createdWorkspace && Directory.Exists(workspaceDir))
                {
                    try { Directory.Delete(workspaceDir, recursive: true); }
                    catch { /* best-effort cleanup; surface the original failure */ }
                }
                throw;
            }
        }

    }
}
