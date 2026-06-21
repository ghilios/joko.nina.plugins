#!/usr/bin/env python3
"""Generate the synthetic illustrative figures for the Hocus Focus documentation site.

Every figure is deterministic (each seeds its own ``numpy`` RNG), so re-running reproduces
byte-for-byte-equivalent images up to matplotlib/freetype rendering differences. The committed
PNGs under ``documentation/docs/assets/figures/`` are the published source of truth; CI runs this
script with ``--check`` to verify they have not drifted.

Usage:
    python generate_figures.py                # (re)generate every figure
    python generate_figures.py --only af_vcurve objective_sfocus
    python generate_figures.py --check        # regenerate to a temp dir and compare to committed
    python generate_figures.py --out <dir>    # write somewhere other than the default assets dir

There are two visual registers:
  * "image" figures (star fields / PSFs / donuts) use a dark astro look: grayscale or magma,
    no axes, optional annotations.
  * "plot" figures (objective terms, V-curves, score curves) use a clean light-friendly line
    style with labelled axes and LaTeX (mathtext) titles that match the body MathJax.
"""

from __future__ import annotations

import argparse
import sys
import tempfile
from pathlib import Path

import matplotlib

matplotlib.use("Agg")  # headless; no display required (CI + WSL)
import matplotlib.pyplot as plt
import numpy as np

# --------------------------------------------------------------------------------------------------
# Constants & style
# --------------------------------------------------------------------------------------------------

DEFAULT_OUT = Path(__file__).resolve().parent.parent / "docs" / "assets" / "figures"
DPI = 144
ACCENT = "#3F51B5"      # indigo, matches the Material theme primary
ACCENT2 = "#E91E63"     # pink, for "rejected"/secondary series
GOOD = "#2E7D32"        # green, for "accepted"
WARN = "#F9A825"        # amber, for thresholds
STAR_CMAP = "gray"

plt.rcParams.update(
    {
        "figure.dpi": DPI,
        "savefig.dpi": DPI,
        "font.family": "DejaVu Sans",   # ships with matplotlib -> stable across machines
        "font.size": 11,
        "axes.titlesize": 12,
        "axes.grid": True,
        "grid.alpha": 0.25,
        "axes.spines.top": False,
        "axes.spines.right": False,
    }
)


# --------------------------------------------------------------------------------------------------
# Pure-numpy primitives
# --------------------------------------------------------------------------------------------------

def _grid(shape):
    h, w = shape
    yy, xx = np.mgrid[0:h, 0:w]
    return xx.astype(float), yy.astype(float)


def gaussian_psf(shape, x0, y0, sigma, amp=1.0):
    xx, yy = _grid(shape)
    r2 = (xx - x0) ** 2 + (yy - y0) ** 2
    return amp * np.exp(-r2 / (2.0 * sigma ** 2))


def elliptical_gaussian(shape, x0, y0, sx, sy, theta=0.0, amp=1.0):
    xx, yy = _grid(shape)
    xr = (xx - x0) * np.cos(theta) + (yy - y0) * np.sin(theta)
    yr = -(xx - x0) * np.sin(theta) + (yy - y0) * np.cos(theta)
    return amp * np.exp(-(xr ** 2 / (2 * sx ** 2) + yr ** 2 / (2 * sy ** 2)))


def moffat_psf(shape, x0, y0, alpha, beta, amp=1.0):
    xx, yy = _grid(shape)
    r2 = (xx - x0) ** 2 + (yy - y0) ** 2
    return amp * (1.0 + r2 / alpha ** 2) ** (-beta)


def moffat_radial(r, alpha, beta):
    return (1.0 + (r / alpha) ** 2) ** (-beta)


def defocused_donut(shape, x0, y0, r_outer, r_inner, amp=1.0, softness=2.0):
    """A ring/annulus star (central-obstruction shadow), softened at both edges."""
    xx, yy = _grid(shape)
    r = np.sqrt((xx - x0) ** 2 + (yy - y0) ** 2)
    outer = 1.0 / (1.0 + np.exp((r - r_outer) / softness))
    inner = 1.0 / (1.0 + np.exp((r_inner - r) / softness))
    ring = outer * inner
    return amp * ring / max(ring.max(), 1e-9)


def add_noise(img, sky, read_noise, rng, gain=1500.0):
    """Add a sky pedestal + shot (Poisson-like) + read (Gaussian) noise.

    Images here are normalized to ~[0, 1], so ``gain`` is electrons-per-unit: shot-noise std is
    sqrt(signal / gain), which keeps the noise physically gentle (a large gain ⇒ high SNR). With
    the default ``gain``, a unit-amplitude star has shot std ≈ 0.026.
    """
    out = img + sky
    shot = rng.normal(0.0, np.sqrt(np.maximum(out, 0.0) / max(gain, 1e-9)))
    read = rng.normal(0.0, read_noise, size=img.shape)
    return out + shot + read


def star_field(shape, n_stars, rng, max_amp=1.0, sigma_range=(1.2, 2.6), margin=8):
    h, w = shape
    img = np.zeros(shape)
    cats = []
    for _ in range(n_stars):
        x0 = rng.uniform(margin, w - margin)
        y0 = rng.uniform(margin, h - margin)
        amp = max_amp * rng.uniform(0.1, 1.0)
        sigma = rng.uniform(*sigma_range)
        img += gaussian_psf(shape, x0, y0, sigma, amp)
        cats.append((x0, y0, amp, sigma))
    return img, cats


# --------------------------------------------------------------------------------------------------
# Render helpers
# --------------------------------------------------------------------------------------------------

def _stretch(img, lo=1.0, hi=99.5):
    a, b = np.percentile(img, [lo, hi])
    return np.clip((img - a) / max(b - a, 1e-9), 0, 1)


def save_image_panel(panels, name, out_dir, cmap=STAR_CMAP, figsize=None):
    """panels: list of (title, 2D array). Renders a row of imshow panels with no ticks."""
    n = len(panels)
    figsize = figsize or (3.1 * n, 3.4)
    fig, axes = plt.subplots(1, n, figsize=figsize)
    if n == 1:
        axes = [axes]
    for ax, (title, arr) in zip(axes, panels):
        ax.imshow(_stretch(arr), cmap=cmap, origin="upper", interpolation="nearest")
        ax.set_title(title)
        ax.set_xticks([])
        ax.set_yticks([])
        ax.grid(False)
        for s in ax.spines.values():
            s.set_visible(False)
    fig.patch.set_facecolor("white")
    return _finalize(fig, name, out_dir)


def save_plot(fig, name, out_dir):
    fig.tight_layout()
    return _finalize(fig, name, out_dir)


def _finalize(fig, name, out_dir):
    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / f"{name}.png"
    fig.savefig(path, bbox_inches="tight", facecolor="white")
    plt.close(fig)
    return path


# --------------------------------------------------------------------------------------------------
# Figure builders  (one per documented concept)
# --------------------------------------------------------------------------------------------------

def fig_psf_models(out_dir):
    shape = (49, 49)
    c = 24
    g = gaussian_psf(shape, c, c, 4.0)
    m40 = moffat_psf(shape, c, c, 6.0, 4.0)
    m15 = moffat_psf(shape, c, c, 6.0, 1.5)
    fig, axes = plt.subplots(1, 2, figsize=(9.5, 4.0))
    # left: 2D images stacked via a small montage
    montage = np.concatenate([_stretch(g), _stretch(m40), _stretch(m15)], axis=1)
    axes[0].imshow(montage, cmap=STAR_CMAP, interpolation="nearest")
    axes[0].set_xticks([c, c + shape[1], c + 2 * shape[1]])
    axes[0].set_xticklabels(["Gaussian", "Moffat β=4.0", "Moffat β=1.5"])
    axes[0].set_yticks([])
    axes[0].grid(False)
    axes[0].set_title("Point-spread function shapes")
    # right: radial profiles (log y) — Moffat has heavier wings
    r = np.linspace(0, 16, 400)
    axes[1].plot(r, np.exp(-r ** 2 / (2 * 4.0 ** 2)), color=ACCENT, label="Gaussian")
    axes[1].plot(r, moffat_radial(r, 6.0, 4.0), color=GOOD, label="Moffat β=4.0")
    axes[1].plot(r, moffat_radial(r, 6.0, 2.5), color=WARN, label="Moffat β=2.5")
    axes[1].plot(r, moffat_radial(r, 6.0, 1.5), color=ACCENT2, label="Moffat β=1.5")
    axes[1].set_yscale("log")
    axes[1].set_ylim(1e-3, 1.2)
    axes[1].set_xlabel("radius (px)")
    axes[1].set_ylabel("normalized intensity")
    axes[1].set_title("Radial profiles — Moffat has heavier wings")
    axes[1].legend(frameon=False, fontsize=9)
    return save_plot(fig, "psf-models", out_dir)


def fig_defocused_donut(out_dir):
    shape = (81, 81)
    c = 40
    focused = gaussian_psf(shape, c, c, 3.0)
    donut = defocused_donut(shape, c, c, r_outer=28, r_inner=14, softness=3.0)
    fig, axes = plt.subplots(1, 3, figsize=(11, 3.8))
    for ax, (title, arr) in zip(
        axes[:2], [("Near focus", focused), ("Far from focus (donut)", donut)]
    ):
        ax.imshow(_stretch(arr), cmap=STAR_CMAP, interpolation="nearest")
        ax.set_title(title)
        ax.set_xticks([])
        ax.set_yticks([])
        ax.grid(False)
    row = shape[0] // 2
    axes[2].plot(focused[row], color=ACCENT, label="near focus")
    axes[2].plot(donut[row], color=ACCENT2, label="donut")
    axes[2].set_title("Horizontal cut")
    axes[2].set_xlabel("column (px)")
    axes[2].set_ylabel("intensity")
    axes[2].legend(frameon=False, fontsize=9)
    return save_plot(fig, "defocused-donut", out_dir)


def fig_hot_pixel(out_dir):
    rng = np.random.default_rng(11)
    shape = (60, 60)
    img, _ = star_field(shape, 10, rng, max_amp=0.8, sigma_range=(1.4, 2.2))
    img = add_noise(img, sky=0.05, read_noise=0.02, rng=rng)
    hot = img.copy()
    hot[20, 38] = img.max() * 6  # single bright hot pixel
    # 3x3 median filter (manual, no scipy dependency required here)
    med = np.copy(hot)
    for y in range(1, shape[0] - 1):
        for x in range(1, shape[1] - 1):
            med[y, x] = np.median(hot[y - 1 : y + 2, x - 1 : x + 2])
    return save_image_panel(
        [("Raw + hot pixel", hot), ("After 3×3 median", med)],
        "hot-pixel",
        out_dir,
        figsize=(7.2, 3.8),
    )


def fig_saturated_star(out_dir):
    shape = (41, 41)
    c = 20
    star = gaussian_psf(shape, c, c, 4.0, amp=1.8)  # peak above full well
    full_well = 1.0
    sat = np.clip(star, 0, full_well)
    fig, axes = plt.subplots(1, 2, figsize=(8.6, 3.8))
    axes[0].imshow(_stretch(sat), cmap=STAR_CMAP, interpolation="nearest")
    axes[0].set_title("Saturated star (flat-topped core)")
    axes[0].set_xticks([])
    axes[0].set_yticks([])
    axes[0].grid(False)
    row = shape[0] // 2
    axes[1].plot(star[row], color=ACCENT, ls="--", label="true profile")
    axes[1].plot(sat[row], color=ACCENT2, label="recorded (clipped)")
    axes[1].axhline(full_well, color=WARN, lw=1, label="saturation threshold")
    axes[1].set_title("Clipping destroys the core")
    axes[1].set_xlabel("column (px)")
    axes[1].set_ylabel("intensity")
    axes[1].legend(frameon=False, fontsize=9)
    return save_plot(fig, "saturated-star", out_dir)


def fig_eccentric_star(out_dir):
    shape = (41, 41)
    c = 20
    round_star = elliptical_gaussian(shape, c, c, 4.0, 4.0)
    elong = elliptical_gaussian(shape, c, c, 6.0, 2.6, theta=np.deg2rad(35))
    return save_image_panel(
        [("Round (e ≈ 0)", round_star), ("Elongated (high e)", elong)],
        "eccentric-star",
        out_dir,
        figsize=(7.2, 3.8),
    )


def fig_contamination_annulus(out_dir):
    shape = (61, 61)
    c = 30
    img = gaussian_psf(shape, c, c, 3.0, amp=1.0)
    img += gaussian_psf(shape, c + 17, c - 6, 3.0, amp=0.6)  # contaminating neighbor
    rng = np.random.default_rng(7)
    img = add_noise(img, sky=0.04, read_noise=0.02, rng=rng)
    fig, ax = plt.subplots(figsize=(4.6, 4.4))
    ax.imshow(_stretch(img), cmap=STAR_CMAP, interpolation="nearest")
    # draw the background annulus
    for rad, style in [(8, "-"), (15, "-")]:
        circ = plt.Circle((c, c), rad, fill=False, color=GOOD, lw=1.4, ls=style)
        ax.add_patch(circ)
    ax.text(c, c - 19, "background annulus", color=GOOD, ha="center", fontsize=8)
    ax.annotate(
        "brighter on\none side →\ncontaminant",
        xy=(c + 17, c - 6),
        xytext=(c - 6, c + 22),
        color=ACCENT2,
        fontsize=8,
        arrowprops=dict(arrowstyle="->", color=ACCENT2),
    )
    ax.set_xticks([])
    ax.set_yticks([])
    ax.grid(False)
    ax.set_title("Contamination: one-sided annulus excess")
    return _finalize(fig, "contamination-annulus", out_dir)


def fig_noise_reduction(out_dir):
    rng = np.random.default_rng(3)
    shape = (70, 70)
    img, _ = star_field(shape, 14, rng, max_amp=0.7, sigma_range=(1.3, 2.0))
    noisy = add_noise(img, sky=0.05, read_noise=0.09, rng=rng)
    # gaussian blur via separable kernel
    def blur(a, sigma):
        k = int(sigma * 3) | 1
        ax = np.arange(k) - k // 2
        g = np.exp(-(ax ** 2) / (2 * sigma ** 2))
        g /= g.sum()
        out = np.apply_along_axis(lambda m: np.convolve(m, g, mode="same"), 0, a)
        out = np.apply_along_axis(lambda m: np.convolve(m, g, mode="same"), 1, out)
        return out

    return save_image_panel(
        [("Raw (noisy)", noisy), ("Noise reduction (blur r=3)", blur(noisy, 1.6))],
        "noise-reduction",
        out_dir,
        figsize=(7.2, 3.8),
    )


def fig_structure_map(out_dir):
    rng = np.random.default_rng(21)
    shape = (90, 90)
    stars, cats = star_field(shape, 20, rng, max_amp=1.0, sigma_range=(1.6, 2.8))
    # add a strong, smooth large-scale "nebula" gradient (the thing the wavelet step removes)
    xx, yy = _grid(shape)
    nebula = 0.8 * np.exp(-((xx - 26) ** 2 + (yy - 64) ** 2) / (2 * 34 ** 2))
    nebula += 0.25 * (xx / shape[1])  # gentle linear gradient too
    img = add_noise(stars + nebula, sky=0.06, read_noise=0.012, rng=rng)

    # crude a-trous-like high pass: subtract a strongly blurred copy (removes large structures)
    def blur(a, sigma):
        k = int(sigma * 3) | 1
        ax = np.arange(k) - k // 2
        g = np.exp(-(ax ** 2) / (2 * sigma ** 2))
        g /= g.sum()
        out = np.apply_along_axis(lambda m: np.convolve(m, g, mode="same"), 0, a)
        return np.apply_along_axis(lambda m: np.convolve(m, g, mode="same"), 1, out)

    highpass = img - blur(img, 6.0)
    thr = np.median(highpass) + 3.0 * (1.4826 * np.median(np.abs(highpass - np.median(highpass))))
    binary = (highpass > thr).astype(float)
    return save_image_panel(
        [
            ("Raw frame (+ nebula)", img),
            ("Wavelet residual (high-pass)", highpass),
            ("Binarized structure map", binary),
        ],
        "structure-map",
        out_dir,
        figsize=(10.5, 3.7),
    )


def fig_hfr_half_flux(out_dir):
    r = np.linspace(0, 12, 500)
    profile = np.exp(-r ** 2 / (2 * 3.0 ** 2))
    flux = np.cumsum(profile * 2 * np.pi * r)
    flux /= flux[-1]
    hfr = r[np.searchsorted(flux, 0.5)]
    fig, ax = plt.subplots(figsize=(6.4, 4.0))
    ax.plot(r, flux, color=ACCENT, label="enclosed flux fraction")
    ax.axhline(0.5, color=WARN, lw=1, ls="--")
    ax.axvline(hfr, color=ACCENT2, lw=1.4)
    ax.annotate(
        f"HFR ≈ {hfr:.1f} px\n(radius enclosing half the flux)",
        xy=(hfr, 0.5),
        xytext=(hfr + 1.2, 0.28),
        fontsize=9,
        arrowprops=dict(arrowstyle="->", color=ACCENT2),
    )
    ax.set_xlabel("radius from star center (px)")
    ax.set_ylabel("fraction of total flux")
    ax.set_title("Half-Flux Radius (HFR)")
    ax.legend(frameon=False, fontsize=9, loc="lower right")
    return save_plot(fig, "hfr-half-flux", out_dir)


def fig_gate_distortion(out_dir):
    fig, axes = plt.subplots(1, 2, figsize=(8.4, 4.0))
    for ax, (title, fill, color) in zip(
        axes,
        [("Compact star — high fill ratio", True, GOOD), ("Stringy/diffuse — low fill", False, ACCENT2)],
    ):
        ax.add_patch(plt.Rectangle((0, 0), 10, 10, fill=False, edgecolor="gray", lw=1.5))
        rng = np.random.default_rng(5 if fill else 9)
        if fill:
            th = np.linspace(0, 2 * np.pi, 220)
            rr = rng.uniform(0, 3.6, th.size)
            xs, ys = 5 + rr * np.cos(th), 5 + rr * np.sin(th)
        else:
            xs = rng.uniform(0.5, 9.5, 70)
            ys = 5 + 0.7 * (xs - 5) + rng.normal(0, 0.6, xs.size)
        ax.scatter(xs, ys, s=8, color=color)
        ax.set_xlim(-0.5, 10.5)
        ax.set_ylim(-0.5, 10.5)
        ax.set_aspect("equal")
        ax.set_xticks([])
        ax.set_yticks([])
        ax.grid(False)
        ax.set_title(title)
    fig.suptitle(r"Max Distortion = pixel count / bounding-box area", fontsize=11)
    return save_plot(fig, "gate-distortion", out_dir)


def fig_gate_centering(out_dir):
    fig, axes = plt.subplots(1, 2, figsize=(8.4, 4.0))
    for ax, (title, cx, cy, ok) in zip(
        axes, [("Centroid inside tolerance", 5.3, 4.7, True), ("Centroid off-center", 7.8, 6.9, False)]
    ):
        ax.add_patch(plt.Rectangle((0, 0), 10, 10, fill=False, edgecolor="gray", lw=1.5))
        ax.add_patch(plt.Rectangle((3.5, 3.5), 3, 3, fill=False, edgecolor=ACCENT, lw=1.5, ls="--"))
        ax.scatter([cx], [cy], s=80, color=GOOD if ok else ACCENT2, marker="x", lw=2.5)
        ax.text(5, 7.2, "tolerance box", color=ACCENT, ha="center", fontsize=8)
        ax.set_xlim(-0.5, 10.5)
        ax.set_ylim(-0.5, 10.5)
        ax.set_aspect("equal")
        ax.set_xticks([])
        ax.set_yticks([])
        ax.grid(False)
        ax.set_title(title)
    fig.suptitle("Star Center Tolerance: centroid must fall in the inner box", fontsize=11)
    return save_plot(fig, "gate-centering", out_dir)


def fig_gate_min_size(out_dir):
    fig, ax = plt.subplots(figsize=(7.2, 3.6))
    sizes = [2, 3, 5, 8, 12]
    minbb = 5
    for i, s in enumerate(sizes):
        x = i * 3
        color = ACCENT2 if s < minbb else GOOD
        ax.add_patch(plt.Rectangle((x, 0), s * 0.18, s * 0.18, color=color, alpha=0.7))
        ax.text(x + s * 0.09, -0.3, f"{s}px", ha="center", fontsize=9)
    ax.axhline(0, color="gray", lw=0.5)
    ax.set_xlim(-0.5, 15)
    ax.set_ylim(-0.8, 2.6)
    ax.set_xticks([])
    ax.set_yticks([])
    ax.grid(False)
    ax.set_title("MinStarBoundingBoxSize = 5 → smaller candidates (pink) rejected")
    return save_plot(fig, "gate-min-size", out_dir)


def fig_gate_sensitivity(out_dir):
    x = np.linspace(0, 40, 400)
    noise = 1.0
    bg = 5.0
    bright = bg + 9 * np.exp(-(x - 20) ** 2 / (2 * 3 ** 2))
    dim = bg + 2.2 * np.exp(-(x - 20) ** 2 / (2 * 3 ** 2))
    fig, ax = plt.subplots(figsize=(6.6, 4.0))
    ax.plot(x, bright, color=GOOD, label="bright: (s−b)/n high → accept")
    ax.plot(x, dim, color=ACCENT2, label="dim: (s−b)/n low → reject")
    ax.axhline(bg, color="gray", ls="--", lw=1, label="background b")
    ax.axhline(bg + 2 * noise, color=WARN, ls=":", lw=1.2, label="b + sensitivity·n")
    ax.set_xlabel("pixel (px)")
    ax.set_ylabel("intensity")
    ax.set_title(r"Brightness Sensitivity gate: $(s-b)/n \geq$ threshold")
    ax.legend(frameon=False, fontsize=8.5, loc="upper right")
    return save_plot(fig, "gate-sensitivity", out_dir)


def fig_gate_flatness(out_dir):
    x = np.linspace(0, 20, 300)
    peaked = np.exp(-(x - 10) ** 2 / (2 * 2.0 ** 2))
    flat = np.clip(1.15 * np.exp(-(x - 10) ** 2 / (2 * 5.0 ** 2)), 0, 0.82)
    fig, ax = plt.subplots(figsize=(6.6, 4.0))
    ax.plot(x, peaked, color=GOOD, label="peaked: median ≪ peak → accept")
    ax.plot(x, flat, color=ACCENT2, label="flat blob: median ≈ peak → reject")
    ax.set_xlabel("pixel (px)")
    ax.set_ylabel("intensity")
    ax.set_title("Star Peak Response: flatness = median / peak")
    ax.legend(frameon=False, fontsize=9)
    return save_plot(fig, "gate-flatness", out_dir)


def fig_noise_clipping(out_dir):
    rng = np.random.default_rng(8)
    bg = rng.normal(0.10, 0.03, 60000)
    stars = rng.uniform(0.3, 1.0, 1500)
    data = np.concatenate([bg, stars])
    median = np.median(bg)
    sigma = 1.4826 * np.median(np.abs(bg - median))
    fig, ax = plt.subplots(figsize=(6.8, 4.0))
    ax.hist(data, bins=120, color="lightgray", edgecolor="none")
    for k, c in [(2, WARN), (4, ACCENT)]:
        ax.axvline(median + k * sigma, color=c, lw=1.6, label=f"median + {k}·σ")
    ax.set_yscale("log")
    ax.set_xlabel("pixel value")
    ax.set_ylabel("count (log)")
    ax.set_title("Noise Clipping Multiplier sets the binarization floor")
    ax.legend(frameon=False, fontsize=9)
    return save_plot(fig, "noise-clipping", out_dir)


def fig_pixel_sample_size(out_dir):
    shape = (15, 15)
    c = 7
    star = gaussian_psf(shape, c + 0.3, c - 0.2, 1.2)
    fig, ax = plt.subplots(figsize=(4.8, 4.6))
    ax.imshow(_stretch(star), cmap=STAR_CMAP, interpolation="nearest", extent=[0, 15, 15, 0])
    for g in np.arange(0, 15.1, 0.5):
        ax.axhline(g, color=ACCENT, lw=0.4, alpha=0.5)
        ax.axvline(g, color=ACCENT, lw=0.4, alpha=0.5)
    ax.set_title("Sub-pixel sampling (0.5 px grid)\nfor undersampled stars")
    ax.set_xticks([])
    ax.set_yticks([])
    ax.grid(False)
    return _finalize(fig, "pixel-sample-size", out_dir)


# -------- optimization plots --------

def _hyperbola(x, xmin, a, b, c):
    return c + a * np.sqrt(1.0 + ((x - xmin) / b) ** 2)


def fig_af_vcurve(out_dir):
    rng = np.random.default_rng(42)
    xmin, a, b, c = 5000.0, 3.5, 280.0, 1.4
    pos = np.linspace(4200, 5800, 9)
    true = _hyperbola(pos, xmin, a, b, c)
    meas = true + rng.normal(0, 0.12, pos.size)
    xfit = np.linspace(4100, 5900, 400)
    fig, ax = plt.subplots(figsize=(7.2, 4.4))
    ax.plot(xfit, _hyperbola(xfit, xmin, a, b, c), color=ACCENT, label="hyperbolic fit")
    ax.errorbar(pos, meas, yerr=0.15, fmt="o", color=GOOD, ms=5, capsize=2, label="measured HFR")
    ax.axvline(xmin, color=ACCENT2, lw=1.2, ls="--")
    ax.axvspan(xmin - 90, xmin + 90, color=ACCENT2, alpha=0.12)
    y_min = c + a  # actual HFR at the fitted minimum
    ax.annotate(
        r"$\sigma_{\mathrm{focus}}$: standard error" + "\nof the fitted minimum",
        xy=(xmin, y_min), xytext=(xmin + 170, y_min + 2.6), fontsize=9,
        arrowprops=dict(arrowstyle="->", color=ACCENT2),
    )
    ax.set_xlabel("focuser position (steps)")
    ax.set_ylabel("HFR (px)")
    ax.set_title("Autofocus V-curve → best focus + σ_focus")
    ax.legend(frameon=False, fontsize=9)
    return save_plot(fig, "af-vcurve", out_dir)


def fig_objective_sfocus(out_dir):
    rho = np.linspace(0, 1.2, 400)
    for_ref = 0.25
    s = 1.0 / (1.0 + (rho / for_ref) ** 2)
    fig, ax = plt.subplots(figsize=(6.4, 4.0))
    ax.plot(rho, s, color=ACCENT, lw=2)
    ax.axvline(for_ref, color=WARN, ls="--", lw=1, label=r"$\rho_{\mathrm{ref}}=0.25$")
    ax.set_xlabel(r"$\rho = \sigma_{\mathrm{focus}} / \mathrm{stepSize}$")
    ax.set_ylabel(r"$S_{\mathrm{focus}}$")
    ax.set_title(r"$S_{\mathrm{focus}} = 1 / (1 + (\rho/\rho_{\mathrm{ref}})^2)$")
    ax.legend(frameon=False, fontsize=9)
    return save_plot(fig, "objective-sfocus", out_dir)


def fig_objective_sstars(out_dir):
    n = np.linspace(0, 30, 400)
    n_floor, n_target = 8, 20
    s_min = np.clip(n / n_floor, 0, 1)
    s_med = np.clip(n / n_target, 0, 1)
    fig, ax = plt.subplots(figsize=(6.6, 4.0))
    ax.plot(n, s_min, color=ACCENT, label=r"$\mathrm{clip}(n_{\min}/N_{\mathrm{floor}})$  (w=0.6)")
    ax.plot(n, s_med, color=GOOD, label=r"$\mathrm{clip}(n_{\mathrm{med}}/N_{\mathrm{target}})$  (w=0.4)")
    ax.axvline(n_floor, color=WARN, ls="--", lw=1)
    ax.axvline(n_target, color=WARN, ls=":", lw=1)
    ax.set_xlabel("stars per frame")
    ax.set_ylabel("component score")
    ax.set_title(r"$S_{\mathrm{stars}} = 0.6\,c(n_{\min}/8) + 0.4\,c(n_{\mathrm{med}}/20)$")
    ax.legend(frameon=False, fontsize=8.5, loc="lower right")
    return save_plot(fig, "objective-sstars", out_dir)


def fig_objective_sfit(out_dir):
    chi = np.linspace(0, 6, 400)
    tau = 2.0
    penalty = np.where(chi <= tau, 1.0, tau / np.maximum(chi, 1e-9))
    fig, ax = plt.subplots(figsize=(6.6, 4.0))
    ax.plot(chi, penalty, color=ACCENT, lw=2)
    ax.axvline(tau, color=WARN, ls="--", lw=1, label=r"$\chi^2_\nu$ knee = 2.0")
    ax.fill_between(chi, penalty, where=chi > tau, color=ACCENT2, alpha=0.12)
    ax.set_xlabel(r"reduced $\chi^2_\nu$ of the curve fit")
    ax.set_ylabel("penalty factor")
    ax.set_title(r"$S_{\mathrm{fit}} = \mathrm{clip}(R^2)\cdot\mathrm{penalty}(\chi^2_\nu)$  (only high side penalized)")
    ax.legend(frameon=False, fontsize=9)
    return save_plot(fig, "objective-sfit", out_dir)


def fig_objective_precision_penalty(out_dir):
    frac = np.linspace(0, 1, 400)
    thr, strength, minf = 0.20, 0.5, 0.5
    pen = np.clip(1.0 - strength * np.maximum(0, frac - thr), minf, 1.0)
    fig, ax = plt.subplots(figsize=(6.6, 4.0))
    ax.plot(frac, pen, color=ACCENT, lw=2)
    ax.axvline(thr, color=WARN, ls="--", lw=1, label="threshold = 0.20")
    ax.axhline(minf, color=ACCENT2, ls=":", lw=1, label="floor = 0.50")
    ax.set_xlabel("near-focus relaxation-admitted fraction")
    ax.set_ylabel("multiplicative penalty")
    ax.set_title("Defocus-precision penalty (label-free junk guard)")
    ax.legend(frameon=False, fontsize=9, loc="lower left")
    return save_plot(fig, "objective-precision-penalty", out_dir)


def fig_multirun_blend(out_dir):
    beta = np.linspace(0, 1, 200)
    mean_j, min_j = 0.8, 0.5
    blend = (1 - beta) * mean_j + beta * min_j
    fig, ax = plt.subplots(figsize=(6.4, 4.0))
    ax.plot(beta, blend, color=ACCENT, lw=2)
    ax.axhline(mean_j, color=GOOD, ls="--", lw=1, label="mean J = 0.8")
    ax.axhline(min_j, color=ACCENT2, ls="--", lw=1, label="min J = 0.5")
    ax.axvline(0.5, color=WARN, ls=":", lw=1, label="β = 0.5 (default)")
    ax.set_xlabel(r"$\beta$ (worst-case weight)")
    ax.set_ylabel(r"$J_{\mathrm{total}}$")
    ax.set_title(r"$J_{\mathrm{total}} = (1-\beta)\,\overline{J} + \beta \min J$")
    ax.legend(frameon=False, fontsize=8.5)
    return save_plot(fig, "multirun-blend", out_dir)


def fig_compass_search(out_dir):
    # synthetic objective surface over two axes (Sensitivity, StarClippingMultiplier)
    s = np.linspace(0, 6, 200)
    cmul = np.linspace(0.5, 4, 200)
    S, C = np.meshgrid(s, cmul)
    J = np.exp(-((S - 2.2) ** 2) / 4.0 - ((C - 1.8) ** 2) / 1.2)
    fig, ax = plt.subplots(figsize=(6.8, 4.6))
    cs = ax.contourf(S, C, J, levels=18, cmap="magma")
    fig.colorbar(cs, ax=ax, label="objective J")
    # phase A coarse grid
    gx, gy = np.meshgrid(np.linspace(0.7, 5.3, 4), np.linspace(0.8, 3.6, 4))
    ax.scatter(gx, gy, s=14, color="white", alpha=0.5, marker="s", label="Phase A grid")
    # phase B compass trajectory
    path = [(4.5, 3.2), (3.6, 3.2), (3.6, 2.4), (2.8, 2.4), (2.8, 1.9), (2.3, 1.9), (2.2, 1.8)]
    px, py = zip(*path)
    ax.plot(px, py, "-o", color="cyan", ms=4, lw=1.5, label="Phase B compass")
    ax.scatter([2.2], [1.8], s=120, marker="*", color="white", edgecolor="k", zorder=5, label="optimum")
    ax.set_xlabel("Sensitivity")
    ax.set_ylabel("StarClippingMultiplier")
    ax.set_title("Staged compass / pattern search")
    ax.legend(frameon=False, fontsize=8, loc="upper right", labelcolor="white")
    ax.grid(False)
    return save_plot(fig, "compass-search", out_dir)


def fig_step_size(out_dir):
    # _hyperbola(x) = c + a*sqrt(1 + ((x-xmin)/b)^2), so its minimum (best-focus HFR) is c + a.
    # Use a minimum HFR of 3, and a curve width (b) that puts the 3x-min crossing comfortably in view.
    xmin, a, c, b = 5000.0, 1.5, 1.5, 110.0
    min_hfr = c + a  # 3.0
    x = np.linspace(4200, 5800, 400)
    y = _hyperbola(x, xmin, a, b, c)
    target = 3.0 * min_hfr  # 3 × min HFR == 9
    fig, ax = plt.subplots(figsize=(7.2, 4.2))
    ax.plot(x, y, color=ACCENT, lw=2)
    ax.axhline(min_hfr, color="0.5", ls=":", lw=1, label="min HFR")
    ax.axhline(target, color=WARN, ls="--", lw=1, label="3 × min HFR")
    # find half-width where HFR == 3*min
    right_mask = x > xmin
    right = x[right_mask][np.argmin(np.abs(y[right_mask] - target))]
    hw = right - xmin
    step = hw / 3.5
    for k in range(-3, 4):
        ax.axvline(xmin + k * step, color=GOOD, lw=0.8, alpha=0.6)
    ax.axvspan(xmin - hw, xmin + hw, color=ACCENT2, alpha=0.08)
    ax.set_xlabel("focuser position (steps)")
    ax.set_ylabel("HFR (px)")
    ax.set_title("Step size ≈ half-width / 3.5 → ~3–4 points per side")
    ax.legend(frameon=False, fontsize=9)
    return save_plot(fig, "step-size", out_dir)


# -------- feature figures --------

def fig_tilt_heatmap(out_dir):
    # best-focus offset across the frame: a tilt plane + curvature
    gx, gy = np.meshgrid(np.linspace(-1, 1, 60), np.linspace(-1, 1, 60))
    tilt = 40 * gx + 18 * gy           # planar tilt
    curv = 25 * (gx ** 2 + gy ** 2)    # field curvature
    z = tilt + curv
    fig, ax = plt.subplots(figsize=(5.6, 4.6))
    im = ax.imshow(z, cmap="coolwarm", extent=[-1, 1, -1, 1], origin="lower")
    fig.colorbar(im, ax=ax, label="best-focus offset (µm)")
    ax.set_title("Sensor tilt + curvature map")
    ax.set_xticks([])
    ax.set_yticks([])
    ax.grid(False)
    return _finalize(fig, "tilt-heatmap", out_dir)


def fig_aberration_corners(out_dir):
    shape = (31, 31)
    c = 15
    center = elliptical_gaussian(shape, c, c, 2.2, 2.2)
    tl = elliptical_gaussian(shape, c, c, 4.2, 2.0, theta=np.deg2rad(45))
    tr = elliptical_gaussian(shape, c, c, 4.2, 2.0, theta=np.deg2rad(135))
    bl = elliptical_gaussian(shape, c, c, 3.8, 2.2, theta=np.deg2rad(20))
    br = moffat_psf(shape, c, c, 5.0, 2.0)
    canvas = np.zeros((shape[0] * 3, shape[1] * 3))
    placements = {
        (0, 0): tl, (0, 2): tr, (2, 0): bl, (2, 2): br, (1, 1): center,
    }
    for (ry, rx), arr in placements.items():
        canvas[ry * shape[0] : (ry + 1) * shape[0], rx * shape[1] : (rx + 1) * shape[1]] = arr
    fig, ax = plt.subplots(figsize=(5.0, 5.0))
    ax.imshow(_stretch(canvas), cmap=STAR_CMAP, interpolation="nearest")
    ax.set_title("Corner PSFs: aberrations grow off-axis")
    ax.set_xticks([])
    ax.set_yticks([])
    ax.grid(False)
    return _finalize(fig, "aberration-corners", out_dir)


def fig_annotation_overlay(out_dir):
    rng = np.random.default_rng(99)
    shape = (110, 150)
    img, cats = star_field(shape, 24, rng, max_amp=1.0, sigma_range=(1.5, 2.6))
    img = add_noise(img, sky=0.05, read_noise=0.012, rng=rng)
    fig, ax = plt.subplots(figsize=(7.4, 5.4))
    ax.imshow(_stretch(img), cmap=STAR_CMAP, interpolation="nearest")
    for i, (x0, y0, amp, sigma) in enumerate(cats):
        accepted = amp > 0.25 and sigma < 2.4
        col = GOOD if accepted else ACCENT2
        ax.add_patch(plt.Circle((x0, y0), sigma * 3.5, fill=False, color=col, lw=1.2))
        if accepted and i % 2 == 0:
            ax.text(x0 + 4, y0 - 4, f"{sigma*2.3:.1f}", color=GOOD, fontsize=6)
    ax.set_title("Star annotation overlay (green = accepted, pink = rejected; labels = HFR)")
    ax.set_xticks([])
    ax.set_yticks([])
    ax.grid(False)
    return _finalize(fig, "annotation-overlay", out_dir)


# --------------------------------------------------------------------------------------------------
# Registry + CLI
# --------------------------------------------------------------------------------------------------

FIGURES = {
    "psf-models": fig_psf_models,
    "defocused-donut": fig_defocused_donut,
    "hot-pixel": fig_hot_pixel,
    "saturated-star": fig_saturated_star,
    "eccentric-star": fig_eccentric_star,
    "contamination-annulus": fig_contamination_annulus,
    "noise-reduction": fig_noise_reduction,
    "structure-map": fig_structure_map,
    "hfr-half-flux": fig_hfr_half_flux,
    "gate-distortion": fig_gate_distortion,
    "gate-centering": fig_gate_centering,
    "gate-min-size": fig_gate_min_size,
    "gate-sensitivity": fig_gate_sensitivity,
    "gate-flatness": fig_gate_flatness,
    "noise-clipping": fig_noise_clipping,
    "pixel-sample-size": fig_pixel_sample_size,
    "af-vcurve": fig_af_vcurve,
    "objective-sfocus": fig_objective_sfocus,
    "objective-sstars": fig_objective_sstars,
    "objective-sfit": fig_objective_sfit,
    "objective-precision-penalty": fig_objective_precision_penalty,
    "multirun-blend": fig_multirun_blend,
    "compass-search": fig_compass_search,
    "step-size": fig_step_size,
    "tilt-heatmap": fig_tilt_heatmap,
    "aberration-corners": fig_aberration_corners,
    "annotation-overlay": fig_annotation_overlay,
}


def _structural_diff(path_a, path_b):
    """Tolerant comparison: downscale to 32x32 grayscale and return mean abs difference in [0,1]."""
    from PIL import Image

    def load(p):
        return np.asarray(Image.open(p).convert("L").resize((32, 32))) / 255.0

    return float(np.mean(np.abs(load(path_a) - load(path_b))))


def run_check(committed_dir):
    tol = 0.05
    missing, drifted = [], []
    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        for name, builder in FIGURES.items():
            fresh = builder(tmp_dir)
            committed = committed_dir / f"{name}.png"
            if not committed.exists():
                missing.append(name)
                continue
            d = _structural_diff(fresh, committed)
            if d > tol:
                drifted.append((name, d))
    ok = not missing and not drifted
    if missing:
        print(f"MISSING committed figures: {', '.join(missing)}")
    if drifted:
        print("DRIFTED figures (structural diff > %.3f):" % tol)
        for name, d in drifted:
            print(f"  {name}: {d:.4f}")
    if ok:
        print(f"OK: all {len(FIGURES)} figures match committed PNGs.")
    return 0 if ok else 1


def main(argv=None):
    ap = argparse.ArgumentParser(description="Generate Hocus Focus documentation figures.")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT, help="output directory")
    ap.add_argument("--only", nargs="*", help="only build these named figures")
    ap.add_argument("--check", action="store_true", help="verify committed PNGs are up to date")
    args = ap.parse_args(argv)

    if args.check:
        return run_check(args.out)

    names = args.only or list(FIGURES)
    unknown = [n for n in names if n not in FIGURES]
    if unknown:
        ap.error(f"unknown figure(s): {', '.join(unknown)}")
    for name in names:
        path = FIGURES[name](args.out)
        print(f"wrote {path}")
    print(f"done: {len(names)} figure(s) -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
