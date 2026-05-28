using System.Numerics;
using SW2GZ.Build;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class ConvexHullColliderTests
    {
        [Fact]
        public void Build_Cube_Produces12Triangles()
        {
            // 8 vertices of unit cube
            var verts = new Vector3[]
            {
                new(-1,-1,-1), new( 1,-1,-1), new( 1, 1,-1), new(-1, 1,-1),
                new(-1,-1, 1), new( 1,-1, 1), new( 1, 1, 1), new(-1, 1, 1),
            };
            var mesh = new MeshData(verts, new int[0], null);
            var hull = ConvexHullCollider.Build(mesh);
            Assert.Equal(12, hull.Triangles.Length / 3);
            Assert.Equal(8, hull.Vertices.Length);
        }

        [Fact]
        public void Build_DegenerateColinear_FallsBackToAabb()
        {
            // 3 colinear points → degenerate, hull falls back to AABB (which is also degenerate but
            // safe — returns a 0-thickness box, 12 triangles)
            var verts = new Vector3[] { new(0,0,0), new(1,0,0), new(2,0,0) };
            var mesh = new MeshData(verts, new int[0], null);
            var hull = ConvexHullCollider.Build(mesh);
            Assert.Equal(12, hull.Triangles.Length / 3); // AABB box
        }

        [Fact]
        public void Build_Cube_AllFaceNormalsPointOutward()
        {
            var verts = new Vector3[]
            {
                new(-1,-1,-1), new( 1,-1,-1), new( 1, 1,-1), new(-1, 1,-1),
                new(-1,-1, 1), new( 1,-1, 1), new( 1, 1, 1), new(-1, 1, 1),
            };
            var mesh = new MeshData(verts, new int[0], null);
            var hull = ConvexHullCollider.Build(mesh);

            int triCount = hull.Triangles.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                var v0 = hull.Vertices[hull.Triangles[i * 3 + 0]];
                var v1 = hull.Vertices[hull.Triangles[i * 3 + 1]];
                var v2 = hull.Vertices[hull.Triangles[i * 3 + 2]];

                // Triangle centroid relative to box center (which is the origin for this fixture)
                var centroid = (v0 + v1 + v2) / 3f;

                // Outward normal must have a positive dot product with the centroid-from-center vector
                var normal = Vector3.Cross(v1 - v0, v2 - v0);
                Assert.True(Vector3.Dot(normal, centroid) > 0,
                    $"Triangle {i} (indices {hull.Triangles[i*3]},{hull.Triangles[i*3+1]},{hull.Triangles[i*3+2]}) winds inward; centroid={centroid}, normal={normal}");
            }
        }
    }
}
