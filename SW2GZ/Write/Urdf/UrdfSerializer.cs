/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: URDF body serializer. Consumes the immutable
RobotModel and emits the children of <robot>...</robot> (i.e. the
fragment that XacroWriter wraps).

Design decision (byte-parity):
  The legacy Sw2gzPipeline.BuildUrdfBodyXml used StringBuilder.AppendLine
  with explicit formatting (per-line CultureInfo.InvariantCulture floats,
  SecurityElement.Escape on every dynamic value, two-space indent).
  Golden tests compare URDF output, and although the existing golden
  for `<link name="base_link"/>` is normalized to LF, the pipeline-fed
  tests (Sw2gzPipelineTests / BugAcceptanceTests) check substrings, not
  whitespace. To guarantee byte-for-byte compatibility with the legacy
  output (and avoid xml-library quirks like attribute reordering and
  self-closing-vs-explicit-close differences), this serializer keeps the
  StringBuilder pattern. XElement-based emission was considered but
  rejected — the legacy two-space indent + AppendLine output is small,
  audited, and trivially reproducible; reaching the same bytes via
  XElement requires custom XmlWriterSettings tweaks that buy nothing
  beyond what StringBuilder already gives us.

Joints: serializes standard URDF <joint> blocks if model.Joints is
non-empty. v2.1 pipeline still passes empty joints, so this exists
for P2 (joint-graph pass) — covered by direct unit tests now.
*/
using System.Collections.Generic;
using System.Globalization;
using System.Security;
using System.Text;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.Write.Urdf
{
    public static class UrdfSerializer
    {
        /// Returns the URDF body XML (children of &lt;robot&gt;...). Byte-identical
        /// to the legacy Sw2gzPipeline.BuildUrdfBodyXml output for the same input,
        /// so existing golden tests pass without regeneration.
        public static string SerializeBody(RobotModel model)
        {
            string pkgEsc = SecurityElement.Escape(model.Meta.PackageName);
            var sb = new StringBuilder();

            foreach (ModelLink ml in model.Links)
                AppendLink(sb, ml, pkgEsc);

            foreach (UrdfJoint j in model.Joints)
                AppendJoint(sb, j);

            return sb.ToString();
        }

        /// P5 — emits the contents of inc/materials.xacro from a RobotModel's
        /// Materials list. Empty list => single placeholder comment so the
        /// file still parses as valid xacro. Floats use InvariantCulture so
        /// the test locale never injects a comma. All dynamic strings escape
        /// via SecurityElement.Escape; the sanitizer already restricts names
        /// to [A-Za-z0-9_], so this is defense-in-depth.
        public static string SerializeMaterialsXacro(string packageName, IReadOnlyList<MaterialDef> materials)
        {
            if (materials == null) throw new System.ArgumentNullException(nameof(materials));

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine("<robot xmlns:xacro=\"http://www.ros.org/wiki/xacro\">");

            if (materials.Count == 0)
            {
                sb.AppendLine("  <!-- No named materials defined. -->");
            }
            else
            {
                foreach (MaterialDef m in materials)
                {
                    string nameEsc = SecurityElement.Escape(m.Name);
                    sb.AppendLine($"  <material name=\"{nameEsc}\">");
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "    <color rgba=\"{0} {1} {2} {3}\"/>",
                        m.R, m.G, m.B, m.A));
                    sb.AppendLine("  </material>");
                }
            }

            sb.AppendLine("</robot>");
            return sb.ToString();
        }

        // Per-link emission. Byte-for-byte match of the legacy BuildUrdfBodyXml
        // when ml.MaterialName is null; emits a <material name="..."/> reference
        // inside the <visual> block when MaterialName is non-null (P5).
        private static void AppendLink(StringBuilder sb, ModelLink ml, string pkgEsc)
        {
            UrdfLink link = ml.Link;
            string nameEsc = SecurityElement.Escape(link.Name);
            sb.AppendLine($"  <link name=\"{nameEsc}\">");
            sb.AppendLine("    <inertial>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <origin xyz=\"{0} {1} {2}\" rpy=\"0 0 0\"/>",
                link.ComLocal.X, link.ComLocal.Y, link.ComLocal.Z));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <mass value=\"{0}\"/>", link.Mass));
            Matrix3 I = link.InertiaAtComLocal;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <inertia ixx=\"{0}\" ixy=\"{1}\" ixz=\"{2}\" iyy=\"{3}\" iyz=\"{4}\" izz=\"{5}\"/>",
                I.M11, I.M12, I.M13, I.M22, I.M23, I.M33));
            sb.AppendLine("    </inertial>");
            if (ml.MaterialName == null)
            {
                // Legacy byte-identical path: no <material> tag.
                sb.AppendLine("    <visual><geometry>");
                sb.AppendLine($"      <mesh filename=\"package://{pkgEsc}/meshes/{SecurityElement.Escape(link.VisualMeshFile)}\"/>");
                sb.AppendLine("    </geometry></visual>");
            }
            else
            {
                // P5: emit <material name="..."/> reference inside <visual>.
                // The full color block lives in inc/materials.xacro and is
                // pulled in by the xacro include — URDF best practice is to
                // reference by name, not duplicate the color inline.
                string matEsc = SecurityElement.Escape(ml.MaterialName);
                sb.AppendLine("    <visual>");
                sb.AppendLine("      <geometry>");
                sb.AppendLine($"        <mesh filename=\"package://{pkgEsc}/meshes/{SecurityElement.Escape(link.VisualMeshFile)}\"/>");
                sb.AppendLine("      </geometry>");
                sb.AppendLine($"      <material name=\"{matEsc}\"/>");
                sb.AppendLine("    </visual>");
            }
            sb.AppendLine("    <collision><geometry>");
            sb.AppendLine($"      <mesh filename=\"package://{pkgEsc}/meshes/{SecurityElement.Escape(link.CollisionMeshFile)}\"/>");
            sb.AppendLine("    </geometry></collision>");
            sb.AppendLine("  </link>");
        }

        // Standard URDF joint block. Optional limit attrs omitted when null
        // for revolute/prismatic; effort/velocity always emitted.
        private static void AppendJoint(StringBuilder sb, UrdfJoint j)
        {
            string nameEsc = SecurityElement.Escape(j.Name);
            string typeStr = JointTypeString(j.Type);
            string parentEsc = SecurityElement.Escape(j.ParentLink);
            string childEsc = SecurityElement.Escape(j.ChildLink);

            sb.AppendLine($"  <joint name=\"{nameEsc}\" type=\"{typeStr}\">");
            sb.AppendLine($"    <parent link=\"{parentEsc}\"/>");
            sb.AppendLine($"    <child link=\"{childEsc}\"/>");

            // TODO P2: convert j.Origin.Rotation (quaternion) to rpy; identity-only here.
            // Origin: position + rpy. Quaternion → rpy conversion stays in
            // P2 (the joint-graph pass); for now we emit position with rpy
            // "0 0 0" if rotation is identity, matching the link inertial
            // origin's behavior. If rotation is non-identity we still emit
            // "0 0 0" rpy as a P2 TODO — explicit so callers see the gap.
            var pos = j.Origin.Position;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "    <origin xyz=\"{0} {1} {2}\" rpy=\"0 0 0\"/>",
                pos.X, pos.Y, pos.Z));

            // Axis. URDF requires <axis> for revolute/continuous/prismatic;
            // we emit unconditionally for simplicity (harmless on fixed joints
            // since downstream xacro/parsers ignore it).
            if (j.Type != UrdfJointType.Fixed)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    <axis xyz=\"{0} {1} {2}\"/>",
                    j.Axis.X, j.Axis.Y, j.Axis.Z));
            }

            // Limits. Revolute/prismatic need lower/upper; continuous omits them.
            // effort and velocity are always emitted.
            if (j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Prismatic)
            {
                double lower = j.LimitLower ?? 0.0;
                double upper = j.LimitUpper ?? 0.0;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    <limit lower=\"{0}\" upper=\"{1}\" effort=\"{2}\" velocity=\"{3}\"/>",
                    lower, upper, j.LimitEffort, j.LimitVelocity));
            }
            else if (j.Type == UrdfJointType.Continuous)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    <limit effort=\"{0}\" velocity=\"{1}\"/>",
                    j.LimitEffort, j.LimitVelocity));
            }

            sb.AppendLine("  </joint>");
        }

        private static string JointTypeString(UrdfJointType t) => t switch
        {
            UrdfJointType.Fixed      => "fixed",
            UrdfJointType.Revolute   => "revolute",
            UrdfJointType.Continuous => "continuous",
            UrdfJointType.Prismatic  => "prismatic",
            _                        => throw new System.InvalidOperationException($"Unhandled UrdfJointType: {t}"),
        };
    }
}
