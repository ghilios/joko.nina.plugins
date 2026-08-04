# Synthetic AF bank — followups wave 4

Plan: [`plans/synthetic-af-bank-followups-wave4-plan.md`](../plans/synthetic-af-bank-followups-wave4-plan.md).
Wave 3: [`docs/synthetic-af-bank-followups-wave3-results.md`](synthetic-af-bank-followups-wave3-results.md).
Register: [`docs/followups.md`](followups.md).

*The question: decide F33's gate on evidence, then bound what the objective may spend — having first found that
the objective is not mis-scoring at all.*

## Headline

| what | result |
|---|---|
| F33 part 2 — the gate | **Decided: grow it.** Three spec rows, no generator code. The blocker was headroom above the hard floor, not the render |
| …and the mechanism is not what F33 assumed | The detector sheds at **any** density; density decides whether the optimizer can **afford** to |
| F20 part 1 — "report it" | **Done.** And the blocker the entry named was not the blocker |
| F38 (new) — the seed trigger compares two pixel spaces | **Found and fixed.** Latent headless, **active in the wizard**. No F35 number affected |
| F39 (new) — the harness records a binning the run never used | **Filed.** The derivation has never run on any dataset; 7 datasets have never run at their expected factor |
| F32 — bound the trade | **Designed, not shipped.** It is blocked on the new datasets, which is why it was ordered after the gate |
| The `Bin2` scare | **Killed by reading.** Inert on the headless path; every run used binning 1 |

## The premise that cost nothing to kill

Every `harness_settings.json` in the synthetic bank says `"DetectionBinning": "Bin2"` while every real run says
`Bin1`. That is a systematic difference between the two banks in a parameter nobody controlled for, and it had a
ready-made mechanism: binning halves star size *in the space the gates operate in*, which would push the
synthetic bank against the `NHard = 3` floor and make shedding impossible — F33's entire divergence, explained.

**It is wrong, and reading settled it in minutes.** Nothing reads that value back into `StarDetectorParams`.
`BuildStarDetectorParams` / `BuildDefaultStarDetectorParams` never map it; only `ApplyDetectionImageContext`
does, and the headless runners never call it. `OptimizationDiagnosticRunner.cs:443` calls
`HarnessSettingsStore.ResolveForRun(...)` and **discards the result** — a pure write side-effect. Every
`optimize --per-run` and every `golden eval` over both banks ran at the default of **1**.

**No wave-1/2/3 measurement is invalidated.** Two real defects fell out anyway ([F39](followups.md)), and the
hypothesis died for the price of a grep instead of a re-baseline.

## F33 part 2 — the gate, and why the entry's mechanism was wrong

F33 reads as though the synthetic bank's *render* is what fails to reproduce real-bank shedding. Measured with
`golden eval` at ~40 s per arm, that is not it. The detector's response to the acceptance gate is much the same
at any density:

| dataset | recall@high, Sensitivity 10 | 20 | 35 | 50 | high-tier stars left at 50 |
|---|---|---|---|---|---|
| `D04_esprit_550mm` (dense) | 0.762 | 0.591 | 0.308 | **0.208** | **4591** |
| `D06_sparse_1000mm` | 0.964 | 0.952 | 0.683 | 0.521 | 87 |
| `D16_esprit550_ha3` (sparse) | 0.821 | 0.652 | 0.288 | **0.130** | **24** across 9 frames |

Precision is **1.000 and FP is 0 on all twelve arms** — everything shed is a real star.

**What density changes is not whether the detector will shed but whether the optimizer can afford to.** D04 keeps
thousands of stars at the top of the range and still clears `NHard = 3` on every frame, so the search can climb
to a shedding operating point and score it. D16 falls to ~2.7 stars/frame, `J` goes to exactly 0, and the search
is repelled before it gets there. The synthetic bank's median min-frame detection count is **7** against the real
bank's **37**; only 2 of 17 datasets clear 100, against 6 of 19 real runs.

So the missing ingredient is **headroom above the hard floor** — and that is a spec-level property. Density comes
from `limitingMagnitude` and pointing against the real Gaia/ASTAP catalog, so the class costs a JSON edit, not
generator code.

### What shipped, and the control that was designed first

| row | field / limiting mag | on-frame stars | job |
|---|---|---|---|
| `D18_m24_deep_shed` | M24, 15.5 | 27084 | shed |
| `D19_cygnus_deep_shed` | Cygnus, 16.0 | 4914 | shed, on a different optic and field |
| `D20_m24_bright_control` | M24, **12.0** | 1151 | **not** shed |

D20 is D18's rig and field made bright-dominated: dense enough to keep the headroom, but with no faint
near-threshold tail. It isolates the faint tail from mere density — if D20 behaves like D18, the faint-tail
mechanism is refuted and density alone explains the regime.

**The control validated itself before any detector ran.** Golden membership across the sweep:

- `D19` (shedder): 452 → 697 → 1270 → 2323 → **3199** → 2323 → 1270 → 697 → 452 — a **7×** swing
- `D20` (control): 1081 → 1106 → 1125 → 1134 → **1137** → 1134 → 1125 → 1106 → 1081 — **1.05×**, flat

That is exactly the property each was designed to have or lack: on D20 essentially every truth star is detectable
at every focus position, so the sampled population cannot change with defocus.

### The cheap instrument could not test the control, and saying so is the result

The natural next move was to sweep the gate on the new rows the way §F33 did on D04. It does not answer the
question:

| dataset | recall@high s=10 | s=50 | Δ | precision |
|---|---|---|---|---|
| `D18_m24_deep_shed` | 0.736 | 0.203 | −0.533 | 1.000 |
| `D19_cygnus_deep_shed` | 0.983 | 0.426 | −0.557 | 1.000 |
| **`D20_m24_bright_control`** | 0.946 | 0.584 | **−0.362** | 1.000 |

**The control sheds too.** Less than the two shedders, but not by a margin that separates them — because a
*forced* Sensitivity of 50 removes faint stars on any field, tail or no tail. This measures whether the detector
**can** shed. The acceptance criterion is about whether the optimizer **chooses** to, which depends on whether
shedding buys a tighter σ_focus, and only an `optimize` pass answers that.

So the criterion is **not yet evaluated**: the rows are generated and carry the required headroom, and the
verdict needs the optimizer arm. Run on the three new datasets **only**, deliberately — per
[F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings), `optimize --per-run` rewrites
settings back into the run folders, and re-baselining the existing 17 as a side effect of testing three new ones
would be exactly the mistake wave 3 recorded.

## F32 — what the recon changed, and why nothing shipped

F32 reads as "the optimizer trades enormous recall for numerically trivial gains." Arithmetically true in `J`
units; substantively misleading, and the misreading points at the wrong fix.

**The optimizer is behaving rationally.** On the real bank σ_focus improves on **10 of 10** shedders (median
ratio **0.320**, a 68% reduction) and R² improves on **9 of 10**. `mccomiskey` goes σ_focus 3.081 → 0.460 and R²
0.9885 → 0.9986. It is genuinely buying a much better fit with the stars it discards.

**And the trade is 5.7:1 by construction.** `SStars` (weight 0.1867) hard-clamps at `nMin ≥ 8` / `nMedian ≥ 20`
(`OptimizationObjective.cs:394-403`) — a *starvation detector*, not a star-count reward. The only unsaturating
star term is the tie-breaker's `0.6·nMean/(nMean+20)` at `Wtie = 0.02`, worth at most **0.012 of `J`** across the
entire range from zero stars to infinite stars. A 10% σ tightening at ρ=0.05 buys **+0.0037**; halving the star
count 300 → 150 costs **−0.00066**.

So **the defect is not that `J` mis-scores. It is that nothing bounds what `J` is allowed to spend** — which
changes the fix from a term to a constraint. Two corollaries worth recording:

- **Rescaling `J` is a no-op for the search.** A monotone transform cannot change an argmax, so re-anchoring
  would leave every landing identical. It buys human legibility, not one different decision.
- **`Wtie` has already lost this fight once.** `OptimizationObjective.cs:60-68` records it being raised
  1e-3 → 0.02 *specifically to out-vote the star-shedding corner*, calibrated against a plateau σ-wiggle of
  ≲4e-3. The real shedders' σ gains are far larger, and F32 measures it being out-voted anyway.

**Nothing shipped, deliberately.** The chosen fix — reject a landing that sheds more than a bounded fraction of
baseline detections, as an acceptance constraint rather than a term — must be scored on **both** banks per F33,
and the synthetic half of that is the class generated above. Shipping an unvalidated objective constraint would
break the rule this wave exists to honour.

## F20 part 1 — done, and the stated blocker was not the blocker

F20 said `TooLowHFR` is one of four `RejectionGate` constants with no `*Bounds` rect list, "so it is not
reachable at the seam that already reads `LowSensitivity`/`TooFlat`."

**The `*Bounds` framing is a red herring.** That seam (`RunEvaluationLoader.cs:334-335`) reads **int properties** —
`Metrics?.LowSensitivity ?? 0` — and `StarDetectorMetrics.TooLowHFR` is already exactly such an int
(`IStarDetector.cs:755`), already incremented unconditionally (`StarDetector.cs:1895`), already merged across
threads, and **already rendered in `AutoFocus/DataTemplates.xaml:1062-1074` as "HFR Too Low"**. Whether a gate
owns a bounds list has nothing to do with reachability there. The project invariant for new
`StarDetectorMetrics` fields was **already satisfied**; the real work was the optimizer-side hop, four edit sites
across three files.

The message is a sibling of `LowSignalChartNote`, not part of the "Star signal" block — that block's contract is
scoped to the *Sensitivity* gate (`VM:227-248`), and this is a different gate with a different remedy. It
triggers off the shared `MinHfrSeed.IsBelowGate`, so the copy and the seeding decision cannot drift apart.

## F38 — the seed trigger compares two pixel spaces

Found while checking F20's trigger *before* reusing it for user-facing copy. `MinHfrSeed.Resolve` compared a fit
vertex in **captured** pixels against a `MinHFR` gate in **binned** ones: the gate fires inside the binned raster
(`StarDetector.cs:1894`) while every reported HFR has already been rescaled at `:805-808`.

The class doc defended the wrong operand — "the whole pipeline runs in binned pixels" is about `MinHFR`, and that
same comment's next clause says outputs are rescaled "so callers never see binned units."

**Missed seeds only, never spurious.** At `MinHFR = 1.2` and binning 2 the silent window is a captured vertex in
(1.2, 2.4]: the near-focus frames really are being emptied, but the wings still fit, so the rule takes the
*"clears the gate — the D05 case"* branch on a rig that emphatically does not, and prints nothing.

**No F35 result is affected** — TestApp never sets `DetectionBinning`, so the whole bank ran at 1. It is the
**wizard** that stamps the user's real factor, i.e. the bug bit the product on exactly the undersampled
population F20/F35 exist to rescue.

## Lessons

**1. Read the thing before scheduling a run against it — again, and it paid the most this wave.** The `Bin2`
hypothesis was a complete, mechanistically plausible explanation of F33 that would have justified a full
re-baseline of both banks. One grep for who reads the field killed it.

**2. A regression-test *set* can pass either way, not just a single test.** Wave 3's rule was "a regression test
that passes either way is worth nothing." The first version of F38's binning coverage was five tests of which
exactly **one** failed when the fix was reverted; the other four asserted behaviour identical with and without
it. Count the tests that discriminate, not the tests you wrote.

**3. An entry's stated blocker is a claim, not a constraint.** F20 named a specific structural reason its fix was
expensive (`TooLowHFR` has no `*Bounds` list). The reason was real and the conclusion did not follow — the seam
never used bounds lists. Two waves deferred part 1 partly on that sentence.

**4. The cheap instrument answers the question it measures, not the question you asked.** The 40 s gate sweep
established that a dense field sheds. It cannot test the control, because forcing a gate is not the same
experiment as letting the optimizer choose one. Noticing that before reading a verdict into −0.362 is the whole
value of stating the acceptance criterion in advance.

## Verification

- Full suite **3387/3387, exit 0** (baseline at branch point: 3371/3371). All 16 new tests are new coverage.
- F38's fix was reverted and the suite re-run: **4 discriminating failures**, then restored.
- `synth-bank --verify` passed on all three new datasets (`PixelSize`/`FocalLength`/`BinX`/`ExposureTime`).
- Per [F37](followups.md), a red CI check is checked against the native test-host crash before being read as a
  regression, and nothing merges on red.

## Reproduce

```
# the cheap instrument (~40 s/arm) -- density and gate response
TestApp golden eval --runs "D:\SyntheticAutofocusBank\<DS>" --params default \
    --sensitivity <s> --match centroid --out <dir>

# generate the new rows (dry-run first; ~2 min for the three)
TestApp synth-bank --spec <repo>\Joko.NINA.Plugins\TestApp\SynthBank\synthetic-bank-spec.json \
    --out "D:\SyntheticAutofocusBank" --datasets D18_m24_deep_shed,D19_cygnus_deep_shed,D20_m24_bright_control \
    --verify

# the acceptance arm -- NEW DATASETS ONLY, so F15 does not re-baseline the existing 17
TestApp optimize --per-run --runs "D:\SyntheticAutofocusBank\<new DS>" --out "D:\hf_w4\optA\<new DS>" --max-evals 250
```
