@echo off
title SRT Broadcast Suite
cd /d "%~dp0"

echo ============================================================
echo Starting SRT Broadcast Suite (.NET 10 x64 Native C#)
echo ============================================================

if exist "bin\Release\net10.0-windows\win-x64\SrtSuite.exe" (
    start "" "bin\Release\net10.0-windows\win-x64\SrtSuite.exe"
) else (
    echo Building Release binary...
    dotnet run -c Release
)
