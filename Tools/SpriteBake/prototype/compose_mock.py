"""Compose a 320x180 16-bit pitch-view mock (RBI framing) from rendered frames.

Usage: python3 compose_mock.py <mock_dir>   (expects mock_dir/batter, mock_dir/pitcher)
Writes mock_dir/pitch_view.gif (3x), pitch_view_contact.png (3x) and sprite sheets.
"""
import json
import random
import sys

import numpy as np
from PIL import Image, ImageDraw

D = sys.argv[1]
W, H = 320, 180
SCALE = 3
rng = random.Random(7)


# ---------------------------------------------------------------- sprites
def load_seq(sub, n, src_px, dst_px):
    out = []
    for i in range(n):
        im = Image.open(f"{D}/{sub}/f{i:03d}.png").convert("RGBA")
        assert im.size[0] == src_px
        s = np.array(im.resize((dst_px, dst_px), Image.BOX))
        s[..., 3] = np.where(s[..., 3] >= 110, 255, 0)
        out.append(s)
    return out


batter = load_seq("batter", 30, 384, 96)
pitcher = load_seq("pitcher", 79, 264, 36)
ptrack = json.load(open(f"{D}/pitcher/track.json"))

allpx = np.concatenate([a[a[..., 3] == 255][:, :3] for a in batter + pitcher])
pal = Image.fromarray(allpx.reshape(1, -1, 3).astype(np.uint8), "RGB").quantize(colors=24, method=Image.Quantize.MEDIANCUT)


def finish(a, mirror=False):
    rgb = Image.fromarray(a[..., :3].astype(np.uint8), "RGB").quantize(palette=pal, dither=Image.Dither.NONE).convert("RGB")
    q = np.zeros_like(a)
    q[..., :3] = np.array(rgb)
    q[..., 3] = a[..., 3]
    op = q[..., 3] == 255
    res = q.copy()
    h, w = op.shape
    for y in range(h):
        for x in range(w):
            if op[y, x]:
                continue
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
                yy, xx = y + dy, x + dx
                if 0 <= yy < h and 0 <= xx < w and op[yy, xx]:
                    c = q[yy, xx, :3].astype(int)
                    res[y, x, :3] = (c * 0.28 + np.array([18, 10, 26]) * 0.72).astype(np.uint8)
                    res[y, x, 3] = 255
                    break
    im = Image.fromarray(res, "RGBA")
    return im.transpose(Image.FLIP_LEFT_RIGHT) if mirror else im


batter_s = [finish(a) for a in batter]
btrack = json.load(open(f"{D}/batter/track.json"))
B_CONTACT = 16        # batter frame index where the barrel is furthest over the plate
B_LAST = 21           # stop before he breaks for first (he runs off his canvas)
# Lay the plate under the barrel at contact; boxes around his feet.
_bx, _by = btrack[B_CONTACT]["barrel_x"] * 96, btrack[B_CONTACT]["barrel_y"] * 96
_fx, _fy = btrack[0]["feet_x"] * 96, btrack[0]["feet_y"] * 96
PLATE_X = 160
BAT_FEET_Y = 171
BAT_LEFT = round(PLATE_X - _bx)
BAT_TOP = round(BAT_FEET_Y - _fy)
FEET_X = BAT_LEFT + _fx
CONTACT_PT = (PLATE_X, round(BAT_TOP + _by))
def team_swap(im):
    # Demo of the team-colour swap: red-dominant pixels (jersey + its outline)
    # become royal blue. The real game does this with a palette shader.
    a = np.array(im).astype(int)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    m = (r > g + 40) & (r > b + 40) & (a[..., 3] > 0)
    a[m, 0], a[m, 1], a[m, 2] = (b[m] * 0.5).astype(int), (g[m] * 1.1 + 25).clip(0, 255).astype(int), r[m]
    return Image.fromarray(a.astype(np.uint8), "RGBA")


pitcher_s = [team_swap(finish(a)) for a in pitcher]


def feet_row(im):
    a = np.array(im)[..., 3]
    rows = np.where(a.max(axis=1) > 0)[0]
    return rows.max()


def save_sheet(frames, path, cols):
    w, h = frames[0].size
    rows = (len(frames) + cols - 1) // cols
    sh = Image.new("RGBA", (w * cols, h * rows), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        sh.alpha_composite(f, ((i % cols) * w, (i // cols) * h))
    sh.save(path)


save_sheet(batter_s, f"{D}/batter_sheet.png", 10)
save_sheet(pitcher_s, f"{D}/pitcher_sheet.png", 20)

# ---------------------------------------------------------------- field
bg = Image.new("RGB", (W, H))
dr = ImageDraw.Draw(bg)
# Sky and stands.
for y in range(0, 8):
    dr.line([(0, y), (W, y)], fill=(118, 170, 226) if y < 4 else (140, 186, 232))
dr.rectangle([0, 8, W, 29], fill=(58, 54, 78))
crowd = [(196, 70, 60), (230, 210, 170), (70, 96, 160), (210, 180, 70), (240, 240, 235), (120, 84, 60), (90, 140, 90)]
for y in range(9, 29):
    for x in range(W):
        if (x + y) % 2 == 0 and rng.random() < 0.55:
            bg.putpixel((x, y), rng.choice(crowd))
for y in (15, 22):
    dr.line([(0, y), (W, y)], fill=(44, 40, 60))
# Outfield wall with yellow top, warning track.
dr.rectangle([0, 30, W, 35], fill=(28, 78, 52))
dr.line([(0, 30), (W, 30)], fill=(232, 204, 70))
dr.rectangle([0, 36, W, 39], fill=(150, 108, 72))
# Grass with mow stripes that widen toward the camera.
y, k = 40, 0
while y < H:
    hgt = 3 + int(k * 1.25)
    dr.rectangle([0, y, W, y + hgt], fill=(66, 142, 72) if k % 2 == 0 else (56, 128, 62))
    y += hgt + 1
    k += 1
# Infield dirt arc and infield grass.
dirt, dirt_l = (176, 122, 80), (190, 138, 94)
dr.ellipse([52, 74, 268, 138], fill=dirt)
dr.polygon([(160, 152), (236, 112), (160, 84), (84, 112)], fill=(60, 136, 68))
# Home plate circle, mound, bases.
dr.ellipse([112, 152, 208, 182], fill=dirt)
dr.ellipse([146, 94, 174, 102], fill=dirt_l)
dr.rectangle([157, 97, 162, 97], fill=(244, 244, 236))
for bx, by in ((238, 111), (160, 82), (82, 111)):
    dr.rectangle([bx - 2, by - 1, bx + 2, by + 1], fill=(250, 250, 244))
chalk = (236, 236, 226)
dr.line([(150, 166), (0, 92)], fill=chalk)
dr.line([(170, 166), (320, 92)], fill=chalk)
_l0 = round(FEET_X - 13)
_l1 = min(round(FEET_X + 13), PLATE_X - 11)
dr.rectangle([_l0, 160, _l1, 178], outline=chalk)
dr.rectangle([2 * PLATE_X - _l1, 160, 2 * PLATE_X - _l0, 178], outline=chalk)
dr.polygon([(154, 166), (166, 166), (166, 169), (160, 172), (154, 169)], fill=(250, 250, 246))

# ---------------------------------------------------------------- timeline (15 fps)
RELEASE = 45          # pitcher frame index at release
FLIGHT = 7            # frames from release to the plate (~0.45 s at 15 fps)
ARRIVE = RELEASE + FLIGHT
HITSTOP = 2
P_SIZE, P_FEET = 36, feet_row(pitcher_s[0])
B_SIZE, B_FEET = 96, feet_row(batter_s[0])
PIT_X, PIT_FEET_Y = 160, 99


def pitcher_pos():
    return PIT_X - P_SIZE // 2, PIT_FEET_Y - P_FEET


def batter_pos():
    return BAT_LEFT, BAT_TOP


def hand_screen(i):
    t = ptrack[i]
    px, py = pitcher_pos()
    return px + t["hand_x"] * P_SIZE, py + t["hand_y"] * P_SIZE


def draw_ball(fr, x, y, r, shadow):
    d = ImageDraw.Draw(fr)
    if shadow:
        sx, sy = shadow
        d.ellipse([sx - r - 1, sy - 1, sx + r + 1, sy], fill=(40, 70, 40))
    d.ellipse([x - r, y - r, x + r, y + r], fill=(248, 248, 242), outline=(150, 40, 40) if r >= 2 else None)


frames = []
total = 92
g_hold = 0
for g in range(total):
    gg = g
    fr = bg.copy().convert("RGBA")
    pi = min(gg, 78)
    fr.alpha_composite(pitcher_s[pi], pitcher_pos())
    bi = int(np.clip(gg - ARRIVE + B_CONTACT, 0, B_LAST))
    # Ball.
    if RELEASE <= gg < ARRIVE:
        # Perspective: the ball is 18.4 m out at release, the camera ~5 m
        # behind the plate, so screen motion is slow at first and fast late.
        t = (gg - RELEASE) / FLIGHT
        s0, s1 = 1 / (5 + 18.4), 1 / 5
        st = 1 / (5 + 18.4 * (1 - t))
        u = (st - s0) / (s1 - s0)
        hx, hy = hand_screen(RELEASE)
        brk = 7 * u * u          # slider: breaks late, away from a RHB
        x = hx + (CONTACT_PT[0] - 7 - hx) * u + brk
        y = hy + (CONTACT_PT[1] - hy) * u
        r = 0 if u < 0.15 else (1 if u < 0.6 else 2)
        shadow_y = 100 + (168 - 100) * u
        draw_ball(fr, x, y, r, (x, shadow_y))
    fr.alpha_composite(batter_s[bi], batter_pos())
    if ARRIVE <= gg < ARRIVE + 2:
        d = ImageDraw.Draw(fr)
        cx, cy = CONTACT_PT
        for dx, dy in ((0, -6), (0, 6), (-6, 0), (6, 0), (-4, -4), (4, 4), (-4, 4), (4, -4)):
            d.line([(cx, cy), (cx + dx, cy + dy)], fill=(255, 250, 200))
        d.ellipse([cx - 2, cy - 2, cx + 2, cy + 2], fill=(255, 255, 255))
    if ARRIVE + 2 <= gg < ARRIVE + 16:
        t = (gg - ARRIVE - 2) / 14
        x = CONTACT_PT[0] + 8 * t
        y = CONTACT_PT[1] - (CONTACT_PT[1] - 30) * (1 - (1 - t) ** 2)
        r = 2 if t < 0.3 else 1
        draw_ball(fr, x, y, r, (x, 166 - (166 - 70) * t))
    frames.append(fr.convert("RGB"))
    if gg == ARRIVE:
        for _ in range(HITSTOP):
            frames.append(fr.convert("RGB"))
        contact_frame = fr

big = [f.resize((W * SCALE, H * SCALE), Image.NEAREST) for f in frames]
gpal = Image.new("RGB", (W, H * 4))
for i, f in enumerate(frames[::max(1, len(frames) // 4)][:4]):
    gpal.paste(f, (0, i * H))
gp = gpal.quantize(colors=255, method=Image.Quantize.MEDIANCUT)
pframes = [b.quantize(palette=gp, dither=Image.Dither.NONE) for b in big]
pframes[0].save(f"{D}/pitch_view.gif", save_all=True, append_images=pframes[1:], duration=66, loop=0, disposal=1)
contact_frame.convert("RGB").resize((W * SCALE, H * SCALE), Image.NEAREST).save(f"{D}/pitch_view_contact.png")
frames[30].resize((W * SCALE, H * SCALE), Image.NEAREST).save(f"{D}/pitch_view_windup.png")
print("frames", len(frames), "palette", len(pal.getpalette()) // 3)
