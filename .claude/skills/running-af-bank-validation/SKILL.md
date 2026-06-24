---
name: running-af-bank-validation
description: Use when verifying HocusFocus star-detector recall/precision, autofocus fit, or sensor-model fit across the saved AF-run bank, regression-comparing a detector change, or sweeping NoiseClippingMultiplier — i.e. running TestApp bank-verify / bank-clean / bank-donut-meta over "D:\Autofocus Bank".
---

# Running AF-bank validation

## Overview

`TestApp bank-verify` measures the star detector against a detector-independent golden set across every run in the
AF bank, per run × config, and writes a timestamped regression report to the bank root
(`verification_<UTC>.{json,md}`, schema `afbank-verify/2`). Configs: **C0** (as-default, swept over
NoiseClippingMultiplier), **A** (optimized donut-off), **B** (optimized donut-on). Use it to confirm a detector
change (e.g. an NC default, a new gate) helps bank-wide, not just on one frame.

**Prerequisite:** the golden set must already exist (per-image `*.golden.json` beside the frames). To create it,
use **generating-af-golden-data**. Goldens are detector-independent, so generate once and reuse across configs.

## Environment / prerequisites

- **Windows + WSL**: TestApp is a Windows `.exe` invoked from WSL (paths quote cleanly through interop). You need
  Windows `dotnet`, `cmd.exe`, and `wslpath` available — i.e. the normal dev box, not a Linux-only checkout.
- **NINA profile**: every detector subcommand loads a NINA profile (it reads camera/pixel-scale + options). With
  no arg it uses the **active** profile (run NINA once to create one); to pick a specific one pass
  `--profile-id <guid>` (GUIDs are the `*.profile` filenames under `%LOCALAPPDATA%\NINA\Profiles`). If you see
  `No active NINA profile could be loaded`, that's this — pass `--profile-id`.
- **Bank path is machine-local** (here: `D:\Autofocus Bank`, WSL `/mnt/d/Autofocus Bank`). It holds the AF runs +
  their golden sidecars; it is NOT in the repo. `cwhite_2026` (the validation anchor below) is a run *folder inside
  this bank* — a complete dry-run replica with full goldens already present.

## Pipeline (in order)

```bash
EXE=./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"   # build first

# 1. Tidy stale per-config clutter (DESTRUCTIVE; keeps frames, autofocus_report_Region*.json, labels/,
#    *.golden.json, run_meta.json, *.linear.fits). Dry-run first (no --apply) and read the list.
$EXE bank-clean --runs "D:\Autofocus Bank"            # lists
$EXE bank-clean --runs "D:\Autofocus Bank" --apply    # deletes

# 2. Per-run donut decision -> run_meta.json (donutAware flag drives golden-gen --donut + report grouping).
$EXE bank-donut-meta --runs "D:\Autofocus Bank" --refresh

# 3. Optimizer A/B prepass (only needed for the A/B columns; C0 NC-sweep needs none). ~1.5-3 hr.
$EXE optimize --per-run            --runs "D:\Autofocus Bank" --out "$(wslpath -w opt_A)"
$EXE optimize --per-run --donut    --runs "D:\Autofocus Bank" --out "$(wslpath -w opt_B)"

# 4. The verification. C0 NC sweep + A/B. Report -> bank root. ~1-2 hr.
$EXE bank-verify --runs "D:\Autofocus Bank" --out "D:\Autofocus Bank" \
     --nc-sweep 2,3,4 --match-radius 12 --commit $(git rev-parse --short HEAD) \
     --opt-a "$(wslpath -w opt_A)" --opt-b "$(wslpath -w opt_B)"
```

Omit `--opt-a/--opt-b` for a C0-only sweep (the headline recall answer; no optimizer prepass needed — much faster).

## Logic / interpretation

- **recall@SNR≥12 is the reliable headline** — the golden high tier is complete, so this number is trustworthy on
  every run.
- **precision is a LOWER BOUND where goldens are high-tier-dominated** (deep wide-field runs whose faint tier was
  only sampled). HF's real faint detections then count as false positives. The built-in `RecommendNoiseClip` can be
  skewed by this — **do not read the NC recommendation off bank-median precision.** Trust instead the **optimizer's
  converged NoiseClippingMultiplier** per run (it optimizes σ_focus, immune to the precision artifact) as the
  "what does the live data want" signal.
- **Donut runs (`donutAware=true`) have broken C0 numbers** (as-default has donut detection OFF → can't form
  candidates on rings → recall ~0.1–0.3, NaN AF σ, garbage sensor R²). Only **config B** evaluates them fairly.
- **A/B sit at the opposite corner** from C0: optimized settings shed faint stars for AF tightness → recall low,
  precision ~1.0, σ_focus ~1. For max recall use C0 (as-default); the optimizer optimizes σ, not star count.
- Run discovery is attempt-anchored (≥3 focuser positions). Frameless folders (e.g. an artifacts-only `astrodet`)
  are skipped. Dataset copies (`sensitivity_example1`≡`fmeschia`, `sensitivity_example2`≡`LinwoodFocus`) must
  produce bit-identical metrics — a free determinism check.

## Gotchas (hard-won)

- **bank-verify runs on a dedicated STA thread with a PUMPED Dispatcher — do not "simplify" that away.** It chains
  `RunEvaluationData.EvaluateAndFitAsync` (optimizer eval) + `StarDetector.Detect` + `SensorModel.RegisterStarsAndFit`
  in one process. Under a bare `new Application()` (no dispatcher pump) two things hang: (1) the EvaluateAndFitAsync
  await deadlocks on the captured non-pumping context; (2) the sensor fit is thread-affine and hangs on a
  thread-pool thread. The fix (in `BankVerifyRunner.Run`) is the StarReviewRunner pattern: dedicated STA thread +
  `DispatcherSynchronizationContext` + `Dispatcher.Run()`, and **NO `ConfigureAwait(false)`** (continuations must
  stay on the pumping STA thread). If you see it hang ~3 min in, this is why.
- **WSL block-buffers the Windows exe's stdout** → live stdout is useless for diagnosing long runs. Watch the
  flushed file log **`<out>/bank_verify_progress.log`** (and per-run `<out>/bank_verify/<runtag>/verify.json`).
  A flat CPU with no new progress lines = blocked; climbing CPU = just slow.
- **Close any running NINA first.** It locks the profile (`MigrateModularizedSolutionNamespaceChange` IO errors —
  benign but noisy) and competes for CPU (~2–3× slower on the 61–102 MP frames).
- **Launch long runs detached** (`nohup … & disown`) and poll the progress file, or use a tracked background job
  that exits when `verification_*.md` appears. Per-config ≈ 60–90 s on 61 MP; donut/optimized configs are slower.
- **WSL UNC opt paths work**: pass `--opt-a "$(wslpath -w opt_A)"` → `\\wsl.localhost\...`; the Windows exe reads it
  (`Test-Path` confirms). `LoadOptimized` looks for `<optRoot>/<sanitizedRunId>/optimized_settings.json` then
  `<optRoot>/optimized_settings.json`.
- **Validate the harness before trusting it:** `cwhite_2026` is a complete dry-run replica — bank-verify reproduces
  its config-B numbers exactly (P=0.848, recall@≥12=0.181, sensor R²=0.9933, 7/9 aligned). If those don't match,
  fix the harness before reading bank-wide results.
- **bank-clean `--apply` is destructive** — always dry-run first and confirm the list is only regenerated artifacts
  (`*_star_detection_result*.json`, diagnostic PNGs, `optimized_settings.json`, optimize/eval/contamination
  outputs). It re-runs idempotently (second pass deletes 0).

## Reference

- Plan + report schema: `plans/autofocus-bank-verification-plan.md`; results write-up `docs/af-bank-noiseclip-sweep-results.md`.
- TestApp CLI: `.claude/docs/testapp-cli.md`. Golden set method: `.claude/docs/golden-star-set.md`.
- Schema note: the live report is `afbank-verify/2` (the plan text predates it and says `/1` — code wins).
