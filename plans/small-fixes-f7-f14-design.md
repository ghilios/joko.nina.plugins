# Small Fixes (F7–F14 + Weight-Chain Follow-ups) — Design

Step 5 of `plans/star-detection-hfr-autofocus-accuracy-analysis.md` (§8 findings F7–F14, §10 row 5),
plus the four follow-ups recorded in the Risks/notes section of `plans/weight-chain-hygiene-design.md`
(SolveHuberIrls failed-solve bug, uncentered-MAD Huber threshold, SensorModel per-star fits not
regularized, structural IRLS reference-fit change).

Branch: `ghilios/step5-small-fixes` → single PR to `develop`. The PR carries everything in scope
except two deferrals discovered/decided along the way: F11 (meanFlux), pulled during execution after
it measured an 8.1% star-count drop → roadmap step 6 (with a sensitivity recalibration); and the
structural IRLS change (W4) → roadmap step 7.

## Decisions taken (brainstorm 2026-06-12)

| Item | Decision |
|---|---|
| F7 — TRENDHYPERBOLIC 50/50 averaging | **Won't fix** (rarely used). Recorded in roadmap notes; no code change. |
| F8 — brightest-N score units + unbounded/duplicate position matching | **Won't fix**, keep as-is (only active when NINA's "use brightest N stars" > 0). Recorded; no code change. |
| F9 — `AddOffset` drops fields | Mechanical fix, in batch. |
| F10 — `ResetDefaults` inconsistencies | Mechanical fix, in batch. |
| F11 — `meanFlux` denominator mismatch | ~~Mechanical fix, in batch~~ → **deferred during execution to its own step (roadmap step 6)**. The fix is correct but the TestApp sanity check measured an 8.1% accepted-star drop on the corpus image (1970→1810), far above the 2% gate — the BrightnessSensitivity knob was tuned against the inflated `NormalizedBrightness`, so it needs a recalibration pass (mirrors the F4 σ-consistency precedent). |
| F12 — parabolic Grubbs unweighted | Mechanical fix, in batch; weights unconditional (see below). |
| F13 — kappa-sigma first iteration unmasked | Fix (mask zeros from iteration 0); keep the 5-iteration cap. |
| F14 — saturated pixels unmasked in HFR | **Document-only** (XML doc on `MeasureStar`); no correction attempted. |
| W1 — `SolveHuberIrls` failed-solve bug | Mechanical fix, in batch. |
| W2 — uncentered-MAD Huber threshold | Mechanical fix, in batch (median-center the comparison). |
| W3 — SensorModel per-star fits not regularized | Mechanical fix, in batch (route through `WeightRegularization`). |
| W4 — structural IRLS reference-fit change | **Own step** — new roadmap step 7. Changes every weighted Huber fit and needs FitQualityRunner corpus validation; exactly the "big or risky" category excluded from this batch. |

## Code changes

### F9 — complete `Star.AddOffset` (CvImageUtility.cs:561-570)

`AddOffset` rebuilds a `Star` for ROI→full-image coordinate translation but copies only
Center/StarBoundingBox/Background/MeanBrightness/HFR/PSF. Three fields are silently dropped for
every ROI detection (the common AF inner-crop path): `PeakBrightness` (→ `MaxBrightness = 0` in
results), `StarContaminationSuspected` (flags wiped), and `BackgroundPlane` (null downstream).

Fix: copy `PeakBrightness` and `StarContaminationSuspected`; translate `BackgroundPlane` by
constructing a new `LocalBackgroundPlane(OriginX + xOffset, OriginY + yOffset, B0, B1, B2, IsFlat)`
(null-safe — a Star built without a plane stays plane-less). The plane is anchored at the star's
ROI-space center, so translating the origin preserves `ValueAt`: the translated plane evaluated at
the translated point equals the original plane at the original point.

`StarDetectorMetrics.AddROIOffset` already translates metric bounds; no metrics change needed.

### F10 — `ResetDefaults` consistency (StarDetectionOptions.cs:203,215)

Two defects:

1. Line 203 writes the backing field `simple_FocusRange` directly — no persist, no
   `RaisePropertyChanged`, no `ConfigureSimpleSettings` retrigger. A user on WideRange who resets
   gets an in-memory Typical that the UI doesn't show and the profile doesn't store.
   Fix: assign the `Simple_FocusRange` property instead.
2. Line 215 sets `StarPeakResponse = 0.6`, but both the load default (line 168) and
   `ConfigureSimpleSettings` (line 124) use 0.75 — verified to be the *only* value mismatch
   between `ResetDefaults` and the simple-mode computation. Fix: 0.6 → 0.75.

### F11 — `meanFlux` denominator (StarDetector.cs:1199)

`var meanFlux = totalFlux / starPoints.Count;` — the numerator sums only clip-surviving pixels
(those above `background + clipMargin`), the denominator counts all structure pixels. The
understated mean inflates `NormalizedBrightness = peak − (1 − PeakResponse)·meanFlux`, loosening
the sensitivity gate inconsistently (most for faint stars with many clipped skirt pixels).

Fix: divide by the clip-survivor count (`numUnclippedPixels`, equal to `starPixels.Length`).
Behavioral effect: `NormalizedBrightness` drops slightly for stars with clipped pixels → the
sensitivity gate becomes honestly (slightly) stricter for marginal faint stars. Expected magnitude
is small (the subtracted term is scaled by `1 − 0.75 = 0.25`); validated with a TestApp
before/after star-count comparison on a real image (expected delta ≈ 0).

### F12 — weighted parabolic Grubbs (AutoFocusEngine.cs:127)

The parabolic rejection test passes no weights while the hyperbolic one (line 136) passes
`BuildResidualWeights(validFocusPoints, WeightedHyperbolicFitEnabled)`. NINA core's
`QuadraticFitting` always weights by 1/ErrorY² (verified against NINA source during the
weight-chain design), so its Grubbs test should judge residuals the way the fit weighted them —
unconditionally.

Fix: pass `weights: AlglibHyperbolicFitting.BuildResidualWeights(validFocusPoints, true)` to the
quadratic `RejectionTest`. The `true` is deliberate and differs from the hyperbolic call site:
`WeightedHyperbolicFitEnabled` gates *our* fitters' weighting, not NINA core's, which has no
unweighted mode.

### F13 — kappa-sigma zero masking (CvImageUtility.cs:516-521)

`KappaSigmaNoiseEstimate` masks pixels outside `[ε, threshold)` from iteration 1 onward, but
iteration 0 computes mean/σ over the whole image — including exact-zero pixels (calibrated/stacked
frames with zeroed borders), which bias the initial mean low and σ high, distorting the first
clipping threshold.

Fix: apply the ≥ε exclusion on iteration 0 as well (an `InRange(image, float.Epsilon,
float.MaxValue)` mask for the first pass). The 5-iteration cap and the 1e-5 σ-convergence check
are unchanged — convergence typically lands in 2–3 iterations. Exact no-op for images with no
zero pixels (the common case), so no recalibration of σ-derived knobs is needed.

### W1 — `SolveHuberIrls` last-good solution (AlglibHyperbolicFitting.cs:431-467)

The IRLS loop's `SolveOnce(guess, …, out solution)` writes directly into the method's `out`
parameter. When a mid-loop reweighted solve fails, the code `return iter > 0` intends to "keep the
previous good solution" — but `solution` has already been overwritten with the failed solve's
parameters (or null if the optimizer threw before producing results). The caller then builds
`Expression`/`Fitting`/`Minimum` from the failed parameters, silently.

Fix: keep a `lastGoodSolution` local updated after each successful `SolveOnce`; on a mid-loop
failure, restore `solution = lastGoodSolution` before returning true. First-iteration failure
still returns false (unchanged). Unexercised today (mid-loop alglib failures are rare), but the
contract now matches the comment.

### W2 — centered Huber residuals (AlglibHyperbolicFitting.cs:450-457)

The Huber threshold δ is built from the *median-centered* MAD of the residuals
(`residuals.MedianMAD()`), but the comparison uses uncentered `|rᵢ|`. With a systematic residual
offset m ≠ 0, bulk points near m can be down-weighted while outliers on the other side are
under-penalized.

Fix: use `|rᵢ − median(r)|` in the Huber factor (`MedianMAD` already returns the median; use both
outputs). δ = `HuberSigmaMultiplier · mad` is unchanged. Negligible change when residuals center
near zero — the usual case for a converged LM fit.

### W3 — SensorModel per-star weight regularization (SensorModel.cs:664)

The Inspection module's per-star weighted hyperbolic fits build their points from
`EstimateHfrStdDev` (HFR/max(SNR, 1), floored at 1e-3) without routing through
`WeightRegularization` — the same structural hazard fixed for the AF path in step 4: a high-SNR
frame hitting the 1e-3 floor gets ~1000× weight within one star's sweep fit.

Fix: wrap the point-list construction in `WeightRegularization.Regularize(...)`. Same 5× weight
cap (0.2·median floor) as the AF path. The downstream rejection loop and `SelectBestModel` already
operate on the list they're handed, so no further changes. Per-star fits where σ never approaches
the floor see relative-weight changes only if some σ < 0.2·median — the cap working as designed.

## Non-code outcomes

- **F14 (document-only)**: add an XML-doc remark on `StarDetector.MeasureStar` stating that
  saturated cores are *not* masked (unlike the PSF fit): a flat saturated top under-weights the
  core and biases HFR high, most likely near focus; no correction is attempted because masking
  core pixels from an empirical flux sum would bias HFR further, and median aggregation across
  stars limits the damage. The `Saturated` metric already tracks exposure to this.
- **F7 / F8 (won't fix)**: recorded in the roadmap row-5 notes (F7: rarely used; F8: only active
  with "use brightest N stars" > 0, kept as-is by decision).
- **F11 (deferred during execution)**: new roadmap step 6 — apply the meanFlux denominator fix plus
  a BrightnessSensitivity recalibration per preset (the knob was tuned against the inflated
  `NormalizedBrightness`); the fix alone dropped accepted stars 8.1% on the corpus image.
- **W4 (deferred)**: new roadmap step 7 — "Structural IRLS robustness: judge Huber residuals
  against an unweighted reference fit so a high-weight displaced point cannot self-mask." Links to
  the investigation notes in `plans/weight-chain-hygiene-design.md` (§1 implementation finding:
  recovery cliff at capped weight ratio ≥ ~1.25×; fit-level damage saturates regardless of cap).
  Requires FitQualityRunner corpus validation and synthetic-sweep experiments before any change.
- **Roadmap §10**: row 5 → in progress (this design + plan), with the F7/F8/F14 decisions noted;
  add step 6 row (F11 + sensitivity recalibration) and step 7 row (W4), both ⬜ Not started.

## Testing

Unit (NUnit, Tests project):

- **F9**: a `Star` with every field populated → `AddOffset(dx, dy)` → all fields carried;
  `plane.ValueAt(x + dx, y + dy)` after offset equals original `ValueAt(x, y)`; null
  `BackgroundPlane` stays null.
- **F10**: from a modified options state, `ResetDefaults()` → `Simple_FocusRange` persisted and
  notified (observe `PropertyChanged`), `StarPeakResponse == 0.75`.
- **F13**: synthetic noise image with a zeroed border → σ within tolerance of the zero-free
  ground truth; zero-free image → identical result to the previous implementation.
- **W1**: mocked `IAlglibAPI` where the second `minlm` solve fails → `Solve()` returns true and
  the fit equals the first (good) solution, not the failed one.
- **W2**: existing Huber-IRLS fitting tests stay green; add one case with an asymmetric outlier
  pattern pinning the centered comparison.
- **W3**: `WeightRegularization` itself is already tested; add a SensorModel-level test only if a
  cheap seam exists (otherwise covered by the existing repeatability tests staying green).
- **F11/F12**: covered by existing detection/fitting suites staying green; F11 additionally gets
  a TestApp before/after star-count sanity check on a real image.

Full suite green (`rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`)
before completion.

## Risks / notes

- **F11** slightly tightens the sensitivity gate for faint stars with clipped skirt pixels. The
  term is scaled by 0.25 (PeakResponse 0.75), so shifts were expected to be small; the TestApp check
  guards against surprises. **Outcome (execution):** the check measured a material −8.1% accepted-star
  drop on the corpus image, so F11 was pulled from this batch and given its own calibration step
  (roadmap step 6) — exactly the "stop and reassess … own calibration pass like F4 got" contingency
  this note anticipated.
- **F13** changes σ only for images containing exact-zero pixels; σ-derived knobs recalibrated in
  PR #48 are unaffected for normal frames.
- **W2** changes robust weighting only when the residual median is materially non-zero —
  fits in that regime were already degraded; the centered comparison is strictly more consistent.
- **W3** changes sensor-model results only where a per-star σ sat below 0.2× that star's median σ
  (the degenerate path being fixed). Healthy stars see identical weights.
- **F12** makes quadratic Grubbs strictly consistent with the always-weighted quadratic fit; users
  on PARABOLIC/TRENDPARABOLIC may see different points rejected when error bars vary strongly
  across the sweep — that is the correction, not a side effect.
- No change touches the AF position computation for default settings (HYPERBOLIC/Hybrid path
  weights, τ policy, gates all untouched).
