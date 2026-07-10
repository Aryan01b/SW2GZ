# Building SW2GZ

SW2GZ ships as a Windows SolidWorks COM add-in DLL (.NET Framework 4.8)
plus an Inno Setup installer. Building requires Windows + SolidWorks API
references. **A full SolidWorks install is NOT required to build** — the
free SolidWorks API SDK provides everything the compiler needs. SolidWorks
itself is only needed at runtime (when the user installs and runs the add-in).

## Prerequisites

1. **Windows 10/11** (x64).
2. **Visual Studio 2022 Community** (free) with these workloads:
   - .NET desktop development
   - Desktop development with C++ (provides MSBuild + Windows 10 SDK that AxImp.exe needs)
3. **.NET Framework 4.8 Developer Pack**
   https://dotnet.microsoft.com/download/dotnet-framework/net48
4. **SolidWorks API SDK** (free, no SW license required to build):
   https://www.solidworks.com/sw/support/api-support.htm
   — registers `SolidWorks.Interop.sldworks.dll`, `SolidWorks.Interop.swconst.dll`,
   `SolidWorks.Interop.swpublished.dll`, `solidworkstools.dll` in
   `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\` (the location the csproj
   references).
   Alternatively install **SolidWorks 2022 or newer** (full product) — same DLLs.
5. **Inno Setup 6** (for building the installer):
   https://jrsoftware.org/isdl.php

## Build the add-in DLL

```powershell
cd D:\path\to\sw2gz
msbuild SW2GZ.sln /t:Restore;Build /p:Configuration=Release /v:minimal
```

Output: `SW2GZ\bin\Release\SW2GZ.dll` plus referenced DLLs.

## Run the pure-C# writer tests (no SolidWorks needed)

```powershell
dotnet test Test\SW2GZ.Writers.Test.csproj --filter "Category=Unit"
```

These verify the ROS 2 / Gz writers + golden file regression across all
three target profiles. All tests must stay green.

## Build the installer

`installer/SW2GZ.iss` takes its version from the `MyAppVersion` preprocessor
define, not a hardcoded value — pass it explicitly so the output filename and
AppVersion match the git tag you're releasing:

```powershell
$version = (git describe --tags --abbrev=0) -replace '^v', ''
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "/DMyAppVersion=$version" installer\SW2GZ.iss
```

Output: `installer\Output\SW2GZ-Setup-<version>.exe`. Omitting
`/DMyAppVersion` falls back to whatever default is hardcoded in the `.iss`
file, which may lag behind the latest tag.

## Install the add-in into SolidWorks

1. Right-click the downloaded `SW2GZ-Setup-<version>.exe` → **Run as administrator**.
2. Default install path: `C:\Program Files\SW2GZ\`.
3. The installer runs `regasm /codebase SW2GZ.dll` automatically (registers
   the COM add-in under `HKLM\SOFTWARE\SolidWorks\AddIns\{34fad620-2a46-4ba6-9f5f-1dfefde894c7}`).
4. Open SolidWorks → **Tools → Add-Ins…** → tick **SW2GZ**.
5. **SW2GZ** menu appears in SolidWorks. Open an assembly → SW2GZ → Export.

SW2GZ installs **side-by-side** with the original SW2URDF add-in (separate
COM GUID + ProgId `SwAddin.SW2GZ.Addin` + registry key). Both can be enabled
at the same time.

## Uninstall

Windows Settings → Apps → SW2GZ → Uninstall. The uninstaller runs
`regasm /u` to deregister the COM add-in.

## Troubleshooting

### `AxImp.exe` not found during build
Install the Windows 10 SDK via Visual Studio Installer (Desktop development
with C++ workload pulls it in).

### `Resources.resx` empty-file build error
SW2GZ v1.0 ships a valid v2 schema `Resources.resx` so this should not
occur. If it does (e.g. after a manual reset), open the SW2GZ project in
VS → Properties → Resources tab → create a new file.

### `SolidWorks.Interop.sldworks.dll not found`
The csproj `HintPath` points at `C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\`
(the SW API SDK install location). Verify the DLLs are there. If you
installed the SDK to a different path, edit `SW2GZ\SW2GZ.csproj` and update
the four `<HintPath>` lines that contain `SOLIDWORKS Corp`.

### `regasm` says "Type library exporter could not load type"
You're on a machine without the .NET Framework 4.8 runtime. Install the
.NET Framework 4.8 redistributable (https://dotnet.microsoft.com/download/dotnet-framework/net48).
