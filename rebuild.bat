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

echo [1/7] Stopping running WinTips...

taskkill /f /im WinTips.exe >nul 2>nul

rem Wait until WinTips.exe has completely exited
:WAIT_FOR_EXIT
tasklist /fi "imagename eq WinTips.exe" 2>nul | find /i "WinTips.exe" >nul

if not errorlevel 1 (
    echo        Waiting for WinTips.exe to exit...
    rem ping as sleep: timeout.exe fails when stdin is redirected
    ping -n 2 127.0.0.1 >nul 2>nul
    goto WAIT_FOR_EXIT
)

echo        WinTips stopped.
echo.

rem ----------------------------------------
rem 4. Clean all build output (bin, obj, publish)
rem ----------------------------------------

echo [2/7] Cleaning build output (bin, obj, publish)...

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

echo [3/7] Cleaning stale self-extract cache...

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

echo [4/7] Publishing WinTips...
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
rem 8. Clean publish residue
rem    dotnet publish always drops WinTips.pdb next to the exe.
rem    The csproj already sets DebugType=none for single-file
rem    publish; this pass removes it anyway, so a manual
rem    `dotnet publish` (or a stale file) can never leak through.
rem ----------------------------------------

echo [5/7] Cleaning publish residue...

set "PUBLISH_REMOVED=0"
set "PUBLISH_LEFT=0"

for %%F in ("%~dp0publish\*.pdb") do (
    if exist "%%~F" (
        del /f /q "%%~F" >nul 2>nul

        if exist "%%~F" (
            echo        [WARN] Could not delete %%~nxF ^(file in use^).
            set "PUBLISH_LEFT=1"
        ) else (
            echo        Removed %%~nxF
            set "PUBLISH_REMOVED=1"
        )
    )
)

rem "00" = nothing matched and nothing failed
if "%PUBLISH_REMOVED%%PUBLISH_LEFT%"=="00" echo        Nothing to clean, publish is already tidy.

echo.

rem ----------------------------------------
rem 9. Remove bin and obj again
rem    dotnet publish recreates bin/ and obj/ even though step 2
rem    deleted them, so wipe them once more to leave a clean tree.
rem    Nothing is needed at runtime: publish\WinTips.exe is
rem    self-contained. VBCSCompiler may still hold obj/ handles,
rem    hence the build-server shutdown + retry below.
rem ----------------------------------------

echo [6/7] Cleaning bin and obj...

dotnet build-server shutdown >nul 2>nul

set "CLEAN_FAIL="

call :RemoveBuildDir "%~dp0bin" bin
if errorlevel 1 set "CLEAN_FAIL=1"

call :RemoveBuildDir "%~dp0obj" obj
if errorlevel 1 set "CLEAN_FAIL=1"

if defined CLEAN_FAIL (
    echo        [WARN] Some folders are still locked and were kept.
    echo        Close Visual Studio / dotnet watch, then rerun this script.
)

echo.

rem ----------------------------------------
rem 10. Start WinTips
rem ----------------------------------------

echo [7/7] Starting WinTips...

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

rem ========================================
rem Subroutines
rem    Placed after exit /b so the main flow can never fall into them.
rem ========================================

:RemoveBuildDir
rem %1 = full path to remove, %2 = short name for the log line
setlocal EnableExtensions

if not exist "%~1" (
    echo        %~2: already gone.
    endlocal & exit /b 0
)

set "TRY=0"

:RemoveBuildDir_Retry
set /a TRY+=1
rmdir /s /q "%~1" 2>nul

if not exist "%~1" (
    echo        %~2: removed.
    endlocal & exit /b 0
)

rem A file handle from the MSBuild / Roslyn server can outlive the
rem publish process for a moment, so retry with a short backoff.
rem "ping -n 2" is used as the sleep: unlike timeout.exe it needs no
rem console stdin and cannot be shadowed by a GNU timeout on PATH.
if %TRY% lss 5 (
    ping -n 2 127.0.0.1 >nul 2>nul
    goto RemoveBuildDir_Retry
)

echo        [WARN] Could not fully remove %~2.
echo        Path: %~1

endlocal & exit /b 1
