# -*- coding: utf-8 -*-
"""WinTips 微软商店截图（效果图，非真实截屏）

按应用真实尺寸与真实主题色，用代码画出一整套「屏幕截图」：
桌面壁纸 + 居中的 Win11 任务栏 + 亚克力浮窗 / 设置窗口。所有界面元素都取自
源码，而不是屏幕抓取：

  · 浮窗       UI/OsdWindow.xaml            160x50dip，圆角 8，指示条 60x3，图标 22
  · 设置窗口   UI/SettingsWindow.xaml       800dip 宽，两栏卡片 348 / 18 / *
  · 开关/滑块  Resources/Styles.xaml        开关 40x20、滑块轨道 4、拇指 18
  · 配色       Core/ThemeHelper.cs          卡片、描边、分隔线、强调色 #0067C0
  · 文案       Core/Localization.cs         运行时解析，随应用一起更新

输出：promo/store/*.png，1920x1080。文件名不带 -en 为中文版。

用法：
    python make_store_shots.py            # 中英各 4 张
    python make_store_shots.py zh         # 仅中文
    python make_store_shots.py en lock    # 仅英文的锁定键那张
"""
import io
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "promo", "store")

W, H = 1920, 1080          # 成图尺寸（微软商店要求 ≥1366x768，16:9）
SS = 2                     # 超采样倍率
SC = 1.5                   # 缩放系数：dip -> 成图像素，逐场景可调
VW, VH = W / SC, H / SC    # 桌面在 dip 下的尺寸（随 SC 变化）

ACCENT = (0, 120, 212)     # Win11 默认强调色（ThemeHelper 读到的就是它）
TASKBAR_H = 48             # Win11 任务栏高度（dip）


def P(v):
    """dip -> 画布像素"""
    return v * SC * SS


def use_scale(v):
    global SC, VW, VH
    SC = v
    VW, VH = W / SC, H / SC


# ---------------------------------------------------------------- 字体 / 文案
FD = os.path.join(ROOT, "Resources", "Fonts")
F_REG = os.path.join(FD, "HarmonyOS_Sans_SC_Regular.ttf")
F_MED = os.path.join(FD, "HarmonyOS_Sans_SC_Medium.ttf")
F_BLD = os.path.join(FD, "HarmonyOS_Sans_SC_Bold.ttf")
MDL2 = r"C:\Windows\Fonts\segmdl2.ttf"          # 任务栏与标题栏的图标字形
_FONTS = {}


def font(path, dip):
    key = (path, dip, SC)
    if key not in _FONTS:
        _FONTS[key] = ImageFont.truetype(path, max(6, int(round(P(dip)))))
    return _FONTS[key]


def ui(dip, weight="reg"):
    return font({"reg": F_REG, "med": F_MED, "bld": F_BLD}[weight], dip)


def icon_font(dip):
    return font(MDL2, dip)


def load_loc():
    """解析 Localization.cs 的 Add("Key", zh-Hans, zh-Hant, en, ...) 表"""
    txt = io.open(os.path.join(ROOT, "Core", "Localization.cs"), encoding="utf-8").read()
    table = {}
    for m in re.finditer(r'Add\("(\w+)",(.*?)\);', txt, re.S):
        vals = re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(2))
        if len(vals) >= 8:
            table[m.group(1)] = [v.replace('\\"', '"') for v in vals[:8]]
    return table


LOC = load_loc()


def T(key, lang="zh"):
    v = LOC.get(key)
    if not v:
        return key
    return v[0] if lang == "zh" else v[2]


def app_version():
    txt = io.open(os.path.join(ROOT, "WinTips.csproj"), encoding="utf-8").read()
    m = re.search(r"<Version>([\d.]+)</Version>", txt)
    return m.group(1) if m else "1.0"


# ---------------------------------------------------------------- 基础工具
def theme(dark):
    if dark:
        return dict(
            text=(255, 255, 255, 255), text2=(255, 255, 255, 199),
            divider=(255, 255, 255, 26), card=(255, 255, 255, 0x24),
            card_stroke=(255, 255, 255, 0x14), surface=(32, 32, 32, 255),
            switch_border=(255, 255, 255, 0x8A), switch_thumb=(255, 255, 255, 0xE6),
            slider_rest=(255, 255, 255, 0x45), input=(255, 255, 255, 0x14),
            input_stroke=(255, 255, 255, 0x1A), chevron=(255, 255, 255, 0xA6),
            thumb_ring=(43, 43, 43, 255), caption=(255, 255, 255, 255),
        )
    return dict(
        text=(25, 25, 25, 255), text2=(0, 0, 0, 199),
        divider=(0, 0, 0, 26), card=(255, 255, 255, 0xA8),
        card_stroke=(0, 0, 0, 0x1A), surface=(243, 243, 243, 255),
        switch_border=(0, 0, 0, 0x6E), switch_thumb=(26, 26, 26, 255),
        slider_rest=(0, 0, 0, 0x3D), input=(255, 255, 255, 0x66),
        input_stroke=(0, 0, 0, 0x1A), chevron=(0, 0, 0, 0x66),
        thumb_ring=(255, 255, 255, 255), caption=(0, 0, 0, 255),
    )


def blend(img, layer):
    """RGBA 图层 -> RGB 画布"""
    base = img.convert("RGBA")
    base.alpha_composite(layer)
    return base.convert("RGB")


def vgrad(size, top, bottom):
    m = Image.linear_gradient("L").resize(size)
    return Image.composite(Image.new("RGB", size, top), Image.new("RGB", size, bottom), m)


def radial_mask(size, cx, cy, rx, ry, power=1.0):
    """椭圆径向遮罩，中心 255 边缘 0"""
    m = Image.radial_gradient("L").resize((max(2, int(rx * 2)), max(2, int(ry * 2))))
    out = Image.new("L", size, 0)
    out.paste(m, (int(cx - rx), int(cy - ry)))
    if power != 1.0:
        out = out.point(lambda v: min(255, int((v / 255.0) ** power * 255)))
    return out


def shadow_round(img, box, radius, blur, alpha, dy):
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay).rounded_rectangle(
        [box[0], box[1] + dy, box[2], box[3] + dy], radius=radius, fill=(0, 0, 0, alpha))
    return blend(img, lay.filter(ImageFilter.GaussianBlur(blur)))


# ---------------------------------------------------------------- 壁纸
def wallpaper():
    """低分辨率逐像素算光场再放大：底色渐变 + 两团主光 + 三条花瓣光带 + 暗角。

    直接在高分辨率上画图层很容易出手接缝和糊块，先按 192x108 算好再双三次放大，
    渐变过渡干净，也顺便省时间。
    """
    import math
    w, h = W * SS, H * SS
    LW, LH = 192, 108
    small = Image.new("RGB", (LW, LH))
    px = small.load()
    for j in range(LH):
        v = (j + 0.5) / LH
        for i in range(LW):
            u = (i + 0.5) / LW
            t = v ** 0.85
            base = ((16 + 6 * (1 - t)) * 0.55 + 9,
                    (34 + 10 * (1 - t)) * 0.55 + 12,
                    (68 + 18 * (1 - t)) * 0.55 + 24)
            g1 = math.exp(-((((u - 0.30) / 0.40) ** 2 + ((v - 0.34) / 0.50) ** 2)) * 1.5)
            g2 = math.exp(-((((u - 0.86) / 0.34) ** 2 + ((v - 0.18) / 0.34) ** 2)) * 1.8)
            b1 = math.exp(-((u - 0.58 + 1.15 * (v - 0.42) ** 2) / 0.115) ** 2)
            b2 = math.exp(-((u - 0.20 + 0.75 * (v - 0.86) ** 2) / 0.100) ** 2)
            b3 = math.exp(-((u - 0.98 + 0.55 * (v - 0.98) ** 2) / 0.090) ** 2)
            r = base[0] + 50 * g1 + 24 * g2 + 30 * b1 + 17 * b2 + 15 * b3
            g = base[1] + 106 * g1 + 50 * g2 + 62 * b1 + 35 * b2 + 31 * b3
            b = base[2] + 168 * g1 + 84 * g2 + 108 * b1 + 60 * b2 + 53 * b3
            rr = ((u - 0.5) ** 2 * 1.05 + (v - 0.5) ** 2) ** 0.5
            k = 1.0 - 0.55 * max(0.0, rr - 0.28) ** 1.4 * 1.6
            px[i, j] = (max(0, min(255, int(r * k))),
                        max(0, min(255, int(g * k))),
                        max(0, min(255, int(b * k))))
    return small.resize((w, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(w * 0.004))


# ---------------------------------------------------------------- 任务栏
def tb_glyph(d, cx, cy, code, dip, col):
    d.text((P(cx), P(cy)), code, font=icon_font(dip), fill=col, anchor="mm")


def tb_start(d, cx, cy, dip=17):
    q = (dip - 1.6) / 2
    x, y = cx - dip / 2, cy - dip / 2
    for i, (dx, dy) in enumerate([(0, 0), (1, 0), (0, 1), (1, 1)]):
        col = [(0, 164, 239), (37, 180, 120), (255, 185, 0), (242, 80, 34)][i]
        col = (0, 150, 220)  # 真实的开始按钮是单色蓝，这里统一
        d.rounded_rectangle([P(x + dx * (q + 1.6)), P(y + dy * (q + 1.6)),
                             P(x + dx * (q + 1.6) + q), P(y + dy * (q + 1.6) + q)],
                            radius=P(0.6), fill=col + (255,))


def tb_folder(d, cx, cy, dip=22):
    x, y = cx - dip / 2, cy - dip / 2
    d.rounded_rectangle([P(x), P(y + dip * 0.12), P(x + dip * 0.44), P(y + dip * 0.34)],
                        radius=P(1.2), fill=(214, 155, 30, 255))
    d.rounded_rectangle([P(x), P(y + dip * 0.22), P(x + dip), P(y + dip * 0.88)],
                        radius=P(2), fill=(252, 199, 78, 255))
    d.rounded_rectangle([P(x + 1.2), P(y + dip * 0.34), P(x + dip - 1.2), P(y + dip * 0.80)],
                        radius=P(1.4), fill=(255, 214, 120, 255))


def tb_terminal(d, cx, cy, dip=22):
    x, y = cx - dip / 2, cy - dip / 2
    d.rounded_rectangle([P(x), P(y), P(x + dip), P(y + dip)], radius=P(3.2), fill=(28, 28, 30, 255))
    d.text((P(cx), P(cy + 0.6)), ">_", font=font(r"C:\Windows\Fonts\consola.ttf", dip * 0.5),
           fill=(235, 235, 235, 255), anchor="mm")


def tb_taskview(d, cx, cy, col, bg):
    """任务视图：后窗被前窗挡住，用任务栏底色挖出遮挡关系"""
    sw = max(1, int(P(1.5)))
    d.rounded_rectangle([P(cx - 3), P(cy - 9), P(cx + 9), P(cy + 3)],
                        radius=P(2), outline=col, width=sw)
    d.rounded_rectangle([P(cx - 11), P(cy - 8), P(cx + 4), P(cy + 8)], radius=P(2), fill=bg)
    d.rounded_rectangle([P(cx - 9), P(cy - 6), P(cx + 2), P(cy + 6)],
                        radius=P(2), outline=col, width=sw)


def tb_battery(d, cx, cy, col):
    d.rounded_rectangle([P(cx - 10), P(cy - 5.5), P(cx + 8), P(cy + 5.5)],
                        radius=P(2.4), outline=col, width=max(1, int(P(1.4))))
    d.rounded_rectangle([P(cx + 8.6), P(cy - 2.4), P(cx + 10.4), P(cy + 2.4)],
                        radius=P(1), fill=col)
    d.rounded_rectangle([P(cx - 8.2), P(cy - 3.4), P(cx + 2.4), P(cy + 3.4)],
                        radius=P(1.2), fill=col)


def draw_taskbar(img, dark):
    y0 = VH - TASKBAR_H
    # 亚克力：先模糊条带，再叠着色
    box = (0, int(P(y0 - 8)), W * SS, H * SS)
    crop = img.crop(box).filter(ImageFilter.GaussianBlur(P(28)))
    img.paste(crop, box[:2])
    tint = (30, 30, 30, 216) if dark else (242, 242, 242, 224)
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay).rectangle([0, P(y0), W * SS, H * SS], fill=tint)
    img = blend(img, lay)

    d = ImageDraw.Draw(img, "RGBA")
    d.line([(0, P(y0)), (W * SS, P(y0))], width=max(1, int(P(1))),
           fill=(255, 255, 255, 20) if dark else (0, 0, 0, 16))
    cy = y0 + TASKBAR_H / 2
    col = (245, 245, 245, 255) if dark else (30, 30, 30, 255)

    # 居中图标簇
    items = ["start", "search", "taskview", "explorer", "terminal", "settings"]
    slot = 44
    left = VW / 2 - len(items) * slot / 2
    for i, it in enumerate(items):
        cx = left + slot * (i + 0.5)
        if it == "start":
            tb_start(d, cx, cy)
        elif it == "search":
            tb_glyph(d, cx, cy, "\uE721", 17, col)
        elif it == "taskview":
            bg = img.getpixel((int(P(cx - 12)), int(P(cy))))
            tb_taskview(d, cx, cy, col, bg)
        elif it == "explorer":
            tb_folder(d, cx, cy)
        elif it == "terminal":
            tb_terminal(d, cx, cy)
        elif it == "settings":
            tb_glyph(d, cx, cy, "\uE713", 17, (150, 150, 150, 255) if not dark else (210, 210, 210, 255))

    # 右侧托盘
    rx = VW - 12
    clock_f = font(r"C://Windows//Fonts//segoeui.ttf", 12)
    date_f = font(r"C://Windows//Fonts//segoeui.ttf", 12)
    tlines = ["11:22", "2026/11/11"]
    wtime = d.textlength(tlines[0], font=clock_f) / P(1)
    wdate = d.textlength(tlines[1], font=date_f) / P(1)
    rx -= max(wtime, wdate)
    d.text((P(rx + max(wtime, wdate)), P(cy - 8)), tlines[0], font=clock_f, fill=col, anchor="rm")
    d.text((P(rx + max(wtime, wdate)), P(cy + 8)), tlines[1], font=date_f, fill=col, anchor="rm")
    rx -= 10

    for code, kind in [("", "battery"), ("\uE767", "glyph"), ("\uE701", "glyph"), ("\uE70E", "glyph")]:
        rx -= 22
        if kind == "battery":
            tb_battery(d, rx + 11, cy, col)
        else:
            tb_glyph(d, rx + 11, cy, code, 14, col)
    return img


# ---------------------------------------------------------------- 浮窗
def lock_layer(size_px, color, on):
    k = size_px / 24.0
    lay = Image.new("RGBA", (size_px, size_px), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    sw = max(2, int(round(1.5 * k)))
    d.rounded_rectangle([4 * k, 7 * k, 20 * k, 21 * k], radius=3.25 * k, outline=color, width=sw)
    d.ellipse([12 * k - 1.6 * k, 15.4 * k - 1.6 * k, 12 * k + 1.6 * k, 15.4 * k + 1.6 * k], fill=color)

    sh = Image.new("RGBA", (size_px, size_px), (0, 0, 0, 0))
    ds = ImageDraw.Draw(sh)
    ds.line([8 * k, 7.2 * k, 8 * k, 5 * k], fill=color, width=sw)
    ds.arc([8 * k, 1 * k, 16 * k, 9 * k], 180, 360, fill=color, width=sw)
    ds.line([16 * k, 5 * k, 16 * k, 7.2 * k], fill=color, width=sw)
    if not on:
        sh = sh.rotate(-25, resample=Image.BICUBIC, center=(16 * k, 7 * k))
    lay.alpha_composite(sh)
    return lay


def check_layer(size_px, color):
    k = size_px / 24.0
    lay = Image.new("RGBA", (size_px, size_px), (0, 0, 0, 0))
    ImageDraw.Draw(lay).line([(5 * k, 12.5 * k), (10 * k, 17.5 * k), (19 * k, 7 * k)],
                             fill=color, width=max(2, int(2.4 * k)), joint="curve")
    return lay


def osd_width(text, fnt, d):
    tw = d.textlength(text, font=fnt) / P(1)
    return max(160.0, tw + 78)


def draw_osd(img, cx, top, dark, text, kind="lock", on=True, count=None):
    f_txt = ui(14, "med")
    probe = ImageDraw.Draw(img)
    w = osd_width(text, f_txt, probe)
    h = 50.0
    x0, y0 = cx - w / 2, top
    r = 8.0

    box = (P(x0), P(y0), P(x0 + w), P(y0 + h))
    img = shadow_round(img, box, P(r), P(6), 110, P(4))
    img = shadow_round(img, box, P(r), P(18), 80, P(12))

    # 玻璃：模糊背后的画面再调亮 + 着色
    pad = P(26)
    reg = (int(box[0] - pad), int(box[1] - pad), int(box[2] + pad), int(box[3] + pad))
    crop = img.crop(reg).filter(ImageFilter.GaussianBlur(P(13)))
    from PIL import ImageEnhance
    crop = ImageEnhance.Brightness(crop).enhance(1.55 if not dark else 0.72)
    mask = Image.new("L", crop.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [pad, pad, pad + box[2] - box[0], pad + box[3] - box[1]], radius=P(r), fill=255)
    img.paste(crop, (reg[0], reg[1]), mask)

    tint = (248, 250, 253, 152) if not dark else (40, 40, 40, 178)
    lay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(lay).rounded_rectangle(box, radius=P(r), fill=tint)
    img = blend(img, lay)

    d = ImageDraw.Draw(img, "RGBA")
    edge = (255, 255, 255, 132) if not dark else (255, 255, 255, 56)
    d.rounded_rectangle(box, radius=P(r), outline=edge, width=max(1, int(P(1))))
    d.rounded_rectangle([box[0] + P(1.5), box[1] + P(1.5), box[2] - P(1.5), box[3] - P(1.5)],
                        radius=P(r - 1.5), outline=(10, 20, 40, 26), width=max(1, int(P(1))))

    col = (26, 26, 26, 255) if not dark else (255, 255, 255, 255)
    ic = P(22)
    ix, iy = box[0] + P(16), box[1] + P((h - 22) / 2) - P(1)
    if kind == "lock":
        lay = lock_layer(int(ic), col, on)
    elif kind == "check":
        lay = check_layer(int(ic), col)
    img.paste(lay, (int(ix), int(iy)), lay)

    # 文案：在图标右侧的剩余区域居中
    area0 = x0 + 16 + 22 + 18
    area1 = x0 + w - 12 - (26 if count else 0)
    tx = (area0 + area1) / 2
    d.text((P(tx), P(y0 + h / 2 - 1)), text, font=f_txt, fill=col, anchor="mm")

    if count:
        f_cnt = ui(12.5, "bld")
        cw = d.textlength("×%d" % count, font=f_cnt) / P(1) + 14
        bh = 19.0
        bx1 = x0 + w - 12
        bx0 = bx1 - cw
        by0 = y0 + (h - bh) / 2
        d.rounded_rectangle([P(bx0), P(by0), P(bx1), P(by0 + bh)], radius=P(9.5),
                            fill=ACCENT + (255,))
        d.text((P((bx0 + bx1) / 2), P(by0 + bh / 2)), "×%d" % count, font=f_cnt,
               fill=(255, 255, 255, 255), anchor="mm")

    # 底部指示条
    bw = 60.0 if on else 36.0
    bh2 = 3.0
    bx = cx - bw / 2
    by = y0 + h - 7 - bh2
    d.rounded_rectangle([P(bx), P(by), P(bx + bw), P(by + bh2)], radius=P(1.5),
                        fill=ACCENT + (255 if on else 51,))
    return img


# ---------------------------------------------------------------- 窗口通用
class Pnt:
    """在窗口局部坐标（dip）上作画"""

    def __init__(self, img):
        self.img = img
        self.d = ImageDraw.Draw(img, "RGBA")

    def box(self, b, r=0, fill=None, outline=None, w=1):
        b2 = [P(b[0]), P(b[1]), P(b[2]), P(b[3])]
        wid = max(1, int(round(P(w))))
        if r:
            self.d.rounded_rectangle(b2, radius=P(r), fill=fill, outline=outline, width=wid)
        else:
            self.d.rectangle(b2, fill=fill, outline=outline, width=wid)

    def text(self, xy, s, fnt, fill, anchor="la"):
        self.d.text((P(xy[0]), P(xy[1])), s, font=fnt, fill=fill, anchor=anchor)

    def tw(self, s, fnt):
        return self.d.textlength(s, font=fnt) / P(1)


def _tokens(text):
    """切成折行单元：西文按词、中日韩按字、空格单独成块"""
    out, buf = [], ""
    for ch in text:
        if ch == " ":
            if buf:
                out.append(buf)
                buf = ""
            out.append(" ")
        elif ord(ch) > 0x2E80:      # CJK 与全角标点：逐字可断
            if buf:
                out.append(buf)
                buf = ""
            out.append(ch)
        else:
            buf += ch
    if buf:
        out.append(buf)
    return out


def wrap(p, text, fnt, maxw):
    """按像素宽度折行，西文不切断单词（超长单词才硬断）"""
    if not text:
        return []
    lines, cur = [], ""
    for tok in _tokens(text):
        cand = cur + tok
        if p.tw(cand, fnt) <= maxw:
            cur = cand
            continue
        if tok == " ":
            lines.append(cur)
            cur = ""
            continue
        if cur.strip():
            lines.append(cur.rstrip())
            cur = ""
        # 单词本身就超宽时逐字硬断
        for ch in tok:
            if p.tw(cur + ch, fnt) > maxw and cur:
                lines.append(cur)
                cur = ""
            cur += ch
    if cur.strip():
        lines.append(cur.rstrip())
    # 中文避头点：行首的收尾标点并回上一行（允许轻微出血）
    no_lead = "。，、；：！？）》」』”’%"
    for i in range(1, len(lines)):
        while lines[i] and lines[i][0] in no_lead:
            lines[i - 1] += lines[i][0]
            lines[i] = lines[i][1:]
    return [ln for ln in lines if ln]


def line_h(fnt):
    a, b = fnt.getmetrics()
    return (a + b) / P(1)


def mica(img, box, dark, radius=8):
    """窗口底色：取壁纸强烈模糊后叠一层浅灰，接近 Win11 Mica（返回 RGB）"""
    reg = img.crop(box).filter(ImageFilter.GaussianBlur(P(48))).convert("RGB")
    tint = (243, 243, 243) if not dark else (32, 32, 32)
    return Image.blend(reg, Image.new("RGB", reg.size, tint), 0.90)


def paste_window(img, win, x, y, radius=8):
    mask = Image.new("L", win.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, win.size[0] - 1, win.size[1] - 1],
                                           radius=P(radius), fill=255)
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sh.paste((0, 0, 0, 96), (int(x), int(y + P(10))), mask)
    img = blend(img, sh.filter(ImageFilter.GaussianBlur(P(20))))
    sh2 = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sh2.paste((0, 0, 0, 60), (int(x), int(y + P(26))), mask)
    img = blend(img, sh2.filter(ImageFilter.GaussianBlur(P(46))))
    img.paste(win, (int(x), int(y)), mask)
    return img


def caption_buttons(p, win_w, dark, disabled_max=True, bar_h=32):
    """右上角最小化 / 最大化 / 关闭"""
    col = (255, 255, 255, 255) if dark else (30, 30, 30, 255)
    dim = (255, 255, 255, 100) if dark else (30, 30, 30, 90)
    bw = 46
    for i, (code, c) in enumerate([("\uE921", col),
                                   ("\uE922", dim if disabled_max else col),
                                   ("\uE8BB", col)]):
        cx = win_w - bw * (2.5 - i)
        p.d.text((P(cx), P(bar_h / 2)), code, font=icon_font(10), fill=c, anchor="mm")


# ---------------------------------------------------------------- 设置窗口
SW_W = 800          # 窗口宽度（dip）
PAD_L, PAD_R = 26, 26
COL1 = 348
GAP = 18


def sec_label_h():
    return 20 + 7


def rows_card1(lang):
    return [
        dict(kind="switch", on=True, title=T("KeyCapsLockLong", lang)),
        dict(kind="switch", on=True, title=T("KeyNumLockLong", lang)),
        dict(kind="switch", on=True, title=T("KeyScrollLockLong", lang)),
        dict(kind="switch", on=True, title=T("RowInsert", lang)),
        dict(kind="switch", on=True, title=T("RowCopy", lang)),
        dict(kind="switch", on=True, title=T("RowPaste", lang)),
        dict(kind="switch", on=True, title=T("RowCut", lang)),
        dict(kind="switch", on=True, title=T("RowAnyCopy", lang), desc=T("RowAnyCopyDesc", lang)),
        dict(kind="switch", on=False, title=T("RowLanguageTip", lang), desc=T("RowLanguageTipDesc", lang)),
        dict(kind="switch", on=True, title=T("RowFocusMonitor", lang), desc=T("RowFocusMonitorDesc", lang)),
        dict(kind="switch", on=True, title=T("RowGlass", lang), desc=T("RowGlassDesc", lang), desc_below=True),
        dict(kind="slider", title=T("RowGlassOpacity", lang), value="55%", ratio=(0.55 - 0.2) / 0.7),
    ]


def rows_card2(lang):
    return [
        dict(kind="slider", title=T("RowDuration", lang),
             value=T("ValueSeconds", lang).format(2), ratio=(2.0 - 0.5) / 4.5),
        dict(kind="slider", title=T("RowAnimSpeed", lang),
             value=T("ValueAnimSpeed", lang).format(1, 180), ratio=1 / 3),
        dict(kind="slider", title=T("RowTipScale", lang), value="100%", ratio=(1.0 - 0.6) / 1.4,
             button=T("BtnResetSize", lang), desc=T("RowTipScaleDesc", lang)),
        dict(kind="seg", title=T("RowPosition", lang),
             items=[T("PositionTop", lang), T("PositionBottom", lang)],
             items2=[T("PositionLeft", lang), T("PositionCenter", lang), T("PositionRight", lang)],
             checked=0),
        dict(kind="button", title=T("RowCustomPos", lang),
             button=T("BtnAdjustPos", lang), button2=T("BtnResetPos", lang),
             desc=T("RowCustomPosDesc", lang)),
        dict(kind="switch", on=False, title=T("RowStartup", lang)),
        dict(kind="switch", on=True, title=T("RowLanguage", lang), right_label=T("LangSystem", lang),
             combo=("简体中文" if lang == "zh" else "English")),
        dict(kind="combo", title=T("RowUiFont", lang),
             combo=T("FontChoiceDefault", lang), desc=T("RowUiFontDesc", lang)),
    ]


def card(p, rows, x, y, w, dark, th, force_h=None):
    """画（或量）一张卡片，返回卡片高度"""
    inner = w - 32
    f14 = ui(14)
    f115 = ui(11.5)
    lh14, lh115 = line_h(f14) * 1.06, line_h(f115) * 1.16
    mp = p if p is not None else Pnt(Image.new("RGBA", (8, 8)))

    # --- 先量高度 ---
    hh = 12
    for i, r in enumerate(rows):
        hh += row_height(mp, r, inner, f14, f115, lh14, lh115)
        if i < len(rows) - 1:
            hh += 7
    hh += 12
    if force_h:
        hh = force_h
    if p is None:
        return hh

    p.box([x, y, x + w, y + hh], r=8, fill=th["card"], outline=th["card_stroke"], w=1)
    cy = y + 6
    for i, r in enumerate(rows):
        rh = row_height(mp, r, inner, f14, f115, lh14, lh115)
        draw_row(p, r, x + 16, cy, inner, rh, dark, th, f14, f115, lh14, lh115)
        cy += rh
        if i < len(rows) - 1:
            p.box([x + 16, cy + 3.25, x + w - 16, cy + 4.0], fill=th["divider"])
            cy += 7
    return hh


def row_height(p, r, inner, f14, f115, lh14, lh115):
    kind = r["kind"]
    lw = inner - 58
    if kind == "switch":
        if r.get("desc_below"):
            lines = wrap(p, r["desc"], f115, inner - 58)
            return max(34, lh14 + 8) + 3 + lh115 * max(1, len(lines)) + 8
        if r.get("desc"):
            lines = wrap(p, r["desc"], f115, lw)
            return max(34, lh14 + 3 + lh115 * max(1, len(lines)) + 8)
        if r.get("combo") and r.get("right_label"):
            return 76
        return 34
    if kind == "slider":
        h = lh14 + 5 + 22 + 6
        if r.get("desc"):
            h += 3 + lh115 * len(wrap(p, r["desc"], f115, inner))
        return h
    if kind == "seg":
        return lh14 + 10 + 30 + 10
    if kind == "button":
        h = 30 + 4
        if r.get("desc"):
            h += 3 + lh115 * len(wrap(p, r["desc"], f115, inner))
        return h + 8
    if kind == "combo":
        h = lh14 + 8 + 32 + 6
        if r.get("desc"):
            h += 3 + lh115 * len(wrap(p, r["desc"], f115, inner))
        return h
    return 34


def draw_switch(p, x, y, w_on, th):
    """40x20 开关，y 为轨道顶边"""
    p.box([x, y, x + 40, y + 20], r=10,
          fill=ACCENT + (255,) if w_on else None,
          outline=None if w_on else th["switch_border"], w=1)
    cx = x + 4 + (20 if w_on else 0)
    p.d.ellipse([P(cx), P(y + 4), P(cx + 12), P(y + 16)],
                fill=(255, 255, 255, 255) if w_on else th["switch_thumb"])


def draw_slider(p, x, y, w, ratio, th):
    """4dip 轨道 + 18dip 拇指，y 为轨道中线"""
    p.box([x, y - 2, x + w, y + 2], r=2, fill=th["slider_rest"])
    p.box([x, y - 2, x + w * ratio, y + 2], r=2, fill=ACCENT + (255,))
    cx = x + w * ratio
    p.d.ellipse([P(cx - 9), P(y - 9), P(cx + 9), P(y + 9)], fill=th["thumb_ring"],
                outline=(0, 0, 0, 20), width=max(1, int(P(1))))
    p.d.ellipse([P(cx - 6), P(y - 6), P(cx + 6), P(y + 6)], fill=ACCENT + (255,))


def draw_button(p, x, y, text, th, dark):
    f13 = ui(13)
    w = p.tw(text, f13) + 26
    p.box([x, y, x + w, y + 30], r=4, fill=th["input"], outline=th["input_stroke"], w=1)
    p.text((x + w / 2, y + 15), text, f13, th["text"], anchor="mm")
    return w


def draw_combo(p, x, y, w, text, th, h=32):
    f13 = ui(13)
    p.box([x, y, x + w, y + h], r=4, fill=th["input"], outline=th["input_stroke"], w=1)
    p.text((x + 11, y + h / 2), text, f13, th["text"], anchor="lm")
    p.d.text((P(x + w - 14), P(y + h / 2)), "\uE70D", font=icon_font(8), fill=th["chevron"], anchor="mm")


def draw_seg(p, x, y, items, checked, th):
    f13 = ui(13)
    cx = x
    for i, it in enumerate(items):
        w = p.tw(it, f13) + 24
        on = (i == checked)
        p.box([cx, y, cx + w, y + 30], r=6, fill=ACCENT + (255,) if on else None)
        p.text((cx + w / 2, y + 15), it, f13, (255, 255, 255, 255) if on else th["text"], anchor="mm")
        cx += w + 6
    return cx - x


def draw_row(p, r, x, y, w, rh, dark, th, f14, f115, lh14, lh115):
    kind = r["kind"]
    if kind == "switch":
        tcy = y + 3 + max(lh14, 20) / 2
        if not (r.get("combo") and r.get("right_label")):
            draw_switch(p, x, y + 3, r["on"], th)
        if r.get("desc_below"):
            p.text((x + 58, tcy), r["title"], f14, th["text"], anchor="lm")
            lines = wrap(p, r["desc"], f115, w - 58)
            ty = y + 34 + 4
            for ln in lines:
                p.text((x + 58, ty + lh115 / 2), ln, f115, th["text2"], anchor="lm")
                ty += lh115
        elif r.get("desc"):
            p.text((x + 58, tcy), r["title"], f14, th["text"], anchor="lm")
            lines = wrap(p, r["desc"], f115, w - 58)
            ty = y + 3 + lh14 + 3 + lh115 / 2
            for ln in lines:
                p.text((x + 58, ty), ln, f115, th["text2"], anchor="lm")
                ty += lh115
        elif r.get("combo") and r.get("right_label"):
            p.text((x, tcy), r["title"], f14, th["text"], anchor="lm")
            f13 = ui(13)
            lw = p.tw(r["right_label"], f13)
            sx = x + w - 40
            draw_switch(p, sx, y + 3, True, th)
            p.text((sx - 8, y + 3 + 10), r["right_label"], f13, th["text2"], anchor="rm")
            draw_combo(p, x, y + 31, w, r["combo"], th)
        else:
            p.text((x + 58, tcy), r["title"], f14, th["text"], anchor="lm")
    elif kind == "slider":
        p.text((x, y + lh14 / 2), r["title"], f14, th["text"], anchor="lm")
        if r.get("button"):
            f13 = ui(13)
            bw = p.tw(r["button"], f13) + 26
            draw_button(p, x + w - bw, y + lh14 / 2 - 15, r["button"], th, dark)
            p.text((x + w - bw - 10, y + lh14 / 2), r["value"], f14, th["text2"], anchor="rm")
        else:
            p.text((x + w, y + lh14 / 2), r["value"], f14, th["text2"], anchor="rm")
        sy = y + lh14 + 5 + 11
        draw_slider(p, x + 9, sy, w - 18, r["ratio"], th)
        if r.get("desc"):
            ty = sy + 11 + 5
            for ln in wrap(p, r["desc"], f115, w):
                p.text((x, ty), ln, f115, th["text2"], anchor="la")
                ty += lh115
    elif kind == "seg":
        p.text((x, y + lh14 / 2), r["title"], f14, th["text"], anchor="lm")
        draw_seg(p, x, y + lh14 + 10, r["items"], r["checked"], th)
        # 第二组（水平方向）接在第一组后面
        dw = 0
        for i, it in enumerate(r["items"]):
            dw += p.tw(it, ui(13)) + 24 + 6
        draw_seg(p, x + dw + 6, y + lh14 + 10, r["items2"], -1, th)
    elif kind == "button":
        p.text((x, y + lh14 / 2), r["title"], f14, th["text"], anchor="lm")
        f13 = ui(13)
        bw2 = p.tw(r["button2"], f13) + 26
        bw1 = p.tw(r["button"], f13) + 26
        x1 = x + w - bw2
        x0 = x1 - 8 - bw1
        draw_button(p, x0, y + lh14 / 2 - 15, r["button"], th, dark)
        draw_button(p, x1, y + lh14 / 2 - 15, r["button2"], th, dark)
        ty = y + 30 + 6
        for ln in wrap(p, r["desc"], f115, w):
            p.text((x, ty), ln, f115, th["text2"], anchor="la")
            ty += lh115
    elif kind == "combo":
        p.text((x, y + lh14 / 2), r["title"], f14, th["text"], anchor="lm")
        draw_combo(p, x, y + lh14 + 8, w, r["combo"], th)
        ty = y + lh14 + 8 + 32 + 5
        for ln in wrap(p, r["desc"], f115, w):
            p.text((x, ty), ln, f115, th["text2"], anchor="la")
            ty += lh115


def render_settings(img, dark, lang):
    th = theme(dark)
    f20 = ui(20, "bld")
    f125 = ui(12.5)
    f13 = ui(13)
    f115 = ui(11.5)

    r1 = rows_card1(lang)
    r2 = rows_card2(lang)
    c1w = COL1
    c2w = SW_W - PAD_L - PAD_R - COL1 - GAP

    # 量高
    h1 = card(None, r1, 0, 0, c1w, dark, th)
    h2 = card(None, r2, 0, 0, c2w, dark, th)
    card_h = max(h1, h2)

    sub_lines = wrap(Pnt(Image.new("RGBA", (8, 8))), T("SettingsSubtitle", lang), f125,
                     SW_W - PAD_L - PAD_R)

    win_h = (32 + 20 + line_h(f20) + 4 + line_h(f125) * len(sub_lines) + 20
             + sec_label_h() + card_h + 20 + line_h(f115) + 22)

    win = Image.new("RGB", (int(P(SW_W)), int(P(win_h))), th["surface"][:3])
    win.paste(mica(img, (0, 0, int(P(SW_W)), int(P(win_h))), dark), (0, 0))

    p = Pnt(win)
    # 标题栏
    title = T("SettingsTitle", lang)
    p.text((16, 32 / 2), title, ui(11.5), th["text"], anchor="lm")
    caption_buttons(p, SW_W, dark)

    y = 32 + 20
    p.text((PAD_L, y), T("SettingsHeader", lang), f20, th["text"], anchor="la")
    y += line_h(f20) + 4
    for ln in sub_lines:
        p.text((PAD_L, y), ln, f125, th["text2"], anchor="la")
        y += line_h(f125)
    y += 20

    # 分组标题
    p.text((PAD_L + 1, y + 9), T("SectionKeys", lang), f13, th["text"], anchor="lm")
    p.text((PAD_L + COL1 + GAP + 1, y + 9), T("SectionGeneral", lang), f13, th["text"], anchor="lm")
    y += sec_label_h()

    card(p, r1, PAD_L, y, c1w, dark, th, card_h)
    card(p, r2, PAD_L + COL1 + GAP, y, c2w, dark, th, card_h)
    y += card_h + 20

    # 窗口外描边
    p.box([0.5, 0.5, SW_W - 0.5, win_h - 0.5], r=8, outline=(0, 0, 0, 40) if not dark else (255, 255, 255, 26), w=1)

    # 页脚
    foot = "V%s · By NatureQ" % app_version()
    fw = p.tw(foot, f115)
    p.text(((SW_W - fw) / 2, y), foot, f115, ACCENT + (255,), anchor="la")

    return win, SW_W, win_h


# ---------------------------------------------------------------- 笔记窗口
def render_notepad(img, dark, lang):
    th = theme(dark)
    pw, ph = 880, 404
    win = Image.new("RGB", (int(P(pw)), int(P(ph))), th["surface"][:3])
    win.paste(mica(img, (0, 0, int(P(pw)), int(P(ph))), dark), (0, 0))

    p = Pnt(win)
    # 标签栏 + 标题
    p.text((18, 16), "笔记.txt" if lang == "zh" else "notes.txt", ui(11.5), th["text"], anchor="lm")
    caption_buttons(p, pw, dark)

    # 编辑区
    p.box([8, 40, pw - 8, ph - 8], r=6, fill=(255, 255, 255, 255) if not dark else (40, 40, 40, 255))

    f = ui(13.5)
    lh = 25
    lines = (["快捷键备忘", "",
              "Ctrl + C     复制",
              "Ctrl + V     粘贴",
              "Ctrl + X     剪切", "",
              "数字锁定开启后，小键盘才能输入数字。",
              "大写锁定开启时，字母会全部变成大写。",
              "连续复制同一内容，浮窗上的角标会累加次数。"] if lang == "zh" else
             ["Shortcuts", "",
              "Ctrl + C     Copy",
              "Ctrl + V     Paste",
              "Ctrl + X     Cut", "",
              "Num Lock must be on for the numeric keypad.",
              "Caps Lock turns every letter into uppercase.",
              "Repeated copies add a counter to the flyout."])
    # 选中第三行（Ctrl+C），交代这次复制从哪来
    sel = 2
    tw = p.tw(lines[sel], f)
    top = 40 + 20 + sel * lh
    p.box([18, top + 1, 20 + tw + 8, top + lh - 3], r=0, fill=ACCENT + (72,))

    ty = 40 + 20
    for ln in lines:
        if ln:
            p.text((26, ty + lh / 2), ln, f, th["text"], anchor="lm")
        ty += lh
    return win, pw, ph


# ---------------------------------------------------------------- 桌面图标
def draw_desktop_icons(img, dark):
    """左侧一列桌面图标：应用本体 + 文件夹 + 文本 + 回收站"""
    d = ImageDraw.Draw(img, "RGBA")
    f = ui(11.5)
    col = (255, 255, 255, 255)
    label_shadow = (0, 0, 0, 120)
    app_icon = Image.open(os.path.join(ROOT, "Resources", "app-icon-512.png")).convert("RGBA")

    items = [
        ("app", "WinTips"),
        ("folder", "项目" if lang_zh() else "Projects"),
        ("txt", "说明.txt" if lang_zh() else "readme.txt"),
        ("png", "截图.png" if lang_zh() else "screenshot.png"),
        ("bin", "回收站" if lang_zh() else "Recycle Bin"),
    ]
    ic = 48.0
    x = 30
    y = 22
    step = 94
    for i, (kind, label) in enumerate(items):
        cy = y + i * step
        if kind == "app":
            im = app_icon.resize((int(P(ic - 6)), int(P(ic - 6))), Image.LANCZOS)
            img.paste(im, (int(P(x + 3)), int(P(cy + 3))), im)
        elif kind == "folder":
            # 文件夹：后片 + 前片 + 顶部高光
            d.rounded_rectangle([P(x + 4), P(cy + 9), P(x + ic * 0.52), P(cy + 20)],
                                radius=P(2.2), fill=(226, 168, 56, 255))
            d.rounded_rectangle([P(x + 4), P(cy + 15), P(x + ic - 4), P(cy + ic - 8)],
                                radius=P(3), fill=(255, 198, 78, 255))
            d.rectangle([P(x + 5), P(cy + 16), P(x + ic - 5), P(cy + 20)],
                        fill=(255, 219, 130, 255))
        elif kind == "txt":
            d.rounded_rectangle([P(x + 9), P(cy + 4), P(x + ic - 9), P(cy + ic - 5)],
                                radius=P(2), fill=(250, 250, 252, 250))
            for k in range(4):
                d.line([P(x + 14), P(cy + 13 + k * 6.4), P(x + ic - 14), P(cy + 13 + k * 6.4)],
                       fill=(150, 160, 175, 235), width=max(1, int(P(1.3))))
        elif kind == "png":
            d.rounded_rectangle([P(x + 6), P(cy + 6), P(x + ic - 6), P(cy + ic - 8)],
                                radius=P(3), fill=(236, 244, 252, 250))
            d.rounded_rectangle([P(x + 9), P(cy + 9), P(x + ic - 9), P(cy + ic - 19)],
                                radius=P(2), fill=(120, 178, 232, 255))
            d.polygon([(P(x + 9), P(cy + ic - 19)), (P(x + 20), P(cy + 20)),
                       (P(x + 30), P(cy + ic - 19))], fill=(88, 142, 200, 255))
            d.ellipse([P(x + ic - 22), P(cy + 12), P(x + ic - 14), P(cy + 20)],
                      fill=(255, 240, 190, 255))
        else:
            bin_col = (198, 210, 226, 255)
            d.rounded_rectangle([P(x + 10), P(cy + 12), P(x + ic - 10), P(cy + 14.5)],
                                radius=P(1.2), fill=bin_col)
            d.rounded_rectangle([P(x + 16), P(cy + 8), P(x + ic - 16), P(cy + 12)],
                                radius=P(1.4), fill=bin_col)
            d.polygon([(P(x + 12), P(cy + 16)), (P(x + ic - 12), P(cy + 16)),
                       (P(x + ic - 16), P(cy + ic - 6)), (P(x + 16), P(cy + ic - 6))],
                      fill=(176, 192, 212, 255))
            for k in range(2):
                d.line([P(x + 18 + k * 8), P(cy + 19), P(x + 17 + k * 8), P(cy + ic - 10)],
                       fill=(214, 226, 240, 255), width=max(1, int(P(1.4))))

        tw = d.textlength(label, font=f) / P(1)
        tx = x + ic / 2
        d.text((P(tx + 1), P(cy + ic + 10 + 1)), label, font=f, fill=label_shadow, anchor="mm")
        d.text((P(tx), P(cy + ic + 10)), label, font=f, fill=col, anchor="mm")


def lang_zh():
    return _LANG[0] == "zh"


_LANG = ["zh"]


# ---------------------------------------------------------------- 终端窗口
def render_terminal(img, dark):
    pw, ph = 900, 392
    # 终端背景：壁纸模糊 + 深色压住，接近真实 Windows Terminal 的亚克力
    reg = img.crop((0, 0, int(P(pw)), int(P(ph)))).filter(ImageFilter.GaussianBlur(P(40)))
    win = Image.blend(reg.convert("RGB"), Image.new("RGB", reg.size, (12, 12, 16)), 0.96)

    p = Pnt(win)
    # 标签栏 + 单个标签页
    p.box([0, 0, pw, 36], fill=(32, 32, 36, 255))
    p.box([9, 7, 186, 36], r=6, fill=(45, 45, 50, 255))
    p.d.rectangle([P(9), P(28), P(186), P(36)], fill=(45, 45, 50, 255))
    p.d.text((P(25), P(21)), "\uE756", font=icon_font(10.5), fill=(190, 205, 225, 255), anchor="lm")
    p.text((41, 21), "PowerShell", ui(12), (238, 240, 246, 255), anchor="lm")
    p.d.text((P(168), P(21)), "\uE8BB", font=icon_font(7), fill=(185, 190, 200, 255), anchor="mm")
    caption_buttons(p, pw, True, disabled_max=True, bar_h=36)

    f = font(r"C:\Windows\Fonts\consola.ttf", 14)
    lh = 25
    prompt = "PS D:\\WinTips> "
    lines = [
        (prompt, "dotnet publish -c Release -r win-x64 -o publish", (236, 238, 244)),
        ("  WinTips -> D:\\WinTips\\publish\\WinTips.exe", "", (146, 154, 168)),
        ("  Publish succeeded in 3.42s.", "", (146, 154, 168)),
        ("", "", (236, 238, 244)),
        (prompt, ".\\publish\\WinTips.exe", (236, 238, 244)),
        ("", "", (236, 238, 244)),
        (prompt, "git log --oneline -3", (236, 238, 244)),
        ("41d9e02 ", "Add clipboard counter badge", (146, 154, 168)),
        ("c7a10b8 ", "Localize the settings window", (146, 154, 168)),
        ("8f2c1a4 ", "Fix tray icon DPI scaling", (146, 154, 168)),
        ("", "", (236, 238, 244)),
        (prompt, "", (236, 238, 244)),
    ]
    ty = 58
    for pre, cmd, col in lines:
        p.d.text((P(26), P(ty)), pre, font=f, fill=(122, 212, 158, 255), anchor="la")
        if cmd:
            ox = 26 + p.d.textlength(pre, font=f) / P(1)
            p.d.text((P(ox), P(ty)), cmd, font=f, fill=col + (255,), anchor="la")
        ty += lh
    # 光标停在最后一行提示符后面
    cw = p.d.textlength(prompt, font=f) / P(1)
    p.d.rectangle([P(26 + cw), P(ty - lh + 3), P(26 + cw + 8), P(ty - lh + 17)],
                  fill=(224, 228, 234, 230))
    return win, pw, ph


# ---------------------------------------------------------------- 场景
SCENES = {
    "lock": dict(scale=1.5, dark=False, icons=True,
                 osd=dict(kind="lock", on=True, text=("KeyCapsLock", "StateOn"), place="top")),
    "copy": dict(scale=1.5, dark=False, notepad=True,
                 osd=dict(kind="check", on=True, text=("TipCopied", None), count=3, place="top")),
    "dark": dict(scale=1.5, dark=True, terminal=True,
                 osd=dict(kind="lock", on=True, text=("KeyNumLock", "StateOn"), place="bottom")),
    "settings": dict(scale=1.1, dark=False, settings=True),
}

ORDER = ["lock", "copy", "dark", "settings"]


def osd_text(spec, lang):
    key, state = spec["text"]
    if state:
        return T("TipToggleFormat", lang).format(T(key, lang), T(state, lang))
    return T(key, lang)


def render(name, lang):
    cfg = SCENES[name]
    use_scale(cfg["scale"])
    _FONTS.clear()
    _LANG[0] = lang

    dark = cfg["dark"]
    img = wallpaper()
    img = draw_taskbar(img, dark)
    if cfg.get("icons"):
        draw_desktop_icons(img, dark)

    # 窗口统一从屏幕上方 72dip 开始，给浮窗留出位置
    if cfg.get("notepad"):
        win, pw, ph = render_notepad(img, dark, lang)
        img = paste_window(img, win, P((VW - pw) / 2), P(96))
    if cfg.get("terminal"):
        win, pw, ph = render_terminal(img, dark)
        img = paste_window(img, win, P((VW - pw) / 2), P(80))
    if cfg.get("settings"):
        win, pw, ph = render_settings(img, dark, lang)
        img = paste_window(img, win, P((VW - pw) / 2), P((VH - TASKBAR_H - ph) / 2))

    if "osd" in cfg:
        spec = cfg["osd"]
        text = osd_text(spec, lang)
        top = 16 if spec["place"] == "top" else VH - TASKBAR_H - 16 - 50
        img = draw_osd(img, VW / 2, top, dark, text, spec["kind"], spec["on"], spec.get("count"))

    out = os.path.join(OUT_DIR, "%s%s.png" % (
        {"lock": "01-lock", "copy": "02-copy", "dark": "03-dark", "settings": "04-settings"}[name],
        "" if lang == "zh" else "-en"))
    img.resize((W, H), Image.LANCZOS).convert("RGB").save(out, "PNG")
    print("saved:", out, img.size)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    args = [a.lower() for a in sys.argv[1:]]
    langs = ["zh", "en"]
    if "zh" in args:
        langs = ["zh"]
    elif "en" in args:
        langs = ["en"]
    names = [a for a in args if a in SCENES] or ORDER
    for lang in langs:
        for n in names:
            render(n, lang)


if __name__ == "__main__":
    main()
