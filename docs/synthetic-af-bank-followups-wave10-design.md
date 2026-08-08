# Synthetic AF bank — followups wave 10 (design)

Plan: [`plans/synthetic-af-bank-followups-wave10-plan.md`](../plans/synthetic-af-bank-followups-wave10-plan.md).
Wave 9: [`docs/synthetic-af-bank-followups-wave9-results.md`](synthetic-af-bank-followups-wave9-results.md).
Register: [`docs/followups.md`](followups.md).

*Wave 9 closed with a PROVENANCE banner saying its numbers were measured on a detector that no longer exists.
This wave opens by acting on that banner rather than by quoting it — the first thing that runs is the gate, and
it is not a pass/fail against wave 5 because there is nothing left to pass.*

---

## §0 — The gate, and why it is NOT a CI gate this time

Every number in wave 9's results doc was measured on **`StarDetectorVersion` 1**, the dense-`SepFilter2D` à-trous
path. `develop` now ships [PR #187](https://github.com/ghilios/hocus-focus/pull/187)'s `AtrousWaveletFast` and
**`StarDetectorVersion` 2**. The two wavelet paths agree to **≤ 3e-8** and are deliberately not bit-identical,
which is exactly why the version was bumped.

So RULE G's eight comparability values, F32's arm landings and the wing statistic's Rule W table are **not
expected to reproduce today**, and wave 5's φ table is **no longer a valid comparison target on its own**. The
suite being green says nothing about this: the suite does not run the optimizer over the bank.

### §0.1 RULE G10 — fixed before the gate ran

The eight runs are re-measured at wave 5's exact invocation (`--per-run --max-evals 250`, `--settings` pinned to
the file that is byte-identical to waves 5–9, `md5 df7c7cd1…`, [F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)),
sequentially, nothing else running ([F55](followups.md#f55--optimize-is-not-reproducible-when-several-instances-run-at-once-and-the-seed-evaluation-is-what-moves)).
The values they return **define** wave 10's fixed point. Two readings are pre-registered, and only one of them is
a gate:

> **G10-A — the INSTRUMENT gate, the only pass/fail clause.** Re-running any of the eight a second time,
> sequentially and alone, must return the same value to 6 dp. *A fixed point that does not reproduce against
> itself is not a fixed point*, and if this fires, every cross-arm comparison in wave 10 is void before it is
> made — F55 firing at rest rather than under fan-out. **STOP.**
>
> **G10-B — a MEASUREMENT, not a gate, and a first-class finding in either direction.** How many of the eight
> landings does a ≤ 3e-8 per-pixel change MOVE? Wave 9 established that sequential execution is deterministic and
> that these eight reproduced across two waves and two binaries on version 1; wave 9's own shipped code is
> measured-inert on the search (the wing statistic is computed *after* it; F49/F51/F52(c) are copy). **The
> wavelet swap is therefore the only search-relevant difference between wave 9's binary and this one.** Written
> down before the numbers arrived:
>
> | moved | reading |
> |---|---|
> | 0 | the swap is inert at landing granularity; "≤ 3e-8" understates its safety |
> | 1–3 | the expected shape — a pattern search amplifies a tiny perturbation on some runs ([F8](followups.md#f8--optimizer-landings-are-not-reproducible-across-invocations)) |
> | ≥ 5 | "≤ 3e-8" describes the per-pixel residual and **not** the detector's behaviour, and PR #187 needs re-examining as a change that moves product landings |

**G10-A has a known weakness, and it is stated here rather than discovered later.** F55's sharpest open clue is
that the attractor is *stable within a session and variable across them*. A same-session repeat therefore agrees
under **both** hypotheses, so G10-A can pass while being uninformative about F55. That is why the gate is
paired with §2's cross-build probe rather than trusted alone — and why the very first gate run is already
evidence about F55 rather than only about the wavelet (see §0.2).

### §0.2 The first gate run, and the fork it opened

`toml999` returned **0.995784** — sequentially, alone, on the version-2 binary. **That is exactly the value wave
9's FAN-OUT arm A produced and that RULE G rejected**, against wave 9's sequential 0.997993. Two readings survive
that one number, and it cannot separate them:

| | hypothesis |
|---|---|
| **H1** | the wavelet swap moves this landing to 0.995784, and its agreeing with the fan-out value is a coincidence |
| **H2** | 0.997993 / 0.995784 are `toml999`'s **two F55 attractors** — as 0.988555 / 0.979173 are `D16`'s — and this run simply landed on the other one, with the wavelet irrelevant |

Under H2 wave 9's F55 write-up needs no change but the "fan-out did it" framing gets weaker; under H1 F55's
`toml999` evidence was **never about concurrency at all** and the wave-9 fan-out arm was voided partly by the
wrong cause. **§2 is designed to discriminate them**, and the discriminator is cheap because the legacy wavelet
survives in the tree as the equivalence-test oracle (`CvImageUtility`), so a one-line bisect build exists.

### §0.3 F15 — which landing the banks end holding

The gate is arm A's exact invocation (shipped defaults), which is what the banks already hold from wave 9's arm A.
The eight folders are rewritten with the same shipped-default landing they already carry, so the resting state is
preserved **by construction** rather than by a follow-up re-land
([F15](followups.md#f15--optimize---per-run-overwrites-each-runs-stored-settings)). Item 1's population pass is
run at that same invocation, for the same reason and as a free inertness control.

### §0.4 Build provenance, recorded by hand because F53's stamp does not exist yet

`D:\hf_w10\exe` — `git` `02c62d8`, `TestApp.dll` sha256 `5e58b669…`, `NINA.Joko.Plugins.HocusFocus.dll` sha256
`ccb3e3e9…`, `StarDetectorVersion` 2. **It is never rebuilt** ([F53](followups.md#f53--wave-8s-arm-x-does-not-reproduce-from-wave-8s-own-exe-because-the-arm-ran-on-an-earlier-build-of-it)(c));
later wave-10 builds go to `exe2`, `exe_v1wav`, … Making this automatic is F53(a), and it is item 2's companion.

---

## §1 — Item 1: F19's POPULATION CHECK

### §1.1 What is at stake, and why it is item 1

The wing rejected fraction **already shipped** in wave 9. It is live product behaviour: it can raise a user's
recommended exposure by 2×, and it is validated on **two datasets** — `D02` and `D16` — chosen because they are
the two this class's argument was built on.

**This project has watched a two-dataset conclusion reverse at full scale twice in one wave.** R2 went from
"restarts recover 0–36 %" (wave 6, 7 runs) to **228 %** (wave 9, 11 runs). R3 looked like a clean win on wave 5's
subset and regressed **4 of 6** newly-covered runs. Same sample size, same failure shape, and in wave 9's case the
population that did not motivate the hypothesis refuted it. The wing statistic has had no such test.

### §1.2 THE FALSIFICATION RULE, fixed before the pass runs

Measured across **all 20 synthetic datasets + the 19-run real bank** (`astrodet` excluded,
[F14](followups.md#f14--astrodet-is-frameless)) at the shipped-default invocation.

> **RULE P, fixed in advance. `WingIsShedding` is REFUTED as a shipped default — and wave 10 turns it off — if
> ANY of these fire:**
>
> - **P1 — the pre-registered fires do not fire.** `D10_rc16_3250mm_sparse` and `D17_cdk14_oiii5` are the bank's
>   two genuinely photon-starved datasets (§1.3). If **neither** fires, the statistic does not detect the
>   population it was built for and its two validation datasets were the whole of its evidence.
> - **P2 — it fires everywhere.** If it fires on **> 50 %** of the 39 runs, it is not a diagnosis, it is a
>   constant. A note that always fires says nothing (wave 9's own rule for `ContinueOptimizingAdviceText`).
> - **P3 — it fires where more exposure is measurably WRONG.** If it fires on `D16_esprit550_ha3` at its derived
>   exposure, W2 — the control the whole statistic had to pass — has failed on the bank. `D16`'s σ_focus minimum
>   is at its derived 2 s and is worse at 4 s and 8 s, so asking for 2× there is asking the user to make their
>   focus worse.
> - **P4 — the instrument is not connected.** If `WingRejectedFraction` is **NaN on > 25 %** of runs, the wing
>   axis is not populated often enough for the statistic to be product behaviour, and "we could not look" is
>   being shipped as "nothing is shedding" in three runs out of four.
>
> **Firing on a dataset that newly qualifies is NOT a refutation** — but every such dataset must be **named with
> a reason** in the results, because a statistic that quietly acquires new subjects at full scale is how R3
> failed.

**P1's direction is the one worth stating twice.** A candidate that does *not* fire on `D10`/`D17` is suspicious
in the *other* direction from the usual over-firing worry. Both directions are pre-registered so neither can be
explained away afterwards.

### §1.2a The control the statistic never had, and the SUCCESSOR — both fixed before the pass finished

*Added while the pass was still running, after two rows had been seen. Recorded as such, because a candidate
written after a refutation is motivated by it and has to be held to a higher bar, not a lower one.*

`WingRejectedFraction` is `rejected / (rejected + accepted)` over the outer third. Its CLAIM is that the **wings**
shed candidates a longer exposure could convert. But the detector rejects most connected components everywhere —
most of them are noise — so a high fraction out there may say nothing about wings. **The discriminating question
is not "is the wing fraction high" but "is it higher than the INNER third's"**, and that control is free from the
`FrameDiagnostics` this pass already writes.

> **The successor, `WingRejectedRatio` = wing-third fraction ÷ inner-third fraction, is PRE-REGISTERED AND NOT
> ADOPTED THIS WAVE.** If RULE P fires, the shipped probe is withdrawn and the successor is left as a candidate
> with its acceptance rule fixed here:
>
> - **RULE W1–W4 unchanged** (the wave-9 ladder: `D02`@0.5 s ≥ 2×; `D16`@2 s < 1.25×; converges; `D16`@0.5 s asks).
> - **NEW W5 — it must SEPARATE.** Its threshold must sit above the median wing/inner ratio measured on the
>   full bank, or it is the same defect in a new coordinate.
> - **NEW W6 — the population that refuted the predecessor may not adopt the successor.** It must be validated
>   on an arm run for it, not scored on this pass's rows. That is wave 9's R3 lesson applied in advance: a
>   statistic tuned on the data that killed its predecessor has been fitted, not tested.
>
> **Adopting a replacement on the refuting data would be fishing**, and this wave does not do it. The
> measurement (`WingRejectedFraction`) stays; only the ACTION is withdrawn.

### §1.2b What "withdraw the action" reaches — THREE sites, not one

Enumerated before the verdict, because the third one was not obvious and is the most consequential:

| # | site | what it does today |
|---|---|---|
| 1 | `rawFactor = max(rawFactor, WingProbeFactor)` | raises the recommended exposure to **2× current** |
| 2 | `exposureIsNotTheLimit = … && !wingIsShedding` | flips the user-facing verdict from *"your exposure is fine"* to *"exposure is the limit"*, which also re-routes `StarSignalCopy.RemedyFor` (F49) |
| 3 | `StarDetectionOptimizerWizardVM.BuildSearchExposureAdvice` | **tells the user to CANCEL a running two-hour optimization** |

**Site 3 is why this matters more than an exposure number.** F52(c) shipped in wave 9 on the explicit reasoning
that *"facts about COST need no statistic; ADVICE needs one"* — and the statistic it was given is the one under
test. It is computed from the **seed** evaluation, i.e. in the first minute, and if it fires on most runs then
most users are advised to abandon their optimization before it has done anything. A statistic that fires
everywhere is not merely uninformative there; it is expensive.

**What withdrawal means concretely, decided before the verdict:** delete the VERDICT (`WingIsShedding`,
`WingSheddingThreshold`, `WingProbeFactor`) and keep the MEASUREMENT (`WingRejectedFraction`). A public boolean
named *"is shedding"* that nothing acts on would be worse than either shipping or removing it — the successor
introduces its own verdict field, and until then the number stands on its own with no claim attached.

**And F52(c) returns to wave 8's position rather than to nothing.** Wave 8 withheld the abort advice for want of
a statistic; wave 9 shipped it on this one; if RULE P fires, it is withheld again for the same reason it was
withheld the first time. That is not a regression — it is the same decision, made again, on better evidence.

### §1.3 Why `D10` and `D17` are the pre-registered fires

They are the bank's only two **ceiling**-clamped datasets: their own derivation asks 335.6 s and 40.4 s and both
are rendered at the 30 s cap (F19(c), wave 9 §2). They are photon-starved *relative to their own physics*, by
construction and by a fact nobody had written down before wave 9. `D17` is additionally F19's poster child —
799 on-frame stars and still starved, because an OIII filter starves it — i.e. exactly the case richness cannot
see and the wing population is supposed to.

### §1.4 What this instrument would do if the thing it checks were completely broken

The harness computes `ExposureRecommendation` for **every** run regardless of display gating — wave 8's
"gate the DISPLAY, never the MEASUREMENT" — so a statistic that is silently never evaluated shows up as NaN
(P4) rather than as a clean row of zeros. And `WingRejectedFraction` is **NaN, never 0**, when the run cannot be
placed on the wing axis, so "we could not look" and "we looked and nothing was shedding" are distinguishable in
the output. Without both properties this pass would report a tidy all-clear on a statistic that never ran.

### §1.5 Cost and shape

**One sequential pass** over both banks at the shipped-default invocation with `FrameDiagnostics` carried, ~3–5 h.
It is three things at once and that is why it is item 1: the population verdict, a **clean F55 control** (39
`BaselineJ` values against the gate's, plus the eight against G10's), and it leaves the banks holding the
**shipped-default landing** (F15).

---

## §2 — Item 2: F55(b), the nondeterminism itself

### §2.1 Why this outranks compute

Fixing it makes high fan-out safe (every future arm ~4× cheaper) and removes the doubt hanging over every
cross-arm comparison this project makes. It is **not a compute task**: `D:\hf_w9\det\determinism_probe.sh`
reproduces the whole 7-hour discrepancy in **40 seconds** — 5 sequential repeats identical, 5 concurrent split
across the two attractors, and they are exactly the two values a 7-hour arm oscillated between.

### §2.2 Already eliminated by measurement — do NOT re-run these

The star-evaluation `Parallel.For` degree (swept 1/2/4/8/48, all identical); the per-run `optimized_settings.json`
(with and without); the disk detection cache (the optimizer path does not read it); OpenCL/`UMat` (none exists);
the frame-level fan-out (disjoint indices); `StarDetectorMetrics.Merge` + `SortBounds`; rented-array sorts
(bounded overloads); `MedianInPlace` (fresh arrays); load-sensitive timeouts (none).

### §2.3 The live clue, and what it does and does not fit

The value is **stable within a session and variable across them**. That does not fit a per-iteration data race.
It fits process-level state acquired once — a warm-up path, a static initialised from observed load, a JIT/tiering
effect. Wave 9 also records five consecutive sequential repeats at the *other* attractor **"from one build"**,
which raises a hypothesis wave 9 did not separate: **the attractor may be selected by the BUILD**, not only by
load. If so, "nondeterminism" is partly build-to-build variation, which looks identical to anyone comparing arms
built from different directories — and which [F42](followups.md#f42--every-build-directory-silently-gets-its-own-detector-settings-and-the-run-instructions-require-a-new-one-per-arm)
and F53 have both been circling from other doors.

### §2.4 The candidate wave 9 did not eliminate

Wave 9 eliminated the **plugin's** parallelism. It did not eliminate **OpenCV's own**. `Cv2` routines dispatch
through `cv::parallel_for_`, whose stripe count derives from the library's thread pool and an `nstripes`
heuristic, and OpenCV additionally dispatches on IPP / hardware features. A convolution is elementwise and would
not care; **a parallel reduction would** — a sum, mean or stddev split into a different number of stripes sums in
a different order. That mechanism is load-sensitive, cross-process visible (a shared pool sees machine load),
stable within a process, and bimodal rather than drifting. It is the shape of the observed defect and it is
cheap to test.

### §2.4a The specific instance — `KappaSigmaNoiseEstimate`

*Written down after reading the source and **before any probe ran**, so it can be refuted rather than confirmed.*

`CvImageUtility.KappaSigmaNoiseEstimate` is §2.4's shape made concrete, and it is a **bimodal amplifier by
construction**:

```
while (numIterations < maxIterations) {
    Cv2.MeanStdDev(image, out mean, out sigma, backgroundMask);   // an OpenCV PARALLEL REDUCTION
    if (++numIterations > 1 && Math.Abs(sigma - lastSigma) <= allowedError)  break;   // allowedError = 1e-5
    threshold = mean + clippingMultiplier * sigma;                // one more iteration moves sigma MACROSCOPICALLY
}
```

An infinitesimal change in `sigma` — from a different stripe partitioning, or from PR #187's ≤ 3e-8 — can flip
the convergence test, and the run then takes **one more iteration**, which changes `threshold` and therefore
`sigma` by a large amount. **Two discrete outcomes from an arbitrarily small perturbation.** That matches every
observed property of F55 at once: bimodal rather than drifting; two attractors; load-sensitive (OpenCV's pool
sees machine load); stable within a process (pool size fixed at init); and invisible to the plugin's own
`Parallel.For` degree, which wave 9 swept and found inert. σ feeds noise clipping ⇒ the star gate ⇒ the star
list ⇒ `J`.

**The rate is consistent with the measurements, which is what makes it worth testing rather than merely
plausible.** A flip needs the iteration-to-iteration σ difference to land within ~1e-8 of the 1e-5 tolerance —
order 1e-3 per call. One `optimize` run makes thousands of these calls (250 candidates × 9 frames × regions), so
several flips per run is the expectation, while a single seed evaluation makes tens and should flip rarely.
Measured: **44 % of landings and 15 % of seed evaluations**.

**How to test it, in preference order.** *Observe the mechanism*, don't just perturb a knob: log `numIterations`
and the per-iteration σ, run the 40-second probe, and diff an attractor-A run against an attractor-B run. If
`numIterations` differs, the convergence test is the amplifier and that is a direct observation. The
single-thread probe (§2.5(3)) is the corroborating perturbation, not the primary evidence.

**Eliminated by reading while looking for this, and recorded because it is the most F55-shaped thing in the
path:** `CvImageUtility.CalculateStatistics` selects its median with a **quickselect whose pivot comes from
`Random.Shared`** — process-level shared state, consumed in a thread-interleaving-dependent order, exactly the
signature. It is nevertheless **inert**: quickselect returns the value at rank *n*, which is the same value
whatever the pivots were, and `Mat.GetArray` hands back a **copy**, so the in-place permutation never reaches
the image. The comment at that line already says so; this is the confirmation that it is right.

### §2.5 The experiments, in cost order

1. **The cross-build probe (decisive for §0.2's fork, ~20 min).** Run the determinism probe on `toml999` with
   wave 9's `exe` (v1) and wave 10's `exe` (v2) **back to back in one session**. If v1 gives 0.997993 five times
   and v2 gives 0.995784 five times, the **build** selects the attractor and it is not session state — which
   simultaneously answers H1/H2 and reframes F55.
2. **The wavelet bisect (~30 min).** `StarDetector.cs:619/622` is the only call site and the legacy dense path
   survives as `CvImageUtility`'s equivalence oracle, so a build with the legacy call restored isolates the
   wavelet from everything else in the v1→v2 delta.
3. **The OpenCV thread probe (~10 min).** Pin OpenCV to one thread and re-run the concurrent phase. If the
   bimodality vanishes, §2.4 is the mechanism. **`WSLENV` must be set** — wave 9's parallelism sweep silently
   measured the same configuration five times because a WSL environment variable does not reach a Windows
   process, and *an instrument that is not connected reports perfect agreement*. The probe therefore carries a
   positive control: a configuration that is KNOWN to change the answer must be shown to change it.
4. **F53's stamp (~20 min, ships regardless).** `optimize` prints the informational version + commit + detector
   version of the binary it ran. This is the standing fix for "a `Reproduce:` line names a COMMAND, not a
   result", and it is the reason a future wave will not have to hand-record §0.4.

**F54 may close for free with it** — its "the flip moves a landing at factor 1" is the same shape, on a binary
pair, and a build-selected attractor would explain it without any `ApplyFactor` defect at all.

---

## §3 — Item 3: THE STEP RECOMMENDER DOES NOT CONVERGE

### §3.1 The cluster, and why it is one thing

Five entries — [F18](followups.md#f18--step-size-is-sized-by-curve-geometry-alone-so-the-sweep-outruns-what-the-detector-can-see),
[F21](followups.md#f21--stepsizerecommenders-half-width-is-not-stable-against-noise-even-on-a-perfect-fit),
[F25](followups.md#f25--from-a-far-too-wide-sweep-the-step-recommender-widens-it-further-instead-of-recovering),
[F26](followups.md#f26--a-stuck-binning-recommendation-starves-the-step-update-indefinitely),
F49(c)/[F51](followups.md#f51--capture-a-new-sweep-and-optimize-is-gated-on-the-exposure-recommendation-so-it-hides-exactly-when-the-run-needs-re-running--and-it-would-not-carry-the-new-step-size-anyway)'s
remainder — are the same component read five ways, and F26 already says so in as many words. This is the largest
untouched **user-facing** cluster in the register, and it has a field session behind it rather than a bank
argument.

The session: **100 → 214 → 459 → 474 → 482** across four runs, each costing 30–120 minutes, each telling the
user to widen. The user experienced it as a runaway. Wave 9's F51 made the iteration **cheap** — the re-capture
now carries the recommended step — and did nothing to make it **STOP**.

### §3.2 The mechanism, and it is arithmetic rather than a mystery

While the cap binds, `halfWidth = MaxHalfWidthSampledHalfSpanMultiple × ½ × searchSpan` and
`step = halfWidth / PointsPerSide`. With `P` points per side actually executed, `searchSpan = 2·P·stepₙ`, so

```
stepₙ₊₁ = (1.5 × P / 3.5) × stepₙ
```

— **1.714× at 4 offset steps, 2.143× at 4 offset + 1 recovery.** The field session is exact at P = 5:
100 → 214 (100 × 2.143 = 214.3), 214 → 459 (214 × 2.143 = 458.6). The ratio is a property of the ALGORITHM, not
of the fit, and it does not depend on the extrapolation the cap exists to distrust.

**So it does terminate**, geometrically, once the sweep is wide enough to contain the 3 × HFR_min band — and the
field session shows it doing so: runs 3 and 4 asked +3 % and +5 %, i.e. the cap had stopped binding. **Nothing on
the page says any of this.** The user is shown a number to accept, four times, with no statement that it is a
deliberate partial step, what it converges toward, or that the asks are shrinking.

### §3.3 What ships: the two halves, and only one of them is copy

**(A) SAY it — F49(c) verbatim.** When the recommendation is capped, the page states that it is a partial step,
what it is converging toward (`3 × HFR_min`), and the mechanism. The **ratio** is quotable because it is exact;
a projected run count is not, because it is computed from the very extrapolation the cap distrusts. The
exposure recommender's `MaxExposureFactor` copy already sets the precedent ("Run again to refine").

**(B) MAKE it stop — the deadband.** 459 → 474 → 482 are asks of +3 % and +5 %, presented as changes to accept
at 30–120 minutes each. Below some threshold the recommendation should read **"converged"** rather than a new
number. The threshold is **derived, not chosen**:

> **A deadband narrower than the recommender's own reproducibility is a deadband that does nothing.** F21
> measures exactly that reproducibility and has owed the measurement since 2026-08-02 — two sweeps of one
> dataset at one step, differing only in noise seed, both at R² = 1.0000, produced half-widths an order of
> magnitude apart. So the deadband is set **at or above the measured run-to-run spread of the recommended step**
> on the bank's S0 control, which closes F21's owed instrumentation and sizes the deadband in the same pass.

### §3.4 The acceptance rule, fixed before the arm runs

Measured with `synth-validate`, which is the recommender's own convergence driver, over the S0/S1/S2 scenarios
wave 7's F18 arms used, one binary, `--settings` pinned, control arm = the shipped recommender.

> **RULE S, fixed in advance.** The deadband ships only if **all** hold:
> - **S1** rounds-to-converge is **not worse** on any cell, and **better on at least one**;
> - **S2** the A3 final-step assertion passes on **at least as many** cells as the control — a deadband that
>   converges by stopping short of the right answer is worse than the runaway;
> - **S3** on the field session's own geometry, replayed as a unit test, the sequence **terminates** and does so
>   at a step within the deadband of 459–482.
>
> **S2 is the clause that can kill it**, and it is the one that must not be relaxed. A deadband is a device for
> declaring victory; the instrument that says whether victory was real is the assertion it could suppress.

**What this would do if the deadband were completely broken:** set it to 100 % and every cell converges in one
round with A3 failing everywhere — S1 passes, S2 collapses. S2 is therefore load-bearing rather than decorative,
which is the check wave 9's lesson list asks of every harness.

### §3.5 Explicitly NOT in scope, with reasons

- **F25's fit-quality gate** (a degenerate fit at R² < 0 still ANSWERS) is a different defect in the same method
  and is left open. It is not made worse by either half above.
- **F26's deferral bound** is the wizard's binning-first update ORDERING, downstream of F22, not the
  recommender's arithmetic. Untouched; F26 already says a passing bank must not be read as four entries closed.
- **F18's `W_detect` default** stays OFF. Wave 7's arms returned a null — the bank cannot exercise the defect —
  and nothing in this wave changes that.

---

## §4 — Deferred, with reasons rather than predictions

- **F52(d)** (a cost term in `J`) has lost most of its motivation: PR #187 made per-layer wavelet cost nearly
  flat, so `StructureLayers` is no longer a cost driver and **a cost term added now would penalise an artifact**.
- **F46(b)** (present the binning recommendation) — nothing depends on it.

*Wave 9's lesson 1 applies to both: a deferral priced on a PREDICTION is not priced. Neither of these is deferred
on a forecast about what a later check would say — F52(d)'s motivation is measurably gone, and F46(b) has no
dependents.*

---

## §5 — Traps this wave inherits, and how each is handled

| trap | handling |
|---|---|
| `D:\hf_w9\f32_arms.sh` is `FANOUT=1` and must stay so until F55 is fixed | every wave-10 pass is sequential; §2 is the work that would lift it |
| the wing threshold (0.20) rests on TWO datasets | that is item 1 |
| `ContinueOptimizingAdviceText` reuses φ = 0.50 as a **DIAGNOSTIC** | never to be confused with the CONSTRAINT of the same value, which wave 9 refuted as a default (σ_focus ×2000 on `vsn07`) |
| `score_f32.py` coerces Newtonsoft `"NaN"` STRINGS | `Panos` has a genuinely degenerate σ fit in every arm; any new scorer inherits both facts |
| the banks hold wave 9's arm A landing, measured on version 1 | §0.3 — the gate and item 1 both run at that same invocation |
| the csproj PostBuild xcopy fails **silently** when NINA is running | a build reporting "Copying …" may have deployed nothing; verify before believing an in-app test |
| wave 8's AF changes are **still unconfirmed in the app** | ask before assuming they work |

## §6 — Measurement discipline this wave is bound by

Each line cost real time to learn, and they are repeated because wave 9 caught itself with several of them.

1. **Write the falsification rule down BEFORE running.** RULE G voided a 7-hour arm; applying it as written was
   the whole value of running it. RULE P and RULE S are in §1.2 and §3.4, above the results.
2. **The population that did not motivate a hypothesis is the one that tests it.** This fired twice in wave 9 and
   both times the conclusion reversed. Use full-bank populations for anything that decides a default.
3. **Verify the instrument is CONNECTED before believing a null.** An instrument that is not connected reports
   perfect agreement (`WSLENV`, §2.5).
4. **A FLOP count is not a benchmark.** Wave 9 rewrote a kernel on an arithmetic argument and shipped something
   50 % slower, because the stage is memory-bandwidth bound.
5. **Pin `--settings` on every arm** (F42) and never rebuild an arm's exe directory (F53).
6. **`BaselineJ` is a FREE control** (F41) — and a number agreeing *too* well is also an instrument failure.
7. **Check whether a cheap instrument already exists, and whether the expensive step is necessary at all**,
   before planning it. F19(c) cost minutes and cancelled a re-render.
8. **Ask of every harness: what would this do if the thing it checks were completely broken?** §1.4 and §3.4
   answer it in writing.
9. **Verify your own test comments' discriminating power.** Wave 9 caught two overclaims of its own that way.
10. **A gate's false-negative count is an UPPER BOUND** on what relieving it buys, not an estimate (F50).
11. **On CI, verify the test COUNT, not the tick** (F37 — a native host crash reports ZERO failures with a
    REDUCED count), and remember an ABSENT check is more dangerous than a red one.
