"""按 Resources/app.ico 现有视觉矢量重绘 WinTips 应用图标，输出高清 PNG。

几何参数全部从原 256x256 位图实测反解：
- 外框：超椭圆（squircle）指数 n=4.5，铺满画布（对 y=4/8/16/32/64 轮廓的
  预测宽度 163/188/214/238/252 与实测 164/190/215/238/252 一致）
- 锁梁：圆心 (127.5, 78)，外半径 45 / 内半径 30，柱体延至锁体
- 锁体：x 72..183，y 122..206，超椭圆角 r=26
- 钥匙孔：实色 #0A69F5，圆心 (127.5, 154)，r=8.6
绘制用 4 倍超采样后 Lanczos 降采样，边缘锐利。

用法：python promo/build_wintips_icon.py（需 Pillow）
输出：仓库根下 Resources/app-icon-1024.png、app-icon-512.png
"""
import math
from pathlib import Path

from PIL import Image, ImageDraw

OUT = 1024
S = OUT * 4          # 4096 绘制画布
UNIT = S / 256.0     # 256 基准 -> 画布


def u(v: float) -> float:
    return v * UNIT


def superellipse(cx, cy, a, b, n=4.5, steps=4096):
    """超椭圆参数方程取点"""
    pts = []
    k = 2.0 / n
    for i in range(steps):
        t = 2.0 * math.pi * i / steps
        ct, st = math.cos(t), math.sin(t)
        pts.append((
            cx + a * math.copysign(abs(ct) ** k, ct),
            cy + b * math.copysign(abs(st) ** k, st),
        ))
    return pts


def squircle_rect(x0, y0, x1, y1, r_top, r_bot, n=4.5, steps=96):
    """四角为超椭圆弧的圆角矩形多边形（屏幕坐标，顺时针不自交）。

    原图锁体上下圆角并不一致：顶部 r≈26、底部 r≈48，故分开传入。
    """
    k = 2.0 / n
    corners = [
        (x1 - r_top, y0 + r_top, -90.0, r_top),   # 右上：顶边 -> 右边
        (x1 - r_bot, y1 - r_bot, 0.0, r_bot),     # 右下：右边 -> 底边
        (x0 + r_bot, y1 - r_bot, 90.0, r_bot),    # 左下：底边 -> 左边
        (x0 + r_top, y0 + r_top, 180.0, r_top),   # 左上：左边 -> 顶边
    ]
    pts = []
    for ox, oy, a0, r in corners:
        for i in range(steps + 1):
            t = math.radians(a0 + 90.0 * i / steps)
            ct, st = math.cos(t), math.sin(t)
            pts.append((
                ox + r * math.copysign(abs(ct) ** k, ct),
                oy + r * math.copysign(abs(st) ** k, st),
            ))
    return pts


# ---------- 1. 背景垂直渐变（实测端点，纯垂直无横向分量） ----------
TOP = (117, 181, 255)
BOT = (8, 104, 245)

strip = Image.new("RGB", (1, 256))
for y in range(256):
    t = y / 255.0
    strip.putpixel((0, y), tuple(int(round(TOP[i] + (BOT[i] - TOP[i]) * t)) for i in range(3)))
grad = strip.resize((S, S), Image.BILINEAR).convert("RGBA")

# ---------- 2. 外框超椭圆蒙版 ----------
frame = Image.new("L", (S, S), 0)
ImageDraw.Draw(frame).polygon(superellipse(u(127.5), u(127.5), u(127.5), u(127.5), 4.5), fill=255)

# ---------- 3. 白色挂锁 ----------
lock = Image.new("L", (S, S), 0)
ld = ImageDraw.Draw(lock)

CX = u(127.5)
SH_CY = u(78.0)
R_OUT = u(45.0)
R_IN = u(30.0)
SH_BOT = u(126.0)

# 锁梁：外圆 + 两柱，再挖内圆
ld.ellipse([CX - R_OUT, SH_CY - R_OUT, CX + R_OUT, SH_CY + R_OUT], fill=255)
ld.rectangle([CX - R_OUT, SH_CY, CX + R_OUT, SH_BOT], fill=255)
ld.ellipse([CX - R_IN, SH_CY - R_IN, CX + R_IN, SH_CY + R_IN], fill=0)
ld.rectangle([CX - R_IN, SH_CY, CX + R_IN, SH_BOT], fill=0)

# 锁体
ld.polygon(squircle_rect(u(72), u(122), u(183), u(205), u(30), u(45)), fill=255)

# ---------- 4. 合成 ----------
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
img = Image.composite(grad, img, frame)
img = Image.composite(Image.new("RGBA", (S, S), (255, 255, 255, 255)), img, lock)

# 钥匙孔：原图为实色深蓝，并非透出背景
KH_CY = u(154.0)
KH_R = u(8.6)
keyhole = Image.new("L", (S, S), 0)
ImageDraw.Draw(keyhole).ellipse([CX - KH_R, KH_CY - KH_R, CX + KH_R, KH_CY + KH_R], fill=255)
img = Image.composite(Image.new("RGBA", (S, S), (10, 105, 245, 255)), img, keyhole)

# ---------- 5. 输出 ----------
# 按脚本自身定位仓库根（promo/ 的上一级），换目录或换机器都不用改路径
ROOT = Path(__file__).resolve().parent.parent
for size, name in ((1024, "app-icon-1024.png"), (512, "app-icon-512.png")):
    out = ROOT / "Resources" / name
    img.resize((size, size), Image.LANCZOS).save(out)
    print(f"wrote {out}")
