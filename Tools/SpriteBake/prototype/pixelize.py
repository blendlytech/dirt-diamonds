"""Pixelize supersampled renders into 16-bit-style sprites and build a preview sheet.

Usage: python3 pixelize.py <render_dir> <out_png> <mode> <sprite_px> <colors>
"""
import sys

import numpy as np
from PIL import Image

src, out_png, mode, sprite_px, colors = sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5])
names = ["S", "SE", "E", "NE", "N", "NW", "W", "SW"]
frames = [Image.open(f"{src}/{mode}_{i}_{n}.png").convert("RGBA") for i, n in enumerate(names)]

# 1. Area-downsample (keeps thin parts readable), then hard alpha.
small = []
for f in frames:
    s = f.resize((sprite_px, sprite_px), Image.BOX)
    a = np.array(s)
    a[..., 3] = np.where(a[..., 3] >= 110, 255, 0)
    small.append(a)

# 2. One shared palette across all directions (consistent colours).
opaque = np.concatenate([a[a[..., 3] == 255][:, :3] for a in small])
pal_img = Image.fromarray(opaque.reshape(1, -1, 3).astype(np.uint8), "RGB").quantize(colors=colors, method=Image.Quantize.MEDIANCUT)


def quantize(a):
    rgb = Image.fromarray(a[..., :3].astype(np.uint8), "RGB").quantize(palette=pal_img, dither=Image.Dither.NONE).convert("RGB")
    q = np.array(rgb)
    out = np.zeros_like(a)
    out[..., :3] = q
    out[..., 3] = a[..., 3]
    return out


# 3. Selective outline: transparent pixels touching the sprite get a dark
#    version of the colour they touch (SNES-style "sel-out").
def outline(a):
    h, w = a.shape[:2]
    res = a.copy()
    op = a[..., 3] == 255
    for y in range(h):
        for x in range(w):
            if op[y, x]:
                continue
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                yy, xx = y + dy, x + dx
                if 0 <= yy < h and 0 <= xx < w and op[yy, xx]:
                    c = a[yy, xx, :3].astype(int)
                    res[y, x, :3] = (c * 0.28 + np.array([18, 10, 26]) * 0.72).astype(np.uint8)
                    res[y, x, 3] = 255
                    break
    return res


sprites = [outline(quantize(a)) for a in small]

# 4. Preview sheet: 8 directions on a grass background, scaled up with nearest.
scale = max(1, 384 // sprite_px)
pad = 4
sheet = Image.new("RGBA", ((sprite_px + pad) * 8 + pad, sprite_px + 2 * pad), (46, 112, 58, 255))
for i, s in enumerate(sprites):
    im = Image.fromarray(s, "RGBA")
    sheet.alpha_composite(im, (pad + i * (sprite_px + pad), pad))
sheet.save(out_png.replace(".png", "_1x.png"))
sheet.resize((sheet.width * scale, sheet.height * scale), Image.NEAREST).save(out_png)
print("palette colours:", len(pal_img.getpalette()) // 3, "sheet", sheet.size, "scale", scale)
