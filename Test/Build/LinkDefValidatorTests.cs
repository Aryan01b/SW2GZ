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
        private static LinkDef L(string name, string parent, params string[] ids) =>
            new LinkDef { Name = name, ParentName = parent, ComponentIds = new List<string>(ids) };

        [Fact]
        public void Valid_SingleRoot_FullCoverage_UniqueNames()
        {
            var links = new List<LinkDef> { L("base", "", "a"), L("wheel", "base", "b") };
            Assert.Empty(LinkDefValidator.Validate(links, new[] { "a", "b" }));
        }

        [Fact]
        public void Flags_NoRoot_And_MultipleRoots()
        {
            var two = new List<LinkDef> { L("a", "", "x"), L("b", "", "y") };
            Assert.Contains(LinkDefValidator.Validate(two, new[] { "x", "y" }), i => i.Contains("root"));

            var cyc = new List<LinkDef> { L("a", "b", "x"), L("b", "a", "y") };
            Assert.Contains(LinkDefValidator.Validate(cyc, new[] { "x", "y" }), i => i.Contains("root") || i.Contains("cycle"));
        }

        [Fact]
        public void Flags_UnknownParent()
        {
            var links = new List<LinkDef> { L("base", "", "a"), L("arm", "ghost", "b") };
            Assert.Contains(LinkDefValidator.Validate(links, new[] { "a", "b" }), i => i.Contains("parent"));
        }

        [Fact]
        public void Flags_Unassigned_And_EmptyLink_And_DuplicateName()
        {
            var links = new List<LinkDef> { L("dup", "", "a"), L("dup", "dup") };
            var issues = LinkDefValidator.Validate(links, new[] { "a", "b" });
            Assert.Contains(issues, i => i.Contains("unassigned") && i.Contains("b"));
            Assert.Contains(issues, i => i.Contains("no components"));
            Assert.Contains(issues, i => i.Contains("name"));
        }
    }
}
