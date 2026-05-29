/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using System.Numerics;
using Moq;
using SW2GZ.Build;
using SW2GZ.SwSurface;
using SW2GZ.SwSurface.Abstractions;
using Xunit;

namespace SW2GZ.SwSurface.Tests
{
    public class MeshTessellatorMockTests
    {
        [Fact]
        public void Tessellate_MockReturnsKnownMesh_PassesThrough()
        {
            var expected = new MeshData(
                new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                new[] { 0, 1, 2 },
                System.Drawing.Color.Blue);

            var mock = new Mock<IMeshTessellator>();
            mock.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>()))
                .Returns(expected);

            var got = mock.Object.Tessellate("/parts/x.SLDPRT", TessellationLod.Coarse);
            Assert.Equal(3, got.Vertices.Length);
            Assert.Equal(3, got.Triangles.Length);
            Assert.Equal(System.Drawing.Color.Blue, got.MaterialColor);
        }

        [Fact]
        public void Tessellate_MockDistinguishesLod()
        {
            var coarse = new MeshData(new[] { Vector3.Zero }, new int[0], null);
            var fine   = new MeshData(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }, new int[0], null);

            var mock = new Mock<IMeshTessellator>();
            mock.Setup(t => t.Tessellate(It.IsAny<string>(), TessellationLod.Coarse)).Returns(coarse);
            mock.Setup(t => t.Tessellate(It.IsAny<string>(), TessellationLod.Fine)).Returns(fine);

            Assert.Single(mock.Object.Tessellate("p", TessellationLod.Coarse).Vertices);
            Assert.Equal(4, mock.Object.Tessellate("p", TessellationLod.Fine).Vertices.Length);
        }

        [Fact]
        public void SolidWorksImpl_NotYetWired_ThrowsNotImplemented()
        {
            var impl = new SolidWorksMeshTessellator();
            Assert.Throws<NotImplementedException>((Action)(() => impl.Tessellate("/parts/x.SLDPRT", TessellationLod.Coarse)));
        }

        [Fact]
        public void TessellationLod_HasCoarseAndFineMembers()
        {
            Assert.Equal(0, (int)TessellationLod.Coarse);
            Assert.Equal(1, (int)TessellationLod.Fine);
        }
    }
}
