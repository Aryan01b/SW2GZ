/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.IO;
using System.Xml.Linq;

namespace SW2GZ.Gz
{
    public class ModelConfigWriter
    {
        public class Input
        {
            public string Name { get; set; }
            public string Version { get; set; } = "1.0";
            public string SdfVersion { get; set; } = "1.10";
            public string SdfFile { get; set; } = "model.sdf";
            public string Author { get; set; } = "SW2GZ";
            public string Email { get; set; } = "TODO@example.com";
            public string Description { get; set; } = "Exported by SW2GZ";
        }

        private readonly Input _in;
        public ModelConfigWriter(Input input) { _in = input; }

        public void Write(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("model",
                    new XElement("name", _in.Name),
                    new XElement("version", _in.Version),
                    new XElement("sdf", new XAttribute("version", _in.SdfVersion), _in.SdfFile),
                    new XElement("author",
                        new XElement("name", _in.Author),
                        new XElement("email", _in.Email)),
                    new XElement("description", _in.Description)));
            doc.Save(Path.Combine(outputDir, "model.config"));
        }
    }
}
