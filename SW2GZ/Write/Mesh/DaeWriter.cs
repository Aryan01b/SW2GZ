/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Collada 1.4.1 mesh writer for visual meshes. Z_UP matches URDF convention;
meter=1 passes SolidWorks meters through unchanged. Material color is
embedded in <library_effects> so RViz + gz sim pick up the SW part color.
*/
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using SW2GZ.Build;

namespace SW2GZ.Write.Mesh
{
    public static class DaeWriter
    {
        public static void Write(MeshData mesh, string path) => Write(mesh, path, false);

        // withNormals=true appends a per-vertex NORMAL source so Gz/Ogre can
        // light the surface (without normals a mesh renders flat + unlit, i.e.
        // plain white with no shading/shadows). Default false keeps existing
        // robot golden DAE output byte-identical.
        public static void Write(MeshData mesh, string path, bool withNormals)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path must not be null or whitespace.", nameof(path));
            if (mesh.Vertices == null)
                throw new ArgumentException("MeshData.Vertices must not be null.", nameof(mesh));
            if (mesh.Triangles == null)
                throw new ArgumentException("MeshData.Triangles must not be null.", nameof(mesh));

            const string ns = "http://www.collada.org/2005/11/COLLADASchema";
            var settings = new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 };
            using var w = XmlWriter.Create(path, settings);

            w.WriteStartDocument();
            w.WriteStartElement("COLLADA", ns);
            w.WriteAttributeString("version", "1.4.1");

            w.WriteStartElement("asset", ns);
            w.WriteStartElement("unit", ns);
            w.WriteAttributeString("name", "meter");
            w.WriteAttributeString("meter", "1");
            w.WriteEndElement();
            w.WriteElementString("up_axis", ns, "Z_UP");
            w.WriteEndElement();

            var color = mesh.MaterialColor ?? Color.Gray;
            float r = color.R / 255f, g = color.G / 255f, b = color.B / 255f;
            w.WriteStartElement("library_effects", ns);
            w.WriteStartElement("effect", ns);
            w.WriteAttributeString("id", "fx0");
            w.WriteStartElement("profile_COMMON", ns);
            w.WriteStartElement("technique", ns);
            w.WriteAttributeString("sid", "common");
            w.WriteStartElement("lambert", ns);
            w.WriteStartElement("diffuse", ns);
            w.WriteStartElement("color", ns);
            w.WriteString(string.Format(CultureInfo.InvariantCulture, "{0:0.###} {1:0.###} {2:0.###} 1", r, g, b));
            w.WriteEndElement(); w.WriteEndElement();
            w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("library_materials", ns);
            w.WriteStartElement("material", ns);
            w.WriteAttributeString("id", "mat0");
            w.WriteStartElement("instance_effect", ns);
            w.WriteAttributeString("url", "#fx0");
            w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();

            w.WriteStartElement("library_geometries", ns);
            w.WriteStartElement("geometry", ns);
            w.WriteAttributeString("id", "g0");
            w.WriteStartElement("mesh", ns);

            var posSb = new StringBuilder();
            foreach (var v in mesh.Vertices)
                posSb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.######} {1:0.######} {2:0.######} ", v.X, v.Y, v.Z));

            w.WriteStartElement("source", ns);
            w.WriteAttributeString("id", "g0-pos");
            w.WriteStartElement("float_array", ns);
            w.WriteAttributeString("id", "g0-pos-array");
            w.WriteAttributeString("count", (mesh.Vertices.Length * 3).ToString());
            w.WriteString(posSb.ToString().Trim());
            w.WriteEndElement();
            w.WriteStartElement("technique_common", ns);
            w.WriteStartElement("accessor", ns);
            w.WriteAttributeString("source", "#g0-pos-array");
            w.WriteAttributeString("count", mesh.Vertices.Length.ToString());
            w.WriteAttributeString("stride", "3");
            foreach (var p in new[] { "X", "Y", "Z" })
            { w.WriteStartElement("param", ns); w.WriteAttributeString("name", p); w.WriteAttributeString("type", "float"); w.WriteEndElement(); }
            w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();

            if (withNormals)
            {
                // Crease-angle smooth normals: curved surfaces shade smoothly,
                // CAD hard edges stay sharp (vs the old flat per-facet normal).
                var normals = MeshNormals.ComputeSmooth(mesh);
                var normSb = new StringBuilder();
                foreach (var n in normals)
                    normSb.Append(string.Format(CultureInfo.InvariantCulture, "{0:0.######} {1:0.######} {2:0.######} ", n.X, n.Y, n.Z));
                w.WriteStartElement("source", ns);
                w.WriteAttributeString("id", "g0-norm");
                w.WriteStartElement("float_array", ns);
                w.WriteAttributeString("id", "g0-norm-array");
                w.WriteAttributeString("count", (normals.Length * 3).ToString());
                w.WriteString(normSb.ToString().Trim());
                w.WriteEndElement();
                w.WriteStartElement("technique_common", ns);
                w.WriteStartElement("accessor", ns);
                w.WriteAttributeString("source", "#g0-norm-array");
                w.WriteAttributeString("count", normals.Length.ToString());
                w.WriteAttributeString("stride", "3");
                foreach (var p in new[] { "X", "Y", "Z" })
                { w.WriteStartElement("param", ns); w.WriteAttributeString("name", p); w.WriteAttributeString("type", "float"); w.WriteEndElement(); }
                w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
            }

            w.WriteStartElement("vertices", ns);
            w.WriteAttributeString("id", "g0-vtx");
            w.WriteStartElement("input", ns);
            w.WriteAttributeString("semantic", "POSITION");
            w.WriteAttributeString("source", "#g0-pos");
            w.WriteEndElement();
            if (withNormals)
            {
                w.WriteStartElement("input", ns);
                w.WriteAttributeString("semantic", "NORMAL");
                w.WriteAttributeString("source", "#g0-norm");
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteStartElement("triangles", ns);
            w.WriteAttributeString("count", (mesh.Triangles.Length / 3).ToString());
            w.WriteAttributeString("material", "mat0");
            w.WriteStartElement("input", ns);
            w.WriteAttributeString("semantic", "VERTEX");
            w.WriteAttributeString("source", "#g0-vtx");
            w.WriteAttributeString("offset", "0");
            w.WriteEndElement();
            var triSb = new StringBuilder();
            foreach (var t in mesh.Triangles) triSb.Append(t).Append(' ');
            w.WriteElementString("p", ns, triSb.ToString().Trim());
            w.WriteEndElement();

            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("library_visual_scenes", ns);
            w.WriteStartElement("visual_scene", ns);
            w.WriteAttributeString("id", "scene");
            w.WriteStartElement("node", ns);
            w.WriteAttributeString("id", "node0");
            w.WriteStartElement("instance_geometry", ns);
            w.WriteAttributeString("url", "#g0");
            w.WriteStartElement("bind_material", ns);
            w.WriteStartElement("technique_common", ns);
            w.WriteStartElement("instance_material", ns);
            w.WriteAttributeString("symbol", "mat0");
            w.WriteAttributeString("target", "#mat0");
            w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();
            w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement(); w.WriteEndElement();

            w.WriteStartElement("scene", ns);
            w.WriteStartElement("instance_visual_scene", ns);
            w.WriteAttributeString("url", "#scene");
            w.WriteEndElement(); w.WriteEndElement();

            w.WriteEndElement();
            w.WriteEndDocument();
        }

    }
}
