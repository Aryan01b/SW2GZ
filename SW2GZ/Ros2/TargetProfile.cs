/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

v2.0 lock: ROS 2 Jazzy + Gz Sim Harmonic only. Distro/Gz enums removed —
every writer hard-codes the Harmonic strings (see Phase 3 writers). The
TargetProfile carries only the export Mode + (Robot Package vs SDF Model
vs SDF World) selection.
*/
namespace SW2GZ.Ros2
{
    public enum ExportMode { RobotPackage, SdfModel, SdfWorld }

    public sealed record TargetProfile
    {
        public ExportMode Mode { get; init; } = ExportMode.RobotPackage;
    }
}
