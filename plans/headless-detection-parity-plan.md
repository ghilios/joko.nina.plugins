# Headless Detection Parity — Implementation Plan (Phase 1)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development or
> superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** every TestApp runner detects on the same image the live app does, so headless results predict in-app
behaviour. Mono runs must stay byte-identical.

**Spec:** `docs/headless-detection-parity-design.md` (approved). Branch: `ghilios/headless-detection-parity`,
branched off `ghilios/exposure-recommendation` because it edits the same TestApp call sites — rebase onto
`develop` once that PR merges.

**Phase 2 (separate branch, after this lands):** regenerate the four bayered goldens, full `bank-verify`
re-baseline, and re-check the `bobp_m101` recall conclusion that motivated PR #111. Not in this plan.

**The measured target.** On `D:\Autofocus Bank\bobp` the fixed harness must land `Sensitivity = 10` with ~29 min
stars, matching the wizard's `10.000` / 27–31. The shipped harness lands `0.0` / 52.

**Environment:**
- No `dotnet` in WSL. Build/run the Windows exe via a `.bat` through `cmd.exe` (UNC paths break `cmd`'s `cd`, so
  the batch must `pushd` the output dir). Bash `timeout` 600000.
- **Close NINA before headless runs** — it locks the profile (`ce3f3e63` = `astrodet`). Profile `b10b1d6d`
  (`Default`) is a usable unlocked alternative; both have `DebayerImage=true`.
- A stale `testhost.exe` blocks rebuilds (`MSB3027`): `cmd.exe /c "taskkill /F /IM testhost.exe /T"`.
- Commit identity: `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com"`
  with `--author=` the same. Never push.

---

## Task 1 — Prefer sharing the app's path over mirroring it

The root bug class is **replication**: `OptimizationDiagnosticRunner.cs:236` says outright that it *replicates*
the sigma rejections "the wizard's `RunEvaluationLoader` uses". Every such copy is a place to drift.

- [ ] **First, investigate whether TestApp can use the plugin's own path directly** — `RunEvaluationLoader` and
      its `HocusFocusSplitFrameDetector`, which already take `IRenderedImage` and therefore get CFA filtering and
      debayer for free, per-candidate, exactly as live.
- [ ] The likely blocker is `RunEvaluationLoader.LoadRenderedImageAsync`'s `imagingMediator.PrepareImage(...)`.
      Determine whether detection actually needs the *rendered/stretched* product or only an `IDebayeredImage`
      carrying the CFA pattern — `PrepareSrcImageFromRenderedImage` reads `image as IDebayeredImage` and
      `ToOpenCVMat`, which suggests raw data, not the stretch. If only the debayered image is needed, TestApp can
      construct one without an imaging mediator.
- [ ] **If sharing is infeasible, STOP and report before writing a parallel implementation.** Do not invent a
      second mirror of the app's behaviour; that is the bug being fixed. Report what blocks it.

## Task 2 — Group 1: fixed-params runners

These call the detector once at fixed params, so `FocusSweepDiagnosticRunner.cs:190`'s existing pattern is exactly
faithful: debayer to luminance + CFA hot-pixel filter at those params + `HotpixelFiltering = false` so the detector
does not filter twice.

- [ ] `GoldenRunner.cs:124`, `GoldenEvalRunner.cs:178`, `AnnotateRunner.cs:92`,
      `ContaminationDiagnosticRunner.cs:172`, `RecommendRunner.cs:172`, `AfFitDiagnosticRunner.cs:144`,
      `BankDonutMetaRunner.cs:111`, `DiagnoseLabelsRunner.cs:293`, `FocusSweepDiagnosticRunner.cs:235`
- [ ] `ExportLinearRunner.cs:69` — **judge separately**. It exports *linear* frames; debayering may be exactly
      what it must not do. Decide, document the decision at the call site, and say which way you went.
- [ ] Honour `ImageSettings.DebayerImage` rather than hard-coding — the app gates on it.
- [ ] At every call site, replace the silent default with an explicit argument and a one-line comment saying why,
      so the next reader sees a decision rather than an omission.

## Task 3 — Group 2: the optimizer

`OptimizationDiagnosticRunner` and `BankVerifyRunner`'s A/B configs must carry the debayered image end-to-end so
the detector does its own per-candidate CFA filtering.

- [ ] Implement via Task 1's finding (share the plugin path if possible).
- [ ] **`HotpixelThreshold` / `HotpixelThresholdingEnabled` must remain live searched axes.** The probe proved a
      load-time filter turns them into silent no-ops — the optimizer went on searching `HotpixelThreshold`
      (`0.0005 → 0.0015`) against an already-filtered image. Assert this in a test.
- [ ] `BankVerifyRunner.cs:181` is the highest-value site: it produces the recall/precision reports the golden sets
      are scored against.

## Task 4 — Tests

- [ ] **Mono runs are byte-identical.** The strongest guard: pick a mono bank run, capture optimizer output
      before and after, assert no change. 18 of 22 bank runs are mono; a regression there would be severe.
- [ ] **Bayered runs match the app.** Pin the measured target: `bobp` lands `Sensitivity = 10`, min stars ≈ 29.
- [ ] **The hot-pixel axes still bite** (Task 3) — mutate the threshold and assert detection output changes.
- [ ] Guard against the defect recurring: a test that fails if a runner calls `LoadFloatMat` without an explicit
      `debayerToLuminance` argument, so the next runner cannot silently inherit the wrong default.
- [ ] Full plugin suite green: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.
      `SendAsync_WritesOnABackgroundThread` is known-flaky and unrelated.

## Task 5 — Record what is now stale

- [ ] Note in the spec which stored artifacts the fix invalidates: goldens for `SorenVance`, `bobp`, `bobp_m101`,
      `timmer`; their `optimized_settings.json`; existing `verification_*.md`; and the `bobp_m101` recall analysis.
- [ ] Add the parity requirement to `.claude/docs/testapp-cli.md` so it is discoverable from the CLI docs.

## Verification

- [ ] `bobp` headless reproduces the wizard: `Sensitivity = 10`, min stars ≈ 29, no exposure recommendation.
- [ ] A mono run (e.g. `uneven`, previously `31.21`) is unchanged.
- [ ] Full suite green.
