@echo off
title LiveKit Multi-Stream Launcher

echo ========================================
echo Starting LiveKit Local Test Environment
echo ========================================

REM --------------------------------------------------
REM Start LiveKit server
REM --------------------------------------------------

@echo off
echo Starting LiveKit server...

start "LiveKit Server" cmd /k ^
cd /d "%~dp0livekit_1.9.11_windows_amd64" ^&^& ^
livekit-server.exe --config config.yaml

timeout /t 2 >nul


REM --------------------------------------------------
REM Start Token Server
REM --------------------------------------------------

echo Starting Token Server...

start "Token Server" cmd /k ^
cd /d "%~dp0token-server" ^&^& ^
node token-server.js

timeout /t 2 >nul


REM --------------------------------------------------
REM Start Publishers
REM --------------------------------------------------

REM echo Starting Publisher Clients...

REM start "Publisher 1" "%~dp0build\MyVRApp.exe" --mode pub --id Client_01 --url ws://127.0.0.1:7880
REM start "Publisher 2" "%~dp0build\MyVRApp.exe" --mode pub --id Client_02 --url ws://127.0.0.1:7880
REM PowerShell: .\LiveKitApp.exe --mode pub --id Client_02 --url ws://127.0.0.1:7880

REM --------------------------------------------------
REM Start Viewer
REM --------------------------------------------------

REM echo Starting Viewer Client...

REM start "Viewer" "%~dp0build\MyVRApp.exe" --mode view --id Viewer_01 --url ws://127.0.0.1:7880


echo.
echo ========================================
echo All services started.
echo ========================================

pause