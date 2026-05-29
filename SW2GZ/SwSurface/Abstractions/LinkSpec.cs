/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

One link to be exported. Name comes from the SW component's top-level
Name2 (sanitized). FlattenedPartPaths is every leaf SLDPRT path the
link aggregates — sub-assemblies have been collapsed into one rigid
body (per spec §4.1, T29 will combine their MassProps via parallel-axis).
*/
using System.Collections.Generic;

namespace SW2GZ.SwSurface.Abstractions
{
    public sealed record LinkSpec(string Name, IReadOnlyList<string> FlattenedPartPaths);
}
