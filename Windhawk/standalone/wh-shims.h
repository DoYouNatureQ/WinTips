// Shims for the Windhawk mod API, used only by the standalone harness.
//
// Included by wintips-osd-test.cpp before the mod source so the same code
// compiles as a plain Win32 program outside Windhawk. None of this is part of
// the mod: Windhawk compiles ../wintips-osd.wh.cpp on its own.
//
// Settings come from %LOCALAPPDATA%\WinTipsOsdTest\settings.ini and log lines
// go to %TEMP%\wintips-osd.log, which is why this lives outside the mod file.

#pragma once

#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cwchar>
#include <cwctype>
#include <string>

// WINBOOL comes from MinGW, which is the toolchain Windhawk builds mods with.
// MSVC has no such type, and the mod's launcher follows the wiki snippet.
typedef int WINBOOL;

static std::wstring StandaloneIniPath() {
    WCHAR path[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", path, MAX_PATH);
    if (!n || n >= MAX_PATH) {
        return L"wintips-osd.ini";
    }
    std::wstring dir = std::wstring(path) + L"\\WinTipsOsdTest";
    CreateDirectoryW(dir.c_str(), nullptr);
    return dir + L"\\settings.ini";
}

static std::wstring StandaloneReadSetting(const wchar_t* name) {
    static std::wstring cachedPath;
    if (cachedPath.empty()) {
        cachedPath = StandaloneIniPath();
    }
    wchar_t buf[512]{};
    GetPrivateProfileStringW(L"settings", name, L"", buf, 512,
                             cachedPath.c_str());
    return buf;
}

static void LogToFile(const wchar_t* text) {
    char path[MAX_PATH];
    DWORD n = GetTempPathA(MAX_PATH, path);
    if (!n || n >= MAX_PATH) {
        return;
    }
    strcat_s(path, "wintips-osd.log");
    FILE* file = nullptr;
    if (fopen_s(&file, path, "a") != 0 || !file) {
        return;
    }
    char utf8[4096];
    int written = WideCharToMultiByte(CP_UTF8, 0, text, -1, utf8, 4096, nullptr,
                                      nullptr);
    if (written > 1) {
        fwrite(utf8, 1, (size_t)(written - 1), file);
        fputc('\n', file);
    }
    fclose(file);
}

static void Wh_Log(const wchar_t* format, ...) {
    wchar_t buf[2048];
    va_list args;
    va_start(args, format);
    vswprintf(buf, 2048, format, args);
    va_end(args);

    OutputDebugStringW(buf);
    OutputDebugStringW(L"\n");
    LogToFile(buf);
}

static int Wh_GetIntSetting(const wchar_t* name, ...) {
    // Mirror the defaults declared in ==WindhawkModSettings== so the standalone
    // build behaves like the real mod when no ini file is present.
    static const struct {
        const wchar_t* name;
        int value;
    } defaults[] = {
        {L"notifyCapsLock", 1},  {L"notifyNumLock", 1},
        {L"notifyScrollLock", 1}, {L"notifyInsert", 1},
        {L"notifyCopy", 1},      {L"notifyPaste", 1},
        {L"notifyCut", 1},       {L"notifyAnyCopy", 1},
        {L"countBadge", 1},
        {L"notifyLanguage", 1},  {L"displayMs", 2000},
        {L"animationPercent", 100},
        {L"tipScale", 100},      {L"tintOpacity", 55},
        {L"offsetX", 0},         {L"offsetY", 0},
        {L"suppressFullscreen", 0},
    };

    std::wstring v = StandaloneReadSetting(name);
    if (v.empty()) {
        for (const auto& entry : defaults) {
            if (_wcsicmp(entry.name, name) == 0) {
                return entry.value;
            }
        }
        return 0;
    }
    return _wtoi(v.c_str());
}

// Windhawk returns a string that must be released with Wh_FreeStringSetting.
static const wchar_t* Wh_GetStringSetting(const wchar_t* name, ...) {
    std::wstring v = StandaloneReadSetting(name);
    if (v.empty()) {
        if (_wcsicmp(name, L"language") == 0) {
            v = L"auto";
        } else if (_wcsicmp(name, L"vertical") == 0) {
            v = L"bottom";
        } else if (_wcsicmp(name, L"horizontal") == 0) {
            v = L"center";
        } else if (_wcsicmp(name, L"theme") == 0) {
            v = L"system";
        } else if (_wcsicmp(name, L"monitor") == 0) {
            v = L"focus";
        }
    }
    size_t size = (v.size() + 1) * sizeof(wchar_t);
    auto* copy = (wchar_t*)malloc(size);
    if (copy) {
        memcpy(copy, v.c_str(), size);
    }
    return copy;
}

static void Wh_FreeStringSetting(const wchar_t* str) {
    free((void*)str);
}

static int Wh_GetIntValue(const wchar_t* valueName, int defaultValue) {
    return (int)GetPrivateProfileIntW(L"state", valueName, defaultValue,
                                      StandaloneIniPath().c_str());
}

static bool Wh_SetIntValue(const wchar_t* valueName, int value) {
    wchar_t buf[32];
    swprintf(buf, 32, L"%d", value);
    return WritePrivateProfileStringW(L"state", valueName, buf,
                                      StandaloneIniPath().c_str()) != FALSE;
}

// The harness never installs a hook, but the mod's launcher references this.
static BOOL Wh_SetFunctionHook(void* target, void* hook, void** original) {
    (void)target;
    (void)hook;
    (void)original;
    return TRUE;
}
