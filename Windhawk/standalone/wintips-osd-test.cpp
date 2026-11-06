// Standalone harness for the WinTips OSD mod: renders the screenshots used in
// the readme and checks the placement math without installing Windhawk.
//
// This file is not part of the mod. It includes the mod source so both share
// one copy of the drawing code, and supplies the Windhawk API shims plus a
// wmain(). Build it with build-test.bat.
//
//   --shot <kind> <on|off> <scale> <out.png> [light|dark] [lang] [bg.png] [top|bottom] [pct] [tint]
//   --selftest                 exercise hook -> window -> animation
//   --bench [frames]           time one card frame and report the footprint
//   --live [seconds]           run the overlay for real
//   --grab <out.png>           capture the whole virtual desktop
//   --crop / --bounds / --pick image helpers
//   --tap                      toggle Caps Lock once

#include "wh-shims.h"

// Process footprint for --bench. Only the harness reports numbers; the mod
// itself never reads them, so this stays out of the .wh.cpp file.
#include <psapi.h>

// Windhawk passes WH_MOD_ID to the compiler for every mod; this file stands in
// for the engine, so it supplies the value here.
#define WH_MOD_ID L"wintips-osd"

#include "../wintips-osd.wh.cpp"

namespace {

// Preview helper: uses a picture as the "screen" behind the card, so the
// acrylic path can be rendered without a live desktop.
bool UseImageAsBackdrop(const wchar_t* path, int w, int h, int radius) {
    Bitmap source(path);
    if (source.GetLastStatus() != Ok || w <= 0 || h <= 0) {
        return false;
    }
    int sw = (int)source.GetWidth();
    int sh = (int)source.GetHeight();
    int cw = std::min(w, sw);
    int ch = std::min(h, sh);
    int cx = (sw - cw) / 2;
    int cy = (sh - ch) / 2;

    g_backdrop.Invalidate();
    if (!g_backdrop.image.Ensure(w, h)) {
        return false;
    }
    {
        Graphics g(g_backdrop.image.dc);
        g.SetInterpolationMode(InterpolationModeHighQualityBicubic);
        g.DrawImage(&source, RectF(0.0f, 0.0f, (float)w, (float)h), (float)cx,
                    (float)cy, (float)cw, (float)ch, UnitPixel);
    }
    BlurBackdrop(g_backdrop.image, radius);
    g_backdrop.x = 0;
    g_backdrop.y = 0;
    g_backdrop.valid = true;
    return true;
}

int GetEncoderClsid(const WCHAR* mimeType, CLSID* clsid) {
    UINT count = 0;
    UINT size = 0;
    if (GetImageEncodersSize(&count, &size) != Ok || size == 0) {
        return -1;
    }
    auto* encoders = (ImageCodecInfo*)malloc(size);
    if (!encoders) {
        return -1;
    }
    int result = -1;
    if (GetImageEncoders(count, size, encoders) == Ok) {
        for (UINT i = 0; i < count; i++) {
            if (wcscmp(encoders[i].MimeType, mimeType) == 0) {
                *clsid = encoders[i].Clsid;
                result = (int)i;
                break;
            }
        }
    }
    free(encoders);
    return result;
}

bool SavePreviewPng(const wchar_t* path) {
    Bitmap preview(g_cardWidth, g_cardHeight, PixelFormat32bppPARGB);
    {
        Graphics g(&preview);
        SolidBrush background(Color(255, 0x8A, 0x8A, 0x8A));
        g.FillRectangle(&background, 0, 0, g_cardWidth, g_cardHeight);
        // The surface holds premultiplied pixels (see Surface in the mod), so
        // it has to be read back as PARGB - treating it as straight ARGB would
        // darken the preview the same way the old double premultiply darkened
        // the real card.
        Bitmap card(g_cardWidth, g_cardHeight, g_surface.stride,
                    PixelFormat32bppPARGB, (BYTE*)g_surface.bits);
        g.DrawImage(&card, 0, 0);
    }

    CLSID clsid;
    if (GetEncoderClsid(L"image/png", &clsid) < 0) {
        return false;
    }
    return preview.Save(path, &clsid, nullptr) == Ok;
}

bool ParseKind(const wchar_t* text, TipKind& kind) {
    if (_wcsicmp(text, L"caps") == 0) {
        kind = kCapsLock;
    } else if (_wcsicmp(text, L"num") == 0) {
        kind = kNumLock;
    } else if (_wcsicmp(text, L"scroll") == 0) {
        kind = kScrollLock;
    } else if (_wcsicmp(text, L"insert") == 0) {
        kind = kInsert;
    } else if (_wcsicmp(text, L"copy") == 0) {
        kind = kCopy;
    } else if (_wcsicmp(text, L"paste") == 0) {
        kind = kPaste;
    } else if (_wcsicmp(text, L"cut") == 0) {
        kind = kCut;
    } else {
        return false;
    }
    return true;
}

int RunShot(int argc, wchar_t** argv) {
    if (argc < 6) {
        wprintf(L"usage: --shot <caps|num|scroll|insert|copy|paste|cut> "
                L"<on|off> <scale> <out.png> [light|dark] [lang] [bg.png] "
                L"[top|bottom]\n");
        return 2;
    }

    TipKind kind;
    if (!ParseKind(argv[2], kind)) {
        wprintf(L"unknown tip kind: %s\n", argv[2]);
        return 2;
    }
    bool on = _wcsicmp(argv[3], L"on") == 0;
    float scale = (float)_wtof(argv[4]);
    if (scale <= 0.1f) {
        scale = 1.0f;
    }
    const wchar_t* path = argv[5];

    const wchar_t* themeOverride = argc > 6 ? argv[6] : L"";
    if (_wcsicmp(themeOverride, L"light") == 0) {
        g_settings.theme = 2;
    } else if (_wcsicmp(themeOverride, L"dark") == 0) {
        g_settings.theme = 1;
    }

    g_scale = scale;
    // Optional 11th argument: the card size setting, in percent. The DPI scale
    // and the size setting multiply the same way they do in the mod, so a
    // preview at 150% + 80% matches what the card really looks like.
    if (argc > 10) {
        g_settings.tipScalePercent = _wtoi(argv[10]);
    }
    // Optional 12th argument: the tint opacity setting, in percent (20-90).
    if (argc > 11) {
        g_settings.tintOpacity = Clamp(_wtoi(argv[11]), 20, 90);
    }
    g_cardScale = g_scale * g_settings.TipScale();
    // 8th argument: the card's language code (zh / zh-Hant / en / de / ja / ru
    // / es / pt, or auto to follow this machine).
    g_lang = (LanguageMode)ParseLanguageCode(argc > 7 ? argv[7] : L"zh");
    if (g_lang == kLangAuto) {
        g_lang = ResolveLanguage();
    }

    g_frame.kind = kind;
    g_frame.on = on;
    g_frame.prevOn = on;
    g_frame.statusT = 1.0f;
    g_frame.clipT = 1.0f;
    g_frame.badgeScale = 1.0f;
    g_frame.badgeAlpha = 1.0f;
    g_frame.count = IsClipKind(kind) ? 3 : 0;

    g_theme = CurrentTheme();
    MeasureCard(g_cardScale, g_frame, g_cardWidth, g_cardHeight);
    if (!g_surface.Ensure(g_cardWidth, g_cardHeight)) {
        wprintf(L"surface allocation failed\n");
        return 1;
    }

    // Optional background picture, so the acrylic path can be previewed without
    // a live desktop behind the card. The literal value "screen" captures the
    // real desktop instead, which exercises the same code the mod runs.
    if (argc > 8 && argv[8][0]) {
        int radius = (int)std::lround((float)g_settings.blurRadius * g_scale);
        bool ok;
        if (_wcsicmp(argv[8], L"screen") == 0) {
            if (!g_monitor.valid) {
                ResolveTargetMonitor();
            }
            Placement p =
                ComputePlacement((float)g_cardWidth, (float)g_cardHeight,
                                 g_scale);
            HDC screen = GetDC(nullptr);
            ok = CaptureBackdrop(screen, p.x, p.y, g_cardWidth, g_cardHeight,
                                 radius);
            ReleaseDC(nullptr, screen);
        } else {
            ok = UseImageAsBackdrop(argv[8], g_cardWidth, g_cardHeight, radius);
        }
        if (!ok) {
            wprintf(L"background unusable: %s\n", argv[8]);
        }
    }

    g_surface.Clear();
    {
        Graphics g(g_surface.dc);
        // 10th argument mirrors the accent bar for previewing a card that hangs
        // off the top edge, without having to reconfigure the placement.
        bool barOnTop = argc > 9 && _wcsicmp(argv[9], L"top") == 0;
        RenderCard(g, g_cardScale, (float)g_cardWidth, (float)g_cardHeight,
                   g_frame, g_theme, barOnTop);
    }

    if (!SavePreviewPng(path)) {
        wprintf(L"saving PNG failed\n");
        return 1;
    }

    wprintf(L"%s: %dx%d\n", path, g_cardWidth, g_cardHeight);
    return 0;
}

int RunLive(int seconds) {
    LoadSettings();
    DetectLanguage();
    InterlockedExchange(&g_pollEnabled, g_settings.pollElevated ? 1 : 0);

    g_readyEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HANDLE uiThread =
        CreateThread(nullptr, 0, UiThreadProc, nullptr, 0, nullptr);
    if (!uiThread) {
        wprintf(L"failed to start UI thread\n");
        return 1;
    }
    if (g_readyEvent) {
        WaitForSingleObject(g_readyEvent, 5000);
    }
    HANDLE hookThread =
        CreateThread(nullptr, 0, HookThreadProc, nullptr, 0, nullptr);

    wprintf(L"running. Press Caps Lock / Num Lock / Insert or Ctrl+C.\n"
            L"Press Enter in this window to quit.\n");
    if (seconds > 0) {
        Sleep((DWORD)seconds * 1000);
    } else {
        getchar();
    }

    if (g_hwnd) {
        PostMessageW(g_hwnd, WM_APP_QUIT, 0, 0);
    }
    WaitForSingleObject(uiThread, 3000);
    if (g_hookThreadId) {
        PostThreadMessageW(g_hookThreadId, WM_QUIT, 0, 0);
    }
    WaitForSingleObject(hookThread, 3000);
    return 0;
}

int RunGrab(const wchar_t* path) {
    // Whole virtual desktop, so a card on a secondary display can be captured
    // too. Coordinates in the PNG are offset by the virtual-screen origin.
    int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
    int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
    int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
    int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
    Surface surface;
    if (!surface.Ensure(width, height)) {
        return 1;
    }

    HDC screen = GetDC(nullptr);
    BitBlt(surface.dc, 0, 0, width, height, screen, x, y, SRCCOPY);
    ReleaseDC(nullptr, screen);

    Bitmap bitmap(width, height, surface.stride, PixelFormat32bppRGB,
                  (BYTE*)surface.bits);
    CLSID clsid;
    if (GetEncoderClsid(L"image/png", &clsid) < 0 || !path) {
        return 1;
    }
    if (bitmap.Save(path, &clsid, nullptr) != Ok) {
        return 1;
    }
    wprintf(L"%s: %dx%d\n", path, width, height);
    return 0;
}

int RunCrop(int argc, wchar_t** argv) {
    if (argc < 9) {
        wprintf(L"usage: --crop <src.png> <x> <y> <w> <h> <scale> <out.png>\n");
        return 2;
    }
    Bitmap source(argv[2]);
    if (source.GetLastStatus() != Ok) {
        wprintf(L"cannot open %s\n", argv[2]);
        return 1;
    }
    int x = _wtoi(argv[3]);
    int y = _wtoi(argv[4]);
    int w = _wtoi(argv[5]);
    int h = _wtoi(argv[6]);
    float scale = (float)_wtof(argv[7]);
    if (scale <= 0.0f) {
        scale = 1.0f;
    }

    Bitmap output((INT)std::lround(w * scale), (INT)std::lround(h * scale),
                  PixelFormat32bppRGB);
    {
        Graphics g(&output);
        g.SetInterpolationMode(InterpolationModeNearestNeighbor);
        g.SetPixelOffsetMode(PixelOffsetModeHalf);
        g.DrawImage(&source, RectF(0, 0, w * scale, h * scale), (REAL)x,
                    (REAL)y, (REAL)w, (REAL)h, UnitPixel);
    }

    CLSID clsid;
    if (GetEncoderClsid(L"image/png", &clsid) < 0) {
        return 1;
    }
    output.Save(argv[8], &clsid, nullptr);
    wprintf(L"%s: %dx%d\n", argv[8], (int)std::lround(w * scale),
            (int)std::lround(h * scale));
    return 0;
}

// Measures the card rect inside a screenshot by diffing against the corner
// pixel, so the port can be compared with the original numerically.
// Averages a small box of pixels, used to compare card colours between the
// standalone build and the original app.
int RunPick(int argc, wchar_t** argv) {
    if (argc < 5) {
        wprintf(L"usage: --pick <src.png> <x> <y> [box]\n");
        return 2;
    }
    Bitmap source(argv[2]);
    if (source.GetLastStatus() != Ok) {
        wprintf(L"cannot open %s\n", argv[2]);
        return 1;
    }
    int width = (int)source.GetWidth();
    int height = (int)source.GetHeight();
    int x0 = _wtoi(argv[3]);
    int y0 = _wtoi(argv[4]);
    int box = argc > 5 ? _wtoi(argv[5]) : 3;
    if (box < 1) {
        box = 1;
    }

    int count = 0;
    long long sumR = 0, sumG = 0, sumB = 0;
    for (int y = y0; y < y0 + box; y++) {
        for (int x = x0; x < x0 + box; x++) {
            if (x < 0 || y < 0 || x >= width || y >= height) {
                continue;
            }
            Color c;
            source.GetPixel(x, y, &c);
            sumR += c.GetR();
            sumG += c.GetG();
            sumB += c.GetB();
            count++;
        }
    }
    if (!count) {
        wprintf(L"out of range\n");
        return 1;
    }
    wprintf(L"#%02X%02X%02X  (%lld, %lld, %lld) over %d px\n",
            (int)(sumR / count), (int)(sumG / count), (int)(sumB / count),
            sumR / count, sumG / count, sumB / count, count);
    return 0;
}

int RunBounds(const wchar_t* path) {
    Bitmap source(path);
    if (source.GetLastStatus() != Ok) {
        wprintf(L"cannot open %s\n", path);
        return 1;
    }

    int width = (int)source.GetWidth();
    int height = (int)source.GetHeight();
    Rect lockRect(0, 0, width, height);
    BitmapData data{};
    if (source.LockBits(&lockRect, ImageLockModeRead, PixelFormat32bppARGB,
                        &data) != Ok) {
        return 1;
    }

    auto* pixels = (BYTE*)data.Scan0;
    auto at = [&](int x, int y) -> BYTE* {
        return pixels + (size_t)y * data.Stride + (size_t)x * 4;
    };

    BYTE* background = at(0, 0);
    (void)background;
    int minX = width, minY = height, maxX = -1, maxY = -1;

    for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
            BYTE* px = at(x, y);
            // The card is the only large bright area in the sampled region.
            int luma = (px[2] * 299 + px[1] * 587 + px[0] * 114) / 1000;
            bool opaque = px[3] > 128;
            if ((opaque && luma > 150)) {
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
    }

    source.UnlockBits(&data);

    if (maxX < 0) {
        wprintf(L"%s: no card found\n", path);
        return 1;
    }

    wprintf(L"%s: card %dx%d at (%d,%d)\n", path, maxX - minX + 1,
            maxY - minY + 1, minX, minY);
    return 0;
}

void TapKey(WORD vk) {    INPUT input[2]{};
    input[0].type = INPUT_KEYBOARD;
    input[0].ki.wVk = vk;
    input[1].type = INPUT_KEYBOARD;
    input[1].ki.wVk = vk;
    input[1].ki.dwFlags = KEYEVENTF_KEYUP;
    SendInput(2, input, sizeof(INPUT));
}

void ShutdownThreads() {
    if (g_hwnd) {
        PostMessageW(g_hwnd, WM_APP_QUIT, 0, 0);
    }
    if (g_hookThreadId) {
        PostThreadMessageW(g_hookThreadId, WM_QUIT, 0, 0);
    }
    Sleep(300);
}

// Verifies the placement math against a two-monitor layout. The secondary
// display sits to the left of the primary one with a negative origin, which is
// where a "primary-monitor only" implementation goes wrong.
int MonitorLayoutChecks() {
    MonitorTarget primary;
    primary.bounds = RECT{0, 0, 1920, 1080};
    primary.work = RECT{0, 0, 1920, 1040};
    primary.scale = 1.0f;
    primary.valid = true;

    MonitorTarget left;
    left.bounds = RECT{-2560, 0, 0, 1440};
    left.work = RECT{-2560, 0, 0, 1400};
    left.scale = 1.5f;
    left.valid = true;

    struct Case {
        const wchar_t* name;
        Placement got;
        int x;
        int y;
        float dx;
        float dy;
    };

    Case cases[6] = {
        {L"bottom-centre on left display",
         ComputePlacement(left, 1, 1, 0, 0, 160.0f, 50.0f, 1.5f), -1360, 1326,
         0.0f, 30.0f},
        {L"right edge slides right",
         ComputePlacement(left, 1, 2, 0, 0, 160.0f, 50.0f, 1.0f), -176, 1334,
         20.0f, 0.0f},
        {L"left edge slides left",
         ComputePlacement(left, 1, 0, 0, 0, 160.0f, 50.0f, 1.0f), -2544, 1334,
         -20.0f, 0.0f},
        // A 200% card (320x100 at 1.0 DPI) on the 150% display: the card grows,
        // the 16 DIP margin does not, so it stays 24 physical pixels off the
        // taskbar instead of 48.
        {L"doubled card keeps the gap",
         ComputePlacement(left, 1, 1, 0, 0, 320.0f, 100.0f, 1.5f), -1440, 1276,
         0.0f, 30.0f},
        {L"vertical offset clamps",
         ComputePlacement(primary, 1, 1, 0, 5000, 160.0f, 50.0f, 1.0f), 880,
         1030, 0.0f, 20.0f},
        {L"horizontal offset clamps",
         ComputePlacement(primary, 1, 1, -5000, 0, 160.0f, 50.0f, 1.0f), 0, 974,
         -20.0f, 0.0f},
    };

    int failures = 0;
    for (const Case& c : cases) {
        bool ok = c.got.x == c.x && c.got.y == c.y &&
                  c.got.dx == c.dx && c.got.dy == c.dy;
        if (!ok) {
            failures++;
        }
        wprintf(L"  %-32s %s  at (%d,%d) slide (%.0f,%.0f)\n", c.name,
                ok ? L"ok  " : L"FAIL", c.got.x, c.got.y, c.got.dx,
                c.got.dy);
    }

    // The accent bar mirrors above the label when the card hangs off the top
    // edge of its monitor; the split is that monitor's vertical middle.
    struct BarCase {
        const wchar_t* name;
        bool got;
        bool want;
    };
    BarCase barCases[4] = {
        {L"bar side: card at top edge",
         CardHangsTop(left, -1360, 16, 160.0f, 50.0f), true},
        {L"bar side: card above taskbar",
         CardHangsTop(left, -1360, 1326, 160.0f, 50.0f), false},
        {L"bar side: just above middle",
         CardHangsTop(left, -1360, 690, 160.0f, 50.0f), true},
        {L"bar side: just below middle",
         CardHangsTop(left, -1360, 700, 160.0f, 50.0f), false},
    };
    for (const BarCase& c : barCases) {
        if (c.got != c.want) {
            failures++;
        }
        wprintf(L"  %-32s %s  %s\n", c.name,
                c.got == c.want ? L"ok  " : L"FAIL",
                c.got ? L"bar on top" : L"bar at bottom");
    }
    return failures;
}

// The band the taskbar occupies decides where the card sits: collapsed it is the
// 2 px reveal strip, so the card hugs the screen edge; slid out it hands the card
// 48 px of clearance. Both come from UsableRect, not from the work area, which
// is why the card follows an auto-hidden taskbar.
int TaskbarBandChecks() {
    MonitorTarget primary;
    primary.bounds = RECT{0, 0, 1920, 1080};
    primary.work = RECT{0, 0, 1920, 1040};
    primary.scale = 1.0f;
    primary.valid = true;

    struct BandCase {
        const wchar_t* name;
        RECT band;
        int edge;
        int y; // expected top edge of a 160x50 card, bottom preset
    };

    BandCase cases[2] = {
        {L"auto-hide, taskbar collapsed", RECT{0, 1078, 1920, 1080}, ABE_BOTTOM,
         1012},
        {L"auto-hide, taskbar slid out", RECT{0, 1032, 1920, 1080}, ABE_BOTTOM,
         966},
    };

    int failures = 0;
    for (const BandCase& c : cases) {
        MonitorTarget mon = primary;
        TaskbarBand band;
        band.rect = c.band;
        band.edge = c.edge;
        band.valid = true;
        mon.work = UsableRect(mon.bounds, band);
        Placement got = ComputePlacement(mon, 1, 1, 0, 0, 160.0f, 50.0f, 1.0f);
        bool ok = got.x == 880 && got.y == c.y;
        if (!ok) {
            failures++;
        }
        wprintf(L"  %-32s %s  at (%d,%d), usable bottom %ld\n", c.name,
                ok ? L"ok  " : L"FAIL", got.x, got.y, mon.work.bottom);
    }

    struct EdgeCase {
        const wchar_t* name;
        bool got;
        bool want;
    };

    EdgeCase edgeCases[3] = {
        {L"band hugs the bottom edge",
         BandHugsEdge(RECT{0, 1032, 1920, 1080}, primary.bounds, ABE_BOTTOM),
         true},
        {L"band is not the left edge",
         BandHugsEdge(RECT{0, 1032, 1920, 1080}, primary.bounds, ABE_LEFT),
         false},
        {L"thin band reads as vertical",
         NearestEdge(RECT{0, 0, 48, 1080}, primary.bounds) == ABE_LEFT, true},
    };
    for (const EdgeCase& c : edgeCases) {
        if (c.got != c.want) {
            failures++;
        }
        wprintf(L"  %-32s %s\n", c.name, c.got == c.want ? L"ok  " : L"FAIL");
    }
    return failures;
}

// Exercises the real chain (hook -> window message -> animation) and reports
// what the overlay actually did, so the logic can be checked without Windhawk.
int RunSelfTest() {
    LoadSettings();
    DetectLanguage();

    wprintf(L"--- environment ---\n");
    wprintf(L"standalone WinTips running : %s\n",
            OtherInstanceRunning() ? L"yes -> tips suppressed, close it first"
                                   : L"no");
    wprintf(L"monitor mode               : %s\n",
            g_settings.monitor == kMonitorPrimary   ? L"primary"
            : g_settings.monitor == kMonitorCursor  ? L"cursor"
                                                    : L"focus");
    if (ResolveTargetMonitor()) {
        POINT cursor{};
        GetCursorPos(&cursor);
        wprintf(L"cursor                     : %ld,%ld\n", cursor.x, cursor.y);
        wprintf(L"target monitor             : %dx%d at (%ld,%ld), "
                L"work %dx%d at (%ld,%ld), dpi %.2f\n",
                g_monitor.Width(), g_monitor.Height(), g_monitor.bounds.left,
                g_monitor.bounds.top, g_monitor.work.right - g_monitor.work.left,
                g_monitor.work.bottom - g_monitor.work.top, g_monitor.work.left,
                g_monitor.work.top, g_monitor.scale);
        if (g_monitor.band.valid) {
            wprintf(L"taskbar band               : (%ld,%ld)-(%ld,%ld), "
                    L"edge %d, auto-hide %s\n",
                    g_monitor.band.rect.left, g_monitor.band.rect.top,
                    g_monitor.band.rect.right, g_monitor.band.rect.bottom,
                    g_monitor.band.edge,
                    TaskbarAutoHide() ? L"on" : L"off");
        }
    } else {
        wprintf(L"target monitor             : unavailable\n");
    }
    UpdateScales();
    Theme theme = CurrentTheme();
    wprintf(L"theme                      : %s, accent #%02X%02X%02X\n",
            theme.dark ? L"dark" : L"light", theme.accent.GetR(),
            theme.accent.GetG(), theme.accent.GetB());
    wprintf(L"animation / display        : %d ms / %d ms\n",
            g_settings.AnimationMs(), g_settings.displayMs);
    wprintf(L"card size                  : %d%% (card scale %.2f)\n",
            g_settings.tipScalePercent, g_cardScale);

    wprintf(L"\n--- placement (two-monitor layout) ---\n");
    int layoutFailures = MonitorLayoutChecks();
    wprintf(L"placement cases            : %s\n",
            layoutFailures == 0 ? L"all passed" : L"FAILURES");

    wprintf(L"\n--- taskbar band ---\n");
    int bandFailures = TaskbarBandChecks();
    wprintf(L"taskbar cases              : %s\n",
            bandFailures == 0 ? L"all passed" : L"FAILURES");
    wprintf(L"auto-hide                  : %s\n",
            TaskbarAutoHide() ? L"on" : L"off");

    g_readyEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    HANDLE uiThread =
        CreateThread(nullptr, 0, UiThreadProc, nullptr, 0, nullptr);
    if (!uiThread) {
        wprintf(L"failed to start UI thread\n");
        return 1;
    }
    if (g_readyEvent) {
        WaitForSingleObject(g_readyEvent, 5000);
    }
    HANDLE hookThread =
        CreateThread(nullptr, 0, HookThreadProc, nullptr, 0, nullptr);
    Sleep(200);

    wprintf(L"\n--- checks ---\n");
    wprintf(L"window created             : %s\n", g_hwnd ? L"yes" : L"no");
    wprintf(L"keyboard hook installed    : %s\n", g_hook ? L"yes" : L"no");

    SHORT capsBefore = GetKeyState(VK_CAPITAL) & 1;
    TapKey(VK_CAPITAL);
    Sleep(400);
    wprintf(L"after Caps Lock tap        : visible=%s kind=%d on=%d (expected "
            L"kind=0 on=%d)\n",
            IsWindowVisible(g_hwnd) ? L"yes" : L"no", (int)g_frame.kind,
            g_frame.on ? 1 : 0, capsBefore ? 0 : 1);

    // Wait for the card to auto-hide before the next check.
    Sleep((DWORD)(g_settings.displayMs + g_settings.AnimationMs() + 250));
    wprintf(L"after auto-hide            : visible=%s (expected no)\n",
            IsWindowVisible(g_hwnd) ? L"yes" : L"no");
    wprintf(L"lock state: system=%d tracked=%d\n",
            (GetKeyState(VK_CAPITAL) & 1) != 0, g_toggleState[kCapsLock] ? 1 : 0);

    // Copy is deferred until the clipboard really changes; a fake update
    // message must flush the pending intent without touching the clipboard.
    PostMessageW(g_hwnd, WM_APP_TIP, (WPARAM)kCopy, 0);
    Sleep(60);
    bool deferred = !IsWindowVisible(g_hwnd);
    PostMessageW(g_hwnd, WM_CLIPBOARDUPDATE, 0, 0);
    Sleep(200);
    wprintf(L"copy waits for clipboard   : %s\n", deferred ? L"yes" : L"no");
    wprintf(L"copy shown after update    : visible=%s kind=%d (expected kind=4)\n",
            IsWindowVisible(g_hwnd) ? L"yes" : L"no", (int)g_frame.kind);

    // Second copy while the card is up must increment the badge.
    PostMessageW(g_hwnd, WM_APP_TIP, (WPARAM)kCopy, 0);
    Sleep(60);
    PostMessageW(g_hwnd, WM_CLIPBOARDUPDATE, 0, 0);
    Sleep(200);
    wprintf(L"repeat copy badge count    : %d (expected 2)\n", g_frame.count);

    // Restore the original Caps Lock state.
    if ((GetKeyState(VK_CAPITAL) & 1) != capsBefore) {
        TapKey(VK_CAPITAL);
        Sleep(300);
    }

    ShutdownThreads();
    return 0;
}

}  // namespace

// Renders a card exactly the way a tip does (clear -> draw -> premultiply) and
// reports what it costs, plus what the process costs while nothing is on
// screen. The performance numbers quoted in Windhawk/README.md come from here.
int RunBench(int argc, wchar_t** argv) {
    int frames = argc > 2 ? _wtoi(argv[2]) : 300;
    if (frames < 1) {
        frames = 1;
    }

    LoadSettings();
    DetectLanguage();
    if (!ResolveTargetMonitor()) {
        LoadMonitor(PrimaryMonitor(), g_monitor);
    }
    UpdateScales();

    g_frame.kind = kCapsLock;
    g_frame.on = true;
    g_frame.prevOn = true;
    g_frame.statusT = 1.0f;
    g_frame.clipT = 1.0f;
    g_frame.badgeScale = 1.0f;
    g_frame.badgeAlpha = 1.0f;
    g_theme = CurrentTheme();
    MeasureCard(g_cardScale, g_frame, g_cardWidth, g_cardHeight);
    if (!g_surface.Ensure(g_cardWidth, g_cardHeight)) {
        wprintf(L"surface allocation failed\n");
        return 1;
    }
    Placement place =
        ComputePlacement((float)g_cardWidth, (float)g_cardHeight, g_scale);

    LARGE_INTEGER freq;
    QueryPerformanceFrequency(&freq);
    LARGE_INTEGER t0, t1;

    // A tip grabs and blurs the pixels behind the card once, before it fades
    // in. That is the single most expensive step of a tip, so it is timed on
    // its own and then kept out of the per-frame number.
    int radius = (int)std::lround((float)g_settings.blurRadius * g_scale);
    QueryPerformanceCounter(&t0);
    {
        HDC screen = GetDC(nullptr);
        CaptureBackdrop(screen, place.x, place.y, g_cardWidth, g_cardHeight,
                        radius);
        ReleaseDC(nullptr, screen);
    }
    QueryPerformanceCounter(&t1);
    double captureMs =
        (double)(t1.QuadPart - t0.QuadPart) * 1000.0 / (double)freq.QuadPart;

    // Clear + draw is the whole frame: GDI+ already leaves premultiplied
    // pixels behind, so there is no separate blend pass to time.
    QueryPerformanceCounter(&t0);
    for (int i = 0; i < frames; i++) {
        g_surface.Clear();
        {
            Graphics g(g_surface.dc);
            RenderCard(g, g_cardScale, (float)g_cardWidth, (float)g_cardHeight,
                       g_frame, g_theme, false);
        }
    }
    QueryPerformanceCounter(&t1);
    double frameMs = (double)(t1.QuadPart - t0.QuadPart) * 1000.0 /
                     (double)freq.QuadPart / frames;

    // Nothing on screen: the only timer left is the optional 250 ms poll, so a
    // second of idling should cost no measurable CPU.
    FILETIME created{}, exited{}, kernel{}, user{};
    GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user);
    ULARGE_INTEGER before;
    before.LowPart = kernel.dwLowDateTime;
    before.HighPart = kernel.dwHighDateTime;
    Sleep(1000);
    GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user);
    ULARGE_INTEGER after;
    after.LowPart = kernel.dwLowDateTime;
    after.HighPart = kernel.dwHighDateTime;
    double idleMs = (double)(after.QuadPart - before.QuadPart) / 10000.0;

    PROCESS_MEMORY_COUNTERS mem{};
    mem.cb = sizeof(mem);
    K32GetProcessMemoryInfo(GetCurrentProcess(), &mem, sizeof(mem));

    int animMs = g_settings.AnimationMs();
    int animFrames = animMs > 0 ? animMs / 16 : 0;

    wprintf(L"card                    : %dx%d px (dpi %.2f, size %d%%)\n",
            g_cardWidth, g_cardHeight, g_scale, g_settings.tipScalePercent);
    wprintf(L"backdrop capture + blur : %.2f ms   once per tip\n", captureMs);
    wprintf(L"card frame (clear+draw) : %.3f ms   over %d frames\n", frameMs,
            frames);
    wprintf(L"entrance animation      : %d ms     %d frames at 60 Hz\n", animMs,
            animFrames);
    wprintf(L"a full tip costs about  : %.1f ms CPU\n",
            captureMs + frameMs * animFrames);
    wprintf(L"idle CPU over 1 s       : %.0f ms\n", idleMs);
    wprintf(L"working set             : %.1f MB (card surface %.2f MB)\n",
            (double)mem.WorkingSetSize / 1048576.0,
            (double)(g_surface.width * g_surface.height * 4) / 1048576.0);
    return 0;
}

int wmain(int argc, wchar_t** argv) {
    GdiplusStartupInput input;
    if (GdiplusStartup(&g_gdiplusToken, &input, nullptr) != Ok) {
        wprintf(L"GdiplusStartup failed\n");
        return 1;
    }

    int result;
    if (argc > 1 && _wcsicmp(argv[1], L"--shot") == 0) {
        result = RunShot(argc, argv);
    } else if (argc > 1 && _wcsicmp(argv[1], L"--selftest") == 0) {
        result = RunSelfTest();
    } else if (argc > 1 && _wcsicmp(argv[1], L"--bench") == 0) {
        result = RunBench(argc, argv);
    } else if (argc > 1 && _wcsicmp(argv[1], L"--grab") == 0 && argc > 2) {
        result = RunGrab(argv[2]);
    } else if (argc > 1 && _wcsicmp(argv[1], L"--crop") == 0) {
        result = RunCrop(argc, argv);
    } else if (argc > 1 && _wcsicmp(argv[1], L"--bounds") == 0 && argc > 2) {
        result = RunBounds(argv[2]);
    } else if (argc > 1 && _wcsicmp(argv[1], L"--pick") == 0) {
        result = RunPick(argc, argv);
    } else if (argc > 1 && _wcsicmp(argv[1], L"--tap") == 0) {
        TapKey(VK_CAPITAL);
        Sleep(120);
        wprintf(L"caps lock is now %s\n",
                (GetKeyState(VK_CAPITAL) & 1) ? L"on" : L"off");
        result = 0;
    } else {
        result = RunLive(0);
    }

    GdiplusShutdown(g_gdiplusToken);
    return result;
}
