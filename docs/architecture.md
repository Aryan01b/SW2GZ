# SW2GZ — Architecture

Developer/contributor reference for the codebase **as it is today**. Last verified
2026-07-10 against `main` (tag `v2.8.0`, 501 tests green). User-facing overview:
[README](../README.md).

---

## 1. What it is

SW2GZ is a **SolidWorks add-in** (in-process COM, .NET Framework 4.8, C# 9) that turns
a SolidWorks assembly or part into a ROS 2 package or a Gz Sim (Harmonic) model/world —
generating meshes, URDF/Xacro, SDF, launch, and config files. Modernized fork of
`solidworks_urdf_exporter`.

## 2. Solution layout

| Project | TFM | Role |
|---|---|---|
| [`SW2GZ/SW2GZ.csproj`](../SW2GZ/SW2GZ.csproj) | net48 | The add-in. Needs SolidWorks COM interop DLLs to build; registered via `regasm` (post-build). |
| [`TestRunner/TestRunner.csproj`](../TestRunner/TestRunner.csproj) | net452 exe | Harness that runs xunit against the add-in assembly on a SolidWorks workstation. |
| [`Test/SW2GZ.Writers.Test.csproj`](../Test/SW2GZ.Writers.Test.csproj) | net8.0 | **The live test suite (501 tests).** Pulls pure-C# sources via `<Compile Include="..\SW2GZ\…">` so it builds with **no SolidWorks dependency**. Run with `dotnet test`. |

## 3. Three export modes, one entry point

The add-in exports three kinds of output, selected per-document:

- **Robot** — assembly → ROS 2 package (URDF, `ros2_control`, launch, RViz, a spawn world).
- **World** — assembly → standalone static `<world>.sdf` + meshes + scene/GUI.
- **Asset** — a part (or sub-assembly) → reusable Gz `model://` (model.config + model.sdf + mesh).

All three route through a single entry point, [`Sw2gzModelExporter.RunCore`](../SW2GZ/URDFExport/Sw2gzModelExporter.cs),
which branches on document type and `Sw2gzExportConfig.Mode`:

```
Sw2gzModelExporter.RunCore(swApp, model, config, outputDir)
  PartDoc                    → Sw2gzAssetExporter   (forced Asset mode)
  AssemblyDoc, Mode=SdfWorld → Sw2gzWorldExporter
  AssemblyDoc, Mode=SdfModel → Sw2gzAssetExporter
  AssemblyDoc, else          → Sw2gzRobotExporter
```

Each of the three exporters (`Sw2gzRobotExporter.cs`, `Sw2gzWorldExporter.cs`,
`Sw2gzAssetExporter.cs`, all in [`SW2GZ/URDFExport/`](../SW2GZ/URDFExport/)) is a
COM-free static class that reads the model through `SwSurface` abstractions, builds
plain-C# domain data, and calls the stateless `Ros2/`/`Gz/` writers directly — there is
no shared model-aggregate/pipeline object between them.

**Dead code:** `ExportHelper.ExportRobot` (the pre-rebuild "legacy" entry point) now
unconditionally throws `NotSupportedException` for Robot Package exports. It's only
reachable from the old WinForms `AssemblyExportForm.cs`, which the current ribbon UI
doesn't use. Don't build on it.

## 4. Component map

| Layer | Directory | Role |
|---|---|---|
| Entry | [`SW/SwAddin.cs`](../SW2GZ/SW/SwAddin.cs), [`SW/EventHandling.cs`](../SW2GZ/SW/EventHandling.cs) | COM lifecycle, command manager (ribbon), doc events |
| UI | [`UI/`](../SW2GZ/UI/), wizard PMPs in [`UI/Pmp/`](../SW2GZ/UI/Pmp/) (`Sw2gzCreateRobotPmp`, `Sw2gzCreateWorldPmp`, `Sw2gzCreateAssetPmp`, `Sw2gzWorldSettingsPmp`, `Sw2gzWorldSensorsPmp`) | SolidWorks PropertyManagerPage wizard steps + `Sw2gzProfileDialog`, `PreviewDialog` |
| Orchestration | [`URDFExport/Sw2gzModelExporter.cs`](../SW2GZ/URDFExport/Sw2gzModelExporter.cs), `Sw2gzRobotExporter.cs`, `Sw2gzWorldExporter.cs`, `Sw2gzAssetExporter.cs`, `Sw2gzDoc*.cs` (in-doc persistence of wizard state) | mode entry point + the three exporters |
| SW boundary | [`SwSurface/`](../SW2GZ/SwSurface/) — `SolidWorksMeshTessellator`, `SolidWorksMassProperties`, `SolidWorksComponentPoses`, `SwMateJointResolver` | DI seams over SolidWorks COM |
| Domain | [`Build/`](../SW2GZ/Build/), [`Math/`](../SW2GZ/Math/) | pure POCO — `MateJointClassification` (mate → joint type/limit), `LinkHierarchy`, `InertialAggregator`, `QuickHull3D` (convex hull) / `ConvexHullCollider`, `PackageNameSanitizer`, `RosNameSanitizer` |
| Writers | [`Ros2/`](../SW2GZ/Ros2/), [`Gz/`](../SW2GZ/Gz/) | stateless statics, one per output artifact — `WorldLaunchPyWriter`, `WorldBridgeYaml`, `SdfWorldWriter`, `SdfAssetModelWriter`, `SdfSensorBlocks`, `TargetProfile` (ROS distro ↔ Gz version pairing) |
| Validate | [`Validate/`](../SW2GZ/Validate/) | `UrdfXmlValidator`, `OutputValidator`, `PackageNameChecker`, `PluginNameChecker`, `MeshFileChecker` |

Support: `Utilities/` (Logger, MathOPS) · `Versioning/` · `Compat/` · `Exceptions/`.

## 5. Joints from SolidWorks mates

Robot mode reads real joint type, axis, and limits from the assembly's mates:

- [`MateJointClassification`](../SW2GZ/Build/MateJointClassification.cs) auto-classifies
  a mate into a joint type + limit: Lock → Fixed, Concentric → Continuous/Revolute,
  Angle → Revolute, Distance → Prismatic.
- [`SwMateJointResolver`](../SW2GZ/SwSurface/SwMateJointResolver.cs) resolves the joint
  axis from a manual face/edge pick in the Joints wizard step (`Sw2gzCreateRobotPmp.Joints.cs`).
- `Sw2gzRobotExporter` computes real relative joint origin/rpy between linked parts.

This is live-tested end-to-end on a real assembly (see the `v2.7.0` tag).

## 6. SolidWorks COM boundary

- COM types appear only under `SwSurface/*.cs`, guarded by `#if SW_INTEROP`.
- Building `SW2GZ.csproj` defines `SW_INTEROP` (real COM code). The net8 test project
  does **not** → the same files compile as throwing skeletons, so domain/writers test
  without SolidWorks.
- COM RCWs (`IModelDoc2`, `IMassProperty`, `ITessellation`, …) are released via
  `Marshal.ReleaseComObject` in `finally`.
- Material color comes straight from the tessellator's `MeshData.MaterialColor` — there
  is no separate appearance-source seam in the live path.

## 7. Build & test

```powershell
# Pure-C# writer/domain tests (no SolidWorks needed) — the everyday loop
dotnet test Test\SW2GZ.Writers.Test.csproj        # 501 tests

# Full add-in: open SW2GZ.sln in Visual Studio on a machine with SolidWorks
#   installed; build registers the add-in via regasm (post-build event).
```

The add-in itself is **not buildable without SolidWorks interop DLLs**; verify add-in /
`#if SW_INTEROP` code on a SolidWorks workstation. See [BUILD.md](../BUILD.md).

## 8. Conventions

- **Name sanitization:** [`PackageNameSanitizer`](../SW2GZ/Build/PackageNameSanitizer.cs)
  (ament, lowercase) and [`RosNameSanitizer`](../SW2GZ/Build/RosNameSanitizer.cs)
  (link/joint/frame, case-preserving).
- **XML safety:** dynamic names escaped with `System.Security.SecurityElement.Escape`
  in every URDF/SDF/xacro writer.
- **Numbers:** all float formatting uses `CultureInfo.InvariantCulture`.
- **Writers are stateless statics**, one per output artifact, golden-tested.
- **Collision strategy:** real convex hull (`QuickHull3D`) by default; AABB available
  as an explicit fallback (`ColliderStrategy.Aabb`).

## 9. Known gaps

- **Actuation backends:** only `None` / `Ros2Control` work. `ActuationBackend.GzPlugin`
  (DiffDrive / JointController / PositionController) is an unimplemented enum value.
- **No `PosePublisher`/`OdometryPublisher`** on Robot mode.
- **No sensor `<noise>` model** config.
- **Asset primitive collision** (box/sphere/cylinder) is shipped, but the **visual
  always stays mesh** — no primitive-geometry visual option anywhere yet.
- **World mode:** no solver/contact-parameter panel, no `LogRecord`/`LogPlayback`.

See [`docs/mode-feature-matrix.md`](mode-feature-matrix.md) for the full per-mode
feature table.

## 10. Extending

- Add an output artifact → new stateless writer in `Ros2/` or `Gz/` + a golden/unit
  test in `Test/`, and (if pure-C#) add its source to the test project's
  `<Compile Include>` list.
- Touch SolidWorks → keep it behind a `SwSurface` interface so the domain stays testable.
- New `.cs` files go in **both** `SW2GZ/SW2GZ.csproj` and `Test/SW2GZ.Writers.Test.csproj`
  (legacy csproj, no glob includes).
