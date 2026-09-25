#!/usr/bin/env python3
"""Convert Screaming Brain Studios 2:1 isometric tiles (CC0) into top-down atlases.

Each source sheet is 1024x384: 4x3 tiles of 256x128, magenta (#ff00ff) is transparent.
Every tile is a flat diamond, so a single affine transform "unprojects" it back to a square
seen from above. The road/path shape is then classified by which of the 4 edges it touches,
giving a connection mask (1=north/-z, 2=east/+x, 4=south/+z, 8=west/-x) used for auto-tiling.

Output per selected sheet: <out>/<name>_atlas.png (4x4 cells of 256 px) + an entry in manifest.json.

Usage: tools/convert_tiles.py [--roads ZIP] [--paths ZIP] [--out game/assets/textures/tiles]
"""
import argparse
import io
import json
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image

TILE_W, TILE_H = 256, 128
OUT = 256  # top-down cell size in pixels

# Sheets converted by default: name -> (zip key, path inside zip).
DEFAULT_SHEETS = {
    "road_country": ("roads", "Large Roads/Realistic Roads/Road_Asphalt_03-256x128.png"),
    "road_curb": ("roads", "Large Roads/Realistic Roads/Road_Asphalt_04-256x128.png"),
    "track_dirt": ("paths", "Exterior Large 256x128/Dry/Path_Dry_26-256x128.png"),
    "track_grass": ("paths", "Exterior Large 256x128/Grass/Path_Grass_01-256x128.png"),
    "path_stones": ("paths", "Exterior Large 256x128/Stones/Path_Stones_03-256x128.png"),
    "stream": ("paths", "Exterior Large 256x128/Elements/Path_Elements_01-256x128.png"),
}


def key_magenta(rgb: np.ndarray) -> np.ndarray:
    """RGB uint8 -> RGBA float, with magenta (and magenta-tinted fringe) made transparent."""
    f = rgb.astype(np.float32) / 255.0
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    # "Magenta-ness": high red and blue, low green. Soft ramp keeps anti-aliased edges.
    m = np.clip(((np.minimum(r, b) - g) - 0.35) / 0.4, 0.0, 1.0)
    alpha = 1.0 - m
    # Remove the magenta contribution from fringe pixels (un-blend toward the tile color).
    magenta = np.array([1.0, 0.0, 1.0], dtype=np.float32)
    denom = np.maximum(alpha, 1e-3)[..., None]
    color = np.clip((f - (1.0 - alpha)[..., None] * magenta) / denom, 0.0, 1.0)
    return np.dstack([color, alpha])


def bleed(rgba: np.ndarray, iterations: int = 12) -> np.ndarray:
    """Spread opaque colors into transparent pixels so filtering/mipmaps don't darken edges."""
    out = rgba.copy()
    solid = out[..., 3] > 0.5
    color = out[..., :3] * solid[..., None]
    weight = solid.astype(np.float32)
    for _ in range(iterations):
        c = np.zeros_like(color)
        w = np.zeros_like(weight)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            c += np.roll(np.roll(color, dy, 0), dx, 1)
            w += np.roll(np.roll(weight, dy, 0), dx, 1)
        fill = (weight == 0) & (w > 0)
        color[fill] = c[fill] / w[fill][:, None]
        weight[fill] = 1.0
    out[..., :3] = np.where(weight[..., None] > 0, color, out[..., :3])
    return out


def unproject(tile: Image.Image) -> Image.Image:
    """Diamond (top=(128,0), right=(256,64), bottom=(128,128), left=(0,64)) -> square.

    Square u (east, +x) runs top->right vertex, v (south, +z) runs top->left vertex, which is how
    the tile reads under a camera yawed 45 degrees looking down the +x/+z diagonal.
    """
    s = OUT
    coeffs = (128.0 / s, -128.0 / s, 128.0, 64.0 / s, 64.0 / s, 0.0)
    return tile.transform((s, s), Image.Transform.AFFINE, coeffs, resample=Image.Resampling.BICUBIC)


def edge_mask(square: np.ndarray) -> int:
    """4-bit connection mask from alpha coverage in the middle of each edge."""
    a = square[..., 3]
    n = a.shape[0]
    band = slice(int(n * 0.4), int(n * 0.6))
    depth = slice(0, int(n * 0.08))
    depth_end = slice(n - int(n * 0.08), n)
    cover = {
        1: a[depth, band].mean(),       # north: v = 0 row
        2: a[band, depth_end].mean(),   # east: u = 1 column
        4: a[depth_end, band].mean(),   # south: v = 1 row
        8: a[band, depth].mean(),       # west: u = 0 column
    }
    return sum(bit for bit, c in cover.items() if c > 0.5)


def convert_sheet(sheet: Image.Image):
    rgb = np.asarray(sheet.convert("RGB"))
    rgba = bleed(key_magenta(rgb))
    tiles = []
    for row in range(3):
        for col in range(4):
            crop = rgba[row * TILE_H:(row + 1) * TILE_H, col * TILE_W:(col + 1) * TILE_W]
            if crop[..., 3].mean() < 0.02:
                continue  # empty slot
            img = Image.fromarray((crop * 255).astype(np.uint8), "RGBA")
            sq = np.asarray(unproject(img)).astype(np.float32) / 255.0
            tiles.append((edge_mask(sq), sq, float(sq[..., 3].mean())))
    return tiles


def main():
    root = Path(__file__).resolve().parent.parent
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--roads", default=str(Path.home() / "Downloads/sbs_-_isometric_roads_-_large.zip"))
    ap.add_argument("--paths", default=str(Path.home() / "Downloads/sbs_-_isometric_pathways_pack_-_large.zip"))
    ap.add_argument("--out", default=str(root / "game/assets/textures/tiles"))
    args = ap.parse_args()

    zips = {"roads": zipfile.ZipFile(args.roads), "paths": zipfile.ZipFile(args.paths)}
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    manifest = {"cellPixels": OUT, "columns": 4, "sets": {}}

    for name, (zkey, member) in DEFAULT_SHEETS.items():
        sheet = Image.open(io.BytesIO(zips[zkey].read(member)))
        tiles = convert_sheet(sheet)
        atlas = np.zeros((OUT * 4, OUT * 4, 4), dtype=np.float32)
        masks = {}
        for idx, (mask, sq, coverage) in enumerate(tiles):
            r, c = divmod(idx, 4)
            atlas[r * OUT:(r + 1) * OUT, c * OUT:(c + 1) * OUT] = sq
            # Keep the first tile found for each mask.
            masks.setdefault(str(mask), idx)
        Image.fromarray((np.clip(atlas, 0, 1) * 255).astype(np.uint8), "RGBA").save(out / f"{name}_atlas.png", optimize=True)
        manifest["sets"][name] = {"source": member, "masks": masks}
        print(f"{name}: {len(tiles)} tiles, masks {sorted(int(m) for m in masks)}")

    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    (out / "LICENSE.txt").write_text(
        "Source tiles: Screaming Brain Studios, CC0 1.0 (public domain).\n"
        "https://opengameart.org/content/700-isometric-road-tiles\n"
        "https://opengameart.org/content/5000-isometric-pathway-tiles\n"
        "Converted to top-down atlases by tools/convert_tiles.py.\n")


if __name__ == "__main__":
    main()
