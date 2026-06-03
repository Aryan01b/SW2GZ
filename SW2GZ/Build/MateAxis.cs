/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — pure, COM-free carrier for a joint axis/type read from a SolidWorks mate.
ComponentA/ComponentB are the raw top-level component ids (Component2.Name2),
matched against LinkDef.ComponentIds so the seeder can attach the axis to the
right link-tree edge regardless of name sanitization. Axis is a direction
vector in the assembly frame; the seeder snaps it to the nearest principal
preset. Produced by the COM mate reader (SolidWorksAssemblyWalker.WalkMateAxes).
*/
using System.Numerics;

namespace SW2GZ.Build
{
    public sealed record MateAxis(
        string ComponentA,
        string ComponentB,
        Vector3 Axis,
        MateKind Kind,
        double? LimitLower = null,
        double? LimitUpper = null);
}
