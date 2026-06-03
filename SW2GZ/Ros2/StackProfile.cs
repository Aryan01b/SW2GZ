/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

StackProfile — the per-assembly, à-la-carte selection of which ROS 2 / Gazebo
stacks an export emits. Replaces the old coarse `modelOnly` boolean in the
export pipeline and is persisted inside Sw2gzExportConfig (assembly attribute),
so a robot's stack choices travel with the model.

Validated model (see docs/superpowers/specs/2026-06-03-modular-stack-ribbon-design.md):
actuation is a SINGLE-CHOICE backend, because Gz native plugins and
gz_ros2_control both drive the same joint and would fight. Hence one
ActuationBackend enum rather than two independent toggles — mutual exclusion is
structural, not a runtime check.

D1 scope: only `Actuation` drives pipeline branching today (it reproduces the
exact full-stack vs model-only output). `GzSim` and `SensorsEnabled` are part of
the persisted schema now (added once to avoid a later serialization migration)
but are wired into behaviour in later phases (D3 world options, D4 sensors).
Detail-parameter records (controller lists, gz plugin params, bridge topic
granularity) are deferred to D3.
*/
using System.Runtime.Serialization;

namespace SW2GZ.Ros2
{
    // Actuation backend for the exported robot. Exactly one applies per robot:
    //   None        — no actuation files (kinematic/visual model only).
    //   GzPlugin    — Gz native system plugins (DiffDrive/JointController). [writer lands D3]
    //   Ros2Control — gz_ros2_control + controller_manager + controllers.yaml.
    public enum ActuationBackend { None, GzPlugin, Ros2Control }

    [DataContract(Name = "StackProfile", Namespace = "")]
    public sealed class StackProfile
    {
        // Master "build for Gz simulation" switch (world + gz system + plugin
        // scaffold). Reserved in D1 (always-on behaviour unchanged); wired to the
        // Configure PMP world options in D3.
        [DataMember] public bool GzSim { get; set; } = true;

        // The single actuation backend. Default Ros2Control reproduces the
        // pre-refactor full-stack output.
        [DataMember] public ActuationBackend Actuation { get; set; } = ActuationBackend.Ros2Control;

        // Whether the export emits Gz sensor blocks + sensor bridge entries.
        // Reserved in D1; populated + wired in D4 (SW COM sensor extraction).
        [DataMember] public bool SensorsEnabled { get; set; } = false;

        // Full ROS 2 + Gz + ros2_control stack. Equivalent to the old
        // `modelOnly: false` path.
        public static StackProfile Default() => new StackProfile();

        // Bare kinematic/visual model — no actuation, no control, no bridge.
        // Equivalent to the old `modelOnly: true` path.
        public static StackProfile ModelOnly() =>
            new StackProfile { GzSim = true, Actuation = ActuationBackend.None, SensorsEnabled = false };
    }
}
