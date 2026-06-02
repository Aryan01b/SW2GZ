/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using System.Linq;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using Xunit;

namespace SW2GZ.Test.Build
{
    public class LinkHierarchyTests
    {
        private static LinkDef L(string name, string parent, params string[] ids) =>
            new LinkDef { Name = name, ParentName = parent, ComponentIds = new List<string>(ids) };

        private static List<LinkDef> Tree() => new List<LinkDef>
        {
            L("base", ""), L("arm", "base"), L("hand", "arm"), L("wheel", "base"),
        };

        [Fact]
        public void Roots_ReturnsParentlessLinks()
        {
            var roots = LinkHierarchy.Roots(Tree());
            Assert.Single(roots);
            Assert.Equal("base", roots[0].Name);
        }

        [Fact]
        public void ChildrenOf_ReturnsDirectChildren()
        {
            var kids = LinkHierarchy.ChildrenOf(Tree(), "base").Select(l => l.Name).ToList();
            Assert.Equal(new[] { "arm", "wheel" }, kids);
        }

        [Fact]
        public void IsDescendant_TrueForTransitiveChild()
        {
            Assert.True(LinkHierarchy.IsDescendant(Tree(), "base", "hand"));
            Assert.False(LinkHierarchy.IsDescendant(Tree(), "hand", "base"));
        }

        [Fact]
        public void HasCycle_DetectsLoop()
        {
            var links = new List<LinkDef> { L("a", "b"), L("b", "a") };
            Assert.True(LinkHierarchy.HasCycle(links));
            Assert.False(LinkHierarchy.HasCycle(Tree()));
        }

        [Fact]
        public void AssignComponent_MovesFromPreviousLink()
        {
            var links = new List<LinkDef> { L("base", "", "c1"), L("arm", "base") };
            LinkHierarchy.AssignComponent(links, "arm", "c1");
            Assert.Empty(links[0].ComponentIds);
            Assert.Equal(new[] { "c1" }, links[1].ComponentIds.ToArray());
        }

        [Fact]
        public void AssignComponent_NoDuplicateWhenAlreadyOnTarget()
        {
            var links = new List<LinkDef> { L("base", "", "c1") };
            LinkHierarchy.AssignComponent(links, "base", "c1");
            Assert.Equal(new[] { "c1" }, links[0].ComponentIds.ToArray());
        }

        [Fact]
        public void Reroot_MakesChosenLinkTheRoot()
        {
            var links = Tree();
            LinkHierarchy.Reroot(links, "arm");
            Assert.Equal("", links.First(l => l.Name == "arm").ParentName);
            Assert.Equal("arm", links.First(l => l.Name == "base").ParentName);
            Assert.False(LinkHierarchy.HasCycle(links));
            Assert.Single(LinkHierarchy.Roots(links));
        }
    }
}
