# -*- coding: utf-8 -*-
"""
WinTips 宣传图生成器（横版 + 竖版）
现代细边画框 + Win11 亚克力浮窗（大写锁定开启态）
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageEnhance

S = 2                       # 超采样倍率
OUT_DIR = os.path.dirname(os.path.abspath(__file__))

def s(v):                   # 设计坐标 -> 画布坐标
    return v * S

# ---------- 字体 ----------
def load_font(cands, size):
    for p in cands:
        if os.path.exists(p):
            return ImageFont.truetype(p, size), p
    return ImageFont.load_default(size), "default"

YAHEI      = [r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\msyh.ttf"]
YAHEI_BOLD = [r"C:\Windows\Fonts\msyhbd.ttc"]
SEGOE_SB   = [r"C:\Windows\Fonts\seguisb.ttf", r"C:\Windows\Fonts\seguib.ttf", r"C:\Windows\Fonts\arialbd.ttf"]

FONTS = {}

def init_fonts():
    f = {}
    f["word"],  _ = load_font(SEGOE_SB, s(66))
    f["tag"],   _ = load_font(YAHEI, s(31))
    f["osd"],   _ = load_font(YAHEI, s(56))
    f["cap"],   _ = load_font(YAHEI_BOLD, s(15))
    return f

# ---------- 尺寸参数（设计坐标）----------
# 现代细边框：窄深灰框条 + 大留白卡纸 + 卡纸底部小字说明
LAYOUTS = {
    "landscape": dict(
        out="wintips-promo.png",
        W=1600, H=1200,
        frame=(80, 70, 1520, 1130), mold=16, mat=92,
        flyout_top=360, word_y=760, tag_y=830,
    ),
    "portrait": dict(
        out="wintips-promo-portrait.png",
        W=1200, H=1600,
        frame=(70, 70, 1130, 1530), mold=16, mat=88,
        flyout_top=436, word_y=836, tag_y=906,
    ),
}

# 现代框配色（哑光石墨）
MOLD_TOP = (58, 58, 64)
MOLD_BOT = (34, 34, 38)
MOLD_EDGE_LIGHT = (120, 120, 128)
MOLD_EDGE_DARK = (14, 14, 16)
MAT_COLOR = (248, 247, 244)
CAPTION_COLOR = (150, 150, 155)

# ---------- 工具 ----------
def vgrad(size, top, bottom):
    m = Image.linear_gradient("L").resize(size)
    a = Image.new("RGB", size, top)
    b = Image.new("RGB", size, bottom)
    return Image.composite(b, a, m)

def rounded_shadow(img, box, radius, blur, alpha, offset=(0, 0)):
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    x0, y0, x1, y1 = box
    d.rounded_rectangle([x0 + offset[0], y0 + offset[1], x1 + offset[0], y1 + offset[1]],
                        radius=radius, fill=(10, 12, 18, alpha))
    lay = lay.filter(ImageFilter.GaussianBlur(blur))
    img.alpha_composite(lay)

def draw_lock(dr, ox, oy, k, color):
    """Fluent 风格挂锁锁体（描边圆角矩形 + 锁孔）"""
    dr.rounded_rectangle([ox + 4 * k, oy + 7 * k, ox + 20 * k, oy + 21 * k],
                         radius=3.25 * k, outline=color, width=max(2, round(1.5 * k)))
    r = 1.5 * k
    dr.ellipse([ox + 12 * k - r, oy + 14 * k - r, ox + 12 * k + r, oy + 14 * k + r], fill=color)

def shackle_layer(icon_size, k, color):
    """锁扣画在独立图层上，便于旋转开启"""
    pad = int(icon_size * 0.8)
    lay = Image.new("RGBA", (icon_size + pad * 2, icon_size + pad * 2), (0, 0, 0, 0))
    dl = ImageDraw.Draw(lay)
    cx, cy = pad + 12 * k, pad + 5 * k
    dl.arc([cx - 4 * k, cy - 4 * k, cx + 4 * k, cy + 4 * k],
           start=180, end=360, fill=color, width=max(2, round(1.5 * k)))
    dl.rectangle([pad + 8 * k, cy, pad + 9.5 * k, pad + 7 * k], fill=color)
    dl.rectangle([pad + 14.5 * k, cy, pad + 16 * k, pad + 7 * k], fill=color)
    return lay, pad

# ============================================================
def render(cfg):
    W, H = cfg["W"], cfg["H"]
    CW, CH = W * S, H * S
    fx0, fy0, fx1, fy1 = cfg["frame"]
    FRAME = (s(fx0), s(fy0), s(fx1), s(fy1))
    MOLD, MAT = s(cfg["mold"]), s(cfg["mat"])
    ART = (FRAME[0] + MOLD + MAT, FRAME[1] + MOLD + MAT,
           FRAME[2] - MOLD - MAT, FRAME[3] - MOLD - MAT)

    # ---- 墙面 ----
    wall = vgrad((CW, CH), (240, 239, 236), (221, 220, 216)).convert("RGBA")
    glow = Image.radial_gradient("L").resize((CW, CH))
    spot = Image.new("RGBA", (CW, CH), (255, 255, 252, 255))
    spot.putalpha(glow.point(lambda v: max(0, 100 - int(v * 0.85))))
    wall.alpha_composite(spot)

    # ---- 画框：细边哑光石墨 + 柔和悬浮投影 ----
    rounded_shadow(wall, FRAME, radius=s(3), blur=s(30), alpha=70, offset=(0, s(16)))
    rounded_shadow(wall, FRAME, radius=s(3), blur=s(80), alpha=50, offset=(0, s(30)))
    mask_f = Image.new("L", (CW, CH), 0)
    ImageDraw.Draw(mask_f).rounded_rectangle(FRAME, radius=s(3), fill=255)
    mold_grad = vgrad((CW, CH), MOLD_TOP, MOLD_BOT).convert("RGBA")
    wall.paste(mold_grad, (0, 0), mask_f)
    d = ImageDraw.Draw(wall)
    # 外沿高光 / 内沿暗线，模拟金属斜切
    d.rounded_rectangle([FRAME[0] + s(1), FRAME[1] + s(1), FRAME[2] - s(1), FRAME[3] - s(1)],
                        radius=s(2), outline=MOLD_EDGE_LIGHT + (200,), width=s(1))
    inner_edge = (FRAME[0] + MOLD, FRAME[1] + MOLD, FRAME[2] - MOLD, FRAME[3] - MOLD)
    d.rounded_rectangle([inner_edge[0] - s(1), inner_edge[1] - s(1),
                         inner_edge[2] + s(1), inner_edge[3] + s(1)],
                        radius=0, outline=MOLD_EDGE_DARK + (255,), width=s(1))

    # ---- 卡纸 ----
    d.rectangle(inner_edge, fill=MAT_COLOR + (255,))
    d.rectangle(inner_edge, outline=(228, 227, 223, 255), width=s(1))
    # 画面内阴影 + 白色斜切线
    rounded_shadow(wall, (ART[0] - s(2), ART[1] - s(2), ART[2] + s(2), ART[3] + s(2)),
                   radius=0, blur=s(8), alpha=60, offset=(0, s(3)))
    d.rectangle((ART[0] - s(3), ART[1] - s(3), ART[2] + s(3), ART[3] + s(3)),
                outline=(255, 255, 255, 220), width=s(2))

    # ---- 画面：Win11 Bloom 背景 ----
    art = Image.new("RGBA", (ART[2] - ART[0], ART[3] - ART[1]))
    rad = Image.radial_gradient("L").resize(art.size)
    art.paste(Image.composite(Image.new("RGB", art.size, (15, 26, 52)),
                              Image.new("RGB", art.size, (54, 84, 148)),
                              rad.point(lambda v: min(255, int(v * 1.35)))), (0, 0))
    rib = Image.new("RGBA", art.size, (0, 0, 0, 0))
    dr = ImageDraw.Draw(rib)
    cw, ch = art.size
    dr.arc([cw * 0.55 - cw * 0.9, ch * 0.45 - ch * 0.75, cw * 0.55 + cw * 0.9, ch * 0.45 + ch * 0.75],
           start=205, end=325, fill=(110, 160, 224, 120), width=int(ch * 0.30))
    dr.arc([cw * 0.45 - cw * 0.65, ch * 0.55 - ch * 0.55, cw * 0.45 + cw * 0.65, ch * 0.55 + ch * 0.55],
           start=215, end=315, fill=(148, 198, 242, 95), width=int(ch * 0.20))
    art.alpha_composite(rib.filter(ImageFilter.GaussianBlur(s(46))))
    gl = Image.new("RGBA", art.size, (0, 0, 0, 0))
    ImageDraw.Draw(gl).ellipse([cw * 0.30, ch * 0.18, cw * 0.70, ch * 0.72], fill=(186, 214, 248, 90))
    art.alpha_composite(gl.filter(ImageFilter.GaussianBlur(s(60))))
    vin = Image.new("RGBA", art.size, (8, 15, 32, 255))
    vin.putalpha(rad.point(lambda v: int(v * 0.42)))
    art.alpha_composite(vin)

    # ---- 浮窗（160x50 窗口 ×4）----
    FW, FH = s(640), s(200)
    FX = (art.size[0] - FW) // 2
    FY = (cfg["flyout_top"] - fy0 - cfg["mold"] - cfg["mat"]) * S
    ICON = s(88)
    K = ICON / 24.0
    IX, IY = FX + s(64), FY + s(58)
    ICON_COLOR = (26, 26, 26, 255)

    rounded_shadow(art, (FX, FY, FX + FW, FY + FH), radius=s(32), blur=s(26), alpha=120, offset=(0, s(10)))
    rounded_shadow(art, (FX, FY, FX + FW, FY + FH), radius=s(32), blur=s(60), alpha=70, offset=(0, s(22)))
    pad = s(30)
    crop = art.crop((FX - pad, FY - pad, FX + FW + pad, FY + FH + pad)).filter(ImageFilter.GaussianBlur(s(7)))
    crop = ImageEnhance.Brightness(crop.convert("RGB")).enhance(1.32).convert("RGBA")
    acrylic_mask = Image.new("L", crop.size, 0)
    ImageDraw.Draw(acrylic_mask).rounded_rectangle([pad, pad, pad + FW, pad + FH], radius=s(32), fill=255)
    art.paste(crop, (FX - pad, FY - pad), acrylic_mask)
    ov = Image.new("RGBA", art.size, (0, 0, 0, 0))
    do = ImageDraw.Draw(ov)
    do.rounded_rectangle([FX, FY, FX + FW, FY + FH], radius=s(32), fill=(246, 250, 254, 134))
    art.alpha_composite(ov)
    do = ImageDraw.Draw(art)
    do.rounded_rectangle([FX, FY, FX + FW, FY + FH], radius=s(32), outline=(255, 255, 255, 165), width=s(2))
    do.rounded_rectangle([FX + s(3), FY + s(3), FX + FW - s(3), FY + FH - s(3)],
                         radius=s(30), outline=(20, 34, 60, 36), width=s(2))
    lay, spad = shackle_layer(int(ICON), K, ICON_COLOR)
    lay = lay.rotate(-30, resample=Image.BICUBIC, center=(spad + 16 * K, spad + 7 * K))
    art.alpha_composite(lay, (int(IX - spad), int(IY - spad)))
    draw_lock(do, IX, IY, K, ICON_COLOR)
    do.text((FX + FW / 2 + s(36), FY + FH / 2 + s(4)), "大写锁定 开启",
            font=FONTS["osd"], fill=(26, 26, 26, 255), anchor="mm")
    BAR_W, BAR_H, BAR_R = s(144), s(12), s(6)
    bx0 = FX + (FW - BAR_W) / 2
    by0 = FY + FH - s(28) - BAR_H
    do.rounded_rectangle([bx0, by0, bx0 + BAR_W, by0 + BAR_H], radius=BAR_R, fill=(0, 103, 192, 255))

    # ---- 画内文字 ----
    wxc = art.size[0] / 2
    wy = (cfg["word_y"] - fy0 - cfg["mold"] - cfg["mat"]) * S
    ty = (cfg["tag_y"] - fy0 - cfg["mold"] - cfg["mat"]) * S
    shadow = Image.new("RGBA", art.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).text((wxc, wy + s(4)), "WinTips", font=FONTS["word"],
                                fill=(0, 0, 0, 110), anchor="mm")
    art.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(s(5))))
    do.text((wxc, wy), "WinTips", font=FONTS["word"], fill=(255, 255, 255, 255), anchor="mm")
    do.text((wxc, ty), "键盘状态 · 一目了然", font=FONTS["tag"], fill=(224, 234, 248, 215), anchor="mm")

    # ---- 合成 + 卡纸说明小字（替代铭牌）----
    wall.paste(art.convert("RGB"), (int(ART[0]), int(ART[1])))
    dd = ImageDraw.Draw(wall)
    text = "WINTIPS  —  CAPS · NUM · SCROLL LOCK"
    ls = s(3)
    fp = FONTS["cap"]
    widths = [dd.textlength(c, font=fp) for c in text]
    total = sum(widths) + ls * (len(text) - 1)
    tyc = (ART[3] + FRAME[3] - MOLD) // 2
    tx0 = CW / 2 - total / 2
    for c, wch in zip(text, widths):
        dd.text((tx0, tyc), c, font=fp, fill=CAPTION_COLOR + (255,), anchor="lm")
        tx0 += wch + ls

    out = os.path.join(OUT_DIR, cfg["out"])
    wall.convert("RGB").resize((W, H), Image.LANCZOS).save(out, "PNG")
    print("saved:", out)

# ============================================================
if __name__ == "__main__":
    FONTS.update(init_fonts())
    for cfg in LAYOUTS.values():
        render(cfg)
