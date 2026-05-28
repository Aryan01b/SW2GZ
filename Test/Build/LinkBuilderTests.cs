using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class LinkBuilderTests
    {
        [Fact]
        public void Build_FillsAllFieldsFromInputs()
        {
            var mass = new MassProps(2.5, new Vector3(0.1f, 0, 0), Matrix3.Identity);
            var visual = new MeshData(new[] { Vector3.Zero, Vector3.One, new Vector3(1, 0, 0) }, new[] { 0, 1, 2 }, null);
            var collision = ConvexHullCollider.Build(visual);

            var link = LinkBuilder.Build("base_link", mass, visual, collision);

            Assert.Equal("base_link", link.Name);
            Assert.Equal(2.5, link.Mass);
            Assert.Equal(0.1, link.ComLocal.X, 5);
            Assert.NotNull(link.VisualMesh);
            Assert.NotNull(link.CollisionMesh);
            Assert.Equal("base_link.dae", link.VisualMeshFile);
            Assert.Equal("base_link_collision.stl", link.CollisionMeshFile);
        }
    }
}
