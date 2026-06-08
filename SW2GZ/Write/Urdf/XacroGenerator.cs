/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

XacroGenerator (formerly UrdfSerializer) — RobotModel → xacro/URDF emitter.
Consumes the immutable RobotModel and emits the children of <robot>...</robot>
(the fragment that XacroWriter wraps).

Output paths:
  SerializeBody(model)             — bare URDF body; byte-identical to legacy
                                     output, preserved for golden tests.
  SerializeBodyForRobot(model, r)  — full-stack robot URDF: prepends a world
                                     link + fixed joint to the root (so the
                                     robot is anchored in Gz), and applies
                                     nonzero defaults to zero effort/velocity
                                     joint limits (ros2_control + Gz physics
                                     reject zero).
  SerializeMaterialsXacro(mats)    — inc/materials.xacro contents.
  SerializeGazeboSensorBlocks(s)   — <gazebo reference=link> sensor blocks.

Formatting: StringBuilder with explicit two-space indent + InvariantCulture
floats + SecurityElement.Escape. XElement was considered but rejected for
the same byte-parity reason the original serializer kept it.
*/
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Security;
using System.Text;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Gz;
using SW2GZ.Math;

namespace SW2GZ.Write.Urdf
{
    public static class XacroGenerator
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
                AppendJoint(sb, j, DefaultEffort, DefaultVelocity, applyDefaults: false);

            sb.Append(SerializeGazeboSensorBlocks(model.Sensors));

            return sb.ToString();
        }

        // Defaults applied to revolute/prismatic/continuous joints when the
        // source mate carried zero limits — URDF accepts zero but ros2_control
        // JTC + Gz physics treat them as "no actuation possible".
        private const double DefaultEffort   = 100.0;
        private const double DefaultVelocity = 1.0;

        /// Robot-ready URDF body: prepends a `<link name="world"/>` plus a fixed
        /// joint anchoring the root link to it (so the robot doesn't fall in Gz),
        /// and applies nonzero defaults to any zero-effort / zero-velocity joint
        /// limits. Used by the full-stack export path; tests of pure
        /// `SerializeBody(model)` remain byte-identical.
        public static string SerializeBodyForRobot(RobotModel model, string rootLink)
            => SerializeBodyForRobot(model, rootLink, emitWorldLink: false);

        /// `emitWorldLink` controls whether the body is prefixed with
        /// `<link name="world"/>` + a `world_to_<root>` fixed joint. ROS REP-105
        /// convention is that `base_link` IS the root (no world frame in URDF);
        /// external `static_transform_publisher` provides world placement when
        /// needed. Set true to embed the world frame for fixed-base manipulators
        /// (Gz anchoring via the URDF itself rather than launch-side spawn args).
        public static string SerializeBodyForRobot(RobotModel model, string rootLink, bool emitWorldLink)
        {
            if (model == null) throw new System.ArgumentNullException(nameof(model));
            string pkgEsc = SecurityElement.Escape(model.Meta.PackageName);
            var sb = new StringBuilder();

            string anchor = string.IsNullOrEmpty(rootLink) && model.Links.Count > 0
                ? model.Links[0].Link.Name
                : rootLink;
            if (emitWorldLink && !string.IsNullOrEmpty(anchor))
            {
                string anchorEsc = SecurityElement.Escape(anchor);
                // SW→ROS rotation rides on this fixed joint. Mesh frames and
                // joint origins inside the body stay in native SW frame; this
                // single rotation puts the whole robot into ROS world.
                (double roll, double pitch, double yaw) =
                    model.Meta.Frame.SwToRos.ToRpy();
                string rpyStr =
                    roll.ToString("0.######", CultureInfo.InvariantCulture)  + " " +
                    pitch.ToString("0.######", CultureInfo.InvariantCulture) + " " +
                    yaw.ToString("0.######", CultureInfo.InvariantCulture);
                sb.AppendLine("  <link name=\"world\"/>");
                sb.AppendLine($"  <joint name=\"world_to_{anchorEsc}\" type=\"fixed\">");
                sb.AppendLine("    <parent link=\"world\"/>");
                sb.AppendLine($"    <child link=\"{anchorEsc}\"/>");
                sb.AppendLine($"    <origin xyz=\"0 0 0\" rpy=\"{rpyStr}\"/>");
                sb.AppendLine("  </joint>");
            }

            foreach (ModelLink ml in model.Links)
                AppendLink(sb, ml, pkgEsc);

            foreach (UrdfJoint j in model.Joints)
                AppendJoint(sb, j, DefaultEffort, DefaultVelocity, applyDefaults: true);

            sb.Append(SerializeGazeboSensorBlocks(model.Sensors));
            return sb.ToString();
        }

        /// P6-data — for each sensor in `sensors`, emits a
        /// <gazebo reference="$AttachedLink"> wrapper containing the SDF
        /// <sensor> block from SdfSensorBlocks. Sensors that share an
        /// AttachedLink are grouped into one <gazebo> wrapper (order
        /// preserved within the group); the group order matches the first
        /// occurrence of each link in `sensors`.
        public static string SerializeGazeboSensorBlocks(IReadOnlyList<SensorDef> sensors)
        {
            if (sensors == null || sensors.Count == 0)
                return string.Empty;

            var order = new List<string>();
            var groups = new Dictionary<string, List<SensorDef>>(System.StringComparer.Ordinal);
            foreach (SensorDef s in sensors)
            {
                if (!groups.TryGetValue(s.AttachedLink, out List<SensorDef> bucket))
                {
                    bucket = new List<SensorDef>();
                    groups[s.AttachedLink] = bucket;
                    order.Add(s.AttachedLink);
                }
                bucket.Add(s);
            }

            var sb = new StringBuilder();
            foreach (string linkName in order)
            {
                string linkEsc = SecurityElement.Escape(linkName);
                sb.AppendLine($"  <gazebo reference=\"{linkEsc}\">");
                foreach (SensorDef s in groups[linkName])
                {
                    sb.Append(SdfSensorBlocks.Write(s, indentSpaces: 4));
                }
                sb.AppendLine("  </gazebo>");
            }
            return sb.ToString();
        }

        /// P5 — emits the contents of inc/materials.xacro from a RobotModel's
        /// Materials list. Empty list => single placeholder comment so the
        /// file still parses as valid xacro. Floats use InvariantCulture so
        /// the test locale never injects a comma. All dynamic strings escape
        /// via SecurityElement.Escape; the sanitizer already restricts names
        /// to [A-Za-z0-9_], so this is defense-in-depth.
        public static string SerializeMaterialsXacro(IReadOnlyList<MaterialDef> materials)
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
        // when ml.MaterialName is null AND link.FrameOffset == Vector3.Zero;
        // emits a <material name="..."/> reference inside the <visual> block
        // when MaterialName is non-null (P5).
        //
        // When link.FrameOffset is non-zero the link's URDF frame is at a
        // mate-reference point and the mesh/inertial must shift by FrameOffset
        // to keep their world placement: <visual>/<collision> get an explicit
        // <origin xyz="<offset>" rpy="0 0 0"/>, and the inertial <origin>
        // adds the offset to ComLocal (the rotation between the part frame
        // and the link frame is identity by construction, so the principal-axis
        // inertia tensor is unchanged).
        private static void AppendLink(StringBuilder sb, ModelLink ml, string pkgEsc)
        {
            UrdfLink link = ml.Link;
            string nameEsc = SecurityElement.Escape(link.Name);
            Vector3 off = link.FrameOffset;
            bool hasOff = off.X != 0f || off.Y != 0f || off.Z != 0f;

            sb.AppendLine($"  <link name=\"{nameEsc}\">");
            sb.AppendLine("    <inertial>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <origin xyz=\"{0} {1} {2}\" rpy=\"0 0 0\"/>",
                link.ComLocal.X + off.X, link.ComLocal.Y + off.Y, link.ComLocal.Z + off.Z));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <mass value=\"{0}\"/>", link.Mass));
            Matrix3 I = link.InertiaAtComLocal;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <inertia ixx=\"{0}\" ixy=\"{1}\" ixz=\"{2}\" iyy=\"{3}\" iyz=\"{4}\" izz=\"{5}\"/>",
                I.M11, I.M12, I.M13, I.M22, I.M23, I.M33));
            sb.AppendLine("    </inertial>");
            if (ml.MaterialName == null)
            {
                if (!hasOff)
                {
                    sb.AppendLine("    <visual><geometry>");
                    sb.AppendLine($"      <mesh filename=\"package://{pkgEsc}/meshes/{SecurityElement.Escape(link.VisualMeshFile)}\"/>");
                    sb.AppendLine("    </geometry></visual>");
                }
                else
                {
                    sb.AppendLine("    <visual>");
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "      <origin xyz=\"{0} {1} {2}\" rpy=\"0 0 0\"/>",
                        off.X, off.Y, off.Z));
                    sb.AppendLine("      <geometry>");
                    sb.AppendLine($"        <mesh filename=\"package://{pkgEsc}/meshes/{SecurityElement.Escape(link.VisualMeshFile)}\"/>");
                    sb.AppendLine("      </geometry>");
                    sb.AppendLine("    </visual>");
                }
            }
            else
            {
                string matEsc = SecurityElement.Escape(ml.MaterialName);
                sb.AppendLine("    <visual>");
                if (hasOff)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "      <origin xyz=\"{0} {1} {2}\" rpy=\"0 0 0\"/>",
                        off.X, off.Y, off.Z));
                }
                sb.AppendLine("      <geometry>");
                sb.AppendLine($"        <mesh filename=\"package://{pkgEsc}/meshes/{SecurityElement.Escape(link.VisualMeshFile)}\"/>");
                sb.AppendLine("      </geometry>");
                sb.AppendLine($"      <material name=\"{matEsc}\"/>");
                sb.AppendLine("    </visual>");
            }
            if (!hasOff)
            {
                sb.AppendLine("    <collision><geometry>");
                sb.AppendLine($"      <mesh filename=\"package://{pkgEsc}/meshes/{SecurityElement.Escape(link.CollisionMeshFile)}\"/>");
                sb.AppendLine("    </geometry></collision>");
            }
            else
            {
                sb.AppendLine("    <collision>");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "      <origin xyz=\"{0} {1} {2}\" rpy=\"0 0 0\"/>",
                    off.X, off.Y, off.Z));
                sb.AppendLine("      <geometry>");
                sb.AppendLine($"        <mesh filename=\"package://{pkgEsc}/meshes/{SecurityElement.Escape(link.CollisionMeshFile)}\"/>");
                sb.AppendLine("      </geometry>");
                sb.AppendLine("    </collision>");
            }
            sb.AppendLine("  </link>");
        }

        // Standard URDF joint block. Optional limit attrs omitted when null
        // for revolute/prismatic; effort/velocity always emitted. When
        // applyDefaults is true, zero-valued effort/velocity are replaced with
        // the robot-ready nonzero defaults — ros2_control + Gz won't actuate
        // joints whose limit fields say "0".
        private static void AppendJoint(StringBuilder sb, UrdfJoint j, double defEffort, double defVelocity, bool applyDefaults)
        {
            double effort   = applyDefaults && j.LimitEffort   == 0.0 ? defEffort   : j.LimitEffort;
            double velocity = applyDefaults && j.LimitVelocity == 0.0 ? defVelocity : j.LimitVelocity;
            string nameEsc = SecurityElement.Escape(j.Name);
            string typeStr = JointTypeString(j.Type);
            string parentEsc = SecurityElement.Escape(j.ParentLink);
            string childEsc = SecurityElement.Escape(j.ChildLink);

            sb.AppendLine($"  <joint name=\"{nameEsc}\" type=\"{typeStr}\">");
            sb.AppendLine($"    <parent link=\"{parentEsc}\"/>");
            sb.AppendLine($"    <child link=\"{childEsc}\"/>");

            var pos = j.Origin.Position;
            var (roll, pitch, yaw) = Matrix3.FromQuaternion(j.Origin.Rotation).ToRpy();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "    <origin xyz=\"{0} {1} {2}\" rpy=\"{3} {4} {5}\"/>",
                pos.X, pos.Y, pos.Z, roll, pitch, yaw));

            if (j.Type != UrdfJointType.Fixed && j.Type != UrdfJointType.Floating)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    <axis xyz=\"{0} {1} {2}\"/>",
                    j.Axis.X, j.Axis.Y, j.Axis.Z));
            }

            if (j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Prismatic)
            {
                double lower = j.LimitLower ?? 0.0;
                double upper = j.LimitUpper ?? 0.0;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    <limit lower=\"{0}\" upper=\"{1}\" effort=\"{2}\" velocity=\"{3}\"/>",
                    lower, upper, effort, velocity));
            }
            else if (j.Type == UrdfJointType.Continuous)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    <limit effort=\"{0}\" velocity=\"{1}\"/>",
                    effort, velocity));
            }

            sb.AppendLine("  </joint>");
        }

        private static string JointTypeString(UrdfJointType t) => t switch
        {
            UrdfJointType.Fixed      => "fixed",
            UrdfJointType.Revolute   => "revolute",
            UrdfJointType.Continuous => "continuous",
            UrdfJointType.Prismatic  => "prismatic",
            UrdfJointType.Planar     => "planar",
            UrdfJointType.Floating   => "floating",
            _                        => throw new System.InvalidOperationException($"Unhandled UrdfJointType: {t}"),
        };
    }
}
