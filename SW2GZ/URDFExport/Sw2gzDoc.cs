/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

In-memory configuration tree for a single SolidWorks assembly's SW2GZ export.
Held per-document by Sw2gzDocStore for the lifetime of the SolidWorks session.
Persistence to a SolidWorks Attribute is OUT OF SCOPE for this plan — lands
in the backend wiring plan.

Each top-level mode (Robot/World/Asset) has its own subtree. All three exist
on every Sw2gzDoc — only the one matching `Mode` is exposed in the ribbon.
The other two are inert but kept so mode switches don't lose data.

Pure / COM-free — source-linked into the test project.
*/
using System.Collections.Generic;

namespace SW2GZ.URDFExport
{
    public enum Sw2gzMode { Robot, World, Asset }

    public sealed class Sw2gzDoc
    {
        public Sw2gzMode Mode { get; set; } = Sw2gzMode.Robot;
        public Sw2gzRobotConfig Robot { get; set; } = new Sw2gzRobotConfig();
        public Sw2gzWorldConfig World { get; set; } = new Sw2gzWorldConfig();
        public Sw2gzAssetConfig Asset { get; set; } = new Sw2gzAssetConfig();
    }

    public sealed class Sw2gzRobotConfig
    {
        public List<string> Links    { get; set; } = new List<string>();
        public List<string> Joints   { get; set; } = new List<string>();
        public List<string> Sensors  { get; set; } = new List<string>();
        public bool UseRos2Control   { get; set; } = true;
    }

    public sealed class Sw2gzWorldConfig
    {
        public string Ground            { get; set; } = string.Empty;
        public List<string> Assets      { get; set; } = new List<string>();
        public string PhysicsEngine     { get; set; } = "ode";
        public double MaxStepSize       { get; set; } = 0.001;
        public double RealTimeFactor    { get; set; } = 1.0;
    }

    public sealed class Sw2gzAssetConfig
    {
        public string BodyPart { get; set; } = string.Empty;
        public double FrictionMu { get; set; } = 0.8;
        public bool IsStatic { get; set; } = true;
    }
}
