# Third-Party Licenses

SW2GZ is a derivative work and bundles several third-party components.
All are redistributed under permissive licenses. This file lists each one
and its license, as required (and as good practice) for redistribution.

SW2GZ itself is licensed under the MIT License — see [LICENSE](LICENSE).

---

## Upstream project (source basis)

| Component | Author | License |
|---|---|---|
| **solidworks_urdf_exporter** | Stephen Brawner | MIT |

SW2GZ is a modernized derivative of `ros/solidworks_urdf_exporter`
(Copyright (c) 2015-2020 Stephen Brawner). Source files inherited from
that project retain their original MIT copyright headers verbatim. The
upstream MIT permission notice is preserved in [LICENSE](LICENSE).

---

## Runtime dependencies (redistributed in the installer)

| Package | Version | License |
|---|---|---|
| CsvHelper | 7.1.1 | MS-PL / Apache-2.0 (dual) |
| MathNet.Numerics | 4.7.0 | MIT |
| log4net | — | Apache-2.0 |
| System.Runtime.CompilerServices.Unsafe | 4.5.0 | MIT |
| System.Threading.Tasks.Extensions | 4.5.1 | MIT |

---

## Test-only dependencies (NOT redistributed)

These are used only by the test projects and are explicitly excluded from
the installer ([installer/SW2GZ.iss](installer/SW2GZ.iss)).

| Package | Version | License |
|---|---|---|
| xunit (+ runners/extensibility) | 2.4.1 | Apache-2.0 |
| Moq | 4.10.1 | BSD-3-Clause |
| Castle.Core | 4.3.1 | Apache-2.0 |
| Microsoft.CodeAnalysis.FxCopAnalyzers | 2.9.6 | Apache-2.0 |
| Microsoft.VisualStudio.TestPlatform | 14.0.0.0 | MIT |

---

## NOT redistributed — proprietary

| Component | Owner | Notes |
|---|---|---|
| `SolidWorks.Interop.*.dll` | Dassault Systèmes | Embedded as interop types (`EmbedInteropTypes`); not shipped as files. |
| `solidworkstools.dll` | Dassault Systèmes | Excluded from build output and installer. Already present on any machine with SolidWorks installed. |

"SolidWorks" is a trademark of Dassault Systèmes. SW2GZ is an independent
project and is not affiliated with or endorsed by Dassault Systèmes. The
name is used only to describe interoperability.
