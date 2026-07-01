/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Robot mode v3 — minimal validating cut for the clean rebuild (see
agent-progress/progress.md "Robot mode gutted for clean rebuild"). One link
per top-level component (config.RobotLinks[0] is the base link), every other
link Fixed to it — no mate-driven joint detection (removed 2026-07-01, was
misclassifying joints; reverted to this known-good baseline), but the joint
DOES carry the real relative pose (rotation + translation) between parent
and child, not an identity placeholder.

Each link's <visual> mesh is expressed in that component's OWN native
part-local frame (un-baked from the tessellator's assembly-frame output using
the same (R, t) pose read for the joint math), so the mesh renders correctly
under the real joint chain and each link's TF frame reflects its true SW
orientation — not just its true position.

    p_world = R_link * p_local + t_link                       (tessellator bake)
    p_local = R_link^T * (p_world - t_link)                   (un-bake for <visual>)
    R_joint(parent->child) = R_parent^T * R_child
    t_joint(parent->child) = R_parent^T * (t_child - t_parent)

Base link is the one exception: its own frame is treated as identity (a
common, valid URDF convention for the root link — nothing above it to be
"relative" to), so its mesh is only re-centered (t_base subtracted), not
un-rotated. This matches FULL_ARM's own base_link (already identity-rotated
in SW) exactly; a base_link with a genuinely rotated native frame would show
that rotation baked into its mesh rather than reflected in its own TF triad —
a known simplification, not a bug, for this validating cut.

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

            string baseLinkName = links[0].Name;
            string baseCompName = links[0].ComponentIds?.FirstOrDefault();
            (Matrix3 baseR, Vector3 baseT) = TryGetPose(poses, baseCompName, issues, baseLinkName);

            foreach (LinkDef link in links)
            {
                string compName = link.ComponentIds?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(compName)) continue;

                MeshData meshWorld;
                try
                {
                    meshWorld = tess.Tessellate(compName, TessellationLod.Fine);
                }
                catch (Exception ex)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MESH",
                        "Link '" + link.Name + "' — could not tessellate '" + compName + "': " + ex.Message,
                        "Sw2gzRobotExporter"));
                    meshWorld = null;
                }

                if (meshWorld != null)
                {
                    MeshData meshLocal;
                    if (link.Name == baseLinkName)
                    {
                        // Base = frame is identity by convention; only recenter.
                        meshLocal = Translate(meshWorld, -baseT);
                    }
                    else
                    {
                        (Matrix3 childR, Vector3 childT) = TryGetPose(poses, compName, issues, link.Name);
                        meshLocal = UnbakeToLocal(meshWorld, childR, childT);

                        Matrix3 rJoint = baseR.Transpose() * childR;
                        Vector3 tJoint = baseR.Transpose().Mul(childT - baseT);
                        jointOrigins[link.Name] = tJoint;
                        jointRpys[link.Name] = rJoint.ToRpy();
                    }

                    string daeFile = link.Name + ".dae";
                    DaeWriter.Write(meshLocal, Path.Combine(meshesDir, daeFile), withNormals: true);
                    meshFiles[link.Name] = daeFile;
                }

                try
                {
                    masses[link.Name] = massProps.Get(compName);
                }
                catch (Exception ex)
                {
                    // ponytail: no SW material on this part → placeholder mass/
                    // inertia. Physical accuracy is out of scope for this
                    // validating cut (no actuation/physics sim runs on the
                    // export yet); revisit once Robot mode gets a real
                    // inertia pipeline.
                    masses[link.Name] = new MassProps(0.1, Vector3.Zero, Matrix3.Identity);
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MASS",
                        "Link '" + link.Name + "' — no material on '" + compName + "', using placeholder mass: " + ex.Message,
                        "Sw2gzRobotExporter"));
                }
            }

            string urdfPath = Path.Combine(urdfDir, pkg + ".urdf.xacro");
            WriteUrdf(urdfPath, pkg, links, meshFiles, masses, jointOrigins, jointRpys, config.EmitWorldLink, swToRos);

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

        private static MeshData Translate(MeshData mesh, Vector3 t)
        {
            if (mesh?.Vertices == null || mesh.Vertices.Length == 0) return mesh;
            var shifted = new Vector3[mesh.Vertices.Length];
            for (int i = 0; i < shifted.Length; i++) shifted[i] = mesh.Vertices[i] + t;
            return new MeshData(shifted, mesh.Triangles, mesh.MaterialColor);
        }

        // p_local = R^T * (p_world - t) — reverses the tessellator's bake so the
        // mesh sits in the component's own native part frame.
        private static MeshData UnbakeToLocal(MeshData mesh, Matrix3 r, Vector3 t)
        {
            if (mesh?.Vertices == null || mesh.Vertices.Length == 0) return mesh;
            Matrix3 rInv = r.Transpose();
            var local = new Vector3[mesh.Vertices.Length];
            for (int i = 0; i < local.Length; i++) local[i] = rInv.Mul(mesh.Vertices[i] - t);
            return new MeshData(local, mesh.Triangles, mesh.MaterialColor);
        }

        private static void WriteUrdf(
            string path, string pkg, List<LinkDef> links,
            Dictionary<string, string> meshFiles, Dictionary<string, MassProps> masses,
            Dictionary<string, Vector3> jointOrigins, Dictionary<string, (double, double, double)> jointRpys,
            bool emitWorldLink, Matrix3 swToRos)
        {
            var uw = new URDFWriter(path);
            System.Xml.XmlWriter w = uw.writer;
            w.WriteStartDocument();
            w.WriteStartElement("robot");
            w.WriteAttributeString("name", pkg);

            string baseLinkName = links[0].Name;

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
