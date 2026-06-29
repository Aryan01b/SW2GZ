/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

World-mode scene/environment settings — the knobs exposed by the "World
Settings" ribbon dialog. Pure value record (no COM), consumed by
SdfWorldWriter.WriteScene to emit lighting / sky / fog / grid / gravity / wind /
spherical-coordinates. Null Settings on WriteScene keeps the original
hard-coded world output (used by the existing tests and any legacy caller).

Defaults are chosen to reproduce the previous fixed world: sun azimuth/elevation
match the old `<direction>-0.5 0.1 -0.9</direction>`, gravity is Earth, sky/fog
off, grid on, background the old pale blue.
*/
namespace SW2GZ.Gz
{
    public sealed record SdfSceneSettings(
        // View
        bool ShowGrid = true,
        // Lighting (sun as azimuth/elevation so the UI is intuitive)
        double SunAzimuthDeg = 169.0,
        double SunElevationDeg = 60.0,
        double SunIntensity = 1.0,
        bool CastShadows = true,
        // Sky & fog
        bool Sky = false,
        bool Fog = false,
        double FogDensity = 0.02,
        double BgR = 0.8, double BgG = 0.85, double BgB = 0.9,
        // Environment
        double GravityZ = -9.8,
        double WindX = 0.0, double WindY = 0.0, double WindZ = 0.0,
        // Geo (spherical coordinates origin)
        bool UseGeo = false,
        double Latitude = 0.0, double Longitude = 0.0,
        double Elevation = 0.0, double HeadingDeg = 0.0);
}
