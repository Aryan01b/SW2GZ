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
    }
}
