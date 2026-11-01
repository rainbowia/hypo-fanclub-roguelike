@echo off
cd /d "%~dp0"
dotnet run --project src\Hypo.Editor
if errorlevel 1 pause
