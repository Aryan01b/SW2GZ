/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Optional side-channel an IAssemblyWalker MAY implement to expose the raw
SW `Component2.Transform2.ArrayData` for a given part. Used exclusively
by the export-time pose dump (PoseDumpWriter) so the column-major vs
row-major interpretation can be verified against live SW values without
risking the pipeline's pose-extraction path. Implementations MUST return
the 16 doubles SW returns (no normalisation, no re-ordering), or null if
the part is unknown.
*/
namespace SW2GZ.SwSurface.Abstractions
{
    public interface IComponentRawTransformSource
    {
        /// Returns the 16-element ArrayData of the part's Component2.Transform2,
        /// or null if the part is unknown or has no transform.
        double[] GetComponentRawTransform(string partPath);
    }
}
