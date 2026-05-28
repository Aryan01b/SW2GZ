using System;
using System.IO;
using System.Numerics;
using SW2GZ.Build;

namespace SW2GZ.Write.Mesh
{
    public static class StlWriter
    {
        // Binary STL: 80-byte header + uint32 triangle count + N * (3 normal floats + 9 vertex floats + uint16 attr)
        public static void Write(MeshData mesh, string path)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));
            if (mesh.Triangles.Length % 3 != 0)
                throw new ArgumentException(
                    $"Triangles array length must be divisible by 3, got {mesh.Triangles.Length}.",
                    nameof(mesh));

            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);

            // 80-byte header (ASCII pad)
            var header = new byte[80];
            var label = System.Text.Encoding.ASCII.GetBytes("SW2GZ");
            System.Array.Copy(label, header, label.Length);
            bw.Write(header);

            int triCount = mesh.Triangles.Length / 3;
            bw.Write(triCount);

            for (int i = 0; i < triCount; i++)
            {
                Vector3 v0 = mesh.Vertices[mesh.Triangles[i * 3 + 0]];
                Vector3 v1 = mesh.Vertices[mesh.Triangles[i * 3 + 1]];
                Vector3 v2 = mesh.Vertices[mesh.Triangles[i * 3 + 2]];
                Vector3 n = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
                if (float.IsNaN(n.X)) n = Vector3.UnitZ;

                bw.Write(n.X); bw.Write(n.Y); bw.Write(n.Z);
                bw.Write(v0.X); bw.Write(v0.Y); bw.Write(v0.Z);
                bw.Write(v1.X); bw.Write(v1.Y); bw.Write(v1.Z);
                bw.Write(v2.X); bw.Write(v2.Y); bw.Write(v2.Z);
                bw.Write((ushort)0); // attribute byte count
            }
        }
    }
}
