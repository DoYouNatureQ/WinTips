namespace WinTips.Core;

/// <summary>
/// 按键时刻的统一表示：<see cref="Environment.TickCount64"/> 的 64 位毫秒域。
/// </summary>
/// <remarks>
/// 三条捕获路径能说出的"这次按下是什么时候"格式各不相同：低级钩子给的是
/// <c>KBDLLHOOKSTRUCT.time</c>，Raw Input 只能问 <c>GetMessageTime</c>，两者都是
/// "开机以来的毫秒数"却只有 32 位、49 天回绕一次；轮询连时间戳都没有，只知道自己
/// 在哪一刻看见了按下。
///
/// <see cref="LockState"/> 靠相减来判断"两条上报说的是不是同一次按下"，所以先要把
/// 32 位的那种放回同一个域里。
/// </remarks>
internal static class EventTime
{
    /// <summary>
    /// 换算结果离"现在"超过这个跨度，就认为那个戳不可信。正常最多是被 UI 线程挡住一会儿
    /// （几百毫秒），10 秒已经远远超出正常范围。
    /// </summary>
    private const long MaxLagMs = 10_000;

    /// <summary>此刻，与 32 位时间戳同一个域。</summary>
    public static long Now => Environment.TickCount64;

    /// <summary>
    /// 把 32 位毫秒时间戳换算成不晚于当前时刻的 64 位 tick。
    /// </summary>
    /// <param name="stamp">来自 <c>GetMessageTime</c> 或 <c>KBDLLHOOKSTRUCT.time</c> 的低 32 位。</param>
    public static long Rebase(uint stamp)
    {
        long now = Now;
        long at = (now & ~0xFFFF_FFFFL) | stamp;

        // 换算结果跑到"现在"之后，只可能是高 32 位刚刚进位、而这个戳来自进位前的最后一刻。
        // 消息投递与取时间之间恰好跨过 49 天边界，概率极低但确实存在。
        if (at > now) at -= 0x1_0000_0000L;

        // 兜底：万一取不到可信的戳（消息时间没设、或停在一个离谱的旧值），退回"此刻"。
        // 退化的结果是回到"按处理时刻判定"——最多偶发一次多余翻转；但要是让时刻整体
        // 落后于已记下的那次上报，之后每一次按键都会被当成"上一次的迟到上报"挡掉，
        // 那才是真正救不回来的。
        if (now - at > MaxLagMs) return now;

        return at;
    }
}
