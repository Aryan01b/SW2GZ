/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — persisted wizard state ("checkpoint") for the native SW2GZ export
PropertyManagerPage. The wizard reads this on open and writes it on each Next,
serialized via DataContract to a SolidWorks Attribute feature in the assembly
document tree (see Sw2gzConfigSerialization). Reopening the assembly and
clicking the SW2GZ button resumes from here.

Pure / COM-free so it round-trips in the net8 test project. Fields grow as the
later wizard steps (Geometry/Joints/Review) are implemented.
*/
using System.Collections.Generic;
using System.Runtime.Serialization;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;

namespace SW2GZ.URDFExport
{
    [DataContract(Name = "Sw2gzExportConfig", Namespace = "")]
    public sealed class Sw2gzExportConfig
    {
        // Step 1 — what to generate. Drives the output file/folder layout.
        [DataMember] public ExportMode Mode { get; set; } = ExportMode.RobotPackage;

        // Step 2 — output destination, package identity, and package metadata.
        [DataMember] public string OutputFolder { get; set; } = string.Empty;
        [DataMember] public string PackageName { get; set; } = string.Empty;
        [DataMember] public string Author { get; set; } = string.Empty;
        [DataMember] public string Email { get; set; } = string.Empty;
        [DataMember] public string License { get; set; } = string.Empty;

        // Resume position — 0-based wizard step index reached at last save.
        [DataMember] public int LastStep { get; set; }

        // Step 3 — link definitions (name + assigned component ids + base flag).
        [DataMember] public List<LinkDef> Links { get; set; } = new List<LinkDef>();

        // Step 4 — joint definitions, one per non-root link edge. Seeded from the
        // link tree (JointSeeder) and editable in the Joints step.
        [DataMember] public List<JointDef> Joints { get; set; } = new List<JointDef>();

        // Stacks — à-la-carte ROS 2 / Gazebo stack selection for this assembly.
        // Defaults to the full stack (Default()) so a config saved before this
        // field existed deserializes to the same full-stack export as before.
        [DataMember] public StackProfile Stacks { get; set; } = StackProfile.Default();
    }
}
