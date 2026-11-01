@echo off
rem Builds the standalone harness from wintips-osd.wh.cpp.
rem The same file is the Windhawk mod; WINTIPS_STANDALONE swaps the engine
rem callbacks for local stubs so the overlay can be run and screenshot without
rem installing Windhawk.
setlocal
cd /d "%~dp0"

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [error] vswhere.exe not found, install Visual Studio with C++ tools.
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSPATH=%%i"
if not defined VSPATH (
    echo [error] MSVC C++ build tools not found.
    exit /b 1
)

call "%VSPATH%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1

if not exist obj mkdir obj

cl /nologo /std:c++20 /EHsc /W3 /DWINTIPS_STANDALONE /DUNICODE /D_UNICODE /utf-8 ^
   /D"WH_MOD_ID=L\"wintips-osd\"" ^
   /Fo:obj\ /Fe:wintips-osd-test.exe wintips-osd.wh.cpp ^
   /link /SUBSYSTEM:CONSOLE
if errorlevel 1 exit /b 1

echo.
echo built: %~dp0wintips-osd-test.exe
