# v2.1.1 stabilization plan

Branch: `v2.1.1-stabilization` → PR into `main`.

Scope: 7 items curated from the review pass on v2.1.0. No new features.
No mode-content changes. No Stacks-ribbon work. Goal is to harden what
shipped in v2.1.0 before publishing the first post-v2.0.0 release.

## Items

| # | Item | Files (primary) | Effort |
|---|------|------------------|--------|
| 1 | Ribbon enable callback gates buttons on assembly-doc | `SW/SwAddin.cs` | small |
| 2 | ExportDialog pre-fills from saved config | `UI/ExportDialog.cs`, `SW/SwAddin.cs` | small |
| 3 | Pre-flight path validation in pipeline | `URDFExport/Sw2gzPipeline.cs` | small |
| 4 | Review step shows link + joint names, not counts | `UI/ViewModels/ReviewStepViewModel.cs` | small |
| 7 | Atomic export with rollback (temp dir + swap) | `URDFExport/Sw2gzPipeline.cs` | medium |
| 8 | Catch-all in every COM-boundary callback | `SW/SwAddin.cs` | small |
| 9 | Per-run `sw2gz_export.log` in workspace | `URDFExport/Sw2gzPipeline.cs` | medium |

Order: 1 → 8 (same file), 2, 4, 3, 7, 9 (3+7+9 stack on the pipeline).

## Out of scope (deferred to v2.2)

- Mode-aware wizard content (item 16 from review).
- Restore Stacks ribbon section (item 15 from review).
- Sensor source from SW COM (TODO P6-COM, item 17).
- Builder validation hardening (item 18).
- Sw2gzPipeline.Run param record refactor (item 11).

## Release

1. PR `v2.1.1-stabilization` → `main`, review diff.
2. After merge, tag `v2.1.1` on main; `gh release create v2.1.1`.
3. `gh release delete v2.0.0` (keep the git tag — fork/clone references stay valid).

## Verification gates

- Test suite green (target: ≥ 542).
- Release build emits fresh `bin\Release\SW2GZ.dll` (SolidWorks must be closed).
- Manual smoke: open assembly → Create Model wizard → Export → confirm workspace + log.
