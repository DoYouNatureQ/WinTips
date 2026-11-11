# -*- coding: utf-8 -*-
"""WinTips 微软商店上架宣传图（横版 + 竖版）

两种出图方式：
  · frame    保留 make_promo.py 的细边画框 + 卡纸（含底部小字）
  · noframe  通版出血，没有画框/卡纸，画面直接铺满画布，内容层放大 1.1x 左右

两种模式下画面元素一致：Win11 Bloom 深蓝底 + 亚克力浮窗 + WinTips 标题 +
标语 + 白色 Store 徽章（四色微软方块 + 「已在微软商店推出」）。

用法：
    python make_store_promo.py                    # 中英 × 横竖 × 两种版式，共 8 张
    python make_store_promo.py zh                 # 仅中文
    python make_store_promo.py zh noframe         # 仅中文无边框版
    python make_store_promo.py all frame          # 仅带框版
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageEnhance

from make_promo import (
    s, vgrad, rounded_shadow, draw_lock, shackle_layer, load_font,
    YAHEI, YAHEI_BOLD, SEGOE, SEGOE_SB, SEGVAR,
    MOLD_TOP, MOLD_BOT, MOLD_EDGE_LIGHT, MOLD_EDGE_DARK, MAT_COLOR, CAPTION_COLOR,
)

OUT_DIR = os.path.dirname(os.path.abspath(__file__))

# ---------- 语言表 ----------
LANGS = {
    "zh": dict(
        suffix="",
        osd_font=YAHEI,    osd_size=56,
        tag_font=YAHEI,    tag_size=31,
        badge_font=YAHEI_BOLD, badge_size=56,
        cap_font=YAHEI_BOLD,   cap_size=15,
        osd_text="大写锁定 开启",
        tag_text="键盘状态 · 一目了然",
        badge_text="已在微软商店推出",
        caption="WINTIPS  —  现已在微软商店推出",
    ),
    "en": dict(
        suffix="-en",
        osd_font=SEGVAR,   osd_size=56,
        tag_font=SEGOE,    tag_size=34,
        badge_font=SEGOE_SB, badge_size=56,
        cap_font=SEGOE_SB,   cap_size=15,
        osd_text="Caps Lock On",
        tag_text="Keyboard status, at a glance",
        badge_text="Now on Microsoft Store",
        caption="WINTIPS  —  NOW AVAILABLE ON MICROSOFT STORE",
    ),
}

# ---------- 版式（画布绝对坐标） ----------
# frame：画框参数沿用 make_promo.py，内容位置也一致
# noframe：无画框，k 放大内容层，三个文字的 y 为全画布坐标
LAYOUTS = {
    "landscape": dict(out="wintips-store.png", W=1600, H=1200,
                      frame=(80, 70, 1520, 1130), mold=16, mat=92,
                      flyout_top=340, word_y=690, tag_y=756, badge_y=874, k=1.0),
    "portrait": dict(out="wintips-store-portrait.png", W=1200, H=1600,
                     frame=(70, 70, 1130, 1530), mold=16, mat=88,
                     flyout_top=476, word_y=876, tag_y=942, badge_y=1050, k=1.0),

    # 无边框：内容整体放大，四边留白按视重排布
    "landscape-noframe": dict(out="wintips-store-noframe.png", W=1600, H=1200,
                              flyout_top=250, word_y=645, tag_y=742, badge_y=900, k=1.15),
    "portrait-noframe": dict(out="wintips-store-noframe-portrait.png", W=1200, H=1600,
                             flyout_top=500, word_y=855, tag_y=940, badge_y=1080, k=1.10),
}

# 微软品牌四色（左上起顺时针）
MS_COLORS = [(242, 80, 34), (127, 186, 0), (0, 164, 239), (255, 185, 0)]

_FONT_CACHE = {}


def init_fonts(code, k=1.0):
    """按语言 + 内容缩放装载字体。"""
    key = (code, round(k, 3))
    if key in _FONT_CACHE:
        return _FONT_CACHE[key]
    L = LANGS[code]
    f = {}
    f["word"],  _ = load_font(SEGOE_SB, s(66 * k))
    f["tag"],   _ = load_font(L["tag_font"], s(L["tag_size"] * k))
    f["osd"],   _ = load_font(L["osd_font"], s(L["osd_size"] * k))
    f["badge"], _ = load_font(L["badge_font"], s(L["badge_size"] * k))
    f["cap"],   _ = load_font(L["cap_font"], s(L["cap_size"]))
    _FONT_CACHE[key] = (f, L)
    return f, L


def build_bloom(size, soft=False):
    """Win11 Bloom 风格深蓝底：径向渐变 + 两道光带 + 中心辉光 + 暗角。

    soft=True 用于通版出血：光带透明度更低、模糊更大，避免放大后露出弧线边缘。
    """
    art = Image.new("RGBA", size)
    rad = Image.radial_gradient("L").resize(size)
    art.paste(Image.composite(Image.new("RGB", size, (15, 26, 52)),
                              Image.new("RGB", size, (54, 84, 148)),
                              rad.point(lambda v: min(255, int(v * 1.35)))), (0, 0))
    cw, ch = size
    a1, a2 = (72, 55) if soft else (120, 95)
    rib = Image.new("RGBA", size, (0, 0, 0, 0))
    dr = ImageDraw.Draw(rib)
    dr.arc([cw * 0.55 - cw * 0.9, ch * 0.45 - ch * 0.75, cw * 0.55 + cw * 0.9, ch * 0.45 + ch * 0.75],
           start=205, end=325, fill=(110, 160, 224, a1), width=int(ch * 0.30))
    dr.arc([cw * 0.45 - cw * 0.65, ch * 0.55 - ch * 0.55, cw * 0.45 + cw * 0.65, ch * 0.55 + ch * 0.55],
           start=215, end=315, fill=(148, 198, 242, a2), width=int(ch * 0.20))
    art.alpha_composite(rib.filter(ImageFilter.GaussianBlur(s(70 if soft else 46))))
    gl = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(gl).ellipse([cw * 0.30, ch * 0.18, cw * 0.70, ch * 0.72],
                               fill=(186, 214, 248, 70 if soft else 90))
    art.alpha_composite(gl.filter(ImageFilter.GaussianBlur(s(78 if soft else 60))))
    vin = Image.new("RGBA", size, (8, 15, 32, 255))
    vin.putalpha(rad.point(lambda v: int(v * (0.50 if soft else 0.42))))
    art.alpha_composite(vin)
    return art


def draw_flyout(art, ox, oy, cfg, k, L, F):
    """亚克力浮窗（160x50 窗口，按 k 缩放）。"""
    FW, FH = int(s(640 * k)), int(s(200 * k))
    FX = (art.size[0] - FW) // 2
    FY = int((cfg["flyout_top"] - oy) * 2)
    ICON = s(88 * k)
    K = ICON / 24.0
    IX, IY = FX + s(64 * k), FY + s(58 * k)
    ICON_COLOR = (26, 26, 26, 255)

    rounded_shadow(art, (FX, FY, FX + FW, FY + FH), radius=s(32 * k), blur=s(26 * k),
                   alpha=120, offset=(0, s(10 * k)))
    rounded_shadow(art, (FX, FY, FX + FW, FY + FH), radius=s(32 * k), blur=s(60 * k),
                   alpha=70, offset=(0, s(22 * k)))
    pad = int(s(30 * k))
    crop = art.crop((FX - pad, FY - pad, FX + FW + pad, FY + FH + pad)).filter(
        ImageFilter.GaussianBlur(s(7 * k)))
    crop = ImageEnhance.Brightness(crop.convert("RGB")).enhance(1.32).convert("RGBA")
    acrylic_mask = Image.new("L", crop.size, 0)
    ImageDraw.Draw(acrylic_mask).rounded_rectangle(
        [pad, pad, pad + FW, pad + FH], radius=s(32 * k), fill=255)
    art.paste(crop, (FX - pad, FY - pad), acrylic_mask)

    ov = Image.new("RGBA", art.size, (0, 0, 0, 0))
    do = ImageDraw.Draw(ov)
    do.rounded_rectangle([FX, FY, FX + FW, FY + FH], radius=s(32 * k), fill=(246, 250, 254, 134))
    art.alpha_composite(ov)

    do = ImageDraw.Draw(art)
    do.rounded_rectangle([FX, FY, FX + FW, FY + FH], radius=s(32 * k),
                         outline=(255, 255, 255, 165), width=s(2))
    do.rounded_rectangle([FX + s(3), FY + s(3), FX + FW - s(3), FY + FH - s(3)],
                         radius=s(30 * k), outline=(20, 34, 60, 36), width=s(2))

    lay, spad = shackle_layer(int(ICON), K, ICON_COLOR)
    lay = lay.rotate(-30, resample=Image.BICUBIC, center=(spad + 16 * K, spad + 7 * K))
    art.alpha_composite(lay, (int(IX - spad), int(IY - spad)))
    draw_lock(do, IX, IY, K, ICON_COLOR)
    do.text((FX + FW / 2 + s(36 * k), FY + FH / 2 + s(4 * k)), L["osd_text"],
            font=F["osd"], fill=(26, 26, 26, 255), anchor="mm")

    BAR_W, BAR_H = s(144 * k), s(12 * k)
    bx0 = FX + (FW - BAR_W) / 2
    by0 = FY + FH - s(28 * k) - BAR_H
    do.rounded_rectangle([bx0, by0, bx0 + BAR_W, by0 + BAR_H],
                         radius=BAR_H / 2, fill=(0, 103, 192, 255))


def draw_store_badge(art, cx, cy, k, L, F):
    """白色胶囊徽章：左侧四色微软方块 + 右侧文案，带柔光与投影。

    垂直对齐按文字实际墨迹范围计算（anchor="ls" + textbbox），而不是字体度量的
    ascent/descent 中点——雅黑的 ascent/descent 不对称，用 "lm" 会把中文整体压到
    方块中线以下。方块边长也由墨迹高度推导，字变大方块跟着变大。
    """
    d = ImageDraw.Draw(art)
    fb = F["badge"]
    txt = L["badge_text"]
    tw = d.textlength(txt, font=fb)
    bb = d.textbbox((0, 0), txt, font=fb, anchor="ls")   # 相对基线左端的墨迹框
    ink_h = bb[3] - bb[1]
    baseline = cy - (bb[1] + bb[3]) / 2                  # 让墨迹垂直中心落在 cy

    logo = ink_h * 0.98          # 四色方块总高 ≈ 字高
    g = logo * 0.08              # 方块间距
    q = (logo - g) / 2           # 单个方块边长
    gap = s(32 * k)              # 徽标与文字间距
    padx = s(62 * k)             # 左右内边距
    h = logo * 2.5               # 胶囊高度，与方块等比

    group = logo + gap + tw
    w = group + 2 * padx
    x0, y0, x1, y1 = cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2
    r = h / 2

    # 背光：让白色徽章从深蓝底上浮起来
    glow = Image.new("RGBA", art.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle(
        [x0 - s(16), y0 - s(16), x1 + s(16), y1 + s(16)], radius=r + s(16),
        fill=(190, 218, 255, 72))
    art.alpha_composite(glow.filter(ImageFilter.GaussianBlur(s(38 * k))))

    rounded_shadow(art, (x0, y0, x1, y1), radius=r, blur=s(22 * k), alpha=115, offset=(0, s(10 * k)))
    rounded_shadow(art, (x0, y0, x1, y1), radius=r, blur=s(50 * k), alpha=60, offset=(0, s(20 * k)))

    d = ImageDraw.Draw(art)
    d.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=(251, 252, 254, 244))
    d.rounded_rectangle([x0 + s(1), y0 + s(1), x1 - s(1), y1 - s(1)],
                        radius=r - s(1), outline=(255, 255, 255, 255), width=s(2))

    # 四色方块
    lx, ly = cx - group / 2, cy - logo / 2
    d.rectangle([lx, ly, lx + q, ly + q], fill=MS_COLORS[0] + (255,))
    d.rectangle([lx + q + g, ly, lx + q + g + q, ly + q], fill=MS_COLORS[1] + (255,))
    d.rectangle([lx, ly + q + g, lx + q, ly + q + g + q], fill=MS_COLORS[2] + (255,))
    d.rectangle([lx + q + g, ly + q + g, lx + q + g + q, ly + q + g + q], fill=MS_COLORS[3] + (255,))

    # 文案：用基线锚点 + 已算好的 baseline，墨迹中心与方块中心同高
    d.text((lx + logo + gap, baseline), txt, font=fb, fill=(22, 23, 27, 255), anchor="ls")


# ============================================================
def render(cfg, L, F):
    W, H = cfg["W"], cfg["H"]
    CW, CH = W * 2, H * 2          # 超采样画布
    k = cfg.get("k", 1.0)
    framed = "frame" in cfg

    if framed:
        fx0, fy0, fx1, fy1 = cfg["frame"]
        FRAME = (s(fx0), s(fy0), s(fx1), s(fy1))
        MOLD, MAT = s(cfg["mold"]), s(cfg["mat"])
        ART = (FRAME[0] + MOLD + MAT, FRAME[1] + MOLD + MAT,
               FRAME[2] - MOLD - MAT, FRAME[3] - MOLD - MAT)
        ox, oy = fx0 + cfg["mold"] + cfg["mat"], fy0 + cfg["mold"] + cfg["mat"]

        # ---- 墙面 ----
        wall = vgrad((CW, CH), (240, 239, 236), (221, 220, 216)).convert("RGBA")
        glow = Image.radial_gradient("L").resize((CW, CH))
        spot = Image.new("RGBA", (CW, CH), (255, 255, 252, 255))
        spot.putalpha(glow.point(lambda v: max(0, 100 - int(v * 0.85))))
        wall.alpha_composite(spot)

        # ---- 画框 ----
        rounded_shadow(wall, FRAME, radius=s(3), blur=s(30), alpha=70, offset=(0, s(16)))
        rounded_shadow(wall, FRAME, radius=s(3), blur=s(80), alpha=50, offset=(0, s(30)))
        mask_f = Image.new("L", (CW, CH), 0)
        ImageDraw.Draw(mask_f).rounded_rectangle(FRAME, radius=s(3), fill=255)
        mold_grad = vgrad((CW, CH), MOLD_TOP, MOLD_BOT).convert("RGBA")
        wall.paste(mold_grad, (0, 0), mask_f)
        d = ImageDraw.Draw(wall)
        d.rounded_rectangle([FRAME[0] + s(1), FRAME[1] + s(1), FRAME[2] - s(1), FRAME[3] - s(1)],
                            radius=s(2), outline=MOLD_EDGE_LIGHT + (200,), width=s(1))
        inner_edge = (FRAME[0] + MOLD, FRAME[1] + MOLD, FRAME[2] - MOLD, FRAME[3] - MOLD)
        d.rounded_rectangle([inner_edge[0] - s(1), inner_edge[1] - s(1),
                             inner_edge[2] + s(1), inner_edge[3] + s(1)],
                            radius=0, outline=MOLD_EDGE_DARK + (255,), width=s(1))

        # ---- 卡纸 ----
        d.rectangle(inner_edge, fill=MAT_COLOR + (255,))
        d.rectangle(inner_edge, outline=(228, 227, 223, 255), width=s(1))
        rounded_shadow(wall, (ART[0] - s(2), ART[1] - s(2), ART[2] + s(2), ART[3] + s(2)),
                       radius=0, blur=s(8), alpha=60, offset=(0, s(3)))
        d.rectangle((ART[0] - s(3), ART[1] - s(3), ART[2] + s(3), ART[3] + s(3)),
                    outline=(255, 255, 255, 220), width=s(2))

        art = build_bloom((ART[2] - ART[0], ART[3] - ART[1]))
    else:
        # ---- 无边框：画面直接铺满画布 ----
        wall = None
        art = build_bloom((CW, CH), soft=True)
        ox, oy = 0, 0

    # ---- 浮窗 / 标题 / 标语 ----
    draw_flyout(art, ox, oy, cfg, k, L, F)

    wxc = art.size[0] / 2
    wy = (cfg["word_y"] - oy) * 2
    ty = (cfg["tag_y"] - oy) * 2
    do = ImageDraw.Draw(art)
    shadow = Image.new("RGBA", art.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).text((wxc, wy + s(4 * k)), "WinTips", font=F["word"],
                                fill=(0, 0, 0, 110), anchor="mm")
    art.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(s(5 * k))))
    do.text((wxc, wy), "WinTips", font=F["word"], fill=(255, 255, 255, 255), anchor="mm")
    do.text((wxc, ty), L["tag_text"], font=F["tag"], fill=(224, 234, 248, 215), anchor="mm")

    # ---- Store 徽章 ----
    draw_store_badge(art, wxc, (cfg["badge_y"] - oy) * 2, k, L, F)

    # ---- 成图 ----
    if framed:
        wall.paste(art.convert("RGB"), (int(ART[0]), int(ART[1])))
        dd = ImageDraw.Draw(wall)
        fp = F["cap"]
        # 卡纸说明小字：拉丁字加宽字距，中文收窄，避免汉字被拉散
        caps = L["caption"]
        gaps = [s(3) if ord(c) < 128 else s(1.2) for c in caps]
        widths = [dd.textlength(c, font=fp) for c in caps]
        total = sum(widths) + sum(gaps[:-1])
        tyc = (ART[3] + FRAME[3] - MOLD) // 2
        tx0 = CW / 2 - total / 2
        for i, (c, wch) in enumerate(zip(caps, widths)):
            dd.text((tx0, tyc), c, font=fp, fill=CAPTION_COLOR + (255,), anchor="lm")
            tx0 += wch + gaps[i]
        out_img = wall
    else:
        out_img = art

    out = os.path.join(OUT_DIR, cfg["out"].replace(".png", L["suffix"] + ".png"))
    out_img.convert("RGB").resize((W, H), Image.LANCZOS).save(out, "PNG")
    print("saved:", out)


# ============================================================
def main():
    args = [a.lower() for a in sys.argv[1:]]
    lang = next((a for a in args if a in LANGS or a in ("all", "both")), "all")
    style = next((a for a in args if a in ("frame", "noframe", "both")), "both")

    codes = ["zh", "en"] if lang in ("all", "both") else [lang]
    if lang not in LANGS and lang not in ("all", "both"):
        raise SystemExit(f"unknown lang: {lang} (choose from {', '.join(LANGS)} or all)")

    for code in codes:
        for key, cfg in LAYOUTS.items():
            is_free = "noframe" in key
            if (style == "frame" and is_free) or (style == "noframe" and not is_free):
                continue
            F, L = init_fonts(code, cfg.get("k", 1.0))
            render(cfg, L, F)


if __name__ == "__main__":
    main()
