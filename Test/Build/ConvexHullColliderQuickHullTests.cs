using System;
using System.Numerics;
using SW2GZ.Build;
using Xunit;

namespace SW2GZ.Build.Tests
{
    /// <summary>
    /// P4: real 3D convex hull (QuickHull). Covers the new strategy-aware
    /// <see cref="ConvexHullCollider.Build(MeshData, ColliderStrategy)"/> overload
    /// in addition to the legacy default-AABB behavior preserved via
    /// <see cref="ConvexHullCollider.Build(MeshData)"/>.
    /// </summary>
    public class ConvexHullColliderQuickHullTests
    {
        // ── 1. AABB strategy preserved ───────────────────────────────────────
        [Fact]
        public void Build_AabbStrategy_ReturnsBoxMesh()
        {
            var verts = new Vector3[]
            {
                new(-1,-1,-1), new( 1,-1,-1), new( 1, 1,-1), new(-1, 1,-1),
                new(-1,-1, 1), new( 1,-1, 1), new( 1, 1, 1), new(-1, 1, 1),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);
            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.Aabb);

            Assert.Equal(8, hull.Vertices.Length);
            Assert.Equal(12, hull.Triangles.Length / 3);
            // AABB centered at origin: surface area = 6 * 2 * 2 = 24.
            Assert.Equal(24.0, SurfaceArea(hull), 4);
        }

        // ── 2. Tetrahedron in, tetrahedron out ───────────────────────────────
        [Fact]
        public void Build_ConvexHullStrategy_TetrahedronInput_ReturnsTetrahedron()
        {
            // Regular-ish tetrahedron with known volume = 1/3 * base_area * height.
            var verts = new Vector3[]
            {
                new(0, 0, 0),
                new(1, 0, 0),
                new(0, 1, 0),
                new(0, 0, 1),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);
            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);

            Assert.Equal(4, hull.Vertices.Length);
            Assert.Equal(4, hull.Triangles.Length / 3);

            // V = |det| / 6 for tet with one vertex at origin = 1/6.
            Assert.Equal(1.0 / 6.0, SignedVolume(hull), 5);
            AssertOutwardNormals(hull);
        }

        // ── 3. Cube → cube hull ──────────────────────────────────────────────
        [Fact]
        public void Build_ConvexHullStrategy_CubeMesh_ReturnsCubeHull()
        {
            // Unit cube, side = 1, centered such that volume = 1.
            var verts = new Vector3[]
            {
                new(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0),
                new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);
            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);

            Assert.Equal(8, hull.Vertices.Length);
            // 6 quad faces triangulated → 12 triangles.
            Assert.Equal(12, hull.Triangles.Length / 3);
            Assert.Equal(1.0, SignedVolume(hull), 5);
            AssertOutwardNormals(hull);
        }

        // ── 4. Interior point ignored ────────────────────────────────────────
        [Fact]
        public void Build_ConvexHullStrategy_PointInsideCube_OutputUnchanged()
        {
            var verts = new Vector3[]
            {
                new(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0),
                new(0,0,1), new(1,0,1), new(1,1,1), new(0,1,1),
                new(0.5f, 0.5f, 0.5f), // dead-center, must be discarded
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);
            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);

            Assert.Equal(8, hull.Vertices.Length);
            Assert.Equal(12, hull.Triangles.Length / 3);
            Assert.Equal(1.0, SignedVolume(hull), 5);
        }

        // ── 5. Too few points → graceful AABB fallback (Fix 1) ───────────────
        // The ConvexHull strategy degrades to the AABB box on degenerate input
        // instead of throwing, so a flat/thin part still gets a valid collider
        // rather than aborting the export. (QuickHull3D.Build itself still throws;
        // only the strategy-aware ConvexHullCollider wrapper degrades.)
        [Fact]
        public void Build_ConvexHullStrategy_Triangle_FallsBackToAabb()
        {
            var verts = new Vector3[] { new(0,0,0), new(1,0,0), new(0,1,0) };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);
            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);
            Assert.Equal(8, hull.Vertices.Length);
            Assert.Equal(12, hull.Triangles.Length / 3);
        }

        // ── 6. Colinear → graceful AABB fallback (Fix 1) ─────────────────────
        [Fact]
        public void Build_ConvexHullStrategy_CollinearPoints_FallsBackToAabb()
        {
            var verts = new Vector3[]
            {
                new(0,0,0), new(1,0,0), new(2,0,0), new(3,0,0),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);
            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);
            Assert.Equal(8, hull.Vertices.Length);
            Assert.Equal(12, hull.Triangles.Length / 3);
        }

        // ── 7. Coplanar → graceful AABB fallback (Fix 1) ─────────────────────
        [Fact]
        public void Build_ConvexHullStrategy_CoplanarPoints_FallsBackToAabb()
        {
            // 4 points on z=0 plane.
            var verts = new Vector3[]
            {
                new(0,0,0), new(1,0,0), new(1,1,0), new(0,1,0),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);
            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);
            Assert.Equal(8, hull.Vertices.Length);
            Assert.Equal(12, hull.Triangles.Length / 3);
        }

        // ── 8. Null input ────────────────────────────────────────────────────
        [Fact]
        public void Build_ConvexHullStrategy_NullVisual_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                ConvexHullCollider.Build(null, ColliderStrategy.ConvexHull));
        }

        // ── 9. All-outward normals on a non-trivial hull ─────────────────────
        [Fact]
        public void Build_ConvexHullStrategy_AllOutwardNormals()
        {
            // Octahedron — 6 vertices.
            var verts = new Vector3[]
            {
                new( 1, 0, 0), new(-1, 0, 0),
                new( 0, 1, 0), new( 0,-1, 0),
                new( 0, 0, 1), new( 0, 0,-1),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);
            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);

            Assert.Equal(6, hull.Vertices.Length);
            Assert.Equal(8, hull.Triangles.Length / 3);
            AssertOutwardNormals(hull);

            // Regular octahedron with axes radius 1 has volume = 4/3.
            Assert.Equal(4.0 / 3.0, SignedVolume(hull), 5);
        }

        // ── 10. Default overload behavior (per P4 decision: option (b) = AABB) ──
        [Fact]
        public void Build_DefaultOverload_MatchesAabb()
        {
            // Per P4 decision documented in ConvexHullCollider.cs header,
            // the no-strategy overload preserves the v2.0 AABB behavior so
            // legacy tests that pass degenerate point clouds keep working.
            var verts = new Vector3[]
            {
                new(-1,-1,-1), new( 1,-1,-1), new( 1, 1,-1), new(-1, 1,-1),
                new(-1,-1, 1), new( 1,-1, 1), new( 1, 1, 1), new(-1, 1, 1),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);
            var defaultHull = ConvexHullCollider.Build(mesh);
            var aabbHull = ConvexHullCollider.Build(mesh, ColliderStrategy.Aabb);

            Assert.Equal(aabbHull.Vertices.Length, defaultHull.Vertices.Length);
            Assert.Equal(aabbHull.Triangles.Length, defaultHull.Triangles.Length);
            for (int i = 0; i < aabbHull.Triangles.Length; i++)
                Assert.Equal(aabbHull.Triangles[i], defaultHull.Triangles[i]);
        }

        // ─────────────────────────── helpers ─────────────────────────────────
        private static double SignedVolume(MeshData m)
        {
            // V = (1/6) Σ v0 · (v1 × v2). For an outward-CCW closed mesh, positive.
            double sum = 0;
            int triCount = m.Triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                var v0 = m.Vertices[m.Triangles[i * 3 + 0]];
                var v1 = m.Vertices[m.Triangles[i * 3 + 1]];
                var v2 = m.Vertices[m.Triangles[i * 3 + 2]];
                double cx = (double)v1.Y * v2.Z - (double)v1.Z * v2.Y;
                double cy = (double)v1.Z * v2.X - (double)v1.X * v2.Z;
                double cz = (double)v1.X * v2.Y - (double)v1.Y * v2.X;
                sum += (double)v0.X * cx + (double)v0.Y * cy + (double)v0.Z * cz;
            }
            return sum / 6.0;
        }

        private static double SurfaceArea(MeshData m)
        {
            double sum = 0;
            int triCount = m.Triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                var v0 = m.Vertices[m.Triangles[i * 3 + 0]];
                var v1 = m.Vertices[m.Triangles[i * 3 + 1]];
                var v2 = m.Vertices[m.Triangles[i * 3 + 2]];
                var cr = Vector3.Cross(v1 - v0, v2 - v0);
                sum += 0.5 * cr.Length();
            }
            return sum;
        }

        private static void AssertOutwardNormals(MeshData m)
        {
            // Compute hull centroid as the mean of all referenced vertices.
            var sum = Vector3.Zero;
            for (int i = 0; i < m.Vertices.Length; i++) sum += m.Vertices[i];
            var centroid = sum / m.Vertices.Length;

            int triCount = m.Triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                var v0 = m.Vertices[m.Triangles[i * 3 + 0]];
                var v1 = m.Vertices[m.Triangles[i * 3 + 1]];
                var v2 = m.Vertices[m.Triangles[i * 3 + 2]];
                var faceCentroid = (v0 + v1 + v2) / 3f;
                var n = Vector3.Cross(v1 - v0, v2 - v0);
                Assert.True(
                    Vector3.Dot(n, faceCentroid - centroid) > 0,
                    $"Triangle {i} winds inward (idx {m.Triangles[i*3]},{m.Triangles[i*3+1]},{m.Triangles[i*3+2]}).");
            }
        }
    }
}
