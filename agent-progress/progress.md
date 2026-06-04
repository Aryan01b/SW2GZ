# Progress

Current: **v2.1.1** stabilization on `v2.1.1-stabilization` branch (pending PR
→ `main`, then tag + GitHub release). Phase 1 shipped in 2.1.0; v2.1.1 is the
first GitHub release after v2.0.0 (v2.1.0 folded in).

## Done

- **v2.1.1 stabilization** (7 items shipped on `v2.1.1-stabilization`):
  ribbon null-safety + DRY, ExportDialog cross-assembly defaults + named
  link/joint summary, pipeline preflight + atomic re-export + per-run log,
  COM-boundary catch-all, stale-test cleanup, golden refresh. Test suite
  605/605 green, Release DLL builds.
- **Phase 1 — RobotPackage export** (v2.1.0, CHANGELOG.md). Wizard +
  one-click Export → turn-key ROS 2 Jazzy / Gz Harmonic package.
- **Pipeline groundwork P1–P9**: `RobotModel` aggregate, inertial math,
  QuickHull3D collider, materials, sensors data, etc. (see CHANGELOG).
- **Export-modes cleanup** — single `Sw2gzPipeline.Run` overload threads
  `ExportMode` + `StackProfile`. All three modes route through it. Dead
  `SdfModelInput` path removed. (commits `54c8ed8`, `5bc28ff`, `0a48f28`)
- **Wizard mode-aware step plan** — gz asset/world skip Links+Joints
  (`763fb20`); Back/Next fixed with unit-tested step plan (`4f9dd42`);
  Output step dropped, moved to ExportDialog (`d7d43f4`).
- **Exporter polish** — per-package README + gitignore (`464dcc2`); Jazzy
  + Gz Harmonic-ready output (`29837f9`).
- **Ribbon trim** — Stacks buttons removed for now (`b7d82c7`).

## Next (v2.2 candidates)

- Wizard step content tailored per `ExportMode` (today only the step *list*
  changes; content for SdfModel/SdfWorld is still placeholder).
- Bring Stacks buttons back behind a saved-model + RobotPackage gate.

## Conventions reminder

- No AI attribution in commits / files (memory: `no-ai-attribution`).
- Add new `.cs` to **both** `SW2GZ.csproj` and `Test\SW2GZ.Writers.Test.csproj`.
- Close SolidWorks before rebuild or `bin\Release\SW2GZ.dll` copy is blocked.
