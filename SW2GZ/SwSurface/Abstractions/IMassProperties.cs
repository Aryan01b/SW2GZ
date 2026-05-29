/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Reads mass / center-of-mass / inertia tensor for a SolidWorks component
identified by its full path name (Component2.GetPathName()).

Throws SW2GZ.Exceptions.MaterialMissingException if the component's
SW material is not set (mass = 0).

Implementations are expected to cache by path so the SW dependency
graph is rebuilt at most once per export.
*/
using SW2GZ.Build;

namespace SW2GZ.SwSurface.Abstractions
{
    public interface IMassProperties
    {
        MassProps Get(string componentPathName);
    }
}
