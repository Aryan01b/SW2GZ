/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Static lint for URDF XML output. Two checks:
  URDF001 — XML is not well-formed (malformed, null, or empty input).
  URDF002 — <geometry> element has no child (no <mesh>, <box>, etc.).
            Bug 9 from v1.0 — Gazebo spawns invisible / collision-less
            links and joints flop.
*/
using System.Collections.Generic;
using System.Xml;

namespace SW2GZ.Validate
{
    public static class UrdfXmlValidator
    {
        public static IReadOnlyList<ValidationIssue> CheckString(string xml)
        {
            var issues = new List<ValidationIssue>();
            if (string.IsNullOrWhiteSpace(xml))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, "URDF001",
                    "URDF XML is null or empty.", "urdf"));
                return issues;
            }

            var doc = new XmlDocument();
            try
            {
                doc.LoadXml(xml);
            }
            catch (XmlException ex)
            {
                issues.Add(new ValidationIssue(IssueSeverity.Error, "URDF001",
                    $"Malformed URDF XML: {ex.Message}", "urdf"));
                return issues;
            }

            var geomNodes = doc.SelectNodes("//geometry");
            if (geomNodes != null)
            {
                foreach (XmlNode geom in geomNodes)
                {
                    bool hasChildElement = false;
                    foreach (XmlNode child in geom.ChildNodes)
                    {
                        if (child.NodeType == XmlNodeType.Element) { hasChildElement = true; break; }
                    }
                    if (!hasChildElement)
                    {
                        issues.Add(new ValidationIssue(IssueSeverity.Error, "URDF002",
                            "Empty <geometry> element — missing <mesh>, <box>, <cylinder>, etc.", "urdf"));
                    }
                }
            }
            return issues;
        }
    }
}
