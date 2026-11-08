# build.ps1
#   .\build.ps1                      -> build dist\AutoPush.exe
#   .\build.ps1 -Pack <SourceDir> -Repo user/repo [-Branch main] [-Setup script]
#                                    -> dong goi source + token -> dist\Bootstrap.exe
# Dung csc.exe co san cua Windows (.NET Framework 4.x), khong can cai them.
param(
    [string]$Pack,
    [string]$Repo,
    [string]$Branch = 'main',
    [string]$Setup
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $here 'dist'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
New-Item -ItemType Directory -Force $dist | Out-Null

function Build-AutoPush {
    & $csc /nologo /target:winexe /optimize+ `
        "/out:$dist\AutoPush.exe" `
        /r:System.Web.Extensions.dll `
        "$here\src\AutoPush.cs"
    if ($LASTEXITCODE -ne 0) { throw "Build AutoPush that bai" }
    if (-not (Test-Path "$dist\config.json")) { Copy-Item "$here\config.example.json" "$dist\config.json" }
    Write-Host "OK -> $dist\AutoPush.exe"
}

# ---- che do mac dinh: chi build AutoPush ----
if (-not $Pack) { Build-AutoPush; return }

# ---- che do dong goi: tao Bootstrap.exe tu giai nen ----
if (-not $Repo) { throw "Can -Repo user/repo khi -Pack" }
Build-AutoPush   # dam bao co AutoPush.exe moi nhat

$src = (Resolve-Path $Pack).Path
if (-not (Test-Path $src -PathType Container)) { throw "SourceDir khong ton tai: $src" }

# token lay tu dist\config.json
$token = ''
if (Test-Path "$dist\config.json") {
    $token = (Get-Content "$dist\config.json" -Raw | ConvertFrom-Json).token
}
if (-not $token -or $token -like '*xxx*') {
    Write-Warning "config.json chua co token hop le -> Bootstrap se khong push duoc cho den khi dien token."
}

# dung thu muc staging: AutoPush.exe + config.json + source\
$stage = Join-Path ([IO.Path]::GetTempPath()) ("appack_" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $stage | Out-Null
try {
    Copy-Item "$dist\AutoPush.exe" $stage
    $srcOut = Join-Path $stage 'source'
    # Copy source, bo .git/node_modules cho gon va LOAI config.json/log de khong day token len GitHub
    robocopy $src $srcOut /E /XD .git node_modules bin obj .vs pyembed /XF config.json autopush.log fun-log.txt payload.zip Bootstrap.exe | Out-Null

    # Nhung Python portable (neu co thu muc pyembed canh build.ps1) -> dat o goc, KHONG push len GitHub
    $runWith = @('py "%SELF%\source\fun.py" || python "%SELF%\source\fun.py"')
    if (Test-Path "$here\pyembed\python.exe") {
        robocopy "$here\pyembed" (Join-Path $stage 'pyembed') /E | Out-Null
        $runWith = @('"%SELF%\pyembed\python.exe" "%SELF%\source\fun.py"')
        Write-Host "Da nhung Python portable (pyembed)."
    } else {
        Write-Warning "Khong co pyembed -> fun.py chi chay neu may phu da cai Python."
    }

    $cfg = [ordered]@{
        token               = $token
        authorName          = ''
        authorEmail         = ''
        startupDelaySeconds = 30
        maxFileSizeMB       = 50
        exclude             = @('node_modules','bin','obj','.vs','__pycache__','*.log','config.json','Bootstrap.exe','pyembed')
        runWith             = $runWith
        projects            = @(@{ path = '%SELF%\source'; repo = $Repo; branch = $Branch })
    }
    ($cfg | ConvertTo-Json -Depth 6) | Set-Content -Encoding utf8 (Join-Path $stage 'config.json')

    if ($Setup) {
        if (-not (Test-Path $Setup)) { throw "Setup script khong ton tai: $Setup" }
        $ext = [IO.Path]::GetExtension($Setup)
        Copy-Item $Setup (Join-Path $stage ("autosetup" + $ext))
    }

    # nen staging -> payload.zip
    $zip = Join-Path $dist 'payload.zip'
    if (Test-Path $zip) { Remove-Item $zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)

    # bien dich Bootstrap.exe, nhung payload.zip lam resource ten "payload"
    & $csc /nologo /target:winexe /optimize+ `
        "/out:$dist\Bootstrap.exe" `
        /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
        "/resource:$zip,payload" `
        "$here\src\Bootstrap.cs"
    if ($LASTEXITCODE -ne 0) { throw "Build Bootstrap that bai" }
    Remove-Item $zip -ErrorAction SilentlyContinue

    Write-Host "OK -> $dist\Bootstrap.exe  (repo: $Repo, branch: $Branch)"
    if ($token -and $token -notlike '*xxx*') { Write-Warning "Bootstrap.exe CHUA token - dung chia se file nay ra ngoai." }
}
finally {
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
}
