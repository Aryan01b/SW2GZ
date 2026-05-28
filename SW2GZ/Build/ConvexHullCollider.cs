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
            // 8 corners of the AABB box:
            //   0: (lo,lo,lo)   1: (hi,lo,lo)   2: (hi,hi,lo)   3: (lo,hi,lo)
            //   4: (lo,lo,hi)   5: (hi,lo,hi)   6: (hi,hi,hi)   7: (lo,hi,hi)
            var v = new Vector3[]
            {
                new(lo.X, lo.Y, lo.Z), new(hi.X, lo.Y, lo.Z),
                new(hi.X, hi.Y, lo.Z), new(lo.X, hi.Y, lo.Z),
                new(lo.X, lo.Y, hi.Z), new(hi.X, lo.Y, hi.Z),
                new(hi.X, hi.Y, hi.Z), new(lo.X, hi.Y, hi.Z),
            };
            // CCW winding seen from outside; normals point AWAY from box center.
            var t = new int[]
            {
                0,3,2, 0,2,1,   // bottom face (-Z)
                4,5,6, 4,6,7,   // top    face (+Z)
                0,1,5, 0,5,4,   // front  face (-Y)
                2,3,7, 2,7,6,   // back   face (+Y)
                0,4,7, 0,7,3,   // left   face (-X)
                1,2,6, 1,6,5,   // right  face (+X)
            };
            return new MeshData(v, t, null);
        }
    }
}
