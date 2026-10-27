# build_release.ps1 - Brain Platform one-click release build
# Usage: run from brain-platform root directory
#   powershell -ExecutionPolicy Bypass -File build_release.ps1
#
# Output: dist\BrainPlatform-Release\
#   BrainPlatform.exe   (WPF single-file executable)
#   backend\            (PyInstaller backend)
#   storage\            (legacy portable data location; normal install uses %LOCALAPPDATA%)

param(
    [string]$OutputDirectory = "",
    [string]$SdkSourcePath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
$Root          = $PSScriptRoot
$BackendDir    = Join-Path $Root "backend"
$DesktopDir    = Join-Path $Root "desktop-client"
$OutDir        = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $Root "dist\BrainPlatform-Release"
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$BackendOutDir = Join-Path $OutDir "backend"
$SdkOutDir     = Join-Path $OutDir "sdk"
$StorageOutDir = Join-Path $OutDir "storage"

$VenvPython    = Join-Path $BackendDir ".venv\Scripts\python.exe"
$EntryScript   = Join-Path $BackendDir "app\server_entry.py"
$CsprojPath    = Join-Path $DesktopDir "BrainPlatform.Desktop\BrainPlatform.Desktop.csproj"
$DefaultSdk    = Join-Path $Root "..\ANT-Amplify\Ant SDK\windows\64bit\eego-SDK.dll"
$SdkPath       = if ([string]::IsNullOrWhiteSpace($SdkSourcePath)) { $DefaultSdk } else { [System.IO.Path]::GetFullPath($SdkSourcePath) }

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Step([string]$msg) {
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
}

function Require([string]$path, [string]$hint) {
    if (-not (Test-Path $path)) {
        Write-Host "ERROR: Not found: $path" -ForegroundColor Red
        Write-Host "       $hint" -ForegroundColor Yellow
        exit 1
    }
}

# ---------------------------------------------------------------------------
# Pre-flight checks
# ---------------------------------------------------------------------------
Step "Pre-flight checks"

Require $VenvPython  "Create venv first: python -m venv backend\.venv then install deps"
Require $EntryScript "backend\app\server_entry.py is missing"
Require $CsprojPath  "desktop-client project file not found"
Require $SdkPath     "Provide -SdkSourcePath with the licensed x64 eego-SDK.dll"

# Check / install PyInstaller into venv
& $VenvPython -m PyInstaller --version 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Step "Installing PyInstaller into venv"
    & $VenvPython -m pip install pyinstaller
    if ($LASTEXITCODE -ne 0) { Write-Host "pip install failed" -ForegroundColor Red; exit 1 }
}

Write-Host "Python : $VenvPython" -ForegroundColor Green
Write-Host "Output : $OutDir"     -ForegroundColor Green

# ---------------------------------------------------------------------------
# Clean previous build
# ---------------------------------------------------------------------------
Step "Cleaning previous output"
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir        | Out-Null
New-Item -ItemType Directory -Force -Path $SdkOutDir    | Out-Null
New-Item -ItemType Directory -Force -Path $StorageOutDir | Out-Null
New-Item -ItemType Directory -Force -Path "$StorageOutDir\recordings" | Out-Null
New-Item -ItemType Directory -Force -Path "$StorageOutDir\artifacts"  | Out-Null
Write-Host "Done" -ForegroundColor Green

Step "Copy licensed ANT/eego SDK"
Copy-Item -LiteralPath $SdkPath -Destination (Join-Path $SdkOutDir "eego-SDK.dll") -Force
Write-Host "SDK packaged -> $SdkOutDir\eego-SDK.dll" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 1: PyInstaller - package Python backend
# ---------------------------------------------------------------------------
Step "Step 1/3 - PyInstaller: package Python backend"

$PyiWork = Join-Path $Root "dist\_pyibuild"
$PyiModuleWork = Join-Path $PyiWork "brain_backend"
New-Item -ItemType Directory -Force -Path $PyiModuleWork | Out-Null

Push-Location $BackendDir
try {
    & $VenvPython -m PyInstaller `
        --onedir `
        --name brain_backend `
        --distpath $BackendOutDir `
        --workpath $PyiWork `
        --noconfirm `
        --clean `
        --log-level WARN `
        --collect-submodules app `
        --collect-submodules mne `
        --collect-data app `
        --collect-data mne `
        --hidden-import app.main `
        $EntryScript
    if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed" }
} finally {
    Pop-Location
}

# PyInstaller writes to <distpath>\brain_backend\ - flatten one level
$PyiOut = Join-Path $BackendOutDir "brain_backend"
if (Test-Path $PyiOut) {
    Get-ChildItem $PyiOut | Move-Item -Destination $BackendOutDir -Force
    Remove-Item $PyiOut -Recurse -Force
}

$BackendExe = Join-Path $BackendOutDir "brain_backend.exe"
Require $BackendExe "PyInstaller did not produce the backend executable"

Write-Host "Backend packaged -> $BackendOutDir" -ForegroundColor Green

Step "Backend smoke test"
$SmokePort = 18000
$SmokeInfo = [System.Diagnostics.ProcessStartInfo]::new()
$SmokeInfo.FileName = $BackendExe
$SmokeInfo.WorkingDirectory = $BackendOutDir
$SmokeInfo.UseShellExecute = $false
$SmokeInfo.CreateNoWindow = $true
$SmokeInfo.RedirectStandardOutput = $true
$SmokeInfo.RedirectStandardError = $true
$SmokeInfo.Environment["BRAIN_BACKEND_PORT"] = "$SmokePort"
$BackendProcess = [System.Diagnostics.Process]::Start($SmokeInfo)
$BackendStdoutTask = $BackendProcess.StandardOutput.ReadToEndAsync()
$BackendStderrTask = $BackendProcess.StandardError.ReadToEndAsync()
try {
    $healthy = $false
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        if ($BackendProcess.HasExited) {
            $BackendProcess.WaitForExit()
            $stdout = $BackendStdoutTask.GetAwaiter().GetResult()
            $stderr = $BackendStderrTask.GetAwaiter().GetResult()
            throw "Packaged backend exited before becoming healthy (exit code $($BackendProcess.ExitCode)).`nstdout:`n$stdout`nstderr:`n$stderr"
        }
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$SmokePort/api/health" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $healthy = $true; break }
        } catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $healthy) {
        if (-not $BackendProcess.HasExited) {
            $BackendProcess.Kill()
            $BackendProcess.WaitForExit()
        }
        $stdout = $BackendStdoutTask.GetAwaiter().GetResult()
        $stderr = $BackendStderrTask.GetAwaiter().GetResult()
        throw "Packaged backend health check timed out after 20 seconds.`nstdout:`n$stdout`nstderr:`n$stderr"
    }
    Write-Host "Backend health check passed" -ForegroundColor Green
} finally {
    if (-not $BackendProcess.HasExited) {
        Stop-Process -Id $BackendProcess.Id -Force
        $BackendProcess.WaitForExit()
    }
}

# ---------------------------------------------------------------------------
# Step 2: dotnet publish - WPF desktop client (single-file, self-contained)
# ---------------------------------------------------------------------------
Step "Step 2/3 - dotnet publish: WPF desktop client"

dotnet publish $CsprojPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    --output $OutDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "WPF published -> $OutDir" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Step 3: Cleanup temp files
# ---------------------------------------------------------------------------
Step "Step 3/3 - Cleanup"

if (Test-Path $PyiWork) { Remove-Item -Recurse -Force $PyiWork }
Get-ChildItem $OutDir -Filter "*.pdb" | Remove-Item -Force

Write-Host "Done" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Step "Build complete!"
Write-Host ""
Write-Host "Release folder: $OutDir" -ForegroundColor Yellow
Write-Host ""
Write-Host "Contents:" -ForegroundColor White
Get-ChildItem $OutDir | Format-Table Name, Length, LastWriteTime
