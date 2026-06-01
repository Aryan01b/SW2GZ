/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: minimal control placeholder. Real schema
(per-joint controller selection, limits, gains, hardware interface) lands
in P2. For v2.1, defaults are: every joint listed, single
joint_state_broadcaster default controller.
*/
using System.Collections.Generic;

namespace SW2GZ.Build.Model
{
    public sealed record ControlSpec(
        IReadOnlyList<string> JointNames,
        string DefaultController);
}
