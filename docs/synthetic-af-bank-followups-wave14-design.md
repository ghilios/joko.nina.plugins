# Synthetic AF bank — followups wave 14 (design)

Plan: [`plans/synthetic-af-bank-followups-wave14-plan.md`](../plans/synthetic-af-bank-followups-wave14-plan.md).
Wave 13: [`docs/synthetic-af-bank-followups-wave13-results.md`](synthetic-af-bank-followups-wave13-results.md).
Standing authorisation: [`docs/waves14-21-autonomous-prompt.md`](waves14-21-autonomous-prompt.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE — fixed before any arm runs
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, at wave 13's HEAD **plus** item 1's default change **plus** item 2's `madFloor` parameter. Commit recorded by the controller at build time |
> | binary | `D:\hf_w14\exe` — **ONE binary for the whole wave**, never rebuilt (F53(c)). `TestApp.dll` / `NINA.Joko.Plugins.HocusFocus.dll` sha256 and `BuildId` recorded at build time |
> | detector | `strings … \| grep AtrousWaveletFast` must **hit** ⇒ `DetectorVersion` 2 |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json`, md5 **`a67ffc06…`** — sets `MaxOutlierRejections` **explicitly** to 0 |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm |
> | profile set | `D:\hf_w14\profiles_before_w14.txt`, snapshotted **before** any arm |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, each with `renderRequest.OptimalFocuserPosition` = **truth**), `D:\Autofocus Bank` (19 runs) |
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3).
> Wave 12 measured the authorisation at **1.33×**, not 4× ([F60](followups.md)) — there is nothing to trade.
>
> **One binary, and the reason it carries BOTH code changes.** F53(c) forbids rebuilding an arm's directory
> mid-wave, and two binaries would make RULE G14 and item 2's control arms incomparable. Item 1's change cannot
> reach `af-fit`'s budget table (that table iterates budgets 0…3 explicitly and never consults
> `AutoFocusOptions.MaxOutlierRejections`), and item 2's `madFloor` defaults to `0.0`. Both claims are
> **measured, not asserted** — see RULE G14 and RULE F14/V1.

---

## DISCLOSURE — what already existed when these rules were fixed

This series' rule is *fix the rule before the data*. **Item 2's Stage A does not satisfy it, and pretending
otherwise would be worse than saying so.**

| stage | did its data exist when the rule was fixed? |
|---|---|
| RULE G14 (the gate) | **no** — the binary did not exist |
| item 2 **Stage B** (measured, the ladder) | **no** — no wave-14 `af-fit` run existed |
| item 2 **Stage A** (counterfactual + SEM audit) | **YES** — it reads wave 13's 39 `af_fit_summary.txt` files, which have been on disk since 2026-08-09 |

Stage A is therefore a **pre-specified analysis of existing data**, not a pre-registration, and it is labelled
that way everywhere below. To keep the distinction honest, this is the complete list of what was computed from
those artifacts *before* the ladder and RULE F14 were fixed:

1. the **structure**: 39 files, 4 budget rows each, **72 Grubbs rounds** total;
2. the **effective-scale distribution** over those 72 rounds — `< 1.0` on **71**, `< 0.5` on 66, `< 0.25` on
   **51**; median **0.1315**, min 0.0 (8 rounds print `0.0000`), max **1.8069** (`D03`, which never fires);
3. **which runs fire**, at which budget — 7 at budget 1 (4 synthetic, 3 real), **13 of 39 at budget 3**;
4. the **already-published** per-budget `minPos`/σ_focus/R² tables (wave 13 §2.2, §2.4, §2.5 and
   `D:\hf_w13\affit_syn_score.txt`), from which the *reachable* range of every counterfactual is derived;
5. **five spot arithmetic checks** used to confirm the clauses are not vacuous, named here so nothing is
   smuggled: `caboose` round 1 (scale 1.48e-5, z 1609.6), `caboose` rounds 2–3, `D08` round 3 (scale ≈ 2.23e-4,
   z 503.6), `D16` round 1 (scale 0.3141, z 4.763), `toml999` round 1 (scale 0.0365, z 12.049);
6. the **star counts** in `af_fit_points.csv`, joined to those rounds (this is what produced the SEM audit);
7. the **decision-margin distribution** `max z / Grubbs limit` over the **64 rounds with a usable printed
   scale** — min 0.372, p25 0.643, **median 1.017**, p75 1.933, max 757.2; **20 of 64 fall within 25 % of the
   limit**, and the thinnest is `D01_ultrawide_40mm` round 1 at **1.0005** (§2.6.1).

**What was deliberately NOT computed:** the floored decision for all 72 rounds at any rung, and any per-rung
outcome table. Stage A's *result* did not exist when RULE F14 was written.

**The scorers were executed once during authoring, and that is disclosed too.** `stageA_w14.py` and
`score_affit_w14.py` were smoke-run — a scorer that has never been run is a liability, not a deliverable — and
their outputs were written to `/tmp` and deleted. What that run displayed: the reproduction gate (**72 of 72,
0 INCONSISTENT**), aggregate round-state counts, and `score_affit_w14.py`'s **`f = 0` baseline row**, which is
wave 13's own published data (`max ρ` 45.1768, `N_fire` 7/13/13, C3 DOES NOT CONTAIN, C4 zero). **`predict.tsv`
and every per-rung outcome were not read.**

> **The smoke run earned its place by finding two defects in the scorer, both of the exact class this register
> exists to catch.** `RX_BUDGET` was compiled without `re.M`, so `^`/`$` never matched and **all 39 files
> parsed as unreadable** — and the validity gate then printed **`PASS — 0 of 0 rounds reproduced`**. *A check
> that could not run must never look like a check that ran and found nothing* (wave 13 §1.1). Both are fixed:
> the population assertion now fails on any unreadable file, and the round count is itself pre-registered at
> **72** so "PASS, 0 of 0" is unreachable.

**The ladder's ordering was decided by a source read, and the source read reversed the controller's first
framing** (§2.4). That read is `AlglibHyperbolicFitting.cs`'s own `ChiSquared`/`ReducedChiSquared` docstrings,
which are older than this wave.

---

## The items

| # | item | kind | decided by |
|---|---|---|---|
| **G14** | the gate reproduces the eight `BestJ` **bit-identically** after item 1 | **stopping gate** | RULE G14 |
| **1** | `MaxOutlierRejections` code default 1 → 0 | **ships** — owner's product-coherence decision | *no rule; see §2* |
| **2** | **F45(b)** — a σ-tied floor on `RejectionTest`'s MAD scale | **measured, returns a costed RECOMMENDATION** | RULE F14 |
| **3** | the **F62 audit** — every register conclusion resting on a within-fit statistic | already running in a separate agent | criteria fixed at spawn |
| **4** | the nine UI changes A1–A9 | **not attemptable** — one line, §5 | — |
| **5** | **F57(d)** — the unused `exe_v1wav` bisect | **RUNS.** Disposition fixed before looking, then **AMENDED before any arm ran** — the "not constructible" reason rested on a probe that returns the same answer for a binary that *does* have the flag | RULE W14-D |

> **THIS DOCUMENT WAS AMENDED ONCE, BY THE CONTROLLER, BEFORE ANY WAVE-14 MEASUREMENT EXISTED.** Item 5's
> disposition flipped from CLOSE-AS-NOT-CONSTRUCTIBLE to RUN, and RULE W14-D was added with its three outcomes
> fixed. The amendment is written in place at item 5 with the retracted text struck rather than deleted, and the
> budget table carries the added arm. **An amendment that makes a wave do more work, recorded with the refuted
> reason left visible, is not a moved goalpost — but it is only distinguishable from one because it is dated and
> the old text is still there.**

---

## RULE G14 — the gate, and it is this wave's STOPPING gate

Wave 11's eight values have reproduced bit-identically across **four** binaries and two settings files. This
wave adds a fifth binary — the first one whose **product default differs**.

| run | expected `BestJ` (6 dp) | run | expected `BestJ` (6 dp) |
|---|---|---|---|
| `toml999` | 0.995784 | `mccomiskey` | 0.976746 |
| `CWhiteFocus` | 0.996068 | `D18_m24_deep_shed` | 0.999882 |
| `uneven` | 0.996368 | `D19_cygnus_deep_shed` | 0.999487 |
| `muggsie` | 0.997195 | `D20_m24_bright_control` | 0.999738 |

> ### RULE G14 — ONE PASS/FAIL CLAUSE
>
> **All eight `BestJ` reproduce the table to 6 dp. A partial reproduction is a FAILURE, not a warning** — the
> eight are one instrument. Bit-identity to all sixteen digits is *reported* as the stronger observation; 6 dp
> is the clause.
>
> **Why a miss is a FINDING and not a nuisance.** `D:\hf_w11\pinned_settings_w11.json` sets
> `MaxOutlierRejections` **explicitly**, so on a pinned arm the code default is never consulted.
> **If the gate moves, item 1's change reached somewhere it should not have** — and that stops the wave before
> item 2 runs, because item 2 is measured in this coordinate system.

Sequential, `--per-run --max-evals 250`, **both pins named in the script header**, machine quiet. Scored by
`python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w14/gate --rule G14` — **a WSL path, never a Windows one**
(wave 13 §1.1: a Windows path yields eight `UNEVALUATED` and looks exactly like a failed arm).

`score_w12.py` accepts `--rule` as a free label and does **not** hardcode rule names, so `--rule G14` works
unmodified; it is reused **as is** and is not edited, because editing a scorer that produced earlier waves'
numbers would retroactively change how those numbers were produced. Its one gap for this wave is that its
`BuildId`-novelty check compares against **wave 11's** id only, so `D:\hf_w14\prov_w14.py` carries the wave 12,
13 and 11 ids and checks the rest of the provenance across the arm.

### The free controls, as FIELDS, diffed rather than asserted

| field | expected |
|---|---|
| `BuildId` | **must DIFFER from wave 13's `62334f10…`** (and from wave 12's `103d61c4…`, wave 11's `5cb7e474…`) — a match means no build happened |
| `DetectorVersion` | 2 ×8 |
| `ProfileId` | `astrodet` ×8 — the pin took |
| `FitInputs` | `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` ×8 |
| `ConcurrencyCheck` | `exclusive` **read across the whole arm** — one landing's `exclusive` proves nothing (`WaitOne(0)` is won by exactly one of N contenders) |
| `BaselineJ` | reproduces wave 11 — [F41](followups.md)'s free control |

Wave 5's φ table is invalid on three axes and is not quoted anywhere in this wave.

> ### RULE G14 IS A **WEAK** CONTROL FOR ITEM 2, AND SAYING SO IS PART OF THE DESIGN
>
> The gate arm runs at `MaxOutlierRejections` = **0**, where `MathUtility.RejectionTest` is **never called**.
> A change to `RejectionTest`'s signature therefore *cannot* move the gate, and a gate PASS is not evidence
> that item 2's parameter is inert. **The real control for item 2 is RULE F14/V1** — the `f = 0` rung of the
> ladder, which runs `af-fit` over all 39 runs at budgets 0…3 (where the rejection *does* execute) and must
> reproduce wave 13's tables exactly. That rung costs ~11 minutes and it is the only thing in this wave that
> can catch a `madFloor` that is not inert at its default.

---

## Item 1 — `MaxOutlierRejections` code default 1 → 0

**Change:** `AutoFocusOptions.cs:62` `GetValueInt32(nameof(MaxOutlierRejections), 1)` → `0`, and
`AutoFocusOptions.cs:81` (`ResetDefaults()`) `MaxOutlierRejections = 1` → `0`. **Profiles that explicitly store
1 keep 1.** This changes the fallback, nothing else.

### This OVERRIDES a pre-registration, and the override is the honest description

> **Wave 13's RULE M13 pre-registered that the shipped default changes only if D1 dominates. D1 did not fire —
> 16 of 20 datasets were ties, the best either value managed was 3 wins, and the median displacement was
> 0.00128 step against a 0.10-step floor — and the pre-registered outcome was NO CHANGE.**
>
> **It ships anyway, as a product-coherence decision by the owner. The rule was not met; it was overridden.**

The evidence below is real and is cited as **support for a decision**, never as a rule having been passed:

- `MOR` = 1 won on **0 of 4** synthetic datasets where the rejection fires, scored against the generator's own
  truth (wave 13 D1);
- the sensor fit prefers 0 on **10 of 10** like-for-like `sChi` runs ([F61](followups.md), wave 13 D3) —
  **WEAKENED by the wave-14 F62 audit**, which verified in code that the paraboloid weights each star by `1/σ²`
  from that star's own `MinimumStdError`, so a fired rejection *raises* the weight and *raises* `sChi`
  arithmetically. The **unweighted** `sRMS` half (9 of 10) is the part that carries the claim;
- the harness has measured waves 5–12 at 0, so the product now matches **eight waves** of measurement;
- at budget 3 the Grubbs cascade takes `caboose` from R² **0.99987 → 0.91475** and reduced χ²
  **0.000317 → 3.946** ([F45](followups.md), wave 13) — so the direction of risk is **entirely on the high
  side**. *The headline σ_focus ratios (12.7× via `bank-verify`, 45.2× via `af-fit`) are **WEAKENED on
  magnitude** by the wave-14 F62 audit — σ_focus inflates mechanically as `n − p` shrinks — so R² and reduced
  χ², which carry no `n − p` term, are quoted here instead. The direction survives outright: F62's bias
  predicts improvement, and a bias cannot manufacture its own opposite.*

*(Those two `caboose` ratios are two pipelines, not two measurements of one number. Wave 13's own lesson —
"the fire rate is not one number" — applies to the blow-up ratio too. Never quote one for the other.)*

### What it does to users, said in the same breath

[F63](followups.md) measured the optimizer's **landing** moving on **6 of 8** runs between these two values, at
`--max-evals 250`, including the recommended AF step size by up to **17 %** (`CWhiteFocus` 101 → 118) and the
brightness sensitivity by a **factor of two** on two runs.

> **Every profile with the key absent silently moves what the wizard recommends.** That is the user-visible
> consequence, it belongs in the PR body, and it is not softened by the fact that `BestJ` is not comparable
> across the two values.

### Also carried by the change, because they pin the old value on purpose

`HarnessFitInputsTests`' code-default assertions, and `DEFAULTS` in `D:\hf_w13\snapshot_profiles_w13.py` (whose
`*` marker means "key absent ⇒ this code default"). Updating them is part of the change, not a workaround.

---

## Item 2 — F45(b): a σ-tied floor on `RejectionTest`'s MAD scale

### §2.1 What the code does, verified

`MathUtility.RejectionTest` (`Utility/MathUtility.cs:149`) takes `weights` (1/σ at each x), forms
`errors[i] = weights(x_i) · (Y_i − f(x_i))` — the **standardized** residual `r` — then:

```
(median, mad) = errors.MedianMAD()          // MedianMAD returns the 1.4826-SCALED MAD
scale = mad
if (scale <= 0 || IsNaN(scale)) scale = stdDev(errors)     // degenerate-data fallback
if (scale <= 0 || IsNaN(scale)) return null
z_i = |errors[i] − median| / scale ;   reject argmax z if z >= grubbZLimit
```

**Because the MAD is taken of `r`, the σ scaling cancels in relative terms.** Every point's residual is divided
by its own σ *and then* by the MAD of those quotients, so what survives is a pure ranking against the other
points' agreement with the curve. **A point can sit at 2 % of its own error bar and be rejected.**

### §2.2 The docstring already promises what the code does not deliver

`RejectionTest`'s own summary, in the source, today:

> *"When `weights` is supplied (typically 1/σ at each x), the residuals are weighted to match the weighting the
> fit actually minimized, **so a point that is far from the curve but has a large measurement uncertainty (low
> weight) is not treated as an outlier.**"*

The MAD re-standardization cancels exactly that. **The floor is not a new idea being evaluated; it is the
code's own documented guarantee, and F45(b) is asking for the guarantee to be made true.** That reframes the
cost side: shipping it is a behaviour change, but *not* shipping it leaves a docstring that is wrong.

### §2.3 The instrument is already printed — and the register's own reproducer

`af-fit`'s `af_fit_summary.txt` prints, **per point per round**, for the winning model: `sigma`, `|Y−f|abs`,
`stdResid r`, `z = |r − med|/MAD`, plus the `Grubbs limit`, `median(r)` and `MAD(r)`; and a per-budget
production table (winner model, #rej, rejected positions, σ_focus, redχ², R², `minPos`) at budgets 0…3 **on
one detection pass**. Wave 13 wrote 20 synthetic + 19 real of these to `D:\hf_w13\affit_syn\*` and
`D:\hf_w13\affit_real\*`, in **10 m 46 s** for both banks.

**Three structural facts about that file were verified before the rules were fixed, and two of them are not
what a casual read suggests:**

1. **The printed `MAD(r)` is the EFFECTIVE SCALE, post-fallback** (`AfFitDiagnosticRunner.cs` prints
   `MAD(r)={scale:F4}` *after* the stdDev substitution), and it is printed to **4 dp** — so on 8 of 72 rounds
   it prints `0.0000` and carries no usable precision. **Any scorer must recover the scale as
   `|r_i − median| / z_i` from the printed rows, never from the printed `MAD(r)`.** (`caboose` round 1: printed
   `0.0000`, actual **1.48e-5**.)
2. **The per-round detail traces ONE model — the budget-0 winner** (`SelectBestModel(…, 0, …)`), while the
   production table at budget *B* is a **consensus** across all four models: each candidate proposes its own
   Grubbs outliers and only the **intersection** is removed (`AlglibHyperbolicFitting.SelectBestModel`'s own
   docstring). That is why `D08` prints three rejecting rounds while its production `#rej` is 1 at every budget.
   **The printed trace is not the production trajectory**, and Stage A's verdict must be three-valued because
   of it (§2.6).
3. **The winner model changes across budgets on 3 of 39 runs** — `LinwoodFocus`, `caboose`, `toml999`, two of
   which are among the three real-bank budget-1 firings. `caboose` flips `TiltedHyperbola` → `Symmetric` at
   budget 3 and that flip *is* its catastrophe.

### §2.4 What σ actually is — the source read that reversed the ladder's ordering

`AlglibHyperbolicFitting.cs` documents its own χ² twice:

> `ChiSquared`: *"per-point σ is the star-ensemble scatter (1.483·MAD), which **overstates the uncertainty of
> the plotted median HFR by roughly √(detected stars)** — so values ≪ 1 are normal for star-rich fields."*
>
> `ReducedChiSquared`: *"…runs **≪ 1 for star-rich fields (≈ 1/N\* scaling)** and grows ×FramesPerPoint now that
> multi-frame σ is SEM-pooled."*

**So `redχ²` ≈ 0.098 median over the bank, and 0.000317 on `caboose`, is the DESIGNED behaviour of a σ that is
deliberately the ensemble scatter rather than the standard error of the plotted median. It is not
miscalibration, and nothing in this wave calls it that.**

Two consequences, and they point in opposite directions:

- **`r` is smaller than a unit-normal residual by ≈√N\* by construction.** So "`MAD(r)` < 1 on 71 of 72 rounds"
  is partly a statement about what σ *is*, and an **absolute** floor in `r` units has an aggressiveness set by
  the star count — the wrong dependency.
- **But the correction is computable for free**, because `af_fit_points.csv` prints `Stars` per focuser
  position and the round tables print `r` per focuser position. **`s = |r| · √N\*` is the rejected point's
  residual in units of the standard error of the plotted median** — the quantity F45(b) actually means by
  *"inside its own error bar"*. Nothing needs to be built to compute it. It is **Stage A(2)** (§2.7).

> **This is also a finding about F45(b)'s own wording**, and it is worth more than either ladder's numbers:
> the register's next-step text says a point should not be an outlier while *"sitting well inside its own error
> bar"* — and the source says the error bar it would be compared against is ≈√N\* too wide for the quantity
> being fitted. **The sub-question as written is under-specified, and the wave can say in which units it should
> have been written.**

### §2.5 The ladder — two families, and a third refuted on paper before it cost anything

Let `scale_k` be the effective scale at Grubbs round *k* of a given model, as defined in §2.1.

| family | rule | rungs | status |
|---|---|---|---|
| **A — ABSOLUTE** | `scale_k ← max(scale_k, f)` | **f ∈ {0.00, 0.25, 0.50, 1.00}** | **PRIMARY.** F45(b) as literally written; `f = 0.00` is the control rung |
| **B — NON-COLLAPSING** | `scale_k ← max(scale_k, α · scale_1)` | **α ∈ {0.50, 1.00}** | SECONDARY. Targets the *cascade*; leaves round 1 bit-identical by construction; dimensionless in σ, so the √N\* factor cancels |
| ~~C — DISPERSION~~ | ~~`scale ← max(mad, β·stdev(r))`~~ | ~~β ∈ {0.25, 0.50}~~ | **DROPPED before measurement, §2.5.1** |

> ### A RECORDED DISAGREEMENT WITH THE CONTROLLER, LEFT OPEN RATHER THAN SILENTLY RESOLVED
>
> The controller designated **family B as the lead candidate** and family A as *"wrong in principle, kept as a
> measured refutation"*. **The arithmetic in §2.5.2 and §2.5.3 contradicts that for the wave's own named
> target**: family B provably cannot contain `caboose` at either rung, and family A contains it at every rung
> ≥ 0.25. The table above therefore lists A as primary **for the C3 clause**.
>
> **The controller's objection to A is not withdrawn and is not weakened** — `f`'s aggressiveness really is
> star-count dependent by ≈√N\*, and the ladder cannot select `f` (§2.5.3). The two positions are compatible:
> **A is the family that reaches the mechanism; B is the family whose units are defensible.** That is why both
> are measured, why the verdict breaks ties **toward B** (§2.9), and why the SEM-unit floor — which has A's
> reach *and* B's units — is named as the likely recommendation shape (§2.11).
>
> *Recorded here, before the data, so that whichever family wins is not read as the pre-registration having
> quietly agreed with the outcome. If the controller prefers B designated lead, that is a one-line change to
> this table and it changes no clause, no threshold and no rung.*

**The floor is applied AFTER the existing degenerate-scale guards**, never instead of them. That is a
pre-registered semantic choice and it is load-bearing:

- flooring the **MAD** *before* the `stdDev` fallback would make the fallback dead code for `f > 0`, and worse,
  it could make the scale **smaller** than it is today (MAD 0 → floor `f`, where the fallback would have given
  a larger stdDev) — which would let a floor **create** a rejection.
- flooring the **effective scale** is monotone: `max(scale, f) ≥ scale` ⇒ every `z` can only shrink ⇒ **a floor
  can only ever suppress a rejection, never add or redirect one.** Stage A's entire framework rests on that
  monotonicity, so the variant that breaks it is excluded by construction rather than by hope.

#### §2.5.1 Family C was proposed, priced at two lines, and refuted on paper

The natural diagnosis of F45's mechanism is *MAD breakdown*: the MAD has a 50 % breakdown point, so when more
than half the residuals are near-identical the MAD collapses while the RMS does not. `caboose` round 1 looks
exactly like that — 5 of 7 residuals print as `0.0000`. A floor at `β · stdev(r)` costs two lines (the stdDev
is already computed in the existing fallback branch).

**It does not work on the case it was proposed for.** `caboose` round 1's printed `r` are
`[0.0012, −0.0238, ~0, ~0, ~0, 0.0082, ~0]`, giving `stdev(r) ≈ 0.0100`. The rejected point's
`|r − med| = 0.0238`, and the Grubbs limit at N = 7 is 2.020:

| β | floored scale | z | verdict |
|---|---|---|---|
| 0.25 | 0.0025 | **9.5** | still rejects |
| 0.50 | 0.0050 | **4.8** | still rejects |
| 1.00 | 0.0100 | **2.4** | still rejects (barely) |

**`caboose`'s scale is small because its residuals are genuinely tiny relative to σ, not because the MAD broke
down.** Family C is dropped, at a cost of zero minutes. *A family refuted on paper against the wave's named
target is worth more than the same family measured for 22 minutes.*

#### §2.5.2 Family B cannot reach `caboose`, and that is pre-registered

`caboose`'s scale is **already collapsed at round 1** — 1.48e-5 against σ values of 0.12–0.79. The collapse is
not progressive there, so a floor anchored to round 1 is anchored to the collapse:

| round | scale | `α·scale_1` at α = 1.0 | max z at that floor |
|---|---|---|---|
| 1 | 1.48e-5 | 1.48e-5 | **1609.6** — unchanged, by construction |
| 2 | 4.17e-6 | 1.48e-5 | **≈ 568** — still rejects |
| 3 | ~1e-9 | 1.48e-5 | ≈ 0 — suppressed |

Production's budget-3 consensus `{7275, 6975}` is formed in **rounds 1 and 2**. **Family B is pre-registered as
UNABLE to contain `caboose` at either rung.** It is measured anyway because (a) it *does* reach the progressive
collapses — `D01` 0.9885 → 0.0570 → 0.0563 and `D08` 0.0572 → 0.0477 → 0.0002 — and (b) a prediction that is
recorded and then confirmed is how a family stops being carried.

#### §2.5.3 Family A is the only family on this ladder that reaches `caboose`

`caboose` round 1: `|r − med| = 0.0238` at scale 1.48e-5. At `f = 0.25`, `z = 0.095` against a limit of 2.020 —
**suppressed, by a factor of 21.** The rejected point sits at **2.4 % of its own error bar** from the curve.

> **Family A is therefore the primary family, and the controller's principled objection to it is preserved
> rather than dismissed:** the *right value* of `f` is star-count dependent by ≈√N\*, and the ladder's 0.25–1.00
> span (4×) does not span the √N\* range across this bank (`caboose` N\* = 9–65 ⇒ √N\* = 3–8; `toml999`
> N\* = 217–1905 ⇒ √N\* = 15–44 — a **10×** span). **The ladder measures behaviour at fixed `f`; it cannot
> select `f`.** Selecting it needs the SEM-unit floor, which needs the star count inside `RejectionTest` —
> named, priced, and deliberately not attempted this wave (§2.10).

### §2.6 Stage A(1) — the counterfactual, free, and its limits stated first

A Python scorer over all 39 wave-13 `af_fit_summary.txt` files. For each round it recovers the effective scale
`s = median over rows with z ≥ 1.0 of |r_i − med| / z_i`, then recomputes `z^f_i = z_i · s / max(s, f)` (family
A) or `z_i · s_k / max(s_k, α·s_1)` (family B).

**The prefix lemma, which is the only reason a counterfactual is possible at all.** A floor can only shrink
`z`, and it shrinks every `z` in a round by the *same* factor, so (i) the argmax is unchanged, and (ii) if a
round still rejects it rejects the *same point*, leaving the next round identical to the actual one.
**Therefore every model's floored rejection set is a PREFIX of its unfloored set**, and since production
removes the **intersection** across models, `consensus^f_B ⊆ consensus_B`.

**Per-run verdict, three-valued, and the third value is what the printed file forces:**

| verdict | when | what production does |
|---|---|---|
| **SUPPRESSED** | the printed model's round 1 is suppressed at `f` | the printed model is one of the four consensus candidates, so the intersection is **empty** ⇒ production's row **is the printed budget-0 row**. **EXACT** |
| **INDETERMINATE** | the printed model still rejects, but the other three models' scales are **not printed** | the outcome is one of the printed rows; on every run whose consensus is a single point that is exactly {budget-0 row, budget-1 row} |
| **N/A** | production rejected nothing at that budget | a floor cannot add a rejection ⇒ nothing can change. Tie by construction |

**INDETERMINATE is not a failure of the scorer; it is the file's limit, and it is why Stage B exists.**
*"Could not look" needs its own state at every level, and the guard comes before any field read.*

**The exact-arithmetic shortcut.** Where the floor suppresses round 1 entirely, the counterfactual fit **is**
the already-printed budget-0 row — whose out-of-sample error against truth is already scored in
`D:\hf_w13\affit_syn_score.txt`. No refit is needed and none is done.

#### §2.6.1 The counterfactual's FORM is exact; its INPUTS are rounded — and one real decision sits inside the rounding

These are two different claims and conflating them would make Stage A look stronger than it is.

- **The form is exact.** A uniform scale floor multiplies every `z` in a round by the same constant, so the
  argmax never moves and the floor can only suppress, never redirect. That is arithmetic and it does not
  depend on precision.
- **The inputs are rounded.** `af_fit_summary.txt` prints `stdResid r` and `MAD(r)` to **4 dp** and `z` to
  **3 dp**. Recomputing `z` from `(r, median(r), MAD(r))` agrees to ~4 significant figures, not bit-identically
  — e.g. `D16` round 1 recomputes to **4.7638** against a printed **4.7630**.

That is harmless where the margin is wide, and 20 of 64 usable rounds are not wide:

| statistic over the 64 usable-scale rounds | `max z / limit` |
|---|---|
| min / p25 / **median** / p75 / max | 0.372 / 0.643 / **1.017** / 1.933 / 757.2 |
| **within 25 % of the limit** | **20 of 64** |
| thinnest | **`D01_ultrawide_40mm` round 1 — max z 2.1280, limit 2.1270, ratio 1.0005** |

> **`D01`'s rejection is decided in the fourth decimal place, and `D01` is one of the four synthetic datasets
> that FIRE** — one of the four points the out-of-sample clause rests on. Its printed margin (0.001) is the
> size of the printed precision itself.

> ### VALIDITY GATE — over DECISIONS, with a third state, and the third state is not a failure
>
> **The gate is phrased over decisions, never over `z` to full precision.** Bit-identity is unattainable from a
> rounded artifact, so demanding it would make the gate unsatisfiable — the exact defect §2.10 exists to
> prevent.
>
> Each round is classified into **exactly one** of four states, and the classification happens **before any
> verdict field is read**:
>
> | state | criterion |
> |---|---|
> | **INERT** | `f ≤ s_lo` (or `α·s₁_lo ≤ s_lo`) ⇒ `max(s, f) = s` exactly ⇒ the floored verdict **is** the printed verdict, at any margin, with no recomputation and no rounding exposure |
> | **RESOLVED** | the floored `max z` interval lies **wholly** on one side of the Grubbs limit |
> | **UNRESOLVABLE_FROM_ARTIFACT** | the interval **straddles** the limit, or `f` lies inside `[s_lo, s_hi]` so it cannot be told whether the floor bites at all |
> | **INCONSISTENT** | the per-row scale intervals do not intersect — the parse or the model of the file is wrong, and this is a scorer bug, not a datum |
>
> Intervals are propagated, not assumed. The effective scale is recovered per row as `s_i = |r_i − med| / z_i`
> over rows with `z_i ≥ 1.0`, each carrying `s_i ∈ [(|r_i − med| − 1e-4)/(z_i + 5e-4),
> (|r_i − med| + 1e-4)/(z_i − 5e-4)]` (half-ulp 5e-5 on `r` and on `median(r)`, 5e-4 on `z`);
> `s_lo = max_i lower_i`, `s_hi = min_i upper_i`. **The printed `MAD(r)` is never used as the scale** (§2.3).
>
> **The reproduction gate:** at `f = 0` every round is INERT by construction, so the scorer must reproduce
> **the actual rejected position on every firing round and the actual "NO outlier" verdict on every silent
> round — 72 of 72.** Below 72, **Stage A is UNEVALUATED and reports so.** *A counterfactual that cannot
> reproduce the present is not a counterfactual.*
>
> **`max z / limit` is REPORTED for every round at every rung**, not just the verdict. An UNRESOLVABLE round is
> **neither a reproduction pass nor a reproduction failure**; it is counted, named, and carried into every
> downstream clause as an interval rather than a value. *"Could not look" needs its own state at every level,
> and wave 13 §1.1 is the precedent — a scorer that reported eight `UNEVALUATED` instead of eight zeros caught
> the person who wrote it.*

> ### WHY STAGE B IS NOT A FORMALITY
>
> **Stage A is provisional wherever the margin is thin; Stage B recomputes from the live doubles and is exact.**
> Pre-registered expectation: **Stage A and Stage B agree on every round except possibly the flagged
> (UNRESOLVABLE) ones.**
>
> **A disagreement on an UNFLAGGED round means one of the two is wrong, and the wave investigates rather than
> picking a winner.** The three candidate causes, in the order they are checked: (i) the prefix lemma is false
> (V4 catches this independently); (ii) Stage A's model of the file is wrong — most likely the consensus
> mechanism (§2.3, fact 2), which Stage A can only bound; (iii) the parse. **Stage A is retracted, not
> reconciled, if (i) or (iii).**

### §2.7 Stage A(2) — the SEM audit, and it needs no code at all

Join each round's printed `r` to `af_fit_points.csv`'s `Stars` on the focuser position, and compute
**`s = |r| · √N\*`** — the residual in units of the standard error of the plotted median, i.e. F45(b)'s
*"its own error bar"* in the units the source says it should be read in.

Two of these were computed while checking satisfiability and are recorded in the disclosure:

| run | rejected point | `r` | N\* | `s = |r|·√N\*` |
|---|---|---|---|---|
| `caboose` | 7275 | −0.0238 | 18 | **0.10 SEM** |
| `D16_esprit550_ha3` | 8000 (**the true focus**) | +1.3307 | 43 | **8.7 SEM** |

> **If that separation holds across the population, F45(b) is right and the register's own wording is wrong
> about the units:** the pathological rejections are at a *tenth* of a standard error and the substantive ones
> are at eight or more, and no floor in raw `r` units can tell them apart while a floor in SEM units can.
> **It also does not settle F45's complaint**, because `D16`'s 8.7-SEM point is the generator's **true focus**
> and discarding it moved the vertex *away* from truth (wave 13 §2.2). A point can be a genuine large deviation
> and still be the one you must keep.

### §2.8 Stage B — measured, six rungs, and the control rung is the point

An **off-by-default optional parameter** on `RejectionTest` — `double madFloor = 0.0`, and a second
`double roundOneFloorFactor = 0.0` for family B — threaded through
`AlglibHyperbolicFitting.SelectBestModel` (~line 305) → its rejection call (~line 506) → `RejectionTest`
(~line 149), and exposed **only** on `af-fit` as `--mad-floor <f>` / `--round1-floor <α>`.

- **No `*Options` class changes, no persisted option, and therefore no
  `Resources/OptionsDataTemplates.xaml` control is required.** (If a future wave ships this as a user option,
  that project invariant applies then.)
- **The product is bit-identically unchanged at the default**, and that is *measured* by RULE F14/V1, not
  asserted — see the RULE G14 weakness note above.
- The summary must print the floor family and value it ran with, and the **effective scale actually used** per
  round at full precision, so the next wave does not have to recover it from a 4-dp field again.

Then `af-fit` over all 39 runs at each rung. **Price: wave 13 measured 3 m 46 s for 20 synthetic and 7 m for
19 real ⇒ ~11 m per rung over both banks.**

### §2.9 RULE F14 — fixed before Stage B's data exists

Notation: `e_b(d) = |minPos_b(d) − OptimalFocuserPosition(d)| / step(d)` on the 20 synthetic datasets, in step
units — **the same metric wave 13's D1 used, so the bar is the predecessors' bar.**
`N_fire(B, f)` = number of the 39 runs whose production rejection set at budget `B` is non-empty at rung `f`.
`ρ(run, f) = σ_focus at budget 3 / σ_focus at budget 0`; **the denominator is the un-pruned fit, which the
floor cannot reach** — *score the change on something the change cannot move.*

#### Validity gates — any failure ⇒ NO VERDICT, and the wave says which gate failed

- **V1 — THE CONTROL RUNG.** At family A `f = 0.00`, all 39 `af_fit_summary.txt` budget tables must reproduce
  wave 13's **exactly** — same winner model, `#rej`, rejected positions, σ_focus, redχ², R² and `minPos` at all
  four budgets, on all 39 runs. **This, not RULE G14, is the proof that the added parameter is inert at its
  default.** Any miss ⇒ **Stage B is UNEVALUATED** and the miss is investigated as a finding (the tree differs
  from wave 13's `f9f2074` by F15 and item 1, neither of which can reach this table — so a miss means
  something else moved).
- **V2 — POPULATION BY COUNT.** `verify_affit_w14.sh <dir> <expected>` asserts **39 of 39** per rung and the
  `Settings: D:\hf_w11\pinned_settings_w11.json` positive-control line in every log. A short population makes
  every number below a non-population statistic; the rung is UNEVALUATED, **not** "fewer runs fired".
- **V3 — STAGE A's REPRODUCTION.** 72 of 72 rounds reproduced at `f = 0`, per §2.6.1. Below 72, Stage A is
  UNEVALUATED and Stage B is reported without it. **Zero INCONSISTENT rounds** — an INCONSISTENT round is a
  scorer bug and blocks Stage A entirely. **UNRESOLVABLE rounds do not block anything**; they are counted,
  named, and propagated as intervals.
- **V4 — THE PREFIX LEMMA IS FALSIFIABLE, AND IT IS TESTED.** For every run and rung, Stage B's production row
  must equal one of that run's **wave-13 printed rows at budget ≤ B**. **A row that matches none of them
  refutes the lemma**, and if that happens Stage A is **retracted in full** and Stage B is reported as raw
  numbers with no counterfactual framework. *This is the clause that makes Stage A's cheapness legitimate.*

#### Decision clauses

- **C1 — CONTAINMENT (the benefit).** `max over the 13 budget-3 firing runs of ρ(run, f) ≤ 1.10`, **reported
  alongside R² and reduced χ² at budget 3 on the same runs, which are not optional decoration.**
  `Panos` is **UNEVALUATED by name** (its σ fit is degenerate — `σ_focus` at budget 3 parses as `NaN`), never
  silently dropped, and Newtonsoft writes it as the **string** `"NaN"`.
  *At `f = 0` that maximum is **45.18** (`caboose`). 1.10 = within 10 % of the run's own un-pruned fit.*
  > **`ρ` IS A CONTAINMENT MEASURE, NOT AN ACCURACY FACTOR — and the wave-14 F62 audit
  > ([`docs/wave14-f62-audit.md`](wave14-f62-audit.md)) is why that sentence is here.** σ_focus rises
  > **mechanically** as the surviving point count approaches the parameter count (`s² = weighted RSS/(n−p)` and
  > `(JᵀWJ)⁻¹` both grow) — on `caboose` at budget 3 that is 5 points against 4 parameters. **So 45.18 must
  > never be quoted as "45× less accurate".** What `ρ` legitimately measures here is whether the floor prevents
  > the removals, because `ρ = 1` exactly when nothing is removed. Two things keep C1 honest: the **direction**
  > survives F62 outright (F62's bias predicts *improvement* from dropping points, and a bias cannot
  > manufacture its own opposite), and **R² 0.99987 → 0.91475 and redχ² 0.000317 → 3.946 carry the conclusion
  > without any `n − p` term.** The audit also records that **no out-of-sample vertex check was ever run at
  > budget 3** — C4 is that check, at both budgets, for the first time.
- **C2 — SELECTIVITY (the cost, and the clause that actually decides).** `N_fire(3, f) ≥ 1` **and**
  `N_fire(1, f) ≥ 1` over the 39-run population.
  > **A rung at which nothing anywhere still rejects has not repaired the Grubbs test — it has disabled it, and
  > the product already has a cheaper and clearer way to say that: `MaxOutlierRejections` = 0, which item 1
  > makes the default.** Report `N_fire(B, f)` for `B ∈ {1,2,3}` × every rung as a table; it is this item's
  > most transferable number.
- **C3 — `caboose` AT BUDGET 3, the named clause** (F45 says (b) is the only sub-question that would let the
  budget be raised safely, and this is what "safely" means). Three-valued:
  *C3 is deliberately a FOUR-criterion clause, and two of the four (`R²`, `minPos`) carry no `n − p` term, so
  the F62 weakening of σ_focus cannot decide it on its own.*
  - **CONTAINS-SELECTIVELY** — budget 3 keeps winner `TiltedHyperbola`, `σ_focus ≤ 0.40`, `R² ≥ 0.9999`,
    `|minPos − 7101.58| ≤ 1` focuser unit **and** budgets 0–2 are unchanged (0 and 1 reject nothing; budget 2's
    single rejection of 7275 is *benign* — σ 0.364 → 0.172, R² → 0.999999);
  - **CONTAINS-BLUNTLY** — budget 3 fixed, but budget 2's benign rejection is suppressed too;
  - **DOES NOT CONTAIN** — otherwise.
- **C4 — OUT-OF-SAMPLE GUARD, veto-only and pre-registered as immaterial.** Median and **max** `|Δe|` between
  each rung and `f = 0`, over the 20 synthetic datasets, at budgets 1 and 3. Vetoes only if the median exceeds
  **0.10 step** — wave 13's own materiality floor, unchanged.
- **C5 — BLAST RADIUS.** Number of the 39 runs whose **budget-1** production row changes at each rung, reported
  per bank. This is what *"(b) changes every AF fit in the product"* costs, in runs.

#### The verdict — and the wave returns a RECOMMENDATION, not a ship

**F45(b) is expensive to ship (it changes every AF fit), so wave 14 measures it and returns a costed
recommendation. The rule decides what the recommendation says, not whether it ships.**

> - **RECOMMEND, at a named family and rung** — iff V1–V4 pass **and** C1 holds **and** C2 holds **and** C3 is
>   CONTAINS-SELECTIVELY or CONTAINS-BLUNTLY **and** C4 does not veto. The recommended rung is the **smallest**
>   satisfying all of them; ties between families go to **B**, because B leaves round 1 bit-identical by
>   construction and is therefore the smaller product change. The recommendation is costed (§2.10) and does
>   **not** ship this wave.
> - **RECOMMEND AGAINST — "the floor does not contain the cascade"** — iff C1 or C3 fails at every rung of
>   every family. F45(b)'s only claimed benefit is then absent and the sub-question closes.
> - **RECOMMEND AGAINST — "the floor is a disguised off-switch"** — iff every rung that satisfies C1 and C3
>   fails C2 (`N_fire = 0`). The floor's containment is then total suppression, which the product achieves more
>   cheaply and more legibly with a budget of 0.
> - **RECOMMEND, IN SEM UNITS, NOT THESE** — iff C1/C3 hold only at rungs that fail C2, **and** Stage A(2)
>   separates (every rejection on a run with `ρ > 2` has `s < 1`, and at least half the rejections on runs with
>   `ρ ≤ 1.10` have `s ≥ 1`). The finding is then that the floor is right and the **units** in the register are
>   wrong, and the recommendation names the SEM-unit change and its price.
> - **NO VERDICT** — iff any validity gate fails, or the families/rungs disagree in a way none of the above
>   covers. Reported as "no verdict", with the reason, and **never** resolved by preferring whichever rung
>   agrees with something else.

### §2.10 SATISFIABILITY — every clause's maximum attainable value, stated before the data

Wave 13's D1 clause (a) needed ≥ 15 of 20 wins, which 16 ties made unreachable in principle; the standing
prompt calls that *"a badly-formed bar, not a passed test"*. This section exists so this wave does not inherit
the shape.

**The structural fact that forecloses the obvious bar:** a floor can only **suppress**. Only **7 of 39** runs
fire at budget 1 and only **13 of 39** at budget 3. **So any clause phrased over 20 or 39 datasets, or any
clause requiring the floor to IMPROVE something on a run that never fired, is foreclosed by construction.**
Every clause above is phrased over the firing population, or over a magnitude, for exactly that reason.

| clause | population | max attainable | reachable? |
|---|---|---|---|
| **V1** | 39 runs × 4 budgets | 39 of 39 identical | yes — item 1 and F15 cannot reach `af-fit`'s budget table; a miss is a finding |
| **V2** | 39 | 39 | yes |
| **V3** | **72 rounds** | 72 of 72 reproduced, 0 INCONSISTENT | yes — every input (`r`, `median(r)`, `z`, limit) is printed for every row, and at `f = 0` every round is INERT so no rounding is involved. **The number of UNRESOLVABLE rounds at rungs > 0 is a REPORTED QUANTITY, not a bar**; 20 of 64 usable rounds sit within 25 % of the limit, so a non-zero count is expected and is not a failure |
| **V4** | 39 × 6 rungs | all rows match a wave-13 row | yes — and it CAN fail; that is its job |
| **C1** | **13** budget-3 firing runs, `Panos` UNEVALUATED ⇒ effectively 12 | `max ρ ≤ 1.10` | **yes.** At `f = 0` only ONE run has `ρ > 2` (`caboose`, 45.18). **This is an n = 1 clause on the benefit side and this document says so** — the other 11 sit at ρ ∈ [0.30, 1.13] and cannot demonstrate containment because they have nothing to contain |
| **C2** | 39 | `N_fire(3, f) ∈ [0, 13]`, `N_fire(1, f) ∈ [0, 7]` | **yes, in both directions**, and this is the clause with real information: 51 of 72 rounds have a scale below 0.25, so `N_fire → 0` is a live outcome at every rung |
| **C3** | 1 run, 4 budget rows | CONTAINS-SELECTIVELY | **NO for this ladder.** `caboose`'s round 1 has z = 1609.6 at scale 1.48e-5, so any family-A rung ≥ 0.25 suppresses round 1 — and budget 2's benign rejection with it. **The maximum attainable is CONTAINS-BLUNTLY for family A and DOES NOT CONTAIN for family B, and both are predicted here before the measurement** |
| **C4** | 20 synthetic | median &#124;Δe&#124; ∈ [0, **0.00993**] step | **CANNOT return "material" in either direction** — see below |
| **C5** | 39 | 0…7 runs at budget 1 | yes |

#### C4 is foreclosed as a decision clause, and it is knowable today

By the prefix lemma, `e^f` for any dataset can only take a value that dataset already attains at some budget
≤ B. Wave 13 measured all of them. Across the whole synthetic bank:

| dataset | e at budgets 0 / 1 / 2 / 3 | reachable change |
|---|---|---|
| `D01_ultrawide_40mm` | 0.00111 / 0.00111 / 0.00111 / 0.00111 | **0** |
| `D08_c11_2800mm` | 0.01829 / 0.01951 / 0.01951 / 0.01951 | 0.00122 |
| `D12_c14_585_afbin2` | 0.00142 / 0.01135 / 0.01135 / 0.01135 | **0.00993** ← the largest in the bank |
| `D16_esprit550_ha3` | 0.00067 / 0.00200 / 0.00200 / 0.00200 | 0.00133 |
| `D17_cdk14_oiii5` | 0.03000 / 0.03000 / 0.03000 / 0.03000 | **0** |
| the other 15 | never fire | **0** |

**And the thinnest-margin round lands where it does the least harm.** `D01` round 1 (ratio 1.0005, §2.6.1) is
the one whose reject/keep decision is inside the printed precision — and `D01`'s `e` is **0.00111 at every
budget**, so the rounding exposure cannot move C4 at all. **It moves C2 and C5** (the `N_fire` and blast-radius
counts), where a single run either is or is not in a count. Those two clauses therefore report `D01` explicitly
as *possibly-either* at family-A rungs where `f` approaches its scale of 0.9885, rather than absorbing it into
a total.

> **The largest out-of-sample movement the floor can produce anywhere in this population is 0.00993 step — 10×
> below the 0.10-step materiality floor wave 13 pre-registered, and the floor a wave cannot move without moving
> the goalposts.** So the synthetic bank's out-of-sample arbiter **cannot decide F45(b)**. It is kept, at zero
> cost, in two roles that do not depend on materiality: as a **veto** (C4), and as the **falsifier of the
> prefix lemma** (V4). *An arbiter that cannot decide should be demoted to a control, not quietly re-weighted
> until it can.*

#### And the honest statistical limit, up front

**One run (`caboose`) carries C1's benefit side and one run carries C3.** A 1-of-1 or 3-of-4 direction is not
evidence at any conventional bar — wave 13 said so about its own 3–0 result (a 3–0 sign test is *p* = 0.25
one-sided). **That is why every clause above is phrased as a magnitude bound or a mechanism, not a win count**:
45.2× on a run with R² = 0.99987, a rejected point at 2.4 % of its own error bar, and `N_fire` over 39 runs do
not depend on sample size the way a sign test does.

### §2.11 What item 2 cannot answer, said in advance

- **It cannot select `f`.** The ladder spans 4×; √N\* spans 10× across this bank. Only the SEM-unit floor can,
  and it needs the star count inside `RejectionTest` — a deeper change than an optional scalar, because
  `ScatterErrorPoint` carries `(X, Y, ErrorX, ErrorY)` and no star count. **Price: ~2 h** (thread `N*` from
  `MeasurePoint` construction through the fit to the rejection test, plus tests), and it is the named follow-up.
- **It cannot decide for a rig whose curve is worse than anything in the bank.** 20 well-formed synthetic
  sweeps and 19 real runs; a rig producing one wild point per sweep is where a rejection budget earns its keep
  and no frame in either bank is that rig.
- **It cannot separate the floor from `OutlierRejectionConfidence`**, pinned at 0.95 on every rung — which
  fires *more* readily than F45's own field case at 0.99 (Grubbs limit at N = 9: 2.2150 vs 2.3868).
- **It says nothing about the default user after item 1.** With `MaxOutlierRejections` defaulting to 0 the
  rejection does not run at all, so the floor reaches only profiles that explicitly store ≥ 1 — **and any
  future decision to raise the budget.** *Item 1 makes item 2's ship value contingent, and that is stated here
  rather than discovered in the results.*

---

## Item 3 — F62 audit (criteria fixed at spawn; inserted by controller)

*(A separate agent ran this with criteria the controller fixed at spawn time. The criteria are this item's
pre-registration and are to be recorded here **verbatim by the controller**. This section is deliberately not
written by the pre-registration agent — designing the audit here would replace criteria that were already
fixed, which is the one thing a pre-registration must never do.)*

Its output is [`docs/wave14-f62-audit.md`](wave14-f62-audit.md), and **two of its findings are already load
bearing in this document** and were folded in above rather than left to the results doc:

- **the `caboose` magnitude is WEAKENED** — σ_focus inflates mechanically as `n − p` shrinks, so RULE F14's C1
  now states in the rule itself that `ρ` is a containment measure and not an accuracy factor, and requires R²
  and reduced χ² to be reported beside it;
- **[F61]'s "`MOR`=0 better on 10 of 10 `sChi`" is WEAKENED** — the sensor paraboloid is weighted by `1/σ²`
  where σ is each star's own `MinimumStdError`, so a fired rejection *raises* that star's weight and *raises*
  `sChi` for arithmetic reasons. **That is one of the four facts item 1 cites as support** (§ item 1), and the
  results doc must carry the weakening beside the citation rather than repeating the wave-13 sentence
  unqualified. *An owner override that quotes weakened support without saying it is weakened is a worse
  document than one that ships nothing.*

---

## Item 4 — the nine UI changes A1–A9: NOT ATTEMPTABLE, one line

`query session` at wave start: session 1 (`ghili`) is **`Disc`**, console is `Conn`. Per wave 13 §3 that means
there is **no composited desktop and a WPF client area cannot be captured** by any of the three routes wave 13
tried. Not attemptable this wave. **Price stays ~30 minutes on a connected session**, with wave 13's procedure
unchanged (its expensive step — comparing the deployed DLL's sha256 to the freshly built one *before* launch —
already passes).

---

## Item 5 — F57(d): CLOSE, and the reasoning is not the register's

The register recommends closing F57(d) (the unused `D:\hf_w10\exe_v1wav` landing-level wavelet bisect) on three
bounds. **Two of those three bounds do not bear on the question, and this document says so rather than
inheriting them:**

| bound | what it establishes | does it bear on ≤3e-8 numerical sensitivity? |
|---|---|---|
| 1. wave 10's crossbuild probe — 15 runs on v1/v2/**bisect** binaries all returning `0.9834767969` | the **seed** is insensitive to the wavelet change | **YES** — this is the one on point, and it covers only the seed |
| 2. wave 12's RULE A12 — 8 landings bit-identical under deliberate process contention at fan-out 4 | the search is not sensitive to **scheduling** | **barely.** Process contention perturbs timing, not arithmetic; a deterministic pipeline passing it is close to tautological |
| 3. RULE G13 — the same 8 landings on a FOURTH binary, bit-identical to 16 digits | **build-to-build determinism of the same source** | **no.** Rebuilding identical source cannot produce a wavelet difference; this measures the compiler, not the question |

**The reason to close is not the bounds. It is that the arm is no longer CONSTRUCTIBLE to this register's own
standard:**

1. **`exe_v1wav` vs wave 14's `exe` is not a bisect.** It spans four waves of source change and would confound
   the wavelet with F58(d)/F61's `AutoFocusOptions` conversion, F15, and everything else. A bisect with one
   variable is the only thing worth 42 minutes.
2. **The only uncofounded comparison is `exe_v1wav` vs `D:\hf_w10\exe`** — same tree, one variable — and wave
   10's stored landings are **VOID**: [F57](followups.md) voided RULE G10-B because those landings ran under a
   different active profile and were never `--profile-id` pinned. **So both halves must be re-run: ~84 minutes,
   not 42.**
3. ~~**And they cannot be pinned.**~~ — **RETRACTED BY THE CONTROLLER, 2026-08-09, BEFORE ANY WAVE-14 ARM RAN.
   See the amendment below. The disposition flips with it.**

### AMENDMENT — the "not constructible" reason was WRONG, and the test that produced it could not fail

The pre-registration argued that `exe_v1wav` cannot pass `--profile-id`, on this evidence:

```
strings D:\hf_w10\exe\TestApp.dll      | grep -c -- '--profile-id'   ->  0
strings D:\hf_w10\exe_v1wav\TestApp.dll | grep -c -- '--profile-id'  ->  0
```

**The controller ran the same command against the CURRENT binary — the one that demonstrably accepts the flag on
every arm of waves 11–13 — and it also returns 0.** A test that reports "absent" for a binary known to have it is
not measuring presence. *This is the register's own recurring failure, in its purest form: an instrument that is
not connected reports whatever you were expecting.*

**The mechanism.** `strings` scans for runs of ASCII bytes. A .NET assembly stores **type and member names** in
the `#Strings` heap as UTF-8 — findable — but **string literals** in the `#US` heap as **UTF-16**, where every
character is followed by a null byte and no ASCII run exists. `--profile-id` is a literal
(`DiagnosticUtil.GetArg(args, "--profile-id")`); `AtrousWaveletFast` — wave 13's provenance probe, which *did*
report hits — is a type name. **Two probes, two heaps, and the register has been reading them as one instrument.**

Re-run against UTF-16:

```
strings -el <dll> | grep -c -- '--profile-id'
  D:\hf_w10\exe          28
  D:\hf_w10\exe_v1wav    28        <-- the flag is PRESENT
  D:\hf_w13\exe          28        <-- the control now discriminates nothing, which is the point
```

> **Both wave-10 binaries support `--profile-id`. The arm is CONSTRUCTIBLE and PINNABLE.**

**And the confound argument — the controller's own — does not survive either, for the reason the pre-registration
gave.** `BestJ` has no sensor-model term ([F4](followups.md)), so F61 cannot reach it; and in any case **the only
comparison that matters is `exe_v1wav` against `D:\hf_w10\exe`, which are the SAME TREE differing in one
variable.** Every wave-11+ change (F58's `HarnessFitInputs`, F15, F61) is absent from *both* arms identically, so
it cannot confound an A/B between them. The arm is not merely constructible — it is **cleaner than the reason
given for skipping it**, because it never needs to be compared to a modern number at all.

> ### **DISPOSITION, REVISED BEFORE ANY WAVE-14 DATA EXISTS: RUN IT, then close F57(d) either way.**
>
> Two arms × 8 gate runs, both wave-10 binaries, `--settings D:\hf_w11\pinned_settings_w11.json` and
> `--profile-id ce3f3e63-…` on both. Wave 10's stored landings stay VOID and are not read — **both halves are
> re-run**, so the comparison is internally generated. **~84 m**, against a wave sitting at ~1 h 50 m of a 6 h
> ceiling.
>
> **The pass/fail is stated now: RULE W14-D.** The eight `BestJ` from `exe_v1wav` are compared to the eight from
> `D:\hf_w10\exe`, **arm-to-arm, never to wave 11's table** — a wave-10 binary is not expected to reproduce the
> modern gate and a mismatch there would mean nothing. Three outcomes, fixed here:
> - **8 of 8 bit-identical** ⇒ the wavelet version does not reach the landing. F57(d) closes as ANSWERED, and
>   the crossbuild probe's seed-level result is extended to the search.
> - **any run differs** ⇒ the wavelet version *does* reach the landing, F57(d) closes as ANSWERED-POSITIVE, and
>   the magnitude is reported per run. **This outcome does not invalidate anything**: every wave since 10 has
>   run one wavelet version, so it would be a statement about v1, not about the published numbers.
> - **an arm fails to run at all** ⇒ NOT CONSTRUCTIBLE is recorded *as a measured fact* rather than as an
>   inference from a probe, and F57(d) closes on that.
>
> **Either way `D:\hf_w10\exe_v1wav` is archived or deleted afterwards and the register says so** — the cost of
> leaving it open is a build directory every future wave has to explain, which is what it has cost for four.
>
> *Two reasons to skip this were offered — the controller's (confounded) and the pre-registration's (not
> constructible). Both were wrong, and they were wrong in opposite directions from the same habit: neither had
> been checked against a control that could fail. The item survived four waves on unchecked reasons; it is
> cheaper to run it than to keep arguing about it.*

---

## Budget — pre-registered so the wave can be scored against it

| arm | estimated | actual |
|---|---|---|
| RULE G14 — the gate, 8 runs sequential | **~42 m** (wave 13 measured 41 m 46 s) | |
| Stage A(1) + A(2) — scorers over wave 13 artifacts | **< 1 m**, no `TestApp` | |
| Stage B rung — family A, `f = 0.00` (**the control**) | ~11 m | |
| Stage B rung — family A, `f = 0.25` | ~11 m | |
| Stage B rung — family A, `f = 0.50` | ~11 m | |
| Stage B rung — family A, `f = 1.00` | ~11 m | |
| Stage B rung — family B, `α = 0.50` | ~11 m | |
| Stage B rung — family B, `α = 1.00` | ~11 m | |
| **RULE W14-D** — F57(d), 8 runs × 2 wave-10 binaries *(added by the amendment above)* | **~84 m** | |
| **total compute** | **~3 h 14 m** | |

*(Each Stage B rung = 20 synthetic + 19 real; wave 13 measured 3 m 46 s + 7 m = 10 m 46 s for one full pass.)*

**Drop order if the gate over-runs or an arm misbehaves:** **RULE W14-D first** — it is the largest single arm
and the only one whose item has already survived four waves of deferral, so dropping it costs nothing that has
not already been paid; then `f = 0.50` (it interpolates between two rungs that are already measured), then
`α = 0.50`. **`f = 0.00` is never dropped** — it is V1, the wave's only real control on item 2's code change.
**Family B is never dropped before family A's rungs**, because dropping the family this document predicts will
fail removes the falsification, not the cost.

The gate runs on a quiet machine. Later arms may overlap with editing and building — which cannot move a
landing (wave 12's RULE A12) but **can** move a timing, so no timing claim is made from an arm that shared the
machine, and any that does says so.

---

## What this wave will NOT run, and what it would cost

- **F63(a) — extending D5's "6 of 8 landings move" to the 39-run population.** ~3 h sequentially (~2 ¼ h fanned
  out, [F60](followups.md)'s measurement). **It is the most interesting thing wave 13 left undone**, because D5
  is the clause that fired, and after item 1 the question changes shape: the population arm would now measure
  what the *new* default recommends against what the old one did. Deferred because item 2 is cheaper per unit
  of decision and because the ~6 h ceiling does not fit both plus the gate.
- **The SEM-unit floor** (`z` computed on `r·√N\*`). **~2 h**: thread the star count from `MeasurePoint`
  construction through the fit to `RejectionTest`, plus tests. Named as item 2's likely recommendation shape
  (§2.11) and deliberately not attempted in the same wave that measures whether a floor is wanted at all.
- **F59's five knobs** (`MaxDistortion`, `StarCenterTolerance`, `SaturationThreshold`, `HotpixelThreshold`,
  `Sensitivity`) stay at code defaults. Re-pinning **moves the coordinate system** RULE G14 will have
  reproduced a fifth time: **~42 m for a new gate baseline plus the re-derivation of every cross-wave
  comparison in the register.** It is a decision, not a cleanup.
- **F63(b) — pinning the SHIPPED default rather than `astrodet`'s in the harness.** Same price and same shape
  as F59, and now sharper: after item 1 the shipped default *is* `astrodet`'s value, so this deferral is
  cheaper than it was last wave and should be re-priced next wave.
- **Item 4's nine UI changes**: ~30 m on a **connected** session (§ item 4).
- **F57(d)**: closed, not run — ~84 m for an arm that cannot be pinned (§ item 5).
- **F61(b), F52(d), F46(b), F54, F50**: nothing depends on them.

---

## The drivers, written with the pre-registration and committed with it

All under `D:\hf_w14\` (WSL `/mnt/d/hf_w14/`), LF line endings, both pins named in every script header.

| script | what it does, and the trap it has baked in |
|---|---|
| `gate_w14.sh` | RULE G14. Refuses to guess the running-process count (F55(c)'s `grep -c` trap), refuses to run while NINA is up, `< /dev/null` on every invocation, and **asserts the population of 8 `aggregate_summary.json` before printing anything** |
| `prov_w14.py` | the free controls across the arm — `BuildId` novel against **waves 11, 12 and 13**, one `BuildId` per arm, `DetectorVersion` 2 ×8, profile pinned ×8, one `FitInputs`, `ConcurrencyCheck` read across all eight |
| `affit_w14.sh` | Stage B. **Rejects any rung not on the pre-registered ladder** — a rung invented at run time is a rung nobody pre-registered — refuses to rebuild an existing arm directory (F53(c)), TAB-separated real list (the `timmer/5 …` space), `< /dev/null`, and asserts the list is 19 rows before starting |
| `verify_affit_w14.sh` | the population COUNT **and** the `Settings:` positive control, per rung. A missing directory is *"COULD NOT LOOK"*, never zero |
| `stageA_w14.py` | the counterfactual + the SEM audit. Four-state round classification, interval-propagated scale recovery, a **self-test that must flip the verdict**, and the round count pre-registered at 72 |
| `score_affit_w14.py` | RULE F14, validity gates printed **before** any decision clause; V1 exact-field comparison against wave 13; refuses Windows paths |

## Traps carried into this wave, kept where they will be read

- **Pass the scorers WSL paths** (`/mnt/d/…`), never Windows paths. A Windows path yields UNEVALUATED — correct
  behaviour that looks exactly like a failed arm (wave 13 §1.1).
- **Silent truncation exits 0, two ways here**: one bank path contains a **SPACE**
  (`timmer/5 AutoFocus_…/attempt01`), and `TestApp.exe` inherits a `while read` loop's stdin and eats the rest
  of the list unless redirected `< /dev/null`. **Assert the POPULATION SIZE.**
- **"Could not look" needs its own state at every level, and the guard comes BEFORE any field read.**
- **Never run NINA during a pinned arm** — `Profile.Load` holds the `.profile` open and the arm throws.
- **Pin `--settings` AND `--profile-id` on every arm.** No fan-out; a pinned arm cannot fan out at all, and
  fan-out is 1.33× rather than 4× ([F60](followups.md)).
- **Newtonsoft writes NaN/Infinity as the STRINGS `"NaN"`/`"Infinity"`** — `Panos`'s budget-3 σ_focus is
  exactly this case.
- `D17_cdk14_oiii5` finds zero stars at short exposures; `lumos` exits rc=3 reproducibly; `astrodet` **the
  DATASET** is frameless ([F14](followups.md)); `Panos` has a degenerate σ fit.
  `D:\SyntheticAutofocusBank` = 20 datasets, `D:\Autofocus Bank` = 19 runs.
- **Never rebuild an arm's directory mid-wave** ([F53](followups.md)(c)). One binary, `D:\hf_w14\exe`.
- **`ConcurrencyCheck == exclusive` on a single landing proves nothing.** Read it across the arm.
- **The fire rate is not one number**, and neither is the blow-up ratio: `caboose` degrades **12.7×** through
  `bank-verify` and **45.2×** through `af-fit`. Never quote one for the other — **and never quote either as an
  accuracy factor**, because σ_focus inflates mechanically as `n − p` shrinks (wave-14 F62 audit).
- **On CI, verify the COUNT, not the tick** ([F37](followups.md)). The branch baseline is **3755**, not
  `develop`'s 3744.
- Wave 5's φ table is invalid on three axes — never quote it.
