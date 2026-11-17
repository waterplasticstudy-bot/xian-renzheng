#!/usr/bin/env python3
"""Draw the cartoon 闲 icon and a capsule-style layout mock."""

import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"
DOCS = ROOT / "docs"
FONT = "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc"


def rounded_mask(size, radius, margin):
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle((margin, margin, size - 1 - margin, size - 1 - margin), radius=radius, fill=255)
    return mask


def _gradient(size):
    image = Image.new("RGBA", (size, size))
    pixels = image.load()
    for y in range(size):
        for x in range(size):
            t = (x * 0.62 + y * 0.38) / max(1, size - 1)
            if t < 0.55:
                u = t / 0.55
                color = (
                    int(255 + (124 - 255) * u),
                    int(45 + (58 - 45) * u),
                    int(149 + (237 - 149) * u),
                    255,
                )
            else:
                u = (t - 0.55) / 0.45
                color = (
                    int(124 + (34 - 124) * u),
                    int(58 + (211 - 58) * u),
                    int(237 + (238 - 237) * u),
                    255,
                )
            pixels[x, y] = color
    return image


def _blob(draw, size, grow, fill):
    m = size * 0.08
    draw.ellipse((m - grow, m * 1.35 - grow, size - m + grow, size - m * 0.55 + grow), fill=fill)
    draw.ellipse((size * 0.16 - grow, size * 0.02 - grow, size * 0.78 + grow, size * 0.46 + grow), fill=fill)
    draw.ellipse((size * 0.50 - grow, size * 0.42 - grow, size * 0.98 + grow, size * 0.90 + grow), fill=fill)
    draw.ellipse((size * -0.02 - grow, size * 0.38 - grow, size * 0.40 + grow, size * 0.86 + grow), fill=fill)


def draw_icon(size):
    outline = max(2, round(size * 0.055))
    plate = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    _blob(ImageDraw.Draw(plate), size, outline, (255, 255, 255, 255))
    mask = Image.new("L", (size, size), 0)
    _blob(ImageDraw.Draw(mask), size, 0, 255)
    color = _gradient(size)
    plate.paste(color, (0, 0), mask)
    draw = ImageDraw.Draw(plate)
    if size >= 16:
        face = max(8, int(size * 0.62))
        glyph = font(face)
        text_box = draw.textbbox((0, 0), "闲", font=glyph)
        tw = text_box[2] - text_box[0]
        th = text_box[3] - text_box[1]
        x = (size - tw) / 2 - text_box[0]
        y = (size - th) / 2 - text_box[1] - size * 0.03
        stroke = max(1, int(size * 0.045))
        for dx in range(-stroke, stroke + 1):
            for dy in range(-stroke, stroke + 1):
                if dx * dx + dy * dy <= stroke * stroke:
                    draw.text((x + dx, y + dy), "闲", font=glyph, fill=(255, 255, 255, 255))
        draw.text((x, y), "闲", font=glyph, fill=(32, 8, 54, 255))
    if size >= 32:
        s = float(size)
        bolt = [
            (0.74 * s, 0.12 * s),
            (0.60 * s, 0.40 * s),
            (0.70 * s, 0.40 * s),
            (0.58 * s, 0.68 * s),
            (0.82 * s, 0.34 * s),
            (0.70 * s, 0.34 * s),
        ]
        draw.polygon(bolt, fill=(255, 244, 80, 255))
        draw.line(bolt[:2], fill=(255, 255, 255, 230), width=max(1, size // 64))
    return plate


def dib_argb(image):
    image = image.convert("RGBA")
    width, height = image.size
    pixels = image.load()
    xor_rows = []
    and_stride = ((width + 31) // 32) * 4
    and_rows = []
    for y in range(height - 1, -1, -1):
        row = bytearray()
        bits = bytearray(and_stride)
        for x in range(width):
            r, g, b, a = pixels[x, y]
            row += bytes((b, g, r, a))
            if a < 16:
                bits[x // 8] |= 0x80 >> (x % 8)
        xor_rows.append(row)
        and_rows.append(bits)
    xor_bytes = b"".join(xor_rows)
    header = struct.pack(
        "<IIIHHIIIIII",
        40,
        width,
        height * 2,
        1,
        32,
        0,
        len(xor_bytes),
        0,
        0,
        0,
        0,
    )
    return header + xor_bytes + b"".join(and_rows)


def save_ico(path, images):
    blobs = []
    for image in images:
        if image.width >= 128:
            import io
            buffer = io.BytesIO()
            image.save(buffer, format="PNG")
            blobs.append(buffer.getvalue())
        else:
            blobs.append(dib_argb(image))
    with path.open("wb") as handle:
        handle.write(struct.pack("<HHH", 0, 1, len(blobs)))
        offset = 6 + 16 * len(blobs)
        for image, blob in zip(images, blobs):
            w = 0 if image.width >= 256 else image.width
            h = 0 if image.height >= 256 else image.height
            handle.write(struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(blob), offset))
            offset += len(blob)
        for blob in blobs:
            handle.write(blob)


def font(size):
    return ImageFont.truetype(FONT, size)


def text(draw, xy, value, size, fill):
    draw.text(xy, value, font=font(size), fill=fill)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def pill(base, box, label):
    x0, y0, x1, y1 = [int(v) for v in box]
    width = max(1, x1 - x0)
    height = max(1, y1 - y0)
    glow = Image.new("RGBA", (width + 16, height + 16), (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle((4, 6, width + 12, height + 10), radius=height // 2, fill=(255, 45, 149, 90))
    base.paste(glow, (x0 - 8, y0 - 4), glow)
    chip = Image.new("RGBA", (width, height))
    pixels = chip.load()
    for x in range(width):
        t = x / max(1, width - 1)
        if t < 0.5:
            color = lerp((255, 45, 149), (124, 58, 237), t / 0.5)
        else:
            color = lerp((124, 58, 237), (34, 211, 238), (t - 0.5) / 0.5)
        for y in range(height):
            pixels[x, y] = color + (255,)
    mask = Image.new("L", (width, height), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, width - 1, height - 1), radius=height // 2, fill=255)
    base.paste(chip, (x0, y0), mask)
    overlay = ImageDraw.Draw(base)
    tw = overlay.textlength(label, font=font(16))
    text(overlay, (x0 + (width - tw) / 2, y0 + 10), label, 16, (255, 255, 255))


def mock(icon):
    w, h = 540, 900
    image = Image.new("RGB", (w, h), (18, 8, 32))
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 0, w, 28), fill=(12, 6, 22))
    text(draw, (12, 4), "闲·认证", 15, (255, 248, 252))
    for i, color in enumerate(((255, 92, 138), (255, 196, 70), (52, 232, 158))):
        draw.ellipse((w - 78 + i * 22, 6, w - 62 + i * 22, 22), fill=color)
    for x in range(w):
        t = x / (w - 1)
        if t < 0.55:
            color = lerp((255, 45, 149), (124, 58, 237), t / 0.55)
        else:
            color = lerp((124, 58, 237), (34, 211, 238), (t - 0.55) / 0.45)
        draw.line((x, 28, x, 132), fill=color)
    badge = icon.resize((64, 64), Image.Resampling.LANCZOS)
    image.paste(badge, (22, 46), badge)
    text(draw, (100, 52), "闲·认证", 28, (255, 255, 255))
    text(draw, (102, 90), "校园网自动认证", 16, (255, 236, 250))

    ink = (255, 248, 252)
    muted = (214, 190, 230)
    text(draw, (88, 160), "开启自动认证", 20, ink)
    draw.rounded_rectangle((28, 156, 78, 188), radius=16, fill=(255, 45, 149))
    draw.ellipse((50, 160, 74, 184), fill=(255, 255, 255))
    text(draw, (28, 210), "账号：", 16, ink)
    draw.rounded_rectangle((28, 236, 500, 268), radius=8, fill=(36, 14, 58), outline=(92, 48, 128))
    text(draw, (36, 242), "学号或账号", 15, muted)
    text(draw, (28, 282), "密码：", 16, ink)
    draw.rounded_rectangle((28, 308, 500, 340), radius=8, fill=(36, 14, 58), outline=(92, 48, 128))
    text(draw, (36, 314), "••••••••", 15, ink)
    text(draw, (28, 354), "目标网络：MyCampus-WiFi", 14, muted)
    text(draw, (28, 376), "当前门户：未设置（识别后只保存在本机）", 14, muted)
    pill(image, (28, 410, 500, 454), "模式选择")
    draw.polygon([(470, 426), (482, 426), (476, 434)], fill=(255, 255, 255))
    for i, label in enumerate(("关机", "重启", "休眠", "睡眠")):
        x = 36 + i * 118
        draw.rounded_rectangle((x, 472, x + 16, 488), radius=3, outline=(34, 211, 238), width=2)
        text(draw, (x + 24, 470), label, 16, ink)
    draw.rounded_rectangle((36, 514, 52, 530), radius=3, outline=(34, 211, 238), width=2)
    text(draw, (60, 512), "VPN选择", 16, ink)
    text(draw, (28, 540), "已识别：Bettbox（TUN 开）", 14, muted)
    draw.rounded_rectangle((36, 572, 52, 588), radius=3, outline=(34, 211, 238), width=2)
    text(draw, (60, 570), "间隔检测", 16, ink)
    draw.rounded_rectangle((168, 566, 230, 594), radius=8, fill=(36, 14, 58), outline=(92, 48, 128))
    text(draw, (186, 570), "10", 16, ink)
    text(draw, (240, 570), "分钟", 16, ink)
    pill(image, (28, 616, 250, 660), "恢复默认配置")
    pill(image, (278, 616, 500, 660), "注销")
    pill(image, (28, 678, 500, 722), "识别待连接网络")
    pill(image, (28, 736, 500, 780), "立即验证")
    pill(image, (28, 794, 500, 838), "保存账号")
    draw.rounded_rectangle((24, 852, 516, 888), radius=18, fill=(42, 18, 68), outline=(52, 232, 158), width=2)
    text(draw, (40, 860), "已在线", 16, (52, 232, 158))
    return image


def main():
    ASSETS.mkdir(exist_ok=True)
    DOCS.mkdir(exist_ok=True)
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [draw_icon(size) for size in sizes]
    draw_icon(96).save(ASSETS / "xian-auth.png")
    images[-1].save(ASSETS / "xian-auth-256.png")
    save_ico(ASSETS / "xian-auth.ico", images)
    preview = mock(images[-1])
    preview.save(DOCS / "ui-preview.png")
    preview.save(DOCS / "ui-mock.png")


if __name__ == "__main__":
    main()
