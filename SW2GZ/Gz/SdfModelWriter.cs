/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Locked to Gz Sim Harmonic — SDF version 1.10. Emits a standard gz model
directory's model.sdf with REAL geometry (visual/collision meshes, material
color, inertial, joints) from the immutable RobotModel. Mesh URIs use the
model:// scheme so the model resolves under GZ_SIM_RESOURCE_PATH.

The legacy name-only SdfModelInput ctor is retained transiently for the
net48 ExportHelper call site and is removed once that is rerouted through
the pipeline.
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Xml.Linq;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.Gz
{
    public class SdfModelWriter
    {
        private readonly SdfModelInput _input;

        public SdfModelWriter(SdfModelInput input, object profile = null)
        {
            _input = input;
        }

        // Legacy name-only emit (transitional — removed in a later task).
        public void Write(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var model = new XElement("model", new XAttribute("name", _input.Name));
            foreach (var l in _input.Links)
                model.Add(new XElement("link", new XAttribute("name", l.Name)));
            foreach (var j in _input.Joints)
                model.Add(new XElement("joint",
                    new XAttribute("name", j.Name),
                    new XAttribute("type", j.Type),
                    new XElement("parent", j.Parent),
                    new XElement("child", j.Child)));
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("sdf", new XAttribute("version", "1.10"), model));
            doc.Save(Path.Combine(outputDir, "model.sdf"));
        }

        // ── New RobotModel-based emit ──────────────────────────────────────
        public static void Write(RobotModel model, string outputDir)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "model.sdf"), Serialize(model));
        }

        public static string Serialize(RobotModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            string modelEsc = SecurityElement.Escape(model.Meta.PackageName);

            var mats = new Dictionary<string, MaterialDef>(StringComparer.Ordinal);
            foreach (MaterialDef m in model.Materials) mats[m.Name] = m;

            // Each link is the child of at most one joint (kinematic tree). Map
            // child-link name → its parent joint so the link can be posed relative
            // to its parent link (SDF frame semantics), reproducing the URDF layout.
            var childToJoint = new Dictionary<string, UrdfJoint>(StringComparer.Ordinal);
            foreach (UrdfJoint j in model.Joints) childToJoint[j.ChildLink] = j;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<sdf version=\"1.10\">");
            sb.AppendLine($"  <model name=\"{modelEsc}\">");
            foreach (ModelLink ml in model.Links) AppendLink(sb, ml, modelEsc, mats, childToJoint);
            foreach (UrdfJoint j in model.Joints) AppendJoint(sb, j);
            sb.AppendLine("  </model>");
            sb.AppendLine("</sdf>");
            return sb.ToString();
        }

        private static void AppendLink(StringBuilder sb, ModelLink ml, string modelEsc,
                                       IReadOnlyDictionary<string, MaterialDef> mats,
                                       IReadOnlyDictionary<string, UrdfJoint> childToJoint)
        {
            UrdfLink link = ml.Link;
            string linkEsc = SecurityElement.Escape(link.Name);
            sb.AppendLine($"    <link name=\"{linkEsc}\">");

            // Pose the link relative to its parent link via the connecting joint's
            // origin. The SDF joint frame defaults to the child link frame, so the
            // joint itself emits no <pose>. Root links (no parent joint) stay at the
            // model origin.
            if (childToJoint.TryGetValue(link.Name, out UrdfJoint pj))
            {
                var p = pj.Origin.Position;
                var (r, pi, y) = Matrix3.FromQuaternion(pj.Origin.Rotation).ToRpy();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <pose relative_to=\"{0}\">{1} {2} {3} {4} {5} {6}</pose>",
                    SecurityElement.Escape(pj.ParentLink), p.X, p.Y, p.Z, r, pi, y));
            }

            sb.AppendLine("      <inertial>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        <pose>{0} {1} {2} 0 0 0</pose>",
                link.ComLocal.X, link.ComLocal.Y, link.ComLocal.Z));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        <mass>{0}</mass>", link.Mass));
            Matrix3 I = link.InertiaAtComLocal;
            sb.AppendLine("        <inertia>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "          <ixx>{0}</ixx><ixy>{1}</ixy><ixz>{2}</ixz><iyy>{3}</iyy><iyz>{4}</iyz><izz>{5}</izz>",
                I.M11, I.M12, I.M13, I.M22, I.M23, I.M33));
            sb.AppendLine("        </inertia>");
            sb.AppendLine("      </inertial>");

            sb.AppendLine($"      <visual name=\"{linkEsc}_visual\">");
            sb.AppendLine("        <geometry>");
            sb.AppendLine($"          <mesh><uri>model://{modelEsc}/meshes/{SecurityElement.Escape(link.VisualMeshFile)}</uri></mesh>");
            sb.AppendLine("        </geometry>");
            if (ml.MaterialName != null && mats.TryGetValue(ml.MaterialName, out MaterialDef mat))
            {
                sb.AppendLine("        <material>");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "          <ambient>{0} {1} {2} {3}</ambient>", mat.R, mat.G, mat.B, mat.A));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "          <diffuse>{0} {1} {2} {3}</diffuse>", mat.R, mat.G, mat.B, mat.A));
                sb.AppendLine("        </material>");
            }
            sb.AppendLine("      </visual>");

            sb.AppendLine($"      <collision name=\"{linkEsc}_collision\">");
            sb.AppendLine("        <geometry>");
            sb.AppendLine($"          <mesh><uri>model://{modelEsc}/meshes/{SecurityElement.Escape(link.CollisionMeshFile)}</uri></mesh>");
            sb.AppendLine("        </geometry>");
            sb.AppendLine("      </collision>");

            sb.AppendLine("    </link>");
        }

        private static void AppendJoint(StringBuilder sb, UrdfJoint j)
        {
            string nameEsc = SecurityElement.Escape(j.Name);
            string typeStr = JointTypeString(j.Type);
            string parentEsc = SecurityElement.Escape(j.ParentLink);
            sb.AppendLine($"    <joint name=\"{nameEsc}\" type=\"{typeStr}\">");
            sb.AppendLine($"      <parent>{parentEsc}</parent>");
            sb.AppendLine($"      <child>{SecurityElement.Escape(j.ChildLink)}</child>");

            // No <pose> here: the SDF joint frame defaults to the child link frame,
            // which already carries the URDF parent→child origin (set on the link),
            // so the axis below is expressed in that frame exactly as in URDF.

            if (j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Continuous
                || j.Type == UrdfJointType.Prismatic)
            {
                sb.AppendLine("      <axis>");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "        <xyz>{0} {1} {2}</xyz>", j.Axis.X, j.Axis.Y, j.Axis.Z));
                if (j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Prismatic)
                {
                    sb.AppendLine("        <limit>");
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "          <lower>{0}</lower><upper>{1}</upper><effort>{2}</effort><velocity>{3}</velocity>",
                        j.LimitLower ?? 0.0, j.LimitUpper ?? 0.0, j.LimitEffort, j.LimitVelocity));
                    sb.AppendLine("        </limit>");
                }
                sb.AppendLine("      </axis>");
            }

            sb.AppendLine("    </joint>");
        }

        // SDF lacks continuous/planar/floating: continuous→revolute (no limit),
        // planar/floating→fixed (documented limitation).
        private static string JointTypeString(UrdfJointType t) => t switch
        {
            UrdfJointType.Fixed      => "fixed",
            UrdfJointType.Revolute   => "revolute",
            UrdfJointType.Continuous => "revolute",
            UrdfJointType.Prismatic  => "prismatic",
            UrdfJointType.Planar     => "fixed",
            UrdfJointType.Floating   => "fixed",
            _ => throw new InvalidOperationException($"Unhandled UrdfJointType: {t}"),
        };
    }
}
