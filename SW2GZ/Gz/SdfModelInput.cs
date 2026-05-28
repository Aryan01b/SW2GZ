/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

POCO inputs for SDF emit. ExportHelper (Phase 4) converts SW2GZ.URDF.Robot
trees into these records so SdfModelWriter stays free of SolidWorks COM
dependencies and can be unit-tested with `dotnet test` outside a
SolidWorks workstation.
*/
using System.Collections.Generic;

namespace SW2GZ.Gz
{
    public class SdfModelInput
    {
        public string Name { get; set; }
        public IReadOnlyList<SdfLinkData> Links { get; set; } = new List<SdfLinkData>();
        public IReadOnlyList<SdfJointData> Joints { get; set; } = new List<SdfJointData>();
    }

    public class SdfLinkData
    {
        public string Name { get; set; }
    }

    public class SdfJointData
    {
        public string Name { get; set; }
        public string Type { get; set; } = "fixed";
        public string Parent { get; set; }
        public string Child { get; set; }
    }
}
