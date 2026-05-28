/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using SW2GZ.Gz;
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestSdfModelWriter : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void WritesModelSdfWithMatchingSdfVersionAndModelName()
        {
            var input = new SdfModelInput
            {
                Name = "my_asset",
                Links = new List<SdfLinkData> { new SdfLinkData { Name = "base_link" } },
                Joints = new List<SdfJointData>(),
            };
            new SdfModelWriter(input, new TargetProfile { Gz = GzVersion.Harmonic }).Write(TempDir);
            Assert.True(Exists("model.sdf"));
            var doc = LoadXml("model.sdf");
            Assert.Equal("sdf", doc.Root.Name.LocalName);
            Assert.Equal("1.10", doc.Root.Attribute("version").Value);
            var model = doc.Root.Element("model");
            Assert.Equal("my_asset", model.Attribute("name").Value);
            Assert.NotNull(model.Element("link"));
            Assert.Equal("base_link", model.Element("link").Attribute("name").Value);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void EmitsJointsWithParentChild()
        {
            var input = new SdfModelInput
            {
                Name = "two_link",
                Links = new List<SdfLinkData>
                {
                    new SdfLinkData { Name = "base_link" },
                    new SdfLinkData { Name = "arm" },
                },
                Joints = new List<SdfJointData>
                {
                    new SdfJointData { Name = "shoulder", Type = "revolute", Parent = "base_link", Child = "arm" },
                },
            };
            new SdfModelWriter(input, new TargetProfile { Gz = GzVersion.Harmonic }).Write(TempDir);
            var doc = LoadXml("model.sdf");
            var joint = doc.Root.Element("model").Element("joint");
            Assert.Equal("shoulder", joint.Attribute("name").Value);
            Assert.Equal("revolute", joint.Attribute("type").Value);
            Assert.Equal("base_link", joint.Element("parent").Value);
            Assert.Equal("arm", joint.Element("child").Value);
        }
    }
}
