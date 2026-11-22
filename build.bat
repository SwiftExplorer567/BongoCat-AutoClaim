@echo off
setlocal
rem Builds dist\BongoCatAutoClaim.exe (single file, no runtime install needed).
rem Requires the .NET 10 SDK: https://dotnet.microsoft.com/download
rem Optional: set RID=win-arm64 for ARM PCs.

if "%RID%"=="" set RID=win-x64
dotnet publish src\BongoCatAutoClaim.csproj -c Release -r %RID% -o dist
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Build failed.
    exit /b 1
)
echo.
echo [SUCCESS] Built dist\BongoCatAutoClaim.exe
