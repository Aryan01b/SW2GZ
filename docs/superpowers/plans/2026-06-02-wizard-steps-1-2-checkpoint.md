# Wizard Steps 1–2 + Checkpoint Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Step 1 (Mode) and Step 2 (Output) of the native SW2GZ export PropertyManagerPage real, and auto-save the wizard's state into the SolidWorks document tree on each Next so reopening the assembly and clicking the SW2GZ button resumes the configuration.

**Architecture:** A pure, COM-free `Sw2gzExportConfig` snapshot holds the wizard state. A pure `Sw2gzConfigCodec` round-trips it to/from an XML string via `DataContractSerializer`. A COM-bound `Sw2gzConfigSerialization` stores that string in a **new** SolidWorks `Attribute` feature (`"SW2GZ Export Configuration (v1)"`) in the assembly tree — the same "instance in tree" mechanism the legacy `ConfigurationSerialization` uses for the URDF link tree. `Sw2gzExportPmp` builds real controls for steps 1–2, loads the config on open (seeding controls + resuming at `LastStep`), and saves on each Next/Finish. The legacy URDF attribute is left untouched. The unused MVVM layer is NOT used (decision: native PMP is the sole UI).

**Tech Stack:** C# / .NET Framework 4.8.1 add-in (SolidWorks COM interop, `#if SW_INTEROP`); `System.Runtime.Serialization.DataContractSerializer`; xUnit test project on net8 (`dotnet test`), pure sources pulled in via `<Compile Include … Link=…/>`.

**Reference docs:** spec at `docs/superpowers/specs/2026-06-02-wizard-steps-1-2-checkpoint-design.md`; existing patterns in `SW2GZ/URDFExport/ConfigurationSerialization.cs` (SW Attribute plumbing) and `SW2GZ/URDFExport/Sw2gzExportPmp.cs` (PMP shell).

---

## File Structure

| File | Responsibility | Build target |
|---|---|---|
| `SW2GZ/URDFExport/Sw2gzExportConfig.cs` *(new)* | Pure POCO snapshot of wizard state (`[DataContract]`) | addin + test |
| `SW2GZ/URDFExport/Sw2gzConfigCodec.cs` *(new)* | Pure serialize/deserialize of `Sw2gzExportConfig` ↔ XML string | addin + test |
| `SW2GZ/URDFExport/Sw2gzConfigSerialization.cs` *(new)* | COM: store/load the XML string as a SW Attribute feature in the doc tree | addin only (`#if SW_INTEROP`) |
| `Test/URDFExport/Sw2gzExportConfigTests.cs` *(new)* | Round-trip unit tests for the codec | test only |
| `Test/SW2GZ.Writers.Test.csproj` *(modify)* | Add `<Compile Include>` for the two pure sources | test only |
| `SW2GZ/URDFExport/Sw2gzExportPmp.cs` *(modify)* | Real Step 1/2 controls, ctor takes `ModelDoc2`, load/seed/resume, save-on-Next | addin only |
| `SW2GZ/SW/SwAddin.cs` *(modify)* | `LaunchWizard` passes the active assembly `ModelDoc2` to the PMP | addin only |

---

## Task 1: Pure config model + codec (TDD)

**Files:**
- Create: `SW2GZ/URDFExport/Sw2gzExportConfig.cs`
- Create: `SW2GZ/URDFExport/Sw2gzConfigCodec.cs`
- Create: `Test/URDFExport/Sw2gzExportConfigTests.cs`
- Modify: `Test/SW2GZ.Writers.Test.csproj`

- [ ] **Step 1: Add the two pure sources to the test project**

In `Test/SW2GZ.Writers.Test.csproj`, immediately after the existing line
`<Compile Include="..\SW2GZ\Ros2\TargetProfile.cs" Link="Sources\Ros2\TargetProfile.cs" />`
(the file that already brings `ExportMode` into the test build), add:

```xml
    <Compile Include="..\SW2GZ\URDFExport\Sw2gzExportConfig.cs" Link="Sources\URDFExport\Sw2gzExportConfig.cs" />
    <Compile Include="..\SW2GZ\URDFExport\Sw2gzConfigCodec.cs"  Link="Sources\URDFExport\Sw2gzConfigCodec.cs" />
```

- [ ] **Step 2: Write the failing test**

Create `Test/URDFExport/Sw2gzExportConfigTests.cs`:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure round-trip tests for the wizard checkpoint config + codec. No COM, so
these run in the net8 test project (the SW Attribute storage layer that wraps
this codec is COM-bound and lives in Sw2gzConfigSerialization, untested here).
*/
using SW2GZ.Ros2;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Test.URDFExport
{
    public class Sw2gzExportConfigTests
    {
        [Fact]
        public void RoundTrip_PreservesAllFields()
        {
            var config = new Sw2gzExportConfig
            {
                Mode = ExportMode.SdfWorld,
                OutputFolder = @"C:\out\robots",
                PackageName = "My Robot Pkg",
                Author = "Aryan Arlikar",
                Email = "aryan@example.com",
                License = "MIT",
                LastStep = 2,
            };

            string xml = Sw2gzConfigCodec.ToXmlString(config);
            Sw2gzExportConfig restored = Sw2gzConfigCodec.FromXmlString(xml);

            Assert.Equal(ExportMode.SdfWorld, restored.Mode);
            Assert.Equal(@"C:\out\robots", restored.OutputFolder);
            Assert.Equal("My Robot Pkg", restored.PackageName);
            Assert.Equal("Aryan Arlikar", restored.Author);
            Assert.Equal("aryan@example.com", restored.Email);
            Assert.Equal("MIT", restored.License);
            Assert.Equal(2, restored.LastStep);
        }

        [Fact]
        public void ToXmlString_ProducesNonEmptyXml()
        {
            string xml = Sw2gzConfigCodec.ToXmlString(new Sw2gzExportConfig());
            Assert.False(string.IsNullOrWhiteSpace(xml));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void FromXmlString_ReturnsNull_OnEmptyInput(string data)
        {
            Assert.Null(Sw2gzConfigCodec.FromXmlString(data));
        }

        [Fact]
        public void Defaults_AreRobotPackageAndEmptyStrings()
        {
            var config = new Sw2gzExportConfig();
            Assert.Equal(ExportMode.RobotPackage, config.Mode);
            Assert.Equal(string.Empty, config.OutputFolder);
            Assert.Equal(string.Empty, config.PackageName);
            Assert.Equal(0, config.LastStep);
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails (does not compile yet)**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter FullyQualifiedName~Sw2gzExportConfigTests`
Expected: FAIL — build error, `Sw2gzExportConfig` / `Sw2gzConfigCodec` do not exist.

- [ ] **Step 4: Create the config model**

Create `SW2GZ/URDFExport/Sw2gzExportConfig.cs`:

```csharp
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
```

- [ ] **Step 5: Create the codec**

Create `SW2GZ/URDFExport/Sw2gzConfigCodec.cs`:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — pure (COM-free) serialization of Sw2gzExportConfig to/from an XML string
using DataContractSerializer, mirroring the scheme in ConfigurationSerialization
but for the wizard checkpoint rather than the URDF link tree. The COM layer that
stores this string in a SolidWorks Attribute lives in Sw2gzConfigSerialization.

UTF-8 (not ASCII) so non-ASCII author names / metadata round-trip intact.
*/
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzConfigCodec
    {
        private static readonly DataContractSerializer Serializer =
            new DataContractSerializer(typeof(Sw2gzExportConfig));

        /// Serialize a config to a UTF-8 XML string. Never returns null.
        public static string ToXmlString(Sw2gzExportConfig config)
        {
            using (var stream = new MemoryStream())
            {
                Serializer.WriteObject(stream, config);
                stream.Flush();
                return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Position);
            }
        }

        /// Deserialize a config from an XML string. Returns null for empty/blank
        /// input (e.g. when no checkpoint has been saved yet).
        public static Sw2gzExportConfig FromXmlString(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
            {
                return (Sw2gzExportConfig)Serializer.ReadObject(stream);
            }
        }
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter FullyQualifiedName~Sw2gzExportConfigTests`
Expected: PASS — 6 tests (1 + 1 + 3 theory cases + 1) green.

- [ ] **Step 7: Run the full test suite (no regressions)**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: PASS — all previously-green tests still pass, plus the new ones.

- [ ] **Step 8: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzExportConfig.cs SW2GZ/URDFExport/Sw2gzConfigCodec.cs Test/URDFExport/Sw2gzExportConfigTests.cs Test/SW2GZ.Writers.Test.csproj
git commit -m "feat(addin): Sw2gzExportConfig checkpoint model + codec (round-trip tested)"
```

---

## Task 2: COM serializer — store config as a SW Attribute in the tree

**Files:**
- Create: `SW2GZ/URDFExport/Sw2gzConfigSerialization.cs`

No unit test: this layer is COM-bound (`ModelDoc2`, `Attribute`) and only compiles/runs inside the SolidWorks add-in build (`#if SW_INTEROP`). It is verified by Task 6's build + manual run on a SolidWorks workstation. The pure serialization it delegates to (`Sw2gzConfigCodec`) is already tested in Task 1.

- [ ] **Step 1: Create the serializer**

Create `SW2GZ/URDFExport/Sw2gzConfigSerialization.cs`. This mirrors the attribute
plumbing in `ConfigurationSerialization.cs` (DefineAttribute / AddParameter / Register /
CreateInstance5 / GetParameter / SetStringValue2 / SetDoubleValue2), but for the wizard
checkpoint and a new attribute name. The whole file is guarded by `#if SW_INTEROP` so the
add-in compiles outside a SolidWorks workstation (consistent with the rest of `URDFExport`).

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — stores/loads the wizard checkpoint (Sw2gzExportConfig) as a SolidWorks
Attribute feature in the top-level assembly document tree. This is the "instance
in tree": a named Attribute carrying the DataContract-serialized config string,
so reopening the assembly and clicking the SW2GZ button resumes the wizard.

Mirrors the attribute plumbing of ConfigurationSerialization (the legacy URDF
link-tree store) but writes a NEW, dedicated attribute. The legacy
"URDF Export Configuration (v1.4)" attribute is intentionally left untouched
here — not read, not deleted (full migration is a later increment).
*/
using System;

#if SW_INTEROP
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2GZ.Utilities;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzConfigSerialization
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        /// Serialization version stored alongside the data for forward-compat.
        private const double SerializationVersion = 1.0;

        /// Attribute feature name shown in the document tree.
        public const string Sw2gzConfigAttributeName = "SW2GZ Export Configuration (v1)";

        /// Save the wizard checkpoint into the model's Attribute (create or update).
        public static void Save(SldWorks swApp, ModelDoc2 model, Sw2gzExportConfig config)
        {
            if (swApp == null) throw new ArgumentNullException(nameof(swApp));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (config == null) throw new ArgumentNullException(nameof(config));

            string data = Sw2gzConfigCodec.ToXmlString(config);
            SaveDataToModelDoc(swApp, model, data);
            logger.Info("Saved SW2GZ wizard checkpoint to the assembly document.");
        }

        /// Load the wizard checkpoint from the model. Returns a fresh default
        /// config (Mode=RobotPackage, blank fields, LastStep=0) when none exists.
        public static Sw2gzExportConfig Load(ModelDoc2 model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            string data = GetConfigData(model);
            Sw2gzExportConfig config = Sw2gzConfigCodec.FromXmlString(data);
            return config ?? new Sw2gzExportConfig();
        }

        // ───────────────────────────── private ───────────────────────────────

        private static string GetConfigData(ModelDoc2 model)
        {
            SolidWorks.Interop.sldworks.Attribute swAtt =
                FindSWSaveAttribute(model, Sw2gzConfigAttributeName);
            if (swAtt == null)
            {
                return "";
            }

            Parameter param = swAtt.GetParameter("data");
            return param.GetStringValue();
        }

        private static Feature GetFeatureAttributeByName(ModelDoc2 model, string featName)
        {
            object[] objects = (object[])model.FeatureManager.GetFeatures(true);
            if (objects == null)
            {
                return null;
            }
            foreach (object obj in objects)
            {
                Feature feature = (Feature)obj;
                if (feature.GetTypeName2() == "Attribute")
                {
                    SolidWorks.Interop.sldworks.Attribute att =
                        (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
                    if (att.GetName() == featName)
                    {
                        return feature;
                    }
                }
            }
            return null;
        }

        private static SolidWorks.Interop.sldworks.Attribute
            FindSWSaveAttribute(ModelDoc2 model, string name)
        {
            Feature feature = GetFeatureAttributeByName(model, name);
            if (feature == null)
            {
                return null;
            }
            return (SolidWorks.Interop.sldworks.Attribute)feature.GetSpecificFeature2();
        }

        private static SolidWorks.Interop.sldworks.Attribute
            CreateSWSaveAttribute(SldWorks swApp, ModelDoc2 model)
        {
            SolidWorks.Interop.sldworks.Attribute existing =
                FindSWSaveAttribute(model, Sw2gzConfigAttributeName);
            if (existing != null)
            {
                return existing;
            }

            int options = 0;
            int configurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;

            AttributeDef def = swApp.DefineAttribute(Sw2gzConfigAttributeName);
            def.AddParameter("data", (int)swParamType_e.swParamTypeString, 0, options);
            def.AddParameter("date", (int)swParamType_e.swParamTypeString, 0, options);
            def.AddParameter("version", (int)swParamType_e.swParamTypeDouble,
                SerializationVersion, options);
            def.Register();

            return def.CreateInstance5(
                model, null, Sw2gzConfigAttributeName, options, configurationOptions);
        }

        private static void SaveDataToModelDoc(SldWorks swApp, ModelDoc2 model, string data)
        {
            int configurationOptions = (int)swInConfigurationOpts_e.swAllConfiguration;
            SolidWorks.Interop.sldworks.Attribute att =
                CreateSWSaveAttribute(swApp, model);

            Parameter param = att.GetParameter("data");
            param.SetStringValue2(data, configurationOptions, "");
            param = att.GetParameter("date");
            param.SetStringValue2(DateTime.Now.ToString(), configurationOptions, "");
            param = att.GetParameter("version");
            param.SetDoubleValue2(SerializationVersion, configurationOptions, "");
        }
    }
}
#endif
```

- [ ] **Step 2: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzConfigSerialization.cs
git commit -m "feat(addin): store wizard checkpoint as SW Attribute in the doc tree"
```

---

## Task 3: PMP — Step 1 (Mode) real controls + ModelDoc ctor

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzExportPmp.cs`

This task widens the per-step control-ID stride, refactors `BuildPage` to dispatch
per-step content builders, adds the three Mode radio buttons, and threads a `ModelDoc2`
into the ctor + a live `Sw2gzExportConfig` field (used by Tasks 4–5). Step 2's builder is
added in Task 4; here it falls through to the generic placeholder.

- [ ] **Step 1: Add `using` + fields for the model, config, and Mode controls**

In the `#if SW_INTEROP` using block (after `using System.Windows.Forms;`), the
SolidWorks usings are already present. No new namespace needed (`Sw2gzExportConfig`,
`ExportMode`, `Sw2gzConfigSerialization` are in `SW2GZ.URDFExport` / `SW2GZ.Ros2`; add
`using SW2GZ.Ros2;`).

Add `using SW2GZ.Ros2;` to the `#if SW_INTEROP` using list.

Replace the field block (currently `swApp`, `PMPage`, group/label/button fields) — add
the model + config + Mode option fields. After the line
`private readonly SldWorks swApp;` add:

```csharp
        // The active assembly document (target of the checkpoint save/load).
        private readonly ModelDoc2 model;

        // Live wizard state — loaded on open, saved on each Next.
        private Sw2gzExportConfig config = new Sw2gzExportConfig();
```

After `private PropertyManagerPageButton PMButtonNext;` add:

```csharp
        // Step 1 (Mode) radio buttons, indexed by ExportMode order.
        private PropertyManagerPageOption PMOptRobotPackage;
        private PropertyManagerPageOption PMOptSdfModel;
        private PropertyManagerPageOption PMOptSdfWorld;
```

- [ ] **Step 2: Widen the per-step ID stride from 10 to 20**

Replace the three step-ID helpers and base constant:

```csharp
        // Step controls start well above the fixed IDs (20 IDs of headroom per step).
        private const int StepIdBase = 100;

        private int StepGroupId(int step) => StepIdBase + step * 20;
        private int StepHeadingId(int step) => StepIdBase + step * 20 + 1;
        private int StepDescId(int step) => StepIdBase + step * 20 + 2;

        // Step 1 (Mode) option IDs.
        private const int OptRobotPackageID = StepIdBase + 0 * 20 + 3;
        private const int OptSdfModelID     = StepIdBase + 0 * 20 + 4;
        private const int OptSdfWorldID     = StepIdBase + 0 * 20 + 5;
```

- [ ] **Step 3: Add `ModelDoc2` to the constructor**

Change the constructor signature and store the model. Replace:

```csharp
        public Sw2gzExportPmp(SldWorks swApp)
        {
            this.swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            this.currentStep = 0;
```

with:

```csharp
        public Sw2gzExportPmp(SldWorks swApp, ModelDoc2 model)
        {
            this.swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.currentStep = 0;
```

- [ ] **Step 4: Refactor `BuildPage`'s per-step loop to dispatch content builders**

In `BuildPage`, replace the body of the `for (int step = 0; …)` loop (the block that
adds `StepHeadingId` + `StepDescId` labels) with a heading label followed by a dispatch:

```csharp
            PMStepGroups = new PropertyManagerPageGroup[StepCount];
            for (int step = 0; step < StepCount; step++)
            {
                PropertyManagerPageGroup stepGroup =
                    (PropertyManagerPageGroup)PMPage.AddGroupBox(
                        StepGroupId(step), StepNames[step], grpOptions);
                PMStepGroups[step] = stepGroup;

                stepGroup.AddControl2(
                    StepHeadingId(step),
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    StepNames[step], (short)leftEdge, visibleEnabled, "");

                switch (step)
                {
                    case 0:
                        BuildModeStep(stepGroup, indent, visibleEnabled);
                        break;
                    default:
                        // Generic placeholder for steps not yet implemented.
                        stepGroup.AddControl2(
                            StepDescId(step),
                            (short)swPropertyManagerPageControlType_e.swControlType_Label,
                            StepDescriptions[step], (short)indent, visibleEnabled, "");
                        break;
                }
            }
```

- [ ] **Step 5: Add the `BuildModeStep` helper**

Add this method just below `BuildPage` (before `ShowStep`):

```csharp
        // Step 1 — three mutually-exclusive radio buttons selecting the export
        // mode. SolidWorks treats a contiguous run of option controls in one
        // group as mutually exclusive; OnOptionCheck mirrors the pick into config.
        private void BuildModeStep(PropertyManagerPageGroup group, int indent, int visibleEnabled)
        {
            PMOptRobotPackage = (PropertyManagerPageOption)group.AddControl2(
                OptRobotPackageID,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Robot package (URDF/Xacro)", (short)indent, visibleEnabled,
                "Generate a ROS 2 robot package with URDF/Xacro");

            PMOptSdfModel = (PropertyManagerPageOption)group.AddControl2(
                OptSdfModelID,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Gz asset (SDF model)", (short)indent, visibleEnabled,
                "Generate a standalone Gazebo SDF model");

            PMOptSdfWorld = (PropertyManagerPageOption)group.AddControl2(
                OptSdfWorldID,
                (short)swPropertyManagerPageControlType_e.swControlType_Option,
                "Gz world (SDF world)", (short)indent, visibleEnabled,
                "Generate a Gazebo SDF world containing the model");
        }

        // Reflects config.Mode onto the radio buttons' Checked state.
        private void SeedModeControls()
        {
            if (PMOptRobotPackage == null) return;
            PMOptRobotPackage.Checked = config.Mode == ExportMode.RobotPackage;
            PMOptSdfModel.Checked = config.Mode == ExportMode.SdfModel;
            PMOptSdfWorld.Checked = config.Mode == ExportMode.SdfWorld;
        }
```

- [ ] **Step 6: Handle option clicks in `OnOptionCheck`**

Replace the no-op `OnOptionCheck` member:

```csharp
        void IPropertyManagerPage2Handler9.OnOptionCheck(int Id) { }
```

with:

```csharp
        void IPropertyManagerPage2Handler9.OnOptionCheck(int Id)
        {
            switch (Id)
            {
                case OptRobotPackageID: config.Mode = ExportMode.RobotPackage; break;
                case OptSdfModelID:     config.Mode = ExportMode.SdfModel; break;
                case OptSdfWorldID:     config.Mode = ExportMode.SdfWorld; break;
                default: break;
            }
        }
```

- [ ] **Step 7: Seed the Mode controls after building the page**

In `BuildPage`, the last line is `ShowStep(0);`. Immediately before it, add:

```csharp
            SeedModeControls();
```

- [ ] **Step 8: Build the add-in (SolidWorks workstation) / compile-review**

Run (on a SolidWorks workstation with Visual Studio / MSBuild):
`msbuild SW2GZ.sln /t:Build /p:Configuration=Debug`
Expected: SW2GZ add-in project compiles. (Off-workstation this build is skipped — the
interop references are unavailable; rely on review + the net8 test build of Task 1.)

- [ ] **Step 9: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzExportPmp.cs
git commit -m "feat(addin): Step 1 Mode radio buttons + ModelDoc ctor in export PMP"
```

---

## Task 4: PMP — Step 2 (Output) real controls + handlers

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzExportPmp.cs`

- [ ] **Step 1: Add Step 2 control fields**

After the Mode option fields added in Task 3, add:

```csharp
        // Step 2 (Output) controls.
        private PropertyManagerPageTextbox PMTextOutputFolder;
        private PropertyManagerPageButton PMButtonBrowse;
        private PropertyManagerPageTextbox PMTextPackageName;
        private PropertyManagerPageTextbox PMTextAuthor;
        private PropertyManagerPageTextbox PMTextEmail;
        private PropertyManagerPageTextbox PMTextLicense;
```

- [ ] **Step 2: Add Step 2 control IDs**

Below the Mode option IDs added in Task 3, add (step index 1 → base 120):

```csharp
        // Step 2 (Output) control IDs.
        private const int LabelOutputFolderID = StepIdBase + 1 * 20 + 2;
        private const int TextOutputFolderID  = StepIdBase + 1 * 20 + 3;
        private const int ButtonBrowseID      = StepIdBase + 1 * 20 + 4;
        private const int LabelPackageNameID  = StepIdBase + 1 * 20 + 5;
        private const int TextPackageNameID   = StepIdBase + 1 * 20 + 6;
        private const int LabelAuthorID       = StepIdBase + 1 * 20 + 7;
        private const int TextAuthorID        = StepIdBase + 1 * 20 + 8;
        private const int LabelEmailID        = StepIdBase + 1 * 20 + 9;
        private const int TextEmailID         = StepIdBase + 1 * 20 + 10;
        private const int LabelLicenseID      = StepIdBase + 1 * 20 + 11;
        private const int TextLicenseID       = StepIdBase + 1 * 20 + 12;
        private const int LabelTargetsID      = StepIdBase + 1 * 20 + 13;
```

- [ ] **Step 3: Dispatch Step 2 in `BuildPage`**

In the `switch (step)` added in Task 3, add a `case 1` before `default`:

```csharp
                    case 1:
                        BuildOutputStep(stepGroup, leftEdge, indent, visibleEnabled);
                        break;
```

- [ ] **Step 4: Add the `BuildOutputStep` + `SeedOutputControls` helpers**

Add below `SeedModeControls`:

```csharp
        // Step 2 — output folder (+ Browse), package name, author/email/license,
        // and read-only target labels (ROS 2 Jazzy + Gz Harmonic are locked).
        private void BuildOutputStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
        {
            group.AddControl2(LabelOutputFolderID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Output folder", (short)leftEdge, visibleEnabled, "");
            PMTextOutputFolder = (PropertyManagerPageTextbox)group.AddControl2(
                TextOutputFolderID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "Folder the package is written to");
            PMButtonBrowse = (PropertyManagerPageButton)group.AddControl2(
                ButtonBrowseID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Browse…", (short)indent, visibleEnabled, "Choose the output folder");

            group.AddControl2(LabelPackageNameID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Package name", (short)leftEdge, visibleEnabled, "");
            PMTextPackageName = (PropertyManagerPageTextbox)group.AddControl2(
                TextPackageNameID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "ROS 2 package name (sanitized on export)");

            group.AddControl2(LabelAuthorID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Author", (short)leftEdge, visibleEnabled, "");
            PMTextAuthor = (PropertyManagerPageTextbox)group.AddControl2(
                TextAuthorID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "Maintainer name for package.xml");

            group.AddControl2(LabelEmailID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Email", (short)leftEdge, visibleEnabled, "");
            PMTextEmail = (PropertyManagerPageTextbox)group.AddControl2(
                TextEmailID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "Maintainer email for package.xml");

            group.AddControl2(LabelLicenseID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "License", (short)leftEdge, visibleEnabled, "");
            PMTextLicense = (PropertyManagerPageTextbox)group.AddControl2(
                TextLicenseID,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "SPDX license id for package.xml (e.g. MIT)");

            group.AddControl2(LabelTargetsID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Targets: ROS 2 Jazzy + Gz Sim Harmonic (fixed in this release)",
                (short)leftEdge, visibleEnabled, "");
        }

        private void SeedOutputControls()
        {
            if (PMTextOutputFolder == null) return;
            PMTextOutputFolder.Text = config.OutputFolder ?? "";
            PMTextPackageName.Text = config.PackageName ?? "";
            PMTextAuthor.Text = config.Author ?? "";
            PMTextEmail.Text = config.Email ?? "";
            PMTextLicense.Text = config.License ?? "";
        }
```

- [ ] **Step 5: Call `SeedOutputControls` after building the page**

In `BuildPage`, where Task 3 added `SeedModeControls();` (just before `ShowStep(0);`),
add a second line so it reads:

```csharp
            SeedModeControls();
            SeedOutputControls();
```

- [ ] **Step 6: Handle text edits in `OnTextboxChanged`**

Replace the no-op:

```csharp
        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text) { }
```

with:

```csharp
        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text)
        {
            switch (Id)
            {
                case TextOutputFolderID: config.OutputFolder = Text ?? ""; break;
                case TextPackageNameID:  config.PackageName = Text ?? ""; break;
                case TextAuthorID:       config.Author = Text ?? ""; break;
                case TextEmailID:        config.Email = Text ?? ""; break;
                case TextLicenseID:      config.License = Text ?? ""; break;
                default: break;
            }
        }
```

- [ ] **Step 7: Handle the Browse button in `OnButtonPress`**

In the existing `OnButtonPress` switch (currently `ButtonBackID` / `ButtonNextID`), add a
`ButtonBrowseID` case before `default`:

```csharp
                    case ButtonBrowseID: BrowseForOutputFolder(); break;
```

Then add the helper below `GoNext`:

```csharp
        // Opens a folder picker and writes the choice into config + the textbox.
        private void BrowseForOutputFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(config.OutputFolder))
                {
                    dialog.SelectedPath = config.OutputFolder;
                }
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    config.OutputFolder = dialog.SelectedPath;
                    if (PMTextOutputFolder != null)
                    {
                        PMTextOutputFolder.Text = dialog.SelectedPath;
                    }
                }
            }
        }
```

- [ ] **Step 8: Build / compile-review (as in Task 3 Step 8)**

Run (SolidWorks workstation): `msbuild SW2GZ.sln /t:Build /p:Configuration=Debug`
Expected: compiles. Off-workstation: skipped (review only).

- [ ] **Step 9: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzExportPmp.cs
git commit -m "feat(addin): Step 2 Output controls (folder/browse/name/metadata) in export PMP"
```

---

## Task 5: Load-on-open, save-on-Next, and SwAddin wiring

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzExportPmp.cs`
- Modify: `SW2GZ/SW/SwAddin.cs`

- [ ] **Step 1: Load the checkpoint in the constructor and resume**

In the constructor, the success branch currently calls `BuildPage();`. Replace:

```csharp
            if (longerrors == (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
            {
                BuildPage();
            }
```

with (load before building so seeding uses the loaded values, then resume position):

```csharp
            if (longerrors == (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
            {
                config = Sw2gzConfigSerialization.Load(model);
                BuildPage();
                ShowStep(config.LastStep);
            }
```

Note: `BuildPage` already ends with `ShowStep(0)`; the extra `ShowStep(config.LastStep)`
after it lands the wizard on the resumed step. `SeedModeControls` / `SeedOutputControls`
(called at the end of `BuildPage`) now reflect the loaded config.

- [ ] **Step 2: Save the checkpoint on each Next and on Finish**

In `GoNext`, record the current step and save before navigating. Replace:

```csharp
        private void GoNext()
        {
            if (currentStep < StepCount - 1)
            {
                ShowStep(currentStep + 1);
            }
            else
            {
                // Finish — no backend wired yet this increment.
                logger.Info("SW2GZ export shell Finish pressed (no backend wired yet)");
                swApp.SendMsgToUser("Export is not wired up yet — this is the navigation shell.");
                PMPage.Close(true);
            }
        }
```

with:

```csharp
        private void GoNext()
        {
            if (currentStep < StepCount - 1)
            {
                SaveCheckpoint(currentStep + 1);
                ShowStep(currentStep + 1);
            }
            else
            {
                // Finish — no backend wired yet this increment; still persist state.
                SaveCheckpoint(currentStep);
                logger.Info("SW2GZ export shell Finish pressed (no backend wired yet)");
                swApp.SendMsgToUser("Export is not wired up yet — this is the navigation shell.");
                PMPage.Close(true);
            }
        }

        // Persists the live config to the assembly document tree (the "checkpoint").
        // resumeStep is the step the wizard should reopen on.
        private void SaveCheckpoint(int resumeStep)
        {
            try
            {
                config.LastStep = resumeStep;
                Sw2gzConfigSerialization.Save(swApp, model, config);
            }
            catch (Exception e)
            {
                logger.Error("Failed to save SW2GZ wizard checkpoint", e);
            }
        }
```

- [ ] **Step 3: Pass the active assembly `ModelDoc2` from `SwAddin.LaunchWizard`**

In `SW2GZ/SW/SwAddin.cs`, in `LaunchWizard`, the assembly doc is already resolved as
`modeldoc`. Replace:

```csharp
                var pmp = new Sw2gzExportPmp((SldWorks)SwApp);
                pmp.Show();
```

with:

```csharp
                var pmp = new Sw2gzExportPmp((SldWorks)SwApp, modeldoc);
                pmp.Show();
```

- [ ] **Step 4: Build / compile-review**

Run (SolidWorks workstation): `msbuild SW2GZ.sln /t:Build /p:Configuration=Debug`
Expected: SW2GZ add-in compiles.

- [ ] **Step 5: Run the full net8 test suite (no regressions in pure code)**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: PASS — all green (the Task 1 round-trip tests included).

- [ ] **Step 6: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzExportPmp.cs SW2GZ/SW/SwAddin.cs
git commit -m "feat(addin): auto-save wizard checkpoint on Next + resume on open"
```

---

## Task 6: Manual verification on a SolidWorks workstation

This is the only end-to-end check for the COM-bound parts (Tasks 2–5). It requires
SolidWorks with the add-in registered/loaded.

- [ ] **Step 1: Build + register the add-in**

Run: `msbuild SW2GZ.sln /t:Build /p:Configuration=Debug`
Then load the add-in in SolidWorks (the post-build regasm step / Tools ▸ Add-Ins).

- [ ] **Step 2: Exercise Steps 1–2 and checkpoint**

1. Open an assembly, click the **SW2GZ** ribbon button → the export panel opens on Step 1.
2. Pick a mode (e.g. "Gz world (SDF world)"). Click **Next**.
3. On Step 2: click **Browse…**, choose a folder; type a package name, author, email, license. Click **Next** (advances to the placeholder Step 3).
4. Cancel/close the panel. In the FeatureManager design tree, confirm a feature named
   **"SW2GZ Export Configuration (v1)"** now exists.

- [ ] **Step 3: Confirm resume**

1. Save the assembly, close it, reopen it.
2. Click **SW2GZ** again. Expected: the panel reopens on the step you left from
   (`LastStep`), with the mode + folder + package + metadata fields all repopulated.

- [ ] **Step 4: Confirm legacy attribute untouched**

If the assembly had a legacy "URDF Export Configuration (v1.4)" feature, confirm it is
still present and unchanged (we neither read nor delete it).

---

## Self-Review

- **Spec coverage:** §1 model → Task 1; §2 serializer (new attribute) → Task 2; §3 leave legacy untouched → Tasks 2 (no read/delete) + 6 Step 4; §4 resume → Task 5 Step 1; §5 auto-save on Next → Task 5 Step 2; §6 controls (Mode radios, Output folder/browse/name/metadata, fixed target labels, stride 20, per-step builders) → Tasks 3–4; §6 files-touched list → all tasks; §6a retire MVVM (don't bind) → honored (no VM references anywhere). All covered.
- **Type consistency:** `Sw2gzExportConfig` fields (`Mode`, `OutputFolder`, `PackageName`, `Author`, `Email`, `License`, `LastStep`) are used identically across codec, serializer, and PMP handlers. `Sw2gzConfigCodec.ToXmlString`/`FromXmlString` and `Sw2gzConfigSerialization.Save`/`Load` signatures match their call sites. Control-ID constants are each defined once and referenced by the matching handler case.
- **Placeholder scan:** no TBD/TODO; every code step shows complete code.
- **Scope:** single focused increment (steps 1–2 + checkpoint). Steps 3–5 controls, export wiring, and MVVM deletion are explicitly out of scope.
