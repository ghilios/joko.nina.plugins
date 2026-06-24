# Plan: adaptive NoiseClippingMultiplier (spatially-adaptive binarization)

**Read first:** `docs/adaptive-noiseclip-design.md` (the approved design — why, the exact root cause, the
algorithm, the knobs, the rollout gate). This plan is the bite-sized execution of that spec. **Branch:**
`ghilios/adaptive-noiseclip` (already created off `develop`; PR #96's verification harness is merged into develop,
so `bank-verify` is available). Run `/clear` before starting so no stale context leaks in.

**One-line goal:** make the structure-map binarization threshold spatially adaptive (`local-median + NC·local-σ`)
behind a flag that is bit-identical when off, then — per the design's rollout gate — **flip the default ON if it
improves recall AND precision at NC=2 on the AF bank with no AF/donut regression, else drop the feature.** No
permanent opt-in flag.

## Execution status — HANDOFF (2026-06-24)

**ALL STEPS DONE.** Steps 1–5 (implementation, commit `9a80324`). **Step 6 (validation) + Step 7 (rollout gate)
COMPLETE:** the OFF-vs-ON AF-bank A/B at NC=2 PASSED all four gate criteria — bank-median recall@SNR≥12 0.870→0.877,
precision 0.585→0.618, AF σ 10.26→8.84 (all ↑); cwhite_2026 recall 0.459→0.862; donut runs no regression (Panos
flat, mufti +26%). So **`LocallyAdaptiveBinarization` default flipped to ON** (4 seams) and the gate-logic test
`Detect_HighSensitivityGate_RejectsCoreHaloStarsOnHonestMeanFlux` pins legacy-OFF (same as its NC pin). Full suite
green 1528/0. Results in `docs/af-bank-noiseclip-sweep-results.md`. Ready for PR into develop.

### Build/test cycle on this machine (WSL → Windows) — important
- There is **no Linux dotnet**; build/test with the **Windows** `dotnet.exe` at `/mnt/c/Program Files/dotnet/dotnet.exe`
  (v10.0.301). Pass **Windows-style paths** (WSL interop does NOT translate `/mnt/c/...` for a Windows exe).
- Run the unit suite scoped to the **Tests project** (this is the CLAUDE.md invariant command, and it dodges a
  TestApp output-DLL lock — see Step 6 note):
  `"/mnt/c/Program Files/dotnet/dotnet.exe" test 'C:\Users\ghili\src\nina.plugins\Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj' -c Debug --nologo`
  (Building the whole `.sln` also builds TestApp, whose post-build copy fails while a TestApp is running — that is the
  lock, not a code error. TestApp itself compiles clean: `dotnet.exe build TestApp.csproj` shows zero `CS####`,
  only `MSB3021/3027` copy-lock errors.)

### What was implemented (all behind the flag; OFF ⇒ byte-for-byte legacy)
- **New options** `LocallyAdaptiveBinarization` (bool, default false) + `AdaptiveNoiseBlockSize` (int, default 128,
  setter-validated [16,1024]) plumbed everywhere `DonutMorphCloseSize`/`DefocusAwareDonutDetection` live:
  `StarDetectorParams` (Interfaces/IStarDetector.cs) + both added to `StarDetector.EarlyCacheKeyProperties`;
  `IStarDetectionOptions`; `StarDetectionOptions` (backing fields, accessors, InitializeOptions, ResetDefaults,
  ApplyOptimizedSnapshotToLiveProperties, ApplyKnobs); `HocusFocusStarDetection.BuildStarDetectorParams` +
  `BuildDefaultStarDetectorParams`; `OptimizedStarDetectionSettings` (+ FromParams, inert defaults);
  `StarDetectionSettingsSnapshot` (+ FromOptions); `OptionsDataTemplates.xaml` (Advanced CheckBox + `UnitTextBox`
  with `DoubleRangeRule` 64–256 — the codebase uses DoubleRangeRule for ints, not IntegerRangeRule — + two tooltips,
  grid rows 52/53).
- **Pure helper** `CvImageUtility.ComputeLocalBackgroundGrid(image, blockSize, sigmaFloor)` → `LocalBackgroundGrid`
  (per-block median + 1.4826·MAD, σ floored, ceil grid, partial edge blocks, parallel, upper-middle median matching
  `CalculateStatistics`; matches `snr_ref.coarse_bg`). TDD'd in `Tests/Utility/LocalBackgroundGridTests.cs`.
- **`CvImageUtility.BinarizeAdaptive(src, dst, surface)`** — per-pixel `src > surface` via Subtract+THRESH_BINARY
  (strict-greater, {0,1} CV_32F, matches scalar `Binarize`; degenerate-equivalence + per-pixel tests).
- **Wiring in `StarDetector`** (the binarization seam): when ON, the σ grid is sampled on `noiseReducedImage` INSIDE
  the existing kappa-sigma background task (before that image is disposed → F4 σ-consistency, same scale as the
  global σ so NC stays meaningful), the median grid on `structureMap` pre-dilation (mirrors the global median), the
  two grids are combined `median + NC·σ` at GRID resolution (bilinear upsample is linear, so one `Cv2.Resize`),
  upsampled, and `BinarizeAdaptive` applied. When OFF, none of this runs — the exact legacy
  `CvImageUtility.Binarize(structureMap, structureMap, binarizeThreshold)` line. Guarded by `StructureNoiseEstimate`
  (the task now returns kappa-sigma result + optional σ grid). Sigma floor `1e-6f` (numerical only).
- **TestApp harness parity:** `bank-verify` `--adaptive-binarize` / `--adaptive-block <n>` (applied to C0 BaseDefault
  next to the NC sweep) + `OverlayOptimized`; `golden eval` `--adaptive-binarize` / `--adaptive-block` in
  `ApplyParamOverrides` + `OverlayAll`; `InspectAlignRunner.ApplySettingsOverrides` + `OverlayAll`.

### Bit-identical proof (the rollout gate)
`Tests/StarDetection/StarDetectorEquivalenceTests.cs`: `Detect_AdaptiveBinarizationOff_MatchesLegacyBaseline`
(flag OFF == committed golden signature) and `Detect_AdaptiveBinarizationOn_RecoversSameStarsOnUniformField`
(ON path runs end-to-end and recovers the same stars on a uniform field). The pre-existing
`Detect_SmallField_MatchesGoldenBaseline` (default params = flag OFF) also still passes.

### Step 6 — how to resume (NOT yet run)
At handoff a prior session's full `bank-verify` (PID 38176, commit `b331479`, opt_A/opt_B, started 08:59) was still
running and held the TestApp output-DLL lock, so `TestApp.exe` could not be rebuilt the normal way. Either:
  1. **Wait** for it to finish — done when a new `D:\Autofocus Bank\verification_<UTC>.md` appears (watch the flushed
     `D:\Autofocus Bank\bank_verify_progress.log`), then build TestApp normally; OR
  2. **Build TestApp to a separate output dir** to dodge the lock:
     `"/mnt/c/Program Files/dotnet/dotnet.exe" build 'C:\...\TestApp\TestApp.csproj' -c Debug -o <freshdir>`
     then run `<freshdir>\TestApp.exe`.
Then run the OFF-vs-ON comparison at NC=2 (close any running NINA first; watch `<out>/bank_verify_progress.log`;
~1–2 hr per config, ON slower due to the per-block reduction):
```
TestApp bank-verify --runs "D:\Autofocus Bank" --out <scratchOFF> --nc-sweep 2,3,4 --match-radius 12
TestApp bank-verify --runs "D:\Autofocus Bank" --out <scratchON>  --nc-sweep 2,3,4 --match-radius 12 --adaptive-binarize
```
(The latest `D:\Autofocus Bank\verification_*.{json,md}` / `_prior_reports` can serve as the flag-OFF baseline if you
prefer not to re-run OFF.) Goldens already exist (147 sidecars) — **do NOT regenerate**. Expected at NC=2:
recall@SNR≥12 up further AND precision held/improved (largest gains on vignetted/gradient frames); no AF σ_focus
regression; no donut-recall regression — verify Panos + mufti explicitly (donut runs need donut-aware: run them via
config B, or `--adaptive-binarize` together with `--defocus-donut` on `golden eval` / `inspect-align`).

### Step 7 — where the default flips on a PASS (mechanical)
Flip false→true in: `StarDetectorParams.LocallyAdaptiveBinarization` initializer (Interfaces/IStarDetector.cs);
`BuildDefaultStarDetectorParams` (HocusFocusStarDetection.cs); `StarDetectionOptions.InitializeOptions`
(`GetValueBoolean("LocallyAdaptiveBinarization", false)`) + `ResetDefaults`. The XAML checkbox binds to the option,
so it needs no separate literal. Keep `AdaptiveNoiseBlockSize`=128. Keep the boolean as an off-switch and KEEP the
bit-identical test (it pins legacy-OFF). Re-baseline nothing. Update `docs/af-bank-noiseclip-sweep-results.md` with
the on-vs-off bank numbers. On a FAIL, remove the option entirely and write up why in
`docs/adaptive-noiseclip-design.md`. Then `superpowers:finishing-a-development-branch` → PR into develop.

## Project invariants (from CLAUDE.md — do not skip)
- TDD: write the failing test first for every pure helper; run the full suite after every change:
  `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.
- A new persisted `*Options` value **must** also get a control in `Resources/OptionsDataTemplates.xaml`.
- Read `.claude/docs/star-detection-internals.md` and `.claude/docs/options-system.md` before touching the
  detector / options.
- Commit with the privacy email (`322725+ghilios@users.noreply.github.com`); never push to develop directly.

## Step 1 — Orient (read, don't code yet)
- `StarDetector.cs`, the binarization block (search `binarizeThreshold`): currently
  `structureMapStats.Median + p.NoiseClippingMultiplier * noiseReducedImageNoise.Sigma` — a single global scalar.
  This is the seam. Note the `EarlyParamKeys` list near the top (NoiseClippingMultiplier is in it) — the two new
  params are EARLY params (they change the candidate set) and must be added there so the optimizer split-frame
  cache stays correct.
- Mirror how `DefocusAwareDonutDetection` is plumbed end-to-end (it is the template for an opt-in, bit-identical
  detector flag): grep it across `Interfaces/IStarDetector.cs` (the `StarDetectorParams` field),
  `StarDetection/HocusFocusStarDetection.cs` (`BuildStarDetectorParams` + `BuildDefaultStarDetectorParams`),
  `StarDetection/StarDetectionOptions.cs` (field + accessor + `ResetDefaults`), and
  `Resources/OptionsDataTemplates.xaml` (the Advanced CheckBox + tooltip).

## Step 2 — Add the params + options (default OFF)
- `StarDetectorParams` (`Interfaces/IStarDetector.cs`): `bool LocallyAdaptiveBinarization = false;` and
  `int AdaptiveNoiseBlockSize = 128;`. Add `LocallyAdaptiveBinarization` to the EARLY param key list.
- `StarDetectionOptions.cs`: backing fields + accessor get/set (persist via `optionsAccessor`) + set both in
  `ResetDefaults` (OFF / 128). Plumb both through `BuildStarDetectorParams` AND `BuildDefaultStarDetectorParams`.
- `OptionsDataTemplates.xaml`: an Advanced CheckBox for the bool + a `UnitTextBox`+`IntegerRangeRule` (≈64–256) for
  the block size, with tooltips (copy the donut-flag block's style).
- TestApp overlay parity (so the harness can exercise it): add both fields to `OptimizedStarDetectionSettings` +
  the overlay in `BankVerifyRunner.OverlayOptimized`, `GoldenEvalRunner` `OverlayAll`, and
  `InspectAlignRunner.ApplySettingsOverrides`. Add a `--adaptive-binarize` / `--adaptive-block <n>` CLI override to
  `golden eval` and `bank-verify` (mirror the existing `--noise-clip` override) so you can A/B the flag without
  changing the default yet.
- Build; full suite green (these are additive, no behavior change yet).

## Step 3 — TDD the pure block-statistics helper
- New pure helper (e.g. `CvImageUtility.ComputeLocalNoiseSurface` or a small static in the detector namespace) that,
  given an image + block size, returns per-block `(median, 1.4826·MAD)` on a coarse grid (floor σ at a small eps),
  matching `tools/golden/snr_ref.py:coarse_bg` semantics (block median + MAD·1.4826, edge blocks pad by
  replication). Keep the grid reduction separate from the upsample so it is unit-testable on a tiny synthetic Mat.
- RED→GREEN tests: a uniform image → flat surface = (median, ~0); a two-region image (clean half + noisy half) →
  the noisy block's σ is larger; edge padding behaves. Put tests under `Tests/` (link any TestApp-resident helper
  like the existing `OptimizationRunDiscovery` pattern, or test the plugin helper directly).

## Step 4 — Wire it into binarization (guarded; bit-identical when off)
- At the binarization seam: if `!p.LocallyAdaptiveBinarization`, keep the EXACT current scalar path (do not even
  compute the surface) — this is the bit-identical guarantee. If on: compute the coarse `(median_local, σ_local)`
  grids from the **same source the global estimate uses** (the noise-reduced structure-map source; honor the F4
  σ-consistency note), bilinearly upsample to full res, and binarize `structureMap(x,y) > median_local + NC·σ_local`
  instead of the scalar compare. Everything downstream (dilation, flood-fill, gates) is unchanged.
- Use OpenCvSharp for the upsample (`Cv2.Resize` bilinear) and a tight loop / `Cv2.Compare` for the threshold; keep
  it in the cacheable EARLY context.

## Step 5 — Bit-identical regression test
- A test that detects a real (or synthetic) frame with the flag OFF and asserts the accepted-star set is identical
  to a pre-change baseline (same guarantee `DefocusAwareDonutDetection`-off has). This is the gate that lets the
  default flip safely. Full suite green.

## Step 6 — Validate on the AF bank (the design's validation plan)
The golden set + optimizer settings already exist on **this machine** (do NOT regenerate):
- Goldens: `*.golden.json` beside the frames in `D:\Autofocus Bank\...` (147 sidecars; **high-tier complete**,
  uncertain tier complete for small/moderate runs, a sample for deep wide-field runs — so trust **recall@SNR≥12**
  everywhere and **precision** on the well-covered runs; mccomiskey/timmer precision stays a lower bound).
- Prior baseline reports: `D:\Autofocus Bank\_prior_reports\` and the latest `verification_<UTC>.{json,md}` at the
  bank root (flag-OFF baseline).
- Optimizer A/B settings (optional, only needed for the A/B columns): the run that produced them is gone, but
  re-run `optimize --per-run [--donut]` if you want A/B; for THIS feature the **C0 flag-on-vs-off** comparison is
  what matters.

Run, comparing flag OFF vs ON at NC=2 (use the `--adaptive-binarize` override from Step 2, or temporarily default it
ON):
```
TestApp bank-verify --runs "D:\Autofocus Bank" --out <scratch> --nc-sweep 2,3,4 --match-radius 12
TestApp bank-verify --runs "D:\Autofocus Bank" --out <scratch> --nc-sweep 2,3,4 --match-radius 12 --adaptive-binarize
```
Expected at fixed NC=2: recall@SNR≥12 up further AND precision held/improved (the spatial surface improves both
axes — no single global NC can). Largest gains on vignetted/gradient frames. Confirm AF σ_focus does not regress
and donut runs (Panos, mufti) do not lose donut recall.
- Operational notes: `bank-verify` writes a flushed progress log `<out>/bank_verify_progress.log` (WSL block-buffers
  stdout, so watch that file, not stdout); per-run sidecars in `<out>/bank_verify/<runtag>/verify.json`; full bank
  is ~1–2 hr on 61–102 MP frames; close any running NINA so it doesn't lock the profile / contend for CPU.

## Step 7 — Rollout gate (the whole point)
- **Pass** (recall ↑ AND precision not ↓, no AF/donut regression): flip `LocallyAdaptiveBinarization` default to
  **ON** in `BuildDefaultStarDetectorParams`, the `StarDetectionOptions` default + `ResetDefaults`, and the
  XAML initial value. Keep the boolean as an off-switch + for the bit-identical test. Update
  `docs/af-bank-noiseclip-sweep-results.md` with the on-vs-off numbers. Full suite green.
- **Fail:** remove the option entirely (no permanent default-OFF flag) and write up why in the design doc.
- Finish via `superpowers:finishing-a-development-branch` → PR into develop.
