/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: top-level metadata block. PackageName is
expected to already be sanitized by the time it lands here (RobotModelBuilder
sanitizes via PackageNameSanitizer).
*/
namespace SW2GZ.Build.Model
{
    public sealed record RobotMeta(
        string PackageName,
        string Author,
        string Email,
        string License,
        CoordinateConvention Frame);
}
