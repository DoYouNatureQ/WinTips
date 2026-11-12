@echo off
rem Builds the standalone harness (standalone\wintips-osd-test.cpp), which
rem includes the mod source and provides the Windhawk API shims, so the overlay
rem can be run and screenshotted without installing Windhawk.
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

rem The import libraries are listed explicitly because the mod no longer carries
rem MSVC-only #pragma comment(lib) lines.
cl /nologo /std:c++20 /EHsc /W3 /DUNICODE /D_UNICODE /utf-8 ^
   /Fo:obj\ /Fe:wintips-osd-test.exe standalone\wintips-osd-test.cpp ^
   /link /SUBSYSTEM:CONSOLE gdiplus.lib gdi32.lib dwmapi.lib shcore.lib ^
   shell32.lib advapi32.lib user32.lib imm32.lib dwrite.lib
if errorlevel 1 exit /b 1

echo.
echo built: %~dp0wintips-osd-test.exe
