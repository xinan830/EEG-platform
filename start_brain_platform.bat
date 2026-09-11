@echo off
setlocal

set "PROJECT_ROOT=%~dp0"
set "BACKEND_ROOT=%PROJECT_ROOT%backend"
set "FRONTEND_ROOT=%PROJECT_ROOT%frontend"
set "BACKEND_PYTHON=%BACKEND_ROOT%\.venv\Scripts\python.exe"
set "NODE_ROOT=D:\nodejs"

if not exist "%BACKEND_PYTHON%" (
    echo Backend Python environment was not found:
    echo %BACKEND_PYTHON%
    pause
    exit /b 1
)

if not exist "%BACKEND_ROOT%\app\main.py" (
    echo Backend entry point was not found.
    pause
    exit /b 1
)

if not exist "%FRONTEND_ROOT%\package.json" (
    echo Frontend package.json was not found.
    pause
    exit /b 1
)

if exist "%NODE_ROOT%\npm.cmd" (
    set "NPM_COMMAND=%NODE_ROOT%\npm.cmd"
) else (
    where npm.cmd >nul 2>nul
    if errorlevel 1 (
        echo npm.cmd was not found. Install Node.js or update NODE_ROOT in this file.
        pause
        exit /b 1
    )
    set "NPM_COMMAND=npm.cmd"
)

echo Starting Brain Platform backend and frontend...
start "Brain Platform Backend" /D "%BACKEND_ROOT%" cmd /k ""%BACKEND_PYTHON%" -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8000"
start "Brain Platform Frontend" /D "%FRONTEND_ROOT%" cmd /k ""%NPM_COMMAND%" run dev -- --host 127.0.0.1 --port 5173"

echo.
echo Backend API: http://127.0.0.1:8000/docs
echo Frontend:    http://127.0.0.1:5173
echo Close the two opened terminal windows to stop the services.
endlocal
