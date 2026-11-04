@echo off
setlocal EnableExtensions

rem ========================================
rem WinTips One-Click Rebuild
rem stop -> clean -> publish -> package -> start
rem
rem usage: rebuild.bat           full rebuild + Build\dist\ (msix + setup)
rem        rebuild.bat /nopack   skip packaging (fast restart only)
rem
rem This script sits in the project root, next to the .csproj and publish\.
rem Everything packaging-related (msix\, installer\, dist\) lives in Build\.
rem ========================================

rem Project root = this script's directory (normalised, no trailing backslash)
for %%I in ("%~dp0.") do set "PROJECT_ROOT=%%~fI"
set "BUILD_DIR=%PROJECT_ROOT%\Build"

cd /d "%PROJECT_ROOT%"

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

if not exist "%PROJECT_ROOT%\*.csproj" (
    echo [ERROR] No .csproj file was found.
    echo Project directory:
    echo %PROJECT_ROOT%
    echo.
    pause
    exit /b 1
)

rem ----------------------------------------
rem 3. Stop running WinTips
rem ----------------------------------------

echo [1/9] Stopping running WinTips...

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
rem    dist\ is deliberately kept: it holds the versioned store
rem    artefacts (msix / setup) that were already uploaded.
rem ----------------------------------------

echo [2/9] Cleaning build output (bin, obj, publish)...

for %%D in (bin obj publish) do (
    if exist "%PROJECT_ROOT%\%%D" (
        rmdir /s /q "%PROJECT_ROOT%\%%D"

        if exist "%PROJECT_ROOT%\%%D" (
            echo [ERROR] Failed to remove %%D directory.
            echo Please make sure no program is using files inside:
            echo %PROJECT_ROOT%\%%D
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

echo [3/9] Cleaning stale self-extract cache...

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

echo [4/9] Publishing WinTips...
echo.

rem EnableCompressionInSingleFile is deliberately FALSE.
rem A compressed single-file bundle has no file backing: every managed
rem assembly is decompressed into private committed memory the first time
rem it is loaded, so the WPF + BCL images become unshareable and the OS can
rem never trim them. Measured on the dev machine (same build, same idle
rem state): 83.5 MB private working set compressed vs 38.8 MB uncompressed.
rem The price is a larger exe on disk (68 MB -> 156 MB), but MSIX and Inno
rem Setup are compressed containers anyway, so the download size barely
rem moves while the resident footprint is halved.

dotnet publish ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=false ^
    -o "%PROJECT_ROOT%\publish"

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

if not exist "%PROJECT_ROOT%\publish\WinTips.exe" (
    echo [ERROR] WinTips.exe was not found after publishing.
    echo Expected:
    echo %PROJECT_ROOT%\publish\WinTips.exe
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

echo [5/9] Cleaning publish residue...

set "PUBLISH_REMOVED=0"
set "PUBLISH_LEFT=0"

for %%F in ("%PROJECT_ROOT%\publish\*.pdb") do (
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

echo [6/9] Cleaning bin and obj...

dotnet build-server shutdown >nul 2>nul

set "CLEAN_FAIL="

call :RemoveBuildDir "%PROJECT_ROOT%\bin" bin
if errorlevel 1 set "CLEAN_FAIL=1"

call :RemoveBuildDir "%PROJECT_ROOT%\obj" obj
if errorlevel 1 set "CLEAN_FAIL=1"

if defined CLEAN_FAIL (
    echo        [WARN] Some folders are still locked and were kept.
    echo        Close Visual Studio / dotnet watch, then rerun this script.
)

echo.

rem ----------------------------------------
rem 10. Build distributables
rem     Both artefacts are cut from the freshly published exe, so a
rem     version bump in the csproj is all it takes - neither packager
rem     keeps its own copy of the version number:
rem       dist\WinTips_<ver>_x64.msix        Microsoft Store MSIX upload
rem       dist\WinTips_<ver>_x64_local.msix  same, placeholder identity (local only)
rem       dist\WinTipsSetup-<ver>.exe        Microsoft Store EXE installer
rem
rem     Before each step the matching packer is asked, via -CheckOnly,
rem     whether its tool exists (Windows SDK / Inno Setup). When it does
rem     not, the step is skipped outright: nothing is attempted, nothing
rem     is written, and the rebuild still succeeds. /nopack skips both.
rem ----------------------------------------

set "PACK_FAIL="

if /i "%~1"=="/nopack" (
    echo [7/9] Skipping MSIX package ^(/nopack^).
    echo [8/9] Skipping setup package ^(/nopack^).
    echo.
    goto START_APP
)

rem No Windows SDK -> no makeappx.exe -> no MSIX. Probe first so the step
rem is skipped cleanly instead of failing halfway through.
powershell -NoProfile -ExecutionPolicy Bypass -File "%BUILD_DIR%\msix\build-msix.ps1" -CheckOnly
if errorlevel 1 goto SKIP_MSIX

echo [7/9] Building MSIX package...
echo.

rem -AllowPlaceholder: when Build\msix\identity.json is absent (fresh clone),
rem produce a *_local.msix from the placeholder identity instead of failing the
rem whole rebuild. With identity.json present it is a no-op and the real
rem upload-ready package is built as usual.
rem
rem NOTE: keep this file pure ASCII with CRLF line endings. cmd.exe seeks in
rem batch files by byte offset, so any multi-byte character (e.g. CJK text in
rem a rem line) desyncs the reader and later commands get executed from the
rem middle of a line ("'k' is not recognized...", stray "at", wrong labels).
powershell -NoProfile -ExecutionPolicy Bypass -File "%BUILD_DIR%\msix\build-msix.ps1" -AllowPlaceholder
set "PACK_RC=%ERRORLEVEL%"
call :CheckPackResult "MSIX" %PACK_RC%
if errorlevel 1 set "PACK_FAIL=1"
goto MSIX_DONE

:SKIP_MSIX
echo [7/9] Skipping MSIX package: makeappx.exe not found ^(Windows SDK absent^).
echo        No .msix was produced.
echo.

:MSIX_DONE

rem Same idea for the installer: no Inno Setup -> skip, do not compile.
powershell -NoProfile -ExecutionPolicy Bypass -File "%BUILD_DIR%\installer\build-setup.ps1" -CheckOnly
if errorlevel 1 goto SKIP_SETUP

echo [8/9] Building setup package...
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%BUILD_DIR%\installer\build-setup.ps1"
set "PACK_RC=%ERRORLEVEL%"
call :CheckPackResult "Setup" %PACK_RC%
if errorlevel 1 set "PACK_FAIL=1"
goto SETUP_DONE

:SKIP_SETUP
echo [8/9] Skipping setup package: Inno Setup is not installed.
echo        No setup exe was produced.
echo.

:SETUP_DONE

echo.

if defined PACK_FAIL (
    echo [ERROR] Packaging failed.
    echo The app was published, but dist is incomplete.
    echo Fix the error above and rerun this script ^(or use /nopack to skip packaging^).
    echo.
    pause
    exit /b 1
)

:START_APP

rem ----------------------------------------
rem 11. Start WinTips
rem ----------------------------------------

echo [9/9] Starting WinTips...

start "" "%PROJECT_ROOT%\publish\WinTips.exe"

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

if exist "%BUILD_DIR%\dist" (
    echo Distributables in Build\dist:
    for %%P in ("%BUILD_DIR%\dist\WinTips_*_x64.msix" "%BUILD_DIR%\dist\WinTips_*_x64_local.msix" "%BUILD_DIR%\dist\WinTipsSetup-*.exe") do (
        echo    %%~nxP  ^(%%~zP bytes^)
    )
    echo.
)

endlocal
exit /b 0

rem ========================================
rem Subroutines
rem    Placed after exit /b so the main flow can never fall into them.
rem ========================================

:CheckPackResult
rem %1 = step name, %2 = exit code of the packaging script
rem 0 = ok, 2 = tool missing (warning only), anything else = failure
if "%~2"=="0" (
    echo        %~1 package created.
    exit /b 0
)

if "%~2"=="2" (
    echo        [WARN] %~1 skipped: build tool not found ^(see message above^).
    echo        Install it, then rerun rebuild.bat to produce this package.
    exit /b 0
)

echo        [ERROR] %~1 packaging failed ^(exit code %~2^).
exit /b 1

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
