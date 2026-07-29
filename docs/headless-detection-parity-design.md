# Headless Detection Parity (Bayered Runs) — Design

## Problem

`TestApp`'s diagnostic runners and the live app **detect on different images** for bayered runs, so headless
validation does not predict in-app behaviour.

`DiagnosticUtil.LoadFloatMat(path, profileService, debayerToLuminance = false, …)` defaults to **false**, and its
own doc comment states the consequence:

> When `debayerToLuminance` is true and the loaded frame is bayered, it is debayered to a luminance Mat — the SAME
> representation the live app feeds star detection when `ImageSettings.DebayerImage` is on (see
> `StarDetector.PrepareSrcImageFromRenderedImage`). **Headless runners otherwise detect on the raw Bayer mosaic**,
> which biases the per-star sensor-model tilt the Tilt Adapter Wizard calibrates from.

Only **two** of ~14 call sites opt in — `FocusSweepDiagnosticRunner.cs:190` and `TiltCalibrationRunner.cs:711`.
Every other runner detects on the raw mosaic:

`OptimizationDiagnosticRunner.cs:434,436` · `BankVerifyRunner.cs:181` · `GoldenRunner.cs:124` ·
`GoldenEvalRunner.cs:178` · `RecommendRunner.cs:172` · `AfFitDiagnosticRunner.cs:144` ·
`ContaminationDiagnosticRunner.cs:172` · `BankDonutMetaRunner.cs:111` · `DiagnoseLabelsRunner.cs:293` ·
`AnnotateRunner.cs:92` · `ExportLinearRunner.cs:69` · `FocusSweepDiagnosticRunner.cs:235`

On a Bayer mosaic a star's flux is sampled by an R/G/B checkerboard, so the effective PSF, the measured noise σ,
and therefore the Sensitivity gate `(s − b)/n` all differ from the debayered luminance the app detects on.

### Observed impact, and the isolated cause

Measured on `D:\Autofocus Bank\bobp` (bayered, 3 s, 9 frames), same code, same profile, same baseline. Each row
changes exactly one thing:

| Config | Landed Sensitivity | min stars |
|---|---|---|
| Raw mosaic, no CFA filter — **the shipped harness** | **0.0** | 52 |
| Debayered luminance, no CFA filter | **0.0** | 55 |
| **Debayered luminance + CFA hot-pixel filter** | **10** | **29** |
| **In-app wizard** | **10.000** | 27–31 |

**The decisive factor is the CFA hot-pixel filter, not the debayer.** Debayering alone leaves the optimum floored;
adding the CFA filter reproduces the app exactly, including the star-count scale and the fact that the landed
gate is no longer at the search floor. Unfiltered hot pixels present as faint stars, so the harness can drive the
Sensitivity gate to its floor and harvest them — an incentive that does not exist on the filtered image the app
detects on.

**Row 2 is not a live-reachable state, and that is the real lesson.** `ImageControlVM.PrepareImage:609` sets
`saveLumChannel = ImageSettings.DebayeredHFR && detectStars`, and *every* HocusFocus caller passes
`detectStars: false` (`RunEvaluationLoader.cs:268`, `AutoFocusEngine.cs:1241`, `InspectorVM.cs:963`, `:1003`). So
live `SaveLumChannel` is always false and `DebayeredData` is always null, which means
`PrepareSrcImageFromRenderedImage` has exactly two live outcomes:

- `HotpixelFiltering && HotpixelThresholdingEnabled` → CFA filter **and its own debayer** → luminance
- either flag off → `ToOpenCVMat(RawImageData)` → **the raw Bayer mosaic**

The debayer and the CFA filter are the *same branch*; there is no live configuration that debayers without
CFA-filtering. The representation the app detects on is therefore **params-dependent**, not fixed — which is
precisely why any load-time filtering scheme is wrong, and why the harness must let the detector decide.

Debayering is nonetheless a real parity defect in its own right and must also be fixed: it moved the measured
per-star SNR from `S_now = 23.3` to `79.2` and shifted `StarClippingMultiplier` (0.25 → 3.5) and `MinHFR`
(0.95 → 0.89). Both changes are required for parity; only one of them moves the sensitivity landing.

Headless is internally deterministic (`bestJ` reproduces to 16 significant figures), so this is a systematic
representation difference, not search noise.

The probe also demonstrated why filtering at load time is not an acceptable fix: with CFA filtering applied once at
a fixed threshold, the optimizer still searched `HotpixelThreshold` (landing `0.0005 → 0.0015`) on an
already-filtered image — i.e. that axis became a silent no-op.

### Scope

**4 of 22** bank runs are bayered: `SorenVance`, `bobp`, `bobp_m101`, `timmer`. The other 18 are mono and
unaffected (the loader falls through to `ToOpenCVMat` when the frame is not bayered).

One of the four matters disproportionately: **`bobp_m101` is bayered**, and it is the run
`docs/optimizer-sensitivity-pinning-design.md` and `docs/bobp-m101-recall-investigation-results.md` are built on.
That `recall@SNR≥12 = 0.126` audit was measured headless, on a mosaic, so its evidence base is suspect — see
Consequences.

## Goal

Every headless runner detects on the same representation the live app does, so a headless result is predictive of
what a user sees. Mono runs must stay **byte-identical**.

## Approach

### The fidelity requirement

The live app does CFA hot-pixel filtering **inside `Detect`**, per-params: `StarDetector.cs:306-317` takes an
`IDebayeredImage`, and gates the CFA filter on `p.HotpixelFiltering && p.HotpixelThresholdingEnabled` using
`p.HotpixelThreshold`, *then* converts CFA → luminance. Both flags default to **true**
(`BuildDefaultStarDetectorParams`), so the app filters by default — and the harness never did.

That ordering matters for the optimizer, where `HotpixelThreshold` and `HotpixelThresholdingEnabled` are **searched
axes**. Filtering once in the loader would freeze CFA filtering at a single threshold before the search begins, so
those two axes would no longer mean what they mean live — confirmed empirically by the probe, where the optimizer
went on searching `HotpixelThreshold` against an image it could no longer affect.

Note also that the profile setting `ImageSettings.DebayerImage` gates whether the app debayers at all; it is `true`
on both profiles tested here. A faithful harness must honour it rather than hard-coding the behaviour.

### Share the app's path at the detector seam

`RunEvaluationLoader.LoadSavedRunAsync` cannot be adopted wholesale — its `imagingMediator.PrepareImage` is
genuinely unsatisfiable headlessly (`ImageControlVM`'s constructor touches
`Application.Current.Resources` and registers a camera consumer). **But it does not need to be**, because
detection never reads the rendered product: `ToOpenCVMat(IRenderedImage)` (`CvImageUtility.cs:90-98`) reads
`DebayeredData.Lum` or `RawImageData` and never `image.Image`. `DebayeredImage.Stretch` returns the same
`RawImageData`/`BayerPattern`/`SaveLumChannel` and only swaps the `BitmapSource`, so skipping the stretch is
provably detection-identical.

The shareable seam is one layer down:

1. Build a real `IDebayeredImage` with two public NINA calls and no mediator:
   `imageData.RenderImage().Debayer(saveColorChannels: false, saveLumChannel: false, bayerPattern: resolved)`.
   `saveLumChannel` **must** be false — true would flip `ToOpenCVMat`'s guard and make the hotpixel-off branch read
   luminance where live reads the mosaic.
2. **Fixed-params runners** switch to the already-public `StarDetector.Detect(IRenderedImage, …)`
   (`StarDetector.cs:278`), same return type — CFA filter and debayer come for free, at the caller's params.
3. **The optimizer** puts that image in `RunFrame.Image` (already typed `object`) and uses the plugin's own
   `HocusFocusSplitFrameDetector`, promoted from `private` to `internal`. `MatSplitFrameDetector` and
   `HarnessDetection.ToFrameDetectionResult` are then deleted for the optimize/bank-verify paths.

This removes both mirrors and needs exactly one plugin change: an access modifier.

**Mono byte-identity comes free by construction** — a non-bayered frame is not `IDebayeredImage`, so detection
falls to `ToOpenCVMat(image.RawImageData)`, the identical call `DiagnosticUtil` makes today.

### Rejected: the `HotpixelFiltering = false` pre-filter pattern

An earlier draft prescribed `FocusSweepDiagnosticRunner:190`'s pattern (pre-filter at load, then disable the
detector's filter). **It is not faithful.** Live sets `hotpixelFilterAlreadyApplied = true`, so `StarDetector.cs:523`
skips re-filtering and `:544` takes the plain `CopyTo` branch. The pre-filter pattern leaves that flag false, so
with the shipped defaults (`NoiseReductionRadius = 3`, `StarMeasurementNoiseReductionEnabled = false`) `:544` is
false and the `else` at `:549` runs `ApplyHotpixelFilter(noiseReducedImage, p)` — a **spatial** hot-pixel filter on
the structure-detection source that live never applies. Different structure map, different candidates, different
detection, and a clobbered `metrics.HotpixelCount`. The `Detect(Mat, …)` overload cannot fix this either: it
hard-codes `hotpixelFilterAlreadyApplied: false` (`StarDetector.cs:412`).

### Rejected alternatives

- **CFA-filter once and drop the hot-pixel axes from the curated set.** Cheap and honest, but the optimizer
  permanently loses the ability to tune two knobs it currently searches — a real capability regression to fix a
  measurement bug.
- **CFA-filter once and leave the axes in as no-ops.** Rejected outright: the search would burn evaluations on
  knobs that no longer affect the image, and `optimized_settings.json` would record hot-pixel values that mean
  nothing.
- **Make `debayerToLuminance` default to `true`.** Tempting as a one-line fix, but it silently changes every
  caller including ones that may legitimately want the mosaic, and it does not address the CFA-ordering problem at
  all. Opt-in per runner, with the decision documented at each site, is safer.

## Two further defects found while scoping

**1. The wizard's own Review Frames step detects on the raw mosaic — a live-app bug.**
`StarDetectionOptimizerWizardVM.LoadFloatMatFromDisk` (`:4152-4171`) is a *third* copy of the loader, inside the
plugin. It hard-codes `isBayered: false` and `CvImageUtility.ToOpenCVMat(imageData)`, then feeds
`FrameReviewBuilder.BuildAsync` (`:4138`), which detects via `StarDetector.Detect(Mat, …)`
(`FrameReviewBuilder.cs:118`). So for an OSC run the wizard's optimizer step uses CFA-filtered luminance while its
Review step shows and detects the raw Bayer mosaic. This is worse than the harness bug: the labels the user draws
there are the labels the optimizer subsequently trusts, so a mosaic-authored label set is fed to a
luminance-scoring objective.

**2. `ExportLinearRunner` contradicts its own documentation.** Its comment (`:29-34`) states it "debayers Bayered
frames to luminance … the SAME linear luminance the HocusFocus detector consumes", but `:69` uses the
`debayerToLuminance: false` default. The doc is right and the code is wrong. This matters beyond tidiness: the
export feeds `tools/golden/snr_ref.py`, the **detector-independent reference** the golden sets are built from. A
reference computed on a mosaic while HocusFocus detects on luminance scores every mosaic-only find as an HF recall
gap HF could never close — i.e. the golden audit measures the wrong thing. "Linear" means no MTF/auto-stretch, not
no debayer. It must **not** gain the CFA filter, though: the reference's independent blind spots are the point, and
hot-pixel rejection is already assigned to the LLM montage QA step (`.claude/docs/golden-star-set.md:37`).

**3. `TiltCalibrationRunner` was the runner the `DiagnosticUtil` warning was written about.** That warning names
tilt explicitly — detecting on the mosaic "biases the per-star sensor-model tilt the Tilt Adapter Wizard calibrates
from" — and tilt was nonetheless the *last* runner still doing it. Its `--debayer` opt-in did not close the gap
either: it debayered at load time **without** the CFA hot-pixel filter, i.e. row 2 of the table above, the
configuration the probe showed leaves the optimum floored. It also held the last references to
`MatSplitFrameDetector` and `HarnessDetection.ToFrameDetectionResult`, so both mirrors survived only for it.

## Consequences for stored artifacts

Changing the representation changes what every stored number for the four bayered runs — `SorenVance`, `bobp`,
`bobp_m101`, `timmer` — means. **Mono runs are unaffected** (proven by test: a non-bayered frame is not an
`IDebayeredImage`, so detection falls through to the identical `ToOpenCVMat(RawImageData)` call).

Invalidated, and to be regenerated before it is trusted again:

| Artifact | Where | Why |
|---|---|---|
| `<frame>.golden.json` | beside each bayered frame | `GoldenRunner` tiled the mosaic and `snr_ref.py` scored it; per-star `confidence` is an SNR tier, so the tiering itself moves |
| `<frame>.linear.fits` | beside each bayered frame | exported from the mosaic (the `ExportLinearRunner` defect); regenerate with `export-linear --overwrite` **first** — the goldens are built from these |
| `optimized_settings.json` | each run folder + each `--out` dir | landed on a different image; `bobp`'s recorded `Sensitivity = 0` is an artifact of the mosaic |
| `verification_<UTC>.{json,md}` | `D:\Autofocus Bank\bank_verify` | recall/precision measured against the stale goldens, on the stale representation |
| `docs/bobp-m101-recall-investigation-results.md` | repo | the `recall@SNR≥12 = 0.126` audit was measured headless on a mosaic — `bobp_m101` is one of the four |
| the parts of `docs/optimizer-sensitivity-pinning-design.md` resting on it | repo | inherits the above |

Re-baselining `bank-verify` and regenerating the goldens is explicitly **out of scope here** (Phase 2, separate
branch). Those conclusions are flagged, not corrected — a deliberate deferral, not a claim that they are fine.

## Runtime cost, measured

Two facts worth not rediscovering.

- **Bayered runs cost ~+20% peak memory.** `bobp_m101` (10 × 23.5 MP, `--max-evals 5`, all frames + all early
  contexts resident) went 6,034 MB → 7,242 MB. The delta is the `Rgb48` debayered `BitmapSource` an
  `IDebayeredImage` carries (~6 B/px) on top of the raw `ushort[]`. Mono is a wash (20,259 → 20,143 MB on `lumos`,
  23 × 61 MP): the `ushort[]` + `Gray16` source replaces the `CV_32F` Mat at the same 4 B/px. **The live wizard
  already pays exactly this.**
- **`bobp`'s optimize phase went 87.6 s → ~300 s.** The CFA filter + debayer now run **per early-context build**
  instead of once at load. **This is inherent, not a regression to optimize away.** The filter is gated on the
  candidate's own `HotpixelFiltering`/`HotpixelThresholdingEnabled`/`HotpixelThreshold`, so hoisting it out of the
  loop is precisely the load-time-filtering scheme this design rejects — it would turn two searched axes into
  silent no-ops. The live wizard pays the same cost for the same reason. (The early-context cache already avoids
  the repeat within a matching early key; what remains is the irreducible per-early-key work.)
- **`tilt`'s 4-corner phase went ~20 s → ~215 s per step** on a bayered 4-run dataset — a bigger factor than the
  optimizer's, because its params are FIXED and its five regions are five different early keys, so one loaded frame
  is CFA-filtered + debayered five times where it used to be cloned five times. The honest fix is not to pre-filter
  (same objection as above) but to cache the *prepared source image* per (frame, hot-pixel params) inside
  `StarDetector` — the debayer only depends on those. Not done here; ~15 min for a full tilt validation is
  acceptable for a diagnostic harness, and the correctness gain is the point.

## Known remaining divergence: per-filter star detection

With **per-filter star detection enabled**, live resolves the captured filter's settings snapshot
(`HocusFocusStarDetection.ResolveEffectiveOptions`, keyed on the frame's `MetaData.FilterWheel.Filter`) and
detects with *those* settings. A headless run has no filter wheel, so it runs `StubPerFilterStarDetectionStore`
with `Enabled => false` and detects with the **profile-level** settings.

This is not fixed here — resolving the filter headlessly would mean `GetOrSeedSnapshot`, which *persists* a seeded
snapshot into the profile, and every headless runner is contractually read-only on the profile. It is made
**loud** instead: the stub reads the persisted flag through `PerFilterStarDetectionStore.IsEnabledForActiveProfile`
— a read-only query on the class that owns the key, so no second copy of `"PerFilterStarDetectionEnabled"` exists —
and warns to stderr + the log when the feature is on. Compare against the target filter's settings, or turn the
feature off for the comparison.

## Verification, as landed

`optimize --per-run`, profile `b10b1d6d` (Default), after the full change including the tilt migration:

| Run | Result |
|---|---|
| `bobp` (bayered) | `Sensitivity 9.25 → 10` (not at floor), hard-floor PASS with **min stars = 27**, `HotpixelThreshold 0.0005 → 0.00035` (the axis still moves), `J 0.990408 → 0.997172`. Matches the in-app wizard's `10.000` / 27–31. Optimize phase ~300 s, 324 early-context builds / 1926 reuses. |
| `uneven` (mono) | `Sensitivity 31.2083`, min stars 51, `J 0.997449 → 0.996368`, σ_focus `6.62139 → 6.95275` — identical to the pre-change baseline, down to **310 early-context builds / 2190 reuses (87.6%)**. Mono is untouched. |

**Tilt results move, and they should.** `tilt --dataset "D:\Tilt Calibration Bank\astrodet"` (bayered, 4-step,
stored detection settings), old binary vs new, same profile `ce3f3e63`:

| | old (raw mosaic) | new (live parity) |
|---|---|---|
| Baseline plane (A, B) | −3.73, 13.46 (dir 195.5°) | −10.85, −7.84 (dir 305.8°) |
| ReBaseline2 plane | 9.60, −13.73 (dir 34.9°) | −27.04, −30.68 (dir 318.6°) |
| Screw 1 angle | 303.31° | **256.26°** (a 47° move) |
| Confidence SNR (4-corner) | 1.9858 → **FAIL** (<2) | 2.2988 → **PASS** |
| Noise floor | 30.28 | 28.00 |
| Screw move-magnitude ratio | 3.61× | **1.79×** (ideal 1×) |
| Stars in the per-star sensor model | 58 / 59 / 59 / 68 | 70 / 68 / 74 / 68 |

Three of the run's quality indicators improve (noise floor, move balance, and the confidence gate it now clears),
and the sensor model fits ~15% more stars — consistent with detecting on a better-conditioned image. The dataset
itself is still flagged unreliable (`move ratio` far from 1×, "re-capture turning each screw the same amount"), so
this is evidence that the representation mattered, **not** a claim that the new screw geometry is correct.

Unit coverage: `HeadlessDetectionParityTests` (mechanism: mono byte-identity, `SaveLumChannel == false`, the
hot-pixel axis still biting, the debayer gate and CFA-pattern precedence) and `HeadlessDetectionParityGuardTests`
(source-level: only the loader may produce a raw Mat, the deleted parameters stay deleted, no re-implemented split
detector, the wizard's Review step still goes through the shared seam). Both mutation-verified.

## Status

Implemented on `ghilios/headless-detection-parity`. Phase 2 (regenerate the four bayered goldens — after
re-exporting their `.linear.fits` — re-baseline `bank-verify`, re-check the `bobp_m101` recall conclusion) is a
separate branch. Execution plan: `plans/headless-detection-parity-plan.md`.
