/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Locked to Gz Sim Harmonic — SDF version 1.10. Profile parameter retained
for ExportMode dispatch in callers; Gz-version lookup removed (v2.0 lock).
*/
using System.IO;
using System.Xml.Linq;

namespace SW2GZ.Gz
{
    public class SdfModelWriter
    {
        private readonly SdfModelInput _input;

        public SdfModelWriter(SdfModelInput input, object profile = null)
        {
            _input = input;
            // profile parameter kept for call-site compatibility; unused (v2.0 Harmonic lock).
        }

        public void Write(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string sdfVer = "1.10"; // Gz Sim Harmonic (v2.0 lock)

            var model = new XElement("model", new XAttribute("name", _input.Name));
            foreach (var l in _input.Links)
            {
                model.Add(new XElement("link", new XAttribute("name", l.Name)));
            }
            foreach (var j in _input.Joints)
            {
                model.Add(new XElement("joint",
                    new XAttribute("name", j.Name),
                    new XAttribute("type", j.Type),
                    new XElement("parent", j.Parent),
                    new XElement("child", j.Child)));
            }

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("sdf", new XAttribute("version", sdfVer), model));
            doc.Save(Path.Combine(outputDir, "model.sdf"));
        }
    }
}
