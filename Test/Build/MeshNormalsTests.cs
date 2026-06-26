/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Numerics;
using SW2GZ.Build;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class MeshNormalsTests
    {
        [Fact]
        public void CoplanarFaces_SmoothToSharedNormal()
        {
            // Two coplanar triangles (a flat quad in XY) → every vertex normal +Z.
            var mesh = new MeshData(
                new[]
                {
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0),
                    new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0),
                },
                new[] { 0, 1, 2, 3, 4, 5 }, null);

            var n = MeshNormals.ComputeSmooth(mesh);
            Assert.Equal(6, n.Length);
            foreach (var v in n)
                Assert.True(v.Z > 0.99f, "expected +Z normal, got " + v);
        }

        [Fact]
        public void SharpCrease_KeepsPerFaceNormals()
        {
            // Floor (XY, +Z) and wall (XZ, -Y) share an edge at 90° > crease →
            // the coincident verts must NOT average to a 45° normal.
            var mesh = new MeshData(
                new[]
                {
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), // floor → +Z
                    new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), // wall  → -Y
                },
                new[] { 0, 1, 2, 3, 4, 5 }, null);

            var n = MeshNormals.ComputeSmooth(mesh);
            // Floor corners stay +Z; wall corners stay -Y (sharp edge preserved).
            Assert.True(n[0].Z > 0.99f && n[1].Z > 0.99f && n[2].Z > 0.99f);
            Assert.True(n[3].Y < -0.99f && n[4].Y < -0.99f && n[5].Y < -0.99f);
        }
    }
}
