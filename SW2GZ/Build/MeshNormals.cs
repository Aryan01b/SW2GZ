/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Crease-angle smooth vertex normals. CAD tessellation gives one independent
triangle soup (no shared vertices), so a naive per-triangle normal renders
every facet flat — curved surfaces look chunky. This welds normals across
faces that meet at the same position AND within a crease threshold: curves
(near-coplanar neighbours) smooth together, hard edges (angle past the
threshold) stay crisp. Pure / COM-free so the test project source-links it.
*/
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SW2GZ.Build
{
    public static class MeshNormals
    {
        // One normal per vertex index. creaseDeg: faces meeting at a shared
        // position blend only if the angle between their face normals is below
        // this (default 35° — typical CAD crease).
        public static Vector3[] ComputeSmooth(MeshData mesh, double creaseDeg = 35.0)
        {
            if (mesh?.Vertices == null || mesh.Triangles == null)
                return Array.Empty<Vector3>();

            int vn = mesh.Vertices.Length;
            int[] tri = mesh.Triangles;
            int triCount = tri.Length / 3;

            // Per-triangle face normal (un-normalized → area-weighted blend).
            var faceN = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                int a = tri[3 * t], b = tri[3 * t + 1], c = tri[3 * t + 2];
                if (!InRange(a, vn) || !InRange(b, vn) || !InRange(c, vn)) continue;
                Vector3 va = mesh.Vertices[a];
                faceN[t] = Vector3.Cross(mesh.Vertices[b] - va, mesh.Vertices[c] - va);
            }

            // position cell → triangles touching it. Quantize so coincident
            // verts from different facets land in the same bucket. Exact
            // (x,y,z) integer-cell key — no hashing, so no false merges.
            var byPos = new Dictionary<(long, long, long), List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int vi = tri[3 * t + k];
                    if (!InRange(vi, vn)) continue;
                    var key = PosKey(mesh.Vertices[vi]);
                    if (!byPos.TryGetValue(key, out var list)) { list = new List<int>(); byPos[key] = list; }
                    list.Add(t);
                }
            }

            float cosCrease = (float)System.Math.Cos(creaseDeg * System.Math.PI / 180.0);
            var normals = new Vector3[vn];
            for (int t = 0; t < triCount; t++)
            {
                Vector3 ft = faceN[t];
                Vector3 ftUnit = Normalize(ft);
                for (int k = 0; k < 3; k++)
                {
                    int vi = tri[3 * t + k];
                    if (!InRange(vi, vn)) continue;
                    Vector3 acc = Vector3.Zero;
                    foreach (int u in byPos[PosKey(mesh.Vertices[vi])])
                    {
                        // Blend a neighbour face only if it's within the crease
                        // angle of THIS corner's face (keeps sharp edges sharp).
                        if (u == t || Vector3.Dot(ftUnit, Normalize(faceN[u])) >= cosCrease)
                            acc += faceN[u];
                    }
                    normals[vi] = Normalize(acc, fallback: ftUnit);
                }
            }
            return normals;
        }

        private static bool InRange(int i, int n) => i >= 0 && i < n;

        private static Vector3 Normalize(Vector3 v, Vector3 fallback = default)
        {
            float len = v.Length();
            if (len > 1e-12f) return v / len;
            return fallback == default ? new Vector3(0, 0, 1) : fallback;
        }

        // Quantize to 1e-5 m (10 µm) cells — finer than any meaningful CAD gap,
        // coarse enough that genuinely coincident verts share a key.
        private static (long, long, long) PosKey(Vector3 v)
        {
            const double q = 1e-5;
            return ((long)System.Math.Round(v.X / q), (long)System.Math.Round(v.Y / q), (long)System.Math.Round(v.Z / q));
        }
    }
}
