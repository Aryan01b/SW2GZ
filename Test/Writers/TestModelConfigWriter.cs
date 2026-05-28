/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Gz;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestModelConfigWriter : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void EmitsModelConfigWithExpectedFields()
        {
            new ModelConfigWriter(new ModelConfigWriter.Input
            {
                Name = "my_asset",
                SdfVersion = "1.10",
                SdfFile = "model.sdf",
                Author = "aryan",
                Email = "aryan@example.com",
                Description = "Exported by SW2GZ",
            }).Write(TempDir);

            Assert.True(Exists("model.config"));
            var doc = LoadXml("model.config");
            Assert.Equal("model", doc.Root.Name.LocalName);
            Assert.Equal("my_asset", doc.Root.Element("name").Value);
            var sdf = doc.Root.Element("sdf");
            Assert.Equal("1.10", sdf.Attribute("version").Value);
            Assert.Equal("model.sdf", sdf.Value);
            Assert.Equal("aryan", doc.Root.Element("author").Element("name").Value);
        }
    }
}
