/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Single static-lint finding emitted by the Phase 4 Validate layer. Errors
block export completion; Warnings surface in the PreExportReport.
*/
namespace SW2GZ.Validate
{
    public enum IssueSeverity { Error, Warning }

    public sealed record ValidationIssue(
        IssueSeverity Severity,
        string Code,
        string Message,
        string Location);
}
