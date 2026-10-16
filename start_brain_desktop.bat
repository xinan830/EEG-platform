@echo off
setlocal EnableExtensions EnableDelayedExpansion

rem Source launcher: one visible backend terminal and one current-source WPF window.
set "PROJECT_ROOT=%~dp0"
set "BACKEND_ROOT=%PROJECT_ROOT%backend"
set "BACKEND_PYTHON=%BACKEND_ROOT%\.venv\Scripts\python.exe"
set "DESKTOP_ROOT=%PROJECT_ROOT%desktop-client\BrainPlatform.Desktop"
set "DESKTOP_CSPROJ=%DESKTOP_ROOT%\BrainPlatform.Desktop.csproj"
set "DESKTOP_DLL=%PROJECT_ROOT%desktop-client\BrainPlatform.Desktop\bin\Release\net10.0-windows\BrainPlatform.Desktop.dll"
set "BACKEND_STARTED=0"
set "BACKEND_PID="
set "BACKEND_WINDOW_TITLE=Brain Platform Backend %RANDOM%"

echo Choose full or on-demand analysis in the desktop analysis mode dropdown.

if not exist "%BACKEND_PYTHON%" (
    echo Backend Python environment was not found:
    echo %BACKEND_PYTHON%
    pause
    exit /b 1
)
if not exist "%BACKEND_ROOT%\app\main.py" (
    echo Backend source entry point was not found:
    echo %BACKEND_ROOT%\app\main.py
    pause
    exit /b 1
)
if not exist "%DESKTOP_CSPROJ%" (
    echo WPF source project was not found:
    echo %DESKTOP_CSPROJ%
    pause
    exit /b 1
)

echo Building the latest WPF source...
dotnet build "%DESKTOP_CSPROJ%" -c Release
if errorlevel 1 (
    echo WPF source build failed. No client was started.
    pause
    exit /b 1
)
if not exist "%DESKTOP_DLL%" (
    echo WPF source build did not produce:
    echo %DESKTOP_DLL%
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
echo Port 8000 is occupied by another backend. Stop that service or choose a different port.
pause
exit /b 2

:restart_existing_backend
echo Restarting the project backend in a visible terminal window...
taskkill /PID %EXISTING_BACKEND_PID% /T /F >nul 2>nul
timeout /t 1 /nobreak >nul
goto start_backend

:start_backend
echo Starting Python source backend in a separate terminal window...
set "BACKEND_STARTED=1"
start "%BACKEND_WINDOW_TITLE%" /D "%BACKEND_ROOT%" "%ComSpec%" /K ""%BACKEND_PYTHON%" -m uvicorn app.main:app --host 127.0.0.1 --port 8000"

:wait_backend
echo Waiting for backend health...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$deadline = (Get-Date).AddSeconds(30); $ready = $false; while ((Get-Date) -lt $deadline) { try { $r = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:8000/api/health' -TimeoutSec 2; if ($r.StatusCode -eq 200) { $ready = $true; break } } catch { }; Start-Sleep -Milliseconds 300 }; if (-not $ready) { exit 1 }"
if errorlevel 1 goto backend_failed

if "%BACKEND_STARTED%"=="1" (
    for /f "usebackq delims=" %%P in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "$root = [IO.Path]::GetFullPath('%PROJECT_ROOT%'); $owner = Get-NetTCPConnection -LocalPort 8000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess; Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -eq $owner } | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($root) } | Where-Object { $_.CommandLine -like '*brain_backend.exe*' -or $_.CommandLine -like '*app.main:app*' } | Select-Object -ExpandProperty ProcessId"`) do set "BACKEND_PID=%%P"
    if defined BACKEND_PID echo Backend process id: !BACKEND_PID!
)

echo Starting WPF desktop client in a separate window...
echo Frontend mode: CURRENT SOURCE DLL
echo Backend window title: %BACKEND_WINDOW_TITLE%
echo Frontend window title: Brain Platform Frontend
start "Brain Platform Frontend" /D "%DESKTOP_ROOT%" /wait dotnet.exe "%DESKTOP_DLL%"
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
    if defined BACKEND_PID (
        taskkill /PID !BACKEND_PID! /T /F >nul 2>nul
    ) else (
        taskkill /FI "WINDOWTITLE eq %BACKEND_WINDOW_TITLE%" /T /F >nul 2>nul
    )
)
exit /b %CLIENT_EXIT_CODE%
