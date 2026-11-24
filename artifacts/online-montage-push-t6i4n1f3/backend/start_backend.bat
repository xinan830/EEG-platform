@echo off
setlocal
cd /d "%~dp0"
for %%I in ("%~dp0..") do set "PROJECT_ROOT=%%~fI"
set "BRAIN_STORAGE_ROOT=%PROJECT_ROOT%\storage"
set "BRAIN_ENABLE_ON_DEMAND_PSD=1"

".venv\Scripts\python.exe" -m uvicorn app.main:app --host 127.0.0.1 --port 8000 --log-level info --no-access-log
endlocal & exit /b %ERRORLEVEL%
