/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Static lint check for ROS 2 ament package name conformance.
Bug 2 prevention: catches hyphenated names like "arm-2dof_description"
that would compile but break find_package / find-pkg-share / Python imports.

The ament regex requires:
  - starts with [a-z]
  - zero-or-more [a-z0-9_] in the middle
  - ends with [a-z0-9] (no trailing underscore)
  - total length >= 2 chars
*/
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SW2GZ.Validate
{
    public static class PackageNameChecker
    {
        private static readonly Regex Valid = new Regex("^[a-z][a-z0-9_]*[a-z0-9]$", RegexOptions.Compiled);

        public static IReadOnlyList<ValidationIssue> Check(string packageName)
        {
            if (!string.IsNullOrWhiteSpace(packageName) && Valid.IsMatch(packageName))
                return System.Array.Empty<ValidationIssue>();

            string display = packageName ?? "<null>";
            return new[]
            {
                new ValidationIssue(
                    IssueSeverity.Error,
                    "PKG001",
                    $"Package name '{display}' is not ament-safe (must match ^[a-z][a-z0-9_]*[a-z0-9]$).",
                    "package.xml")
            };
        }
    }
}
