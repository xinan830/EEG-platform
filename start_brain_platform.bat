
@echo off
setlocal EnableExtensions

rem =====================================================
rem Brain Platform - Python Backend Development Launcher
rem Only run Python source code, never packaged EXE.
rem =====================================================

set "PROJECT_ROOT=%~dp0"
set "BACKEND_ROOT=%PROJECT_ROOT%backend"
set "BACKEND_PYTHON=%BACKEND_ROOT%\.venv\Scripts\python.exe"

rem Enable on-demand PSD analysis
set "BRAIN_ENABLE_ON_DEMAND_PSD=1"

title Brain Platform - Python Backend

echo ==========================================
echo    Brain Platform Python Backend
echo ==========================================
echo.

rem Step 1: Verify Python environment
if not exist "%BACKEND_PYTHON%" (
    echo [ERROR] Python virtual environment not found:
    echo %BACKEND_PYTHON%
    pause
    exit /b 1
)

rem Step 2: Verify backend source code
if not exist "%BACKEND_ROOT%\app\main.py" (
    echo [ERROR] Backend source code not found:
    echo %BACKEND_ROOT%\app\main.py
    pause
    exit /b 1
)

rem Step 3: Stop process listening on port 8000
echo [1/3] Checking port 8000...

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$pids8000 = @(Get-NetTCPConnection -LocalPort 8000 -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique); foreach ($procId in $pids8000) { if ($procId -gt 0 -and $procId -ne $PID) { Write-Host ('Stopping PID: ' + $procId); Stop-Process -Id $procId -Force -ErrorAction Stop } }"

if errorlevel 1 (
    echo [ERROR] Failed to release port 8000.
    pause
    exit /b 1
)

rem Step 4: Wait for port release
echo [2/3] Waiting for port release...

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$free = $false; for ($i = 0; $i -lt 20; $i++) { if (-not (Get-NetTCPConnection -LocalPort 8000 -State Listen -ErrorAction SilentlyContinue)) { $free = $true; break }; Start-Sleep -Milliseconds 250 }; if (-not $free) { exit 1 }"

if errorlevel 1 (
    echo [ERROR] Port 8000 is still occupied.
    pause
    exit /b 1
)

rem Step 5: Start Python source backend
echo [3/3] Starting Python backend...
echo.
echo Backend mode : SOURCE CODE
echo Backend path : %BACKEND_ROOT%
echo Backend URL  : http://127.0.0.1:8000
echo On-demand PSD: ENABLED
echo.
echo Close this window to stop the backend.
echo ==========================================
echo.

cd /d "%BACKEND_ROOT%"

"%BACKEND_PYTHON%" -m uvicorn app.main:app --host 127.0.0.1 --port 8000

set "EXIT_CODE=%ERRORLEVEL%"

echo.
echo Backend stopped. Exit code: %EXIT_CODE%
pause

endlocal & exit /b %EXIT_CODE%
