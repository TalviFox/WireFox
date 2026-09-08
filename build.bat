@echo off
setlocal
echo ===================================================
echo Building WireFox (WPF) - FoxDen Software
echo ===================================================

:: Ensure any running instance is closed so the output executable isn't locked
taskkill /F /IM WireFox.exe 2>nul
taskkill /F /IM WGManager.exe 2>nul

set "DOTNET_DIR=%LOCALAPPDATA%\Microsoft\dotnet"
if exist "%DOTNET_DIR%\dotnet.exe" (
    set "DOTNET_ROOT=%DOTNET_DIR%"
    set "PATH=%DOTNET_DIR%;%PATH%"
    set "DOTNET_PATH=%DOTNET_DIR%\dotnet.exe"
) else (
    set "DOTNET_PATH=dotnet"
)

echo Publishing single-file standalone executable...
"%DOTNET_PATH%" publish "%~dp0WireFox.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%~dp0publish"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ===================================================
    echo Build Succeeded!
    echo Output Executable: %~dp0publish\WireFox.exe
    if exist "%USERPROFILE%\Desktop\WireFox.exe" (
        copy /Y "%~dp0publish\WireFox.exe" "%USERPROFILE%\Desktop\WireFox.exe" >nul
        echo Synced to Desktop: %USERPROFILE%\Desktop\WireFox.exe
    )
    echo ===================================================
) else (
    echo.
    echo Build failed with error code %ERRORLEVEL%
)

pause
