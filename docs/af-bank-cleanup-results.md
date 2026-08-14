# The synthetic-AF-bank cleanup — a SHIP run, and the end of the thread

**Run window.** `T0 = 2026-08-14T00:00:31Z`, stop `T0 + 6 h = 2026-08-14T06:00:31Z` (the invoking message named no
duration). PR #196 verified `MERGED` at `2026-08-13T22:56:58Z` before anything else ran.

**Suite baseline, by COUNT out of the log (F37), re-established on `develop @ 2ebbd43` after the merge:**

```
Passed!  - Failed: 0, Passed: 4039, Skipped: 0, Total: 4039, Duration: 5 m 52 s
```

**4039 survived the merge**, so 4039 is this run's baseline and every count below is relative to it.

**Final suite: 4052 = 4039 + 3 + 10, zero failures, zero skips.** CI agrees, read out of the run log rather
than off the tick — `gh run view 31761696772 --log` →
`Passed! - Failed: 0, Passed: 4052, Skipped: 0, Total: 4052`, run conclusion **success**.

**Branch:** `ghilios/af-bank-cleanup`, one branch and one PR (**#200**), base `2ebbd43`.

---

## THE HEADLINE

**`V-0` fired, and item 2 is VOID — the pre-registration did exactly the job it was written for.** The F82 fix
`(3′)` cannot change a single recommended step on `D01` in the configuration the product actually runs, and this
was established *before* any code was written, at a cost of 26 minutes of bank time instead of the 2 h 25 m –
3 h 10 m the decision document priced.

It is stronger than the gate required. `V-0` asked about `D01`. Measured on all three published cells, in the
product's configuration, `(3′)` is inert on every one of them — twice because F18's detectability bound clamps
far below `(3′)`'s floor, and once because the round already sits exactly on that floor.

---

## Item 1 — `V-0`, the validity gate. **FIRES. Item 2 is VOID.**

### The check, first

[F110](followups.md)'s claim verified before acting on it:

```
$ grep -c 'maxUsefulHalfSpan.*NaN' /mnt/d/hf_w25/after/D01_ultrawide_40mm__S1/synth_validate_report.json
2
```

Both of `D01`'s published rounds carry `maxUsefulHalfSpan: NaN`. The harness builds `stepDetectability` only
when `--step-detect-bound` or `--step-size-for-executed-sweep` was passed (`SynthValidateRunner.cs:783-793`,
read at `HEAD`), and wave 25's S1 arm passed neither.

### The arm

Run on **B15** (`/mnt/d/hf_w25/exe`) — no build. DLL hashes asserted against `binary_provenance_w25.txt` before
the loop, and they matched exactly (`TestApp.dll 2fb0c8fd…`, `NINA.Joko.Plugins.HocusFocus.dll 3fd1fc4b…`);
`TestApp.exe` itself was **not** hashed as the decision criterion (F66 — the apphost is byte-identical across
seven distinct binaries). Driver: `/mnt/d/hf_ship1/v0_arm.sh`, log `/mnt/d/hf_ship1/v0_arm.log`,
`V0_ARM_START … V0_ARM_END … manifest rows: 6 of 6 scheduled`, one `TestApp.exe` at a time.

**Both arms were run on the same binary** — `off` (no flag) and `on` (`--step-detect-bound`) — so that any
difference is attributable to the flag rather than to a re-run. The `off` arm **reproduces wave 25's published
`D01` report bit-identically**: `halfWidth` 12.0 / 9.0, steps 3 / 3, `sampledHfrRange` matching to all 16
printed digits. That is what licenses reading the `on`/`off` delta as the flag's effect.

### The result

`(3′)`'s floor is `MaxHalfWidthSampledHalfSpanMultiple × 0.5 × requestedSpan = 1.5 × 0.5 × (2·offsetSteps·stepSize)`.

| cell | round | requested span | `(3′)` floor | detect bound ON | `halfWidth` ON | step ON | step OFF |
|---|---|---|---|---|---|---|---|
| `D01` | r1 | 24.0 | **18.00** | **yes**, `maxUsefulHalfSpan = 7.789` | **7.789** | **2** | 3 |
| `D02` | r1 | 24.0 | **18.00** | **yes**, `maxUsefulHalfSpan = 15.130` | **15.130** | **4** | 5 |
| `D03` | r1 | 56.0 | **42.00** | no (`NaN`) | 42.0 | 12 | 12 |

**`D01` r1 — the exact round `(3′)` was designed to move from step 3 to step 5 — is detect-bounded to 7.789,
which is 10.2 below `(3′)`'s floor of 18.0.**

### Why that is decisive, from the source rather than by inference

`StepSizeRecommender.Recommend`, read at `HEAD` (`:320-379`), applies its bounds in this order:

1. `maxHalfWidth = MaxHalfWidthSampledHalfSpanMultiple × 0.5 × searchSpan` (`:320`);
2. the **cap** — clamp down to `maxHalfWidth` (`:346-350`);
3. the **`BandDemonstrablyUnsampled` floor** — raise up to `maxHalfWidth` (`:363-366`);
4. **F18's detectability bound** (`:368-379`), whose comment reads *"Only ever tightens"*:
   `detectHalfWidth = Math.Max(maxUsefulHalfSpan, minHalfWidth)`, and `if (detectHalfWidth < halfWidth)` then
   `halfWidth = detectHalfWidth`.

`(3′)` inserts `maxHalfWidth = Math.Max(maxHalfWidth, 1.5 × 0.5 × requestedSpan)` at step 1. On `D01` r1 that
raises `maxHalfWidth` to 18.0 and step 2 or 3 sets `halfWidth = 18.0` — and then **step 4 clamps it straight
back to 7.789**, because `(3′)` does not touch `searchSpan` and therefore does not move `minHalfWidth`
(`0.5 × 0.5 × 12.0 = 3.0`, and `max(7.789, 3.0) = 7.789` either way). `round(7.789 / 3.5) = 2` — the step the
run already produces. **`(3′)` changes zero recommended steps on `D01` in the product's configuration.**

This is precisely the pre-registered void condition, decision document §6 row `V-0` and §7 clause 1: *"if the
detect bound already clamps `D01` below the requested-span floor, `(3′)` cannot fix `D01` in the product and the
finding is an F18-vs-F81 conflict, not F82. Then this decision is void and the entry is re-scoped."*

### Two things the arm found that nobody asked it for

- **The product's configuration does not reproduce F82's stall at all.** With the detect bound supplied, `D01`
  does not go 3 → 3; it goes **3 → 2 → 3 → 3** and runs the full four rounds instead of stopping at two. The
  behaviour is an **oscillation**, not the monotone stall F82 describes. F82's published two-round table is an
  artifact of the harness configuration, which is the register delta the decision document's §8 already
  anticipated ("F82's published evidence was produced in a harness configuration the product does not run") —
  now measured rather than argued.
- **The detect bound moves the step in the opposite direction from `(3′)`.** On `D01` and `D02` it *tightens*
  (3 → 2 and 5 → 4). Whatever the right answer for a star-poor shallow sweep is, `(3′)` was pushing against
  F18, not with it.

### What this run did **not** do, and why

`(3′)` was **not implemented**: `V-0` is a stop condition, not a warning, and the prompt's instruction on a fire
is to say so and skip to item 3. The 42 m `optimize` gate that `(3′)` would have owed was therefore never owed.
`StepSizeRecommender.cs` is **unmodified on this branch** — confirmed by the diff in the closing section.

**F82's correct entry is now "the F18-vs-F81 ordering conflict", not a requested-span bound.** The open question
it leaves is a real one and it is *not* what F82 says: on a shallow, star-poor sweep, F18's measured
detectability bound and F81's band floor disagree about which way to move, and F18 wins by being applied last.
That deserves its own decision, and it needs the blind wide-field data that §3 puts out of scope.

---

## Item 3 — the `ACCEPTED-elsewhere` residual. **RESOLVED: a matching artifact. Not a precision risk.**

Zero TestApp minutes: everything below is computed from artifacts already on disk.

### The check, first

```
$ grep -n 'ACCEPTED-elsewhere' /mnt/d/hf_w30/g/out/M{1,3,4}/attempt01/golden_eval.txt
M1: 3141 (all tiers) / 1031 (high)    M3: 3417 / 1106    M4: 3333 / 1091
```

### The mechanism, which is not what the framing assumes

The two numbers in wave 30 §4.5's table are produced by **two different predicates**, and they are not the same
strictness:

| | predicate | source |
|---|---|---|
| the **matcher**, which decides TP/FN | `centerIn(D.center, G.box) OR dist(D.center, G.center) ≤ 12 px`, then greedy 1:1 by IoU desc / distance asc | `GoldenGeometry.cs:202-256`, mode `Center` |
| the **FN attribution**, which prints `ACCEPTED-elsewhere` | `IoU(G.box, D.box) > 0.0` — **any** overlap, however small | `BoxMatcher.cs:96-150` via `GoldenEvalRunner.cs:355-360` |

So a golden star can be labelled `ACCEPTED-elsewhere` while being unmatchable to every accepted detection on the
frame. That is a third possibility the binary question omits, and it is decidable on disk.

### The measurement, with its self-test stated first

`/mnt/d/hf_ship1/item3_accepted_elsewhere.py` rebuilds the matcher offline from `golden.json` +
`detected_f*.csv` and **reproduces the run's own per-frame `TP`, `FN` and `accepted` counts exactly on every
frame of all seven cells**, and the published `ACCEPTED-elsewhere` totals exactly (1740, 3141, 3333, 3417, 1869,
1769, 1740). Without that the rest would be worthless, so it runs first and exits non-zero on a mismatch.

| cell | gate | accepted | AE | unmatchable (graze) | competing | `d(G,G′) ≤ 12 px` | `> 12 px` |
|---|---|---|---|---|---|---|---|
| `M0` | baseline, `MaxDistortion` 0.5 | 20 831 | 1 740 | 55 (3.2 %) | 1 685 | 1 660 (**98.5 %**) | 25 (1.5 %) |
| `M1` | `MaxDistortion` 0.3 | 22 756 | 3 141 | 169 (5.4 %) | 2 972 | 2 790 (**93.9 %**) | 182 (6.1 %) |
| `M4` | `MaxDistortion` 0.2 | 22 933 | 3 333 | 228 (6.8 %) | 3 105 | 2 895 (**93.2 %**) | 210 (6.8 %) |
| `M2` | `Sensitivity` 32.333 | 23 635 | 1 869 | 56 (3.0 %) | 1 813 | 1 787 (98.6 %) | 26 (1.4 %) |
| `L3` | `Sensitivity` 35.333 | 21 505 | 1 769 | 55 (3.1 %) | 1 714 | 1 688 (98.5 %) | 26 (1.5 %) |

`d(G,G′)` is the distance from the `ACCEPTED-elsewhere` golden star to **the golden star that actually won its
nearest qualifying detection**. The median **winning detection box** contains **exactly 2.0 golden centres**
(the median is over those winning boxes, not over all accepted detections).

**My "instrumentation artifact" hypothesis was mostly wrong and is recorded as such:** only 3–7 % are grazes.
The dominant mechanism is the *other* one.

### The answer

**It is real stars matched to a neighbour, and it is harmless to precision.** In 93–99 % of the competing cases
the `ACCEPTED-elsewhere` star and the star that won its detection are **themselves closer together than the
match radius** (median separation 7–8 px against a 12 px radius, and a median of 2.0 golden centres inside the
winning detection box). A
greedy 1:1 matcher **cannot** pair both, whatever the detector does: the loser is a false negative by
construction of the *scorer*, not by any behaviour of the *detector*.

The distortion-linked component is real but bounded, and it is the same mechanism at a wider scale, not a
different one. The `d(G,G′) > 12 px` bucket rises 4× from the baseline and sensitivity arms (1.4–1.5 %) to the
distortion arms (6.1–6.8 %) — 25 → 210 stars. Characterised
(`/mnt/d/hf_ship1/item3c_far.py`): **100 % of those cases have `d(G,G′) ≤ 2R = 24 px`** (max 22.8 px at `M4`,
14.6 px at `M0`), with **no tail**, and the winning detections are larger than typical (median box area 513 px²
against 182 px² for all accepted). That is one relaxed-gate blob spanning a close pair — blending — which costs
**recall**, cannot cost precision, and is bounded at twice the match radius by construction.

Consistent with the direct measurement wave 30 already had and did not connect to it: precision is **1.000 with
`FP = 0`** at `MaxDistortion` 0.5 / 0.3 / 0.2 (`/mnt/d/hf_w30/row7_precision_rescore.txt`).

**Wave 30 §10 row 7 — "precision at the opened `MaxDistortion` settings", `OWED by any ship that lowers
MaxDistortion` — is discharged.** The residual it was created for is a scorer-side 1:1 matching consequence of
golden crowding below the match radius. It is not evidence of a precision risk, and there is no precision
number left to go and get.

---

## Item 4 — bound and rename the `MaxDistortion` axis

### The check, first

```
$ grep -n 'fillRatio' …/StarDetection/StarDetector.cs
1802:  var fillRatio = (starPoints.Count + donutHoleArea) / d / d;
1804:  if (fillRatio < effectiveMaxDistortion) {   → RejectionGate.TooDistorted
```

[F98](followups.md) confirmed at `HEAD`: the gate **rejects when the fill ratio falls below the value**, so
`MaxDistortion` is a **minimum fill ratio** and raising it makes the gate *stricter*. A perfectly round star
tops out at `π/4 ≈ 0.785`.

### The bound — shipped

The axis was `Continuous(MaxDistortion, 0.1, 1.0, 0.1)` (`OptimizerVariable.cs:176`). Shipped as a named
constant with the geometry in its doc comment, matching the file's existing style — and called out as the one
bound in the curated set that is **geometric rather than heuristic**:

```csharp
public const double MaxDistortionSearchUpper = Math.PI / 4.0;
```

> **CORRECTION, made mid-run against the source, and the wrong version is printed here so the correction is
> legible.** I first justified this as a coarse-grid saving: *"the coarse grid samples `Lower + t·(Upper − Lower)`
> inclusive of both bounds at `CoarseGridLevels = 4`, so the old four levels were 0.1 / 0.4 / 0.7 / 1.0 and one
> of them was spent on a guaranteed-empty setting."* **That is false. `MaxDistortion` is never coarse-gridded.**
> `StarDetectionOptimizer`'s Phase A grids **only** `Sensitivity × StarClippingMultiplier` (`:764-789`) and holds
> every other variable at the incumbent; `GridValue` is called on exactly those two axes and nowhere else.
>
> I caught it because the false premise produced a **falsifiable prediction that the gate then refuted** — which
> is the entire argument for writing predictions down before running the instrument. It also propagated into the
> first commit message and the F98 register entry; both are corrected, and the correction says what it replaces.

**The true mechanism, narrower and still worth shipping.** `MaxDistortion` moves only under the **pattern
search**, whose proposals are clamped by `Quantize` to `[Lower, Upper]`. The old ceiling left roughly the
**top 21 % of the axis reachable** while returning zero detections for a round star by construction — the
collapse wave 29 measured directly at 0.9 (`TP=0 FP=0 FN=110384`, recall 0.000 on both `L2` and `L4`). The bound
makes that band **unreachable**: a guard against a pathological upward excursion, and therefore **expected to be
inert on any run that never walks up there**.

That reframing changes what the test should assert, so the test changed with it: instead of enumerating
coarse-grid levels that do not exist, it walks the axis in its own `InitialStep` past the old 1.0 ceiling and
asserts that **no reachable stored value** ever lands in the dead band.

### The rename — what applies, stated rather than assumed

**`MaxDistortion` is both user-visible and persisted**, so a rename of the knob itself is a settings migration:

- persisted under the literal key `"MaxDistortion"` (`StarDetectionOptions.cs:276,764`), default 0.5;
- it **already has** its control in `Resources/OptionsDataTemplates.xaml` (label + tooltip + validated binding
  at `:1573-1595`), so `CLAUDE.md`'s options-owe-XAML invariant is satisfied and no new control is owed;
- the name reaches 11 C# files, the replay snapshot, the diff, `OptimizedStarDetectionSettings`, `TestApp`, and
  ~12 references in the user manual, which documents it correctly as *"Min fill ratio of the bounding box"*.

**So I bounded the axis and renamed the label, and did not rename the persisted key.** Renaming a persisted
key under a stop clock risks silently resetting every user's tuned value, and it buys nothing a user can see
that the label does not. The label is where a user actually reads the name, and it now states the direction:

- label `"Max Distortion"` → **`"Max Distortion (min fill ratio)"`** — states the direction, keeps the
  documented name findable;
- the tooltip now leads with *"A MINIMUM fill ratio, despite the name … rejected as Too Distorted when its fill
  ratio falls BELOW this value. Raising it makes the gate stricter, not looser"*, and states the π/4 ceiling.
  It also fixes two errors it has carried: `"circule"`, and `"PI/4, which is approximately 0.7"` (it is 0.785 —
  the old text understated the ceiling by 0.085, which is most of a coarse-grid step).

**The persisted-key rename is left as a named, unpaid debt** — it is a migration, and it deserves its own
decision rather than a corner of a cleanup run.

### The bottom of the axis — unchanged, and now with a reason

Item 3 gates it, and item 3 resolved as *no precision risk*. That licenses the bottom of the axis but does not
motivate a change to it: the floor is already 0.1, wave 30 measured 0.5 / 0.3 / 0.2 at `FP = 0`, and nothing in
this run says the search is being kept out of a region it needs. **No change, deliberately.**

### Shown RED against a named mutant

`M-OLD-CEILING` — the one constant the change introduced, reverted to exactly the value it replaced
(`Math.PI / 4.0 → 1.0`). Driver: `mutant_M-OLD-CEILING.sh`; byte backup, `trap`-restore, `touch` after restore
(F89), sha256 re-verified identical afterwards.

```
MUTATED: MaxDistortionSearchUpper = Math.PI / 4.0  ->  1.0
  Failed CreateCuratedSet_BoundsAndStepsMatchSpec
  Failed MaxDistortionAxis_NoReachableValue_RejectsEveryRoundStar
  Failed MaxDistortionAxis_WriteClampsAboveTheGeometricCeiling
  Failed MaxDistortionSearchUpper_IsTheFillRatioOfARasterisedPerfectDisk
Failed!  - Failed: 4, Passed: 23, Skipped: 0, Total: 27
restored ok sha256(after) = 5770c214…c91bd06   (identical)
```

(Re-run after the correction above, against the corrected tests — the mutant kills the reachability test just as
it killed the coarse-grid one it replaced, because the constant is what both depend on.)

**Final suite, by COUNT: `Passed! - Failed: 0, Passed: 4052, Skipped: 0, Total: 4052`** = 4039 + 3 (item 4) +
10 (item 5). Zero failures, zero skips, and no test was marked expected-to-fail.

---

## Item 5 — the `A4` truth-model gap. **FIXED, harness-side, and it carries no gate.**

### The check, first

`grep -rn 'truthModel' Joko.NINA.Plugins/TestApp/SynthBank/` returns the spec's truth-model block, and `A4`
itself lives at `SynthValidateRunner.cs:930-948`. Still owed, and [F96](followups.md) makes it more urgent:
`stepBehavioral` is `StepSizeRecommender.Recommend` iterated to a fixed point (`:1213-1270`), so the harness
scores the recommender against a fixed point of itself.

### Two defects, both on the assertion side, and the product is untouched

1. **The boundary came from the wrong span.** `A4` built it as `1.5 × (offsetSteps × stepSize)` — the
   **requested** sweep. The product builds `maxHalfWidth = 1.5 × 0.5 × SearchSpan(bestFit)`, and `SearchSpan` is
   `max(x) − min(x)` over the points that **survived into the fit** — smaller whenever a position was dropped
   for a non-finite pooled HFR, held out as a recovery frame, or removed as a consensus outlier. On `D01` r1
   that is requested 24.0 against fitted 12.0: **`A4` was asserting against 18.0 while the product applied 9.0.**
2. **It asserted the cap predicate on band-floored rounds, where it does not apply.** The cap and the floor are
   mutually exclusive and bound in **opposite directions**: the cap clamps a fitted crossing *down* to
   `maxHalfWidth`, the floor raises one *up* to it when the sweep's own HFRs prove the band was never sampled.
   "Truth is above the boundary" predicts the **cap** and says nothing about the **floor** — so on `D03` r0 the
   rule FAILed (`truth 46.1 vs boundary 24.0`) a round the floor had handled exactly as F81 designed. A floored
   round is now reported **`NotApplicable`** and named, not silently dropped.

> **A correction to my own first attempt at this, made before it shipped and recorded because the record is
> better for it.** I first wrote defect 2 as *"reading `WasCapped` alone; the two branches clamp to the same
> `maxHalfWidth`, so `bounded = WasCapped || WasBandFloored`."* That is wrong, and re-scoring the real reports
> is what showed it. It does repair `D03` r0 — but it breaks the two floored rounds whose truth sits **below**
> the boundary: `D01` r0 (ratio 0.87) would drop **PASS → FLAG** and `D02` r0 (ratio 0.70) **PASS → FAIL**, on
> rounds where the recommender did nothing wrong. The outcome-level claim ("the half-width ended at
> `maxHalfWidth`") is true of both branches; the truth-level claim ("the model wanted to go further than the
> data supports") is only about the cap. Conflating them trades one wrong verdict for two. **Exemption is the
> verdict that is right in both directions**, and both directions are now pinned by tests.
>
> `SynthValidationVerdict` gains a fourth value, `NotApplicable`, appended **last** so `Pass`/`Flag`/`Fail` keep
> ordinals 0/1/2 and no existing artifact changes meaning. `Worst()` already treats anything that is not
> `Fail`/`Flag` as passing, so an exemption cannot fail a scenario; and because the report lists every non-`Pass`
> finding, the exemption is *visible* rather than a silently missing row.

`SynthValidateRunner` now records `fittedSearchSpan` (recomputed from `bestFit.Inputs`, mirroring the private
`SearchSpan` rather than exposing it), and the decision moved into `TestApp/SynthBank/CapSemantics.cs` — the
same shape as `TruthDisclosure`, because the runner renders frames and runs the optimizer and cannot be linked
into the test project.

**Backward compatibility is a property, not a hope:** reports written before `fittedSearchSpan` existed carry
`NaN` and fall back to the requested sweep, which is *exactly* the historical boundary — and on the 36 of 37
bank rounds where nothing was dropped the two spans are equal anyway, so re-scoring old artifacts cannot move a
verdict. Both are pinned by tests.

**No product file is touched**, deliberately — so this change carries **no `optimize` gate** and cannot be
confused with a behaviour change. It is committed separately from item 4 for the same reason.

### What A4 actually says on real reports, before and after — MEASURED, not predicted

The three published cells re-run on the **new binary** (`/mnt/d/hf_ship1/a4_recheck.sh`, `a4/`, TestApp.dll
`a1dca0ae…`), same pinned inputs, same no-flag configuration wave 25 published. `fittedSearchSpan` is read out
of the new reports rather than inferred:

| cell | round | branch | fitted span | old A4 (boundary) | **new A4 (boundary)** |
|---|---|---|---|---|---|
| `D01` | r0 | floored | 16.0 | PASS (12.0) | **N/A** |
| `D01` | r1 | capped | **12.0** | **FAIL** (18.0) | **PASS** (9.0, ratio 1.15) |
| `D02` | r0 | floored | 16.0 | PASS (12.0) | **N/A** |
| `D02` | r1 | capped | **24.0** | **FAIL** (18.0) | **FAIL** (18.0, ratio 0.47) |
| `D03` | r0 | floored | 32.0 | **FAIL** (24.0) | **N/A** |
| `D03` | r1 | capped | 56.0 | PASS (42.0) | **PASS** (42.0, ratio 1.10) |

**Three of six A4 verdicts were failures; after the repair, one is.** Two were assertion artifacts. The
survivor is the point.

> **A second correction to my own write-up, and this one the re-run caught.** I had listed `D02` r1 as
> FAIL → PASS, by assuming its fitted span matched `D01` r1's. **It does not: `D02` r1's fitted span is 24.0,
> exactly its requested span**, so the boundary is 18.0 under either rule and the verdict is unchanged. It was
> derivable from the published report all along — `halfWidth 18.0` on a capped round inverts to
> `18.0 / 0.75 = 24.0` — and I did not derive it. I had also listed `D01` r3, whose fitted span is **not**
> recoverable from a B15 report at all (it is capped *and* detect-bounded, so `halfWidth` is the detect bound,
> not `maxHalfWidth`). Re-running with the new binary replaced both guesses with measurements.

**`D02` r1's surviving FAIL is a real signal, not residual noise.** The round was capped — the fitted crossing
exceeded `maxHalfWidth = 18.0` — while the analytic truth half-width is **8.4**. The model wanted to extrapolate
past twice the truth. That is a fit-quality observation about `D02`'s round 1, and it is exactly the kind of
thing A4 exists to catch; it was previously buried among two failures the assertion was manufacturing itself.

It also demonstrates the property the fix most needed: **the repaired A4 still fails when it should.** A repair
that made every round pass would have been the easier bug to ship, and this is the real-data evidence that it
was not shipped.

### Shown RED against three named mutants

Two are the repaired defects; the third is the alternative that was **considered and rejected**, so the suite
records *why* it was rejected instead of merely asserting the choice.

```
M-A4-REQUESTED-SPAN       (useFitted := false)
  Failed Evaluate_UsesTheFittedSpan_NotTheRequestedSweep
  Failed! - Failed: 1, Passed: 9, Total: 10

M-A4-NO-FLOOR-EXEMPTION   (the old behaviour: floored rounds fall through to the cap assertion)
  Failed Evaluate_ABandFlooredRound_TruthAboveTheBoundary_IsNotApplicable
  Failed Evaluate_ABandFlooredRound_TruthBelowTheBoundary_IsAlsoNotApplicable
  Failed! - Failed: 2, Passed: 8, Total: 10

M-A4-FLOOR-COUNTS-AS-CAP  (the rejected alternative: no exemption AND floor satisfies the cap)
  Failed Evaluate_ABandFlooredRound_TruthAboveTheBoundary_IsNotApplicable
  Failed Evaluate_ABandFlooredRound_TruthBelowTheBoundary_IsAlsoNotApplicable
  Failed! - Failed: 2, Passed: 8, Total: 10

restored ok sha256(after) = 6296d26a…06279b   (identical)
```

**Stated rather than glossed:** the last two mutants are killed by the **same pair** of tests, so the suite
*excludes* both alternatives without *distinguishing* between them. That is honest coverage of the decision —
neither alternative survives — but it is not three independently-discriminated mutants, and reporting it as
three would overstate what the suite proves.

---

## THE 42-MINUTE `optimize` GATE — owed, and run as a PAIRED arm

### It is owed, and the predicate was run rather than asserted

`Q27-V1`'s reachable set, quoted verbatim from `/mnt/d/hf_w27/q27_v1.txt`, intersected against the change set
(tracked diff **unioned with untracked**, F88), against the BEFORE binary's tree read out of **its own
provenance file** (`/mnt/d/hf_w25/binary_provenance_w25.txt` → `29665a62…`), never an assumed previous HEAD:

```
>>> intersection size = 4  =>  GATE IS OWED
    …/HocusFocus/StarDetection/Optimization/OptimizerVariable.cs      <-- this branch
    …/HocusFocus/Controls/EdgeJustifiedWrapPanel.cs                   <-- merged since B15, not this branch
    …/HocusFocus/Properties/AssemblyInfo.cs                           <-- "
    …/HocusFocus/Utility/DetectionBinningResolver.cs                  <-- "
```

### Why one arm was not enough — a confound found before it could mislead

**The B15 → B17 diff spans everything merged to `develop` since wave 25, not just this branch.** One of those
files, `DetectionBinningResolver.cs`, changed **+76/−14** and is referenced by `OptimizationDiagnosticRunner.cs`
— *the gate's own path*. So a single arm against K8 cannot separate "the axis bound moved it" from "someone
else's merge moved it". **Both arms were run:** `BASE` = `develop @ 2ebbd43` (this branch's own base) and
`BRANCH` = this branch. **`BRANCH` vs `BASE` is the only comparison that isolates this change**, and it is the
one the clause is read on.

### The prediction, written before the gate ran

Recorded in full at `/mnt/d/hf_ship1/gate_prediction.txt`, before the binary was laid out. In short: this is
**not** the believed-inert shape waves 20/21/25/27 used, where `G-PASS` *is* the inertness measurement. Bounding
the axis **deliberately changes the search space** — three of this axis's four coarse-grid levels move — so
`8 of 8 bit-identical` was *not* predicted, and would not have been evidence of correctness if it happened.

- **P1** — fewer than 8 of 8 bit-identical to K8. Sharpened from the BEFORE landings: `D20_m24_bright_control`
  and `muggsie` both landed at exactly **0.4**, an old coarse-grid level that no longer exists, so they are the
  likeliest to move; `CWhiteFocus` never left the 0.5 seed on this axis, so it is the likeliest to be identical.
- **P2** — no dataset materially worse (> 1e-3). Every BEFORE landing is ≤ 0.6, well under π/4, so **no viable
  optimum lived in the excised band**.
- **P3** — every landed `MaxDistortion` ≤ π/4, by construction.
- **Refutation condition, fixed in advance:** any dataset's `BestJ` falling more than 1e-3 below its reference
  means the bound removed something the search was using, and **the change does not ship in this form**.

### The result — `G-PASS`, 8 of 8 bit-identical, and **my prediction P1 was REFUTED**

`GATE_branch_DONE 2026-08-14T01:36:29Z`, `[COUNT] aggregate_summary.json produced: 8 expected: 8`, marker
written. 49 m 32 s wall (wave 25's was 42 m 42 s on the same eight).

| dataset | B15 `BestJ` | BRANCH `BestJ` | bit-identical | landed `MaxDistortion` B15 → BRANCH |
|---|---|---|---|---|
| `toml999` | 0.9957838768299878 | 0.9957838768299878 | **YES** | 0.4500 → 0.4500 |
| `CWhiteFocus` | 0.9960675916058808 | 0.9960675916058808 | **YES** | seed → seed |
| `uneven` | 0.9963677194179505 | 0.9963677194179505 | **YES** | 0.6000 → 0.6000 |
| `muggsie` | 0.9971948738498605 | 0.9971948738498605 | **YES** | 0.4000 → 0.4000 |
| `mccomiskey` | 0.9767460801208465 | 0.9767460801208465 | **YES** | 0.4125 → 0.4125 |
| `D18_m24_deep_shed` | 0.9998815090506263 | 0.9998815090506263 | **YES** | 0.1000 → 0.1000 |
| `D19_cygnus_deep_shed` | 0.9994870586135448 | 0.9994870586135448 | **YES** | 0.5375 → 0.5375 |
| `D20_m24_bright_control` | 0.9997378027339423 | 0.9997378027339423 | **YES** | 0.4000 → 0.4000 |

**`BestJ` bit-identical to B15: 8 of 8. Landed `MaxDistortion` identical: 8 of 8. `P2` HOLDS, `P3` HOLDS.**
This is K8's thirteenth consecutive reproduction.

**`P1` — "fewer than 8 of 8 will be bit-identical" — is REFUTED, and that refutation is the most useful thing
the gate produced.** `muggsie` and `D20`, which I named in advance as the likeliest to move because they land on
0.4, did not move. The prediction was wrong because its **premise** was wrong: I believed `MaxDistortion` was
coarse-gridded, and it is not. Reading the source after the refutation is what found it (see the correction in
item 4). **A prediction with a false premise is still doing its job when the instrument contradicts it** — that
is the whole reason for writing it down first, and it is why this gate was worth its 49 minutes even though it
"only" reproduced the baseline.

Once the mechanism is corrected, `8 of 8` is exactly what the change predicts: the bound only removes a region
the pattern search never visited on these eight runs — every landing sits at or below 0.6.

### The paired BASE arm was prepared and NOT run, with a reason

`develop @ 2ebbd43` was built and laid out (`/mnt/d/hf_ship1/exe_base`, `TestApp.dll 617513d9…`, a different
binary from the branch's `51daec0c…`), ready to run. **It was not run, because the confound it exists to resolve
did not arise.** The base arm separates "the axis bound moved it" from "a merge since B15 moved it" — but
**nothing moved**: the branch reproduces B15 bit-identically on all eight, so both candidate causes are inert on
this population and there is nothing left to attribute. Running it would have cost 42 minutes to partition zero
difference. The binary is on disk if a later run wants it.

---

## What was NOT run

- **`(3′)`, and its 42 m gate** — voided by `V-0`. Not a debt: there is nothing left to ship for F82 in that
  shape.
- **The four withdrawn wave-30 ledger rows** (§10 rows 3–6) — not re-listed, per §3 of the prompt.
- **Rendering datasets at 1.4–19.4 ″/px** — out of scope by instruction, and still the only route to a blind
  wide-field population. It is now *more* clearly the next decision, because `V-0` retired the last product
  question this bank could answer with another arm.
- **The `MaxDistortion` persisted-key rename** — a settings migration, named as an unpaid debt rather than
  attempted under a stop clock. See item 4.

---

## THE HONEST ENDING

**This run ends the synthetic-AF-bank thread, and it ends it on a real result rather than by running out of
things to do.**

The bank is measured out. Of the five items:

| item | outcome |
|---|---|
| 1 — `V-0` | **FIRED.** `(3′)` cannot change a step in the product's configuration. Item 2 is void. |
| 2 — implement `(3′)` | **VOID**, by item 1, before any code was written. |
| 3 — the `ACCEPTED-elsewhere` residual | **RESOLVED** as a scorer-side matching artifact. Wave 30 row 7 discharged. |
| 4 — bound + rename `MaxDistortion` | **SHIPPED** (axis bound + label + tooltip). Persisted-key rename named as a debt. |
| 5 — the `A4` truth-model gap | **FIXED**, harness-side, no gate owed. Two failures were the assertion's own; **one survives and is real**. |

**Three self-corrections, all made before the thing being corrected could mislead anyone.** The
`MaxDistortion` coarse-grid justification (caught by a refuted prediction), the first A4 fix (caught by
re-scoring real reports), and the A4 before/after table (caught by re-running instead of predicting). Each is
recorded where the claim it replaces was made, in the commit that made it and in this document.

**Three of the five closed by measurement rather than by construction**, which is the pattern the wave series was
stopped for: the remaining questions were not answerable with another arm on this bank, and two of them turned
out not to be product questions at all.

**What is genuinely left is new data, and it is the owner's decision.** `V-0` retired the last product question
this bank could answer, and it did so by showing that F82's live entry — the F18-vs-F81 ordering conflict — needs
a **blind** wide-field population that does not exist here (`c_blind = 0`, F97). The 1.4–19.4 ″/px render is the
only route to one. It has been open twelve waves, it is ≥ 1 h of render, and it was explicitly out of scope for
this run. **Render it, or stop.** Nothing else on the backlog is blocked on anything but that.

---

## EVIDENCE INDEX

Everything below is on disk and re-runnable.

| what | where |
|---|---|
| `V-0` driver + log (`V0_ARM_START`/`_END`, 6 of 6 rows) | `/mnt/d/hf_ship1/v0_arm.sh`, `v0_arm.log` |
| `V-0` reports, both arms | `/mnt/d/hf_ship1/v0/{off,on}/D0{1,2,3}_*__S1/synth_validate_report.json` |
| `V-0` scorer | `/mnt/d/hf_ship1/v0_compare.py` |
| item 3 — reproduction + self-test | `/mnt/d/hf_ship1/item3_accepted_elsewhere.py` |
| item 3 — the decisive split | `/mnt/d/hf_ship1/item3b_resolve.py` |
| item 3 — the far-group characterisation | `/mnt/d/hf_ship1/item3c_far.py` |
| gate prediction, written first | `/mnt/d/hf_ship1/gate_prediction.txt` |
| gate change set + intersection | `/mnt/d/hf_ship1/gate_changeset.txt`, `gate_intersection.txt` |
| gate driver, both arms | `/mnt/d/hf_ship1/gate_ship1.sh`, `gate_branch.log`, `gate_base.log` |
| gate scorer | `/mnt/d/hf_ship1/score_gate_ship1.py` |
| B17 provenance (dll sha256, two-binary diff) | `/mnt/d/hf_ship1/binary_provenance_ship1.txt` |
| A4 re-check on the new binary (measured verdicts) | `/mnt/d/hf_ship1/a4_recheck.sh`, `a4_recheck.log`, `a4/` |
| the base gate binary, built and not run | `/mnt/d/hf_ship1/exe_base` (`TestApp.dll 617513d9…`) |
| mutants | `mutant_M-OLD-CEILING.sh`, `mutant_A4.sh` (job tmp dir) |

**One incidental confirmation of [F66](followups.md), measured rather than quoted:** B17's `TestApp.exe` hashes
`dd7103c2…` — **byte-identical to B15's apphost**, across two binaries whose DLLs plainly differ. Hashing the
apphost as a binary identity would have reported these two builds as the same build.
