# Adaptive NoiseClippingMultiplier — design

## Why

The AF-bank NC sweep (`docs/af-bank-noiseclip-sweep-results.md`) established three things:

1. Recall@SNR≥12 rises monotonically as the global `NoiseClippingMultiplier` (NC) falls — lower NC admits more
   real faint structure during candidate formation.
2. The shipped default **NC = 2** is well-centered: the free per-run optimizer (which optimizes σ_focus) converges
   to a **median of exactly 2.0**, and where it deviates it goes *lower* (1.25–1.7 on the densest/noisiest runs),
   never toward 4.
3. Recall is still rising at NC = 2, so a single global default likely leaves recall on the table on the cleanest
   frames while a fixed low NC risks admitting noise on the noisiest frames.

The natural next step the data points to is **not** a different global constant — it is making the threshold that
NC scales **adapt to the live data**. This document specifies that.

## Root cause: the threshold is a single global scalar

Candidate formation binarizes the (wavelet-residual, blurred) structure map at one scalar threshold
(`StarDetector.cs`, the binarization step):

```csharp
double binarizeThreshold = structureMapStats.Median           // GLOBAL median of the whole structure map
                         + p.NoiseClippingMultiplier           // NC (the knob)
                         * noiseReducedImageNoise.Sigma;        // GLOBAL kappa-sigma noise estimate (whole frame)
```

`structureMapStats.Median` and `noiseReducedImageNoise.Sigma` (`CvImageUtility.KappaSigmaNoiseEstimate`) are both
**whole-frame** statistics, so a **single threshold is applied uniformly** across the image. Real AF frames are
spatially non-uniform — vignetting, sky gradients, amp glow, and field-edge falloff make the local background and
local noise vary by 2–4× across the frame. A global threshold is therefore:

- **too high in the clean center** → real faint stars there fall below it → lost recall (lower NC compensates
  globally, but then…)
- **too low in the noisy corners/glow** → noise pixels there clear it → false candidates → lost precision.

Lowering NC globally trades one for the other; it cannot fix both because the *shape* of the threshold is wrong,
not just its level. Tellingly, the detector-independent reference detector `tools/golden/snr_ref.py` — which finds
the recall gaps HF misses — already uses **local** statistics (`coarse_bg`: per-block median + per-block
MAD·1.4826 on a 128-cell grid, then `(img − bg_local) > k·σ_local`). HF's global threshold is the gap.

## Proposed design: spatially-adaptive binarization (NC stays the knob)

Replace the scalar threshold with a **spatially-varying threshold surface** computed from robust local statistics,
with NC unchanged as the multiplier:

```
threshold(x,y) = median_local(x,y) + NC · sigma_local(x,y)
```

where `median_local` and `sigma_local` are estimated on a coarse grid over the structure-map source and bilinearly
upsampled to full resolution. The same NC = 2 then yields a *locally fair* threshold everywhere: low where the
background is clean (recovering faint reals), high where it is noisy (rejecting noise). This is the standard
adaptive-thresholding idea, matched to the reference detector that defines our recall ground truth.

### Algorithm

1. **Block statistics on a coarse grid.** Partition the structure-map source into a grid of `G×G`-pixel blocks
   (default block ≈ 128 px, matching `snr_ref.coarse_bg`; configurable). Per block compute:
   - `median_block` = block median (robust background level), and
   - `sigma_block` = `1.4826 · MAD` of the block (robust noise), floored at a small epsilon.
   Use the **same source image** the current global estimate uses (the noise-reduced structure-map source, honoring
   the existing F4 σ-consistency note — local σ must come from the image actually sampled).
2. **Upsample to a threshold surface.** Bilinearly upsample `median_block` and `sigma_block` to full frame
   resolution → `median_local(x,y)`, `sigma_local(x,y)`. Edge blocks pad by replication (as `snr_ref` does).
3. **Binarize against the surface.** `structureMap(x,y) > median_local(x,y) + NC · sigma_local(x,y)`. Everything
   downstream (dilation, flood-fill, candidate gates) is unchanged.

This is O(N) over the frame plus a tiny grid reduction — comparable to the existing single kappa-sigma pass, and
the block reduction parallelizes trivially.

### Knobs (options system)

- **`LocallyAdaptiveBinarization`** — `bool`. Default **OFF during development** (so the spatial path is never
  entered and detection stays **byte-for-byte identical** for the A/B comparison + the bit-identical regression
  test), then flipped to **default ON after validation** (see the Rollout gate in the Validation plan). This is
  **not** a permanent opt-in flag like `DefocusAwareDonutDetection` / `DefocusAwareGates`: the intended end state
  is that locally-adaptive binarization is the standard behavior. The boolean is retained only as a user escape
  hatch and to keep the bit-identical-vs-legacy regression test runnable; its **default** becomes ON, or the
  feature is not shipped at all (no permanent default-OFF middle ground).
- **`AdaptiveNoiseBlockSize`** — `int`, default 128 px (Advanced; `UnitTextBox` + `IntegerRangeRule`). Larger =
  smoother surface (less local adaptivity, cheaper); smaller = more adaptive but noisier surface and risks tracking
  real extended structure into the background. 64–256 is the sane range.
- NC itself is **unchanged** (default 2) and remains both a user option and a curated optimizer variable. The
  adaptive surface makes NC locally fair; it does not replace NC.

Per the project invariants: every new persisted option gets a control in `Resources/OptionsDataTemplates.xaml`,
and both new `StarDetectorParams` fields flow through `BuildStarDetectorParams` /
`BuildDefaultStarDetectorParams` (and the TestApp overlay in `bank-verify` / `golden eval` / `inspect-align`).

## Alternatives considered

- **Per-frame NC auto-scaling** (scale NC by a global noise-quality metric, e.g. structured-vs-white σ ratio).
  Rejected as the *primary* mechanism: the optimizer already shows the *global* sweet spot is ~2 on essentially
  every run, so per-frame scaling chases a small residual while leaving the real defect (spatial non-uniformity)
  unaddressed. Could be layered on later as a second-order trim; not worth the added surface now.
- **Per-region NC** (different NC per inspector region). Rejected: regions are an aberration-inspection construct,
  too coarse for vignette/glow gradients, and would couple detection to the inspector. The block grid is finer and
  detector-local.
- **Learned noise→threshold mapping.** Over-engineered for a problem the robust block statistics already solve, and
  it would need labeled training data the golden set can't cheaply provide bank-wide.
- **Just lower the global default below 2.** Rejected by the data: the optimizer does *not* want a lower global
  value on clean runs (it sits at 2.0), and a lower global constant worsens precision on the noisy corners — the
  exact failure the spatial surface fixes.

## Validation plan

Reuses the harness this design came from:

1. Add `LocallyAdaptiveBinarization` + `AdaptiveNoiseBlockSize`; confirm **bit-identical** detection when off
   (unit test: a frame detected with the flag off equals the pre-change result; this is the same guarantee the
   donut-master flag has).
2. Re-run `bank-verify --nc-sweep 2,3,4` with the flag ON vs OFF on the AF bank. Expected at fixed **NC = 2**:
   recall@SNR≥12 **up further** (faint reals recovered in clean regions) with precision **held or improved**
   (noise rejected in noisy regions) — i.e. the spatial surface beats *any* global NC because it improves both
   axes at once. Vignetted / gradient-heavy frames (wide-field refractors, fast Newtonians) should show the
   largest gains.
3. Compare HF's accepted set against `snr_ref.py` (which already uses local stats): the adaptive path should close
   the recall gap that motivated the whole audit, especially toward the field edges.
4. Confirm AF σ_focus does not regress (more real faint stars should tighten, not loosen, the HFR curve), and that
   the optimizer's curated NC search still behaves (NC should now want to sit *at* 2 even more tightly).
5. **Rollout gate — flip the default to ON, or drop the feature.** This functionality is meant to land **on by
   default or not at all** — there is no permanent opt-in state. After steps 1–4, decide:
   - **Ship (default ON)** iff, at fixed NC = 2 on the AF bank, the flag ON **improves recall@SNR≥12 and does not
     regress precision** (ideally improves both), with **no AF σ_focus regression** and **no donut-recall
     regression** (Panos / mufti verified explicitly). On a pass, flip the default to ON in all the seams that
     define the shipped default — `BuildDefaultStarDetectorParams`, the `StarDetectionOptions` default +
     `ResetDefaults`, and the `OptionsDataTemplates.xaml` initial value — so a fresh profile gets adaptive
     binarization out of the box. Keep the boolean as an off-switch + for the bit-identical regression test, but
     its default is now ON. Update `docs/af-bank-noiseclip-sweep-results.md` with the on-vs-off bank numbers.
   - **Do not ship** if it fails to clearly improve the bank (e.g. it only trades recall for precision, or it
     regresses donuts / AF σ): remove the option rather than leave a permanent default-OFF flag. The opt-in/default-
     OFF state in steps 1–4 exists **only** to make this go/no-go comparison clean — it is not a shipping outcome.

   **Outcome (2026-06-24): PASSED — shipped ON.** OFF-vs-ON at NC=2 across the 17-run bank improved bank-median
   recall@SNR≥12 (0.870→0.877), precision (0.585→0.618), and AF σ_focus (10.26→8.84); cwhite_2026 recall 0.459→0.862;
   donut runs no regression (Panos flat, mufti +26%). Default flipped ON (commit `c59a4b1`, PR #98); full numbers +
   per-run table in `docs/af-bank-noiseclip-sweep-results.md`.

## Risks & notes

- **Tracking real extended structure into the background.** A too-small block could absorb a large defocused
  donut or a bright bloom into `median_local`, raising the local threshold and *hurting* donut recall. Mitigation:
  the wavelet-residual + blur already run *before* binarization (they remove large-scale structure), so the block
  statistics see a flattened map; keep the default block ≥ 128 px; and note the interaction with
  `DefocusAwareDonutDetection` (validate donut runs explicitly — Panos/mufti — under the flag).
- **Performance.** One extra coarse reduction + an upsample per frame. Negligible vs the wavelet/PSF stages, and
  it sits in the cacheable EARLY detection context (it changes the candidate set, so it is an EARLY param — same
  bucket as NC), so the optimizer's split-frame cache still applies.
- **Determinism.** Block statistics are deterministic; the upsample is fixed bilinear. No new nondeterminism.

## TL;DR

Keep NC = 2 (the data endorses it). Make the threshold NC scales **spatially adaptive** — robust per-block
local median + NC·local-σ, upsampled — matching the reference detector that defines our recall target. Build it
behind a flag that is **default-OFF only during validation** (for the bit-identical A/B comparison); if it lifts
recall *and* precision at NC = 2 on the AF bank with no AF/donut regression, **flip the default to ON** so it is
the standard behavior. It lands **on by default or not at all** — no permanent opt-in flag.
