using System;
using System.Collections.Generic;
using System.Numerics;

namespace SW2GZ.Build
{
    /// <summary>
    /// Incremental 3D convex hull (QuickHull). Geometric predicates use double
    /// precision; only the final vertex array is cast to <see cref="Vector3"/>.
    ///
    /// Degenerate input (fewer than 4 distinct points, colinear or coplanar
    /// clouds) throws <see cref="ArgumentException"/> — callers that want a
    /// safe fallback should use <see cref="ColliderStrategy.Aabb"/>.
    /// </summary>
    internal static class QuickHull3D
    {
        // Face record. Uses int indices into the input point cloud (doubles).
        private sealed class Face
        {
            public int A, B, C;            // CCW seen from outside
            public Vec3D Normal;           // unit outward normal
            public Vec3D Centroid;
            public double Offset;          // Normal · A (plane equation: n·x = offset)
            public List<int> Conflicts;    // indices of points in front of this face
            public bool Removed;
        }

        private readonly struct Vec3D
        {
            public readonly double X, Y, Z;
            public Vec3D(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static Vec3D operator -(Vec3D a, Vec3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec3D operator +(Vec3D a, Vec3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec3D operator *(Vec3D a, double s) => new(a.X * s, a.Y * s, a.Z * s);
            public static double Dot(Vec3D a, Vec3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            public static Vec3D Cross(Vec3D a, Vec3D b) => new(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
            public double Length() => System.Math.Sqrt(X * X + Y * Y + Z * Z);
            public Vec3D Normalized()
            {
                double l = Length();
                if (l == 0) return new Vec3D(0, 0, 0);
                double inv = 1.0 / l;
                return new Vec3D(X * inv, Y * inv, Z * inv);
            }
        }

        public static MeshData Build(MeshData visual)
        {
            if (visual == null || visual.Vertices == null)
                throw new ArgumentException("MeshData has no vertices", nameof(visual));
            if (visual.Vertices.Length < 4)
                throw new ArgumentException(
                    $"Convex hull requires at least 4 vertices; got {visual.Vertices.Length}.",
                    nameof(visual));

            // Promote to double precision.
            var src = visual.Vertices;
            int n = src.Length;
            var pts = new Vec3D[n];
            for (int i = 0; i < n; i++)
                pts[i] = new Vec3D(src[i].X, src[i].Y, src[i].Z);

            // Bounding box diagonal for relative epsilon.
            double minX = pts[0].X, maxX = pts[0].X;
            double minY = pts[0].Y, maxY = pts[0].Y;
            double minZ = pts[0].Z, maxZ = pts[0].Z;
            for (int i = 1; i < n; i++)
            {
                if (pts[i].X < minX) minX = pts[i].X; if (pts[i].X > maxX) maxX = pts[i].X;
                if (pts[i].Y < minY) minY = pts[i].Y; if (pts[i].Y > maxY) maxY = pts[i].Y;
                if (pts[i].Z < minZ) minZ = pts[i].Z; if (pts[i].Z > maxZ) maxZ = pts[i].Z;
            }
            double dx = maxX - minX, dy = maxY - minY, dz = maxZ - minZ;
            double diag = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double eps = System.Math.Max(1e-9, 1e-9 * diag);
            // Tolerance for "in-front-of-face" tests: scale with bbox.
            double frontEps = System.Math.Max(1e-12, 1e-10 * System.Math.Max(diag, 1.0));

            // ── Initial tetrahedron ──────────────────────────────────────────
            int i0 = 0, i1 = 0;
            for (int i = 0; i < n; i++)
            {
                if (pts[i].X < pts[i0].X) i0 = i;
                if (pts[i].X > pts[i1].X) i1 = i;
            }
            if (i0 == i1 || (pts[i1] - pts[i0]).Length() <= eps)
                throw new ArgumentException("Vertices are colinear or degenerate; cannot build convex hull.", nameof(visual));

            // i2 = vertex farthest from line i0—i1
            Vec3D edge = pts[i1] - pts[i0];
            double edgeLen = edge.Length();
            int i2 = -1;
            double bestD2 = -1;
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1) continue;
                Vec3D d = pts[i] - pts[i0];
                Vec3D cr = Vec3D.Cross(edge, d);
                double dist = cr.Length() / edgeLen;
                if (dist > bestD2) { bestD2 = dist; i2 = i; }
            }
            if (i2 < 0 || bestD2 <= eps)
                throw new ArgumentException("Vertices are colinear or degenerate; cannot build convex hull.", nameof(visual));

            // i3 = vertex farthest from plane (i0,i1,i2)
            Vec3D planeN = Vec3D.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]);
            double planeNLen = planeN.Length();
            if (planeNLen <= eps)
                throw new ArgumentException("Vertices are coplanar; cannot build convex hull.", nameof(visual));
            Vec3D planeNu = planeN * (1.0 / planeNLen);
            double planeOff = Vec3D.Dot(planeNu, pts[i0]);
            int i3 = -1;
            double bestD3 = -1;
            double signed3 = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1 || i == i2) continue;
                double s = Vec3D.Dot(planeNu, pts[i]) - planeOff;
                double a = System.Math.Abs(s);
                if (a > bestD3) { bestD3 = a; i3 = i; signed3 = s; }
            }
            if (i3 < 0 || bestD3 <= eps)
                throw new ArgumentException("Vertices are coplanar; cannot build convex hull.", nameof(visual));

            // Build 4 faces. If i3 is below plane (signed3 < 0), the natural
            // CCW order (i0,i1,i2) already has its outward normal opposite to i3.
            // We want faces oriented so outward normals point AWAY from the
            // centroid. Compute centroid and swap to fix any inverted face.
            Vec3D centroid = (pts[i0] + pts[i1] + pts[i2] + pts[i3]) * 0.25;

            var faces = new List<Face>(4)
            {
                MakeFace(i0, i1, i2, pts, centroid),
                MakeFace(i0, i3, i1, pts, centroid),
                MakeFace(i1, i3, i2, pts, centroid),
                MakeFace(i2, i3, i0, pts, centroid),
            };

            // Assign every remaining point to a conflict list (in front of one face).
            var assigned = new bool[n];
            assigned[i0] = assigned[i1] = assigned[i2] = assigned[i3] = true;
            for (int i = 0; i < n; i++)
            {
                if (assigned[i]) continue;
                AssignPoint(i, pts, faces, frontEps);
            }

            // ── Main loop ────────────────────────────────────────────────────
            // For each face with conflicts, pick its farthest conflict, find
            // horizon, replace visible faces with new ones from horizon to point.
            //
            // Safety cap: at most ~n iterations of point absorption.
            int safetyCap = 16 * n + 64;
            int iter = 0;

            while (true)
            {
                Face hot = null;
                double bestFarDist = frontEps;
                int eyeIdx = -1;
                for (int fi = 0; fi < faces.Count; fi++)
                {
                    var f = faces[fi];
                    if (f.Removed || f.Conflicts == null || f.Conflicts.Count == 0) continue;
                    // Find the farthest point of this face's conflict list.
                    for (int ci = 0; ci < f.Conflicts.Count; ci++)
                    {
                        int pi = f.Conflicts[ci];
                        double d = Vec3D.Dot(f.Normal, pts[pi]) - f.Offset;
                        if (d > bestFarDist) { bestFarDist = d; hot = f; eyeIdx = pi; }
                    }
                }
                if (hot == null) break;
                if (++iter > safetyCap)
                    throw new InvalidOperationException("QuickHull3D exceeded iteration cap; possible numerical instability.");

                Vec3D eye = pts[eyeIdx];

                // ── Find visible set and horizon ─────────────────────────────
                // Mark all faces visible from the eye. Visible = signed distance > frontEps.
                var visible = new List<Face>();
                foreach (var f in faces)
                {
                    if (f.Removed) continue;
                    double d = Vec3D.Dot(f.Normal, eye) - f.Offset;
                    if (d > frontEps) visible.Add(f);
                }

                // Build edge map: each undirected edge appears in 1 or 2 visible faces.
                // Edges that appear once → horizon edges (we keep their direction
                // from the visible face so the new triangle [edge.A, edge.B, eye]
                // is CCW outward).
                //
                // Edge key: pair (min, max). Value: (a, b) directed edge from
                // the visible face, plus a "second occurrence" flag.
                var edgeCount = new Dictionary<(int, int), (int a, int b, int hits)>();
                foreach (var f in visible)
                {
                    AddEdge(edgeCount, f.A, f.B);
                    AddEdge(edgeCount, f.B, f.C);
                    AddEdge(edgeCount, f.C, f.A);
                }

                // Collect horizon edges (hits == 1) preserving the directed (a,b) order.
                var horizon = new List<(int a, int b)>();
                foreach (var kv in edgeCount)
                {
                    if (kv.Value.hits == 1)
                        horizon.Add((kv.Value.a, kv.Value.b));
                }

                if (horizon.Count == 0)
                {
                    // Degenerate: eye visible to all faces but no horizon — shouldn't happen
                    // for non-degenerate clouds. Drop point safely.
                    hot.Conflicts.Remove(eyeIdx);
                    continue;
                }

                // ── Remove visible faces, gather their orphaned conflicts ────
                var orphans = new List<int>();
                foreach (var f in visible)
                {
                    f.Removed = true;
                    if (f.Conflicts != null)
                    {
                        foreach (int pi in f.Conflicts)
                            if (pi != eyeIdx) orphans.Add(pi);
                    }
                }
                assigned[eyeIdx] = true;

                // ── Create new faces from each horizon edge + eye ────────────
                var newFaces = new List<Face>(horizon.Count);
                foreach (var (a, b) in horizon)
                {
                    // Visible face was CCW outward and contained edge a→b. The
                    // adjacent (kept) face contains b→a. The new triangle
                    // (a, b, eye) replaces the visible side, so it must also be
                    // CCW outward. Compute normal and use centroid-side check
                    // against the existing hull centroid to confirm.
                    var face = MakeFace(a, b, eyeIdx, pts, centroid);
                    newFaces.Add(face);
                }
                faces.AddRange(newFaces);

                // Re-assign orphans (excluding the eye itself).
                foreach (int pi in orphans)
                {
                    if (assigned[pi]) continue;
                    // Try the new faces first; if none, drop (point is inside).
                    AssignPoint(pi, pts, newFaces, frontEps);
                }

                // Compact faces occasionally to keep iteration cheap.
                if (faces.Count > 256 && (iter & 31) == 0)
                {
                    var alive = new List<Face>(faces.Count);
                    foreach (var f in faces) if (!f.Removed) alive.Add(f);
                    faces = alive;
                }
            }

            // ── Compose output mesh ─────────────────────────────────────────
            var keep = new List<Face>(faces.Count);
            foreach (var f in faces) if (!f.Removed) keep.Add(f);

            if (keep.Count < 4)
                throw new InvalidOperationException("QuickHull3D produced fewer than 4 faces; input is degenerate.");

            // Re-index used vertices into a compact array.
            var remap = new Dictionary<int, int>(keep.Count * 2);
            var outVerts = new List<Vector3>(keep.Count);
            var outTris = new int[keep.Count * 3];

            int triCursor = 0;
            // Compute final hull centroid in double for the outward-orientation guard.
            Vec3D hullCentroid = new Vec3D(0, 0, 0);
            int cCount = 0;
            foreach (var f in keep)
            {
                hullCentroid = hullCentroid + pts[f.A] + pts[f.B] + pts[f.C];
                cCount += 3;
            }
            hullCentroid = hullCentroid * (1.0 / cCount);

            foreach (var f in keep)
            {
                int ia = MapIndex(f.A);
                int ib = MapIndex(f.B);
                int ic = MapIndex(f.C);

                // Guard: ensure CCW seen from outside the final hull. (The
                // tetrahedron seed and per-edge construction already enforce
                // this, but a final check costs little and guarantees the
                // documented contract.)
                Vec3D fc = (pts[f.A] + pts[f.B] + pts[f.C]) * (1.0 / 3.0);
                Vec3D fn = Vec3D.Cross(pts[f.B] - pts[f.A], pts[f.C] - pts[f.A]);
                if (Vec3D.Dot(fn, fc - hullCentroid) < 0)
                {
                    // Swap to fix winding.
                    (ib, ic) = (ic, ib);
                }

                outTris[triCursor++] = ia;
                outTris[triCursor++] = ib;
                outTris[triCursor++] = ic;
            }

            return new MeshData(outVerts.ToArray(), outTris, visual.MaterialColor);

            int MapIndex(int srcIdx)
            {
                if (!remap.TryGetValue(srcIdx, out int mapped))
                {
                    mapped = outVerts.Count;
                    remap[srcIdx] = mapped;
                    var p = pts[srcIdx];
                    outVerts.Add(new Vector3((float)p.X, (float)p.Y, (float)p.Z));
                }
                return mapped;
            }
        }

        private static void AddEdge(Dictionary<(int, int), (int a, int b, int hits)> map, int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            if (map.TryGetValue(key, out var v))
                map[key] = (v.a, v.b, v.hits + 1);
            else
                map[key] = (a, b, 1);
        }

        private static Face MakeFace(int a, int b, int c, Vec3D[] pts, Vec3D hullCentroid)
        {
            Vec3D va = pts[a], vb = pts[b], vc = pts[c];
            Vec3D n = Vec3D.Cross(vb - va, vc - va);
            // Orient outward: dot(n, faceCentroid - hullCentroid) must be > 0.
            Vec3D fc = (va + vb + vc) * (1.0 / 3.0);
            if (Vec3D.Dot(n, fc - hullCentroid) < 0)
            {
                (b, c) = (c, b);
                vb = pts[b]; vc = pts[c];
                n = Vec3D.Cross(vb - va, vc - va);
            }
            Vec3D nu = n.Normalized();
            return new Face
            {
                A = a, B = b, C = c,
                Normal = nu,
                Centroid = (va + vb + vc) * (1.0 / 3.0),
                Offset = Vec3D.Dot(nu, va),
                Conflicts = new List<int>(),
                Removed = false,
            };
        }

        private static void AssignPoint(int idx, Vec3D[] pts, List<Face> faces, double frontEps)
        {
            // Find the face this point is most in front of.
            Face best = null;
            double bestD = frontEps;
            foreach (var f in faces)
            {
                if (f.Removed) continue;
                double d = Vec3D.Dot(f.Normal, pts[idx]) - f.Offset;
                if (d > bestD) { bestD = d; best = f; }
            }
            if (best != null) best.Conflicts.Add(idx);
        }
    }
}
