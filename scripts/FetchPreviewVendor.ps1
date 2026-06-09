# Copyright (c) 2026 Aryan Arlikar. MIT License - see CONTRIBUTING.md.
#
# Pulls three.js + urdf-loader source files we ship with the in-addin
# preview, pinning exact versions so air-gapped SOLIDWORKS machines can
# render the preview without ever touching the network.
#
# Run this once per version bump - the vendored files are committed to
# the repo. The addin install pipes them through:
#
#   SW2GZ/UI/PreviewWeb/vendor/   <- source (this script writes here)
#       -> bin/Release/preview/vendor/   <- csproj <Content> copy
#       -> {app}/preview/vendor/         <- installer [Files]
#       -> served by PreviewServer at runtime via importmap rewiring
#
# Versions are pinned to match the preview index.html importmap. Bumping
# requires editing both this script and index.html.
$ErrorActionPreference = 'Stop'

$THREE_VER = '0.160.0'
$URDF_VER  = '0.12.7'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\SW2GZ\UI\PreviewWeb\vendor'))
Write-Host "vendor root: $root"

# Files: (sourceUrl, relativeDest under $root)
$files = @(
    @("https://unpkg.com/three@$THREE_VER/build/three.module.js",
      'three\build\three.module.js'),
    @("https://unpkg.com/three@$THREE_VER/examples/jsm/controls/OrbitControls.js",
      'three\examples\jsm\controls\OrbitControls.js'),
    @("https://unpkg.com/three@$THREE_VER/examples/jsm/loaders/STLLoader.js",
      'three\examples\jsm\loaders\STLLoader.js'),
    @("https://unpkg.com/three@$THREE_VER/examples/jsm/loaders/ColladaLoader.js",
      'three\examples\jsm\loaders\ColladaLoader.js'),
    # ColladaLoader internally imports ../loaders/TGALoader.js - same dir.
    @("https://unpkg.com/three@$THREE_VER/examples/jsm/loaders/TGALoader.js",
      'three\examples\jsm\loaders\TGALoader.js'),
    @("https://unpkg.com/urdf-loader@$URDF_VER/src/URDFLoader.js",
      'urdf-loader\src\URDFLoader.js'),
    # URDFLoader.js imports './URDFClasses.js' - same dir.
    @("https://unpkg.com/urdf-loader@$URDF_VER/src/URDFClasses.js",
      'urdf-loader\src\URDFClasses.js')
)

$total = 0
foreach ($pair in $files) {
    $url = $pair[0]
    $dest = Join-Path $root $pair[1]
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    Write-Host ("  fetching {0,-60} -> {1}" -f ($url -replace 'https://unpkg.com/', ''), ($pair[1]))
    Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
    $total += (Get-Item $dest).Length
}

Write-Host ""
Write-Host ("vendor total: {0:N0} bytes ({1:N1} KB)" -f $total, ($total / 1KB))
Write-Host "done. Commit the contents of SW2GZ\UI\PreviewWeb\vendor\."
