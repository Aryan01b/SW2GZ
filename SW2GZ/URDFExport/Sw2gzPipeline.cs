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
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Gz;
using SW2GZ.Math;
using SW2GZ.Ros2;
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

        public Sw2gzPipeline(IMassProperties mass, IAssemblyWalker walker, IMeshTessellator tess)
        {
            _mass   = mass   ?? throw new ArgumentNullException(nameof(mass));
            _walker = walker ?? throw new ArgumentNullException(nameof(walker));
            _tess   = tess   ?? throw new ArgumentNullException(nameof(tess));
        }

        // Runs SwSurface → Build → Write → Validate. Throws Sw2gz*Exception on
        // pre-export failures (material missing, geometry corrupt). Returns
        // ValidationReport after writing files. No output directory is created
        // if pre-export fails.
        public SW2GZ.Validate.ValidationReport Run(string outputDir, string packageName,
                                    string author, string email, string license)
        {
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

            foreach (LinkSpec spec in specs)
            {
                // Tessellate primary part (first in list) for the visual mesh.
                // v2.0: use first part path as the visual source; multi-body flattening deferred.
                string primaryPath = spec.FlattenedPartPaths[0];
                MeshData visual = _tess.Tessellate(primaryPath, TessellationLod.Fine);

                // Build AABB collision hull from visual mesh.
                MeshData collision = ConvexHullCollider.Build(visual);

                // Aggregate mass over all flattened parts at identity pose (v2.0 limitation —
                // inter-part transforms deferred to v2.1).
                var partsForAgg = new List<(MassProps, Pose)>(spec.FlattenedPartPaths.Count);
                foreach (string partPath in spec.FlattenedPartPaths)
                    partsForAgg.Add((massCache[partPath], Pose.Identity));

                MassProps agg = InertialAggregator.Combine(partsForAgg);

                UrdfLink link = LinkBuilder.Build(spec.Name, agg, visual, collision);
                links.Add(link);
            }

            // ── Step 4: Joints (deferred to v2.1) ────────────────────────────
            // Pipeline emits empty joints list for v2.0.
            var joints = new List<UrdfJoint>();

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

                // Build the immutable RobotModel keystone (P1) and route the
                // body XML through the dedicated serializer. The legacy
                // BuildUrdfBodyXml helper is gone — UrdfSerializer reproduces
                // its bytes exactly for the same input.
                RobotMeta meta = new RobotMeta(pkg, author, email, license, CoordinateConvention.Identity);
                RobotModel model = RobotModelBuilder.Build(meta, links, joints);
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

                // urdf/<pkg>.urdf.xacro
                File.WriteAllText(
                    Path.Combine(root, "urdf", $"{pkg}.urdf.xacro"),
                    XacroWriter.Write(pkg, bodyXml));

                // urdf/inc/materials.xacro (placeholder)
                File.WriteAllText(
                    Path.Combine(root, "urdf", "inc", "materials.xacro"),
                    "<?xml version=\"1.0\"?>\n<robot xmlns:xacro=\"http://www.ros.org/wiki/xacro\">\n  <!-- Named materials populated by SW2GZ. -->\n</robot>\n");

                // urdf/inc/ros2_control.xacro
                var jointNames = new List<string>();
                foreach (UrdfJoint j in joints) jointNames.Add(j.Name);
                File.WriteAllText(
                    Path.Combine(root, "urdf", "inc", "ros2_control.xacro"),
                    Ros2ControlWriter.Write(pkg, jointNames));

                // urdf/inc/gz.xacro
                File.WriteAllText(
                    Path.Combine(root, "urdf", "inc", "gz.xacro"),
                    GzPluginTags.WriteGzRos2ControlXacro(pkg));

                // worlds/empty.sdf
                File.WriteAllText(
                    Path.Combine(root, "worlds", "empty.sdf"),
                    SdfWorldWriter.Write(new SdfWorldInput("empty")));

                // launch/
                string launchDir = Path.Combine(root, "launch");
                File.WriteAllText(Path.Combine(launchDir, "display.launch.py"),      LaunchPyWriter.Display(pkg));
                File.WriteAllText(Path.Combine(launchDir, "gz_sim.launch.py"),       LaunchPyWriter.GzSim(pkg));
                File.WriteAllText(Path.Combine(launchDir, "ros2_control.launch.py"), LaunchPyWriter.Ros2Control(pkg));

                // config/
                File.WriteAllText(
                    Path.Combine(root, "config", "controllers.yaml"),
                    ControllersYaml.Write(new ControllersInput(pkg, jointNames)));

                File.WriteAllText(
                    Path.Combine(root, "config", "ros_gz_bridge.yaml"),
                    RosGzBridgeYaml.Write(pkg));

                // ── Step 6: Validate ──────────────────────────────────────────────
                return new SW2GZ.Validate.OutputValidator().Run(root, pkg);
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
