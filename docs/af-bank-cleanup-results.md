# The synthetic-AF-bank cleanup — a SHIP run, and the end of the thread

**Run window.** `T0 = 2026-08-14T00:00:31Z`, stop `T0 + 6 h = 2026-08-14T06:00:31Z` (the invoking message named no
duration). PR #196 verified `MERGED` at `2026-08-13T22:56:58Z` before anything else ran.

**The prompt's five items were complete and reported at 02:55Z, inside that window.** Everything under
**SCOPE EXPANSION** below was authorised by the owner afterwards ("*I don't mind expanding scope. I want to get
this done*") and runs past the original stop by permission, not by overrun. The sections above it are the run as
the prompt scoped it.

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

## SCOPE EXPANSION (owner-authorised, after the first report)

### The twelve-wave coverage-hole row is FALSE, and its own search path could never have caught that

Wave 30 §10 **row 10** reads: *"new datasets between 1.4 and 19.4 ″/px … `ls -d /mnt/d/SyntheticAutofocusBank/D*`
→ **20, none in the band** … OPEN, twelve waves."*

**Seven of the twenty are in that band.** Computed (`/mnt/d/hf_ship1/coverage_band.py`) as
`206.265 × pixelSizeMicrons × captureBinning / focalLengthMm`, with pixel sizes read out of
`SensorRegistry.cs` rather than from memory:

| ″/px | dataset |
|---|---|
| 19.389 | `D01_ultrawide_40mm` |
| 5.745 | `D02_rich_135mm` |
| 3.102 | `D03_redcat_250mm` |
| 1.410 | `D04` / `D16` / `D18` / `D20` (550 mm, IMX571) |

**The row's search path counts directories.** `ls -d D*` returns 20 whatever the plate scales are; it cannot
evaluate the predicate it is attached to. Wave 30 §10's whole point was that *"every row below carries a literal
search path that returns the answer"* — this row carries one that returns a **different** answer, and being
checkable-looking is what let it ride for twelve waves.

### The real hole, and it is a different band

| ratio gap | from | to |
|---|---|---|
| **× 3.38** | 19.389 (`D01`) | 5.745 (`D02`) |
| × 2.20 | 3.102 (`D03`) | 1.410 (`D20`) |
| × 1.85 | 5.745 (`D02`) | 3.102 (`D03`) |

**Exactly one dataset sits above 5.75 ″/px, and it is `D01` — the cell F97 records as burned.** That is the
mechanical reason `c_blind = 0`: not that wide-field data is missing, but that *all* of it is one burned cell.
So a blind wide-field population needs **≈ 5.7 – 19.4 ″/px**, not "1.4 – 19.4", which is covered seven times
over. **A render aimed at the row as written would have added cells the bank already had.**

### Two datasets added to close it

Appended to the checked-in spec (`TestApp/SynthBank/synthetic-bank-spec.json`), with fresh `datasetSeed`s so
the existing twenty are untouched:

| new | rig | ″/px | field |
|---|---|---|---|
| `D21_widefield_60mm` | 60 mm f/4.0, IMX571 bin1 | **12.926** | Orion belt/sword |
| `D22_widefield_100mm` | 100 mm f/5.6, IMX533 bin1 | **7.756** | Perseus/Auriga |

Fields are chosen so that **neither new cell CONTAINS any existing dataset**: `D21`'s nearest is 21.3° away
against a 13.2° FOV radius, `D22`'s is 17.2° against 4.6°. An earlier draft put `D21` in Orion **0.3° from
`D03`** — a 26.5° field that would have swallowed a burned cell whole, which is precisely the correlation the
new cells exist to avoid. Caught by computing separations rather than eyeballing constellations. The wide-field
ladder becomes 19.389 → 12.926 → 7.756 → 5.745, every step ≤ 1.67×.

### The render cost 43 seconds, not "≥ 1 h"

```
RENDER_render_START 2026-08-14T04:13:13Z … synth-bank: 2 generated, 0 skipped, 0 failed
RENDER_verify_END   2026-08-14T04:13:56Z
```

9 FITS + 9 `golden.json` + `synthetic_meta.json` per cell, `--verify` clean, 532 MB + 184 MB. `--dry-run`
first (a habit worth keeping — it exercises the kernel-cap guard, which is what would actually have wasted
render time); both cells came back `withinCap=YES` at 0.7 % utilization, identical to `D01`/`D02`/`D03`.

**Stated fairly, because the row's price deserves a fair reading.** Row 10 priced the item at *"≥ 1 h render"*
**and** *"≥ 1 h compute"* (§12.1). **The compute half was about right** — this run's blind arm was 27 minutes
for four `synth-validate` runs, and the table arm below adds more. **It is the RENDER half that was never
grounded in a measurement:** wave 12 recorded *"Render + 15 optimizes: 13 m 51 s total"* for three datasets, so
rendering was always a small fraction of any arm. Nobody re-derived the render price; it was inherited. So the
row was not blocked by an hour of rendering — it was blocked by an unexamined number sitting next to an
unfalsifiable check.

### And then the thing the whole series wanted: `c_blind` is no longer 0

Pre-registered before the arm ran (`/mnt/d/hf_ship1/blind_widefield_prereg.txt`), because these are the first
genuinely blind wide-field cells the series has had.

| arm | cell | r | requested | **fitted** | `halfWidth` | step | branch | detect-bound |
|---|---|---|---|---|---|---|---|---|
| off | `D21` | 2 | 40.0 | **30.0** | 22.5 | 6 | capped | — |
| on | `D21` | 2 | 40.0 | **30.0** | **19.384** | 6 | capped | **binds** |
| off/on | `D22` | 0–2 | 16/24/40 | 16/24/40 | — | 3/5/9 | — | — |

**`P1` CONFIRMED.** `D21` r2 requested a span of 40.0 and the fit came back spanning **30.0** — the sweep was
widened and the outer frames stopped yielding usable HFR points. **That is F82's shrink-while-widening
transition, on a cell nothing has ever been tuned against.** [F97](followups.md)'s `c_blind = 0` was an artifact
of the coverage hole, not a fact about rarity: the very first blind wide-field cell rendered exhibits it.

**`P2` CONFIRMED.** On the one round where the product's detectability bound binds, `maxUsefulHalfSpan = 19.384`
against a requested-span floor of **30.0** — F18 clamps **below** F81's floor, exactly as on `D01` r1 and `D02`
r1. **The F18-vs-F81 ordering conflict reproduces off `D01`**, which is what F82's re-scoped entry needed and
could not previously get.

**`P3` CONFIRMED, but narrowly, and the narrowness matters.** The arms differ on `D21` r2 — `halfWidth` 22.5 vs
19.384 — but **the recommended step is 6 either way** (`round(22.5/3.5) = round(19.384/3.5) = 6`). On `D01` the
same conflict moved the step (3 → 2). So the ordering conflict is **real and general, while its consequence is
not**: it changes the half-width on both cells and the recommended step on only one. Any fix must be argued on
severity, not rate — which is exactly where the F82 decision document's §7 clause 5 already landed.

`D22` shows neither: fitted == requested on all three rounds, no detect bound. **1 of 2.**

**What this burns:** `D21`/`D22` are no longer blind *for the F82 / F18-vs-F81 question*. They remain blind for
recall, precision, exposure and binning, none of which this arm read as a thresholded quantity. Recorded so a
later reader does not have to reconstruct it.

### A4 do-no-harm on the seventeen NON-burned cells — holds, and half of it was never exercised

The A4 repair was measured on `D01`/`D02`/`D03`, which are exactly the cells it was designed against
(F14 / `S16` / `D20`). This arm runs the **other seventeen** S1 cells on the new binary and compares A4's
verdicts against wave 25's published ones, round by round. Prediction fixed before it ran: *a verdict changes
if and only if the round is band-floored (→ N/A), or capped with fitted ≠ requested (the boundary moves).*

```
A4_DONOHARM_END 2026-08-14T06:13:02Z   manifest rows: 15 of 17
  rounds compared: 31      verdicts changed: 0      UNEXPLAINED (refuting): 0
  verdict mix BEFORE: {PASS: 31}       AFTER: {PASS: 31}
```

**Population, stated rather than shrunk.** `D05_tec140_1000mm` and `D19_cygnus_deep_shed` both hit the 900 s
timeout, got **no manifest row, and are NAMED** — and they are precisely the two cells wave 23 lost to
`timeout 600` and wave 25's driver carried as *"the two NAMED timeout risks"*. A reproduction of a known risk,
not a new failure.

**The result, and the honest half of it.** All 31 rounds are **`capped` with `fitted == requested`** — so the
boundary is identical under either rule and the repair is confirmed **inert on 31 rounds it could have moved
and did not**. That is a real do-no-harm measurement for the **fitted-span** half.

**It is not one for the exemption half.** The population contains **zero band-floored rounds**, so the branch
that turns a floored round into `NotApplicable` was never entered. This arm says nothing about it. The
exemption's evidence remains what it was: the three burned cells (where it converts `D03` r0's FAIL and
`D01`/`D02` r0's PASSes into N/A) plus two unit tests and two mutants. **Reporting this as an unqualified
"do-no-harm holds" would overstate it by half**, which is why the split is written out.

### What the two new cells measure — and the wide-field recall story is NOT a clean gradient

`optimize --per-run --max-evals 250` then `golden eval --params optimized`, both pinned to the same settings and
profile as every other arm. Four steps, **5 m 45 s total** (`/mnt/d/hf_ship1/wf_driver.log`).

> **[F22](followups.md) FENCE, inline and mandatory.** `D21` and `D22` have native `hfrMin` of **0.331 px** and
> **0.473 px** — both far below the ~1.1 px point where the detector's measured HFR over-reads badly, the same
> regime that fences `D01`. **No recall count below may be quoted as a recall figure for a user.** The frame in
> which they may be read is **goal 3** — *is a landed knob sitting at an extreme against the physics?*

| ″/px | dataset | prec | `recall@high` | `recall@all` | `BestJ` (from Baseline) | σ Best (from Baseline) | Sensitivity |
|---|---|---|---|---|---|---|---|
| 19.389 | `D01_ultrawide_40mm` | 1.000 | 0.183 | 0.123 | 0.994825 (0.000000) | 0.1514 (0.6145) | 36.333 |
| **12.926** | **`D21_widefield_60mm`** | **1.000** | **0.103** | **0.061** | 0.993161 (0.000000) | 0.2084 (0.6122) | 35.333 |
| **7.756** | **`D22_widefield_100mm`** | **1.000** | **0.235** | **0.079** | 0.995044 (0.000000) | 0.1468 (0.7701) | 34.583 |
| 5.745 | `D02_rich_135mm` | 1.000 | 0.446 | 0.377 | 0.996558 (0.000000) | 0.0991 (0.2321) | 33.333 |
| 3.102 | `D03_redcat_250mm` | 1.000 | 0.365 | 0.238 | 0.995494 (0.712458) | 0.2698 (1.2397) | 31.583 |

**Three readings, in increasing order of confidence.**

1. **Precision is 1.000 with `FP = 0` on both new cells**, matching 19 of the existing 20. Nothing about the
   wide-field regime costs precision.
2. **`recall@high` is NOT monotone in plate scale.** 0.183 → **0.103** → 0.235 → 0.446 → 0.365. `D21` at
   12.9 ″/px is the **worst** of the wide-field set — worse than `D01` at 19.4. So the table's implied reading,
   that recall degrades smoothly as the plate scale coarsens, does not survive two new points. **Confounded and
   labelled as such:** these cells differ in field, limiting magnitude (10.5 / 12.0) and f-ratio as well as in
   plate scale, so this refutes the *clean gradient*, it does not establish a new law.
3. **Every one of the five has `BaselineJ = 0.000000` except `D03`.** On wide-field rigs the *default* detection
   settings score **zero**, and the optimizer recovers to 0.993–0.995. That is the strongest goal-1 statement in
   the set and the two new cells reproduce it independently.

**And a goal-3 reading that lands squarely on this PR's own change:** the landed `MaxDistortion` on the new
cells is **0.20** and **0.40** — both far below the π/4 ceiling item 4 introduced, and `D21`'s is near the
axis's *bottom*. Every landed value now known across the bank spans **0.1 – 0.6**. **Two fresh datapoints,
gathered after the bound shipped, confirm it removes nothing the search uses**, and that the pressure on that
axis is downward.

**Not pasted into `docs/synthetic-af-bank-results-table.md`, deliberately.** That table's header pins its
provenance — *"Optimize columns from wave 18's pinned seedA0 arm … on B15 (BuildId d79dae73); all 20 verified
against each log's own snapshot-source line."* These rows come from a **different binary and a different
optimize arm**, so pasting them in would quietly break the claim the header makes. Adding them properly means
re-running them under the table's own conventions — named here as the owed work, in the same spirit as the `K`
column refusing rather than emitting a short column.

### The `K` column — paid, and it refused first, which is the good outcome

Wave 30 §10 **row 3** withdrew the results-table `K` column after three unpaid waves, noting *"`kcol_w30.py`
stays on disk (self-test `21 of 21`); any later ship can run it in ~2 m."* **This is a later ship.** Self-test
re-run: `SELFTEST W30K 21 of 21`. Then the real pass:

```
K-0 datasets in the bank   candidates: 22   findings: 2   pre-registered population 20
>>> RULE W30-K = K-UNEVALUATED   (K-0 False, K-1 True)
    "the bank carries 22 dataset(s) against a pre-registered 20 … NO COLUMN IS EMITTED --
     a short column pasted into the results table would silently drop rows."
```

**It refused because I had just added two datasets, and refusing was correct.** A scorer with a pinned
population noticed that the population moved and declined to emit a 20-row column for a 22-row bank. That is
the discipline this series spent thirty waves building, catching a change made ten minutes earlier by the same
run — and it is worth more than the column.

The floors themselves read cleanly on all 22 (`K-1`: 22 candidates, **0 unreadable**), so the content is
available even though the pinned rule declines to publish it:

| ″/px | dataset | `hfrMin` | `hfrMinEffective` | **K** |
|---|---|---|---|---|
| 19.389 | `D01_ultrawide_40mm` | 0.2307 | 0.7 | **0.700** |
| **12.926** | **`D21_widefield_60mm`** | 0.3310 | 0.7 | **0.700** |
| **7.756** | **`D22_widefield_100mm`** | 0.4727 | 0.7 | **0.700** |
| 5.745 | `D02_rich_135mm` | 0.2800 | 0.7 | **0.700** |
| 3.102 | `D03_redcat_250mm` | 0.5766 | 0.7 | **0.700** |
| 1.410 | `D04`/`D18`/`D20` | 1.038 | 1.038 | 1.038 |
| … | (long focal length) | native | native | 1.08 – 10.20 |

**The five datasets the generator floors at K = 0.700 are exactly the five at ≥ 3.1 ″/px** — and the two new
cells are two of them. That is the "severely oversampled R2" class the spec describes, and it **corroborates
the assumption `P1` rested on**: `D21`/`D22` really are in `D01`'s regime, measured rather than asserted from
their focal lengths. It is also why `D21` reproduced the shrink and `D22` did not have to.

**Row 3 is discharged**, with the honest note that `kcol_w30.py` needs its pre-registered population updated
from 20 to 22 before it can emit — a one-line change owed to whoever next wants the column in the table.

---

## F49 / F51 — asked for as remaining work, and mostly already done

**I got this wrong first and the correction is the point.** Asked what followups remained, I named F49 and F51
as *"the highest-value cluster — the only entries with a real user hitting them on shipped defaults."* I had
read their **`**Status:** Open`** lines and their opening paragraphs. I had not read their bodies.

| entry | what the status said | what the body says |
|---|---|---|
| **F51** | `Open` | **all three parts (a)(b)(c) SHIPPED 2026-08-07 (wave 9)**, with a correction caught by an existing test |
| **F49** | `Open` | **(a) SHIPPED wave 9**, **(c) SHIPPED wave 10**; only **(b)** — a *decision* — remained |

Both status lines are now corrected, and **F51's residual is not F51's**: its own closing line redirects it to
`F21`/F49(c), and F49(c) shipped the copy that quotes the exact convergence ratio plus a test asserting the cap
releases.

**This is [F106](followups.md)'s class — "a hand-carried debt ledger rots in BOTH directions" — rotting toward
OVERSTATING what is left**, and it is the same failure as F112's row 10: a field that *looks* checkable, is read
as authoritative, and is not maintained. A scan of all 113 entries finds **12 whose status says `Open` while
their body says `SHIPPED`/`DONE`**. Two are fixed here. **The other ten are named, not bulk-edited**, because
deciding what genuinely remains in each requires reading it — which is the mistake being corrected, not a
licence to repeat it at scale:

> `F18` `F25` `F53` `F55` `F58` `F63` `F69` `F70` `F79` `F80`

Two of those (`F79`, `F80`) are already honest — their statuses say *"fix identified and priced; deliberately
not shipped"* — so the flag is a false positive there, which is itself why a regex must not drive the edit.

### The full audit of the twelve — and I made the same mistake again, one message later

All twelve were read. **Five were genuinely stale, four were false positives, one was internally
contradictory, and two were F49/F51.** After the corrections, a scan for *"an entry whose own part shipped while
its status says nothing about it"* returns **zero**.

| entry | status said | reality | action |
|---|---|---|---|
| **F79** | *"deliberately **not** shipped in wave 23"* | its own body: ***"SHIPPED IN WAVE 23"*** | **corrected** — status directly contradicted the entry |
| **F80** | *"remedy specified and priced"* | the `--verify-derivation` pre-flight **shipped** (wave 25) | **corrected** — open as the *class*, not the remedy |
| **F25** | bare *"Open"* | the **narrow half** of its gate shipped (wave 25, P4) | **corrected** — the wide half really is unmeasured |
| **F53** | bare *"Open"* | **(a) shipped** wave 10 | **corrected** — open as (b), a standing rule |
| **F58** | mechanism only | **(d) DONE** 2026-08-09 | **corrected** |
| **F69** | *"**Open** · … · **CLOSED** 2026-08-11"* | closed | **corrected** — it opened with the word it then contradicted |
| F18, F55, F70 | already disclose what shipped, in the status | accurate | no change |
| F63 | bare *"Open"* | the `SHIPPED` in its body refers to *another* change's default, not F63's own work | no change — **true false positive** |

**The part worth writing down: I repeated the exact error while reporting on it.** In the message proposing this
audit I wrote that *"two of those (F79, F80) are already honest — their statuses say 'fix identified and priced;
deliberately not shipped'"*. **I had read their status lines and taken them at face value** — the identical
mistake that produced the F49/F51 misdirection two messages earlier. F79 was the *worst* case in the set: its
status and its own body contradict each other outright.

**So the mechanism is not carelessness about one field; it is that a status line reads as an authority and a
body reads as history, and nobody re-reads history.** That is why the fix here is not "be more careful" but the
scan itself, which is now cheap to re-run:

```python
# an entry whose OWN part shipped, while its status stays silent about it
own = re.findall(r'(?m)^>?\s*#{2,4} .*\bSHIPPED\b.*$', body) \
    + re.findall(r'~~[^~]{5,90}~~\s*[—-]+\s*\*\*DONE', body)
flag = own and not re.search(r'SHIPPED|DONE|CLOSED', status_block)
```

It keys on the entry's **own** parts — a sub-heading declaring a ship, or a struck-through part marked `DONE` —
so it does not fire on prose like *"the shipped `Default` profile"*. A looser scan over the same register
returns 23 candidates, almost all noise; this one returns the 5 that were real and now returns **0**.

### F49(b), decided: **won't fix as asked**

Full argument in **`docs/f49b-lever-choice-decision.md`**. F49(b) asked whether to expose a user-facing floor on
the search's Sensitivity, or F32's keep fraction. **Neither faces the pathology.**

The premise was re-verified at `HEAD`: `MinDetectionKeepFraction` still has **no XAML binding anywhere**, and
the search's Sensitivity lower bound (`OptimizerVariable.DefaultSensitivityLower = 0.0`) is not user-exposed
either — so it is true that today there is no lever.

But F49's landing **kept 7× MORE stars than its seed** (834 → 5 766), and both candidates guard the *shedding*
end:

- **F32's keep fraction** *"rejects a candidate keeping less than φ of the SEED's accepted stars"* — it could
  not have bound. It is also retired *"permanently"* as *"the wrong instrument"*. Exposing it would ship a
  control whose honest tooltip is *"this does not apply to your situation."*
- **A Sensitivity floor** is defeatable through the other half of F6's inseparable pair — this very run moved
  `StarClippingMultiplier` 6.750 → 0.250 alongside the gate — and it would forbid a **real optimum**: F83 shows
  `J` has no precision term on an unlabelled run and `sStars` rewards star count, and F84 measured **22 of 24**
  pinned instances *driven* to the bound rather than never-moved. F84 says outright: *"Do NOT propose a floor on
  the Sensitivity axis as the fix."*

**The user's complaint is not "I cannot set a floor" — it is "I cannot tell whether this landing is good."** The
number that would tell them does not exist on an unlabelled run, and that is **F83**, whose two exits (a
precision term, or labelling the bank) are now also F49's. **F49(b) resolves into F83 and F49 is closed.**

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
