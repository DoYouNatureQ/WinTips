namespace WinTips.Core;

public enum TipKind
{
    CapsLock,
    NumLock,
    ScrollLock,
    Insert,
    Copy,
    Paste,
    Cut,
    Language,
}

internal static class TipInfo
{
    public static int VirtualKey(TipKind k) => k switch
    {
        TipKind.CapsLock => 0x14,
        TipKind.NumLock => 0x90,
        TipKind.ScrollLock => 0x91,
        TipKind.Insert => 0x2D,
        TipKind.Copy => 0x43,
        TipKind.Paste => 0x56,
        TipKind.Cut => 0x58,
        _ => 0,
    };

    public static string Name(TipKind k) => k switch
    {
        TipKind.CapsLock => Loc.T("KeyCapsLock"),
        TipKind.NumLock => Loc.T("KeyNumLock"),
        TipKind.ScrollLock => Loc.T("KeyScrollLock"),
        TipKind.Copy => Loc.T("TipCopied"),
        TipKind.Paste => Loc.T("TipPasted"),
        TipKind.Cut => Loc.T("TipCut"),
        TipKind.Language => Loc.T("MenuTestLanguage"),
        _ => "Insert",
    };

    public static string Describe(TipKind k, bool? state)
    {
        if (!IsToggle(k)) return k == TipKind.Insert ? Loc.T("TipInsertPressed") : Name(k);
        return Loc.Format("TipToggleFormat", Name(k), Loc.T(state == true ? "StateOn" : "StateOff"));
    }

    public static bool IsToggle(TipKind k) =>
        k is TipKind.CapsLock or TipKind.NumLock or TipKind.ScrollLock;

    public static bool IsClipboard(TipKind k) =>
        k is TipKind.Copy or TipKind.Paste or TipKind.Cut;

    public static bool TryParseKind(string s, out TipKind kind)
    {
        kind = s.ToLowerInvariant() switch
        {
            "caps" or "capslock" => TipKind.CapsLock,
            "num" or "numlock" => TipKind.NumLock,
            "scroll" or "scrolllock" => TipKind.ScrollLock,
            "insert" or "ins" => TipKind.Insert,
            "copy" or "c" => TipKind.Copy,
            "paste" or "v" => TipKind.Paste,
            "cut" or "x" => TipKind.Cut,
            "lang" or "language" or "ime" => TipKind.Language,
            _ => TipKind.CapsLock,
        };
        return s.ToLowerInvariant() is "caps" or "capslock" or "num" or "numlock"
            or "scroll" or "scrolllock" or "insert" or "ins"
            or "copy" or "c" or "paste" or "v" or "cut" or "x"
            or "lang" or "language" or "ime";
    }
}
