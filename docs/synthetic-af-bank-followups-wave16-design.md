# Synthetic AF bank — followups wave 16 (design / pre-registration)

Plan: [`plans/synthetic-af-bank-followups-wave16-plan.md`](../plans/synthetic-af-bank-followups-wave16-plan.md).
Charter: [`docs/waves16-21-handoff-prompt.md`](waves16-21-handoff-prompt.md).
Wave 15: [`docs/synthetic-af-bank-followups-wave15-results.md`](synthetic-af-bank-followups-wave15-results.md).
Wave 14: [`docs/synthetic-af-bank-followups-wave14-results.md`](synthetic-af-bank-followups-wave14-results.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE — the inputs, fixed before any wave-16 data exists
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. This pre-registration is committed **before** the code and **before** any arm |
> | binary | `D:\hf_w16\exe` — **ONE binary for the whole wave**, built **after** all of this wave's C# is merged. sha256 of `TestApp.dll` and `NINA.Joko.Plugins.HocusFocus.dll` plus `BuildId` recorded in the results doc at build time |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`** — `MaxOutlierRejections` explicitly **0**. The only settings file this wave uses |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = **truth**), `D:\Autofocus Bank` (19 runs) |
> | F15 control | `/mnt/d/hf_w16/bank_landing_fingerprint_BEFORE.json`, 42 landings, **re-written immediately before the first arm**. Verified current at pre-registration time: **42 of 42 byte-identical**. `--update-run-folder` is passed nowhere |
> | prior `BuildId`s | `5cb7e474…` (w11), `103d61c4…` (w12), `62334f10…` (w13), `084e3485…` (w14 gate), **`df3a867d6fc74204a0dbfc75060b9a96`** (w15). Wave 14's `exe_floor` wrote no landing, so its id was never recorded |
> | suite baseline | **3781** on this branch |
>
> **ONE BINARY, AND THE ORDER IS THE POINT.** Wave 14 needed two binaries because item 2's code did not exist
> when the gate binary was frozen, and had to disclose that RULE G14 was not a control on the binary its second
> item ran. Wave 16 ships code, so the order is fixed here and is not negotiable afterwards:
>
> > **code first → build ONE binary → gate → probe → rungs.**
>
> **No second binary is required and none is planned.** Every line this wave ships (`SemScaleSpec`, the
> `RejectionTest` overload, the two `StarDetectorParams` printouts, `SemScaleArgs`) is authored before the build,
> and every arm exercises the same tree. If a second binary becomes necessary anyway, that is a **finding** and it
> is disclosed in the results doc in wave 14's own words — it is not quietly done.
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3); the
> authorisation is worth 1.33×, not 4× ([F60](followups.md)). Wave 5's φ table is quoted nowhere.

## The items

| item | question | rule | cost |
|---|---|---|---|
| **the gate** | does the coordinate system reproduce on a **seventh** binary? | **RULE G16** (§1) | ~42 m |
| **A** | F45(b) in the **right units**: does a criterion in units of the **standard error of the median** separate the cascade from the benign rejections, and is it selective? | **RULE S16** (§3) | ~66 m of arms + ~2 h of code |
| **B** | close [F67](followups.md): do `af-fit` and `optimize` build **different detectors** from one settings file? | **RULE P16** (§5) | ~1 m of arms + ~30 m of code |
| **C** | are the nine UI changes attemptable? | one command (§6) | 0 |

---

## §0 — RULE F14 is closed, and nothing below re-opens it

Wave 15 §2.5 recorded that **RULE F14 returns NO VERDICT permanently**, on three grounds, and that its corrected
diagnostic must never be converted into a recommendation. This design honours that literally:

- **No rung of wave 14's MAD-floor ladder is named anywhere below** — not as a baseline, not as a comparison, not
  as support. `score_sem_w16.py` says so in its own output.
- Wave 14's `affit_A0.00` **is** used, in exactly one place: as the **byte reference for this wave's control
  rung** (clause W1). That is a reproduction reference for an *inert-by-default* claim, not a rung under test,
  and it carries no verdict from wave 14 with it.
- Wave 15's **V4′** is applied **prospectively**, to a population that did not exist when the clause was written
  — which is the use wave 15 reserved it for.

---

## §1 — RULE G16, the gate, and this wave's stopping gate

Driver: `/mnt/d/hf_w16/gate_w16.sh`. Eight runs, sequential, `optimize --per-run --max-evals 250`,
`--settings D:\hf_w11\pinned_settings_w11.json` **and** `--profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, both
named in the script header. Machine quiet, no NINA, one `TestApp.exe`.

```
toml999 0.995784 | CWhiteFocus 0.996068 | uneven 0.996368 | muggsie 0.997195
mccomiskey 0.976746 | D18 0.999882 | D19 0.999487 | D20 0.999738
```

**G16-1 (the only PASS/FAIL clause).** All eight `BestJ` reproduce the table above **to 6 dp**. Bit-identity to
all sixteen digits is *reported* as the stronger observation; 6 dp is the clause.
**A partial reproduction is a FAILURE, not a warning, and it STOPS THE WAVE** — nothing else runs and the wave is
written up as a gate failure.

**G16-2 (population, asserted before scoring).** `aggregate_summary.json produced: 8, expected: 8`. A short
population makes every number below a non-population statistic; the driver exits non-zero rather than scoring.

**G16-3 (the free controls, as FIELDS, read ACROSS the arm).** `prov_w16.py`:

| field | clause |
|---|---|
| `BuildId` | exactly **one** distinct value across the eight, and **novel** against every prior wave's recorded id |
| `DetectorVersion` | **2** on all eight, read as the FIELD ([F66](followups.md)) |
| `ProfileId` | contains `ce3f3e63-…` on all eight **and** exactly one distinct value (containment **and** cardinality) |
| `FitInputs` | one distinct value, `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` |
| `ConcurrencyCheck` | `exclusive` on **all eight**. One landing's `exclusive` proves nothing: `WaitOne(0)` is won by exactly one of N contenders |
| `BaselineJ` | reproduces wave 11 ([F41](followups.md)'s free control) |

**G16-4 (the scorer is demonstrated in both directions before it is quoted).**
`python3 /mnt/d/hf_w16/prov_w16.py --self-test /mnt/d/hf_w16/gate` runs the checks on the real landings **and**
on a copy with one landing's `ProfileId` rewritten, **asserting the mutation happened** before believing the
FAIL. A self-test that passes against any input certifies nothing; this one refuses to certify itself when
pointed at another wave's arm.

**What the gate does and does not control.** It runs at `MaxOutlierRejections = 0`, where `RejectionTest` is
**never called**. So a G16 PASS proves the new `SemScaleSpec` parameter did not reach anything *outside* the
rejection path — model selection, weighting, the search. It says **nothing** about whether the parameter is inert
*inside* the rejection path. That is clause **W1** (§3), and the two claims are never collapsed into one.

---

## §2 — Item A: what changes in the code, and the `N*` plumbing answer

### §2.1 The measurement wave 14 left, restated

`MathUtility.RejectionTest` ranks points by `z = |r − median(r)| / MAD(r)` with `r = w(x)·(Y − f(x))` and
`w = 1/σ`, where **σ = 1.483·MAD(HFR) over the detected stars** and **Y is the median HFR**.
`AlglibHyperbolicFitting.cs` documents its own σ as *"the star-ensemble scatter … which overstates the
uncertainty of the plotted median HFR by roughly √(detected stars)"*. So in units of the standard error of the
quantity actually being fitted,

```
s = |r| · √N*          (N* = af_fit_points.csv's `Stars`, printed since wave 6)
```

Wave 14's 42 rejections across 39 runs, recomputed in those units (`/mnt/d/hf_w14/stageA/sem_audit.tsv`):

| bucket | measured |
|---|---|
| runs with `ρ = σ₃/σ₀ > 2` — **`caboose` alone**, ρ = 45.18 | **3 of 3** rejections at **s < 1** (0.1010, 0.0403, 0.0000) |
| runs with `ρ ≤ 1.10` | **32 of 33** at **s ≥ 1** (97 %); the exception is `D08`'s third at 0.3883 |
| **`D16_esprit550_ha3`**, F45's headline case where the discarded point **is** the generator's true focus | **s = 8.7260** |

> **The honest half, carried in the same breath and not in a footnote.** The SEM criterion separates *"the test is
> destroying a near-perfect fit"* from *"the test is flagging a real deviation"*. It does **not** vindicate F45's
> original complaint: no threshold anywhere near 1 touches `D16`, and none is proposed that would.

### §2.2 The change — off by default, the `MadFloorSpec` shape reused, not a second mechanism beside it

**`SemScaleSpec`** (`Joko.NINA.Plugins.HocusFocus/Utility/SemScaleSpec.cs`), a readonly struct whose `default`
is `None`, with two mutually-exclusive families built by private-ctor factories — the exact invariant shape
`MadFloorSpec` uses, so "which family is this?" is never a question about how a caller filled two loose fields:

| family | semantics | monotone? |
|---|---|---|
| **V — veto** | after `RejectionTest` selects the argmax point, the rejection is **suppressed** when that point's `s = \|r\|·√N*` is `< t` | **yes.** The argmax cannot move; no rejection is added or redirected; each model's set is a **prefix** of its unfloored set |
| **R — rank** | `errors_i ← r_i · √N*_i` **before** the median/MAD | **no.** `√N*` varies within a run, so the argmax **can** move |

Threading, and it needs **no change to any production class**:

```
af-fit  --sem-veto <t> | --sem-rank      (SemScaleArgs, exact flag match, mutually exclusive
                                          with each other AND with --mad-floor/--round1-floor)
   └─ AlglibHyperbolicFitting.SelectBestModel(..., MadFloorSpec madFloor = default,
                                              SemScaleSpec semScale = default,
                                              Func<double,double> starCounts = null)
        └─ FitWithOutlierRejection(..., madFloor, semScale, starCounts)
             └─ MathUtility.RejectionTest(points, fitting, confidence, weights,
                                          scaleFloor, semScale, starCounts,
                                          out scaleUsed, out semOfSelected)
```

`starCounts` is a `Func<double,double>` keyed by `p.X`, **exactly the shape `AlglibHyperbolicFitting.BuildResidualWeights`
already uses** for the 1/σ weights (`AlglibHyperbolicFitting.cs:116-127`). That choice is deliberate and is
recorded with its rejected alternative:

> **Rejected alternative: ride `N*` on `ScatterErrorPoint.Tag`.** `ScatterErrorPoint` is OxyPlot's, non-sealed,
> with a 7-arg ctor and a public `Tag` setter, so it *can* carry the count. It was rejected because
> `WeightRegularization.Regularize` **rebuilds** every point (`WeightRegularization.cs:58, 68`) and would have to
> be edited to forward `Tag` — a change to a **production** class on the **production** path, in a wave whose
> whole claim is that the product is bit-identically unchanged at the default. The side map touches no production
> class at all. *The cheapest change is not the one with the fewest lines; it is the one that cannot reach the
> product.*

Three properties are load-bearing and are asserted in code, not hoped for:

1. **`SemScaleSpec.None` is the shipped behaviour, bit-identically.** The existing 6-argument overload delegates
   with `SemScaleSpec.None, starCounts: null`, and the new code path is guarded on `!semScale.IsNone` so the
   default path does not even evaluate a `Math.Sqrt`. One implementation, not two that must be kept in step.
2. **A SEM spec with no star counts THROWS.** If `semScale` is not `None` and `starCounts` is `null`,
   `RejectionTest` raises rather than silently behaving like `None`. *A criterion that quietly does nothing when
   its input is missing is F66's shape at the API boundary.*
3. **`semOfSelected` is always the UNSCALED `|r|·√N*` of the selected point**, at every rung including family R,
   so the number printed is comparable to wave 14's `s` across the whole ladder.

**Nothing is printed into `af_fit_summary.txt` when the spec is `None`** — which is what lets clause W1 be a
**byte** comparison against wave 14's control rung rather than only a field comparison. When the spec is live,
one ASCII-only line per round is added:

```
  SEM detail   : spec=<tag>  N*(sel)=<int>  |r|(sel)=<G17>  s=<G17>  verdict=<KEPT|SUPPRESSED|N/A>
```

> **ASCII-ONLY, and this is a measured trap, not a style preference.** `HarnessSettingsStore`'s preset-override
> warning writes a Unicode `→`; on this machine the console code page cannot encode it, so a **redirected** log
> receives the single byte `0x1A`. A parser written against the source string matches **0 of 40** perfectly good
> wave-15 logs and reports "could not look" on the entire population — which is exactly what the first run of
> `score_params_w16.py` did during pre-registration. Every line wave 16 prints for a scorer to read is ASCII.

### §2.3 Is `N*` available in the PRODUCTION engine without new plumbing? **No — and the answer changes a price**

This was asked because it decides whether a SEM criterion could ever ship. Traced end to end:

| where | what is there |
|---|---|
| `HocusFocusStarDetection.BuildStarDetectionResult` **:804-806** | `var (hfrMedian, hfrMAD) = hfrStars.Select(s => s.HFR).MedianMAD(); result.AverageHFR = hfrMedian; result.HFRStdDev = hfrMAD;` — **this is where production's Median-mode (Y, σ) is formed, and N\* is right there**: `hfrStars.Count` (the σ's true denominator) and `result.DetectedStars` (**:759**) |
| `AutoFocusEngine.EvaluateExposure` **:901** | `return new MeasureAndError() { Measure = analysisResult.AverageHFR, Stdev = analysisResult.HFRStdDev };` — **the count is dropped here** |
| `MeasureAndError` | `NINA.WPF.Base.ViewModel.AutoFocus`, from the **NINA.Plugin NuGet**. A `struct` with exactly two `double`s. Not subclassable, not extensible |
| `AutoFocusRegionState` **:207-208** | `Dictionary<int, MeasureAndError> MeasurementsByFocuserPoint`, `Dictionary<int, List<MeasureAndError>> SubMeasurementsByFocuserPoints` — typed on the struct that lost the count |
| `AutoFocusEngine` **:1071** | `new ScatterErrorPoint(fp.Key, fp.Value.Measure, 0, SafeDisplayError(fp.Value.Stdev))` — **the fit's points are built here, and N\* is genuinely absent.** `imageState` holds the detection result for the frame that *just* completed, not for the other 8–15 positions already in the dictionary |

> **So: `N*` is available where σ is COMPUTED and gone by the time the fit's points are BUILT.** Threading it to
> the product needs (a) a parallel count map on `AutoFocusRegionState`, cleared with the measurements; (b) a
> **pooling rule** for multi-frame points — `AverageMeasurement` (`CvImageUtility.cs:891-921`) already pools σ as
> `√(Σσ²/k)/√total`, i.e. it **already divides by √frames**, so a naive σ/√N* would double-count and the rule has
> to be decided, not assumed; (c) the same again in `RunEvaluationData` (**:745, :783, :788**), which is the
> **optimizer's** independent point-building path; and (d) a decision between `DetectedStars` and `hfrStars.Count`
> — they differ whenever saturated stars are excluded, and only the second is the σ's actual denominator.

**What this does to the brief's ~2 h.** The brief priced *"thread `N*` from `MeasurePoint` construction through
the fit to `RejectionTest`"* at ~2 h. Two corrections:

- **There is no `MeasurePoint` type in this repo.** The harness's row type is `AfFitDiagnosticRunner.PointRow`
  (which already carries `Stars`); production's carrier is `MeasureAndError` → `ScatterErrorPoint`.
- **~2 h is right for the HARNESS change and wrong for the PRODUCT change.** The harness path needs no production
  plumbing at all, because `af-fit` already has `rows[i].Stars` at the point where it builds `rawPoints`. The
  product path needs (a)–(d) above. **Wave 16 does the harness change only**, which is what "off by default,
  exactly as wave 14's `MadFloorSpec`" means — and the consequence is stated up front in §3's branch table: **the
  best outcome this wave can reach is a costed RECOMMENDATION, never a ship.** Pricing (a)–(d) is itself part of
  the wave's deliverable and is entered in the register with the recommendation.

---

## §3 — RULE S16

> ### RULE S16 — in full
>
> **Scope.** Six rungs of `af-fit` over the same 39 runs (20 synthetic + 19 real) at S0, `--profile-id`-pinned,
> `--params-from __PINNED_NO_SAVED_PARAMS__`, `--confidence 0.95`, `--weighted true`, explicit `--step-size`.
> Rungs, fixed here: **`N`** (control, `SemScaleSpec.None`), **`V0.07`**, **`V1.00`**, **`V2.00`**, **`V4.00`**
> (family V), **`R`** (family R). `V1.00` is the **named criterion** — one standard error of the plotted median,
> which is the unit F45(b)'s own wording implies once the error bar is the right one.
> **The rungs' spacing is derived from wave-14 data already on disk (§4.1) and is therefore not independent
> evidence; only the behaviour AT each rung is new.**
>
> **VALIDITY GATES. Any failure ⇒ RULE S16 returns NO VERDICT, and the wave NAMES the failing gate. Everything
> below a failed gate is a diagnostic and is written up as one.**
>
> | gate | threshold, fixed here |
> |---|---|
> | **W1 — the control rung** | Rung `N`'s **39** `af_fit_summary.txt` are **byte-identical** to wave 14's `affit_A0.00`, and **0 field differences** across 39 runs × 4 budgets × 7 fields (winner model, `#rej`, rejected positions, σ_focus, redχ², R², `minPos`). **Tie-break, fixed now and not after the data:** if bytes differ but all 39 × 4 × 7 fields match, W1 is decided by the **FIELD** comparison and the byte difference is reported by name as a finding about the report format. Population: 39 of 39 on both sides, or W1 fails as UNEVALUATED. **This is the only clause in the wave that can catch a `SemScaleSpec` that is not inert at its default.** |
> | **W2 — population by count** | Per rung: **20 syn + 19 real = 39**, each with `run.log`, `af_fit_summary.txt`, and an `af_fit_points.csv` **carrying a `Stars` column**. A short population makes **that rung** UNEVALUATED — never "fewer rejections fired". |
> | **W3 — `N*` reached the test** | On every round of every non-`N` rung: the printed `s` equals `\|printed r\| · √(Stars from the CSV at that position)` to 1e-6 relative, **and** the printed `N*` equals the CSV's. Three independently printed quantities, tied. |
> | **V4′ — PROSPECTIVE** | Carried forward **verbatim** from `/mnt/d/hf_w15/score_f14_w15.py`: (i) subset, (ii) cardinality (`≤` control's, and `≤ B`), (iii) monotone in budget, (iv) budget-0 empty both sides, (v) NaN-safe suppression identity. **NOVEL-CONSENSUS ([F65](followups.md)) is REPORTED, never a bar.** Population: **4 V-rungs × 39 runs × 3 budgets = 468** triples and 156 budget-0 rows. **Family R is EXCLUDED by pre-registration** — the subset lemma is not proved for a non-monotone rescaling, and applying a gate to a mechanism it was not proved for is wave 14's V4 exactly. |
> | **V4′-CONFORM** | This wave's copy of V4′ reproduces wave 15's published result on wave 14's rungs: **585 triples, 0 violations, 2 NOVEL-CONSENSUS, 195 budget-0 rows**. An instrument carried forward must be shown to be the **same** instrument. |
>
> **DECISION CLAUSES. Read only if every validity gate passes.**
>
> | clause | threshold, fixed here |
> |---|---|
> | **S16-A — separation** | At **`V1.00`**: (a) rejections surviving on runs whose control-rung `ρ > 2` **= 0**; (b) rejections surviving on runs whose control-rung `ρ ≤ 1.10` **≥ 30 of 33**. `Panos_attempt01` is **UNEVALUATED BY NAME** (degenerate σ ⇒ `ρ` = NaN). **(a) is n = 1 on the high side by construction — `ρ > 2` is `caboose` and nothing else — and is labelled as such wherever it is quoted.** |
> | **S16-B — containment, on statistics with no `n − p` term, against a reference the treatment CANNOT MOVE** | Per run, against **that run's own budget-0 row at rung `N`** (budget 0 never calls `RejectionTest`): `ΔR² = R²(b3, rung) − R²(b0, N)` and `κ = redχ²(b3, rung) / redχ²(b0, N)`. **PASS at a rung iff over all 39 runs `min ΔR² ≥ −0.005` AND `max κ ≤ 3.0`.** σ_focus and `ρ` are **REPORTED ONLY and are never a bar** ([F62](followups.md): σ_focus is anti-informative when a rejection is what changed it, and it inflates mechanically as `n − p` shrinks). |
> | **S16-C — selectivity** | `N_fire(budget 1) ≥ 1` **and** `N_fire(budget 3) ≥ 1` over the 39 runs. Separates a criterion from an off-switch. |
> | **S16-D — `caboose` at budget 3, three-valued** | **CONTAINS-SELECTIVELY** (budget 3's catastrophe repaired **and** budget 2's *benign* rejection of `7275` retained) / **CONTAINS-BLUNTLY** (repaired, benign also suppressed) / **DOES NOT CONTAIN**. |
> | **S16-E — out of sample, on the predecessors' bar, unchanged** | `e = \|minPos − truth\| / step` over the 20 synthetic datasets, `truth = renderRequest.OptimalFocuserPosition`, read from wave 13's own `affit_syn_score.txt` rather than re-derived, at budgets 1 and 3. **(i) VETO:** median `\|Δe\| ≥ 0.10 step` at a rung vetoes that rung. **(ii) MAGNITUDE:** reported per dataset **with its sign** (toward / away from truth). **No ratio column is printed** (wave 15's Lesson 5). **(iii) ALARM:** any `e ≥ 1.0 step` anywhere is a new register finding. |
>
> **THE BRANCH TABLE. Every measurement in §4.2 has one of these as its consumer.**
>
> | # | outcome | condition |
> |---|---|---|
> | **1** | **NO VERDICT** | any validity gate fails. The gate is named. |
> | **2** | **RECOMMEND a SEM veto at t = 1.00** — **a costed recommendation, NOT a ship** | at `V1.00`: S16-A(a) **and** S16-A(b) **and** S16-B **and** S16-C **and** S16-D ∈ {SELECTIVELY, BLUNTLY} **and** no S16-E(i) veto **and** no S16-E(iii) alarm. **Nothing in the product changes**: the product plumbing for `N*` does not exist (§2.3) and its price rides with the recommendation into the register. |
> | **3** | **RECOMMEND AGAINST** | S16-C fails at **every** rung that passes S16-B (the criterion is an off-switch), **or** S16-D is DOES NOT CONTAIN at **every** rung (it never reaches the case it exists for). |
> | **4** | **NO RECOMMENDATION, MECHANISM RECORDED** | the clauses split. **A terminal state, not a failure**, and no rung is named a winner. |
> | **5** | **FAMILY R, separate and never eligible for (2)** | **R-INERT** (its consensus sets equal rung `N`'s at every budget on all 39) / **R-SUPPRESSES** (strict subsets, no new positions) / **R-REDIRECTS** (**at least one run rejects a position rung `N` never rejected at any budget**) / **R-UNEVALUATED**. **R-REDIRECTS is the measurement only family R can produce**, and it is what decides whether a future wave pursues re-ranking at all. |
>
> **The rule is applied as written. An unsatisfiable clause is a FINDING, not a licence to re-decide one**
> (wave 14 Lesson 3). `score_sem_w16.py` prints the branch it took and refuses to print a winner in outcome 4.

---

## §4 — SATISFIABILITY, in two parts

Wave 15's central criticism of RULE F14 was that *its strongest measurement was unreachable by any branch written
to consume it*, and that the section written to prevent unreachable branches contained one. This section
therefore does **two** jobs, not one.

### §4.1 Part (i) — every clause's maximum attainable value, with the arithmetic, before the data

**The s-distribution over wave 14's 42 rejections** (`/mnt/d/hf_w14/stageA/sem_audit.tsv`), sorted:

```
0.0000 0.0000 0.0403 0.1010 0.3883 | 1.0493 1.2934 1.3282 1.5075 1.5920 1.6262 1.8206 1.8328
2.1285 2.1355 2.2616 2.7380 3.0332 3.0762 3.1939 3.5975 3.8292 3.8869 4.0907 4.1524 4.3377
4.5597 5.0219 5.3793 6.9262 7.6675 8.6853 8.7260 10.6876 14.6902 16.3351 18.2715 19.0993
24.2482 25.6755 47.5143 79.7824
```

**Why these four rungs and not others.** Under family V a rung truncates each model's rounds at its first round
with `s < t`, so the rung's *effect* is a step function of `t` whose steps are exactly the s-values above. Applied
as a prefix truncation to the winning-model traces:

| rung | rejections removed (of 42) | runs touched |
|---|---|---|
| `V0.07` | **3** | `caboose` (from round 2), `Panos` (round 3) |
| `V0.50` | 5 | `caboose` (round 1), `Panos`, `D08` |
| **`V1.00`** | **5** | *identical to `V0.50`* |
| `V2.00` | **14** | + `D08`, `D11`, `D12`, `Linwood`, `lumos`, `fmeschia`, `timmer` |
| `V4.00` | **26** | + `D17`, `FlyData`, `bobp`, `cwhite_2026` |

> **`V0.50` and `V1.00` are the same rung, and that is why `V0.50` is not on the ladder.** The largest gap
> anywhere below s = 5 is **[0.3883, 1.0493)**, and it straddles 1.0 — so any threshold in that whole interval
> produces the identical population. A ladder of {0.5, 1.0, 2.0} would have spent 11 minutes measuring the same
> rung twice. **`t = 1.00` is chosen because it is the unit** — one standard error of the plotted median — and it
> happens to sit in the widest natural gap; the unit came first and the gap is a bonus, not the reason.

**S16-D's CONTAINS-SELECTIVELY branch, and the arithmetic that makes it reachable at all.** `caboose`'s printed
trace rejects **`7275` at round 1 with s = 0.1010** — the *benign* rejection, the one that takes σ_focus
0.364 → 0.172 and R² to 0.999999 — and **`6975` at round 2 with s = 0.0403**, the one that destroys the fit.
Because suppression truncates a prefix, a threshold **strictly inside [0.0403, 0.1010)** keeps round 1 and kills
round 2, which is precisely CONTAINS-SELECTIVELY. **The midpoint is 0.0707, hence `V0.07`.**

> **`V0.07` is a caboose-derived PROBE rung and is named as one wherever it appears.** It is `n = 1` by
> construction: its threshold was read off one run's own two s-values, so it can **never** be recommended on its
> own, and outcome 2 is defined on `V1.00` alone so that it cannot be. Its job is to answer a **mechanism**
> question — *is selectivity attainable by ANY threshold in this family?* — that no MAD floor could reach, since
> wave 14 measured every floor rung that repaired budget 3 as also suppressing budget 2.
> **And reachability is plausible, not proved:** the window is derived from the *winning model's* trace, while the
> consensus is an intersection over four models that need not agree on round order ([F65](followups.md)). At all
> **three** other V rungs CONTAINS-SELECTIVELY is **foreclosed**, because `t ≥ 0.1010` suppresses round 1 too.

**S16-E's materiality clause is FORECLOSED for family V, by arithmetic, and is retained as a veto only.**
Under pure suppression each synthetic dataset's budget-3 row can only become one of its own budget ≤ 3 rows at
rung `N`. Computed from wave 14's `affit_A0.00` against wave 13's truth table — **only 5 of 20 synthetic datasets
reject anything at budget 3 at all**:

| dataset | `e(b0)` | `e(b1)` | `e(b2)` | `e(b3)` | reachable spread |
|---|---|---|---|---|---|
| `D01_ultrawide_40mm` | 0.00111 | 0.00111 | 0.00111 | 0.00111 | 0.00000 |
| `D08_c11_2800mm` | 0.01829 | 0.01951 | 0.01951 | 0.01951 | 0.00122 |
| **`D12_c14_585_afbin2`** | 0.00142 | 0.01135 | 0.01135 | 0.01135 | **0.00993** |
| `D16_esprit550_ha3` | 0.00067 | 0.00200 | 0.00200 | 0.00200 | 0.00133 |
| `D17_cdk14_oiii5` | 0.03000 | 0.03000 | 0.03000 | 0.03000 | 0.00000 |
| *the other 15* | — | — | — | — | **0.00000** |

> **Maximum attainable `\|Δe\|` for family V, anywhere on this bank: 0.00993 step, on `D12`, against a 0.10-step
> materiality floor — foreclosed by a factor of 10.1.** The **median** over 20 datasets is 0.00000 and cannot be
> anything else, since 15 of 20 have zero reachable spread. **S16-E(i) therefore cannot fire for family V and is
> NOT a decisive clause; it is a veto, exactly as wave 14's C4 was, and it is labelled so in the rule.** The
> decisive clauses are phrased on **magnitude and mechanism** — S16-B's `ΔR²`/`κ` against a reference the
> treatment cannot move, and S16-D's three-valued containment.
>
> **For family R the bound does NOT apply**, because a re-ranking can select a position no budget row ever
> rejected. **S16-E(i) is live for `R` and `R` alone.** That is not an accident of the ladder: it is why `R` is on
> it, and why `R` is on the never-drop list.

**Maximum attainable, every remaining clause:**

| clause | max attainable | attainable? | failable? |
|---|---|---|---|
| G16-1 | 8 of 8 to 6 dp | yes — six binaries | yes — one differing value fails it |
| W1 | 39 byte-identical, 0 field diffs | **demonstrated before the wave**: A0.00 vs itself → PASS 39/39 | **demonstrated**: A0.25 vs A0.00 → FAIL |
| W2 | 39 of 39 × 6 rungs | yes — wave 14 hit 39/39 on six rungs | yes — a short arm |
| W3 | every round tied | yes | **demonstrated** on constructed input, three failure shapes |
| V4′ | 468 triples, 0 violations | yes — family V is a prefix truncation | **demonstrated**, four violation shapes |
| V4′-CONFORM | 585 / 0 / 2 / 195 | **already run at pre-registration: PASS** | yes — any edit to the clause breaks it |
| S16-A(a) | 0 of 3 | yes (all three caboose s < 1) | yes |
| S16-A(b) | 33 of 33 | yes | yes (an aggressive rung drops below 30) |
| S16-B | `ΔR² = 0`, `κ = 1` | yes, when the cascade is fully suppressed | yes — rung `N` itself fails it (`ΔR² = −0.0852`, `κ ≈ 1.2e4`) |
| S16-C | 7 / 13 (the control's counts) | yes | yes — a strong rung reaches 0/0 |
| S16-D | CONTAINS-SELECTIVELY | **only at `V0.07`** (above) | yes — all three values reachable, demonstrated |
| S16-E(i) | — | **foreclosed for V; live for R** | yes for R |
| S16-E(iii) | — | yes | wave 15's max was 0.02921, 34× under the alarm |
| P16 | P-a / P-b / P-c / UNEVALUATED | all four demonstrated | yes |

### §4.2 Part (ii) — every measurement this wave produces, and the branch that consumes it

This is the half wave 15 says the previous designs omitted: *"a measurement with no reachable consumer is a
design defect."*

| measurement | produced by | consuming branch | is that branch reachable? |
|---|---|---|---|
| 8 × `BestJ` | gate | **G16-1** | yes; PASS demonstrated six times, FAIL by any differing value |
| `BuildId` / `DetectorVersion` / `ProfileId` / `FitInputs` / `ConcurrencyCheck` / `BaselineJ` | gate landings | **G16-3**, and G16-4 self-tests both directions | yes, **demonstrated** |
| 42 bank landing sha256 | fingerprint | the F15 control | yes; PASS already observed at pre-registration |
| rung `N`'s 39 budget tables | af-fit | **W1** | yes, **both directions demonstrated on wave-14 data** |
| per-rung population counts | `verify_affit_w16.sh` | **W2** | yes |
| per-round printed `s`, `\|r\|`, `N*` | af-fit | **W3** | yes, both directions demonstrated |
| each V rung's consensus sets vs rung `N` | scorer | **V4′**, then **S16-C**, **S16-D** | yes |
| the s of every surviving / suppressed rejection | scorer | **S16-A** | yes |
| `ΔR²`, `κ` per run per rung | af-fit summary | **S16-B** | yes; rung `N` fails it, so the clause discriminates |
| σ_focus, `ρ` per run per rung | af-fit summary | **reported only** — F62's warning attached, never a bar | n/a by design, and stated |
| `caboose`'s budget-2/3 rows at each rung | af-fit summary | **S16-D** | SELECTIVELY reachable **only at `V0.07`** (§4.1); BLUNTLY and DOES-NOT-CONTAIN reachable everywhere |
| `e` per synthetic dataset per rung per budget | scorer vs truth | **S16-E(i)** veto, **(ii)** magnitude, **(iii)** alarm | (i) **foreclosed for V by arithmetic and retained as a veto only**; **live for R**. (ii) and (iii) reachable at every rung |
| family R's consensus sets | scorer | **branch 5**, R-INERT / R-SUPPRESSES / **R-REDIRECTS** | yes; all four demonstrated on constructed input |
| af-fit's full `StarDetectorParams` | af-fit run.log | **P16** (P-a / P-b) | yes |
| optimize's full `StarDetectorParams` | gate + probe logs | **P16** (P-a / P-b) | yes |
| the wave-15 knob-diff re-score | wave-15 logs | **P16-b** | yes; already run, reproduces 205 of 205 |
| the `N*` plumbing price (§2.3) | source read | rides with **branch 2**'s recommendation into the register | yes — branch 2 is reachable (§4.3) |
| `query session` | one command | §6 | yes, either way |

**Two measurements are deliberately consumed by "reported only" and both say so in the rule**: σ_focus/`ρ`
(F62) and V4′'s NOVEL-CONSENSUS count (F65). *Naming a measurement as non-decisive in advance is not the same
defect as leaving one with no consumer* — the defect is a branch nobody can reach, not a number nobody votes on.

### §4.3 Both branches of every clause, checked — not just the PASS value

Wave 15's G-d could never observe *"converted files identical"* because a landing's `CreatedAtUtc` guarantees
they differ. Each clause is therefore asked: **what input makes this return each of its outcomes?**

| clause | what makes it PASS | what makes it FAIL | verified |
|---|---|---|---|
| **W1** | a `SemScaleSpec` that is inert at `None` | a parameter that leaks into the default path | **run before the wave**: A0.00 vs A0.00 → PASS 39/39 byte-identical; A0.25 vs A0.00 → FAIL, 39 byte-differs |
| **W2** | 39 runs with all three artifacts | a truncated read loop (wave 13's I2) or a missing `Stars` column | driver exits non-zero; the `Stars`-header check is separate from the file-exists check |
| **W3** | `s == \|r\|·√N*` on every round | a wrong `s`, a wrong `N*`, or **no `SEM detail` line at all** | 4 constructed cases incl. COULD-NOT-LOOK |
| **V4′** | family V's prefix property | 4 named violation shapes | 8 constructed cases, plus CONFORM on real data |
| **S16-A** | 0/3 and ≥30/33 | cascade rejections surviving, or benign ones over-suppressed | 3 constructed populations |
| **S16-B** | cascade contained | rung `N` itself fails it | 2 constructed cases |
| **S16-C** | anything still fires | 0/0 | 2 constructed cases |
| **S16-D** | all three values | — | 3 constructed cases, one per value |
| **S16-E** | no veto | a ≥ 0.10-step median move; a ≥ 1.0-step `e` | 3 constructed cases incl. the alarm |
| **branch 5** | all four values | — | 3 constructed cases + the absent-rung case |
| **P16** | P-b | P-a; P-c; **UNEVALUATED when only the agreeing half of the population is covered** | 4 constructed cases |
| **branch 2 (RECOMMEND)** | see §4.4 | any conjunct failing | reachability worked through in §4.4 |

### §4.4 Is outcome 2 reachable? Worked through, before the data

A branch table whose best outcome is unreachable is the defect this section exists for. Against wave-14 data:

| conjunct at `V1.00` | reachable? |
|---|---|
| S16-A(a) = 0 of 3 | **yes** — caboose's three s are 0.1010 / 0.0403 / 0.0000, all < 1 |
| S16-A(b) ≥ 30 of 33 | **yes** — 32 of 33 sit at s ≥ 1 |
| S16-B | **yes** — with caboose's cascade suppressed its `ΔR² → 0` and `κ → 1`; the next-worst budget-3 redχ² on this bank is well under 3× its own budget-0 value |
| S16-C ≥ 1 / ≥ 1 | **yes** — `V1.00` removes 5 of 42 rejections; 37 survive |
| S16-D ∈ {SELECTIVELY, BLUNTLY} | **yes** — `V1.00` is predicted BLUNTLY (it suppresses caboose's round 1 as well), and BLUNTLY qualifies |
| no S16-E(i) veto | **yes** — foreclosed from firing at all for family V |
| no S16-E(iii) alarm | **yes** — the reachable max `e` on this bank is 0.03 step |

> **Outcome 2 is reachable.** Note what had to be *repaired* to make it so: an earlier draft of S16-B kept wave
> 14's `max ρ ≤ 1.10` bar, and **`D12` sits at ρ = 1.1304 with its only rejection at s = 1.3282** — a genuine
> 1.3-standard-error deviation the SEM criterion is not claimed to catch. That draft's outcome 2 was
> **unreachable at `V1.00` by arithmetic**, and the fix was not to relax the bar but to **restate the clause on
> the quantity the criterion targets**, on statistics with no `n − p` term, against each run's own budget-0 row —
> a reference the treatment cannot move. *A ship condition that demands what the change was never claimed to do
> is an unreachable branch wearing a bar's clothing.*

---

## §5 — Item B: RULE P16, closing F67

### §5.1 The brief's version of item B cannot close it, and here is why

The register's next step reads *"print the detector's own params from `af-fit`"*. **Printing one side's params is
not a diff.** `optimize` prints five fields today (`Sensitivity`, `StarClippingMultiplier`,
`NoiseClippingMultiplier`, `StructureLayers`, `MeasurementAverage`) against a ~45-field object. So wave 16 prints
the **full field set from BOTH runners through ONE shared formatter**, so the two printouts cannot drift:

```
PARAMS-DUMP <source> BEGIN          <source> ∈ { af-fit/detector, optimize/baseline, optimize/seed }
  <FieldName>=<value>               one per line, sorted by name, InvariantCulture, ASCII only
PARAMS-DUMP <source> END
```

**Cost, re-priced downward.** F67(b) was priced at ~45 m *including a rebuild, a fresh gate and a re-run of both
scoring passes*. In wave 16 the rebuild and the gate are already paid for by item A, and the af-fit side rides
item A's control rung. The **marginal arm cost of item B is ~1 minute** — `STEP P0`, two `optimize` runs on
`D12_c14_585_afbin2` (15 s in wave 15) and `D17_cdk14_oiii5` (24 s), which exist so the diff covers the
**disagreeing** set and not only the agreeing one.

### §5.2 What is already foreclosed from source, before the data

| candidate | status |
|---|---|
| **`PixelScale`** — set per-run by `optimize`, left at the `1.0` default by `af-fit` | **INERT BY PROOF.** `PixelScale` is consumed only inside the `ModelPSF` block (`StarDetector.cs:902, 915`), and **both paths set `ModelPSF = false`**. The gate logs even print `PixelScale=NaN`. |
| **`Region`** | Full on both — `af-fit` by the `StarDetectorParams` default (`IStarDetector.cs:443`), `optimize` explicitly (`OptimizationDiagnosticRunner.cs:357`). **CHECKED, never assumed**: the scorer promotes it to F-live if either side is not Full. |
| **the `MeanOutliers` HFR-σ post-filter** (`HocusFocusStarDetection.cs:725-735`), which runs on the `optimize` path only | inert at S0, where `MeasurementAverage=Median` (read from wave 15's gate log). **CHECKED, never assumed** — and note the scorer flags it even when the two sides are *equal*, because it is the **condition** that makes the field inert, not the equality. |
| **direction** | the extra filters live on the **optimize** path and can only **remove** stars, while F67 measures `af-fit` finding **fewer**. So a params-level explanation was already unlikely, and saying so in advance is what makes a P-b verdict informative rather than deflating. |

### §5.3 RULE P16 — the clauses

> **P16-a — the field-by-field diff.** Fields are partitioned **before the data** into **F-inert-by-proof**
> (consumed only under `ModelPSF`, or purely administrative: `PixelScale`, all `PSF*`,
> `SaveIntermediateFilesPath`, `StoreStructureMap`, `SuppressInfoLogging`, `MaxStarEvaluationParallelism`),
> **F-conditional** (`Region`, `MeasurementAverage`, `ModelPSF` — inert only while a **checked** condition holds,
> and **promoted to F-live** when it does not), and **F-live** (everything else). Verdicts:
>
> | verdict | meaning |
> |---|---|
> | **P-a** | at least one **F-live** field differs. Fields and both values are named. **A PRODUCT FINDING**: two entry points build different detectors from one settings file, and the register records **which one the user gets** — the app runs the `HocusFocusStarDetection` (`optimize`) path; `af-fit` is the harness. |
> | **P-b** | every F-live field is identical and every conditional's condition holds. **The disagreement is DOWNSTREAM of the params**, and F67(c) is narrowed from three candidates to **two, by construction**: (1) the frame loading (`DetectionSource`/`DiagnosticUtil.LoadRenderedImage` vs `RunEvaluationLoader`), (2) the counting stage (`StarDetectorResult.DetectedStars.Count`, **pre**-filter, vs `HocusFocusStarDetectionResult.DetectedStars`, **post** ROI-crop and post-MeanOutliers, `HocusFocusStarDetection.cs:759`). **Also a product finding**, and the more interesting one: two numbers with the same name are different measurements and nothing in either artifact says so. |
> | **P-c** | a dump is missing on one side. **COULD NOT LOOK — not "the params agree".** |
> | **UNEVALUATED** | the population clause is not met (below). |
>
> **P16-POP — the population clause, and it is wave 15's Lesson 1 made mechanical.** The diff must cover **at
> least one dataset from the disagreeing set `{D08, D09, D10, D12, D14, D15, D17}` and at least one from the
> agreeing set.** Any field whose value **varies across runs** on either side is reported as **PER-RUN**, and no
> single-dataset conclusion is generalised over it. *Wave 15's G-c was demonstrated 9-of-9 in both directions on
> `D18` — a member of the agreeing set — and reached 13 of 20.*
>
> **P16-INSTRUMENT-vs-PRODUCT, decided in advance.** If every F-live difference is a field that exists only
> because the **harness** configures `af-fit` — i.e. the live app never sets it differently from itself — then
> F67 is an **instrument** artifact and every future control built on either count must be rebuilt on the
> knob-diff control. Otherwise it is a **product** finding and goes in the register as one.
>
> **P16-b — F67(a), the free half.** Re-score wave 15's G-c on the **knob-diff** control
> (`HarnessSettingsStore.SimpleModePresetOverrides`) instead of on star counts. **Pure Python over wave-15
> artifacts, zero `TestApp` time.** *It is a re-score of a previous wave's data and can never be quoted as new
> evidence* — the same standing wave 15 gave its own item A. Already run at pre-registration and it reproduces
> wave 15's published figure exactly: **40 of 40 runs readable, 205 overridden knobs, 0 could-not-look**, which
> is a free reproduction control on the parse before either number is quoted.

---

## §6 — Item C: the nine UI changes A1–A9

`cmd.exe /c "query session"`, run at pre-registration:

```
 SESSIONNAME               USERNAME                 ID  STATE
 services                                            0  Disc
>                          ghili                     1  Disc
 console                                             2  Conn
 rdp-tcp                                         65537  Listen
```

**`Disc`. NOT ATTEMPTABLE — seventh consecutive wave.** ~30 m on a connected session, wave 13 §3's procedure
unchanged. The controller re-runs the command at wave start; if it reads `Conn`, item 2 becomes attemptable and
the wave spends the 30 m. One line either way.

---

## §7 — Budget

| step | population | rate basis | estimate |
|---|---|---|---|
| **RULE G16** — the gate | 8 runs (5 real + 3 syn), `optimize --per-run --max-evals 250` | wave 15 **41 m 11 s**, wave 14 **41 m 48 s** on the same eight | **~42 m** |
| **STEP P0** — item B's probe | 2 synthetic `optimize` runs | wave 15 measured `D12` at **15 s**, `D17` at **24 s** | **~1 m** |
| **item A** — 6 `af-fit` rungs × 39 runs | 20 syn + 19 real | wave 14 measured **10 m 53 s – 11 m 15 s per rung** on this exact instrument and this exact population, six rungs in 66 m 44 s, 1 % from estimate | **~66 m** |
| scoring (S16, P16, prov, fingerprint) | Python | — | **< 10 m** |
| **wave total `TestApp` wall** | | | **~1 h 49 m** against a 6 h ceiling |
| the suite | `dotnet.exe test`, baseline 3781 | wave 15 **3 m 33 s** | ~4 m |

> **PRICE AN ARM FROM ITS OWN INSTRUMENT, NOT FROM THE GATE'S.** The brief prices arms at ~2.6 m/run
> (all-synthetic) and ~5.2 m/run (mixed). **Those are `optimize --per-run --max-evals 250` rates and they do not
> transfer to `af-fit`**: 39 runs × 5.2 m would price one rung at **203 minutes** against wave 14's measured
> **11**, an 18× error. `af-fit` detects each frame **once** and then evaluates four budgets on those points,
> while `optimize` runs a 250-evaluation search. Wave 15's own lesson was that a rate must come from a
> representative *population*; the prior lesson, which still applies, is that it must come from the same
> *instrument*. **Both estimate and actual are recorded per step in the results doc.**

**Drop order, pre-registered.** If time runs short: **`V4.00` first, then `V0.07`, then `V2.00`.**
**Never dropped:** rung `N` (the control — without it nothing else can be scored), `V1.00` (the named criterion,
the only rung outcome 2 is defined on), and `R` (the only rung whose out-of-sample arbiter is not foreclosed).
**If the gate fails the wave stops** and nothing else runs. Item B's code ships regardless, because it costs no
arm time; item B's *scoring* is the last thing dropped because it is Python.

---

## §8 — What this wave will NOT run, and what it costs

- **The product-side `N*` plumbing** (§2.3): a count map on `AutoFocusRegionState`, a pooling rule for
  multi-frame points that does not double-divide against `AverageMeasurement`'s existing `/√frames`, the same
  again in `RunEvaluationData`, and a decision between `DetectedStars` and `hfrStars.Count`. **~3–4 h plus
  tests**, and it is the entire distance between outcome 2 and a ship. Priced here so the recommendation carries
  its price.
- **A Stage-A committed counterfactual** for family V. Wave 14 built one; its V4 gate is what cost RULE F14 its
  verdict, and repairing a two-defect rule yielded a rule that was still wrong. Wave 16 measures Stage B directly
  and keeps the **control rung**, which is the part of wave 14's apparatus that actually proved inertness.
  **Cost: the wave has no committed prediction to be scored against — it has a control instead.** Stated, not
  hidden.
- **`V0.50`** — arithmetically identical to `V1.00` on this bank (§4.1). ~11 m saved, nothing lost.
- **Family C-style paper refutations of further families.** None are proposed; a family nobody has a mechanism
  for is not worth a rung.
- **F67(c) — identifying the pipeline disagreement's CAUSE.** P16 narrows it to two candidates; naming which one
  needs an instrumented run of both loaders. **~1 h**, a different wave.
- **[F63](followups.md)(b)** — pinning the SHIPPED default rather than `astrodet`'s. **Re-priced at ~0** by wave
  15 (after wave 14 they are the same value). Not done here only because wave 16 pins both explicitly on every
  arm, so nothing is left to move; it is a documentation close and should be taken by whichever wave next edits
  the register.
- **[F59](followups.md)'s five knobs** stay at code defaults. Re-pinning moves the coordinate system RULE G16
  will have reproduced a **seventh** time: **~42 m for a fresh baseline plus the re-derivation of every cross-wave
  comparison**. A decision, not a cleanup.
- **F63(a) on the 19 real bank runs at the landing level.** ~90 m per arm and **it buys no arbiter** — the real
  bank has no truth.
- **The nine UI changes A1–A9**: ~30 m on a **connected** session (§6).
- **F18 / F21 / F25 / F26** — the step-size and sweep-width family, untouched for many waves. **F21's half-width
  instability is the load-bearing one.** Still unpriced, and it is the strongest candidate for wave 17.
- **F61(b), F52(d), F46(b), F54, F50** — nothing depends on them.

---

## §9 — DISCLOSURE: what this design already predicts, and what is genuinely new

Wave 15 required that a wave which can foresee its own outcome says so before the data. This one largely can, and
§4 is why:

- **Predicted:** rung `N` reproduces wave 14's control rung (W1); V4′ passes on family V; S16-A holds at `V1.00`
  (0 of 3 and 32 of 33); S16-D is **CONTAINS-BLUNTLY** at `V1.00`, `V2.00`, `V4.00`; S16-E(i) does not fire for
  family V; **outcome 2 is the predicted branch.** All of it is derived from wave-14 numbers already on disk and
  published, and **none of it can surprise this design's author.**
- **Genuinely new, and not derivable from disk:**
  1. **Whether the separation survives at the CONSENSUS level over all four candidate models.** Every s value
     above comes from the *winning model's* printed trace; the production consensus is an intersection over four
     models that can disagree on rejection order (F65). This is the same gap that refuted wave 14's V4.
  2. **S16-D at `V0.07`** — can *any* threshold in this family be selective on `caboose`? No MAD floor could be.
  3. **Family R entirely.** A non-monotone re-ranking has no counterfactual on disk. **R-REDIRECTS** — R
     rejecting a position rung `N` never rejected at any budget — is the wave's only genuinely unpredictable
     measurement, and it is the one that decides whether re-ranking is worth a future wave.
  4. **RULE P16's verdict.** §5.2 argues P-b is likely; the field-by-field diff has never been made.
- **Therefore, if outcome 2 is reached, the register entry says so in these words**: *predicted in advance from
  wave-14 data, confirmed at the consensus level for the first time, and shipping nothing.*

---

## §10 — Corrections to the brief, stated plainly

The pre-registration was asked to say where the framing is wrong. Five places:

1. **`a2693968` is not a `BuildId`.** The brief requires the wave's `BuildId` to differ from
   *"`62334f10`, `084e3485`, `a2693968`"*. The first two are waves 13 and 14. **`a2693968…` is wave 15's
   `TestApp.dll` sha256**; wave 15's `BuildId` is **`df3a867d6fc74204a0dbfc75060b9a96`**. A novelty list that
   mixes a file hash with a build id can never fire on the entry it thinks it is guarding — F66's shape exactly.
   `prov_w16.py` carries the correct list, keeps the sha256 in a separate clearly-labelled table, and **fails
   loudly** if a `BuildId` field ever starts with a known dll hash.
2. **The arm rates do not transfer to `af-fit`.** ~2.6 / ~5.2 m per run are `optimize --per-run --max-evals 250`
   rates. Applied to a 39-run `af-fit` rung they over-price it by **18×** (203 m vs a measured 11 m). §7 prices
   from wave 14's measurement of the same instrument on the same population.
3. **There is no `MeasurePoint` type in this repo**, and **`N*` is not available in the production engine without
   new plumbing.** It is available where σ is *computed* (`HocusFocusStarDetection.cs:804`) and gone by the time
   the fit's points are *built* (`AutoFocusEngine.cs:1071`), because the only carrier between them is
   `MeasureAndError` — an external NINA `struct` with two `double`s. §2.3 prices the gap. **The harness change is
   ~2 h as the brief says; the product change is not, and the wave's best outcome is therefore a recommendation.**
4. **Printing `af-fit`'s params alone cannot close F67.** One side's params is not a diff, and `optimize` prints
   five of ~45 fields. Item B prints **both** sides through one formatter (§5.1). The brief's ~45 m is also too
   high given item A pays for the build and the gate: the marginal arm cost is **~1 minute**.
5. **"Rank on a SEM-scaled residual" and "the measurement wave 14 made" are not the same change.** Wave 14
   measured `s` **at the point the CURRENT rule selects**. A re-ranking changes which point is selected, so that
   measurement no longer describes the re-ranked rule's behaviour — and the prefix/subset lemma V4′ tests does
   not hold for it. The change the measurement actually licenses is the **veto**. Wave 16 therefore runs both,
   under one spec type, with **family R excluded from V4′ and from the RECOMMEND branch by pre-registration**,
   and with R's own three-valued outcome (branch 5) as its consumer. *Refusing to gate R is not softening the
   rule; gating a mechanism on a lemma proved for a different one is what wave 14 did.*

---

## The traps, baked into the drivers rather than remembered

`< /dev/null` on every `TestApp` call in a read loop, and the **population size asserted** · the real-bank list is
**TAB**-separated (one path contains a space) · scorers take **WSL** paths and abort on a backslash ·
**"could not look" is its own state and its guard comes BEFORE any field read** · `DetectorVersion` is read as a
**FIELD**, never from `strings` (two heaps, neither discriminates) · Newtonsoft writes `"NaN"`/`"Infinity"` as
**strings** · `Panos_attempt01` is UNEVALUATED **by name** on any ρ clause · `D17_cdk14_oiii5` finds zero stars at
short exposures and af-fit drops positions with ≤ 1 star — a **named** state, not a short population ·
never run NINA during a pinned arm · one `TestApp.exe` at a time · no fan-out · **never rebuild an arm's directory
mid-wave** · the 42 bank landings are fingerprinted **before** and verified **after** ·
**every printout a scorer reads is ASCII-only** (§2.2) · and, the one wave 15 paid for:
**do not build a control on an equality between two pipelines without testing that equality across the population
first** — W3 is deliberately an **intra-process** identity and P16 is deliberately a **diff**, not an equality
control.
