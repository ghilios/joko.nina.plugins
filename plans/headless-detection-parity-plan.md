# Headless Detection Parity — Implementation Plan (Phase 1)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or
> superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** every detection path — headless *and* the wizard's Review step — detects on the same image the live
optimizer does, so headless results predict in-app behaviour. Mono runs must stay byte-identical.

**Spec:** `docs/headless-detection-parity-design.md` (approved, revised after the Task-1 investigation).
Branch: `ghilios/headless-detection-parity`, off `ghilios/exposure-recommendation` — rebase onto `develop` once
that PR merges.

**Phase 2 (separate branch):** regenerate the four bayered goldens, full `bank-verify` re-baseline, and re-check
the `bobp_m101` recall conclusion behind PR #111. Not in this plan.

**The measured target.** On `D:\Autofocus Bank\bobp` the fixed harness must land `Sensitivity = 10` with ~29 min
stars, matching the wizard's `10.000` / 27–31. The shipped harness lands `0.0` / 52.

**Environment:** no `dotnet` in WSL — build/run the Windows exe via a `.bat` through `cmd.exe` using `pushd` (UNC
breaks `cmd`'s `cd`). Bash `timeout` 600000. Close NINA before headless runs (it locks the profile);
`ce3f3e63`=astrodet, `b10b1d6d`=Default, both `DebayerImage=true`. Stale `testhost.exe` blocks rebuilds — kill it.
Commit with the noreply identity as author **and** committer. Never push.

---

## Task 1 — The shared seam (do this first; everything depends on it)

Investigation complete; the approach is decided. Build an `IDebayeredImage` headlessly and hand it to the
detector, so CFA filtering + debayer happen **inside `Detect`, at the caller's params**, exactly as live.

- [ ] Add a shared helper (in `DiagnosticUtil`) that returns an `IRenderedImage`:
      `imageData.RenderImage().Debayer(saveColorChannels: false, saveLumChannel: false, bayerPattern: resolved)`.
      **`saveLumChannel` must be `false`** — `true` flips `ToOpenCVMat`'s guard (`CvImageUtility.cs:91`) and makes
      the hotpixel-off branch read luminance where live reads the mosaic.
- [ ] Load with `isBayered:` the frame's real value (the `_BayeredN_` filename token, as live does) instead of
      today's hard-coded `false` (`DiagnosticUtil.cs:91-93`). That flag only sets `Properties.IsBayered`; pixel
      data is untouched.
- [ ] **Bayer-pattern precedence must match live** (`ImageControlVM.cs:611-621`): profile
      `CameraSettings.BayerPattern` when ≠ `Auto`, else the frame's `MetaData.Camera.SensorType` when not
      Mono/Color. Today's mirror (`DiagnosticUtil.cs:111-114`) uses metadata-or-RGGB and ignores the profile
      override. **Fail loudly** rather than silently defaulting to RGGB — `ImageUtility.Debayer` throws
      `InvalidImagePropertiesException` on Mono/Color/Auto.
- [ ] **Honour `profileService.ActiveProfile.ImageSettings.DebayerImage`** — live gates the whole debayer on it
      (`ImageControlVM.cs:605`), and with it off, detection runs on the mosaic even at default hotpixel params.
      TestApp reads it nowhere today.
- [ ] Promote `HocusFocusSplitFrameDetector` from `private sealed` to `internal sealed`
      (`RunEvaluationLoader.cs:278`). `InternalsVisibleTo("TestApp")` already exists (`AssemblyInfo.cs:22`);
      `private` is not covered by it. Zero behaviour change — this is the only plugin change Task 1 needs.
- [ ] **Measure memory on the largest bank run before committing.** `IDebayeredImage` holds the raw `ushort[]`
      *plus* an `Rgb48` `BitmapSource` (6 B/px vs the current 4 B/px `CV_32F`), and the optimizer keeps all frames
      resident. The live wizard already pays this, so it should be fine — but confirm. If it bites, the documented
      fallback is a TestApp-side minimal `IDebayeredImage` carrying only `RawImageData` + `BayerPattern` +
      `SaveLumChannel=false`; that duplicates data shape, not algorithm.

## Task 2 — Fixed-params runners → `Detect(IRenderedImage, …)`

**Do NOT use the `HotpixelFiltering = false` pre-filter pattern** — the spec explains why it is unfaithful (it
triggers a spatial hot-pixel filter at `StarDetector.cs:549` that live never applies). Switch each detecting runner
from `StarDetector.Detect(Mat, …)` to the already-public `StarDetector.Detect(IRenderedImage, …)`
(`StarDetector.cs:278`). Same return type; `RejectedCandidates`, `DetectedStars`, `Metrics` all unchanged.

- [ ] `GoldenEvalRunner.cs:178` — highest value, it scores recall/precision
- [ ] `RecommendRunner.cs:172`, `DiagnoseLabelsRunner.cs:293`, `AfFitDiagnosticRunner.cs:144`,
      `BankDonutMetaRunner.cs:111`, `FocusSweepDiagnosticRunner.cs:235` **and `:190`** (replace its manual pattern
      too — it is the unfaithful one)
- [ ] `InspectAlignRunner.cs:122` — **was missing from the original list**; drives the real
      `SensorModel.RegisterStarsAndFit`
- [ ] `StarReview/StarReviewRunner.cs:160` — **was missing**; labels authored here feed the optimizer
- [ ] `ContaminationDiagnosticRunner.cs:172` — shared path for `.xisf`/`.fits`; **keep `.tif` on the Mat route**
      (NINA's TIFF loader normalizes by `1<<16` vs DiagnosticUtil's `ushort.MaxValue`, a ~1.5e-5 shift, and a TIFF
      has no CFA anyway)
- [ ] `GoldenRunner.cs:124` and `AnnotateRunner.cs:92` do **not** detect — they render tiles/overlays. They should
      still debayer (a mosaic renders as a visible checkerboard and these are the human/LLM authoring surface) but
      need no CFA filter.

## Task 3 — The optimizer and bank-verify

- [ ] `OptimizationDiagnosticRunner` (~`:461`) and `BankVerifyRunner.cs:181`: put the `IDebayeredImage` in
      `RunFrame.Image` (already `object`, `RunEvaluationData.cs:79`) and use the plugin's
      `HocusFocusSplitFrameDetector`. `ISplitFrameDetector.BuildContextAsync` already takes `object`, so
      `RunEvaluationData` needs **no change**.
- [ ] Delete `MatSplitFrameDetector` and `HarnessDetection.ToFrameDetectionResult` for these paths — they are the
      mirror being removed.
- [ ] **`HotpixelThreshold` / `HotpixelThresholdingEnabled` must remain live searched axes.** The probe proved a
      load-time filter turns them into silent no-ops (the optimizer kept searching `0.0005 → 0.0015` against an
      already-filtered image). Assert this in a test.
- [ ] Fix `RunFitConfig.PreferredModel`: `afOptions.HyperbolicFitModel` in the loader
      (`RunEvaluationLoader.cs:226`) but hard-coded `null` in `OptimizationDiagnosticRunner.cs:453` and
      `BankVerifyRunner.cs:208`.

## Task 4 — The two other defects (see spec)

- [ ] **`ExportLinearRunner.cs:69` — debayer, do NOT CFA-filter.** Its own doc already claims it debayers; the
      code doesn't. It feeds `tools/golden/snr_ref.py`, so a mosaic reference scores mosaic-only finds as HF recall
      gaps HF could never close. The reference must keep its independent blind spots, so no CFA filter.
- [ ] **`StarDetectionOptimizerWizardVM.LoadFloatMatFromDisk` (`:4152-4171`) — a live-app bug.** It hard-codes
      `isBayered: false` + `ToOpenCVMat`, feeding `FrameReviewBuilder.BuildAsync` (`:4138`) which detects via
      `Detect(Mat, …)` (`FrameReviewBuilder.cs:118`). So the wizard's Review step detects and displays the raw
      mosaic for OSC runs while its optimizer step uses luminance — and the labels drawn there are what the
      optimizer then trusts. Route it through the same shared path.

## Task 5 — Tests

- [ ] **Mono byte-identity.** The strongest guard: a mono bank run's optimizer output must be unchanged before vs
      after. 18 of 22 bank runs are mono.
- [ ] **Bayered parity.** Pin the measured target: `bobp` lands `Sensitivity = 10`, min stars ≈ 29.
- [ ] **Hot-pixel axes still bite** — mutate the threshold, assert detection output changes.
- [ ] **Guard against recurrence, properly.** After the refactor, **delete** `LoadFloatMat`'s
      `debayerToLuminance` / `applyCfaHotpixel` parameters — nothing needs them once the rendered-image path
      exists — and assert no runner calls `StarDetector.Detect(Mat, …)` on a `.xisf`/`.fits` path. (An
      "argument must be explicit" test would still permit the *wrong* argument.)
- [ ] Full plugin suite green. `SendAsync_WritesOnABackgroundThread` is known-flaky and unrelated.

## Task 6 — Record what is now stale

- [ ] Note in the spec which stored artifacts the fix invalidates: goldens for `SorenVance`, `bobp`, `bobp_m101`,
      `timmer`; their `optimized_settings.json`; existing `verification_*.md`; and the `bobp_m101` recall analysis.
- [ ] Add the parity requirement to `.claude/docs/testapp-cli.md`.
- [ ] Note the pre-existing per-filter gap: with per-filter star detection enabled, live resolves the target
      filter's snapshot and headless cannot. Warn at runtime rather than silently diverging.

## Verification

- [ ] `bobp` headless reproduces the wizard: `Sensitivity = 10`, min stars ≈ 29, no exposure recommendation.
- [ ] A mono run (e.g. `uneven`, previously `31.21`) is unchanged.
- [ ] Full suite green.
