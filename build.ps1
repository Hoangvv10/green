# Build AutoPush.exe bang csc.exe co san trong Windows (.NET Framework 4.x), khong can cai them gi.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

$dist = Join-Path $here 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

& $csc /nologo /target:winexe /optimize+ `
    "/out:$dist\AutoPush.exe" `
    /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll `
    "$here\src\AutoPush.cs"
if ($LASTEXITCODE -ne 0) { throw "Build that bai" }

$cfg = Join-Path $dist 'config.json'
if (-not (Test-Path $cfg)) { Copy-Item "$here\config.example.json" $cfg }

Write-Host "OK -> $dist\AutoPush.exe"
