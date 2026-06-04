/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Renders an ASCII TF tree from a generated URDF or SDF document so the
Export-preview dialog can show users exactly which frames the published
robot_state_publisher (URDF) or Gz model (SDF) will broadcast.

Source of truth is the emitted document, NOT the wizard config — joint
origins / axes / limits are filled in by the pipeline, so parsing the
output mirrors what robot_state_publisher / Gz actually see.

Pure / COM-free. Best-effort XML parsing: on a parse failure the helper
returns a friendly error string rather than throwing, so a malformed
xacro doesn't sink the preview.
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;

namespace SW2GZ.Build.Model
{
    public static class TfTreeFormatter
    {
        /// Parses URDF/xacro text and returns an ASCII tree of <link> frames
        /// joined by <joint> edges. Defensive: returns a friendly message on
        /// parse failure or when there is no recognisable structure.
        public static string FormatUrdf(string urdfXml)
        {
            if (string.IsNullOrWhiteSpace(urdfXml))
                return "(URDF was empty — nothing to render)";

            XmlDocument doc;
            try
            {
                doc = new XmlDocument();
                // xacro files reference the xacro xmlns; LoadXml accepts it
                // and we ignore the unprocessed macros (they don't affect
                // the link/joint topology we extract).
                doc.LoadXml(urdfXml);
            }
            catch (Exception e)
            {
                return "(could not parse URDF: " + e.Message + ")";
            }

            // Collect every <link> and <joint> regardless of nesting depth;
            // xacro macros may push some under intermediate elements.
            var linkNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement el in doc.GetElementsByTagName("link"))
            {
                string n = el.GetAttribute("name");
                if (!string.IsNullOrEmpty(n)) linkNames.Add(n);
            }

            var joints = new List<UrdfJointInfo>();
            foreach (XmlElement el in doc.GetElementsByTagName("joint"))
            {
                UrdfJointInfo info = ParseUrdfJoint(el);
                if (info != null) joints.Add(info);
            }

            if (linkNames.Count == 0)
                return "(no <link> elements found)";

            // Children-by-parent map; a parent listed in a joint that does
            // not appear as a <link> is still a valid intermediate (e.g. the
            // "world" link is sometimes implicit), so seed it as a synthetic
            // link too so the tree walk reaches it.
            var children = new Dictionary<string, List<UrdfJointInfo>>(StringComparer.Ordinal);
            var hasIncomingEdge = new HashSet<string>(StringComparer.Ordinal);
            foreach (UrdfJointInfo j in joints)
            {
                if (!string.IsNullOrEmpty(j.Parent))
                {
                    if (!linkNames.Contains(j.Parent)) linkNames.Add(j.Parent);
                    if (!children.TryGetValue(j.Parent, out List<UrdfJointInfo> list))
                    {
                        list = new List<UrdfJointInfo>();
                        children[j.Parent] = list;
                    }
                    list.Add(j);
                }
                if (!string.IsNullOrEmpty(j.Child)) hasIncomingEdge.Add(j.Child);
            }

            // Roots = links with no incoming joint edge.
            var roots = new List<string>();
            foreach (string ln in linkNames)
                if (!hasIncomingEdge.Contains(ln)) roots.Add(ln);
            roots.Sort(StringComparer.Ordinal);

            if (roots.Count == 0)
                return "(no root link — every link has an incoming joint; possible cycle)";

            var sb = new StringBuilder();
            sb.AppendLine("TF tree (published by robot_state_publisher):");
            sb.AppendLine();
            foreach (string r in roots)
            {
                sb.AppendLine(r);
                WriteUrdfChildren(sb, r, children, prefix: "", visited: new HashSet<string>());
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        /// Parses SDF text and returns an ASCII tree of <link>s joined by
        /// <joint>s. SDF uses <parent> and <child> as element text (not the
        /// URDF "<parent link=name>" attribute form), so the parse path is
        /// distinct from the URDF one.
        public static string FormatSdf(string sdfXml)
        {
            if (string.IsNullOrWhiteSpace(sdfXml))
                return "(SDF was empty — nothing to render)";

            XmlDocument doc;
            try
            {
                doc = new XmlDocument();
                doc.LoadXml(sdfXml);
            }
            catch (Exception e)
            {
                return "(could not parse SDF: " + e.Message + ")";
            }

            var linkNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement el in doc.GetElementsByTagName("link"))
            {
                string n = el.GetAttribute("name");
                if (!string.IsNullOrEmpty(n)) linkNames.Add(n);
            }

            var joints = new List<SdfJointInfo>();
            foreach (XmlElement el in doc.GetElementsByTagName("joint"))
            {
                SdfJointInfo info = ParseSdfJoint(el);
                if (info != null) joints.Add(info);
            }

            if (linkNames.Count == 0)
                return "(no <link> elements found in SDF)";

            var children = new Dictionary<string, List<SdfJointInfo>>(StringComparer.Ordinal);
            var hasIncoming = new HashSet<string>(StringComparer.Ordinal);
            foreach (SdfJointInfo j in joints)
            {
                if (!string.IsNullOrEmpty(j.Parent))
                {
                    if (!linkNames.Contains(j.Parent)) linkNames.Add(j.Parent);
                    if (!children.TryGetValue(j.Parent, out List<SdfJointInfo> list))
                    {
                        list = new List<SdfJointInfo>();
                        children[j.Parent] = list;
                    }
                    list.Add(j);
                }
                if (!string.IsNullOrEmpty(j.Child)) hasIncoming.Add(j.Child);
            }

            var roots = linkNames.Where(l => !hasIncoming.Contains(l))
                                 .OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (roots.Count == 0)
                return "(no root link in SDF — possible cycle)";

            var sb = new StringBuilder();
            sb.AppendLine("TF tree (SDF model frames):");
            sb.AppendLine();
            foreach (string r in roots)
            {
                sb.AppendLine(r);
                WriteSdfChildren(sb, r, children, prefix: "", visited: new HashSet<string>());
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        // ──────────────────────────────────────────────────────────────────
        // URDF helpers
        // ──────────────────────────────────────────────────────────────────

        private sealed class UrdfJointInfo
        {
            public string Name;
            public string Type;
            public string Parent;
            public string Child;
            public string Xyz;
            public string Rpy;
            public string Axis;
            public string LimitLower, LimitUpper, LimitEffort, LimitVelocity;
        }

        private static UrdfJointInfo ParseUrdfJoint(XmlElement el)
        {
            string parent = FindChild(el, "parent")?.GetAttribute("link");
            string child  = FindChild(el, "child")?.GetAttribute("link");
            if (string.IsNullOrEmpty(parent) && string.IsNullOrEmpty(child)) return null;

            var info = new UrdfJointInfo
            {
                Name = el.GetAttribute("name"),
                Type = el.GetAttribute("type"),
                Parent = parent,
                Child  = child,
            };
            XmlElement origin = FindChild(el, "origin");
            if (origin != null)
            {
                info.Xyz = origin.GetAttribute("xyz");
                info.Rpy = origin.GetAttribute("rpy");
            }
            XmlElement axis = FindChild(el, "axis");
            if (axis != null) info.Axis = axis.GetAttribute("xyz");

            XmlElement limit = FindChild(el, "limit");
            if (limit != null)
            {
                info.LimitLower = limit.GetAttribute("lower");
                info.LimitUpper = limit.GetAttribute("upper");
                info.LimitEffort = limit.GetAttribute("effort");
                info.LimitVelocity = limit.GetAttribute("velocity");
            }
            return info;
        }

        private static void WriteUrdfChildren(StringBuilder sb, string linkName,
            Dictionary<string, List<UrdfJointInfo>> children,
            string prefix, HashSet<string> visited)
        {
            if (!visited.Add(linkName)) return;   // cycle guard
            if (!children.TryGetValue(linkName, out List<UrdfJointInfo> kids)) return;

            for (int i = 0; i < kids.Count; i++)
            {
                UrdfJointInfo j = kids[i];
                bool last = (i == kids.Count - 1);
                string branch = last ? "└── " : "├── ";
                string indent = last ? "    " : "│   ";

                sb.AppendLine(prefix + branch + (j.Child ?? "?") +
                    "    [" + (string.IsNullOrEmpty(j.Type) ? "?" : j.Type) +
                    ": " + (string.IsNullOrEmpty(j.Name) ? "?" : j.Name) + "]");

                string indented = prefix + indent;
                if (!string.IsNullOrEmpty(j.Xyz))
                    sb.AppendLine(indented + "  xyz: " + j.Xyz);
                if (!string.IsNullOrEmpty(j.Rpy))
                    sb.AppendLine(indented + "  rpy: " + j.Rpy + RpyDegSuffix(j.Rpy));
                if (!string.IsNullOrEmpty(j.Axis))
                    sb.AppendLine(indented + "  axis: " + j.Axis);
                string limitLine = FormatUrdfLimits(j);
                if (!string.IsNullOrEmpty(limitLine))
                    sb.AppendLine(indented + "  " + limitLine);

                WriteUrdfChildren(sb, j.Child, children, indented, visited);
            }
        }

        private static string FormatUrdfLimits(UrdfJointInfo j)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(j.LimitLower) || !string.IsNullOrEmpty(j.LimitUpper))
                parts.Add("limit: " + (j.LimitLower ?? "?") + " .. " + (j.LimitUpper ?? "?"));
            if (!string.IsNullOrEmpty(j.LimitEffort)) parts.Add("effort=" + j.LimitEffort);
            if (!string.IsNullOrEmpty(j.LimitVelocity)) parts.Add("velocity=" + j.LimitVelocity);
            return string.Join("  ", parts);
        }

        // Appends "(deg: a b c)" alongside an "rpy" attribute so the SW→ROS
        // rotation on world_to_<root> is legible at a glance.
        private static string RpyDegSuffix(string rpy)
        {
            string[] parts = rpy.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return string.Empty;
            double[] vals = new double[3];
            for (int i = 0; i < 3; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out vals[i]))
                    return string.Empty;
            double rad2deg = 180.0 / System.Math.PI;
            return "   (deg: " +
                (vals[0] * rad2deg).ToString("0.##", CultureInfo.InvariantCulture) + " " +
                (vals[1] * rad2deg).ToString("0.##", CultureInfo.InvariantCulture) + " " +
                (vals[2] * rad2deg).ToString("0.##", CultureInfo.InvariantCulture) + ")";
        }

        // ──────────────────────────────────────────────────────────────────
        // SDF helpers
        // ──────────────────────────────────────────────────────────────────

        private sealed class SdfJointInfo
        {
            public string Name, Type, Parent, Child, Pose, AxisXyz;
        }

        private static SdfJointInfo ParseSdfJoint(XmlElement el)
        {
            XmlElement parentEl = FindChild(el, "parent");
            XmlElement childEl  = FindChild(el, "child");
            string parent = parentEl?.InnerText?.Trim();
            string child  = childEl?.InnerText?.Trim();
            if (string.IsNullOrEmpty(parent) && string.IsNullOrEmpty(child)) return null;

            var info = new SdfJointInfo
            {
                Name = el.GetAttribute("name"),
                Type = el.GetAttribute("type"),
                Parent = parent,
                Child  = child,
            };
            XmlElement pose = FindChild(el, "pose");
            if (pose != null) info.Pose = pose.InnerText?.Trim();
            XmlElement axis = FindChild(el, "axis");
            if (axis != null)
            {
                XmlElement axyz = FindChild(axis, "xyz");
                if (axyz != null) info.AxisXyz = axyz.InnerText?.Trim();
            }
            return info;
        }

        private static void WriteSdfChildren(StringBuilder sb, string linkName,
            Dictionary<string, List<SdfJointInfo>> children,
            string prefix, HashSet<string> visited)
        {
            if (!visited.Add(linkName)) return;
            if (!children.TryGetValue(linkName, out List<SdfJointInfo> kids)) return;

            for (int i = 0; i < kids.Count; i++)
            {
                SdfJointInfo j = kids[i];
                bool last = (i == kids.Count - 1);
                string branch = last ? "└── " : "├── ";
                string indent = last ? "    " : "│   ";

                sb.AppendLine(prefix + branch + (j.Child ?? "?") +
                    "    [" + (string.IsNullOrEmpty(j.Type) ? "?" : j.Type) +
                    ": " + (string.IsNullOrEmpty(j.Name) ? "?" : j.Name) + "]");

                string indented = prefix + indent;
                if (!string.IsNullOrEmpty(j.Pose))
                    sb.AppendLine(indented + "  pose: " + j.Pose);
                if (!string.IsNullOrEmpty(j.AxisXyz))
                    sb.AppendLine(indented + "  axis: " + j.AxisXyz);

                WriteSdfChildren(sb, j.Child, children, indented, visited);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // Shared
        // ──────────────────────────────────────────────────────────────────

        // First direct child with the given local name. Used in place of
        // XPath so namespaced documents (xacro, sdf) work without extra
        // namespace-manager setup.
        private static XmlElement FindChild(XmlElement parent, string localName)
        {
            foreach (XmlNode n in parent.ChildNodes)
                if (n is XmlElement e && string.Equals(e.LocalName, localName, StringComparison.Ordinal))
                    return e;
            return null;
        }
    }
}
