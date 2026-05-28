/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.IO;
using System.Xml.Linq;
using SW2GZ.Ros2;

namespace SW2GZ.Gz
{
    public class SdfModelWriter
    {
        private readonly SdfModelInput _input;
        private readonly TargetProfile _profile;

        public SdfModelWriter(SdfModelInput input, TargetProfile profile)
        {
            _input = input;
            _profile = profile;
        }

        public void Write(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string sdfVer = TargetProfile.SdfVersion[_profile.Gz];

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
