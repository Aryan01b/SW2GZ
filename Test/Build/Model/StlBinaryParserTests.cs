/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Round-trip the StlWriter output through StlBinaryParser to confirm the
binary format the 3D preview consumes matches what the pipeline emits.
*/
using System.IO;
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Write.Mesh;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class StlBinaryParserTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void RoundTrip_TwoTriangle_Quad()
        {
            // Quad on the XY plane split into two triangles.
            var verts = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(1, 1, 0),
                new Vector3(0, 1, 0),
            };
            var indices = new[] { 0, 1, 2, 0, 2, 3 };
            var mesh = new SW2GZ.Build.MeshData(verts, indices, null);

            string tmp = Path.Combine(Path.GetTempPath(), "sw2gz_stl_test_" + System.Guid.NewGuid() + ".stl");
            try
            {
                StlWriter.Write(mesh, tmp);
                StlBinaryParser.Triangles parsed = StlBinaryParser.ParseFile(tmp);

                Assert.Equal(6, parsed.Vertices.Count);   // 2 tris × 3 verts (STL is per-tri, no dedup)
                Assert.Equal(6, parsed.Indices.Count);
                Assert.Equal(new Vector3(0, 0, 0), parsed.Vertices[0]);
                Assert.Equal(new Vector3(1, 0, 0), parsed.Vertices[1]);
                Assert.Equal(new Vector3(1, 1, 0), parsed.Vertices[2]);
                Assert.Equal(new Vector3(0, 0, 0), parsed.Vertices[3]);
                Assert.Equal(new Vector3(1, 1, 0), parsed.Vertices[4]);
                Assert.Equal(new Vector3(0, 1, 0), parsed.Vertices[5]);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void TooShortHeader_Throws()
        {
            byte[] tiny = new byte[40];
            Assert.Throws<InvalidDataException>(() => StlBinaryParser.Parse(tiny));
        }
    }
}
