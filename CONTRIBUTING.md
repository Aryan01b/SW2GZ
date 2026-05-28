# Contributing to SW2GZ

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

## Branching

- `main` — stable, releases tagged `v1.0.0`, `v1.1.0`, …
- Feature branches: `feat/<short-name>`
- Bug branches: `fix/<short-name>`

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

Pure-C# writer tests run via `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "Category=Unit"`. SolidWorks-dependent integration tests require a workstation with SolidWorks installed and run via the `TestRunner/` standalone exe (see `TestRunner/README.md`).
