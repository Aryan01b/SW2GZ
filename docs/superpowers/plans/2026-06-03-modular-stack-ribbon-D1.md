# Modular Stack Ribbon — D1 (Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce a persisted `StackProfile` config object and thread it through `Sw2gzPipeline.Run`, replacing the coarse `modelOnly` boolean — with zero change to current output (default profile reproduces full stack, a model-only profile reproduces today's `modelOnly:true` output).

**Architecture:** New `StackProfile` (sealed `[DataContract]` class, JointDef pattern) carrying an `ActuationBackend` enum + reserved flags. Stored on `Sw2gzExportConfig` so it round-trips to the assembly attribute. `Sw2gzPipeline.Run` gains a profile overload; the existing `modelOnly` overload delegates by mapping `true→StackProfile.ModelOnly()`, `false→StackProfile.Default()`. Internal `!modelOnly` branches become `profile.Actuation == ActuationBackend.Ros2Control`. No UI, no caller changes — behavior-preserving refactor.

**Tech Stack:** C# .NET Framework 4.8.1 (`SW2GZ`), net8.0 xUnit (`Test/SW2GZ.Writers.Test.csproj`), DataContractSerializer.

**Checkpoint / revert:** `git reset --hard checkpoint/pre-modular-ribbon`.

**Commenting requirement (user):** every new type, method, factory, and non-obvious branch gets a clear comment explaining *intent* (why), in the existing house style (file-header block + inline `//`). Reviewers must reject thin commenting.

---

### Task 1: `StackProfile` model + `ActuationBackend` enum

**Files:**
- Create: `SW2GZ/Ros2/StackProfile.cs`
- Test: `Test/URDFExport/StackProfileTests.cs`

**House style:** sealed `[DataContract(Namespace="")]` class with settable `[DataMember]` props initialised to defaults (mirror `SW2GZ/Build/Model/JointDef.cs`). Enum is a plain public enum (mirror `ExportMode`).

- [ ] **Step 1: Write the failing tests**

```csharp
// Test/URDFExport/StackProfileTests.cs
using SW2GZ.Ros2;
using Xunit;

namespace Test.URDFExport
{
    public class StackProfileTests
    {
        [Fact]
        public void Default_IsFullRos2ControlStack()
        {
            var p = StackProfile.Default();
            Assert.True(p.GzSim);
            Assert.Equal(ActuationBackend.Ros2Control, p.Actuation);
            Assert.False(p.SensorsEnabled);
        }

        [Fact]
        public void ModelOnly_DisablesActuation()
        {
            var p = StackProfile.ModelOnly();
            Assert.Equal(ActuationBackend.None, p.Actuation);
            Assert.False(p.SensorsEnabled);
        }

        [Fact]
        public void NewInstance_DefaultsMatchFactoryDefault()
        {
            // A bare `new StackProfile()` (what deserialization of an old config
            // without the field yields) must equal the full-stack default, so
            // existing assemblies keep exporting the full stack.
            var bare = new StackProfile();
            Assert.True(bare.GzSim);
            Assert.Equal(ActuationBackend.Ros2Control, bare.Actuation);
        }
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter StackProfileTests`
Expected: FAIL — `StackProfile` / `ActuationBackend` do not exist.

- [ ] **Step 3: Implement `StackProfile.cs`**

```csharp
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
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter StackProfileTests`
Expected: PASS (3/3).

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/Ros2/StackProfile.cs Test/URDFExport/StackProfileTests.cs
git commit -m "feat: add StackProfile config model (actuation backend + reserved stack flags)"
```

---

### Task 2: Persist `StackProfile` on `Sw2gzExportConfig`

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzExportConfig.cs` (add one `[DataMember]`)
- Test: `Test/URDFExport/Sw2gzExportConfigTests.cs` (add round-trip assertions)

- [ ] **Step 1: Write the failing test** — add to `Sw2gzExportConfigTests`:

```csharp
[Fact]
public void RoundTrip_PreservesStackProfile()
{
    var config = new Sw2gzExportConfig
    {
        Stacks = new SW2GZ.Ros2.StackProfile
        {
            GzSim = true,
            Actuation = SW2GZ.Ros2.ActuationBackend.GzPlugin,
            SensorsEnabled = true,
        },
    };

    string xml = Sw2gzConfigCodec.ToXmlString(config);
    Sw2gzExportConfig restored = Sw2gzConfigCodec.FromXmlString(xml);

    Assert.NotNull(restored.Stacks);
    Assert.True(restored.Stacks.GzSim);
    Assert.Equal(SW2GZ.Ros2.ActuationBackend.GzPlugin, restored.Stacks.Actuation);
    Assert.True(restored.Stacks.SensorsEnabled);
}

[Fact]
public void Default_StacksIsFullStack()
{
    // A fresh config must default to the full stack so unconfigured assemblies
    // export exactly as before this refactor.
    var config = new Sw2gzExportConfig();
    Assert.Equal(SW2GZ.Ros2.ActuationBackend.Ros2Control, config.Stacks.Actuation);
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter Sw2gzExportConfigTests`
Expected: FAIL — `Stacks` member does not exist.

- [ ] **Step 3: Add the member** to `Sw2gzExportConfig` (after `Joints`):

```csharp
        // Stacks — à-la-carte ROS 2 / Gazebo stack selection for this assembly.
        // Defaults to the full stack (Default()) so a config saved before this
        // field existed deserializes to the same full-stack export as before.
        [DataMember] public StackProfile Stacks { get; set; } = StackProfile.Default();
```

(`using SW2GZ.Ros2;` is already present at the top of the file.)

- [ ] **Step 4: Run — verify pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter Sw2gzExportConfigTests`
Expected: PASS (existing + 2 new).

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzExportConfig.cs Test/URDFExport/Sw2gzExportConfigTests.cs
git commit -m "feat: persist StackProfile on Sw2gzExportConfig (round-trips to assembly attribute)"
```

---

### Task 3: Thread `StackProfile` through `Sw2gzPipeline.Run`

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzPipeline.cs`
- Test: `Test/Integration/Sw2gzPipelineStackProfileTests.cs` (new)

**Design:** add a profile overload; keep the `modelOnly` overload delegating; refactor the body to use a single local derived from the profile. The five sites that read `modelOnly` (xacro include choice, ros2_control+gz.xacro block, gz_sim launch variant, ros2_control launch + config block) all key off **`profile.Actuation == ActuationBackend.Ros2Control`** — that is exactly today's `!modelOnly` set, so output is byte-identical.

- [ ] **Step 1: Write the failing tests** (new file):

```csharp
// Test/Integration/Sw2gzPipelineStackProfileTests.cs
using System;
using System.IO;
using Moq;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using SW2GZ.Ros2;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDFExport;
using Xunit;

namespace Test.Integration
{
    public class Sw2gzPipelineStackProfileTests
    {
        // NOTE: mirror the mock setup used in Test/Integration/Sw2gzPipelineTests.cs
        // (TinyMesh / LinkSpec / MassProps helpers). Implementer: reuse that file's
        // private helpers or copy the minimal setup.

        [Fact]
        public void DefaultProfile_EmitsFullStack_SameAsLegacy()
        {
            string tmp = NewTmp();
            var report = MakePipeline().Run(tmp, "prof_pkg", "A", "a@b", "Apache-2.0",
                Array.Empty<SensorDef>(), StackProfile.Default());

            string pkg = Path.Combine(tmp, "prof_pkg_ws", "src", "prof_pkg");
            Assert.True(File.Exists(Path.Combine(pkg, "urdf", "inc", "ros2_control.xacro")));
            Assert.True(File.Exists(Path.Combine(pkg, "urdf", "inc", "gz.xacro")));
            Assert.True(File.Exists(Path.Combine(pkg, "config", "controllers.yaml")));
            Assert.True(File.Exists(Path.Combine(pkg, "config", "ros_gz_bridge.yaml")));
        }

        [Fact]
        public void ModelOnlyProfile_OmitsControlAndPlugins_SameAsLegacyModelOnly()
        {
            string tmp = NewTmp();
            MakePipeline().Run(tmp, "bare_pkg", "A", "a@b", "MIT",
                Array.Empty<SensorDef>(), StackProfile.ModelOnly());

            string pkg = Path.Combine(tmp, "bare_pkg_ws", "src", "bare_pkg");
            Assert.False(File.Exists(Path.Combine(pkg, "urdf", "inc", "ros2_control.xacro")));
            Assert.False(File.Exists(Path.Combine(pkg, "urdf", "inc", "gz.xacro")));
            Assert.False(File.Exists(Path.Combine(pkg, "config", "controllers.yaml")));
            Assert.False(File.Exists(Path.Combine(pkg, "config", "ros_gz_bridge.yaml")));
            // model-only still writes the model + an empty world.
            Assert.True(File.Exists(Path.Combine(pkg, "urdf", "bare_pkg.urdf.xacro")));
            Assert.True(File.Exists(Path.Combine(pkg, "worlds", "empty.sdf")));
        }

        // --- helpers: implementer mirrors Sw2gzPipelineTests.cs mock setup ---
        private static string NewTmp()
        {
            string t = Path.Combine(Path.GetTempPath(), "sw2gz_prof_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(t);
            return t;
        }
        private static Sw2gzPipeline MakePipeline() { /* mirror existing test mocks */ throw new NotImplementedException(); }
    }
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter Sw2gzPipelineStackProfileTests`
Expected: FAIL — profile overload of `Run` does not exist (compile error) / helper NotImplemented.

- [ ] **Step 3: Implement the overload + refactor body** in `Sw2gzPipeline.cs`.

3a. Replace the existing 7-arg `Run(..., IReadOnlyList<SensorDef> sensors, bool modelOnly = false)` signature with a thin delegating overload plus a new profile overload:

```csharp
// Back-compat overload — the old coarse boolean maps onto the new profile:
//   modelOnly:false → full stack (Default)   modelOnly:true → bare model (ModelOnly)
// Existing callers/tests keep working byte-for-byte.
public SW2GZ.Validate.ValidationReport Run(string outputDir, string packageName,
                            string author, string email, string license,
                            IReadOnlyList<SensorDef> sensors, bool modelOnly = false) =>
    Run(outputDir, packageName, author, email, license, sensors,
        modelOnly ? StackProfile.ModelOnly() : StackProfile.Default());

// Profile-driven overload — single source of truth for which stacks emit.
public SW2GZ.Validate.ValidationReport Run(string outputDir, string packageName,
                            string author, string email, string license,
                            IReadOnlyList<SensorDef> sensors, StackProfile profile)
{
    if (sensors == null) throw new ArgumentNullException(nameof(sensors));
    if (profile == null) throw new ArgumentNullException(nameof(profile));

    // D1: actuation == Ros2Control reproduces the legacy full-stack output;
    // every other backend reproduces the legacy model-only output. GzPlugin's
    // own writer arrives in D3 — until then it falls through to model-only.
    bool fullStack = profile.Actuation == ActuationBackend.Ros2Control;

    // ... existing body unchanged, except replace every `modelOnly` read:
    //   `modelOnly ? A : B`  →  `fullStack ? B : A`   (invert — fullStack is !modelOnly)
    //   `if (!modelOnly)`     →  `if (fullStack)`
}
```

3b. In the moved body, update the five sites (keep all surrounding code identical):
- xacro: `fullStack ? XacroWriter.Write(pkg, bodyXml) : XacroWriter.WriteModelOnly(pkg, bodyXml)`
- `if (fullStack) { ros2_control.xacro + gz.xacro }`
- gz_sim launch: `fullStack ? LaunchPyWriter.GzSim(pkg) : LaunchPyWriter.GzSimModelOnly(pkg)`
- `if (fullStack) { ros2_control.launch.py + controllers.yaml + ros_gz_bridge.yaml }`

Add a short comment at each site: `// gated by actuation backend (D1: Ros2Control == full stack)`.

3c. Implement the test's `MakePipeline()` helper by mirroring the Moq setup in `Test/Integration/Sw2gzPipelineTests.cs` (same `IMassProperties`/`IAssemblyWalker`/`IMeshTessellator` stubs producing one tiny link, no mates).

- [ ] **Step 4: Run the full suite — verify pass + no regressions**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: PASS — new profile tests green AND the pre-existing `Sw2gzPipelineTests` (including the legacy `modelOnly: true` test) still green (proves byte-identical behaviour via the delegating overload).

- [ ] **Step 5: Build the add-in (Framework) to confirm no COM-side break**

Run: `& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" SW2GZ\SW2GZ.csproj /p:Configuration=Release /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build`
Expected: `SW2GZ -> ...bin\Release\SW2GZ.dll`. (PostBuild regasm MSB3216 access-denied is EXPECTED/non-fatal — DLL still compiles.) Close SolidWorks first if a file-lock MSB3027 appears.

- [ ] **Step 6: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzPipeline.cs Test/Integration/Sw2gzPipelineStackProfileTests.cs
git commit -m "refactor: thread StackProfile through Sw2gzPipeline.Run, replace modelOnly boolean"
```

---

## Done-when (D1)

- `StackProfile` exists, defaults to full stack, round-trips on `Sw2gzExportConfig`.
- `Sw2gzPipeline.Run` is profile-driven; `modelOnly` overload delegates; **all existing tests stay green** (behavior-preserving).
- Add-in compiles. No UI yet — D2 adds the ribbon flyout that writes `config.Stacks` and routes it into the export call.
