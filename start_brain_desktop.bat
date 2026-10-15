@echo off
setlocal EnableExtensions

rem One-click launcher: one visible backend terminal and one WPF frontend window.
set "PROJECT_ROOT=%~dp0"
set "BACKEND_ROOT=%PROJECT_ROOT%backend"
set "BACKEND_PYTHON=%BACKEND_ROOT%\.venv\Scripts\python.exe"
set "DESKTOP_EXE=%PROJECT_ROOT%desktop-client\BrainPlatform.Desktop\bin\Release\net10.0-windows\BrainPlatform.Desktop.exe"
set "PORTABLE_EXE=%PROJECT_ROOT%dist\BrainPlatform-Release\BrainPlatform.Desktop.exe"
set "BACKEND_STARTED=0"
set "BACKEND_WINDOW_TITLE=Brain Platform Backend %RANDOM%"
set "PORTABLE_BACKEND_EXE=%PROJECT_ROOT%dist\BrainPlatform-Release\backend\brain_backend.exe"
set "PORTABLE_BACKEND_ROOT=%PROJECT_ROOT%dist\BrainPlatform-Release"
set "BACKEND_MODE=python"

rem PSD dynamic analysis acceptance mode. Both processes must receive the flag.
set "BRAIN_ENABLE_ON_DEMAND_PSD=1"
set "BRAIN_WPF_ON_DEMAND_PSD=1"
echo PSD on-demand analysis: ENABLED

if not exist "%DESKTOP_EXE%" (
    if exist "%PORTABLE_EXE%" (
        set "DESKTOP_EXE=%PORTABLE_EXE%"
        set "BACKEND_MODE=portable"
    ) else (
        if not exist "%BACKEND_PYTHON%" (
            echo Backend Python environment was not found:
            echo %BACKEND_PYTHON%
            echo Run the backend setup first, or build the portable release package.
            pause
            exit /b 1
        )
        if not exist "%BACKEND_ROOT%\app\main.py" (
            echo Backend entry point was not found:
            echo %BACKEND_ROOT%\app\main.py
            pause
            exit /b 1
        )
        echo WPF Release executable was not found. Building it now...
        dotnet build "%PROJECT_ROOT%desktop-client\BrainPlatform.Desktop\BrainPlatform.Desktop.csproj" -c Release --no-restore
        if errorlevel 1 (
            echo WPF build failed.
            pause
            exit /b 1
        )
    )
)

if "%BACKEND_MODE%"=="python" (
    if not exist "%BACKEND_PYTHON%" (
        echo Backend Python environment was not found:
        echo %BACKEND_PYTHON%
        pause
        exit /b 1
    )
    if not exist "%BACKEND_ROOT%\app\main.py" (
        echo Backend entry point was not found:
        echo %BACKEND_ROOT%\app\main.py
        pause
        exit /b 1
    )
) else if not exist "%PORTABLE_BACKEND_EXE%" (
    echo Portable backend executable was not found:
    echo %PORTABLE_BACKEND_EXE%
    pause
    exit /b 1
)

if not exist "%PROJECT_ROOT%storage" mkdir "%PROJECT_ROOT%storage"

echo Checking local backend...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "try { $r = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:8000/api/health' -TimeoutSec 2; if ($r.StatusCode -ne 200) { exit 1 } } catch { exit 1 }"
if errorlevel 1 goto start_backend
echo Backend is already running; checking whether it belongs to this project...
goto inspect_existing_backend

:inspect_existing_backend
set "EXISTING_BACKEND_PID="
for /f "usebackq delims=" %%P in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$root = [IO.Path]::GetFullPath('%PROJECT_ROOT%'); $owner = Get-NetTCPConnection -LocalPort 8000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess; Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -eq $owner } | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($root) } | Where-Object { $_.CommandLine -like '*brain_backend.exe*' -or $_.CommandLine -like '*app.main:app*' } | Select-Object -ExpandProperty ProcessId"`) do set "EXISTING_BACKEND_PID=%%P"
if defined EXISTING_BACKEND_PID goto restart_existing_backend
echo Existing backend is not owned by this project; reusing it without stopping it.
goto wait_backend

:restart_existing_backend
echo Restarting the project backend in a visible terminal window...
taskkill /PID %EXISTING_BACKEND_PID% /T /F >nul 2>nul
timeout /t 1 /nobreak >nul
goto start_backend

:start_backend
echo Starting local backend in a separate terminal window...
set "BACKEND_STARTED=1"
if "%BACKEND_MODE%"=="python" (
    start "%BACKEND_WINDOW_TITLE%" /D "%BACKEND_ROOT%" "%ComSpec%" /K ""%BACKEND_PYTHON%" -m uvicorn app.main:app --host 127.0.0.1 --port 8000"
) else (
    start "%BACKEND_WINDOW_TITLE%" /D "%PORTABLE_BACKEND_ROOT%" "%ComSpec%" /K ""%PORTABLE_BACKEND_EXE%""
)

:wait_backend
echo Waiting for backend health...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$deadline = (Get-Date).AddSeconds(30); $ready = $false; while ((Get-Date) -lt $deadline) { try { $r = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:8000/api/health' -TimeoutSec 2; if ($r.StatusCode -eq 200) { $ready = $true; break } } catch { }; Start-Sleep -Milliseconds 300 }; if (-not $ready) { exit 1 }"
if errorlevel 1 goto backend_failed

echo Starting WPF desktop client in a separate window...
echo Backend window title: %BACKEND_WINDOW_TITLE%
echo Frontend window title: Brain Platform Frontend
start "Brain Platform Frontend" /wait "%DESKTOP_EXE%"
set "CLIENT_EXIT_CODE=%errorlevel%"
goto cleanup

:backend_failed
echo Backend did not become ready within 30 seconds.
if "%BACKEND_STARTED%"=="1" taskkill /FI "WINDOWTITLE eq %BACKEND_WINDOW_TITLE%" /T /F >nul 2>nul
pause
exit /b 1

:cleanup
if "%BACKEND_STARTED%"=="1" (
    echo Stopping the backend started by this launcher...
    taskkill /FI "WINDOWTITLE eq %BACKEND_WINDOW_TITLE%" /T /F >nul 2>nul
)
exit /b %CLIENT_EXIT_CODE%
