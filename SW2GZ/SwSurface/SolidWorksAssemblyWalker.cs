/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Skeleton — Task 28 wires actual IAssemblyDoc.GetComponents traversal.
*/
using System.Collections.Generic;
using SW2GZ.SwSurface.Abstractions;

namespace SW2GZ.SwSurface
{
    public sealed class SolidWorksAssemblyWalker : IAssemblyWalker
    {
        public IReadOnlyList<LinkSpec> WalkActive()
        {
            throw new System.NotImplementedException(
                "SolidWorksAssemblyWalker.WalkActive() not yet wired to SldWorks API — see Task 28.");
        }
    }
}
