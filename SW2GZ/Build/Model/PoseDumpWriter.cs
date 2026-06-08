/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Diagnostic dump emitted next to the export output every run. Captures
the SW-frame data the pipeline used to position links and joints so a
mis-located robot in Gz Sim can be debugged offline without re-running
the export — and, critically, without trusting the column-major vs
row-major interpretation of Transform2.ArrayData (the raw 16 doubles
are dumped alongside the extracted Pose so the two can be compared).

Per-link section emits:
  - link name + first part path
  - extracted anchor Pose (xyz, quaternion)
  - the 16 raw ArrayData doubles when the walker exposes IComponentRawTransformSource
Per-joint section emits:
  - parent / child link names
  - parent + child anchor Pose
  - URDF <origin> xyz + rpy (already in parent frame)
  - axis BEFORE the child-frame re-expression (= assembly-frame axis,
    pulled from the original MateSpec / wizard JointDef via the
    `axisAssembly` callback)
  - axis AFTER re-expression (= the URDF emitted axis)
  - joint type + limits

Pure / file-system-only; no SW dependencies.
*/
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;

namespace SW2GZ.Build.Model
{
    public static class PoseDumpWriter
    {
        /// Build the dump string. Pure: takes resolved data, no I/O.
        public static string Build(
            string packageName,
            IReadOnlyList<LinkSpec> specs,
            IReadOnlyDictionary<string, Pose> linkAnchors,
            IReadOnlyList<UrdfJoint> joints,
            IReadOnlyDictionary<string, Vector3> jointAxesAssembly,
            IComponentRawTransformSource rawSource)
        {
            var sb = new StringBuilder();
            CultureInfo c = CultureInfo.InvariantCulture;

            sb.AppendLine("SW2GZ Pose Dump");
            sb.AppendLine("================");
            sb.AppendLine("Package: " + (packageName ?? "(unset)"));
            sb.AppendLine();
            sb.AppendLine("Conventions:");
            sb.AppendLine("  Pose = (xyz translation, quaternion x y z w).");
            sb.AppendLine("  RawArrayData16 is Component2.Transform2.ArrayData verbatim from SW");
            sb.AppendLine("  (indices 0..8 = 3x3 rotation; 9..11 = translation; 12 = scale; 13..15 padding).");
            sb.AppendLine("  Joint origin xyz/rpy already in PARENT-link frame (URDF emit).");
            sb.AppendLine("  AxisAssembly = mate axis as read from SW assembly frame (BEFORE re-expression).");
            sb.AppendLine("  AxisChildFrame = axis as written into URDF (AFTER childAnchor.Rotation^-1).");
            sb.AppendLine();

            // ── Links ──────────────────────────────────────────────────────
            sb.AppendLine("Links (" + (specs?.Count ?? 0) + "):");
            if (specs != null)
            {
                foreach (LinkSpec spec in specs)
                {
                    if (spec == null) continue;
                    string firstPart = (spec.FlattenedPartPaths != null && spec.FlattenedPartPaths.Count > 0)
                        ? spec.FlattenedPartPaths[0] : "(none)";
                    sb.AppendLine("  - " + spec.Name);
                    sb.AppendLine("      FirstPart: " + firstPart);
                    Pose anchor = (linkAnchors != null && linkAnchors.TryGetValue(spec.Name, out Pose a))
                        ? a : Pose.Identity;
                    sb.AppendLine("      Anchor.xyz:  " + Fmt(anchor.Position, c));
                    sb.AppendLine("      Anchor.quat: " + Fmt(anchor.Rotation, c));

                    double[] raw = (rawSource != null && firstPart != "(none)")
                        ? rawSource.GetComponentRawTransform(firstPart) : null;
                    if (raw != null)
                    {
                        sb.Append("      RawArrayData16: [");
                        for (int i = 0; i < raw.Length; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(raw[i].ToString("R", c));
                        }
                        sb.AppendLine("]");
                    }
                    else
                    {
                        sb.AppendLine("      RawArrayData16: (unavailable)");
                    }
                }
            }
            sb.AppendLine();

            // ── Joints ─────────────────────────────────────────────────────
            sb.AppendLine("Joints (" + (joints?.Count ?? 0) + "):");
            if (joints != null)
            {
                foreach (UrdfJoint j in joints)
                {
                    if (j == null) continue;
                    sb.AppendLine("  - " + j.Name + " [" + j.Type + "]");
                    sb.AppendLine("      Parent: " + j.ParentLink);
                    sb.AppendLine("      Child:  " + j.ChildLink);

                    Pose pA = (linkAnchors != null && linkAnchors.TryGetValue(j.ParentLink, out Pose pp))
                        ? pp : Pose.Identity;
                    Pose cA = (linkAnchors != null && linkAnchors.TryGetValue(j.ChildLink, out Pose cc))
                        ? cc : Pose.Identity;
                    sb.AppendLine("      ParentAnchor.xyz:  " + Fmt(pA.Position, c));
                    sb.AppendLine("      ParentAnchor.quat: " + Fmt(pA.Rotation, c));
                    sb.AppendLine("      ChildAnchor.xyz:   " + Fmt(cA.Position, c));
                    sb.AppendLine("      ChildAnchor.quat:  " + Fmt(cA.Rotation, c));

                    sb.AppendLine("      Origin.xyz: " + Fmt(j.Origin.Position, c));
                    (double roll, double pitch, double yaw) = Matrix3.FromQuaternion(j.Origin.Rotation).ToRpy();
                    sb.AppendLine("      Origin.rpy: " +
                        roll.ToString("R", c) + " " +
                        pitch.ToString("R", c) + " " +
                        yaw.ToString("R", c));

                    Vector3 axisAssembly = (jointAxesAssembly != null &&
                        jointAxesAssembly.TryGetValue(j.Name, out Vector3 axA))
                        ? axA : j.Axis;
                    sb.AppendLine("      AxisAssembly:   " + Fmt(axisAssembly, c));
                    sb.AppendLine("      AxisChildFrame: " + Fmt(j.Axis, c));

                    sb.AppendLine("      LimitLower: " + (j.LimitLower?.ToString("R", c) ?? "(none)"));
                    sb.AppendLine("      LimitUpper: " + (j.LimitUpper?.ToString("R", c) ?? "(none)"));
                    sb.AppendLine("      LimitEffort:   " + j.LimitEffort.ToString("R", c));
                    sb.AppendLine("      LimitVelocity: " + j.LimitVelocity.ToString("R", c));
                }
            }

            return sb.ToString();
        }

        /// Write the dump to `path`. Creates parent directory if missing.
        /// Best-effort: caller should catch + log; this routine intentionally
        /// throws on hard I/O failures so tests can assert success.
        public static void Write(
            string path,
            string packageName,
            IReadOnlyList<LinkSpec> specs,
            IReadOnlyDictionary<string, Pose> linkAnchors,
            IReadOnlyList<UrdfJoint> joints,
            IReadOnlyDictionary<string, Vector3> jointAxesAssembly,
            IComponentRawTransformSource rawSource)
        {
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);
            File.WriteAllText(path, Build(packageName, specs, linkAnchors, joints,
                jointAxesAssembly, rawSource));
        }

        private static string Fmt(Vector3 v, CultureInfo c) =>
            v.X.ToString("R", c) + " " + v.Y.ToString("R", c) + " " + v.Z.ToString("R", c);

        private static string Fmt(Quaternion q, CultureInfo c) =>
            q.X.ToString("R", c) + " " + q.Y.ToString("R", c) + " " +
            q.Z.ToString("R", c) + " " + q.W.ToString("R", c);
    }
}
