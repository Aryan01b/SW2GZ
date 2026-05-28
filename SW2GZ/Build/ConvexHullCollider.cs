using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SW2GZ.Build
{
    public static class ConvexHullCollider
    {
        // v2.0: ship the AABB fallback only. Real QuickHull lands in v2.1 once basic export
        // pipeline is green. AABB is a strict superset of the visual mesh, safe for collision.
        public static MeshData Build(MeshData visual)
        {
            if (visual == null || visual.Vertices == null || visual.Vertices.Length == 0)
                throw new ArgumentException("MeshData has no vertices", nameof(visual));

            float minX = visual.Vertices.Min(v => v.X), maxX = visual.Vertices.Max(v => v.X);
            float minY = visual.Vertices.Min(v => v.Y), maxY = visual.Vertices.Max(v => v.Y);
            float minZ = visual.Vertices.Min(v => v.Z), maxZ = visual.Vertices.Max(v => v.Z);

            return BoxMesh(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
        }

        private static MeshData BoxMesh(Vector3 lo, Vector3 hi)
        {
            var v = new Vector3[]
            {
                new(lo.X, lo.Y, lo.Z), new(hi.X, lo.Y, lo.Z),
                new(hi.X, hi.Y, lo.Z), new(lo.X, hi.Y, lo.Z),
                new(lo.X, lo.Y, hi.Z), new(hi.X, lo.Y, hi.Z),
                new(hi.X, hi.Y, hi.Z), new(lo.X, hi.Y, hi.Z),
            };
            var t = new int[]
            {
                0,1,2, 0,2,3,  // bottom
                4,6,5, 4,7,6,  // top
                0,4,5, 0,5,1,  // front
                2,6,7, 2,7,3,  // back
                0,3,7, 0,7,4,  // left
                1,5,6, 1,6,2,  // right
            };
            return new MeshData(v, t, null);
        }
    }
}
