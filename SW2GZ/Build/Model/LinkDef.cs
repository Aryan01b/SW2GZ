/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — one robot link as defined in wizard Step 3: a name, the SolidWorks
component ids (Component2.Name2) assigned to it, and whether it is the base/
root link. Pure / COM-free and DataContract-serialized inside Sw2gzExportConfig
so Step 3 resumes from the document checkpoint.
*/
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SW2GZ.Build.Model
{
    [DataContract(Name = "LinkDef", Namespace = "")]
    public sealed class LinkDef
    {
        [DataMember] public string Name { get; set; } = string.Empty;
        [DataMember] public List<string> ComponentIds { get; set; } = new List<string>();
        [DataMember] public bool IsBase { get; set; }
    }
}
