/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Walks the joint tree of a generated URDF and computes the 4x4 transform
of each link in the world (root) frame. The in-app 3D preview applies
the matrices to position each link's collision mesh.

URDF joint origin convention (REP-0035 / urdf_parser):
  - <origin xyz="x y z" rpy="r p y"/>  is the transform from the parent
    link's frame to the child link's frame.
  - rpy is fixed-axis XYZ (extrinsic): R = Rz(y) * Ry(p) * Rx(r).
  - Composition: T_link_world = T_parent_world * Translate(xyz) * R_rpy.

Returns world-frame matrices keyed by link name. Roots are at identity.
Disconnected sub-trees each get their own identity-rooted walk. Cycles
are guarded so a malformed URDF doesn't infinite-loop.

Pure / COM-free.
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Xml;

namespace SW2GZ.Build.Model
{
    public static class UrdfTransforms
    {
        public sealed class LinkPlacement
        {
            public string LinkName { get; }
            public Matrix4x4 LinkToWorld { get; }
            public LinkPlacement(string n, Matrix4x4 m) { LinkName = n; LinkToWorld = m; }
        }

        public static IReadOnlyList<LinkPlacement> Compute(string urdfXml)
        {
            var result = new List<LinkPlacement>();
            if (string.IsNullOrWhiteSpace(urdfXml)) return result;

            XmlDocument doc;
            try
            {
                doc = new XmlDocument();
                doc.LoadXml(urdfXml);
            }
            catch
            {
                return result;
            }

            var linkNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement el in doc.GetElementsByTagName("link"))
            {
                string n = el.GetAttribute("name");
                if (!string.IsNullOrEmpty(n)) linkNames.Add(n);
            }

            // Joints by parent link + every link's parent edge (for root detection
            // and to fetch the parent→child local transform during the walk).
            var childEdges = new Dictionary<string, List<(string child, Matrix4x4 local)>>(StringComparer.Ordinal);
            var hasIncoming = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement j in doc.GetElementsByTagName("joint"))
            {
                string parent = FindChild(j, "parent")?.GetAttribute("link");
                string child  = FindChild(j, "child")?.GetAttribute("link");
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child)) continue;

                Matrix4x4 local = ParseOrigin(FindChild(j, "origin"));
                if (!childEdges.TryGetValue(parent, out var list))
                {
                    list = new List<(string, Matrix4x4)>();
                    childEdges[parent] = list;
                }
                list.Add((child, local));
                hasIncoming.Add(child);

                if (!linkNames.Contains(parent)) linkNames.Add(parent);
                if (!linkNames.Contains(child))  linkNames.Add(child);
            }

            // Roots = links without an incoming joint edge.
            var roots = new List<string>();
            foreach (string ln in linkNames)
                if (!hasIncoming.Contains(ln)) roots.Add(ln);
            roots.Sort(StringComparer.Ordinal);

            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string root in roots)
                Walk(root, Matrix4x4.Identity, childEdges, visited, result);

            return result;
        }

        private static void Walk(
            string link, Matrix4x4 linkToWorld,
            Dictionary<string, List<(string child, Matrix4x4 local)>> children,
            HashSet<string> visited, List<LinkPlacement> output)
        {
            if (!visited.Add(link)) return;   // cycle guard
            output.Add(new LinkPlacement(link, linkToWorld));
            if (!children.TryGetValue(link, out var kids)) return;

            foreach (var (childName, localChildInParent) in kids)
            {
                // System.Numerics.Matrix4x4 is row-vector convention (v * M),
                // so the standard "parent * local" composition flips to
                // "local * parent" to land the same world matrix.
                Matrix4x4 childToWorld = localChildInParent * linkToWorld;
                Walk(childName, childToWorld, children, visited, output);
            }
        }

        // Parses <origin xyz="..." rpy="..."/> into a single 4x4 matrix.
        // Missing origin element → identity. RPY uses URDF's fixed-axis XYZ
        // convention: R = Rz(y) * Ry(p) * Rx(r).
        private static Matrix4x4 ParseOrigin(XmlElement origin)
        {
            if (origin == null) return Matrix4x4.Identity;
            (float x, float y, float z) = ParseTriple(origin.GetAttribute("xyz"));
            (float r, float p, float yw) = ParseTriple(origin.GetAttribute("rpy"));
            Matrix4x4 rot = Matrix4x4.CreateRotationX(r) *
                            Matrix4x4.CreateRotationY(p) *
                            Matrix4x4.CreateRotationZ(yw);
            Matrix4x4 tr = Matrix4x4.CreateTranslation(x, y, z);
            // System.Numerics row-vector composition: v * R * Tr puts R first,
            // then translation — matches the URDF expectation that translation
            // is applied in the parent frame after rotation.
            return rot * tr;
        }

        private static (float, float, float) ParseTriple(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return (0, 0, 0);
            string[] parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return (0, 0, 0);
            float[] vals = new float[3];
            for (int i = 0; i < 3; i++)
                float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out vals[i]);
            return (vals[0], vals[1], vals[2]);
        }

        private static XmlElement FindChild(XmlElement parent, string localName)
        {
            if (parent == null) return null;
            foreach (XmlNode n in parent.ChildNodes)
                if (n is XmlElement e && string.Equals(e.LocalName, localName, StringComparison.Ordinal))
                    return e;
            return null;
        }
    }
}
