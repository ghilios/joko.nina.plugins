# Synthetic AF bank — followups wave 7 (plan)

Spec: [`docs/synthetic-af-bank-followups-wave7-design.md`](../docs/synthetic-af-bank-followups-wave7-design.md).
Register: [`docs/followups.md`](../docs/followups.md).
Base: `develop` @ `761b9b1` (wave 6 merged, PR #182, unreleased — 4.0.0.12 predates it). Suite: **3635**.
Branch: `ghilios/synthetic-af-bank-followups-wave7`.

**Scope:** F19 (a decision), F18 (a shipped-recommender change), F39(b) (a bank axis that has never run), and — no
bank, no re-baseline — the AF chart's starting focuser position.

## Progress (2026-08-06)

Results so far: [`docs/synthetic-af-bank-followups-wave7-results.md`](../docs/synthetic-af-bank-followups-wave7-results.md).

| step | state |
|---|---|
| 0 — branch + pinned settings | **done** (`D:\hf_w7\pinned_settings.json`; the `UseAdvanced=False` warning reports **0** overridden knobs, so the file is clean) |
| 1 — AF chart starting position | **done** — 4 discriminating tests, 2 guards, both revert-checked |
| 2 — F19 instrument + decision | **decision DONE (no change)**; `exposureSecondsOverride` shipped; **Arm E NOT run** |
| 3 — F18 recommender + flags | **done** — 6 discriminating, 3 guards, revert-checked; wired into the wizard and `synth-validate` |
| 4 — F18 bank-derivation twin | **not started** |
| 5 — F39(b) flags | **done** — `golden eval --detection-binning`, `optimize --apply-run-detection-binning`, 4 tests |
| 6 — F39(b) arms | **arm G1 done and decisive** (see results); **arm G2 + the F38 assertion NOT run** |
| 7 — dry-run diff + re-render | **not started.** Confirmed unnecessary for F19 and F39(b); still owed for F18 |
| 8 — F18 σ_focus arms C/D/S | **not started** (flags exist, so this is now a script) |
| 9 — write-up | results doc + `followups.md` **done for what has run** |
| 10 — ship | PR opened |

**Because Step 2 closed F19 as "no change" and Step 6 showed F39(b) needs no re-render, the wave's re-render
footprint is F18's alone** — and nothing has re-rendered a frame yet, so F32's confirmation arm is still runnable
against wave 5's φ table exactly as it stands.

---

## Standing rules for every arm in this wave

Each of these cost a previous wave real time. They are not reminders; they are steps.

1. **`--settings D:\hf_w7\pinned_settings.json` on EVERY arm, explicitly** ([F42](../docs/followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)).
   Wave 6 made the default path a per-user file, so a fresh `-o` directory now INHERITS rather than bootstraps, and
   made the bootstrap warn loudly. Pin anyway.
2. **Read the `UseAdvanced=False` warning.** A settings file with `UseAdvanced=False` has its advanced knobs
   RECOMPUTED from the `Simple_*` presets, so editing `MinHFR` or `NoiseClippingMultiplier` there does nothing. The
   harness names the specific keys it is overriding. If that warning appears, the arm is void.
3. **`BaselineJ` is a free control** ([F41](../docs/followups.md#f41--a-prior-waves-control-arm-is-not-a-control-for-a-later-waves-binary)).
   No search produces it, so two arms of the same binary MUST agree on it to 6 dp. A disagreement is an instrument
   fault, not a subject. **And its twin:** a number that agrees TOO WELL with the hypothesis under test is also an
   instrument fault — wave 6's multi-round keep readout read ≈ 1.0 because the baseline pinning was conditional on a
   floor being set. Disbelieve agreement as readily as disagreement.
4. **Check for an existing cheap instrument before scheduling an expensive one.** `golden eval` exposes every
   detection override and needs no code (§ Step 6 uses exactly that).
5. **Change one thing.** `--dry-run` the derived-parameter diff BEFORE re-rendering anything (Step 7). Wave 6's
   numbers are readable only because it did.
6. **Count the tests that DISCRIMINATE.** Revert the fix, see which fail, label the rest as guards. Wave 6 shipped 7
   and reported 4.
7. **[F15](../docs/followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings): `optimize --per-run`
   rewrites settings INTO run folders.** Order arms so the canonical one lands LAST, and record which folders hold
   which arm in the results doc.
8. **[F37](../docs/followups.md#f37--the-ci-test-host-crashes-natively-accessviolationexception-aborting-2000-tests-with-zero-failures):
   on a red CI check, suspect the native test-host crash before a regression** — verify the test COUNT, not just the
   colour. Nothing merges on red.
9. **The plugin DLL is file-locked while NINA runs**, so build TestApp to a separate `-o` directory.

```
Build:  dotnet.exe build "$(wslpath -w $PWD/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w7\exe'
Tests:  dotnet.exe test "$(wslpath -w $PWD/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```

Wave-6 artifacts to reuse: `D:\hf_w6\pinned_settings.json` (copy forward to `D:\hf_w7\`), `oldframes\`, `snapshots\`.

---

## Step 0 — branch, and copy the pinned settings forward

- [ ] `git checkout -b ghilios/synthetic-af-bank-followups-wave7`
- [ ] `mkdir D:\hf_w7`; copy `D:\hf_w6\pinned_settings.json` → `D:\hf_w7\pinned_settings.json`
- [ ] Confirm it does **not** carry `UseAdvanced=False` (wave 6 established its advanced values coincide with the
      Typical presets, so the diff is empty — re-confirm rather than assume)

---

## Step 1 — item 4 first: the AF chart's starting position (no bank, unblocks nothing, blocks nothing)

Done first because it is independent of every measurement below and can therefore be finished and reviewed while
renders run.

- [ ] **Verify, do not implement, the serialization half.** `HocusFocusReport.cs:93-96` already writes
      `InitialFocusPoint {Position, Value}`; `:105` writes `FinalHFR`. Re-confirm against a real report on disk and
      record the numbers in the results doc (`2026-08-05--21-47-43…json` → Position 25000, Value 0.70298,
      FinalHFR 0.70526). **No code change here.**
- [ ] Add `ILoadedAutoFocusReportSource` (internal) with `TryFind(DateTime timestamp) → HocusFocusReport`.
- [ ] Default implementation over `HocusFocusVM.ReportDirectory`: filename-second candidates within ±5 s, ordered by
      |Δ|, deserialized as `HocusFocusReport`, **exact `Timestamp` equality** decides. Never throws; returns null.
- [ ] Inject it into `HocusFocusVM` (internal settable, defaulted) so tests can point it at a temp directory.
- [ ] Rewrite `SetCurveFittings`'s foreign-chart branch (`HocusFocusVM.cs:594-600`) per design §4.3/§4.4:
      re-populate from the loaded report, fall back to the existing sentinels on every failure path.
      **The same-run branch is untouched.**
- [ ] Tests in `HocusFocusVMChartReloadTests` writing REAL report JSON to a temp dir (design §4.5's table).
- [ ] **Revert the fix locally and re-run** — confirm exactly the three discriminating tests fail and the two guards
      pass. Record the count.
- [ ] Full suite green.

---

## Step 2 — F19's instrument, then F19's decision (cheap, and it gates Steps 3–8)

- [ ] Add `exposureSecondsOverride` to `SynthDatasetSpec` (three lines mirroring `stepSizeOverride` /
      `detectionBinningOverride` / `donutOverride`, `SynthBankSpec.cs:141-143`) + a `[Test]` that it overrides the
      derived value and is echoed in `synthetic_meta.json`.
- [ ] **Arm E**: render `D16_esprit550_ha3` and `D02_rich_135mm` at 0.5 / 1 / 2 / 4 / 8 s into
      `D:\hf_w7\armE\<dataset>_<t>s` (a scratch bank root — **never** the live bank).
- [ ] `optimize --per-run --settings <pinned>` on each rung → σ_focus, R², `FinalJ`, `BaselineJ`.
- [ ] `golden eval --settings <pinned>` on each rung → recall@high, precision, FP.
- [ ] Apply the **pre-registered rule** (design §1.4) verbatim: any rung improving σ_focus > 20 % relative to the
      derived-exposure rung on either dataset ⇒ **F19 REFUTED, stays Open**; otherwise **F19 closes
      working-as-intended** with design §1.1–1.3 as its stated position.
- [ ] **Say the answer out loud before planning Steps 3–8.** If it is "no change", the exposure axis of the bank
      derivation does not move and the re-render footprint is F18's alone — record that as the reason Steps 7–8 are
      cheaper than they would otherwise be.
- [ ] Control check: `BaselineJ` must be identical across the two datasets' repeated rungs where the frames are
      unchanged; D02's re-render must reproduce wave 6's bit-identical result at its derived exposure.

---

## Step 3 — F18: the recommender change, behind flags, with unit tests before any bank time

- [ ] `StepSizeRecommendation` gains `DetectHalfWidth` and `MaxUsefulHalfSpan` (both NaN when unmeasurable) and
      `WasDetectBounded`.
- [ ] `StepSizeRecommender.Recommend` gains an optional measured-detectability input — per-frame star counts,
      focuser positions, recovery flags, `NHard`, and `recoveryStepsPerSide`. **Absent ⇒ byte-identical to today**,
      pinned by a test.
- [ ] Implement `min(W_3x, max(W_detect, floor))` with:
      - `MinFramesForDetectHalfWidth = 3` non-recovery frames clearing `NHard`, else `W_detect = NaN` (no bound).
      - `MinHalfWidthSampledHalfSpanMultiple = 0.5`, the mirror of the existing `1.5` widening cap.
- [ ] Second, independently switchable behaviour: divide by `offsetSteps + recoverySteps` scaled to today's
      outermost-point multiple (design §2.2's table) — arm S only.
- [ ] Unit tests, each stated as discriminating or guard:
      - F18's own 3800 mm numbers reproduce: `min(6221, 5310) = 5310` → step in 1060–1330.
      - A run with 2 qualifying frames ⇒ `W_detect` NaN ⇒ recommendation unchanged (the starless guard).
      - A run where only the innermost frames qualify ⇒ the 0.5× floor binds ⇒ step ≥ 0.57 × current.
      - `W_detect` > `W_3x` ⇒ `W_3x` still wins (the bound only ever tightens).
      - Recovery frames excluded from `W_detect` (a recovery frame with stars must not widen it).
      - Absent input ⇒ bit-identical to the pre-change result on the existing fixtures.
- [ ] Wizard call site `StarDetectionOptimizerWizardVM.cs:3512` passes `bestEval.Metrics` +
      `capturedRecoveryStepsPerSide`; harness call sites pass theirs behind the flag.
- [ ] `--step-detect-bound` / `--step-size-for-executed-sweep` on `optimize` and `synth-validate`.
- [ ] Full suite green. **Revert-and-rerun** to count discriminators.

---

## Step 4 — F18: the bank derivation's analytic twin (this is what forces the re-render)

- [ ] `SynthBankDerivations` gains `W_detect*`: the largest defocus offset at which ≥ `NHard` truth stars still
      exceed the gate at the dataset's derived exposure, from the radiometry already there
      (`SolveExposureForTargetGateSnr`'s flux/σ model + the defocus-vs-peak-fraction model).
- [ ] `step*` becomes `min(W_3x*, max(W_detect*, floor))/PointsPerSide`, term for term with the runtime rule.
- [ ] Unit tests: a rig where detectability binds, one where it does not, and one where the floor binds.
- [ ] The derived-parameter change must appear in `expectedOptimal.stepSizeDefinition` prose so a stored
      `synthetic_meta.json` says which rule produced it.

---

## Step 5 — F39(b): apply the factor, cheap instrument first

- [ ] `golden eval --detection-binning N` → `DetectionBinningResolver.ApplyFactor` (one `Int(...)` line +
      the note string). Test: the flag changes `StarDetectorParams.DetectionBinning` **and** `PixelScale`.
- [ ] `optimize --apply-run-detection-binning`: read the factor, preferring the run's
      `synthetic_meta.json` `expectedOptimal.detectionBinning` over the settings file's (possibly
      `kept-from-base`) value, **print which source was used**, apply via `ApplyFactor` to seed and baseline.
      Absent ⇒ bit-identical.
- [ ] Test: with the flag absent, the resolved value is still written to disk (today's behaviour) and the params are
      untouched — i.e. the write side-effect stays, the discard becomes conditional.
- [ ] Full suite green.

---

## Step 6 — F39(b) arm, on the CURRENT frames (no re-render needed — design §3.3)

Run this **before** Step 7's re-render: it needs no new frames, and running it first keeps its two arms on one frame
set no matter what Step 7 does.

- [ ] **Arm G1** (cheap): `golden eval --detection-binning 1` and `--detection-binning 2` over
      D08, D09, D10, D12, D14, D15, D17, `--match centroid --match-radius 12`, `--settings <pinned>`.
      Read recall/precision overall + per-region + per-SNR-tier + FN gate attribution.
- [ ] **Arm G2**: `optimize --per-run` with and without `--apply-run-detection-binning` on the same seven.
      `BaselineJ` control first (it must differ between arms here — the binning changes what the baseline detects —
      so the F41 check takes its other form: **arm-vs-itself** reproducibility, run one dataset twice).
- [ ] Record `σ_focus`, `R²`, `FinalJ`, landed Sensitivity, and the recall/precision pair per dataset.
- [ ] **F38 regression assertion**: with binning 2 now live on these seven, add the bank check F38's fix has never
      been able to run — `MinHfrSeed.Resolve`'s captured-vs-binned comparison on a run whose factor is > 1.
- [ ] **F15**: these arms rewrite `optimized_settings.json` in the seven run folders. Order them so the canonical
      arm lands last; record the mapping.

---

## Step 7 — the `--dry-run` diff, and only then the re-render

**Nothing is rendered before this step's output is read.**

- [ ] `synth-bank --spec <spec> --out "D:\SyntheticAutofocusBank" --dry-run` on **all 20 datasets**, on the Step-3/4
      binary, captured to `D:\hf_w7\dryrun\after.txt`.
- [ ] Same, on a `develop`-@`761b9b1` build → `D:\hf_w7\dryrun\before.txt`. Diff.
- [ ] **Read the diff and write down, before rendering:**
      - which datasets' `step*` moved, and by how much;
      - that `exposureSeconds` moved on **nothing** (if Step 2 closed F19 as no-change) — if it moved, stop, because
        that means Step 2's decision did not actually hold the axis still;
      - that `detectionBinning` and `donutDetection` moved on nothing;
      - **whether `D18` / `D19` / `D20` are in the moved set** — design §0's question. If they are not, F32's
        confirmation arm is unaffected and this wave says so with the diff as evidence.
- [ ] Preserve the pre-wave frames + `synthetic_meta.json` of every dataset about to be re-rendered:
      `D:\hf_w7\oldframes\<dataset>` and `D:\hf_w7\snapshots\<dataset>`.
- [ ] `synth-bank --overwrite --datasets <exactly the moved set>`. **Not the whole bank.**
- [ ] Verify the truth sidecars of at least one unmoved dataset are **bit-identical** — the wave-6 `D02` control
      that bounds drift.

---

## Step 8 — F18's acceptance arms, σ_focus as the metric

- [ ] **Arm C** (control, no flags), **Arm D** (`--step-detect-bound`), **Arm S** (`+ --step-size-for-executed-sweep`)
      — one binary, `--settings` pinned, `synth-validate --scenarios S0,S1,S2` over the bank.
- [ ] **F41 check first**: `BaselineJ` identical across C/D/S on every run. Do not read a σ before it passes.
- [ ] **Instrument-fault check** (design §2.5 rule 4): if arm D's `W_detect` equals the sampled half-span on most
      datasets it is measuring the sweep's edge, not detectability — the arm is void until explained.
- [ ] Apply the pre-registered rules (design §2.5) verbatim: D ships at ≤ 2 % median σ_focus regression, no dataset
      worse than 20 %, A3 pass count not reduced; S ships instead of D only if it beats D by > 5 % median.
- [ ] Record which arm won **and the losing arm's numbers** — the point of pre-registering is that the loser is
      reported, not deleted.

---

## Step 9 — write it down

- [ ] `docs/synthetic-af-bank-followups-wave7-results.md`: headline table, every arm's numbers, the F19 decision with
      its rule and its measurement, the F18 arm that won and the one that did not, the F39(b) before/after on the
      seven, the dry-run diff, the F15 folder mapping, the discriminating-test counts, and a Reproduce block.
- [ ] `docs/followups.md`:
      - **F19** → Done/working-as-intended (or Open with the refutation), Status header and body in sync.
      - **F18** → Done, with which arm shipped and the σ_focus numbers.
      - **F39** → part (b) Done; the seven datasets' before/after.
      - **F32** → record what this wave did to its confirmation arm (design §0): which frames it must use, and that
        using the new bank means re-running wave 5's φ arms rather than comparing across.
      - **F21 / F25 / F26** → record precisely what F18's bound does and does not cover (design §2.4). None closes.
      - **F38** → the bank can now regression-test it; record the assertion added.
      - **New findings FLAGGED, not fixed inline**, with their own F-numbers.
- [ ] Sync every `**Status:**` header with its body (wave 6's last commit was exactly this cleanup).

---

## Step 10 — ship

- [ ] Full suite green locally; count and record discriminating vs guard tests for the whole wave.
- [ ] Commit with the privacy email:
      `GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "…"`
- [ ] Push the branch, open a PR against `develop`. **Never push `develop`.**
- [ ] On a red check, apply rule 8 (verify the test COUNT before reading it as a regression).

---

## Risks, and what each one costs

| risk | cost if it bites | mitigation, in this plan |
|---|---|---|
| Step 2 refutes F19 | the exposure axis moves too, and the re-render grows | it is Step 2 for exactly this reason — the decision is made before anything expensive is planned |
| Step 7's diff shows `D18`/`D19`/`D20` moved | F32's confirmation arm loses comparability with wave 5 | Step 7 preserves their old frames before rendering; F32's entry records which frame set its arm must use |
| `W_detect*`'s analytic twin disagrees with the runtime `W_detect` | the bank asserts a target the product cannot hit | Step 4's unit tests plus Step 8's A1/A3 pass-count rule catch it as an assertion regression, not as a σ drift |
| Arm S wins on σ but costs lever arm on runs that never recover | a conditional path shrinks every unconditional sweep | the 5 % margin in rule 2 is the price S has to pay to win |
| Step 6's two arms are not comparable because binning changes the baseline | the F41 check cannot be applied in its usual form | stated in Step 6: the control becomes arm-vs-itself reproducibility on one dataset |
