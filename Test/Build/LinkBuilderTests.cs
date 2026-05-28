using System;
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
        public void Build_FillsAllEightFieldsFromInputs()
        {
            var inertia = new Matrix3(2, 0, 0, 0, 3, 0, 0, 0, 4);
            var mass = new MassProps(2.5, new Vector3(0.1f, 0.2f, 0.3f), inertia);
            var visual = new MeshData(new[] { Vector3.Zero, Vector3.One, new Vector3(1, 0, 0) }, new[] { 0, 1, 2 }, null);
            var collision = ConvexHullCollider.Build(visual);

            var link = LinkBuilder.Build("base_link", mass, visual, collision);

            Assert.Equal("base_link", link.Name);
            Assert.Equal(2.5, link.Mass);

            Assert.Equal(0.1, link.ComLocal.X, 5);
            Assert.Equal(0.2, link.ComLocal.Y, 5);
            Assert.Equal(0.3, link.ComLocal.Z, 5);

            Assert.Equal(2.0, link.InertiaAtComLocal.M11);
            Assert.Equal(3.0, link.InertiaAtComLocal.M22);
            Assert.Equal(4.0, link.InertiaAtComLocal.M33);

            Assert.Same(visual,    link.VisualMesh);
            Assert.Same(collision, link.CollisionMesh);
            Assert.Equal("base_link.dae",            link.VisualMeshFile);
            Assert.Equal("base_link_collision.stl",  link.CollisionMeshFile);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Build_RejectsBlankName(string name)
        {
            var mass = new MassProps(1, Vector3.Zero, Matrix3.Identity);
            var visual = new MeshData(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { 0, 1, 2 }, null);
            var collision = ConvexHullCollider.Build(visual);

            Assert.Throws<ArgumentException>(() => LinkBuilder.Build(name, mass, visual, collision));
        }

        [Fact]
        public void Build_RejectsNullMass()
        {
            var visual = new MeshData(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { 0, 1, 2 }, null);
            var collision = ConvexHullCollider.Build(visual);

            Assert.Throws<ArgumentNullException>(() => LinkBuilder.Build("base_link", null, visual, collision));
        }

        [Fact]
        public void Build_RejectsNullVisual()
        {
            var mass = new MassProps(1, Vector3.Zero, Matrix3.Identity);
            var collision = new MeshData(new[] { Vector3.Zero }, new int[0], null);

            Assert.Throws<ArgumentNullException>(() => LinkBuilder.Build("base_link", mass, null, collision));
        }

        [Fact]
        public void Build_RejectsNullCollision()
        {
            var mass = new MassProps(1, Vector3.Zero, Matrix3.Identity);
            var visual = new MeshData(new[] { Vector3.Zero }, new int[0], null);

            Assert.Throws<ArgumentNullException>(() => LinkBuilder.Build("base_link", mass, visual, null));
        }
    }
}
