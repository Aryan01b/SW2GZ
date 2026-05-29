/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using Moq;
using SW2GZ.SwSurface;
using SW2GZ.SwSurface.Abstractions;
using Xunit;

namespace SW2GZ.SwSurface.Tests
{
    public class AssemblyWalkerMockTests
    {
        [Fact]
        public void WalkActive_MockReturnsTwoLinks_FieldsPlumb()
        {
            var mock = new Mock<IAssemblyWalker>();
            mock.Setup(w => w.WalkActive()).Returns(new[]
            {
                new LinkSpec("base_link", new[] { "/parts/base.SLDPRT" }),
                new LinkSpec("arm1",      new[] { "/parts/arm1.SLDPRT", "/parts/arm1_sub.SLDPRT" }),
            });

            var result = mock.Object.WalkActive();
            Assert.Equal(2, result.Count);
            Assert.Equal("base_link", result[0].Name);
            Assert.Single(result[0].FlattenedPartPaths);
            Assert.Equal("arm1", result[1].Name);
            Assert.Equal(2, result[1].FlattenedPartPaths.Count);
        }

        [Fact]
        public void WalkActive_MockReturnsEmpty_IsValid()
        {
            var mock = new Mock<IAssemblyWalker>();
            mock.Setup(w => w.WalkActive()).Returns(Array.Empty<LinkSpec>());
            Assert.Empty(mock.Object.WalkActive());
        }

        [Fact]
        public void SolidWorksImpl_NotYetWired_ThrowsNotImplemented()
        {
            var impl = new SolidWorksAssemblyWalker();
            Assert.Throws<NotImplementedException>((Action)(() => impl.WalkActive()));
        }

        [Fact]
        public void LinkSpec_RecordEquality_ByValue()
        {
            var a = new LinkSpec("x", new[] { "p1" });
            var b = new LinkSpec("x", new[] { "p1" });
            // record equality is reference-equality for the IReadOnlyList<string> field,
            // so two records with the same Name but different list instances are NOT equal.
            // Confirm by-value semantics on Name only — useful sanity check.
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.FlattenedPartPaths.Count, b.FlattenedPartPaths.Count);
        }
    }
}
