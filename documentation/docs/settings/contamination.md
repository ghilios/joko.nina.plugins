# Contamination Rejection

A faint companion star, a hot column, or a steep nebula edge sitting next to a real star
quietly inflates that star's measured background, Half-Flux Radius, and PSF shape. Hocus
Focus runs a **gradient-robust contamination test** on every candidate to catch this: it
models the smooth local background, then flags a star only when extra light leaks in from
**one side** of its background annulus. These two settings control how aggressively that
test fires and whether a flagged star is thrown out or merely marked.

## Settings at a glance

| Setting | Default | Range | Effect |
|---|---|---|---|
| Contamination Sensitivity | 5.0 σ | 0 – 20 (0 disables) | Required one-sided asymmetry, in multiples of the per-star background scatter, before a star is flagged. Higher = fewer flags. |
| Reject Contaminated Stars | On | On / Off | On removes flagged stars from the result; Off keeps them and only marks them. |

These are Advanced-mode options. In Simple mode the preset derivation never overrides them,
so they keep whatever value you (or a reset) last set — i.e. the defaults above.

## How the gradient-robust test works

For each candidate the detector samples the pixels in a ring (the **background annulus**)
around the star and fits a robust background **plane** \( b_0 + b_1\,dx + b_2\,dy \) to them
by iteratively reweighted least squares (Huber weighting). That plane is the key idea: a
smooth one-sided gradient — the limb of a galaxy, a nebula edge, vignetting — is absorbed
into \(b_1\) and \(b_2\) and **subtracted away**, so it cannot masquerade as contamination.

The fitted plane is then evaluated per pixel and doubles as the **local background** used for
the star's centroid, flux, HFR, and PSF, so a gradient no longer biases any measurement. On a
flat field the plane simply equals the annulus level and nothing changes.

After subtracting the plane, the residuals are binned into 8 octants around the star. The
detector computes the median residual in each octant and its standard error
\( \mathrm{se} = 1.2533\,\sigma_{\text{local}} / \sqrt{N} \), where \(\sigma_{\text{local}}\)
is the robust scatter of the residuals measured from that star's own annulus. A star is
flagged when **any single octant** shows a one-sided **positive** excess:

\[
\mathrm{median}_{\text{octant}} \;>\; \text{ContaminationSensitivity} \times \mathrm{se}
\]

Two design choices make this specific to real contaminants. The test is **one-sided and
positive** — a contaminant only *adds* light — so an edge-clip *deficit* on one side never
trips it. And it acts **per octant**, so the localized glow of a neighbor is caught while the
smooth, all-around gradient (already removed by the plane) is ignored. An octant needs at
least 8 residual pixels to be considered.

![Star with a background annulus that is brighter on one side because of a nearby neighbor](../assets/figures/contamination-annulus.png){ width=620 }
*One octant of the background annulus is brighter than the robust plane predicts — the
signature of a nearby star. The plane fit removes any smooth gradient first, so only this
localized, one-sided excess remains to trip the test.*

!!! note "What a flagged star means"

    Flagging records the star's bounding box (so the annotator can still draw it) and sets a
    "contamination suspected" marker. Whether the star is then dropped or kept is decided
    entirely by **Reject Contaminated Stars** below.

## Contamination Sensitivity

Sets how large the one-sided asymmetry must be, in sigma, before a star is flagged.

> How aggressively to flag stars whose background annulus is brighter on one side — a sign of
> a nearby star, hot column, or steep gradient. Value is the required asymmetry in multiples
> of the local background scatter (measured per star from the annulus itself); higher = fewer
> flags; 0 disables. Default 5.

**Default:** 5.0 σ &nbsp;•&nbsp; **Range:** 0 to 20 (UI field is in σ; values below 0 are
clamped to 0, and 0 disables the test entirely)

Because the threshold is in multiples of each star's *own* annulus scatter, it adapts
automatically to bright and faint stars and to noisy versus clean frames — the σ is measured
per star, not assumed globally.

!!! tip "When to adjust"

    - **Lower it (e.g. 3–4)** when you image dense fields, clusters, or galaxy/nebula regions
      where close pairs are common and you want HFR and PSF statistics scrubbed of every
      contaminated star. More stars get flagged — watch the **Contaminated** rejection count in
      the [Star Detection Results panel](index.md#reading-the-results-the-star-detection-results-panel)
      climb as you lower it (or, with rejection off, the contaminated annotation count).
    - **Leave it at 5** for typical wide-to-medium fields. This is the validated default and
      balances rejecting genuine contaminants against keeping good stars.
    - **Raise it (e.g. 8–12)** in sparse fields if you find the test is flagging real,
      isolated stars and you want only the most blatant contaminants caught.
    - **Set it to 0** to switch the flagging decision off completely. The plane is still fit
      and still used as the local background, so measurements stay gradient-corrected — you
      only lose the contamination flag itself.
    - **Can hurt when** set too low on star-poor frames: over-flagging combined with
      rejection (below) can thin out your usable star list and weaken the focus measurement.

## Reject Contaminated Stars

Decides whether a flagged star is removed from the result or kept and only marked.

> When enabled (default), stars flagged as contaminated are rejected outright so their
> one-sided contamination does not skew HFR and PSF statistics. When disabled, contaminated
> stars are kept and only flagged. A smooth one-sided background gradient (galaxy/nebula) is
> removed before the test, so only genuine localized contaminants trip it.

**Default:** On &nbsp;•&nbsp; **Range:** On / Off

When **on**, a flagged star is dropped via the `Contaminated` rejection gate and never enters
the HFR/PSF aggregation. When **off**, the same star stays in the detected set carrying its
"contamination suspected" marker — useful for inspection and for the labeling/diagnostic
tools, which keep flagged stars so they can be analyzed.

!!! tip "When to adjust"

    - **Leave it on** for normal autofocus and HFR work. Removing contaminated stars keeps
      the focus curve and PSF statistics clean, which is the whole point of the test; with
      reject on, the count appears under **Contaminated** in the
      [Star Detection Results panel](index.md#reading-the-results-the-star-detection-results-panel),
      whereas with reject off the same stars stay accepted and show only the flagged/contaminated
      annotation.
    - **Turn it off** when you want to *see* which stars are suspect rather than lose them —
      for example while diagnosing why a frame is short on stars, or when feeding frames to
      the review/diagnostic tooling. It is also the safe choice in very sparse fields where
      you cannot afford to drop borderline stars.
    - **Can hurt when** left on together with a low **Contamination Sensitivity** in a
      star-poor frame: the two compound, and you may reject enough stars to undermine the fit.
      If that happens, raise the sensitivity or switch to flag-only first.

!!! warning "Not a substitute for clean candidate selection"

    This test runs on stars that already passed the other acceptance gates; it specifically
    targets *one-sided* background excess. Broad problems — saturation, distortion, hot
    pixels — are handled by their own settings, not here.
