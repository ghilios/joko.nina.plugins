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
adding the CFA filter reproduces the app exactly, including the star-count scale and the absence of the exposure
recommendation. Unfiltered hot pixels present as faint stars, so the harness can drive the Sensitivity gate to its
floor and harvest them — an incentive that does not exist on the filtered image the app detects on.

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

### Two groups

**Group 1 — fixed-params runners.** `GoldenRunner`, `GoldenEvalRunner`, `AnnotateRunner`,
`ContaminationDiagnosticRunner`, `RecommendRunner`, `AfFitDiagnosticRunner`, `BankDonutMetaRunner`,
`DiagnoseLabelsRunner`, `ExportLinearRunner`, and `FocusSweepDiagnosticRunner:235`. These call the detector once at
fixed params, so `FocusSweepDiagnosticRunner:190`'s existing pattern is exactly faithful: debayer to luminance, CFA
hot-pixel filter at those params, and set `HotpixelFiltering = false` so the detector does not filter twice.

**Group 2 — the optimizer.** `OptimizationDiagnosticRunner` and `BankVerifyRunner`'s optimized (A/B) configs must
**carry the debayered image end-to-end** rather than a pre-loaded `Mat`, so the detector performs its own
per-candidate CFA filtering exactly as live. This is the decided approach: it is the only option under which the
hot-pixel axes retain their live meaning, and the optimizer is precisely the tool whose output we want to trust.

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

## Consequences for stored artifacts

Changing the representation changes what every stored number for the four bayered runs means.

- **Golden star sets** for `SorenVance`, `bobp`, `bobp_m101`, `timmer` were generated by `GoldenRunner` on the
  mosaic. Their per-star `confidence` is an SNR tier, and `recall@SNR≥12` is the headline metric — so the tiering
  itself shifts. **These are being regenerated** as part of this work (see the plan).
- **`bank-verify` reports** (`verification_<UTC>.{json,md}`) and the stored `optimized_settings.json` A/B inputs
  for those runs were produced on mosaics and should be treated as stale.
- **`docs/bobp-m101-recall-investigation-results.md`** and the parts of
  `docs/optimizer-sensitivity-pinning-design.md` that rest on it were measured on a mosaic. Re-baselining
  bank-verify is explicitly **out of scope** here, so those conclusions are flagged rather than corrected. That is
  a deliberate deferral, not a claim that they are fine.

Mono runs are unaffected and must be proven so by test.

## Status

Design approved. Execution plan: `plans/headless-detection-parity-plan.md`.
