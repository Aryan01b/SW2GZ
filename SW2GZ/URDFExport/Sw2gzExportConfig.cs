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
using System.Runtime.Serialization;
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
    }
}
