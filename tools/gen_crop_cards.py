#!/usr/bin/env python3
"""Generate the crop billboard atlas (alpha cards) procedurally.

Atlas layout: 8 columns (cards 0..6 referenced by stages, column 7 = dead) x 4 rows (atlasRow in crop data:
wheat, barley, canola, corn). Each cell is 128 x 256 px and maps onto a quad CARD_WIDTH meters wide
and <stage height> meters tall, so plants are drawn in meter space with per-stage vertical scale.

Usage: tools/gen_crop_cards.py [--out game/assets/textures/crops/crop_atlas.png]
"""
import argparse
import json
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

CELL_W, CELL_H = 128, 256
SS = 4  # supersampling
CARD_WIDTH = 1.1  # meters, must match CropRenderer.CardWidth
COLS, ROWS = 8, 4


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def hexc(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


class Card:
    """Draws in meters: x in [0, CARD_WIDTH], y in [0, height] (y up from the ground)."""

    def __init__(self, height, rng):
        self.h = max(height, 0.02)
        self.img = Image.new("RGBA", (CELL_W * SS, CELL_H * SS), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.rng = rng
        self.sx = CELL_W * SS / CARD_WIDTH
        self.sy = CELL_H * SS / self.h

    def px(self, x, y):
        return (x * self.sx, CELL_H * SS - y * self.sy)

    def shade(self, color, y):
        # Darker toward the ground (self-shadowing inside the canopy).
        t = 0.55 + 0.45 * min(1.0, max(0.0, y / self.h))
        j = self.rng.uniform(0.9, 1.08)
        return tuple(min(255, int(c * t * j)) for c in color) + (255,)

    def blade(self, x0, length, lean, width_m, color, y0=0.0, droop=0.0, steps=10):
        """A tapered leaf/stalk from (x0, y0) leaning sideways; droop bends the tip down."""
        pts_l, pts_r = [], []
        for i in range(steps + 1):
            t = i / steps
            x = x0 + lean * t * t
            y = y0 + length * (t - droop * t * t)
            w = width_m * (1.0 - t * 0.85) * 0.5
            # Normal of the curve approximated sideways in x (pixels are anisotropic anyway).
            pts_l.append(self.px(x - w, y))
            pts_r.append(self.px(x + w, y))
        poly = pts_l + pts_r[::-1]
        self.d.polygon(poly, fill=self.shade(color, y0 + length * 0.6))

    def ellipse(self, x, y, rx, ry, color):
        cx, cy = self.px(x, y)
        self.d.ellipse([cx - rx * self.sx, cy - ry * self.sy, cx + rx * self.sx, cy + ry * self.sy],
                       fill=self.shade(color, y))

    def line(self, x0, y0, x1, y1, width_px, color):
        self.d.line([self.px(x0, y0), self.px(x1, y1)], fill=self.shade(color, y1), width=max(1, int(width_px * SS)))

    def finish(self):
        img = self.img.filter(ImageFilter.GaussianBlur(0.6)).resize((CELL_W, CELL_H), Image.Resampling.LANCZOS)
        # Binary-ish alpha works best with alpha scissor; keep a soft but firm edge.
        a = img.getchannel("A").point(lambda v: 255 if v > 100 else 0)
        img.putalpha(a)
        return img


GREEN = hexc("#5d7a3c")
GREEN_DARK = hexc("#43602f")
BLUE_GREEN = hexc("#56704f")
DEAD = hexc("#6e5c42")


def cereal(card, stage, n_stages, ripe_color, heads, barley=False):
    """Wheat/barley: tillers of thin leaves, then stems with ears that ripen."""
    rng = card.rng
    h = card.h
    ripeness = {5: 0.55, 6: 1.0}.get(stage, 0.0)
    leaf = mix(GREEN, ripe_color, ripeness)
    count = {1: 22, 2: 34}.get(stage, 40)
    for _ in range(count):
        x = rng.uniform(0.04, CARD_WIDTH - 0.04)
        if stage <= 2:
            card.blade(x, h * rng.uniform(0.6, 1.0), rng.uniform(-0.12, 0.12), 0.012, leaf, droop=0.15)
        else:
            # Leaves low down, stems to the top.
            card.blade(x, h * rng.uniform(0.3, 0.5), rng.uniform(-0.15, 0.15), 0.014,
                       mix(GREEN_DARK, ripe_color, ripeness * 0.8), droop=0.35)
            top = h * rng.uniform(0.82, 1.0) - (0.08 if heads else 0.0)
            lean = rng.uniform(-0.05, 0.05)
            card.blade(x, top, lean, 0.007, leaf, steps=4)
            if heads:
                ear = mix(hexc("#7d9446"), ripe_color, ripeness)
                ex, ey = x + lean, top
                if barley and ripeness > 0.3:
                    # Nodding ear with awns.
                    card.blade(ex, 0.09, rng.choice([-0.06, 0.06]), 0.02, ear, y0=ey - 0.02, droop=0.9, steps=5)
                else:
                    card.ellipse(ex, ey + 0.045, 0.011, 0.045, ear)
                    if barley:
                        card.line(ex, ey + 0.06, ex + rng.uniform(-0.03, 0.03), ey + 0.14, 0.6, ear)


def canola(card, stage):
    rng = card.rng
    h = card.h
    ripe = stage == 6
    if stage in (1, 2):
        # Rosette: broad leaves fanning out.
        for _ in range(9 if stage == 1 else 14):
            x = rng.uniform(0.1, CARD_WIDTH - 0.1)
            card.blade(x, h * rng.uniform(0.6, 1.0), rng.uniform(-0.25, 0.25), 0.09, BLUE_GREEN, droop=0.5)
        return
    stem_col = hexc("#6d5a3a") if ripe else mix(BLUE_GREEN, hexc("#7d8a45"), 0.3 if stage == 5 else 0.0)
    for _ in range(10):
        card.blade(rng.uniform(0.1, 1.0), h * 0.35, rng.uniform(-0.2, 0.2), 0.08,
                   hexc("#5a4a33") if ripe else BLUE_GREEN, droop=0.5)
    for _ in range(14):
        x = rng.uniform(0.08, CARD_WIDTH - 0.08)
        top = h * rng.uniform(0.75, 1.0)
        lean = rng.uniform(-0.08, 0.08)
        card.blade(x, top, lean, 0.012, stem_col, steps=5)
        # Branches with flowers (yellow) or pods.
        for b in range(3):
            by = top * rng.uniform(0.7, 0.95)
            bx = x + lean * (by / top) ** 2
            dx = rng.uniform(-0.12, 0.12)
            card.line(bx, by, bx + dx, by + 0.12, 0.7, stem_col)
            if stage == 4:
                for _ in range(4):
                    card.ellipse(bx + dx + rng.uniform(-0.03, 0.03), by + 0.12 + rng.uniform(-0.03, 0.04), 0.022, 0.022,
                                 hexc("#d4bd3a"))
            elif stage >= 5:
                pod = hexc("#4f3f2a") if ripe else hexc("#8a9448")
                for _ in range(3):
                    px = bx + dx + rng.uniform(-0.03, 0.03)
                    card.line(px, by + 0.08, px + rng.uniform(-0.03, 0.03), by + 0.16, 0.8, pod)


def corn(card, stage):
    rng = card.rng
    h = card.h
    ripeness = {5: 0.35, 6: 1.0}.get(stage, 0.0)
    leaf = mix(hexc("#4d7133"), hexc("#b3a06e"), ripeness)
    stems = 3 if stage >= 3 else 5
    for s in range(stems):
        x = (s + 0.5) / stems * CARD_WIDTH + rng.uniform(-0.05, 0.05)
        if stage <= 2:
            for _ in range(6):
                card.blade(x, h * rng.uniform(0.6, 1.0), rng.uniform(-0.25, 0.25), 0.05, leaf, droop=0.45)
            continue
        top = h * rng.uniform(0.9, 1.0)
        card.blade(x, top, rng.uniform(-0.03, 0.03), 0.035, mix(hexc("#5a7a3a"), hexc("#a8946a"), ripeness), steps=6)
        nodes = 7
        for k in range(nodes):
            y = top * (0.12 + 0.75 * k / nodes)
            side = -1 if k % 2 else 1
            card.blade(x, h * rng.uniform(0.28, 0.4), side * rng.uniform(0.18, 0.3), 0.075, leaf, y0=y, droop=0.8)
        if stage >= 4:
            tassel = mix(hexc("#b49a5c"), hexc("#8c7650"), ripeness)
            for _ in range(5):
                card.line(x, top, x + rng.uniform(-0.1, 0.1), top - rng.uniform(0.05, 0.18) + 0.2, 0.9, tassel)
            ear = mix(hexc("#7f9a4c"), hexc("#c2a86a"), ripeness)
            card.ellipse(x + 0.07, top * 0.5, 0.035, 0.11, ear)


def dead(card):
    rng = card.rng
    for _ in range(30):
        x = rng.uniform(0.02, CARD_WIDTH - 0.02)
        card.blade(x, card.h * rng.uniform(0.3, 1.0), rng.uniform(-0.35, 0.35), 0.02, DEAD, droop=0.7)


def main():
    root = Path(__file__).resolve().parent.parent
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data", default=str(root / "game/data/crops/field_crops.json"))
    ap.add_argument("--out", default=str(root / "game/assets/textures/crops/crop_atlas.png"))
    args = ap.parse_args()

    text = "\n".join(l for l in Path(args.data).read_text().splitlines() if not l.strip().startswith("//"))
    crops = json.loads(text)
    atlas = Image.new("RGBA", (CELL_W * COLS, CELL_H * ROWS), (0, 0, 0, 0))
    for crop in crops:
        row = crop["atlasRow"]
        stages = crop["stages"]
        max_h = max(s["height"] for s in stages)
        # Stages may share a card ("card" field); draw each card once, from its first stage.
        cards = {}
        for i, st in enumerate(stages):
            cards.setdefault(st.get("card", i), st)
        for col in range(COLS):
            is_dead = col == COLS - 1
            if not is_dead and (col not in cards or cards[col]["height"] <= 0):
                continue
            height = max_h * 0.4 if is_dead else cards[col]["height"]
            rng = random.Random(row * 131 + col * 7 + 1)
            card = Card(height, rng)
            if is_dead:
                dead(card)
            elif crop["id"] in ("wheat", "barley"):
                ripe = hexc("#c9a95e") if crop["id"] == "wheat" else hexc("#d4bf85")
                cereal(card, col, 7, ripe, heads=col >= 4, barley=crop["id"] == "barley")
            elif crop["id"] == "canola":
                canola(card, col)
            else:
                corn(card, col)
            atlas.paste(card.finish(), (col * CELL_W, row * CELL_H))
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(out, optimize=True)
    print(f"wrote {out} ({atlas.width}x{atlas.height})")


if __name__ == "__main__":
    main()
