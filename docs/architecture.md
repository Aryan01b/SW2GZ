# SW2GZ — Architecture (current state)

Developer/contributor reference for the codebase **as it is today** (2026-06-01). For the future direction see the roadmap: [robust-exporter-architecture](superpowers/specs/2026-06-01-robust-exporter-architecture.md). For what changed recently: [session change log](session-2026-06-01-changes.md). User-facing overview: [README](../README.md).

---

## 1. What it is

SW2GZ is a **SolidWorks add-in** (in-process COM, .NET Framework 4.8.1, C# 9) that turns a SolidWorks assembly into a ROS 2 package or a Gz Sim (Harmonic) model/world — generating meshes, URDF/Xacro, SDF, launch, and config files. Modernized fork of `solidworks_urdf_exporter`.

## 2. Solution layout

| Project | TFM | Role |
|---|---|---|
| [`SW2GZ/SW2GZ.csproj`](../SW2GZ/SW2GZ.csproj) | net48 | The add-in. Needs SolidWorks COM interop DLLs to build; registered via `regasm` (post-build). |
| [`TestRunner/TestRunner.csproj`](../TestRunner/TestRunner.csproj) | net452 exe | Harness that runs xunit against the add-in assembly. |
| [`Test/SW2GZ.Writers.Test.csproj`](../Test/SW2GZ.Writers.Test.csproj) | net8.0 | **The live test suite (254 tests).** Pulls pure-C# sources via `<Compile Include="..\SW2GZ\…">` so it builds with **no SolidWorks dependency**. Run with `dotnet test`. |

## 3. Layered architecture

```
SolidWorks host
   │  ISwAddin
┌──▼─────────────────────────────────────────────────┐
│ SW/SwAddin.cs        COM entry, toolbar/menu, events │
├─────────────────────────────────────────────────────┤
│ UI/  (WinForms + WPF + SW PropertyManagerPage)       │
│   AssemblyExportForm · PartExportForm · TreeMergeWPF  │
│   URDFTreeView · Sw2gzProfileDialog                   │
├─────────────────────────────────────────────────────┤
│ URDFExport/  orchestration                            │
│   ExportHelper ─┬─ Sw2gzPipeline   (NEW, tested)      │
│                 └─ legacy URDFRobot path              │
├──────────────┬──────────────────────────────────────┤
│ SwSurface/   │ abstractions (IMassProperties,         │
│ (SW boundary)│ IAssemblyWalker, IMeshTessellator) +   │
│              │ SolidWorks* impls  (#if SW_INTEROP)    │
├──────────────┴──────────────────────────────────────┤
│ Build/ , Math/   pure-POCO domain                     │
│   LinkSpec→UrdfLink/Joint · InertialAggregator ·      │
│   ConvexHullCollider(AABB) · Matrix3/Pose ·           │
│   PackageNameSanitizer · RosNameSanitizer             │
├─────────────────────────────────────────────────────┤
│ Write/  stateless emitters (1 per artifact)           │
│   Ros2/ (Xacro, AmentCMake, LaunchPy, Controllers,    │
│   Ros2Control, RvizConfig, Readme, PackageXmlV3) ·    │
│   Gz/ (Sdf*, ModelConfig, GzPluginTags, RosGzBridge) ·│
│   Write/Mesh/ (DaeWriter, StlWriter)                  │
├─────────────────────────────────────────────────────┤
│ Validate/  UrdfXmlValidator, OutputValidator, checkers│
└─────────────────────────────────────────────────────┘
Support: Utilities/ (Logger, MathOPS) · Versioning/ · Compat/ · Exceptions/
```

## 4. Two export paths (important — current reality)

[`ExportHelper.ExportRobot`](../SW2GZ/URDFExport/ExportHelper.cs) branches:

- **New pipeline** — when `Mode == RobotPackage` and built with `SW_INTEROP`: constructs [`Sw2gzPipeline`](../SW2GZ/URDFExport/Sw2gzPipeline.cs) with the SolidWorks SwSurface services and runs **SwSurface → Build → Write → Validate**. This path is unit-tested (net8 suite).
- **Legacy path** — `SdfModel` / `SdfWorld` modes, and the `RobotPackage` fallback: builds a `URDFRobot` tree ([`URDF/`](../SW2GZ/URDF)) via `CreateRobotFromActiveModel`, consumed by `Ros2Package` / `ModelConfigWriter` / `SdfModelWriter`.

The roadmap unifies these onto a single `RobotModel` and deletes the legacy path (see roadmap §3, §9.4).

## 5. Component map

| Layer | Key files | Notes |
|---|---|---|
| Entry | [`SW/SwAddin.cs`](../SW2GZ/SW/SwAddin.cs), [`SW/EventHandling.cs`](../SW2GZ/SW/EventHandling.cs) | COM lifecycle, command manager, doc events |
| Orchestration | [`URDFExport/Sw2gzPipeline.cs`](../SW2GZ/URDFExport/Sw2gzPipeline.cs), [`URDFExport/ExportHelper.cs`](../SW2GZ/URDFExport/ExportHelper.cs) | new + legacy |
| SW boundary | `SwSurface/Abstractions/*`, `SwSurface/SolidWorks*.cs` | DI seams; COM behind `#if SW_INTEROP` |
| Domain | `Build/*`, `Math/*` | pure POCO, no SW types |
| Writers | `Ros2/*`, `Gz/*`, `Write/Mesh/*` | stateless statics |
| Targets | [`Ros2/TargetProfile.cs`](../SW2GZ/Ros2/TargetProfile.cs) | ROS distro ↔ Gz version pairing |
| Validate | `Validate/*`, [`URDFExport/OutputValidator.cs`](../SW2GZ/URDFExport/OutputValidator.cs) | XML well-formedness + checkers |

## 6. Data flow (new pipeline)

```
SwAddin → ExportHelper.ExportRobot
  → Sw2gzPipeline.Run(outputDir, pkg, author, email, license)
      1 sanitize pkg (PackageNameSanitizer)
      2 IAssemblyWalker.WalkActive() → LinkSpec[]
      3 per link: IMeshTessellator + IMassProperties
                 → ConvexHullCollider, InertialAggregator, LinkBuilder → UrdfLink
      4 joints: (empty in v2.0)
      5 transactional write: meshes + URDF/Xacro + SDF + launch + config
      6 OutputValidator.Run → ValidationReport
```

## 7. SolidWorks COM boundary

- COM types appear only in `SwSurface/SolidWorks*.cs`, guarded by `#if SW_INTEROP`.
- Building `SW2GZ.csproj` defines `SW_INTEROP` (real COM code). The net8 test project does **not** → the same files compile as throwing skeletons, so domain/writers test without SolidWorks.
- COM RCWs (`IModelDoc2`, `IMassProperty`, `ITessellation`, …) are released via `Marshal.ReleaseComObject` in `finally` (long-lived host → no per-export leak).

## 8. Build & test

```powershell
# Pure-C# writer/domain tests (no SolidWorks needed) — the everyday loop
dotnet test Test\SW2GZ.Writers.Test.csproj        # 254 tests

# Full add-in: open SW2GZ.sln in Visual Studio on a machine with SolidWorks
#   installed; build registers the add-in via regasm (post-build event).
```
The add-in itself is **not buildable without SolidWorks interop DLLs**; verify add-in / `#if SW_INTEROP` code on a SolidWorks workstation. See [BUILD.md](../BUILD.md).

## 9. Conventions

- **Name sanitization:** [`PackageNameSanitizer`](../SW2GZ/Build/PackageNameSanitizer.cs) (ament, lowercase) and [`RosNameSanitizer`](../SW2GZ/Build/RosNameSanitizer.cs) (link/joint/frame, case-preserving) at the `LinkBuilder` chokepoint.
- **XML safety:** dynamic names escaped with `System.Security.SecurityElement.Escape` in every URDF/SDF/xacro writer (defense-in-depth on top of sanitization).
- **Numbers:** all float formatting uses `CultureInfo.InvariantCulture` (comma-decimal locales must not corrupt URDF/SDF).
- **Transactional write:** `Sw2gzPipeline` removes the workspace dir on mid-write failure if it created it (no half-written packages).
- **Writers are stateless statics**, one per output artifact, golden-tested.

## 10. Known limitations (v2.0)

- **Joints:** pipeline emits an empty joint list → multi-link robots export as disconnected links at the origin. (`JointBuilder` exists but is not wired in.)
- **Collision:** `ConvexHullCollider` currently produces an **AABB box**, not a real hull (name is aspirational).
- **Inertia:** multi-part links aggregated at identity pose (no parallel-axis offset).
- **Materials / sensors:** not exported.
- **Multi-body links:** only the first part is tessellated for the visual mesh.
- **Modes:** `SdfModel` / `SdfWorld` run only on the legacy path.

All of the above are addressed in the [roadmap](superpowers/specs/2026-06-01-robust-exporter-architecture.md).

## 11. Extending

- Add an output artifact → new stateless writer in `Ros2/` or `Gz/` + a golden/unit test in `Test/`, and (if pure-C#) add its source to the test project's `<Compile Include>` list.
- Touch SolidWorks → keep it behind a `SwSurface` interface so the domain stays testable.
- Planned work and sequencing live in the [roadmap](superpowers/specs/2026-06-01-robust-exporter-architecture.md); UI direction in [docs/ui-mockups/](ui-mockups/).
