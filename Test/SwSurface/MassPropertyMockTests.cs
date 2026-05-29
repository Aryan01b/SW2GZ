/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Numerics;
using Moq;
using SW2GZ.Build;
using SW2GZ.Exceptions;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using Xunit;

namespace SW2GZ.SwSurface.Tests
{
    public class MassPropertyMockTests
    {
        [Fact]
        public void Get_WhenMockReturnsValue_PassesThroughFields()
        {
            var expected = new MassProps(2.5, new Vector3(0.1f, 0.2f, 0.3f), Matrix3.Identity);
            var mock = new Mock<IMassProperties>();
            mock.Setup(m => m.Get("part1")).Returns(expected);

            var got = mock.Object.Get("part1");
            Assert.Equal(2.5, got.Mass);
            Assert.Equal(0.1f, got.ComLocal.X, 5);
            Assert.Equal(1.0, got.InertiaAtComLocal.M11);
        }

        [Fact]
        public void Get_WhenMockThrowsMaterialMissing_BubblesUp()
        {
            var mock = new Mock<IMassProperties>();
            mock.Setup(m => m.Get("bad")).Throws(new MaterialMissingException("bad"));

            var ex = Assert.Throws<MaterialMissingException>(() => mock.Object.Get("bad"));
            Assert.Equal("bad", ex.LinkName);
        }

        [Fact]
        public void SolidWorksImpl_NotYetWired_ThrowsNotImplemented()
        {
            // T28 wires the SW invocation. Until then the skeleton throws so
            // upstream callers notice if they invoke the SW surface prematurely.
            var impl = new SolidWorksMassProperties();
            Assert.Throws<System.NotImplementedException>(() => impl.Get("/parts/x.SLDPRT"));
        }
    }
}
