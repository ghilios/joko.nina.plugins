# Plan: adaptive NoiseClippingMultiplier (spatially-adaptive binarization)

**Read first:** `docs/adaptive-noiseclip-design.md` (the approved design — why, the exact root cause, the
algorithm, the knobs, the rollout gate). This plan is the bite-sized execution of that spec. **Branch:**
`ghilios/adaptive-noiseclip` (already created off `develop`; PR #96's verification harness is merged into develop,
so `bank-verify` is available). Run `/clear` before starting so no stale context leaks in.

**One-line goal:** make the structure-map binarization threshold spatially adaptive (`local-median + NC·local-σ`)
behind a flag that is bit-identical when off, then — per the design's rollout gate — **flip the default ON if it
improves recall AND precision at NC=2 on the AF bank with no AF/donut regression, else drop the feature.** No
permanent opt-in flag.

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
