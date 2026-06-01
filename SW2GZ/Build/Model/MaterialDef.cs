/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: named material definition. Range validation (0..1)
belongs in the builder, not on the record itself, so this stays a dumb POCO.
*/
namespace SW2GZ.Build.Model
{
    public sealed record MaterialDef(string Name, double R, double G, double B, double A);
}
