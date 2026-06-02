/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using Xunit;

namespace SW2GZ.Test.Build
{
    public class LinkDefValidatorTests
    {
        private static LinkDef Link(string name, bool baseLink, params string[] ids) =>
            new LinkDef { Name = name, IsBase = baseLink, ComponentIds = new List<string>(ids) };

        [Fact]
        public void Valid_WhenEveryComponentAssignedOnce_OneBase_UniqueNames()
        {
            var links = new List<LinkDef> { Link("base", true, "a", "b"), Link("wheel", false, "c") };
            var issues = LinkDefValidator.Validate(links, new[] { "a", "b", "c" });
            Assert.Empty(issues);
        }

        [Fact]
        public void Flags_UnassignedComponent()
        {
            var links = new List<LinkDef> { Link("base", true, "a") };
            var issues = LinkDefValidator.Validate(links, new[] { "a", "b" });
            Assert.Contains(issues, i => i.Contains("unassigned") && i.Contains("b"));
        }

        [Fact]
        public void Flags_ComponentInTwoLinks()
        {
            var links = new List<LinkDef> { Link("base", true, "a"), Link("two", false, "a") };
            var issues = LinkDefValidator.Validate(links, new[] { "a" });
            Assert.Contains(issues, i => i.Contains("more than one") && i.Contains("a"));
        }

        [Fact]
        public void Flags_ZeroOrMultipleBase()
        {
            var none = LinkDefValidator.Validate(
                new List<LinkDef> { Link("x", false, "a") }, new[] { "a" });
            Assert.Contains(none, i => i.Contains("base"));

            var two = LinkDefValidator.Validate(
                new List<LinkDef> { Link("x", true, "a"), Link("y", true, "b") }, new[] { "a", "b" });
            Assert.Contains(two, i => i.Contains("base"));
        }

        [Fact]
        public void Flags_DuplicateNames_And_EmptyLink()
        {
            var links = new List<LinkDef> { Link("dup", true, "a"), Link("dup", false), };
            var issues = LinkDefValidator.Validate(links, new[] { "a" });
            Assert.Contains(issues, i => i.Contains("name"));
            Assert.Contains(issues, i => i.Contains("no components"));
        }
    }
}
