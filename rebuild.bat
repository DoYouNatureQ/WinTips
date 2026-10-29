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

echo [1/5] Stopping running WinTips...

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
rem 4. Clean all build output (bin, obj, publish)
rem ----------------------------------------

echo [2/5] Cleaning build output (bin, obj, publish)...

for %%D in (bin obj publish) do (
    if exist "%~dp0%%D" (
        rmdir /s /q "%~dp0%%D"

        if exist "%~dp0%%D" (
            echo [ERROR] Failed to remove %%D directory.
            echo Please make sure no program is using files inside:
            echo %~dp0%%D
            echo.
            pause
            exit /b 1
        )
    )
)

echo        Build output cleaned.
echo.

rem ----------------------------------------
rem 5. Clean single-file self-extract cache
rem    With PublishSingleFile + IncludeNativeLibrariesForSelfExtract,
rem    every build unpacks native libs into TEMP\.net\WinTips\hash.
rem    Old hash folders are never reused, so they must be cleaned here.
rem ----------------------------------------

echo [3/5] Cleaning stale self-extract cache...

set "CACHE_LEFT="

for %%C in ("%LOCALAPPDATA%\Temp\.net\WinTips" "%TEMP%\.net\WinTips") do (
    if exist "%%~C" (
        rmdir /s /q "%%~C" 2>nul

        if exist "%%~C" (
            set "CACHE_LEFT=1"
        )
    )
)

if defined CACHE_LEFT (
    echo        [WARN] Some cache files are still in use and were skipped.
    echo        Path: %LOCALAPPDATA%\Temp\.net\WinTips
) else (
    echo        Self-extract cache cleaned.
)

echo.

rem ----------------------------------------
rem 6. Publish
rem ----------------------------------------

echo [4/5] Publishing WinTips...
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
rem 7. Check output
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
rem 8. Start WinTips
rem ----------------------------------------

echo [5/5] Starting WinTips...

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
