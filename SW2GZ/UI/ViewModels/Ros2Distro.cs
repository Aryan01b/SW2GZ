/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — wizard-only enum for the Targets step. The v2.0 export core is hard-
locked to Jazzy + Harmonic (see Ros2/TargetProfile.cs), so this enum exists
purely to drive the distro selector + Gz-pairing UI. Only Jazzy is "supported"
in v2.1; the rest render a not-yet-supported note and block Next.
*/
namespace SW2GZ.UI.ViewModels
{
    public enum Ros2Distro { Humble, Jazzy, Kilted, Rolling }
}
