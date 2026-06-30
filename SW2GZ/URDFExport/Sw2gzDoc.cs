/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

In-memory configuration tree for a single SolidWorks assembly's SW2GZ export.
Held per-document by Sw2gzDocStore for the lifetime of the SolidWorks session.

Each top-level mode (Robot/World/Asset) has its own subtree. All three exist
on every Sw2gzDoc — only the one matching `Mode` is exposed in the ribbon.
The other two are inert but kept so mode switches don't lose data.

Pure / COM-free — source-linked into the test project.

v2.1.0 schema: Robot subtree holds the rich LinkDef/JointDef types ported
from main's Sw2gzExportPmp wizard so the Create-Robot PMP can rebuild the
full link-tree + mate-driven joint flow. Sw2gzDocSerialization persists the
full tree to a "SW2GZ Doc v1" SolidWorks Attribute.
*/
using System.Collections.Generic;
using System.Runtime.Serialization;
using SW2GZ.Build.Model;

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
        public List<LinkDef>  Links    { get; set; } = new List<LinkDef>();
        public List<JointDef> Joints   { get; set; } = new List<JointDef>();
        public List<string>   Sensors  { get; set; } = new List<string>();
        public bool UseRos2Control     { get; set; } = true;
    }

    public sealed class Sw2gzWorldConfig
    {
        public string Ground            { get; set; } = string.Empty;
        public List<string> Assets      { get; set; } = new List<string>();
        public string PhysicsEngine     { get; set; } = "ode";
        public double MaxStepSize       { get; set; } = 0.001;
        public double RealTimeFactor    { get; set; } = 1.0;
        // Scene/environment settings ("World Settings" dialog). Legacy docs saved
        // before this field deserialize it to null (initializers are skipped),
        // so the hook below reseeds a default — callers never see null.
        public Sw2gzWorldSceneConfig Scene { get; set; } = new Sw2gzWorldSceneConfig();
        // World-level sensor/teleop support plugins ("Sensors" panel) — the
        // world enables these so spawned models can use them.
        public Sw2gzWorldSensorsConfig SensorPlugins { get; set; } = new Sw2gzWorldSensorsConfig();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Scene = new Sw2gzWorldSceneConfig();
            SensorPlugins = new Sw2gzWorldSensorsConfig();
        }
    }

    public sealed class Sw2gzAssetConfig
    {
        public string BodyPart   { get; set; } = string.Empty;
        public double FrictionMu { get; set; } = 0.8;
        public bool IsStatic     { get; set; } = true;
        // A1 — 1-DOF joint anchoring the asset to the world (door/lift/wheel).
        public string JointType  { get; set; } = "none";   // none|fixed|revolute|continuous|prismatic
        public double JointAxisX { get; set; } = 0.0;
        public double JointAxisY { get; set; } = 0.0;
        public double JointAxisZ { get; set; } = 1.0;
        public double JointLower { get; set; } = -1.5708;
        public double JointUpper { get; set; } = 1.5708;
        // A2 — optional sensor on the asset link.
        public string SensorKind  { get; set; } = "none";  // none|camera|gpu_lidar|imu
        public string SensorTopic { get; set; } = "/asset/sensor";
        // A3 — collision geometry.
        public string Collision   { get; set; } = "mesh";  // mesh|box|sphere|cylinder

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // Legacy docs saved before these fields existed deserialize them to
            // null/0 (initializers are skipped); reseed safe defaults.
            JointType = "none";
            JointAxisX = 0.0; JointAxisY = 0.0; JointAxisZ = 1.0;
            JointLower = -1.5708; JointUpper = 1.5708;
            SensorKind = "none"; SensorTopic = "/asset/sensor";
            Collision = "mesh";
        }
    }
}
