/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: minimal per-link Gazebo property placeholder.
Real fields (kp/kd, mu1/mu2, fdir1, max_contacts, self_collide policy)
land in P6 alongside sensor/world data.
*/
namespace SW2GZ.Build.Model
{
    public sealed record GazeboLinkProps(double? Mu, double? Mu2, bool SelfCollide);
}
