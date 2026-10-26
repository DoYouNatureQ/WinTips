# -*- coding: utf-8 -*-
"""
WinTips 演示视频生成器（1920x1080 @30fps, ~13.6s）
动效复刻 UI/OsdWindow.xaml.cs，整体较应用默认（300ms）提速约 1.2x：
  · 窗口浮现 240ms：淡入 + 自下方 20dip 上滑（均 Cubic EaseOut）
  · 窗口隐藏 240ms：线性淡出 + 下滑 20dip（Cubic EaseIn）
  · 锁键状态 180ms（Quadratic EaseOut），与浮现同帧启动：
      开启 = 锁扣自 +25°(弹开) 顺时针转回 0°(闭合) + 回弹下沉，
      指示条 36→60dip、不透明度 0.2→1.0；关闭全反向
  · 剪贴板：对勾随浮现一笔描出（仅一次，文字与 ×N 解耦）；
      ×N 徽章在浮窗落定后淡入，连击时缩放弹跳 1→1.3→1；
      浮窗保持屏幕居中（宽度按应用 UpdateSize 公式）
时间线：Caps 开启 → (原地切换) 关闭 → Num 开启 → 复制连击 ×1..×13 → 粘贴 → 片尾

多语言：文案与字体按语言切换（中文=微软雅黑，英文=Segoe UI Variable / Segoe UI），
浮窗宽度同样走应用 UpdateSize 公式，因此两种语言的版式与时间线逐帧一致。

用法：
    python make_demo_video.py        # 中英两版都出
    python make_demo_video.py zh     # 仅 wintips-demo.mp4
    python make_demo_video.py en     # 仅 wintips-demo-en.mp4
"""
import os, sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageEnhance
import imageio.v2 as imageio

SS = 2                       # 图层预渲染超采样
W, H = 1920, 1080
SCALE = 3                    # 浮窗放大倍数（真实 160x50dip -> 480x150px）
FPS = 30
OUT_DIR = os.path.dirname(os.path.abspath(__file__))

def s(v):
    return v * SS

# ---------- 字体 ----------
def load_font(cands, size):
    for p in cands:
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default(size)

YAHEI    = [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyh.ttf"]
YAHEI_BD = [r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\msyh.ttf"]
SEGOE    = [r"C:\Windows\Fonts\segoeui.ttf", r"C:\Windows\Fonts\arial.ttf"]
SEGOE_SB = [r"C:\Windows\Fonts\seguisb.ttf", r"C:\Windows\Fonts\seguib.ttf", r"C:\Windows\Fonts\arialbd.ttf"]
# 应用正文用 "Segoe UI Variable Display, Segoe UI" Medium；PIL 没有 Medium 实例，取 Variable 默认字重
SEGVAR   = [r"C:\Windows\Fonts\SegUIVar.ttf", r"C:\Windows\Fonts\segoeui.ttf"]

# ---------- 语言表 ----------
LANGS = {
    # 文案取自 Core/Localization.cs，保证视频与真实界面一致
    "zh": dict(
        out="wintips-demo.mp4",
        osd_font=YAHEI,    osd_size=14,
        badge_font=YAHEI_BD, badge_size=12.5,
        word_font=SEGOE_SB, word_size=88,
        tag_font=YAHEI,    tag_size=38,
        cap_font=SEGOE,    cap_size=15,
        caps_on="大写锁定 开启", caps_off="大写锁定 关闭", num_on="数字锁定 开启",
        copied="已复制", pasted="已粘贴",
        end_tag="键盘状态 · 一目了然",
        end_cap="Caps · Num · Scroll Lock  |  Ctrl+C / V / X",
    ),
    "en": dict(
        out="wintips-demo-en.mp4",
        osd_font=SEGVAR,   osd_size=14,
        badge_font=SEGOE_SB, badge_size=12.5,
        word_font=SEGOE_SB, word_size=88,
        tag_font=SEGOE,    tag_size=42,   # 拉丁 x-height 偏小，字号略放大以求视觉等重
        cap_font=SEGOE,    cap_size=15,
        caps_on="Caps Lock On", caps_off="Caps Lock Off", num_on="Num Lock On",
        copied="Copied", pasted="Pasted",
        end_tag="Keyboard status, at a glance",
        end_cap="Caps · Num · Scroll Lock  |  Ctrl+C / V / X",
    ),
}

LANG = "zh"        # 当前语言（set_lang 设置）
S = {}             # 当前语言的文案/字体配置
f_osd = f_badge = f_word = f_tag = f_cap = None

def set_lang(code):
    """切换渲染语言：重载字体、绑定文案。"""
    global LANG, S, f_osd, f_badge, f_word, f_tag, f_cap
    cfg = LANGS[code]
    LANG, S = code, cfg
    f_osd   = load_font(cfg["osd_font"],   s(cfg["osd_size"] * SCALE))     # 浮窗正文 14dip
    f_badge = load_font(cfg["badge_font"], s(cfg["badge_size"] * SCALE))   # 徽章 12.5dip SemiBold
    f_word  = load_font(cfg["word_font"],  s(cfg["word_size"]))
    f_tag   = load_font(cfg["tag_font"],   s(cfg["tag_size"]))
    f_cap   = load_font(cfg["cap_font"],   s(cfg["cap_size"]))
    return cfg

# ---------- 动效参数（整体较应用默认提速约 1.2x） ----------
APPEAR = DISAPPEAR = 0.24     # 窗口浮现/隐藏 240ms
STATUS_S = 0.18               # 状态动效 180ms
POP_S   = 0.24               # 徽章弹跳 240ms
SLIDE_DIP = 20                # 上浮/下滑 20dip
SHACKLE_OPEN = 25.0           # 应用：关闭态锁扣 +25°（顺时针弹开），开启态 0°

# ---------- 时间线（秒） ----------
# 文案不写死在这里：锁键文案由 S["caps_on"]/["caps_off"]/["num_on"] 提供
CAPS = dict(in0=0.5, off_t=2.24, out0=3.44)    # off_t：浮窗内原地切换为“关闭”
NUM  = dict(in0=4.03, off_t=None, out0=5.87)
RAMP_N = 13
RAMP  = dict(in0=6.46, out0=9.60)
PASTE = dict(in0=10.19, out0=11.53)
END0 = 11.77
ENDCARD = 1.8
TOTAL = END0 + ENDCARD
N_FRAMES = int(round(TOTAL * FPS))

# 复制连击时刻表（相对浮窗 in0）：浮现完成 -> 对勾描出 -> ×1 淡入，
# ×2/×3 慢速展示，之后 0.10s 疯狂连击。对勾只在浮窗出现时描一次，
# 连击只动徽章（文字与 ×N 动效解耦）。
RAMP_X1  = APPEAR             # ×1：浮窗落定后淡入
RAMP_T1, RAMP_T2, RAMP_T3, RAMP_FAST = 0.60, 0.96, 1.08, 0.10

def ramp_n_at(u):
    if u < 0: return 0
    if u < RAMP_T1: return 1
    if u < RAMP_T2: return 2
    if u < RAMP_T3: return 3
    return min(RAMP_N, 4 + int((u - RAMP_T3) / RAMP_FAST))

def ramp_inc(n):
    if n <= 1: return RAMP_X1
    if n == 2: return RAMP_T1
    if n == 3: return RAMP_T2
    return RAMP_T3 + (n - 4) * RAMP_FAST

# ---------- 缓动 ----------
def clamp01(u):
    return 0.0 if u < 0 else (1.0 if u > 1 else u)

def cubic_out(u): return 1 - (1 - u) ** 3      # CubicEase EaseOut
def cubic_in(u):  return u ** 3                # CubicEase EaseIn
def cubic_io(u):                             # CubicEase EaseInOut
    return 4 * u ** 3 if u < 0.5 else 1 - (-2 * u + 2) ** 3 / 2
def quad_out(u):  return 1 - (1 - u) ** 2      # QuadraticEase EaseOut

def set_alpha(layer, f):
    a = layer.getchannel("A").point(lambda v: int(v * f))
    layer.putalpha(a)
    return layer

def compose(img, layer, x, y, alpha):
    if alpha <= 0: return
    lay = layer if alpha >= 1 else set_alpha(layer.copy(), alpha)
    img.alpha_composite(lay, (int(x), int(y)))

# ---------- 窗口浮现/隐藏（AnimateIn / HideAnimated） ----------
def window_pose(t, in0, out0):
    if t < in0 or t >= out0 + DISAPPEAR:
        return None
    if t < in0 + APPEAR:                        # 淡入 + 自下方 20dip 上滑
        e = cubic_out((t - in0) / APPEAR)
        return e, (1 - e) * SLIDE_DIP * SCALE
    if t < out0:
        return 1.0, 0.0
    u = (t - out0) / DISAPPEAR                  # 线性淡出 + 下滑 20dip（Cubic EaseIn）
    return 1 - u, cubic_in(u) * SLIDE_DIP * SCALE

# ---------- 锁键状态动效（AnimateStatus） ----------
def lock_status(t, in0, off_t):
    """返回 (锁扣角, 指示条宽 dip, 指示条不透明度, 状态进度, 相位)"""
    if off_t is None or t < off_t:              # 开启：锁扣闭合 + 指示条生长
        u = clamp01((t - in0) / STATUS_S)
        k = quad_out(u)
        return SHACKLE_OPEN * (1 - k), 36 + 24 * k, 0.2 + 0.8 * k, u, "on"
    u = clamp01((t - off_t) / STATUS_S)          # 关闭：锁扣弹开 + 指示条收缩
    k = quad_out(u)
    return SHACKLE_OPEN * k, 60 - 24 * k, 1 - 0.8 * k, u, "off"

def bounce_px(u):
    """锁扣下沉回弹：ShackleBounce Y=1 为 Path 坐标（24 网格），经 Viewbox 缩放 ≈ 22/24dip；
    20ms 到位（Cubic EaseOut），200ms 归零（Cubic EaseInOut）"""
    if u <= 0 or u >= 1: return 0.0
    amp = 22 * SCALE / 24.0
    if u < 0.1: return cubic_out(u / 0.1) * amp
    return (1 - cubic_io((u - 0.1) / 0.9)) * amp

# ---------- 桌面（壁纸 + 任务栏），2x 预渲染 ----------
def build_desktop():
    img = Image.new("RGBA", (s(W), s(H)))
    rad = Image.radial_gradient("L").resize(img.size)
    img.paste(Image.composite(Image.new("RGB", img.size, (13, 22, 45)),
                              Image.new("RGB", img.size, (44, 70, 128)),
                              rad.point(lambda v: min(255, int(v * 1.35)))), (0, 0))
    rib = Image.new("RGBA", img.size, (0, 0, 0, 0))
    dr = ImageDraw.Draw(rib)
    cw, ch = img.size
    dr.arc([cw*0.5-cw*0.95, ch*0.55-ch*0.8, cw*0.5+cw*0.95, ch*0.55+ch*0.8],
           start=200, end=330, fill=(105, 155, 220, 115), width=int(ch*0.32))
    dr.arc([cw*0.42-cw*0.6, ch*0.62-ch*0.5, cw*0.42+cw*0.6, ch*0.62+ch*0.5],
           start=210, end=315, fill=(145, 195, 240, 90), width=int(ch*0.2))
    img.alpha_composite(rib.filter(ImageFilter.GaussianBlur(s(40))))
    gl = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(gl).ellipse([cw*0.3, ch*0.2, cw*0.7, ch*0.85], fill=(170, 205, 245, 80))
    img.alpha_composite(gl.filter(ImageFilter.GaussianBlur(s(50))))
    vin = Image.new("RGBA", img.size, (6, 12, 26, 255))
    vin.putalpha(rad.point(lambda v: int(v * 0.45)))
    img.alpha_composite(vin)

    # 纯净桌面：不画任务栏（浮窗位置按应用无任务栏分支 WorkArea.Bottom-16 摆放）
    return img

# ---------- 浮窗 ----------
FW, FH = 160 * SCALE, 50 * SCALE            # 基准 480 x 150（窄于文字时按 UpdateSize 加宽）
# 无任务栏：按应用 Reposition 的 WorkArea 分支，浮窗底缘距屏幕底部 16px
REST_Y = H - 16 - FH
ICON_COLOR = (26, 26, 26, 255)
ACCENT = (0, 103, 192, 255)

def _flyout_shell(blurred_ss, fw_px):
    """亚克力底板（SS 预渲染，四周 20px 出血），返回 (SS 图层, pad_SS)"""
    FWs, FHs = s(fw_px), s(FH)
    rest_x, rest_y = ((W - fw_px) // 2) * SS, REST_Y * SS
    pad = s(20)
    crop = blurred_ss.crop((rest_x - pad, rest_y - pad,
                            rest_x + FWs + pad, rest_y + FHs + pad))
    crop = ImageEnhance.Brightness(crop.convert("RGB")).enhance(1.32).convert("RGBA")
    mask = Image.new("L", crop.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, pad + FWs, pad + FHs],
                                           radius=s(8 * SCALE), fill=255)
    fl = Image.new("RGBA", (FWs + pad * 2, FHs + pad * 2), (0, 0, 0, 0))
    fl.paste(crop, (0, 0), mask)
    do = ImageDraw.Draw(fl)
    do.rounded_rectangle([pad, pad, pad + FWs, pad + FHs],
                         radius=s(8 * SCALE), fill=(246, 250, 254, 134))
    do.rounded_rectangle([pad, pad, pad + FWs, pad + FHs],
                         radius=s(8 * SCALE), outline=(255, 255, 255, 165), width=SS)
    do.rounded_rectangle([pad + s(3), pad + s(3), pad + FWs - s(3), pad + FHs - s(3)],
                         radius=s(8 * SCALE - 3), outline=(20, 34, 60, 36), width=SS)
    return fl, pad

# ---------- 剪贴板浮窗（文字布局 + 宽度随 ×N 增长，复刻 UpdateSize） ----------
_meas = ImageDraw.Draw(Image.new("RGBA", (8, 8)))

def _w_dip(font, text):
    b = _meas.textbbox((0, 0), text, font=font)
    return (b[2] - b[0]) / SS / SCALE

def lock_width_dip(text):
    """复刻 UpdateSize 锁键分支：宽 = max(160, 16+22+18+文字宽+12)"""
    return max(160, math.ceil(16 + 22 + 18 + _w_dip(f_osd, text) + 12))

def build_lock_flyout(blurred_ss, text):
    """锁键浮窗：锁体 + 文字（锁扣、指示条随动效逐帧叠加）。
    宽随语言文案变化，返回 (图层, pad, 窗宽px)"""
    w_dip = lock_width_dip(text)
    fw_px = int(round(w_dip * SCALE))
    fl, pad = _flyout_shell(blurred_ss, fw_px)
    do = ImageDraw.Draw(fl)
    # 图标垂直居中于内容区(6..40)+Margin(0,0,0,-2) -> 中心 24dip，顶 = 24-11 = 13dip
    ox, oy = pad + s(16 * SCALE), pad + s(13 * SCALE)
    K = s(22 * SCALE) / 24.0
    do.rounded_rectangle([ox + 4*K, oy + 7*K, ox + 20*K, oy + 21*K],
                         radius=3.25*K, outline=ICON_COLOR, width=max(2, round(1.5*K)))
    r = 1.5 * K
    do.ellipse([ox + 12*K - r, oy + 14*K - r, ox + 12*K + r, oy + 14*K + r], fill=ICON_COLOR)
    # 文字：居中 + Margin(18,0,0,2)，内容区 16..宽-12 -> 中心 (宽+4)/2，再右移 9dip
    # （160dip 时即 91dip，与原版一致）；垂直中心 24dip
    do.text((pad + s(((w_dip + 4) / 2 + 9) * SCALE), pad + s(24 * SCALE)), text,
            font=f_osd, fill=ICON_COLOR, anchor="mm")
    return fl.resize((fw_px + 40, FH + 40), Image.LANCZOS), pad, fw_px

def build_shackle(app_angle, bounce_vpx, fw_px=FW):
    """锁扣层（与浮窗图层同原点）：app_angle 0=闭合(开启态)、+25=顺时针弹开(关闭态)，
    复刻 WPF RotateTransform（正角顺时针，PIL 反号）；bounce 为 1dip 下沉回弹（视频 px）"""
    FWs, FHs = s(fw_px), s(FH)
    lay = Image.new("RGBA", (FWs + s(40), FHs + s(40)), (0, 0, 0, 0))
    dl = ImageDraw.Draw(lay)
    off = s(20)
    ox = off + s(16 * SCALE)
    oy = off + s(13 * SCALE) + bounce_vpx * SS     # 与锁体同原点（图标顶 13dip）
    K = s(22 * SCALE) / 24.0
    cx, cy = ox + 12*K, oy + 5*K
    dl.arc([cx - 4*K, cy - 4*K, cx + 4*K, cy + 4*K],
           start=180, end=360, fill=ICON_COLOR, width=max(2, round(1.5*K)))
    dl.rectangle([ox + 8*K, cy, ox + 9.5*K, oy + 7*K], fill=ICON_COLOR)
    dl.rectangle([ox + 14.5*K, cy, ox + 16*K, oy + 7*K], fill=ICON_COLOR)
    lay = lay.rotate(-app_angle, resample=Image.BICUBIC,
                     center=(ox + 16*K, oy + 7*K))
    return lay.resize((lay.size[0] // SS, lay.size[1] // SS), Image.LANCZOS)

_shackle_cache = {}
def get_shackle(angle, bounce, fw_px=FW):
    key = (round(angle * 2) / 2.0, int(round(bounce)), fw_px)
    if key not in _shackle_cache:
        _shackle_cache[key] = build_shackle(key[0], float(key[1]), fw_px)
    return _shackle_cache[key]

def draw_bar(fl, fw_px, w_dip, op):
    """指示条（视频 px 空间，图层四周 pad=20）：宽 36..60dip，不透明度 0.2..1.0"""
    bw, bh = w_dip * SCALE, 3 * SCALE
    x0 = 20 + (fw_px - bw) / 2
    y0 = 20 + FH - 7 * SCALE - bh
    lay = Image.new("RGBA", (int(bw) + 8, int(bh) + 8), (0, 0, 0, 0))
    ImageDraw.Draw(lay).rounded_rectangle([4, 4, 4 + bw, 4 + bh], radius=2 * SCALE,
                                          fill=(ACCENT[0], ACCENT[1], ACCENT[2],
                                                int(round(255 * op))))
    fl.alpha_composite(lay, (int(round(x0)) - 4, int(round(y0)) - 4))

def clip_layout(label, n):
    """复刻 UpdateSize：宽 = max(160, 16+22+18+文字宽+12+徽章字宽+22)；
    文字在图标右缘与徽章左缘之间居中；徽章距右缘 8dip"""
    count = f"×{n}"
    count_w = _w_dip(f_badge, count)
    pill_w = count_w + 14                          # 徽章内边距 7+7
    text_w = _w_dip(f_osd, label)
    w_dip = max(160, math.ceil(16 + 22 + 18 + text_w + 12 + count_w + 22))
    # 徽章 HorizontalAlignment=Right，ContentGrid 右边距 8 -> 徽章右缘 = 窗宽-8（窗口坐标）
    badge_start = w_dip - 8 - pill_w
    mid = (38 + badge_start) / 2                   # 图标右缘 16+22 与徽章左缘的中点
    text_left = max(46, mid - text_w / 2)
    return dict(w_dip=w_dip, pill_w=pill_w, text_left=text_left)

def build_clip_flyout(blurred_ss, label, n):
    """剪贴板浮窗：文字 + 出血底板（对勾、徽章随动效逐帧叠加）"""
    lay = clip_layout(label, n)
    fw_px = int(round(lay["w_dip"] * SCALE))
    fl, pad = _flyout_shell(blurred_ss, fw_px)
    do = ImageDraw.Draw(fl)
    do.text((pad + s(lay["text_left"] * SCALE), pad + s(25 * SCALE)), label,
            font=f_osd, fill=ICON_COLOR, anchor="lm")   # 垂直：内容区(8..42)中心 25dip
    return fl.resize((fw_px + 40, FH + 40), Image.LANCZOS), lay

def build_badge_pill(n):
    """×N 徽章胶囊（视频 px），返回 (图层, 宽, 高)"""
    text = f"×{n}"
    tb = _meas.textbbox((0, 0), text, font=f_badge)
    tw, th = tb[2] - tb[0], tb[3] - tb[1]
    pw, ph = int(tw + s(14 * SCALE)), int(th + s(4 * SCALE))
    bl = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
    db = ImageDraw.Draw(bl)
    db.rounded_rectangle([0, 0, pw - 1, ph - 1], radius=s(9 * SCALE), fill=ACCENT)
    db.text((pw / 2, ph / 2), text, font=f_badge,
            fill=(255, 255, 255, 255), anchor="mm")
    w2, h2 = max(1, round(pw / SS)), max(1, round(ph / SS))
    return bl.resize((w2, h2), Image.LANCZOS), w2, h2

def badge_pop_scale(u):
    """连击弹跳：1 → 1.3（30% 处，Cubic EaseOut）→ 1（Cubic EaseInOut）"""
    u = clamp01(u)
    if u < 0.3:
        return 1.0 + 0.3 * cubic_out(u / 0.3)
    return 1.0 + 0.3 * (1.0 - cubic_io((u - 0.3) / 0.7))

def compose_badge(fl, n, balpha, k, lay, pills):
    pill, pw, ph = pills[n]
    if k != 1.0:
        pw, ph = max(1, int(round(pw * k))), max(1, int(round(ph * k)))
        pill = pill.resize((pw, ph), Image.BILINEAR)
    if balpha < 1:
        pill = set_alpha(pill.copy(), balpha)
    cx = 20 + (lay["w_dip"] - 8 - lay["pill_w"] / 2) * SCALE
    cy = 20 + FH / 2
    fl.alpha_composite(pill, (int(round(cx - pw / 2)), int(round(cy - ph / 2))))

def draw_check(fl, frac):
    """对勾一笔描出（复刻 StrokeDashOffset 8.75→0，视频 px 空间）"""
    if frac <= 0: return
    do = ImageDraw.Draw(fl)
    K = (22 * SCALE) / 24.0
    ox, oy = 20 + 16 * SCALE, 20 + 14 * SCALE   # 剪贴板图标：内容区(8..42)中心 25dip -> 顶 14dip
    Ax, Ay = ox + 5 * K, oy + 12.5 * K
    Bx, By = ox + 10 * K, oy + 17.5 * K
    Cx, Cy = ox + 19 * K, oy + 7 * K
    w = max(2, round(2.4 * K))
    L1 = math.hypot(Bx - Ax, By - Ay)
    L2 = math.hypot(Cx - Bx, Cy - By)
    vis = clamp01(frac) * (L1 + L2)
    if vis <= L1:
        u = vis / L1
        pts = [(Ax, Ay), (Ax + (Bx - Ax) * u, Ay + (By - Ay) * u)]
    else:
        u = (vis - L1) / L2
        pts = [(Ax, Ay), (Bx, By), (Bx + (Cx - Bx) * u, By + (Cy - By) * u)]
    do.line(pts, fill=ICON_COLOR, width=w, joint="curve")
    r = w / 2
    do.ellipse([Ax - r, Ay - r, Ax + r, Ay + r], fill=ICON_COLOR)       # 圆头起点
    if vis > L1:
        do.ellipse([Bx - r, By - r, Bx + r, By + r], fill=ICON_COLOR)   # 圆角折点
    tx, ty = pts[-1]
    do.ellipse([tx - r, ty - r, tx + r, ty + r], fill=ICON_COLOR)       # 圆头笔尖

# ---------- 片尾字卡 ----------
def build_endcard():
    lay = Image.new("RGBA", (s(W), s(H)), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    cx, cy = s(W) / 2, s(H) / 2 - s(30)
    sh = Image.new("RGBA", lay.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).text((cx, cy + s(5)), "WinTips", font=f_word,
                            fill=(0, 0, 0, 130), anchor="mm")
    lay.alpha_composite(sh.filter(ImageFilter.GaussianBlur(s(6))))
    d.text((cx, cy), "WinTips", font=f_word, fill=(255, 255, 255, 255), anchor="mm")
    d.text((cx, cy + s(78)), S["end_tag"], font=f_tag,
           fill=(224, 234, 248, 220), anchor="mm")
    d.text((cx, cy + s(130)), S["end_cap"], font=f_cap,
           fill=(190, 205, 228, 190), anchor="mm")
    return lay.resize((W, H), Image.LANCZOS)

# ---------- 主流程 ----------
def render(code):
    cfg = set_lang(code)
    OUT = os.path.join(OUT_DIR, cfg["out"])
    print(f"[{code}] building assets...")
    desktop_ss = build_desktop()
    desktop = desktop_ss.resize((W, H), Image.LANCZOS)
    blurred_ss = desktop_ss.filter(ImageFilter.GaussianBlur(s(7)))

    lock_shells = {
        S["caps_on"]:  build_lock_flyout(blurred_ss, S["caps_on"]),
        S["caps_off"]: build_lock_flyout(blurred_ss, S["caps_off"]),
        S["num_on"]:   build_lock_flyout(blurred_ss, S["num_on"]),
    }
    ramp_shells = {n: build_clip_flyout(blurred_ss, S["copied"], n)
                   for n in range(1, RAMP_N + 1)}
    paste_shell = build_clip_flyout(blurred_ss, S["pasted"], 1)
    pills = {n: build_badge_pill(n) for n in range(1, RAMP_N + 1)}
    endcard = build_endcard()

    clip_events = (
        (RAMP, ramp_shells, ramp_n_at, ramp_inc),
        (PASTE, {1: paste_shell}, lambda u: 1, lambda n: APPEAR),
    )

    writer = imageio.get_writer(OUT, fps=FPS, codec="libx264",
                                quality=9, pixelformat="yuv420p", macro_block_size=None)
    print(f"[{code}] rendering {N_FRAMES} frames ({TOTAL:.2f}s) -> {os.path.basename(OUT)}")
    for i in range(N_FRAMES):
        t = i / FPS
        img = desktop.copy()

        # --- 锁键事件：Caps 开启→原地切换关闭；Num 开启 ---
        for ev in (CAPS, NUM):
            pose = window_pose(t, ev["in0"], ev["out0"])
            if pose is None:
                continue
            a, dy = pose
            ang, bar_w, bar_op, su, phase = lock_status(t, ev["in0"], ev["off_t"])
            if phase == "on":
                text = S["caps_on"] if ev is CAPS else S["num_on"]
            else:
                text = S["caps_off"]
            shell, _pad, fw_px = lock_shells[text]
            fl = shell.copy()
            fl.alpha_composite(get_shackle(ang, bounce_px(su), fw_px))
            draw_bar(fl, fw_px, bar_w, bar_op)
            compose(img, fl, (W - fw_px) // 2 - 20, REST_Y - 20 + dy, a)

        # --- 剪贴板事件：复制连击 ×1..×N；粘贴 ---
        for ev, shells_n, n_fn, inc_fn in clip_events:
            pose = window_pose(t, ev["in0"], ev["out0"])
            if pose is None:
                continue
            a, dy = pose
            u = t - ev["in0"]
            n = n_fn(u)
            fl, lay = shells_n[n]
            fl = fl.copy()
            # 对勾只在浮窗出现时描一次（表示动作），连击不重描（文字与 ×N 解耦）
            draw_check(fl, quad_out(clamp01(u / STATUS_S)))
            if n >= 1:
                dt = u - inc_fn(n)
                if n == 1:                                     # 首现：淡入
                    balpha, k = quad_out(clamp01(dt / STATUS_S)), 1.0
                else:                                           # 连击：缩放弹跳
                    balpha, k = 1.0, badge_pop_scale(dt / POP_S)
                if balpha > 0:
                    compose_badge(fl, n, balpha, k, lay, pills)
            compose(img, fl,
                    (W - int(round(lay["w_dip"] * SCALE))) // 2 - 20,
                    REST_Y - 20 + dy, a)

        # --- 片尾 ---
        if t >= END0:
            u = min(1.0, (t - END0) / 0.45)
            e = cubic_out(u)
            img.alpha_composite(set_alpha(endcard.copy(), e), (0, int((1 - e) * 16)))

        writer.append_data(np.asarray(img.convert("RGB")))
        if i % 90 == 0:
            print(f"  [{code}] frame {i}/{N_FRAMES}")
    writer.close()
    print("saved:", OUT)

def main():
    arg = (sys.argv[1].lower() if len(sys.argv) > 1 else "all")
    codes = ["zh", "en"] if arg in ("all", "both") else [arg]
    for code in codes:
        if code not in LANGS:
            raise SystemExit(f"unknown lang: {code} (choose from {', '.join(LANGS)} or all)")
        render(code)

if __name__ == "__main__":
    main()
