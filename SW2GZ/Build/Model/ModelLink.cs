/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: thin wrapper around UrdfLink that pairs the
kinematic/inertial data with an optional named material reference (P5)
and optional per-link Gazebo props (P6). For v2.1 both are null.
*/
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build.Model
{
    public sealed record ModelLink(
        UrdfLink Link,
        string? MaterialName,
        GazeboLinkProps? Gazebo);
}
