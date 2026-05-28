using System;
using System.IO;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Write.Mesh;
using Xunit;

namespace SW2GZ.Write.Mesh.Tests
{
    public class StlWriterTests
    {
        [Fact]
        public void Write_BinarySTL_HasCorrectHeaderAndTriangleCount()
        {
            var mesh = new MeshData(
                Vertices: new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                Triangles: new[] { 0, 1, 2 },
                MaterialColor: null);

            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.stl");
            try
            {
                StlWriter.Write(mesh, path);
                var bytes = File.ReadAllBytes(path);
                // 80-byte header + 4-byte triangle count + 50 bytes per triangle (12 floats + 2 bytes)
                Assert.Equal(80 + 4 + 50, bytes.Length);
                // Triangle count at offset 80
                int count = BitConverter.ToInt32(bytes, 80);
                Assert.Equal(1, count);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Write_BinarySTL_TriangleByteContentIsCorrect()
        {
            // Single triangle: v0=(0,0,0), v1=(1,0,0), v2=(0,1,0)
            // Cross product of (1,0,0)-(0,0,0) x (0,1,0)-(0,0,0) = (0,0,1)  → normal = UnitZ
            var v0 = Vector3.Zero;
            var v1 = Vector3.UnitX;
            var v2 = Vector3.UnitY;
            var expectedNormal = Vector3.UnitZ;

            var mesh = new MeshData(
                Vertices: new[] { v0, v1, v2 },
                Triangles: new[] { 0, 1, 2 },
                MaterialColor: null);

            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.stl");
            try
            {
                StlWriter.Write(mesh, path);
                var bytes = File.ReadAllBytes(path);

                // Triangle data starts at offset 84 (80 header + 4 count)
                int offset = 84;

                // Normal (3 floats)
                float nx = BitConverter.ToSingle(bytes, offset + 0);
                float ny = BitConverter.ToSingle(bytes, offset + 4);
                float nz = BitConverter.ToSingle(bytes, offset + 8);
                Assert.Equal(expectedNormal.X, nx, precision: 5);
                Assert.Equal(expectedNormal.Y, ny, precision: 5);
                Assert.Equal(expectedNormal.Z, nz, precision: 5);

                // Vertex 0 (3 floats)
                float v0x = BitConverter.ToSingle(bytes, offset + 12);
                float v0y = BitConverter.ToSingle(bytes, offset + 16);
                float v0z = BitConverter.ToSingle(bytes, offset + 20);
                Assert.Equal(v0.X, v0x, precision: 5);
                Assert.Equal(v0.Y, v0y, precision: 5);
                Assert.Equal(v0.Z, v0z, precision: 5);

                // Vertex 1 (3 floats)
                float v1x = BitConverter.ToSingle(bytes, offset + 24);
                float v1y = BitConverter.ToSingle(bytes, offset + 28);
                float v1z = BitConverter.ToSingle(bytes, offset + 32);
                Assert.Equal(v1.X, v1x, precision: 5);
                Assert.Equal(v1.Y, v1y, precision: 5);
                Assert.Equal(v1.Z, v1z, precision: 5);

                // Vertex 2 (3 floats)
                float v2x = BitConverter.ToSingle(bytes, offset + 36);
                float v2y = BitConverter.ToSingle(bytes, offset + 40);
                float v2z = BitConverter.ToSingle(bytes, offset + 44);
                Assert.Equal(v2.X, v2x, precision: 5);
                Assert.Equal(v2.Y, v2y, precision: 5);
                Assert.Equal(v2.Z, v2z, precision: 5);

                // Attribute byte count (2 bytes) must be 0
                ushort attr = BitConverter.ToUInt16(bytes, offset + 48);
                Assert.Equal(0, attr);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Write_NullMesh_ThrowsArgumentNullException()
        {
            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.stl");
            try
            {
                Assert.Throws<ArgumentNullException>(() => StlWriter.Write(null!, path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Write_TrianglesNotDivisibleBy3_ThrowsArgumentException()
        {
            var mesh = new MeshData(
                Vertices: new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                Triangles: new[] { 0, 1 },   // length 2, not divisible by 3
                MaterialColor: null);

            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.stl");
            try
            {
                Assert.Throws<ArgumentException>(() => StlWriter.Write(mesh, path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Fact]
        public void Write_MultipleTriangles_CountAndSizeCorrect()
        {
            // Two triangles sharing an edge
            var mesh = new MeshData(
                Vertices: new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new Vector3(1f, 1f, 0f) },
                Triangles: new[] { 0, 1, 2, 1, 3, 2 },
                MaterialColor: null);

            var path = Path.Combine(Path.GetTempPath(), $"sw2gz_test_{Guid.NewGuid()}.stl");
            try
            {
                StlWriter.Write(mesh, path);
                var bytes = File.ReadAllBytes(path);
                Assert.Equal(80 + 4 + 2 * 50, bytes.Length);
                int count = BitConverter.ToInt32(bytes, 80);
                Assert.Equal(2, count);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
