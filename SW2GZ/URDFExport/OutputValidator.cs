/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace SW2GZ.URDFExport
{
    public class OutputValidator
    {
        public class Result
        {
            public bool Ok { get { return Errors.Count == 0; } }
            public List<string> Errors { get; } = new List<string>();
        }

        public static Result ValidateXmlWellFormedness(string filePath)
        {
            var result = new Result();
            try
            {
                using (var r = XmlReader.Create(filePath))
                {
                    while (r.Read()) { /* read to end */ }
                }
            }
            catch (XmlException ex)
            {
                result.Errors.Add(filePath + ": " + ex.Message);
            }
            catch (IOException ex)
            {
                result.Errors.Add(filePath + ": " + ex.Message);
            }
            return result;
        }

        public static Result ValidateDirectoryXml(string dir)
        {
            var result = new Result();
            foreach (var pattern in new[] { "*.xml", "*.urdf", "*.sdf", "*.world" })
            {
                foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories))
                {
                    result.Errors.AddRange(ValidateXmlWellFormedness(f).Errors);
                }
            }
            return result;
        }
    }
}
