using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SW2GZ.Build
{
    /// <summary>
    /// Builds a closed collision mesh from a visual mesh.
    ///
    /// P4 (v2.1): adds real 3D convex hull (QuickHull) via
    /// <see cref="ColliderStrategy.ConvexHull"/>. AABB remains as
    /// <see cref="ColliderStrategy.Aabb"/> for the explicit primitive fallback
    /// and is the default of the parameterless <see cref="Build(MeshData)"/>
    /// overload to preserve historic behavior for unit-test callers that
    /// pass degenerate point clouds (colinear / coplanar) and expect a
    /// safe AABB box rather than an exception.
    ///
    /// Production code (<c>Sw2gzPipeline</c>) opts in to
    /// <see cref="ColliderStrategy.ConvexHull"/> explicitly.
    ///
    /// Output mesh is closed-manifold with CCW winding seen from outside —
    /// every face normal points away from the hull centroid.
    /// </summary>
    public static class ConvexHullCollider
    {
        /// <summary>
        /// Backwards-compatible entry point. Defaults to
        /// <see cref="ColliderStrategy.Aabb"/> — the original v2.0 behavior.
        /// New callers should prefer the strategy-aware overload.
        /// </summary>
        public static MeshData Build(MeshData visual)
            => Build(visual, ColliderStrategy.Aabb);

        /// <summary>
        /// Strategy-aware collider builder. <see cref="ColliderStrategy.ConvexHull"/>
        /// invokes the 3D QuickHull implementation; degenerate inputs throw
        /// <see cref="ArgumentException"/>. <see cref="ColliderStrategy.Aabb"/>
        /// returns the axis-aligned bounding box and tolerates degenerate inputs.
        /// </summary>
        public static MeshData Build(MeshData visual, ColliderStrategy strategy)
        {
            switch (strategy)
            {
                case ColliderStrategy.Aabb:
                    return BuildAabbHull(visual);
                case ColliderStrategy.ConvexHull:
                    try
                    {
                        return QuickHull3D.Build(visual);
                    }
                    catch (ArgumentException)
                    {
                        // Degenerate input: a planar/colinear/thin part (gasket, shim,
                        // washer, sheet-metal) tessellates to a cloud with no 3D extent,
                        // so QuickHull3D cannot form a closed 3D hull and throws
                        // ArgumentException. AABB is the safe closed-manifold fallback —
                        // a flat link still gets a valid box collider instead of aborting
                        // the whole export. Only ArgumentException (the documented
                        // degeneracy signal) is caught; other failures propagate.
                        return BuildAabbHull(visual);
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown collider strategy.");
            }
        }

        private static MeshData BuildAabbHull(MeshData visual)
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
