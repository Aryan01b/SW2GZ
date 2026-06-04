# CLAUDE.md — SW2GZ

Project-specific guidance for the coding agent. Read this first.

## Local progress tracking

The agent maintains a short scratchpad at **`agent-progress/`**:

- [`agent-progress/flow.md`](agent-progress/flow.md) — one-page mental model
  (ribbon → wizard → pipeline, mode matrix).
- [`agent-progress/progress.md`](agent-progress/progress.md) — what shipped,
  what's next.

Read both at the start of any non-trivial task so you don't relearn the
project. Update `progress.md` when something material lands; keep it terse.

Authoritative history lives in `CHANGELOG.md` and `git log` — `agent-progress/`
is just an at-a-glance cache.

## Build / deploy

See memory `sw2gz-build-deploy` (MSBuild path, `SolutionDir` param, regasm +
SolidWorks-lock gotchas).

## Conventions

- **No AI attribution.** No "Generated with Claude" footers, no
  `Co-Authored-By: Claude`. Credit Aryan Arlikar + the upstream
  `solidworks_urdf_exporter` only.
- **Legacy csproj.** New `.cs` / content files must be added to **both**
  `SW2GZ\SW2GZ.csproj` and `Test\SW2GZ.Writers.Test.csproj`.
- **Tests must stay green** (currently 542).
