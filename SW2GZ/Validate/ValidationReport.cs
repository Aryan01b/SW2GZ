/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Aggregated output of OutputValidator (T24). Holds every ValidationIssue
the checker pipeline produced and exposes severity-filtered views.
*/
using System.Collections.Generic;
using System.Linq;

namespace SW2GZ.Validate
{
    public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Issues)
    {
        public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
        public IEnumerable<ValidationIssue> Errors   => Issues.Where(i => i.Severity == IssueSeverity.Error);
        public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == IssueSeverity.Warning);
    }
}
