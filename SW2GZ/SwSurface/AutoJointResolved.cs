/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure (COM-free) DTO returned by AutoJointResolver.Resolve. Lives in its own
file so the test project (which doesn't define SW_INTEROP and therefore
can't compile the COM-bound AutoJointResolver class itself) can still
source-link this type and assert on defaults / aggregation.
*/
using System.Numerics;
using SW2GZ.Build;

namespace SW2GZ.SwSurface
{
    // Not sealed: SW_INTEROP-gated AutoJointResolver.Resolved is a thin alias
    // that derives from this DTO so tests reference the base type and the SW
    // add-in references AutoJointResolver.Resolved without a switch on the
    // call site.
    public class AutoJointResolved
    {
        public bool Found;
        public string MateName = string.Empty;
        public MateKind Kind = MateKind.Fixed;
        public Vector3 AxisAssembly = Vector3.Zero;   // unit; Zero when no cylinder geometry
        public Vector3? OriginAssembly;               // point on the joint axis, assembly frame
        public double? LimitLower;
        public double? LimitUpper;
    }
}
