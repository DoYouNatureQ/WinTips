namespace WinTips.Core;

public enum TipKind
{
    CapsLock,
    NumLock,
    ScrollLock,
    Insert,
}

/// <summary>键位的虚拟键码、显示名与解析。</summary>
internal static class TipInfo
{
    public static int VirtualKey(TipKind k) => k switch
    {
        TipKind.CapsLock => 0x14,
        TipKind.NumLock => 0x90,
        TipKind.ScrollLock => 0x91,
        _ => 0x2D, // Insert
    };

    public static string Name(TipKind k) => k switch
    {
        TipKind.CapsLock => "大写锁定",
        TipKind.NumLock => "数字锁定",
        TipKind.ScrollLock => "滚动锁定",
        _ => "Insert",
    };

    public static bool IsToggle(TipKind k) => k != TipKind.Insert;

    public static bool TryParseKind(string s, out TipKind kind)
    {
        kind = s.ToLowerInvariant() switch
        {
            "caps" or "capslock" => TipKind.CapsLock,
            "num" or "numlock" => TipKind.NumLock,
            "scroll" or "scrolllock" => TipKind.ScrollLock,
            "insert" or "ins" => TipKind.Insert,
            _ => TipKind.CapsLock,
        };
        return s.ToLowerInvariant() is "caps" or "capslock" or "num" or "numlock"
            or "scroll" or "scrolllock" or "insert" or "ins";
    }
}
