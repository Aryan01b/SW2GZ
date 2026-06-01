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
| [`Test/SW2GZ.Writers.Test.csproj`](../Test/SW2GZ.Writers.Test.csproj) | net8.0 | **The live test suite (413 tests).** Pulls pure-C# sources via `<Compile Include="..\SW2GZ\…">` so it builds with **no SolidWorks dependency**. Run with `dotnet test`. |

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
│ (SW boundary)│ IAssemblyWalker, IMeshTessellator,     │
│              │ IAppearanceSource, IUnitsContext       │
│              │ — last two: deferred wiring) +         │
│              │ SolidWorks* impls  (#if SW_INTEROP)    │
├──────────────┴──────────────────────────────────────┤
│ Build/ , Math/   pure-POCO domain                     │
│   LinkSpec→UrdfLink/Joint · InertialAggregator ·      │
│   QuickHull3D (real hull) + AABB fallback ·           │
│   RobotModel · RobotModelBuilder · UnitsScaler ·      │
│   Matrix3/Pose · PackageNameSanitizer ·               │
│   RosNameSanitizer                                    │
├─────────────────────────────────────────────────────┤
│ Write/  stateless emitters (1 per artifact)           │
│   Urdf/UrdfSerializer (RobotModel → URDF/Xacro) ·     │
│   Ros2/ (Xacro, AmentCMake, LaunchPy, Controllers,    │
│   Ros2Control, RvizConfig, Readme, PackageXmlV3) ·    │
│   Gz/ (Sdf*, SdfSensorBlocks, SdfSensorPlugins,       │
│   ModelConfig, GzPluginTags, RosGzBridge) ·           │
│   Write/Mesh/ (DaeWriter, StlWriter)                  │
├─────────────────────────────────────────────────────┤
│ Validate/  UrdfXmlValidator, OutputValidator,         │
│   RobotModelValidator (pre-write structural), checkers│
└─────────────────────────────────────────────────────┘
Support: Utilities/ (Logger, MathOPS) · Versioning/ · Compat/ · Exceptions/
```

## 4. Two export paths (important — current reality)

[`ExportHelper.ExportRobot`](../SW2GZ/URDFExport/ExportHelper.cs) branches:

- **New pipeline** — when `Mode == RobotPackage` and built with `SW_INTEROP`: constructs [`Sw2gzPipeline`](../SW2GZ/URDFExport/Sw2gzPipeline.cs) with the SolidWorks SwSurface services and runs **SwSurface → Build (RobotModelBuilder) → Validate (RobotModelValidator) → Write (UrdfSerializer + Sdf*) → Validate (output)**. URDF/Xacro is assembled via the `UrdfSerializer` against an immutable `RobotModel` (no inline string concat); SDF sensor blocks come from `SdfSensorBlocks`. This path is unit-tested (net8 suite).
- **Legacy path** — `SdfModel` / `SdfWorld` modes, and the `RobotPackage` fallback: builds a `URDFRobot` tree ([`URDF/`](../SW2GZ/URDF)) via `CreateRobotFromActiveModel`, consumed by `Ros2Package` / `ModelConfigWriter` / `SdfModelWriter`.

The roadmap unifies these onto a single `RobotModel` and deletes the legacy path (see roadmap §3, §9.4).

## 5. Component map

| Layer | Key files | Notes |
|---|---|---|
| Entry | [`SW/SwAddin.cs`](../SW2GZ/SW/SwAddin.cs), [`SW/EventHandling.cs`](../SW2GZ/SW/EventHandling.cs) | COM lifecycle, command manager, doc events |
| Orchestration | [`URDFExport/Sw2gzPipeline.cs`](../SW2GZ/URDFExport/Sw2gzPipeline.cs), [`URDFExport/ExportHelper.cs`](../SW2GZ/URDFExport/ExportHelper.cs) | new + legacy |
| SW boundary | `SwSurface/Abstractions/*`, `SwSurface/SolidWorks*.cs` | DI seams; COM behind `#if SW_INTEROP` |
| Domain | `Build/*`, `Math/*` | pure POCO, no SW types |
| Domain aggregate | `Build/RobotModelBuilder.cs`, `Build/Model/*` (`RobotModel`, `MaterialDef`, `SensorDef`, …) | immutable aggregate consumed by all serializers |
| Units | [`Build/UnitsScaler.cs`](../SW2GZ/Build/UnitsScaler.cs) | SI scaler (schema only; pipeline wiring deferred to P3-units) |
| Collision | [`Build/QuickHull3D.cs`](../SW2GZ/Build/QuickHull3D.cs) | real convex hull (replaces AABB-only); AABB retained as `ColliderStrategy.Aabb` fallback |
| Writers | `Ros2/*`, `Gz/*`, `Write/Mesh/*` | stateless statics |
| URDF serializer | [`Write/Urdf/UrdfSerializer.cs`](../SW2GZ/Write/Urdf/UrdfSerializer.cs) | RobotModel → URDF/Xacro (supersedes pipeline string-concat) |
| SDF sensors | [`Gz/SdfSensorBlocks.cs`](../SW2GZ/Gz/SdfSensorBlocks.cs), [`Gz/SdfSensorPlugins.cs`](../SW2GZ/Gz/SdfSensorPlugins.cs) | per-link sensor SDF + world-level family plugin dedup |
| Targets | [`Ros2/TargetProfile.cs`](../SW2GZ/Ros2/TargetProfile.cs) | ROS distro ↔ Gz version pairing |
| Validate | `Validate/*`, [`Validate/RobotModelValidator.cs`](../SW2GZ/Validate/RobotModelValidator.cs), [`URDFExport/OutputValidator.cs`](../SW2GZ/URDFExport/OutputValidator.cs) | structural pre-write checks + XML well-formedness + checkers |

## 6. Data flow (new pipeline)

```
SwAddin → ExportHelper.ExportRobot
  → Sw2gzPipeline.Run(outputDir, pkg, author, email, license, sensors)
      1 sanitize pkg (PackageNameSanitizer)
      2 IAssemblyWalker.WalkActive() → LinkSpec[]
      3 per link: IMeshTessellator + IMassProperties + IAppearanceSource
                 → QuickHull3D (or AABB fallback), InertialAggregator (R·I·Rᵀ),
                   LinkBuilder → UrdfLink; MaterialDef collected
      4 RobotModelBuilder → immutable RobotModel
                            (joints still empty in pipeline — JointBuilder
                             ready, awaits P2 SW mate reader)
      4.5 RobotModelValidator (errors throw Sw2gzExportException;
                               warnings flow to ValidationReport)
      5 transactional write: meshes + URDF/Xacro (UrdfSerializer)
                            + SDF (incl. SdfSensorBlocks per sensor +
                                   SdfSensorPlugins dedup at world level)
                            + launch + config + ros_gz_bridge entries
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
- **Collision strategy** is `ColliderStrategy.ConvexHull` (real QuickHull3D); AABB available as explicit fallback (`ColliderStrategy.Aabb`).

## 10. Known limitations (v2.1)

- **Joints:** still empty in pipeline — multi-link robots export as disconnected links at the origin. `JointBuilder` is ready; P2 needs the SolidWorks mate reader (SW workstation required).
- **Collision:** ✅ now real convex hull (QuickHull3D); AABB retained as explicit fallback.
- **Inertia:** ✅ rotated per-part (`R·I·Rᵀ` via `InertialAggregator`); multi-body parallel-axis still uses identity rotation between part frames (existing limitation — see roadmap §1.2).
- **Materials:** ✅ shipped via `IAppearanceSource` + `MaterialDef`; default stub returns `null` (SW COM implementation deferred to P5-COM).
- **Sensors:** ✅ schema + emit shipped (7 types + per-sensor SDF + world plugin dedup + bridge entries); SW COM source deferred; UI assignment deferred to P8.
- **Multi-body links:** only the first part is tessellated for the visual mesh (unchanged limitation).
- **Modes:** `SdfModel` / `SdfWorld` run only on the legacy path.

All of the above are tracked in the [roadmap](superpowers/specs/2026-06-01-robust-exporter-architecture.md).

## 10.5 v2.1-revamp status

Six phases have shipped on the `v2.1-revamp` branch (tests 254 → 413/413 green):

- **P1** — `RobotModel` aggregate + `RobotModelBuilder` + `UrdfSerializer` (keystone)
- **P3-math** — `InertialAggregator` rotation (`R·I·Rᵀ`) + `Matrix3` ops + `UnitsScaler` (schema)
- **P4** — `QuickHull3D` real convex hull + `ColliderStrategy { ConvexHull, Aabb }` enum
- **P5** — `IAppearanceSource` + `MaterialDef` (RGBA-validated, deduped) + `inc/materials.xacro`
- **P6-data** — 7 sensor records + `SdfSensorBlocks` + `SdfSensorPlugins` + per-sensor bridge entries
- **P9** — `RobotModelValidator` (12 structural checks; pre-write in `Sw2gzPipeline.Run`)

**Deferred:** P2 (joints from SW mates), P3-units (pipeline wiring), P5-COM (SW appearance reader), P6-COM (SW sensor reader), P7 (SDF serializer + legacy retirement), P8 (WPF wizard UI). P2 / P3-units / P5-COM / P6-COM require a SolidWorks workstation; P7 / P8 are out-of-scope for this branch.

## 11. Extending

- Add an output artifact → new stateless writer in `Ros2/` or `Gz/` + a golden/unit test in `Test/`, and (if pure-C#) add its source to the test project's `<Compile Include>` list.
- Touch SolidWorks → keep it behind a `SwSurface` interface so the domain stays testable.
- Planned work and sequencing live in the [roadmap](superpowers/specs/2026-06-01-robust-exporter-architecture.md); UI direction in [docs/ui-mockups/](ui-mockups/).
