/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P5 — RobotModelBuilder.AssembleLinksWithMaterials tests. Covers the
RGBA validation, sanitization, dedup-by-name, and conflict-detection
paths spec'd in the P5 roadmap entry.
*/
using System;
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using Xunit;

namespace SW2GZ.Test.Build
{
    public class RobotModelBuilderMaterialsTests
    {
        // Lookup-table appearance source — simple test harness so we don't need Moq here.
        private sealed class MapAppearanceSource : IAppearanceSource
        {
            private readonly Dictionary<string, MaterialDef?> _map;
            public MapAppearanceSource(Dictionary<string, MaterialDef?> map) { _map = map; }
            public MaterialDef? GetMaterial(string partPath) =>
                _map.TryGetValue(partPath, out MaterialDef? m) ? m : null;
        }

        private static UrdfLink MakeLink(string name) =>
            new UrdfLink(
                name,
                Mass: 1.0,
                ComLocal: Vector3.Zero,
                InertiaAtComLocal: Matrix3.Identity,
                VisualMesh: null,
                CollisionMesh: null,
                VisualMeshFile: $"{name}.dae",
                CollisionMeshFile: $"{name}_collision.stl");

        [Fact]
        public void SingleLink_NoAppearance_EmptyMaterials_NullName()
        {
            var src = new MapAppearanceSource(new Dictionary<string, MaterialDef?>
            {
                ["/p/base"] = null,
            });
            var input = new[] { (MakeLink("base"), "/p/base") };

            var (links, mats) = RobotModelBuilder.AssembleLinksWithMaterials(input, src);

            Assert.Single(links);
            Assert.Null(links[0].MaterialName);
            Assert.Empty(mats);
        }

        [Fact]
        public void SingleLink_OneMaterial_Tagged()
        {
            var red = new MaterialDef("red", 1.0, 0.0, 0.0, 1.0);
            var src = new MapAppearanceSource(new Dictionary<string, MaterialDef?>
            {
                ["/p/base"] = red,
            });
            var input = new[] { (MakeLink("base"), "/p/base") };

            var (links, mats) = RobotModelBuilder.AssembleLinksWithMaterials(input, src);

            Assert.Single(links);
            Assert.Equal("red", links[0].MaterialName);
            Assert.Single(mats);
            Assert.Equal("red", mats[0].Name);
            Assert.Equal(1.0, mats[0].R);
        }

        [Fact]
        public void TwoLinks_SameMaterial_Deduped()
        {
            var blue = new MaterialDef("blue", 0.0, 0.0, 1.0, 1.0);
            var src = new MapAppearanceSource(new Dictionary<string, MaterialDef?>
            {
                ["/p/a"] = blue,
                ["/p/b"] = new MaterialDef("blue", 0.0, 0.0, 1.0, 1.0),
            });
            var input = new[]
            {
                (MakeLink("a"), "/p/a"),
                (MakeLink("b"), "/p/b"),
            };

            var (links, mats) = RobotModelBuilder.AssembleLinksWithMaterials(input, src);

            Assert.Equal(2, links.Count);
            Assert.Equal("blue", links[0].MaterialName);
            Assert.Equal("blue", links[1].MaterialName);
            Assert.Single(mats);
        }

        [Fact]
        public void TwoLinks_SameNameDifferentRgba_Throws()
        {
            // Use raw names that sanitize to the same key but differ as inputs, so
            // the message can be checked for both raw forms.
            var src = new MapAppearanceSource(new Dictionary<string, MaterialDef?>
            {
                ["/p/a"] = new MaterialDef("Blue!", 0.0, 0.0, 1.0, 1.0),
                ["/p/b"] = new MaterialDef("Blue?", 0.0, 0.0, 0.5, 1.0),
            });
            var input = new[]
            {
                (MakeLink("a"), "/p/a"),
                (MakeLink("b"), "/p/b"),
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                RobotModelBuilder.AssembleLinksWithMaterials(input, src));
            Assert.Contains("conflicting definitions", ex.Message);
            Assert.Contains("Blue!", ex.Message);
            Assert.Contains("Blue?", ex.Message);
        }

        [Theory]
        [InlineData(-0.1, 0.0, 0.0, 1.0)]
        [InlineData(1.5,  0.0, 0.0, 1.0)]
        [InlineData(0.0, -0.1, 0.0, 1.0)]
        [InlineData(0.0,  1.5, 0.0, 1.0)]
        [InlineData(0.0,  0.0, -0.1, 1.0)]
        [InlineData(0.0,  0.0,  1.5, 1.0)]
        [InlineData(0.0,  0.0,  0.0, -0.1)]
        [InlineData(0.0,  0.0,  0.0,  1.5)]
        public void OutOfRangeRgba_Throws(double r, double g, double b, double a)
        {
            var src = new MapAppearanceSource(new Dictionary<string, MaterialDef?>
            {
                ["/p/a"] = new MaterialDef("bright", r, g, b, a),
            });
            var input = new[] { (MakeLink("a"), "/p/a") };

            Assert.Throws<ArgumentException>(() =>
                RobotModelBuilder.AssembleLinksWithMaterials(input, src));
        }

        [Fact]
        public void SanitizedNameUsedForDedup()
        {
            // "Sky Blue!" and "Sky_Blue" both sanitize to "Sky_Blue".
            var src = new MapAppearanceSource(new Dictionary<string, MaterialDef?>
            {
                ["/p/a"] = new MaterialDef("Sky Blue!", 0.5, 0.5, 1.0, 1.0),
                ["/p/b"] = new MaterialDef("Sky_Blue",  0.5, 0.5, 1.0, 1.0),
            });
            var input = new[]
            {
                (MakeLink("a"), "/p/a"),
                (MakeLink("b"), "/p/b"),
            };

            var (links, mats) = RobotModelBuilder.AssembleLinksWithMaterials(input, src);

            Assert.Single(mats);
            Assert.Equal("Sky_Blue", mats[0].Name);
            Assert.Equal("Sky_Blue", links[0].MaterialName);
            Assert.Equal("Sky_Blue", links[1].MaterialName);
        }
    }
}
