/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Phase 4 capstone — runs all checkers over a written package and aggregates
their findings into a ValidationReport. Errors block the export's success
path; Warnings appear in PreExportReport.

Lives in SW2GZ.Validate to avoid collision with the v1 SW2GZ.URDFExport.OutputValidator,
which T29 (Sw2gzPipeline) will eventually displace.
*/
using System;
using System.Collections.Generic;
using System.IO;

namespace SW2GZ.Validate
{
    public sealed class OutputValidator
    {
        public ValidationReport Run(string packagePath, string packageName)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new ArgumentException("packagePath must not be null or whitespace.", nameof(packagePath));

            var issues = new List<ValidationIssue>();
            issues.AddRange(PackageNameChecker.Check(packageName));

            string urdfPath = Path.Combine(packagePath, "urdf", $"{packageName}.urdf.xacro");
            if (File.Exists(urdfPath))
            {
                string xml = File.ReadAllText(urdfPath);
                issues.AddRange(UrdfXmlValidator.CheckString(xml));
                issues.AddRange(PluginNameChecker.Check(xml));
                issues.AddRange(MeshFileChecker.Check(xml, packagePath));
            }

            return new ValidationReport(issues);
        }
    }
}
