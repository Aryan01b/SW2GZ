/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure-C# tests for the geometry-assignment model the native PMP writes and the
wizard reads. No SolidWorks dependency.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class GeometryAssignmentTests
    {
        private static GeometryAssignment ThreeLinks() =>
            new GeometryAssignment(new[] { "base_link", "link1", "link2" });

        [Fact]
        public void SeedsOneLinkGeometryPerName()
        {
            GeometryAssignment ga = ThreeLinks();
            Assert.Equal(3, ga.Links.Count);
            Assert.Equal("base_link", ga.Links[0].LinkName);
            Assert.Equal("link2", ga.Links[2].LinkName);
            Assert.All(ga.Links, l => Assert.False(l.HasGeometry));
        }

        [Fact]
        public void NullSeedYieldsEmptyLinkList()
        {
            var ga = new GeometryAssignment(null);
            Assert.Empty(ga.Links);
        }

        [Fact]
        public void FindReturnsMatchingLink()
        {
            GeometryAssignment ga = ThreeLinks();
            LinkGeometry link = ga.Find("link1");
            Assert.NotNull(link);
            Assert.Equal("link1", link.LinkName);
        }

        [Fact]
        public void FindUnknownLinkReturnsNull()
        {
            GeometryAssignment ga = ThreeLinks();
            Assert.Null(ga.Find("does_not_exist"));
            Assert.Null(ga.Find(null));
        }

        [Fact]
        public void AssignFlipsHasGeometryAndStoresNames()
        {
            LinkGeometry link = ThreeLinks().Find("base_link");
            Assert.False(link.HasGeometry);

            link.Assign(new[] { "body_a", "body_b" });

            Assert.True(link.HasGeometry);
            Assert.Equal(new[] { "body_a", "body_b" }, link.SelectedBodyNames);
        }

        [Fact]
        public void AssignReplacesPreviousSelection()
        {
            LinkGeometry link = new LinkGeometry("l");
            link.Assign(new[] { "body_a" });
            link.Assign(new[] { "body_b", "body_c" });

            Assert.Equal(new[] { "body_b", "body_c" }, link.SelectedBodyNames);
        }

        [Fact]
        public void AssignSkipsNullAndWhitespaceNames()
        {
            LinkGeometry link = new LinkGeometry("l");
            link.Assign(new List<string> { "body_a", null, "  ", "body_b" });

            Assert.Equal(new[] { "body_a", "body_b" }, link.SelectedBodyNames);
        }

        [Fact]
        public void AssignNullClearsAndStaysEmpty()
        {
            LinkGeometry link = new LinkGeometry("l");
            link.Assign(new[] { "body_a" });
            link.Assign(null);

            Assert.False(link.HasGeometry);
            Assert.Empty(link.SelectedBodyNames);
        }

        [Fact]
        public void ClearEmptiesAssignment()
        {
            LinkGeometry link = ThreeLinks().Find("base_link");
            link.Assign(new[] { "body_a", "body_b" });
            Assert.True(link.HasGeometry);

            link.Clear();

            Assert.False(link.HasGeometry);
            Assert.Empty(link.SelectedBodyNames);
        }

        [Fact]
        public void LinkNameIsEditable()
        {
            LinkGeometry link = new LinkGeometry("raw");
            link.LinkName = "sanitized";
            Assert.Equal("sanitized", link.LinkName);
        }
    }
}
