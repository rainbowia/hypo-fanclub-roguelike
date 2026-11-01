@echo off
cd /d "%~dp0"
echo [update] git pull ...
git pull --ff-only
if errorlevel 1 echo [update] pull failed - running local version anyway
dotnet build HypoFanclub.sln
pause
