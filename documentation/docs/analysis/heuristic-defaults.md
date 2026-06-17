# Heuristic Defaults Analysis

Most star-detection settings are tuned **empirically** today — either by hand, by the Simple-mode presets, or
by the [Optimization Wizard](../optimization/index.md). A natural question is: how many of them could instead
be **derived** from things we already know about your rig — the pixel scale, the typical star size (FWHM), the
measured background noise, the sensor's full well, and the local star density?

This page is a plain-language summary of that analysis. It is **analysis and recommendation only** — no
detection behavior changes as a result of it. The full derivation, with proposed formulas, lives in the
repository's internal design note `docs/star-detection-heuristic-defaults-analysis.md`.

!!! note "The plugin already does some of this"
    Simple mode is exactly a heuristic layer: the **Pixel Scale** preset already nudges Structure Layers,
    Minimum Bounding Box Size, and Pixel Sample Size, and the **Noise Level** preset sets the Noise Reduction
    Radius. The analysis below is about *where that idea could go further* — replacing or seeding more of the
    empirical defaults with rig-derived values.

## Which settings could be auto-derived?

| Setting | Could be derived from | Why it works (or doesn't) |
|---|---|---|
| **Structure Layers** | Pixel scale + seeing FWHM | Structures larger than ~\(2^L\) px are removed; pick \(L\) so \(2^L\) is a few × the expected star size in pixels. Star size in px ≈ FWHM(arcsec) / pixelScale(arcsec/px) — both known. |
| **Min Bounding Box Size** | Pixel scale + FWHM | A real star spans a few × FWHM in pixels; the floor can scale with that instead of a fixed 5 px. |
| **Pixel Sample Size** | Sampling regime (FWHM in px) | Undersampled rigs (FWHM ≈ 1–2 px) benefit from sub-pixel sampling; well-sampled rigs do not. This is a direct function of FWHM in pixels. |
| **Noise Reduction Radius** | Measured background σ vs signal | More blur helps when the per-pixel noise is large relative to the stars; the detector already measures the noise. |
| **Hot Pixel Threshold** | Local noise σ (see below) | A noise-relative threshold adapts per sensor and exposure; a fixed full-well fraction does not. |
| **Background Box Expansion** | Star density / crowding | Sparse fields can sample a wider annulus; crowded fields need a tighter one. Density is measurable from the detections themselves. |

The remaining gates are deliberately expressed in **units of the measured noise**, so they tend to be
near-universal and need little per-rig tuning:

> Noise Clipping Multiplier, Star Clipping Multiplier, Brightness Sensitivity, Contamination Sensitivity,
> Saturation Threshold, Max Distortion, Star Center Tolerance, and Star Peak Response are all σ-multiples or
> dimensionless ratios. Their defaults travel well across rigs because they mean the same thing regardless of
> sensor or exposure.

And some settings are genuinely **empirical or a matter of preference** — best left to the wizard or to you:
the Defocus-Aware Gates and their tuning knobs, Measurement Average, the PSF-fitting options, and Minimum HFR.

## Worked example: the hot-pixel threshold

The **Hot Pixel Threshold** is the headline case. Today it is a fraction of the full well: the default
**0.001** means a pixel is treated as hot when it differs from its 3×3 median by more than 0.1 % of full-well
ADU. A tempting heuristic — *"flag a fixed percentage of the pixels"* — sounds adaptive but is actually
risky:

- A **clean, calibrated** frame has essentially **zero** hot pixels. Forcing some fixed fraction of pixels to
  be flagged would corrupt good data on exactly the images that need no correction.
- A fixed full-well fraction is **not adaptive** either: the same ADU gap means very different things at
  different gains, exposures, and sky levels.

The principled form is **noise-relative**: flag a pixel when it stands out from its neighborhood by more than a
few times the *local* noise,

\[
\bigl|\,\text{median}_{3\times3} - \text{pixel}\,\bigr| \;>\; k \cdot \sigma_{\text{local}},
\]

or, equivalently, use a high image quantile gated by an absolute noise floor. The detector already estimates
the noise σ (via its Kappa-Sigma background measurement), so the input is in hand. This decouples the
threshold from full well and exposure and adapts automatically to each sensor — and, crucially, it flags
**nothing** on a clean frame, because nothing stands out from the local noise. See the
[Hot Pixels & Saturation settings](../settings/hotpixel-saturation.md) page for the current control.

## The bottom line

Several settings could be **seeded** heuristically from the pixel scale, FWHM, noise, full well, and star
density, leaving the [Optimization Wizard](../optimization/index.md) to refine them empirically from your
actual autofocus data. The σ-unit gates are already close to universal. A fixed percentage-of-pixels rule for
hot pixels is *not* a safe heuristic, but a noise-relative threshold is — and the plugin already computes the
noise it would need.
