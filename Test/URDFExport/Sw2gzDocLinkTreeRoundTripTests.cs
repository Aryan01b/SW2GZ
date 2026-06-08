/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

D1 guards that Sw2gzDoc round-trips a non-trivial link tree (A → B → C)
through DataContract serialization without loss. Sw2gzCreateRobotPmp.cs
no longer re-seeds Robot.Links from the assembly when a tree is already
present, so the persisted tree must survive verbatim across save/load.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Test.URDFExport
{
    public class Sw2gzDocLinkTreeRoundTripTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void ChainOfThreeLinks_SurvivesXmlRoundTrip()
        {
            var src = new Sw2gzDoc();
            src.Robot.Links = new List<LinkDef>
            {
                new LinkDef { Name = "A", ParentName = "",  ComponentIds = new List<string> { "a-1@asm" } },
                new LinkDef { Name = "B", ParentName = "A", ComponentIds = new List<string> { "b-1@asm" } },
                new LinkDef { Name = "C", ParentName = "B", ComponentIds = new List<string> { "c-1@asm", "c-2@asm" } },
            };

            string xml = Sw2gzDocCodec.ToXmlString(src);
            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(xml);

            Assert.NotNull(dst);
            Assert.Equal(3, dst.Robot.Links.Count);

            // Tree shape preserved in order.
            Assert.Equal("A", dst.Robot.Links[0].Name);
            Assert.Equal("",  dst.Robot.Links[0].ParentName);
            Assert.Equal("B", dst.Robot.Links[1].Name);
            Assert.Equal("A", dst.Robot.Links[1].ParentName);
            Assert.Equal("C", dst.Robot.Links[2].Name);
            Assert.Equal("B", dst.Robot.Links[2].ParentName);

            // Component-id payloads preserved.
            Assert.Equal(new[] { "a-1@asm" }, dst.Robot.Links[0].ComponentIds);
            Assert.Equal(new[] { "b-1@asm" }, dst.Robot.Links[1].ComponentIds);
            Assert.Equal(new[] { "c-1@asm", "c-2@asm" }, dst.Robot.Links[2].ComponentIds);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void EmptyLinks_RoundTripsAsEmpty()
        {
            var src = new Sw2gzDoc();
            // No Robot.Links populated. This is the seed-needed case the
            // Create-Robot PMP detects on first open.

            string xml = Sw2gzDocCodec.ToXmlString(src);
            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(xml);

            Assert.NotNull(dst);
            Assert.Empty(dst.Robot.Links);
        }
    }
}
