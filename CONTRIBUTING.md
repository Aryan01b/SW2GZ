# Contributing to SW2GZ

Bug reports, feature requests, and PRs are welcome.

## Getting started

1. Fork the repo, clone your fork.
2. Set up a build per [BUILD.md](BUILD.md).
3. Branch off `main`: `feat/<short-name>` or `fix/<short-name>`.
4. Make your change, add/update tests (see below).
5. Open a PR against `main`. Describe *why*, not just *what* — link the issue
   if there is one. CI (build + tests) must pass before merge.

## Architecture

Robot Package mode runs a layered pipeline: SolidWorks I/O sits behind
interfaces (`SwSurface`), so the Build → Write → Validate layers are fully
unit-testable without SolidWorks installed. See
[`docs/reference/solidworks-api.md`](docs/reference/solidworks-api.md) for
the SolidWorks COM API surface this codebase calls.

## Commits

Conventional Commits format:
- `feat:` new feature
- `fix:` bug fix
- `chore:` housekeeping (deps, build, ignored files)
- `refactor:` no behavior change
- `test:` tests only
- `docs:` documentation
- `ci:` CI workflow changes

## Tests

Pure-C# writer tests run anywhere — no SolidWorks needed:

```bash
dotnet test Test/SW2GZ.Writers.Test.csproj --filter "Category=Unit"
```

New `.cs` files go in **both** `SW2GZ/SW2GZ.csproj` and
`Test/SW2GZ.Writers.Test.csproj`. All tests must stay green.

SolidWorks-dependent integration tests require a workstation with SolidWorks
installed and run via the `TestRunner/` standalone exe (see
`TestRunner/README.md`).

## License header for new files

Any new .cs file added in this fork uses this header:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/
```

Files inherited from upstream `ros/solidworks_urdf_exporter` keep their
original `Copyright (c) 2015 Stephen Brawner` header verbatim per the MIT
license. Never delete or alter those headers.
