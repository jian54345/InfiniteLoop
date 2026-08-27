@echo off
echo Building projects...
dotnet build -c Release

echo Checking for Windows Terminal...
where wt >nul 2>&1
if %errorlevel%==0 (
    echo Windows Terminal not found. Using Command Prompt...
    start "AscNet" /d "AscNet" cmd /k dotnet run -no-build -c Release
)
pause