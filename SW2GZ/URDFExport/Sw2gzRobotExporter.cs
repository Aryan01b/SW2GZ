/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Robot mode v3 — arbitrary user-built link tree (drag-to-reparent in the
Links wizard), no mate-driven joint detection (removed 2026-07-01, was
misclassifying joints; reverted to a known-good baseline) — every joint is
still type="fixed", but DOES carry the real relative pose (rotation +
translation) between parent and child, computed against each link's OWN
declared ParentName, not always the root. Root is resolved by tree
structure (LinkHierarchy.Roots — whichever link has no parent), not by
list position, since re-rooting (LinkTreeView's "Set as base link") edits
ParentName pointers without reordering Robot.Links.

A link's mesh and mass/inertia are each a UNION of every component
assigned to it (LinkDef.ComponentIds, wizard multi-select), not just the
first — components are combined in the link's own reference frame, which
is always its FIRST assigned component's pose. Mesh union: every
component's tessellated mesh is un-baked into that one shared frame and
concatenated. Mass union: every component's own MassProps + own pose feed
InertialAggregator.Combine, rebased into the same shared frame — for a
single-component link this is byte-identical to reading that component's
MassProps directly (the rebase-by-anchor math cancels exactly when a
part's own frame equals the anchor).

Each link's <visual> mesh is expressed in that shared reference frame
(un-baked from the tessellator's assembly-frame output using the same
(R, t) pose read for the joint math), so the mesh renders correctly under
the real joint chain and each link's TF frame reflects its true SW
orientation — not just its true position.

    p_world = R_link * p_local + t_link                       (tessellator bake)
    p_local = R_link^T * (p_world - t_link)                   (un-bake for <visual>)
    R_joint(parent->child) = R_parent^T * R_child
    t_joint(parent->child) = R_parent^T * (t_child - t_parent)

Base link is the one exception: its own frame is treated as identity (a
common, valid URDF convention for the root link — nothing above it to be
"relative" to), so its mesh is only re-centered (t_base subtracted), not
un-rotated — mass combination does NOT get this same treatment (it rebases
into the root's REAL pose, not forced identity; see CombineMass's own doc
comment for why). This matches FULL_ARM's own base_link (already
identity-rotated in SW) exactly; a base_link with a genuinely rotated
native frame would show that rotation baked into its mesh rather than
reflected in its own TF triad — a known simplification, not a bug, for
this validating cut.

Output layout (no ament package yet — deliberately out of scope for this
validating cut):
    <outputDir>/<pkg>_ws/src/<pkg>/urdf/<pkg>.urdf.xacro
    <outputDir>/<pkg>_ws/src/<pkg>/meshes/<link>.dae

COM-free (takes IMeshTessellator + IMassProperties + IComponentPoses) so it's
unit-testable with fakes.
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDF;
using SW2GZ.Validate;
using SW2GZ.Write.Mesh;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzRobotExporter
    {
        public static ValidationReport Export(
            IMeshTessellator tess, IMassProperties massProps, IComponentPoses poses,
            Sw2gzExportConfig config, string outputDir, Matrix3 swToRos)
        {
            if (tess == null) throw new ArgumentNullException(nameof(tess));
            if (massProps == null) throw new ArgumentNullException(nameof(massProps));
            if (poses == null) throw new ArgumentNullException(nameof(poses));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Output folder is empty — set a target directory in the Export dialog.");

            List<LinkDef> links = config.RobotLinks ?? new List<LinkDef>();
            if (links.Count == 0)
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "No links defined — open Create Robot and add at least one link.");

            string pkg = PackageNameSanitizer.Sanitize(config.PackageName).Value;
            string workspace = Path.Combine(outputDir, pkg + "_ws");
            string root = Path.Combine(workspace, "src", pkg);
            string urdfDir = Path.Combine(root, "urdf");
            string meshesDir = Path.Combine(root, "meshes");
            Directory.CreateDirectory(urdfDir);
            Directory.CreateDirectory(meshesDir);

            var issues = new List<ValidationIssue>();
            var meshFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var masses = new Dictionary<string, MassProps>(StringComparer.Ordinal);
            var jointOrigins = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            var jointRpys = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);

            // Root = whichever link the TREE says has no parent, not
            // links[0]. "Set as base link" (re-root, in LinkTreeView) edits
            // ParentName pointers but never reorders Robot.Links, so list
            // position [0] can silently stop being the real root.
            LinkDef baseLink = LinkHierarchy.Roots(links).FirstOrDefault() ?? links[0];
            string baseLinkName = baseLink.Name;

            // Pass 1: every link's own reference pose (its first assigned
            // component), read once up front. A child can be positioned
            // before its parent in this list after a drag-drop reparent
            // (reparenting only edits ParentName, never reorders Links), so
            // pass 2 needs random access to ANY link's pose by name, not
            // list order.
            var linkPoses = new Dictionary<string, (Matrix3 R, Vector3 T)>(StringComparer.Ordinal);
            foreach (LinkDef link in links)
            {
                string refComp = link.ComponentIds?.FirstOrDefault();
                linkPoses[link.Name] = TryGetPose(poses, refComp, issues, link.Name);
            }

            foreach (LinkDef link in links)
            {
                string compName = link.ComponentIds?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(compName)) continue;

                (Matrix3 linkR, Vector3 linkT) = linkPoses[link.Name];

                MeshData meshLocal = link.Name == baseLinkName
                    ? UnionMeshInLocalFrame(tess, link.ComponentIds, Matrix3.Identity, linkT, issues, link.Name)
                    : UnionMeshInLocalFrame(tess, link.ComponentIds, linkR, linkT, issues, link.Name);

                if (meshLocal != null)
                {
                    string daeFile = link.Name + ".dae";
                    DaeWriter.Write(meshLocal, Path.Combine(meshesDir, daeFile), withNormals: true);
                    meshFiles[link.Name] = daeFile;
                }

                // Joint origin relative to THIS link's own declared parent —
                // not always the root. Same formula as before; only the
                // source of "parent pose" changed.
                if (!string.IsNullOrEmpty(link.ParentName) &&
                    linkPoses.TryGetValue(link.ParentName, out (Matrix3 R, Vector3 T) parentPose))
                {
                    Matrix3 rJoint = parentPose.R.Transpose() * linkR;
                    Vector3 tJoint = parentPose.R.Transpose().Mul(linkT - parentPose.T);
                    jointOrigins[link.Name] = tJoint;
                    jointRpys[link.Name] = rJoint.ToRpy();
                }
                else if (!string.IsNullOrEmpty(link.ParentName))
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.PARENT",
                        "Link '" + link.Name + "' — parent '" + link.ParentName + "' not found, joint origin defaults to identity.",
                        "Sw2gzRobotExporter"));
                }

                masses[link.Name] = CombineMass(massProps, poses, link.ComponentIds, linkR, linkT, issues, link.Name);
            }

            string urdfPath = Path.Combine(urdfDir, pkg + ".urdf.xacro");
            WriteUrdf(urdfPath, pkg, baseLinkName, links, meshFiles, masses, jointOrigins, jointRpys, config.EmitWorldLink, swToRos);

            return new ValidationReport(issues);
        }

        private static (Matrix3, Vector3) TryGetPose(
            IComponentPoses poses, string compName, List<ValidationIssue> issues, string linkName)
        {
            if (string.IsNullOrWhiteSpace(compName)) return (Matrix3.Identity, Vector3.Zero);
            try
            {
                return poses.GetPose(compName);
            }
            catch (Exception ex)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.POSE",
                    "Link '" + linkName + "' — could not read pose for '" + compName + "', using identity: " + ex.Message,
                    "Sw2gzRobotExporter"));
                return (Matrix3.Identity, Vector3.Zero);
            }
        }

        // Every component assigned to a link gets tessellated and folded
        // into ONE mesh, all expressed in the SAME reference frame (refR,
        // refT) — not each component's own frame, which would scatter the
        // pieces apart. Generalizes the old single-component un-bake to N
        // components via the same vertex-offset + index-shift pattern
        // SolidWorksMeshTessellator already uses internally to union
        // multiple solid bodies within one component.
        private static MeshData UnionMeshInLocalFrame(
            IMeshTessellator tess, IReadOnlyList<string> componentIds,
            Matrix3 refR, Vector3 refT, List<ValidationIssue> issues, string linkName)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            System.Drawing.Color? color = null;
            Matrix3 refRInv = refR.Transpose();

            foreach (string compName in componentIds ?? (IReadOnlyList<string>)Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(compName)) continue;

                MeshData meshWorld;
                try
                {
                    meshWorld = tess.Tessellate(compName, TessellationLod.Fine);
                }
                catch (Exception ex)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MESH",
                        "Link '" + linkName + "' — could not tessellate '" + compName + "': " + ex.Message,
                        "Sw2gzRobotExporter"));
                    continue;
                }
                if (meshWorld?.Vertices == null || meshWorld.Vertices.Length == 0) continue;

                color ??= meshWorld.MaterialColor;
                int baseIdx = verts.Count;
                foreach (Vector3 v in meshWorld.Vertices) verts.Add(refRInv.Mul(v - refT));
                foreach (int idx in meshWorld.Triangles) tris.Add(baseIdx + idx);
            }

            return verts.Count == 0 ? null : new MeshData(verts.ToArray(), tris.ToArray(), color);
        }

        // Combines every assigned component's own mass/COM/inertia
        // (parallel-axis, via InertialAggregator) into one MassProps rebased
        // into the link's own reference frame (linkR/linkT — the SAME pose
        // used for the joint and mesh math). For a single-component link
        // this is byte-identical to using that component's raw MassProps
        // directly: InertialAggregator's rebase exactly cancels when a
        // part's own frame equals the anchor (see
        // CombineWithLinkAnchor_SinglePartAtAnchor_RebasesBackToPartLocal /
        // its Matrix3 twin). Mass/inertia physical accuracy beyond this is
        // already out of scope for this validating cut (see the ponytail
        // note this replaces) — this does not force the base_link
        // identity-orientation convention the way mesh un-baking does,
        // since that would only matter for a multi-component root with a
        // non-identity native rotation, which isn't exercised yet.
        private static MassProps CombineMass(
            IMassProperties massProps, IComponentPoses poses, IReadOnlyList<string> componentIds,
            Matrix3 linkR, Vector3 linkT, List<ValidationIssue> issues, string linkName)
        {
            var parts = new List<(MassProps Props, Matrix3 R, Vector3 T)>();
            foreach (string compName in componentIds ?? (IReadOnlyList<string>)Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(compName)) continue;

                MassProps mp;
                try
                {
                    mp = massProps.Get(compName);
                }
                catch (Exception ex)
                {
                    mp = new MassProps(0.1, Vector3.Zero, Matrix3.Identity);
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MASS",
                        "Link '" + linkName + "' — no material on '" + compName + "', using placeholder mass: " + ex.Message,
                        "Sw2gzRobotExporter"));
                }
                (Matrix3 compR, Vector3 compT) = TryGetPose(poses, compName, issues, linkName);
                parts.Add((mp, compR, compT));
            }

            if (parts.Count == 0) return new MassProps(0.1, Vector3.Zero, Matrix3.Identity);
            return InertialAggregator.Combine(parts, linkR, linkT);
        }

        private static void WriteUrdf(
            string path, string pkg, string baseLinkName, List<LinkDef> links,
            Dictionary<string, string> meshFiles, Dictionary<string, MassProps> masses,
            Dictionary<string, Vector3> jointOrigins, Dictionary<string, (double, double, double)> jointRpys,
            bool emitWorldLink, Matrix3 swToRos)
        {
            var uw = new URDFWriter(path);
            System.Xml.XmlWriter w = uw.writer;
            w.WriteStartDocument();
            w.WriteStartElement("robot");
            w.WriteAttributeString("name", pkg);

            // Same mechanism Sw2gzModelPreviewer already uses for the browser
            // preview: a synthetic world link + fixed joint carrying the SW→ROS
            // rotation, only emitted when the caller opts in (preview forces
            // this on; real exports honour the user's saved EmitWorldLink).
            if (emitWorldLink)
            {
                w.WriteStartElement("link");
                w.WriteAttributeString("name", "world");
                w.WriteEndElement();

                (double roll, double pitch, double yaw) = swToRos.ToRpy();
                w.WriteStartElement("joint");
                w.WriteAttributeString("name", "world_to_" + baseLinkName);
                w.WriteAttributeString("type", "fixed");
                w.WriteStartElement("parent"); w.WriteAttributeString("link", "world"); w.WriteEndElement();
                w.WriteStartElement("child"); w.WriteAttributeString("link", baseLinkName); w.WriteEndElement();
                w.WriteStartElement("origin");
                w.WriteAttributeString("xyz", "0 0 0");
                w.WriteAttributeString("rpy", Fmt(roll) + " " + Fmt(pitch) + " " + Fmt(yaw));
                w.WriteEndElement();
                w.WriteEndElement();
            }

            foreach (LinkDef link in links)
            {
                w.WriteStartElement("link");
                w.WriteAttributeString("name", link.Name);

                if (meshFiles.TryGetValue(link.Name, out string daeFile))
                {
                    WriteVisualOrCollision(w, "visual", pkg, daeFile);
                    WriteVisualOrCollision(w, "collision", pkg, daeFile);
                }

                if (masses.TryGetValue(link.Name, out MassProps mp))
                {
                    w.WriteStartElement("inertial");
                    // ponytail: COM held at the link origin rather than the SW
                    // mass-property centroid re-expressed in this link's local
                    // frame. Physical accuracy is out of scope for this
                    // validating cut (no actuation/physics sim runs on the
                    // export yet); revisit once Robot mode gets a real
                    // inertia pipeline.
                    w.WriteStartElement("origin");
                    w.WriteAttributeString("xyz", "0 0 0");
                    w.WriteEndElement();
                    w.WriteStartElement("mass");
                    w.WriteAttributeString("value", Fmt(mp.Mass));
                    w.WriteEndElement();
                    w.WriteStartElement("inertia");
                    w.WriteAttributeString("ixx", Fmt(mp.InertiaAtComLocal.M11));
                    w.WriteAttributeString("ixy", Fmt(mp.InertiaAtComLocal.M12));
                    w.WriteAttributeString("ixz", Fmt(mp.InertiaAtComLocal.M13));
                    w.WriteAttributeString("iyy", Fmt(mp.InertiaAtComLocal.M22));
                    w.WriteAttributeString("iyz", Fmt(mp.InertiaAtComLocal.M23));
                    w.WriteAttributeString("izz", Fmt(mp.InertiaAtComLocal.M33));
                    w.WriteEndElement();
                    w.WriteEndElement();
                }

                w.WriteEndElement(); // link
            }

            foreach (LinkDef link in links)
            {
                if (string.IsNullOrEmpty(link.ParentName)) continue;
                Vector3 origin = jointOrigins.TryGetValue(link.Name, out var o) ? o : Vector3.Zero;
                (double roll, double pitch, double yaw) = jointRpys.TryGetValue(link.Name, out var rpy)
                    ? rpy : (0.0, 0.0, 0.0);
                w.WriteStartElement("joint");
                w.WriteAttributeString("name", link.ParentName + "_to_" + link.Name);
                w.WriteAttributeString("type", "fixed");
                w.WriteStartElement("parent"); w.WriteAttributeString("link", link.ParentName); w.WriteEndElement();
                w.WriteStartElement("child"); w.WriteAttributeString("link", link.Name); w.WriteEndElement();
                w.WriteStartElement("origin");
                w.WriteAttributeString("xyz", Fmt(origin.X) + " " + Fmt(origin.Y) + " " + Fmt(origin.Z));
                w.WriteAttributeString("rpy", Fmt(roll) + " " + Fmt(pitch) + " " + Fmt(yaw));
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteEndElement(); // robot
            w.WriteEndDocument();
            w.Flush();
            w.Close();
        }

        private static void WriteVisualOrCollision(System.Xml.XmlWriter w, string tag, string pkg, string daeFile)
        {
            w.WriteStartElement(tag);
            w.WriteStartElement("origin");
            w.WriteAttributeString("xyz", "0 0 0");
            w.WriteAttributeString("rpy", "0 0 0");
            w.WriteEndElement();
            w.WriteStartElement("geometry");
            w.WriteStartElement("mesh");
            w.WriteAttributeString("filename", "package://" + pkg + "/meshes/" + daeFile);
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        private static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
