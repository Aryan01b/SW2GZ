/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Walks the active SolidWorks assembly's top-level component tree and
returns one LinkSpec per top-level component. Sub-assemblies are
flattened into the FlattenedPartPaths list of their parent.

Active configuration only — multi-config support deferred to v2.1+.
*/
using System.Collections.Generic;
using SW2GZ.Build;

namespace SW2GZ.SwSurface.Abstractions
{
    public interface IAssemblyWalker
    {
        IReadOnlyList<LinkSpec> WalkActive();

        // P2 — surfaces the raw mate specs (link-pair relationships) discovered
        // in the active assembly. Each MateSpec carries ParentLink/ChildLink
        // (sanitized names matching LinkSpec.Name) so the joint-graph pass can
        // resolve connectivity. Empty list = no joints (links export at origin).
        IReadOnlyList<MateSpec> WalkMates();
    }
}
