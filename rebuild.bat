@echo off
setlocal EnableExtensions

rem ========================================
rem WinTips One-Click Rebuild
rem stop -> clean -> publish -> start
rem ========================================

cd /d "%~dp0"

echo.
echo ========================================
echo          WinTips One-Click Rebuild
echo ========================================
echo.

rem ----------------------------------------
rem 1. Check .NET SDK
rem ----------------------------------------

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet was not found.
    echo Please install the .NET 10 SDK.
    echo.
    pause
    exit /b 1
)

echo [INFO] .NET SDK:
dotnet --version
echo.

rem ----------------------------------------
rem 2. Check project file
rem ----------------------------------------

if not exist "%~dp0*.csproj" (
    echo [ERROR] No .csproj file was found.
    echo Project directory:
    echo %~dp0
    echo.
    pause
    exit /b 1
)

rem ----------------------------------------
rem 3. Stop running WinTips
rem ----------------------------------------

echo [1/4] Stopping running WinTips...

taskkill /f /im WinTips.exe >nul 2>nul

rem Wait until WinTips.exe has completely exited
:WAIT_FOR_EXIT
tasklist /fi "imagename eq WinTips.exe" 2>nul | find /i "WinTips.exe" >nul

if not errorlevel 1 (
    echo        Waiting for WinTips.exe to exit...
    timeout /t 1 /nobreak >nul
    goto WAIT_FOR_EXIT
)

echo        WinTips stopped.
echo.

rem ----------------------------------------
rem 4. Clean publish directory
rem ----------------------------------------

echo [2/4] Cleaning publish directory...

if exist "%~dp0publish" (
    rmdir /s /q "%~dp0publish"

    if exist "%~dp0publish" (
        echo [ERROR] Failed to remove publish directory.
        echo Please make sure no program is using files inside:
        echo %~dp0publish
        echo.
        pause
        exit /b 1
    )
)

echo        Publish directory cleaned.
echo.

rem ----------------------------------------
rem 5. Publish
rem ----------------------------------------

echo [3/4] Publishing WinTips...
echo.

dotnet publish ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -o "%~dp0publish"

if errorlevel 1 (
    echo.
    echo [ERROR] Publish failed.
    echo.
    pause
    exit /b 1
)

echo.
echo        Publish completed.
echo.

rem ----------------------------------------
rem 6. Check output
rem ----------------------------------------

if not exist "%~dp0publish\WinTips.exe" (
    echo [ERROR] WinTips.exe was not found after publishing.
    echo Expected:
    echo %~dp0publish\WinTips.exe
    echo.
    pause
    exit /b 1
)

rem ----------------------------------------
rem 7. Start WinTips
rem ----------------------------------------

echo [4/4] Starting WinTips...

start "" "%~dp0publish\WinTips.exe"

if errorlevel 1 (
    echo [ERROR] Failed to start WinTips.
    echo.
    pause
    exit /b 1
)

echo.
echo ========================================
echo              Build Complete
echo ========================================
echo.
echo WinTips has been restarted.
echo.

endlocal
exit /b 0