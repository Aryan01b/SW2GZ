using System;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Xml;
using SW2GZ.Build;
using SW2GZ.Write.Mesh;
using Xunit;

namespace SW2GZ.Write.Mesh.Tests
{
    public class DaeWriterTests
    {
        [Fact]
        public void Write_EmitsZUpMeterUnit_AndValidXml()
        {
            var mesh = new MeshData(
                Vertices: new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                Triangles: new[] { 0, 1, 2 },
                MaterialColor: Color.Red);

            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.dae");
            try
            {
                DaeWriter.Write(mesh, path);
                var doc = new XmlDocument();
                doc.Load(path);
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("c", "http://www.collada.org/2005/11/COLLADASchema");

                var upAxis = doc.SelectSingleNode("//c:asset/c:up_axis", ns);
                Assert.NotNull(upAxis);
                Assert.Equal("Z_UP", upAxis.InnerText);

                var unit = (XmlElement)doc.SelectSingleNode("//c:asset/c:unit", ns);
                Assert.NotNull(unit);
                Assert.Equal("1", unit.GetAttribute("meter"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Write_EmbedsMaterialColor_AsLambertDiffuse()
        {
            // Color.FromArgb(255, 128, 64): R=255 → 1.0, G=128 → ~0.502, B=64 → ~0.251
            var color = Color.FromArgb(255, 128, 64);
            var mesh = new MeshData(
                Vertices: new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                Triangles: new[] { 0, 1, 2 },
                MaterialColor: color);

            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.dae");
            try
            {
                DaeWriter.Write(mesh, path);
                var doc = new XmlDocument();
                doc.Load(path);
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("c", "http://www.collada.org/2005/11/COLLADASchema");

                var colorNode = doc.SelectSingleNode(
                    "//c:library_effects/c:effect/c:profile_COMMON/c:technique/c:lambert/c:diffuse/c:color", ns);
                Assert.NotNull(colorNode);

                var parts = colorNode.InnerText.Trim().Split(' ');
                Assert.Equal(4, parts.Length);
                Assert.Equal(1.0, double.Parse(parts[0]), 3);
                Assert.Equal(0.502, double.Parse(parts[1]), 2);
                Assert.Equal(0.251, double.Parse(parts[2]), 2);
                Assert.Equal(1.0, double.Parse(parts[3]), 3);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Write_EmitsTrianglesAndVertexCounts_MatchInput()
        {
            // Two triangles sharing an edge; 4 vertices
            var mesh = new MeshData(
                Vertices: new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new Vector3(1f, 1f, 0f) },
                Triangles: new[] { 0, 1, 2, 1, 3, 2 },
                MaterialColor: null);

            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.dae");
            try
            {
                DaeWriter.Write(mesh, path);
                var doc = new XmlDocument();
                doc.Load(path);
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("c", "http://www.collada.org/2005/11/COLLADASchema");

                var triangles = (XmlElement)doc.SelectSingleNode(
                    "//c:library_geometries/c:geometry/c:mesh/c:triangles", ns);
                Assert.NotNull(triangles);
                Assert.Equal("2", triangles.GetAttribute("count"));

                var accessor = (XmlElement)doc.SelectSingleNode(
                    "//c:library_geometries/c:geometry/c:mesh/c:source/c:technique_common/c:accessor", ns);
                Assert.NotNull(accessor);
                Assert.Equal("4", accessor.GetAttribute("count"));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Write_NullMesh_ThrowsArgumentNullException()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.dae");
            Assert.Throws<ArgumentNullException>(() => DaeWriter.Write(null!, path));
        }

        [Fact]
        public void Write_NullPath_ThrowsArgumentNullException()
        {
            var mesh = new MeshData(
                Vertices: new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                Triangles: new[] { 0, 1, 2 },
                MaterialColor: null);

            Assert.Throws<ArgumentNullException>(() => DaeWriter.Write(mesh, null!));
        }
    }
}
