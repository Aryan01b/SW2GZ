<#
.SYNOPSIS
  Stand-alone server for the REAL production preview page
  (SW2GZ/UI/PreviewWeb/index.html), for iterating on the viewer UI
  without SolidWorks running.

.DESCRIPTION
  Mirrors PreviewServer.cs's route contract exactly (SW2GZ/URDFExport/
  PreviewServer.cs) against a sample exported package on disk, so the
  same index.html + vendor/ that ships in the addin renders unmodified:

    GET /                  -> <AssetsDir>/index.html
    GET /vendor/<path>     -> <AssetsDir>/vendor/<path>
    GET /robot.urdf        -> <Root>/urdf/*.urdf.xacro, xacro tags stripped
    GET /meshes/<name>     -> <Root>/meshes/<name>
    GET /joint_states      -> {} (no live SW session to sample from)

.EXAMPLE
  pwsh -NoProfile -File tools\preview\serve-web.ps1 `
       -Root "C:\aryan\SW2GZ\examples\full_arm_ws\src\full_arm" -Port 8090
#>
param(
    [string]$Root      = "$PSScriptRoot\..\..\examples\full_arm_ws\src\full_arm",
    [string]$AssetsDir = "$PSScriptRoot\..\..\SW2GZ\UI\PreviewWeb",
    [int]$Port         = 8090,
    [string]$OpenWith  = 'msedge'
)
$ErrorActionPreference = 'Stop'
$Root      = (Resolve-Path -LiteralPath $Root).Path
$AssetsDir = (Resolve-Path -LiteralPath $AssetsDir).Path

$indexHtml = Join-Path $AssetsDir 'index.html'
if (-not (Test-Path $indexHtml)) { throw "Missing $indexHtml" }

$xacroPath = Get-ChildItem -LiteralPath (Join-Path $Root 'urdf') -Filter '*.urdf.xacro' -File `
                | Select-Object -First 1 -ExpandProperty FullName
if (-not $xacroPath) { throw "No *.urdf.xacro under $Root\urdf" }

Write-Host "Preview-web server (real production page)" -ForegroundColor Cyan
Write-Host "  assets   : $AssetsDir"
Write-Host "  pkg root : $Root"
Write-Host "  urdf     : $xacroPath"
Write-Host "  port     : $Port"

function ConvertTo-Urdf {
    param([string]$XacroPath)
    $text = Get-Content -LiteralPath $XacroPath -Raw
    $text = [System.Text.RegularExpressions.Regex]::Replace($text, '<xacro:[a-zA-Z_:]+\b[^/>]*/>', '')
    $text = [System.Text.RegularExpressions.Regex]::Replace(
        $text, '<xacro:([a-zA-Z_:]+)\b[^>]*>.*?</xacro:\1>', '',
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $text = [System.Text.RegularExpressions.Regex]::Replace($text, '\s+xmlns:xacro="[^"]*"', '')
    return $text
}

function MimeFor {
    param([string]$Path)
    switch ([System.IO.Path]::GetExtension($Path).ToLowerInvariant()) {
        '.dae'  { 'model/vnd.collada+xml'; break }
        '.stl'  { 'application/sla'; break }
        '.html' { 'text/html; charset=utf-8'; break }
        '.js'   { 'text/javascript; charset=utf-8'; break }
        '.mjs'  { 'text/javascript; charset=utf-8'; break }
        '.css'  { 'text/css; charset=utf-8'; break }
        '.json' { 'application/json; charset=utf-8'; break }
        '.map'  { 'application/json; charset=utf-8'; break }
        '.png'  { 'image/png'; break }
        default { 'application/octet-stream' }
    }
}

function Send-Bytes { param($Resp, [byte[]]$Bytes, [string]$Ctype, [int]$Status = 200)
    $Resp.StatusCode = $Status; $Resp.ContentType = $Ctype; $Resp.ContentLength64 = $Bytes.Length
    $Resp.OutputStream.Write($Bytes, 0, $Bytes.Length); $Resp.OutputStream.Close()
}
function Send-Text  { param($Resp, [string]$Text, [string]$Ctype, [int]$Status = 200)
    Send-Bytes $Resp ([System.Text.Encoding]::UTF8.GetBytes($Text)) $Ctype $Status
}
function Send-File  { param($Resp, [string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { Send-Text $Resp "404: $Path" 'text/plain; charset=utf-8' 404; return }
    Send-Bytes $Resp ([System.IO.File]::ReadAllBytes($Path)) (MimeFor $Path)
}

$prefix = "http://127.0.0.1:$Port/"
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($prefix)
try { $listener.Start() } catch { Write-Error "Failed to bind $prefix : $($_.Exception.Message)"; return }

Write-Host "Serving at $prefix  (Ctrl-C to stop)" -ForegroundColor Green
try { Start-Process $OpenWith $prefix | Out-Null } catch { Write-Warning "Open $prefix manually." }

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $req = $ctx.Request; $resp = $ctx.Response
        $path = $req.Url.AbsolutePath.TrimStart('/')
        try {
            if ($path -eq '' -or $path -eq 'index.html') { Send-File $resp $indexHtml }
            elseif ($path -eq 'scaffold.html') { Send-File $resp (Join-Path $PSScriptRoot 'design-scaffold.html') }
            elseif ($path -eq 'robot.urdf') { Send-Text $resp (ConvertTo-Urdf $xacroPath) 'application/xml; charset=utf-8' }
            elseif ($path -eq 'joint_states') { Send-Text $resp '{}' 'application/json; charset=utf-8' }
            elseif ($path -like 'meshes/*') {
                $name = [System.IO.Path]::GetFileName($path.Substring('meshes/'.Length))
                Send-File $resp (Join-Path $Root "meshes\$name")
            }
            elseif ($path -like 'vendor/*') {
                $rel = $path.Substring('vendor/'.Length).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
                Send-File $resp (Join-Path (Join-Path $AssetsDir 'vendor') $rel)
            }
            else { Send-Text $resp "404: $path" 'text/plain; charset=utf-8' 404 }
            Write-Host ("  {0,3} {1}" -f $resp.StatusCode, $path) -ForegroundColor DarkGray
        } catch {
            Write-Warning "Handler threw for $path`: $($_.Exception.Message)"
            try { Send-Text $resp "500: $($_.Exception.Message)" 'text/plain; charset=utf-8' 500 } catch {}
        }
    }
} finally {
    if ($listener.IsListening) { $listener.Stop() }
    $listener.Close()
    Write-Host "Stopped." -ForegroundColor Yellow
}
