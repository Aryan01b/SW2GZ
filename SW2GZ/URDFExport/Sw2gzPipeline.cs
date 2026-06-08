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
                                    ExportMode mode = ExportMode.RobotPackage,
                                    CoordinateConvention coord = null,
                                    bool emitWorldLink = false)
        {
            if (sensors == null) throw new ArgumentNullException(nameof(sensors));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            // Default to Identity (no rotation) for back-compat with callers
            // that haven't been updated yet. SW-side callers compute the
            // convention from Sw2gzExportConfig.SwUpAxis/SwForwardAxis and
            // pass it in; the test suite uses Identity to keep golden output
            // stable for any test that doesn't specifically exercise rotation.
            if (coord == null) coord = CoordinateConvention.Identity;
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

            // ── Step 2.5: Per-link anchor poses ───────────────────────────────
            // Each link is anchored at its first part's assembly-frame placement
            // (Component2.Transform2). Mesh vertices are rebased into the
            // link-local frame, and joint origins are computed as
            // parentAnchor⁻¹ ∘ childAnchor — so hinges sit in the right place
            // in space when Gz rotates them. Test mocks of IAssemblyWalker
            // don't implement IComponentPoseSource → all anchors fall back to
            // Pose.Identity → legacy byte-identical output for existing goldens.
            IComponentPoseSource poseSource = _walker as IComponentPoseSource;
            IReadOnlyDictionary<string, Pose> linkAnchors =
                LinkAnchorMap.Build(specs, poseSource);

            // ── Step 2.6: Mate-point overrides ────────────────────────────────
            // When a mate carries a geometric MatePointAssembly (e.g. the axis
            // origin of a concentric mate's cylindrical face), the child link's
            // URDF frame moves from the child part's design origin to that mate
            // point. We keep the child's ORIENTATION (so meshes need only a pure
            // translation offset to keep their world position) and replace the
            // position with the mate point. FrameOffset per link captures the
            // residual translation from the new frame to the part origin so the
            // emitters can shift <visual>/<collision>/<inertial>.
            //
            // Walking mates here (before link build) so the mesh-rebase uses
            // the FINAL link frame, not the legacy part-origin frame.
            IReadOnlyList<MateSpec> matesEarly = _walker.WalkMates();
            var matePointByChild = new Dictionary<string, System.Numerics.Vector3>(System.StringComparer.Ordinal);
            foreach (MateSpec ms in matesEarly)
            {
                if (ms == null || ms.ChildLink == null || !ms.MatePointAssembly.HasValue) continue;
                // First mate wins on duplicate child (kinematic tree => at most
                // one parent joint per child, but JointGraphBuilder validates).
                if (!matePointByChild.ContainsKey(ms.ChildLink))
                    matePointByChild[ms.ChildLink] = ms.MatePointAssembly.Value;
            }

            // Effective link frames + per-link FrameOffset. For a link without a
            // mate-point override: linkFrame = legacy anchor, FrameOffset = 0
            // (byte-identical to pre-fix behavior). For one WITH a mate-point:
            // linkFrame.pos = mate point, linkFrame.rot = childAnchor.rot;
            // FrameOffset = childAnchor.rot⁻¹ · (childAnchor.pos − matePoint).
            var linkFrames = new Dictionary<string, Pose>(System.StringComparer.Ordinal);
            var frameOffsets = new Dictionary<string, System.Numerics.Vector3>(System.StringComparer.Ordinal);
            foreach (var kv in linkAnchors)
            {
                Pose ca = kv.Value;
                if (matePointByChild.TryGetValue(kv.Key, out System.Numerics.Vector3 mp))
                {
                    linkFrames[kv.Key] = new Pose(mp, ca.Rotation);
                    var invR = System.Numerics.Quaternion.Inverse(ca.Rotation);
                    frameOffsets[kv.Key] = System.Numerics.Vector3.Transform(ca.Position - mp, invR);
                }
                else
                {
                    linkFrames[kv.Key] = ca;
                    frameOffsets[kv.Key] = System.Numerics.Vector3.Zero;
                }
            }

            // ── Step 3: Build links ───────────────────────────────────────────
            var links = new List<UrdfLink>(specs.Count);
            // P5 — parallel list of (link, primary part path) for appearance lookup.
            // Multi-body links: first-part-wins for v2.1; richer schema deferred.
            var linksWithPaths = new List<(UrdfLink Link, string PartPath)>(specs.Count);

            foreach (LinkSpec spec in specs)
            {
                // BUG FIX: tessellate ALL parts assigned to this link and union
                // their meshes — assembly-frame vertices (the tessellator now
                // bakes Component2.Transform2). Previous code took only the
                // first part, silently dropping every other part's geometry.
                MeshData visual;
                if (spec.FlattenedPartPaths.Count == 1)
                {
                    visual = _tess.Tessellate(spec.FlattenedPartPaths[0], TessellationLod.Fine);
                }
                else
                {
                    var unionV = new List<System.Numerics.Vector3>();
                    var unionT = new List<int>();
                    System.Drawing.Color? unionColor = null;
                    foreach (string partPath in spec.FlattenedPartPaths)
                    {
                        MeshData m = _tess.Tessellate(partPath, TessellationLod.Fine);
                        int baseIdx = unionV.Count;
                        unionV.AddRange(m.Vertices);
                        foreach (int idx in m.Triangles) unionT.Add(baseIdx + idx);
                        if (unionColor == null) unionColor = m.MaterialColor;
                    }
                    visual = new MeshData(unionV.ToArray(), unionT.ToArray(), unionColor);
                }

                // Rebase assembly-frame mesh vertices into the link-local frame.
                // No-op when the walker isn't an IComponentPoseSource (test paths).
                Pose anchor = linkAnchors.TryGetValue(spec.Name, out Pose a) ? a : Pose.Identity;
                visual = MeshRebase.Apply(visual, anchor);

                // Build real convex hull (QuickHull) collision mesh from the visual mesh.
                MeshData collision = ConvexHullCollider.Build(visual, ColliderStrategy.ConvexHull);

                // Aggregate mass over all flattened parts at the link anchor
                // (assembly-frame pose of the link's first part). The Combine
                // call yields COM + inertia in assembly coords; the overload
                // then rebases both into the LINK-local frame for the URDF
                // <inertial> block. For single-part links this round-trips
                // back to the part-local COM and inertia (anchor cancels).
                // Multi-part links: the per-part inter-frame is still
                // unresolved (deferred to a follow-up walker change), but at
                // least the link-frame rebase is now applied consistently.
                var partsForAgg = new List<(MassProps, Pose)>(spec.FlattenedPartPaths.Count);
                foreach (string partPath in spec.FlattenedPartPaths)
                    partsForAgg.Add((massCache[partPath], anchor));

                MassProps agg = InertialAggregator.Combine(partsForAgg, anchor);

                UrdfLink link = LinkBuilder.Build(spec.Name, agg, visual, collision);
                // FrameOffset shifts <visual>/<collision>/<inertial> when the
                // link's URDF frame is the mate point rather than the part
                // origin. Zero when no mate point applies, so legacy goldens
                // remain byte-identical.
                if (frameOffsets.TryGetValue(spec.Name, out System.Numerics.Vector3 fo) &&
                    fo != System.Numerics.Vector3.Zero)
                {
                    link = link with { FrameOffset = fo };
                }
                links.Add(link);
                // First part path drives appearance lookup (DefaultAppearanceSource).
                linksWithPaths.Add((link, spec.FlattenedPartPaths[0]));
            }

            // ── Step 4: Joints (P2) ──────────────────────────────────────────
            // Reuse the mate list walked in Step 2.6 (matePoint extraction needs
            // it BEFORE link build to size the link frames). WalkMates may
            // return an empty list (no mates / skeleton walker) — joints then stay
            // empty, identical to the pre-P2 behaviour (links export at origin).
            IReadOnlyList<MateSpec> mates = matesEarly;
            var (graphJoints, rootLink, jointWarnings) = JointGraphBuilder.Build(links, mates);
            var joints = new List<UrdfJoint>(graphJoints.Count);

            // mateSpec per joint name → recover the (optional) MatePointAssembly
            // so JointOriginResolver knows whether to use the mate-point path.
            var mateByName = new Dictionary<string, MateSpec>(System.StringComparer.Ordinal);
            foreach (MateSpec ms in mates)
                if (ms != null && !string.IsNullOrEmpty(ms.Name) && !mateByName.ContainsKey(ms.Name))
                    mateByName[ms.Name] = ms;

            // Patch each joint's <origin> and <axis> using the link anchors.
            // - Legacy (no mate point): origin = parentAnchor⁻¹ ∘ childAnchor.
            // - Mate-point: origin.pos sits at the mate point in the parent
            //   frame; child link frame is at the mate point. Both modes also
            //   re-express axis into the child (= joint) frame.
            // Identity anchors → joint emerges byte-identical to JointGraphBuilder's
            // output for back-compat with tests.
            //
            // We capture the assembly-frame axis BEFORE re-expression so the
            // pose dump can show both vectors side by side.
            var jointAxesAssembly = new Dictionary<string, System.Numerics.Vector3>(graphJoints.Count);
            foreach (UrdfJoint j in graphJoints)
            {
                Pose pA = linkAnchors.TryGetValue(j.ParentLink, out Pose pp) ? pp : Pose.Identity;
                Pose cA = linkAnchors.TryGetValue(j.ChildLink,  out Pose cc) ? cc : Pose.Identity;
                MateSpec msFound = mateByName.TryGetValue(j.Name, out MateSpec found) ? found : null;
                System.Numerics.Vector3? matePoint = msFound?.MatePointAssembly;
                jointAxesAssembly[j.Name] = j.Axis;

                // D3 — when the walker has pre-computed a parent-frame origin
                // (Reference-CS path), use it as-is; only the axis still needs
                // re-expression into the joint frame. Otherwise fall back to
                // anchor-derived origin + mate-point overlay.
                bool walkerProvidedOrigin = msFound != null && !IsIdentityPose(msFound.Origin);
                if (walkerProvidedOrigin)
                {
                    // Axis in MateSpec is the assembly-frame direction read off
                    // the Reference Axis feature. Re-express in the child link
                    // frame so URDF <axis> stays a joint-frame vector.
                    System.Numerics.Quaternion invChildRot =
                        System.Numerics.Quaternion.Inverse(cA.Rotation);
                    var axisInJoint = System.Numerics.Vector3.Transform(j.Axis, invChildRot);
                    joints.Add(j with { Origin = msFound.Origin, Axis = axisInJoint });
                }
                else
                {
                    JointOriginResolver.Resolved r =
                        JointOriginResolver.Compute(pA, cA, j.Axis, matePoint);
                    joints.Add(j with { Origin = r.Origin, Axis = r.AxisInJointFrame });
                }
            }

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
            RobotMeta meta = new RobotMeta(pkg, author, email, license, coord);
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
            //
            // Atomicity strategy:
            //   - Fresh export (workspace did not exist): write directly to
            //     workspaceDir. On failure, delete it (clean rollback).
            //   - Re-export (workspace existed): write to a sibling staging dir
            //     and swap on success, so a mid-run failure leaves the prior
            //     successful export intact.
            // Direct-write for the common fresh case avoids a Directory.Move on
            // every export — that rename briefly desyncs file visibility on
            // Windows, which broke parallel test runs.
            string workspaceDir = Path.Combine(outputDir, $"{pkg}_ws");
            bool hadPriorWorkspace = Directory.Exists(workspaceDir);
            string writeBaseDir = hadPriorWorkspace
                ? workspaceDir + ".sw2gz.tmp"
                : workspaceDir;
            string srcDir = Path.Combine(writeBaseDir, "src");
            string root = Path.Combine(srcDir, pkg);
            try
            {
                if (hadPriorWorkspace && Directory.Exists(writeBaseDir))
                    Directory.Delete(writeBaseDir, recursive: true);
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

                    // SDF doesn't have URDF's world-link trick, so the SW→ROS
                    // rotation rides on the spawn command (-R -P -Y) for SdfModel
                    // and on the world-include <pose> for SdfWorld.
                    (double sdfRoll, double sdfPitch, double sdfYaw) =
                        coord.SwToRos.ToRpy();

                    if (mode == ExportMode.SdfModel)
                    {
                        File.WriteAllText(Path.Combine(root, "worlds", "empty.sdf"),
                            SdfWorldWriter.Write(new SdfWorldInput("empty"), model.Sensors));
                        File.WriteAllText(Path.Combine(root, "launch", $"{pkg}.launch.py"),
                            LaunchPyWriter.GzAsset(pkg, sdfRoll, sdfPitch, sdfYaw));
                    }
                    else // ExportMode.SdfWorld
                    {
                        File.WriteAllText(Path.Combine(root, "worlds", $"{pkg}.sdf"),
                            SdfWorldWriter.WriteWithModel(new SdfWorldInput(pkg), pkg,
                                sdfRoll, sdfPitch, sdfYaw));
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

                    // Full-stack body: optionally prepend world link + fixed joint
                    // (REP-105 default is no world link — base_link is root). Apply
                    // nonzero defaults to zero effort/velocity joint limits.
                    // Model-only export keeps the legacy bare-bones SerializeBody.
                    string bodyXml = fullStack
                        ? XacroGenerator.SerializeBodyForRobot(model, rootLink, emitWorldLink)
                        : XacroGenerator.SerializeBody(model);

                    // SW→ROS rotation. If the URDF embeds a world link the rotation
                    // is already on world_to_<root> — pass identity to the launch.
                    // Otherwise the launch's spawn -R -P -Y carries it (REP-105 path).
                    (double urdfRoll, double urdfPitch, double urdfYaw) =
                        emitWorldLink ? (0.0, 0.0, 0.0) : coord.SwToRos.ToRpy();

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
                        fullStack
                            ? LaunchPyWriter.GzSim(pkg, urdfRoll, urdfPitch, urdfYaw)
                            : LaunchPyWriter.GzSimModelOnly(pkg, urdfRoll, urdfPitch, urdfYaw));

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
                // Written inside the write base so it ships with the workspace.
                // Best-effort: a log-write failure must not sink an otherwise-successful export.
                try
                {
                    File.WriteAllText(
                        Path.Combine(writeBaseDir, "sw2gz_export.log"),
                        BuildSummaryLog(mode, pkg, author, email, license, outputDir,
                            links.Count, joints.Count, sensors.Count, finalReport));
                }
                catch (Exception logEx)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "sw2gz_export.log write failed: " + logEx.Message);
                }

                // ── Step 7.5: Pose dump for diagnostics ───────────────────────────
                // Captures the link-anchor poses, joint origins, axis-pre/post
                // re-expression, and the raw Transform2.ArrayData for each link's
                // first part. Lets the next bug report attach a single text file
                // instead of re-running the export under a debugger. Best-effort.
                try
                {
                    IComponentRawTransformSource rawSource = _walker as IComponentRawTransformSource;
                    var matePointsByJoint = new Dictionary<string, System.Numerics.Vector3?>(System.StringComparer.Ordinal);
                    foreach (UrdfJoint j in joints)
                    {
                        matePointsByJoint[j.Name] = mateByName.TryGetValue(j.Name, out MateSpec ms)
                            ? ms.MatePointAssembly : null;
                    }
                    PoseDumpWriter.Write(
                        Path.Combine(writeBaseDir, "sw2gz_pose_dump.dbg.txt"),
                        pkg, specs, linkAnchors, joints, jointAxesAssembly, rawSource,
                        matePointsByJoint);
                }
                catch (Exception dumpEx)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "sw2gz_pose_dump.dbg.txt write failed: " + dumpEx.Message);
                }

                // ── Step 8: Re-export swap (only when prior workspace existed) ────
                // Prior workspace held in <ws>.sw2gz.bak during the swap so a
                // rename failure can restore it. Fresh exports skip this entirely —
                // writeBaseDir already IS workspaceDir.
                if (hadPriorWorkspace)
                {
                    string bakDir = workspaceDir + ".sw2gz.bak";
                    if (Directory.Exists(bakDir))
                        Directory.Delete(bakDir, recursive: true);
                    Directory.Move(workspaceDir, bakDir);
                    try
                    {
                        Directory.Move(writeBaseDir, workspaceDir);
                    }
                    catch
                    {
                        if (Directory.Exists(bakDir) && !Directory.Exists(workspaceDir))
                            Directory.Move(bakDir, workspaceDir);
                        throw;
                    }
                    if (Directory.Exists(bakDir))
                    {
                        try { Directory.Delete(bakDir, recursive: true); }
                        catch { /* best-effort — new workspace already in place */ }
                    }
                }

                return finalReport;
            }
            catch
            {
                // Fresh-export rollback: delete the workspace we just created so
                // a mid-run failure leaves nothing behind.
                // Re-export rollback: delete the staging dir; the prior workspace
                // was never touched (the swap only runs after success).
                string toDelete = hadPriorWorkspace ? writeBaseDir : workspaceDir;
                if (Directory.Exists(toDelete))
                {
                    try { Directory.Delete(toDelete, recursive: true); }
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

        // D3 — "is this Pose effectively Pose.Identity?" used to detect whether
        // the walker pre-populated MateSpec.Origin via the Reference-CS path.
        // Tolerance is tight (1e-6); the legacy code path constructs MateSpec
        // with literal Pose.Identity so an exact match is the common case, but
        // floating-point noise from a no-op localize must still register as
        // identity to keep golden output stable.
        private static bool IsIdentityPose(Pose p)
        {
            if (p == null) return true;
            const float eps = 1e-6f;
            if (System.Math.Abs(p.Position.X) > eps) return false;
            if (System.Math.Abs(p.Position.Y) > eps) return false;
            if (System.Math.Abs(p.Position.Z) > eps) return false;
            var q = p.Rotation;
            if (System.Math.Abs(q.X) > eps) return false;
            if (System.Math.Abs(q.Y) > eps) return false;
            if (System.Math.Abs(q.Z) > eps) return false;
            if (System.Math.Abs(System.Math.Abs(q.W) - 1f) > eps) return false;
            return true;
        }

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

            // Writability probe — write+delete a marker WITHOUT creating outputDir
            // itself. If outputDir exists, probe there; otherwise probe the parent
            // (which we just verified exists). Leaving outputDir uncreated preserves
            // the invariant "pre-export failure touches nothing" — the write phase
            // will create outputDir on demand via Directory.CreateDirectory(root).
            string probeDir = Directory.Exists(fullOut) ? fullOut : parent;
            if (!string.IsNullOrEmpty(probeDir))
            {
                // Unique probe filename — concurrent exports (including parallel
                // test runs) into a shared parent must not race on a fixed name.
                string probe = Path.Combine(probeDir,
                    ".sw2gz_writeprobe_" + Path.GetRandomFileName());
                try
                {
                    File.WriteAllText(probe, "");
                }
                catch (Exception e)
                {
                    throw new SW2GZ.Exceptions.Sw2gzExportException(
                        "Output folder is not writable: " + probeDir + " (" + e.Message + ")");
                }
                finally
                {
                    try { File.Delete(probe); } catch { /* best-effort */ }
                }
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
