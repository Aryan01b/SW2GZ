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
            var mesh = new MeshData(
                Vertices: new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                Triangles: new[] { 0, 1, 2 },
                MaterialColor: System.Drawing.Color.FromArgb(255, 128, 64));   // 1.0 0.502 0.251 alpha=1
            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.dae");
            try
            {
                DaeWriter.Write(mesh, path);
                var doc = new XmlDocument();
                doc.Load(path);
                var ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("c", "http://www.collada.org/2005/11/COLLADASchema");
                var color = doc.SelectSingleNode("//c:lambert/c:diffuse/c:color", ns);
                Assert.NotNull(color);
                // locale-stable: assert the invariant-formatted literal appears verbatim
                Assert.Equal("1 0.502 0.251 1", color.InnerText);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Write_PathWhitespace_Throws()
        {
            var mesh = new MeshData(new[] { Vector3.Zero }, new int[0], null);
            Assert.Throws<ArgumentException>(() => DaeWriter.Write(mesh, "   "));
        }

        [Fact]
        public void Write_NullVertices_Throws()
        {
            var mesh = new MeshData(null, new int[0], null);
            Assert.Throws<ArgumentException>(() =>
                DaeWriter.Write(mesh, Path.Combine(Path.GetTempPath(), "x.dae")));
        }

        [Fact]
        public void Write_NullTriangles_Throws()
        {
            var mesh = new MeshData(new[] { Vector3.Zero }, null, null);
            Assert.Throws<ArgumentException>(() =>
                DaeWriter.Write(mesh, Path.Combine(Path.GetTempPath(), "x.dae")));
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
        public void Write_NullPath_ThrowsArgumentException()
        {
            var mesh = new MeshData(
                Vertices: new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                Triangles: new[] { 0, 1, 2 },
                MaterialColor: null);

            Assert.Throws<ArgumentException>(() => DaeWriter.Write(mesh, null!));
        }
    }
}
