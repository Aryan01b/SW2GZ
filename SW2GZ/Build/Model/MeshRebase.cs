/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure helper: transforms a MeshData (assembly-frame vertices) into a
link-local frame by applying the inverse of the link's anchor pose.

Identity anchor → vertices unchanged (no-op). Used by Sw2gzPipeline
after the tessellator returns assembly-frame meshes, so each link's
URDF visual/collision mesh ends up centred at the link's frame origin.
*/
using System;
using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public static class MeshRebase
    {
        /// Returns a new MeshData with every vertex transformed by
        /// `anchor⁻¹`. Triangle indices, material color, and array sizes are
        /// preserved. Returns the input unchanged when anchor is identity
        /// (so legacy test paths emit byte-identical mesh files).
        public static MeshData Apply(MeshData mesh, Pose anchor)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (IsIdentity(anchor)) return mesh;

            Pose inv = PoseMath.Inverse(anchor);
            var rebased = new Vector3[mesh.Vertices.Length];
            for (int i = 0; i < rebased.Length; i++)
                rebased[i] = PoseMath.TransformPoint(inv, mesh.Vertices[i]);
            return new MeshData(rebased, mesh.Triangles, mesh.MaterialColor);
        }

        private static bool IsIdentity(Pose p)
        {
            if (p == null) return true;
            if (p.Position != Vector3.Zero) return false;
            Quaternion q = p.Rotation;
            // Allow ±identity quaternion (q and -q encode the same rotation).
            const float eps = 1e-6f;
            float dx = System.Math.Abs(q.X);
            float dy = System.Math.Abs(q.Y);
            float dz = System.Math.Abs(q.Z);
            float w  = System.Math.Abs(q.W);
            return dx < eps && dy < eps && dz < eps && System.Math.Abs(w - 1f) < eps;
        }
    }
}
