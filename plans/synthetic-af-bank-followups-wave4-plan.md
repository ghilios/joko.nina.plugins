# Synthetic AF bank — followups wave 4 (plan)

Wave 3: [`docs/synthetic-af-bank-followups-wave3-results.md`](../docs/synthetic-af-bank-followups-wave3-results.md).
Register: [`docs/followups.md`](../docs/followups.md) — F33, F32, F20, F24, F4, F26.
Branch: `ghilios/synth-bank-followups-wave4` (renamed from `ghilios/f37-ci-testhost-crash`; carries the F37 entry,
which is also open as PR #172).

*The question: decide the F33 gate on evidence rather than argument, then bound what the objective is allowed to
spend — having first established that the objective is not mis-scoring at all.*

---

## Four premises checked before any run was scheduled

Wave 3's lesson was "read the thing before scheduling a run against it." Applied again, it killed one hypothesis
outright and turned two others into register entries. None of this cost a bank pass.

### P1 — the objective is behaving RATIONALLY, and F32's framing hides that

F32 reads as "the optimizer trades enormous recall for numerically trivial gains." Arithmetically true in `J`
units; substantively misleading. On the real bank, **σ_focus improves on 10 of 10 shedders** (median ratio
**0.320**, a 68% reduction in focus-position uncertainty) and **R² improves on 9 of 10**. `mccomiskey` goes
σ_focus 3.081 → 0.460 and R² 0.9885 → 0.9986.

The trade is 5.7:1 in favour of shedding *by construction*, and the arithmetic reproduces exactly from the code:
a 10% σ tightening at ρ=0.05 buys ΔJ ≈ **+0.0037**, while halving the mean star count 300 → 150 costs
**−0.00066** — because `SStars` (weight 0.1867) hard-clamps at `nMin ≥ 8` / `nMedian ≥ 20`
(`OptimizationObjective.cs:394-403`, `NFloor = 8` / `NTarget = 20` at `:37-38`) and is therefore a *starvation
detector*, not a star-count reward. The only unsaturating star term is the tie-breaker's
`0.6 · nMean/(nMean+20)` at `Wtie = 0.02` — at most **0.012 of `J`** across the entire range from zero stars to
infinite stars.

**So the defect is not that `J` mis-scores. It is that nothing bounds what `J` is allowed to spend.** That
changes the fix from "add/rescale a term" to "add a constraint", and it is why this plan does not touch the
objective's terms at all.

Corollary worth stating because it was on the menu: **a monotone rescaling of `J` cannot change its argmax**, so
re-anchoring it would leave every landing identical. It buys human legibility, not a single different decision.

### P2 — `Wtie` has already lost this fight once

`OptimizationObjective.cs:60-68` records that `Wtie` was raised **1e-3 → 0.02 specifically to out-vote the
star-shedding corner** (`bobp_m101`, Sensitivity 50 / StarClip 9.5, recall 0.13), calibrated to beat a plateau
σ-wiggle of ≲4e-3. F32 measures it being out-voted anyway — the real shedders' σ gains are far larger than the
4e-3 it was sized against. Raising it again is retuning a knob that has already failed at this exact task.

### P3 — the `Bin2` scare is real as a defect and dead as an explanation

Every `harness_settings.json` in the synthetic bank says `"DetectionBinning": "Bin2"` while every real run says
`Bin1`. That looked like it might explain the whole F33 divergence — binning halves star size in the space the
gates operate in, which would push the synthetic bank against the `NHard = 3` floor and make shedding impossible.

**It does not, because the value is inert on the headless path.** Nothing reads `harness_settings.json`'s
`DetectionBinning` back into `StarDetectorParams`: `BuildStarDetectorParams` / `BuildDefaultStarDetectorParams`
never map it, only `ApplyDetectionImageContext` does (`HocusFocusStarDetection.cs:573-574`), and the headless
runners never call it. `OptimizationDiagnosticRunner.cs:443` calls `HarnessSettingsStore.ResolveForRun` and
**discards the result** — it is a pure write side-effect. Every `optimize --per-run` and every `golden eval` ran
at the `StarDetectorParams` default of **1**.

**No wave-1/2/3 measurement is invalidated.** Two real defects fall out anyway, filed as F39.

### P4 — F20's stated blocker is not the blocker

F20 says `TooLowHFR` "is one of the four `RejectionGate` constants with no `*Bounds` rect list, so it is not
reachable at the seam that already reads `LowSensitivity`/`TooFlat`." The `*Bounds` framing is a red herring:
that seam (`RunEvaluationLoader.cs:334-335`) reads **int properties** — `Metrics?.LowSensitivity ?? 0` — and
`StarDetectorMetrics.TooLowHFR` is already exactly such an int (`IStarDetector.cs:755`), already incremented
unconditionally (`StarDetector.cs:1895`), already merged across threads (`IStarDetector.cs:832`), and **already
rendered in `AutoFocus/DataTemplates.xaml:1062-1074` as "HFR Too Low"**.

So the project invariant about new `StarDetectorMetrics` fields **is already satisfied** and no XAML change is
needed for the counter. What is genuinely missing is the optimizer-side hop, which is smaller than the entry
implies.

---

## Work

### W1 — F33 part 2: grow the shedding class (THE GATE, decided: yes)

**Decided on evidence, not argument.** The mechanism already transfers: `D04_esprit_550mm` — the only dense
synthetic dataset, **1178 stars in its sparsest frame** — behaves like a real shedder under config B (lands
Sensitivity 17.67, keeps **62.7%** of detections, Δrecall **−0.113**, trade rate **−44.57**, squarely inside the
real bank's shedder range). What the bank lacks is not the mechanism but the *population*:

| bank / arm | median min-frame detections at C0 | datasets with ≥100 |
|---|---|---|
| real A | **37** | 6 / 19 |
| synthetic A | **7** | 2 / 17 |

Sparse datasets sit against `NHard = 3` and the `SStars` knees, so the optimizer **cannot** shed without zeroing
`J`. That is why the synthetic median trade rate is 0.00 while the real one is −17.3 — not a property of the
render.

**The decisive argument for spending anything here:** only the synthetic bank knows the **true optimal focuser
position** (`optimalFocuserPosition` is a spec input). So only it can answer whether shedding bought *real focus
accuracy* or merely a smaller **self-reported** σ from a fit with fewer, better-behaved points. That question is
the crux of F32 and F4 and is currently unanswerable on either bank — the real bank has no truth, and the
synthetic bank has no shedding.

**Cost: a JSON edit, not generator code.** Density is set by `limitingMagnitude` + `raDegrees`/`decDegrees`
against a real Gaia/ASTAP catalog (`SynthDatasetSpec`, `SynthBankSpec.cs:78-135`; catalog read at
`AstapCatalogReader.cs:68`).

Add to `Joko.NINA.Plugins/TestApp/SynthBank/synthetic-bank-spec.json`:

- **D18 — dense/deep shedder.** Rich pointing (M24-class, as D04) at `limitingMagnitude` 16.5–17.0 on a
  wide-ish fast rig, sized so the sparsest frame carries ≳800 detections. The row whose job is to shed.
- **D19 — dense/deep shedder, second optic.** Same density target, different focal length / obstruction, so a
  finding cannot rest on one rig's geometry.
- **D20 — dense NON-shedder control.** Same density, but an operating point where shedding should not pay.
  **Design the control before the experiment and read it first** — wave 3's D05 lesson, which is the only reason
  the seed leak was caught.

**Acceptance criterion, fixed before generation so it cannot be moved afterwards.** A row earns its place only
if, at config A, it reaches **median trade rate ≤ −10** recall-points per unit `J` with **keep% < 80%**, at
**precision ≥ 0.99**. D20 must NOT meet it. If no row meets it, the class is refuted and W2's validation falls
back to the real bank alone — that outcome is a result, not a failure, and gets written up as one.

**Cheap instrument first, per the standing rule.** Before generating: `golden eval` arms (~40 s each) over D04
sweeping the Sensitivity gate, to confirm the density→shedding relationship holds at config A and not only under
config B's donut-relaxed landing. Generation is scheduled only if that check passes.

### W2 — F32: bound the trade as an acceptance constraint (decided)

Not a new term, not a rescaling. **A candidate landing is rejected if it sheds more than a bounded fraction of
the baseline's detections.** This is the fix P1 implies: leave the scoring alone, bound the spend.

- Seam: `StarDetectionOptimizer.SearchContext.EvalJ` (`StarDetectionOptimizer.cs:272-296`) is the single point
  every `bestJ`/`FinalJ` flows through, and `RunEvaluationMetrics.FrameStarCounts` is already on the metrics it
  holds. The baseline evaluation is already computed (it is what `BaselineJ` reports).
- Shape: an `OptimizerSettings` cap (default off or generous), applied as a **feasibility rejection** ahead of
  the `j > bestJ` comparison — never as a multiplier on `J`, so the objective's numbers stay comparable to every
  prior arm.
- Report it: when the cap binds, say so, so a landing that was constrained is never mistaken for one that was
  not.

**Scored on BOTH banks** (F33's rule, which binds a constraint exactly as hard as a term). The synthetic bank
alone cannot validate this — it is the whole reason W1 exists.

**Falsification requirement.** Re-run the acceptance test with the constraint reverted and confirm it fails.
Wave 3's first unit test asserted the bug and passed.

### W3 — F20 part 1: report the MinHFR gate to the user

Two halves, both small now that P4 has cleared the framing.

**(a) The counter** — mirror the `LowSensitivity`/`TooFlat` plumbing exactly, 4 edit sites across 3 files. No
`StarDetectorMetrics` change, no `AutoFocus/DataTemplates.xaml` change.

1. `int TooLowHFRCount` on `FrameDetectionResult` (`RunEvaluationData.cs`, beside `:86`)
2. populate in `GateAndMeasure` (`RunEvaluationLoader.cs:335`) from `Metrics?.TooLowHFR ?? 0`
3. `FrameTooLowHFRCounts` on `RunEvaluationMetrics` (`OptimizationObjective.cs`, beside `:303`)
4. accumulate + assign (`RunEvaluationData.cs:656 / 670 / 793`)

Keep it **inert in the objective**, like its two siblings — this wave adds no scoring behaviour here.

**(b) The message** — the wizard currently computes the identical seed and says nothing
(`StarDetectionOptimizerWizardVM.cs:3014-3016`: the return of `MinHfrSeed.Resolve` goes straight into
`optimizerSettings` and never reaches `OptimizationSummary`).

- Precedent to copy: `LowSignalChartNote` (`VM:1944-1948`) → the italic `NotificationWarningBrush` TextBlock
  above the curve (`Optimization/DataTemplates.xaml:730-737`). It is deliberately above the fold because Accept
  lives outside the `ScrollViewer`.
- **Do NOT fold this into the "Star signal" block.** Its doc comment (`VM:227-248`) scopes it to the
  *Sensitivity* gate; an HFR-gate finding there would violate its stated contract.
- Trigger: reuse `MinHfrSeed`'s own trigger so copy and seed can never disagree. `MeasuredInFocusHfr` is already
  on the summary (`VM:3145/3196`); add `VariantMinHfr`/`BaselineMinHfr` following the `VariantSensitivity`
  template (`VM:3206`), and the matching line in `BuildCurrentSummary` (`VM:3221-3250`).
- Name the rig, per the entry: pixel scale / focal length are on the summary already.

### W4 — F38: the MinHFR seed trigger compares two different pixel spaces

Found this wave, upheld by three independent adversarial adjudicators. **Fix it here because W3 reuses the same
trigger** — shipping the message on a broken trigger would propagate the bug into user-facing copy.

`MinHfrSeed.Resolve(fitVertexHfr, currentMinHfr)` compares a **captured-pixel** vertex against a
**binned-pixel** gate. The gate fires at `StarDetector.cs:1894` inside the binned raster; HFRs are rescaled to
source pixels afterwards at `:805-808` (`ScaleToSourcePixels` multiplies HFR by the factor,
`CvImageUtility.cs:818`), and that rescaled value is what reaches `BestFit.Minimum.Y`.

- Direction: **missed seeds only, never spurious** — `{h ≤ G} ⊂ {h ≤ N·G}`. Sound but incomplete.
- Window at the shipped `MinHFR = 1.2`, `N = 2`: captured vertex in **(1.2, 2.4]** px, i.e. a rig whose stars
  really are under the gate, silently taking the "leave it alone (the D05 case)" branch.
- Reach: **latent headless** (TestApp is always `N = 1`, so no F35 result is invalidated), **active in the
  wizard**, which stamps the user's real profile/per-filter factor via `ApplyDetectionImageContext`.
- Fix: take the factor explicitly so the mixed-unit compare cannot recur silently —
  `Resolve(fitVertexHfr, currentMinHfr, detectionBinning)` comparing `fitVertexHfr / N <= currentMinHfr`.
  The seeded **value** is already correct: `SeedFloor = 0.30` is binned and is written to a binned param.
- `MinHfrSeedTests` has **zero binning coverage**, which is why it never surfaced. Add it, and confirm the new
  test fails without the fix.

### W5 — F39: the harness records a detection binning the run never applied

`HarnessSettingsStore.ResolveForRun` derives `DetectionBinning` from `RecommendFromHfr(inFocusHfrPixels)` only
when the run has an `autofocus_report_Region0.json` with a fitted minimum. **No synthetic bank folder has one**,
so all 17 fell to "kept from base" and inherited `Bin2` from a profile export. Two consequences:

1. **Provenance is wrong.** The file records `Bin2` for runs executed at 1.
2. **Seven datasets have never run at the binning their own truth model specifies.** `expectedOptimal.detectionBinning = 2`
   for D08, D09, D10, D12, D14, D15, D17 — and D08's spec description calls it "the first detectionBinning=2
   dataset." That axis of the bank has never been exercised.

Also circular on the datasets that matter most: the derivation needs a fitted in-focus HFR, and D01/D02 have
none *because the MinHFR gate zeroed the curve* — the very defect F20/F35 are about.

**Flag, do not fix inline.** Making the headless path honour a per-run binning changes every synthetic number
and would re-baseline the bank mid-wave. It gets its own entry and its own wave.

### W6 — queued behind W1/W2

F24's remaining step (default the master's +2 structure boost and 5 px morph-close to neutral on runs the donut
heuristic did not flag), F4 (sensor-model term), F26's unverified precision clause at `/5`. Only started if
W1–W5 land with time to score them on both banks.

---

## Runs

Nothing long is scheduled until the cheap instrument has been read.

```
# cheap, ~40 s per arm — the density -> shedding check that gates W1's generation
TestApp golden eval --runs "D:\SyntheticAutofocusBank\D04_esprit_550mm" --params default \
    --match centroid --out <dir>

# long, only after the above: bank passes, detached, progress file, exe locked while running
TestApp optimize --per-run --runs "D:\SyntheticAutofocusBank" --out <dir> --max-evals 250   # ~55 min
TestApp optimize --per-run --runs "D:\Autofocus Bank"        --out <dir> --max-evals 250   # ~3.5-4 h
```

Control arms not to clobber: `D:\hf_f23\H_A` (synthetic), `D:\hf_f23\H_real_A` (real).
Wave-4 arms: `D:\hf_w4\...`. Build to a separate `-o` directory — the exe is file-locked while running.
Per **F15**, `optimize --per-run` rewrites settings *in the run folders*; read the `--out` copies, and say so.

## Verification

- Full suite green before and after. Baseline at HEAD confirmed this session: **3371/3371, exit 0**.
- Every regression test re-run with its fix reverted, confirmed failing. Non-negotiable after wave 3.
- Per **F37**, a red CI check is checked against the native test-host crash *before* being read as a regression —
  and nothing merges on red.
