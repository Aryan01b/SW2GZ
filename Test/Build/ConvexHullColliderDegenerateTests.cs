using System;
using System.Numerics;
using SW2GZ.Build;
using Xunit;

namespace SW2GZ.Build.Tests
{
    /// <summary>
    /// Fix 1 — degenerate-geometry graceful degradation. A flat or colinear part
    /// (gasket, shim, washer, thin sheet-metal) tessellates to a cloud QuickHull
    /// cannot turn into a closed 3D hull. The ConvexHull strategy must fall back
    /// to the AABB box instead of throwing and aborting the whole export.
    /// </summary>
    public class ConvexHullColliderDegenerateTests
    {
        private static int TriCount(MeshData m) => m.Triangles.Length / 3;

        // 1. Coplanar cloud (all z = 0) → valid non-empty AABB box, no throw.
        [Fact]
        public void Build_ConvexHull_CoplanarCloud_FallsBackToAabbBox()
        {
            var verts = new Vector3[]
            {
                new(0, 0, 0), new(2, 0, 0), new(2, 3, 0), new(0, 3, 0),
                new(1, 1, 0), new(1.5f, 2f, 0),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);

            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);

            // AABB box: 8 corners, 12 triangles.
            Assert.NotNull(hull);
            Assert.Equal(8, hull.Vertices.Length);
            Assert.Equal(12, TriCount(hull));
        }

        // 2. Colinear cloud (all points on one line) → AABB box, no throw.
        [Fact]
        public void Build_ConvexHull_ColinearCloud_FallsBackToAabbBox()
        {
            var verts = new Vector3[]
            {
                new(0, 0, 0), new(1, 1, 1), new(2, 2, 2), new(3, 3, 3),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);

            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);

            Assert.NotNull(hull);
            Assert.Equal(8, hull.Vertices.Length);
            Assert.Equal(12, TriCount(hull));
        }

        // 3. Proper 3D solid (cube corners) → a REAL hull, not the AABB fallback.
        //    Proves the fallback triggers only on degeneracy. A genuine QuickHull
        //    of 8 cube corners yields 8 vertices and 12 triangles, but with the
        //    hull's own (non-AABB-template) triangulation/winding.
        [Fact]
        public void Build_ConvexHull_CubeCorners_ReturnsRealHull_NotDegenerateFallback()
        {
            var verts = new Vector3[]
            {
                new(-1,-1,-1), new( 1,-1,-1), new( 1, 1,-1), new(-1, 1,-1),
                new(-1,-1, 1), new( 1,-1, 1), new( 1, 1, 1), new(-1, 1, 1),
            };
            var mesh = new MeshData(verts, Array.Empty<int>(), null);

            var hull = ConvexHullCollider.Build(mesh, ColliderStrategy.ConvexHull);

            Assert.NotNull(hull);
            // A closed convex hull of 8 corners: 8 verts, 12 triangular faces
            // (Euler: V - E + F = 2 with F triangles → F = 2V - 4 = 12).
            Assert.Equal(8, hull.Vertices.Length);
            Assert.Equal(12, TriCount(hull));
            // Cube surface area = 6 faces * 2*2 = 24, confirming a real closed hull.
            Assert.Equal(24.0, SurfaceArea(hull), 4);
        }

        private static double SurfaceArea(MeshData m)
        {
            double area = 0.0;
            for (int i = 0; i < m.Triangles.Length; i += 3)
            {
                Vector3 a = m.Vertices[m.Triangles[i]];
                Vector3 b = m.Vertices[m.Triangles[i + 1]];
                Vector3 c = m.Vertices[m.Triangles[i + 2]];
                area += 0.5 * Vector3.Cross(b - a, c - a).Length();
            }
            return area;
        }
    }
}
