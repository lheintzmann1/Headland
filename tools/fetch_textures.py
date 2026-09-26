#!/usr/bin/env python3
"""Download the CC0 ground materials (ambientCG, 1K JPG) and pack them for the terrain shader.

Outputs (in game/assets/textures/terrain/):
  terrain_albedo.jpg / terrain_normal.jpg  vertical strips of 1024x1024 layers, imported by Godot
                                           as Texture2DArray (see the .import files written here).
  CREDITS section appended to game/assets/CREDITS.md.

Layer order is the contract with shaders/terrain.gdshader (LAYER_* constants).
Raw downloads are cached in tools/_cache/ (git-ignored).

Usage: tools/fetch_textures.py [--size 1024]
"""
import argparse
import io
import urllib.request
import zipfile
from pathlib import Path

from PIL import Image

LAYERS = [
    ("grass", "Grass004"),
    ("meadow", "Ground037"),
    ("soil", "Ground048"),
    ("residue", "Ground082S"),
    ("gravel", "Gravel041"),
    ("snow", "Snow014"),
]
UA = {"User-Agent": "Mozilla/5.0 (X11; Linux x86_64) HeadlandAssetFetch/1.0"}

IMPORT_TEMPLATE = """[remap]

importer="2d_array_texture"
type="CompressedTexture2DArray"

[deps]

source_file="res://assets/textures/terrain/{name}"

[params]

compress/mode=2
compress/high_quality=false
compress/lossy_quality=0.7
compress/hdr_compression=1
compress/channel_pack=0
mipmaps/generate=true
mipmaps/limit=-1
slices/horizontal=1
slices/vertical={layers}
"""


def download(asset_id: str, cache: Path) -> zipfile.ZipFile:
    path = cache / f"{asset_id}_1K-JPG.zip"
    if not path.exists():
        url = f"https://ambientcg.com/get?file={asset_id}_1K-JPG.zip"
        print(f"downloading {url}")
        data = urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60).read()
        path.write_bytes(data)
    return zipfile.ZipFile(path)


def member(z: zipfile.ZipFile, suffix: str) -> Image.Image:
    name = next(n for n in z.namelist() if n.endswith(suffix))
    return Image.open(io.BytesIO(z.read(name))).convert("RGB")


def main():
    root = Path(__file__).resolve().parent.parent
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--size", type=int, default=1024)
    args = ap.parse_args()

    cache = root / "tools/_cache"
    cache.mkdir(parents=True, exist_ok=True)
    out = root / "game/assets/textures/terrain"
    out.mkdir(parents=True, exist_ok=True)

    s = args.size
    albedo = Image.new("RGB", (s, s * len(LAYERS)))
    normal = Image.new("RGB", (s, s * len(LAYERS)))
    for i, (layer, asset) in enumerate(LAYERS):
        z = download(asset, cache)
        albedo.paste(member(z, "_Color.jpg").resize((s, s), Image.Resampling.LANCZOS), (0, i * s))
        normal.paste(member(z, "_NormalGL.jpg").resize((s, s), Image.Resampling.LANCZOS), (0, i * s))
        print(f"layer {i}: {layer} <- {asset}")

    for name, img in (("terrain_albedo.jpg", albedo), ("terrain_normal.jpg", normal)):
        img.save(out / name, quality=90)
        (out / f"{name}.import").write_text(IMPORT_TEMPLATE.format(name=name, layers=len(LAYERS)))

    credits = root / "game/assets/CREDITS.md"
    text = credits.read_text() if credits.exists() else "# Asset credits\n"
    marker = "## Terrain materials"
    if marker not in text:
        lines = [f"\n{marker}\n", "CC0 1.0, from ambientCG (https://ambientcg.com):\n"]
        lines += [f"- {layer}: [{asset}](https://ambientcg.com/view?id={asset})\n" for layer, asset in LAYERS]
        credits.write_text(text.rstrip("\n") + "\n" + "".join(lines))


if __name__ == "__main__":
    main()
