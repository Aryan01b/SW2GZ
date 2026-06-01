/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P5 — Materials/appearances boundary. The SW addin will implement this against
the SolidWorks COM API (IModelDoc2 / IPartDoc.MaterialPropertyValues2 etc.)
in a workstation-only file guarded by #if SW_INTEROP. The pure-domain pipeline
talks to this interface only — never to COM types directly — so all the
RobotModel-side material logic stays unit-testable from the writers test
project.
*/
namespace SW2GZ.SwSurface.Abstractions
{
    /// Per-part appearance lookup from the active SolidWorks document.
    /// Maps a part path (or body identifier) to an RGBA color tuple in 0..1.
    public interface IAppearanceSource
    {
        /// Returns the named material for the part at the given path, or null if
        /// the part has no override appearance (use the default material).
        SW2GZ.Build.Model.MaterialDef? GetMaterial(string partPath);
    }
}
