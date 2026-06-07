<#
.SYNOPSIS
  Stand-alone preview server for a SW2GZ-exported ROS 2 package.

.DESCRIPTION
  Serves a 3-file three.js viewer (tools/preview/index.html + preview.js) plus
  the package's meshes, and rewrites .urdf.xacro on the fly into plain URDF so
  the browser-side URDFLoader can fetch it without a ROS toolchain.

  Routes:
    /                          -> tools/preview/index.html
    /preview.js                -> tools/preview/preview.js
    /favicon.ico               -> 204
    /urdf/<pkg>.urdf           -> .urdf.xacro with <xacro:*> + xmlns:xacro stripped
    /meshes/<file>             -> -Root\meshes\<file>
    /urdf/<anything-else>      -> -Root\urdf\<file>     (e.g. inc/ros2_control.xacro)

  No external tooling needed (no python, no node) — pure PowerShell + .NET.

.EXAMPLE
  pwsh -NoProfile -File tools\preview\serve.ps1 `
       -Root  "C:\Users\arlik\Downloads\full_arm_ws\src\full_arm" `
       -Port  8080
#>
param(
    [string]$Root,
    [int]$Port = 8080,
    [string]$OpenWith = 'msedge'
)
if (-not $Root) { throw "Pass -Root <path-to-package>" }

$ErrorActionPreference = 'Stop'

# ─── Resolve paths ───────────────────────────────────────────────
$Root = (Resolve-Path -LiteralPath $Root).Path
# $PSScriptRoot is the directory containing this .ps1; works even when launched
# via `powershell -File`. Fall back to $MyInvocation if somehow null.
$here = $PSScriptRoot
if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
$indexHtml = Join-Path $here 'index.html'
$previewJs = Join-Path $here 'preview.js'

if (-not (Test-Path $indexHtml)) { throw "Missing $indexHtml" }
if (-not (Test-Path $previewJs)) { throw "Missing $previewJs" }
if (-not (Test-Path $Root))      { throw "Package root not found: $Root" }

# Find the .urdf.xacro inside <Root>\urdf — first match wins.
$xacroPath = Get-ChildItem -LiteralPath (Join-Path $Root 'urdf') -Filter '*.urdf.xacro' -File `
                | Select-Object -First 1 -ExpandProperty FullName
if (-not $xacroPath) { throw "No *.urdf.xacro under $Root\urdf" }

Write-Host "Preview server"            -ForegroundColor Cyan
Write-Host "  package root : $Root"
Write-Host "  urdf.xacro   : $xacroPath"
Write-Host "  port         : $Port"

# ─── Xacro → URDF strip (regex-based, geometry-only) ─────────────
# Removes:
#   - <xacro:include ... />
#   - <xacro:arg ... />
#   - <xacro:property ... />
#   - whole <xacro:macro ...>...</xacro:macro> and friends (multi-line)
#   - xmlns:xacro="..." attribute on the root <robot ...>
# Leaves the <link> + <joint> tree intact for URDFLoader.
function ConvertTo-Urdf {
    param([string]$XacroPath)

    $text = Get-Content -LiteralPath $XacroPath -Raw

    # Drop self-closing xacro tags first.
    $text = [System.Text.RegularExpressions.Regex]::Replace(
        $text, '<xacro:[a-zA-Z_:]+\b[^/>]*/>', '')
    # Drop paired xacro blocks (lazy, multi-line, DOTALL).
    $text = [System.Text.RegularExpressions.Regex]::Replace(
        $text, '<xacro:([a-zA-Z_:]+)\b[^>]*>.*?</xacro:\1>', '',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    # Strip xmlns:xacro attribute.
    $text = [System.Text.RegularExpressions.Regex]::Replace(
        $text, '\s+xmlns:xacro="[^"]*"', '')
    return $text
}

# ─── HttpListener ────────────────────────────────────────────────
$prefix = "http://localhost:$Port/"
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($prefix)
try { $listener.Start() }
catch {
    Write-Error "Failed to bind $prefix — is the port already in use?  $($_.Exception.Message)"
    return
}

Write-Host "Serving at $prefix  (Ctrl-C to stop)" -ForegroundColor Green

# Pop browser.
try { Start-Process $OpenWith $prefix | Out-Null }
catch { Write-Warning "Could not launch $OpenWith — open $prefix manually." }

function Resolve-ContentType {
    param([string]$Path)
    switch -Regex ($Path) {
        '\.html?$' { 'text/html; charset=utf-8'; break }
        '\.js$'    { 'application/javascript; charset=utf-8'; break }
        '\.urdf$'  { 'application/xml; charset=utf-8'; break }
        '\.xacro$' { 'application/xml; charset=utf-8'; break }
        '\.xml$'   { 'application/xml; charset=utf-8'; break }
        '\.stl$'   { 'model/stl'; break }
        '\.dae$'   { 'model/vnd.collada+xml'; break }
        '\.png$'   { 'image/png'; break }
        '\.svg$'   { 'image/svg+xml'; break }
        default    { 'application/octet-stream' }
    }
}

function Send-Bytes {
    param($Resp, [byte[]]$Bytes, [string]$Ctype, [int]$Status = 200)
    $Resp.StatusCode = $Status
    $Resp.ContentType = $Ctype
    $Resp.ContentLength64 = $Bytes.Length
    # CORS so the file: drop-down case (open index.html directly) also works.
    $Resp.Headers.Add('Access-Control-Allow-Origin', '*')
    $Resp.OutputStream.Write($Bytes, 0, $Bytes.Length)
    $Resp.OutputStream.Close()
}

function Send-Text {
    param($Resp, [string]$Text, [string]$Ctype, [int]$Status = 200)
    Send-Bytes $Resp ([System.Text.Encoding]::UTF8.GetBytes($Text)) $Ctype $Status
}

function Send-File {
    param($Resp, [string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        Send-Text $Resp "404: $Path" 'text/plain; charset=utf-8' 404
        return
    }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    Send-Bytes $Resp $bytes (Resolve-ContentType $Path)
}

# Main request loop.
try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $req = $ctx.Request
        $resp = $ctx.Response
        $url = $req.Url.AbsolutePath

        try {
            switch -Regex ($url) {
                '^/$' {
                    Send-File $resp $indexHtml
                    break
                }
                '^/preview\.js$' {
                    Send-File $resp $previewJs
                    break
                }
                '^/favicon\.ico$' {
                    $resp.StatusCode = 204
                    $resp.OutputStream.Close()
                    break
                }
                '^/urdf/[^/]+\.urdf$' {
                    # Synthesize URDF from the .xacro on every request — cheap
                    # and means edits in SolidWorks → re-export → just refresh.
                    $urdf = ConvertTo-Urdf -XacroPath $xacroPath
                    Send-Text $resp $urdf 'application/xml; charset=utf-8'
                    break
                }
                '^/(meshes|urdf|worlds|config|launch)/' {
                    $rel = $url.TrimStart('/').Replace('/', [System.IO.Path]::DirectorySeparatorChar)
                    $local = Join-Path $Root $rel
                    Send-File $resp $local
                    break
                }
                default {
                    Send-Text $resp "404: $url" 'text/plain; charset=utf-8' 404
                }
            }
            Write-Host ("  {0,3} {1}" -f $resp.StatusCode, $url) -ForegroundColor DarkGray
        }
        catch {
            Write-Warning "Handler threw for $url`: $($_.Exception.Message)"
            try { Send-Text $resp "500: $($_.Exception.Message)" 'text/plain; charset=utf-8' 500 } catch {}
        }
    }
}
finally {
    if ($listener.IsListening) { $listener.Stop() }
    $listener.Close()
    Write-Host "Stopped." -ForegroundColor Yellow
}
