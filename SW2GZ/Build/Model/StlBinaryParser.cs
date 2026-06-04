/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Reads a binary STL file (the format the SW2GZ collision-mesh StlWriter
emits) into a vertex list + triangle-index list ready for WPF
MeshGeometry3D. Used by the in-app 3D preview viewport.

Format reminder:
   80 bytes  — ASCII header (ignored)
    4 bytes  — uint32 triangle count, little-endian
  per tri:
   12 bytes  — float[3] normal (ignored; recomputed for shading)
   36 bytes  — 3 × float[3] vertex positions
    2 bytes  — uint16 attribute (ignored)
   total = 50 bytes per triangle

Each triangle's vertices are independent; we don't dedupe across
triangles because STL has no shared-vertex notion (and WPF doesn't
require dedup). Pure / COM-free.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace SW2GZ.Build.Model
{
    public static class StlBinaryParser
    {
        public sealed class Triangles
        {
            public List<Vector3> Vertices { get; }
            public List<int> Indices { get; }
            public Triangles(List<Vector3> v, List<int> i) { Vertices = v; Indices = i; }
        }

        public static Triangles Parse(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length < 84)
                throw new InvalidDataException(
                    "Binary STL too short: need at least 84 bytes (header + count), got " + bytes.Length + ".");

            using (var ms = new MemoryStream(bytes, writable: false))
            using (var r = new BinaryReader(ms))
            {
                r.BaseStream.Seek(80, SeekOrigin.Begin);
                uint triCount = r.ReadUInt32();

                long needed = 84L + 50L * triCount;
                if (bytes.Length < needed)
                    throw new InvalidDataException(
                        "Binary STL truncated: declares " + triCount +
                        " triangles (need " + needed + " bytes) but file is " + bytes.Length + " bytes.");

                var verts = new List<Vector3>((int)(triCount * 3));
                var indices = new List<int>((int)(triCount * 3));
                for (int i = 0; i < triCount; i++)
                {
                    // Skip the per-tri normal — WPF recomputes shading from
                    // vertex positions, so we don't need to round-trip it.
                    r.ReadSingle(); r.ReadSingle(); r.ReadSingle();

                    int baseIdx = verts.Count;
                    for (int v = 0; v < 3; v++)
                    {
                        float x = r.ReadSingle();
                        float y = r.ReadSingle();
                        float z = r.ReadSingle();
                        verts.Add(new Vector3(x, y, z));
                        indices.Add(baseIdx + v);
                    }
                    r.ReadUInt16();   // attribute count, ignored
                }
                return new Triangles(verts, indices);
            }
        }

        public static Triangles ParseFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            return Parse(File.ReadAllBytes(path));
        }
    }
}
