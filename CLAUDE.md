# CLAUDE.md — SW2GZ

Project-specific guidance for the coding agent. Read this first.

## Local progress tracking

**FETCH PROJECT STATUS FIRST.** At the start of every session, read
[`agent-progress/progress.md`](agent-progress/progress.md) — it is the
single source of current context (what shipped, where the code is, what's
next). The context window may have been cleared; this file is how you
recover the project state. Do not relearn the project from scratch.

The agent maintains a short scratchpad at **`agent-progress/`**:

- [`agent-progress/progress.md`](agent-progress/progress.md) — **canonical
  status / dev-track.** What shipped, current session's work, what's next.
  Read this FIRST. Update it whenever something material lands; keep terse.
- [`agent-progress/flow.md`](agent-progress/flow.md) — one-page mental model
  (ribbon → wizard → pipeline, mode matrix).

Authoritative history lives in `CHANGELOG.md` and `git log` — `agent-progress/`
is just an at-a-glance cache.

## Build / deploy

See memory `sw2gz-build-deploy` (MSBuild path, `SolutionDir` param, regasm +
SolidWorks-lock gotchas).

## Conventions

- **PowerShell + git.** git writes normal status (e.g. `Switched to branch`)
  to stderr, which PowerShell flags as an error and sets `$?` to `$false` on
  success. Don't chain git steps on `if ($?)`; check `$LASTEXITCODE -eq 0` or
  run steps separately.
- **No AI attribution.** No "Generated with Claude" footers, no
  `Co-Authored-By: Claude`. Credit Aryan Arlikar + the upstream
  `solidworks_urdf_exporter` only.
- **Legacy csproj.** New `.cs` / content files must be added to **both**
  `SW2GZ\SW2GZ.csproj` and `Test\SW2GZ.Writers.Test.csproj`.
- **Tests must stay green** (currently 751).
