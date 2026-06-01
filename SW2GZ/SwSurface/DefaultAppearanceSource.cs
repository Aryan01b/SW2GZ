/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P5 — Materials/appearances boundary: pure-domain fallback. Returns null for
every part so the link emits no <material> tag. The real SW COM implementation
will live in SolidWorksAppearanceSource.cs guarded by #if SW_INTEROP and is
deferred to a SolidWorks-workstation session.
*/
namespace SW2GZ.SwSurface
{
    using SW2GZ.Build.Model;
    using SW2GZ.SwSurface.Abstractions;

    /// Stub fallback that returns null for every part (no appearance override).
    /// Real SW COM implementation will live in SolidWorksAppearanceSource.cs (#if SW_INTEROP).
    public sealed class DefaultAppearanceSource : IAppearanceSource
    {
        public MaterialDef? GetMaterial(string partPath) => null;
    }
}
