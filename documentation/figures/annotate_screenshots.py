#!/usr/bin/env python3
"""Crop and highlight real NINA / Hocus Focus screenshots for the documentation site.

Unlike ``generate_figures.py`` (which renders synthetic figures from code and is run by CI),
these screenshots come from a live Windows app captured through the Windows MCP. They cannot be
produced in CI, so this helper is run locally and its outputs are committed. To keep the result
reproducible, every published image is driven by a small JSON *sidecar*: the untouched capture in
``documentation/docs/assets/screenshots/raw/`` plus a crop box and a list of highlights. Re-running
this script reproduces the published PNG, so refining a highlight is a sidecar edit, never a
re-capture.

Sidecars live in ``documentation/figures/screenshots/*.json``:

    {
      "source": "star-detector-simple.png",      # file under assets/screenshots/raw/
      "crop":   [x0, y0, x1, y1],                 # optional; raw pixel space
      "scale_from": [1600, 1000],                 # optional; capture window size, informational
      "highlights": [
        {"box": [x0, y0, x1, y1], "style": "rect"},                 # rounded outline (default)
        {"box": [...], "style": "spotlight"},                       # dim everything else
        {"box": [...], "style": "arrow", "tail": [x, y], "label": "Turn on"}
      ],
      "output": "star-detector-simple.png"        # written under assets/screenshots/
    }

All coordinates (``crop`` and every ``box``/``tail``) are in the RAW capture's pixel space; the
crop offset is subtracted automatically, so you read every number straight off the raw PNG.

Usage:
    python annotate_screenshots.py                      # render every sidecar
    python annotate_screenshots.py --only star-detector-simple autofocus-options
    python annotate_screenshots.py --check              # verify committed PNGs match their sidecars
    python annotate_screenshots.py --list               # list known sidecars
"""

from __future__ import annotations

import argparse
import json
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

# --------------------------------------------------------------------------------------------------
# Layout & style
# --------------------------------------------------------------------------------------------------

_HERE = Path(__file__).resolve().parent
SIDECAR_DIR = _HERE / "screenshots"
RAW_DIR = _HERE.parent / "docs" / "assets" / "screenshots" / "raw"
OUT_DIR = _HERE.parent / "docs" / "assets" / "screenshots"

# Palette shared with generate_figures.py so screenshots and synthetic figures look like one set.
ACCENT = "#3F51B5"      # indigo (Material primary)
ACCENT2 = "#E91E63"     # pink, the default emphasis colour (stands out on NINA's dark theme)
DEFAULT_COLOR = ACCENT2

SS = 2                  # supersampling factor for crisp anti-aliased outlines/arrows
BOX_PAD = 6             # px the rect/spotlight outline is expanded beyond the control
RECT_WIDTH = 5          # outline thickness (1x px)
RECT_RADIUS = 14        # rounded-corner radius (1x px)
SPOTLIGHT_OPACITY = 0.55
ARROW_WIDTH = 7
ARROW_HEAD = 26         # arrowhead length (1x px)
LABEL_SIZE = 30


def _hex_to_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def _rgba(h, alpha=255):
    return _hex_to_rgb(h) + (alpha,)


def _font(size):
    """A readable TrueType font, falling back to PIL's bitmap default if none is found."""
    for path in (
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "DejaVuSans.ttf",
    ):
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


# --------------------------------------------------------------------------------------------------
# Drawing primitives (operate on the supersampled overlay unless noted)
# --------------------------------------------------------------------------------------------------

def _local(box, ox, oy):
    """Translate a raw-space box/point into the cropped image's coordinate space."""
    if len(box) == 2:
        return (box[0] - ox, box[1] - oy)
    return (box[0] - ox, box[1] - oy, box[2] - ox, box[3] - oy)


def _scaled_box(box, pad=0):
    x0, y0, x1, y1 = box
    return [
        (x0 - pad) * SS, (y0 - pad) * SS,
        (x1 + pad) * SS, (y1 + pad) * SS,
    ]


def _draw_outline(draw, box, color, pad=BOX_PAD, width=RECT_WIDTH, radius=RECT_RADIUS):
    """Rounded rectangle with a subtle dark halo so it reads on both light and dark UI."""
    xy = _scaled_box(box, pad)
    # Halo: slightly wider, semi-transparent black underneath for contrast on bright panels.
    draw.rounded_rectangle(
        [xy[0] - SS, xy[1] - SS, xy[2] + SS, xy[3] + SS],
        radius=(radius + 1) * SS, outline=(0, 0, 0, 110), width=(width + 2) * SS,
    )
    draw.rounded_rectangle(xy, radius=radius * SS, outline=_rgba(color), width=width * SS)


def _draw_arrow(draw, box, color, tail=None, label=None, ox=0, oy=0):
    """Arrow from `tail` (or a default point to the right of the box) into the control."""
    x0, y0, x1, y1 = box
    cy = (y0 + y1) / 2.0
    if tail is None:
        tail_pt = (x1 + 70, cy)          # default: come in from the right
        tip = (x1 + 4, cy)
    else:
        tail_pt = _local(tail, ox, oy)
        # Aim the tip at the nearest point on the box edge from the tail.
        tx = min(max(tail_pt[0], x0), x1)
        ty = min(max(tail_pt[1], y0), y1)
        tip = (tx, ty)

    tail_s = (tail_pt[0] * SS, tail_pt[1] * SS)
    tip_s = (tip[0] * SS, tip[1] * SS)
    draw.line([tail_s, tip_s], fill=_rgba(color), width=ARROW_WIDTH * SS)

    # Arrowhead: a filled triangle at the tip, perpendicular to the shaft.
    import math
    ang = math.atan2(tip_s[1] - tail_s[1], tip_s[0] - tail_s[0])
    h = ARROW_HEAD * SS
    spread = math.radians(26)
    p1 = (tip_s[0] - h * math.cos(ang - spread), tip_s[1] - h * math.sin(ang - spread))
    p2 = (tip_s[0] - h * math.cos(ang + spread), tip_s[1] - h * math.sin(ang + spread))
    draw.polygon([tip_s, p1, p2], fill=_rgba(color))

    if label:
        font = _font(LABEL_SIZE * SS)
        # Anchor the label just beyond the tail, away from the control.
        anchor = "lm" if tail_pt[0] >= tip[0] else "rm"
        lx = tail_s[0] + (10 * SS if anchor == "lm" else -10 * SS)
        # Halo for legibility, then the text.
        for dx, dy in ((-2, 0), (2, 0), (0, -2), (0, 2)):
            draw.text((lx + dx * SS, tail_s[1] + dy * SS), label, font=font,
                      fill=(0, 0, 0, 160), anchor=anchor)
        draw.text((lx, tail_s[1]), label, font=font, fill=_rgba(color), anchor=anchor)


# --------------------------------------------------------------------------------------------------
# Render one sidecar
# --------------------------------------------------------------------------------------------------

def render(spec, out_dir):
    base = Image.open(RAW_DIR / spec["source"]).convert("RGBA")
    crop = spec.get("crop")
    if crop:
        base = base.crop(tuple(crop))
        ox, oy = crop[0], crop[1]
    else:
        ox = oy = 0
    W, H = base.size
    highlights = spec.get("highlights", [])

    # 1) Spotlight dimming (flat fill, drawn at 1x; no anti-aliasing needed).
    spots = [h for h in highlights if h.get("style") == "spotlight"]
    if spots:
        dim = Image.new("RGBA", (W, H), (0, 0, 0, int(255 * SPOTLIGHT_OPACITY)))
        dd = ImageDraw.Draw(dim)
        for h in spots:
            x0, y0, x1, y1 = _local(h["box"], ox, oy)
            pad = h.get("pad", BOX_PAD)
            dd.rectangle([x0 - pad, y0 - pad, x1 + pad, y1 + pad], fill=(0, 0, 0, 0))
        base = Image.alpha_composite(base, dim)

    # 2) Outlines / arrows / labels on a supersampled overlay for clean edges.
    overlay = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay)
    for h in highlights:
        style = h.get("style", "rect")
        color = h.get("color", DEFAULT_COLOR)
        box = _local(h["box"], ox, oy)
        if style in ("rect", "spotlight"):
            _draw_outline(od, box, color,
                          pad=h.get("pad", BOX_PAD),
                          width=h.get("width", RECT_WIDTH),
                          radius=h.get("radius", RECT_RADIUS))
        elif style == "arrow":
            _draw_arrow(od, box, color, tail=h.get("tail"), label=h.get("label"), ox=ox, oy=oy)
        else:
            raise ValueError(f"unknown highlight style: {style!r}")
    overlay = overlay.resize((W, H), Image.LANCZOS)
    base = Image.alpha_composite(base, overlay)

    out_path = out_dir / spec["output"]
    base.convert("RGB").save(out_path, "PNG", optimize=True)
    return out_path


# --------------------------------------------------------------------------------------------------
# Sidecar registry + CLI
# --------------------------------------------------------------------------------------------------

def load_sidecars():
    """Map {sidecar-stem: spec} for every JSON under SIDECAR_DIR."""
    specs = {}
    for p in sorted(SIDECAR_DIR.glob("*.json")):
        with open(p, encoding="utf-8") as f:
            specs[p.stem] = json.load(f)
    return specs


def _structural_diff(path_a, path_b):
    """Tolerant compare: downscale to 32x32 grayscale, mean abs difference in [0,1]."""
    import numpy as np

    def load(p):
        return np.asarray(Image.open(p).convert("L").resize((32, 32))) / 255.0

    return float(np.mean(np.abs(load(path_a) - load(path_b))))


def run_check(specs):
    tol = 0.05
    missing, drifted = [], []
    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        for name, spec in specs.items():
            fresh = render(spec, tmp_dir)
            committed = OUT_DIR / spec["output"]
            if not committed.exists():
                missing.append(spec["output"])
                continue
            d = _structural_diff(fresh, committed)
            if d > tol:
                drifted.append((spec["output"], d))
    if missing:
        print(f"MISSING committed screenshots: {', '.join(missing)}")
    if drifted:
        print("DRIFTED screenshots (structural diff > %.3f):" % tol)
        for name, d in drifted:
            print(f"  {name}: {d:.4f}")
    if not missing and not drifted:
        print(f"OK: all {len(specs)} screenshots match committed PNGs.")
        return 0
    return 1


def main(argv=None):
    ap = argparse.ArgumentParser(description="Annotate Hocus Focus documentation screenshots.")
    ap.add_argument("--only", nargs="*", help="only render these sidecars (by stem or output name)")
    ap.add_argument("--check", action="store_true", help="verify committed PNGs match their sidecars")
    ap.add_argument("--list", action="store_true", help="list known sidecars and exit")
    args = ap.parse_args(argv)

    specs = load_sidecars()
    if args.list:
        for name, spec in specs.items():
            print(f"{name:40s} -> {spec['output']}")
        return 0
    if not specs:
        print(f"no sidecars found under {SIDECAR_DIR}")
        return 0

    if args.check:
        return run_check(specs)

    if args.only:
        wanted = set(args.only)
        chosen = {n: s for n, s in specs.items()
                  if n in wanted or s["output"] in wanted or Path(s["output"]).stem in wanted}
        unknown = wanted - set(chosen) - {Path(s["output"]).stem for s in chosen.values()}
        if unknown:
            ap.error(f"unknown sidecar(s): {', '.join(sorted(unknown))}")
        specs = chosen

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for name, spec in specs.items():
        path = render(spec, OUT_DIR)
        print(f"wrote {path}")
    print(f"done: {len(specs)} screenshot(s) -> {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
