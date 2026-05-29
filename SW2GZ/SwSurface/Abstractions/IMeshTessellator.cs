/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Triangulates a SolidWorks component into a MeshData record. LOD controls
the chord-tolerance / facet-deviation passed to IPartDoc.GetTessellation.

Coarse → low-fidelity, fast (collision-mesh source).
Fine   → high-fidelity (visual-mesh source).

Throws SW2GZ.Exceptions.Sw2gzMeshException if tessellation fails on a
corrupt body.
*/
using SW2GZ.Build;

namespace SW2GZ.SwSurface.Abstractions
{
    public enum TessellationLod { Coarse = 0, Fine = 1 }

    public interface IMeshTessellator
    {
        MeshData Tessellate(string componentPathName, TessellationLod lod);
    }
}
