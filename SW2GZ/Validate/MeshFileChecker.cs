/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Static lint that walks every <mesh filename="package://pkg/relpath"/>
element in the URDF and confirms the relpath exists under packageRoot.

MSH001 — referenced mesh missing on disk.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SW2GZ.Validate
{
    public static class MeshFileChecker
    {
        // Captures the relpath inside package://<pkgname>/<relpath>.
        private static readonly Regex MeshHref = new Regex(
            "<mesh\\s+filename=\"package://[^/]+/(?<rel>[^\"]+)\"",
            RegexOptions.Compiled);

        public static IReadOnlyList<ValidationIssue> Check(string urdfXml, string packageRoot)
        {
            if (string.IsNullOrWhiteSpace(packageRoot))
                throw new ArgumentException("packageRoot must not be null or whitespace.", nameof(packageRoot));

            var issues = new List<ValidationIssue>();
            if (string.IsNullOrEmpty(urdfXml)) return issues;

            foreach (Match m in MeshHref.Matches(urdfXml))
            {
                string rel = m.Groups["rel"].Value;
                string abs = Path.Combine(packageRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs))
                    issues.Add(new ValidationIssue(IssueSeverity.Error, "MSH001",
                        $"Referenced mesh missing on disk: {rel}", abs));
            }
            return issues;
        }
    }
}
