# Synthetic AF bank — followups wave 14 (results)

Design: [`docs/synthetic-af-bank-followups-wave14-design.md`](synthetic-af-bank-followups-wave14-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave14-plan.md`](../plans/synthetic-af-bank-followups-wave14-plan.md).
Wave 13: [`docs/synthetic-af-bank-followups-wave13-results.md`](synthetic-af-bank-followups-wave13-results.md).
F62 audit: [`docs/wave14-f62-audit.md`](wave14-f62-audit.md).
Stage A's committed prediction: [`docs/wave14-stageA/`](wave14-stageA/).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`. Pre-registration `11811f6`, item 1 `316b298`, Stage A prediction `5d1186b` (committed 2026-08-09T21:49:34Z), Stage B code `486fb1f` |
> | binary — **gate** | `D:\hf_w14\exe` · `TestApp.dll` sha256 `116c448d96…` · `NINA.Joko.Plugins.HocusFocus.dll` sha256 `df22d1e1e5…` · `BuildId` `084e3485f9f94260a1cfc2d9ac99f453` |
> | binary — **Stage B** | `D:\hf_w14\exe_floor` · `TestApp.dll` sha256 `8a49565425…` · `NINA.Joko.Plugins.HocusFocus.dll` sha256 `8a430f61cb…` — **a different binary, deliberately** (see below) |
> | detector | `DetectorVersion` **2**, confirmed as a FIELD on all 8 gate landings, not from a `strings` probe (see §1.2) |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`** — sets `MaxOutlierRejections` explicitly to **0** |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm |
> | profile set | `D:\hf_w14\profiles_after_default_change_w14.txt`, 9 profiles |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = truth), `D:\Autofocus Bank` (19 runs) |
> | suite | **3781 passed, 0 failed, 0 skipped** = 3755 wave-13 baseline + 1 (item 1's invariant test) + 25 (Stage B) |
>
> **TWO BINARIES, AND WHAT THAT COSTS.** The pre-registration fixed *one* binary for the whole wave. It did not
> survive contact with its own F53(c) clause: `D:\hf_w14\exe` was built and frozen for the gate **before item
> 2's Stage B code existed**, and rebuilding that directory mid-wave is precisely what F53(c) forbids. Stage B
> therefore ran on a second directory, `D:\hf_w14\exe_floor`.
>
> **The consequence, stated rather than buried: RULE G14 is not a control on Stage B's binary.** The eight
> `BestJ` say nothing about the tree that carries `madFloor`. That is exactly why V1 exists — the `f = 0.00`
> control rung re-runs all 39 `af-fit` runs at budgets 0…3, where the rejection *does* execute, and must
> reproduce wave 13's tables field for field. **V1, not RULE G14, is this wave's proof that the added parameter
> is inert at its default**, and the design said so before the split happened.
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out (wave 11's K3); the
> authorisation is worth 1.33×, not 4× ([F60](followups.md)). Wave 5's φ table is invalid on three axes and is
> quoted nowhere.

## Status of this document

| item | state |
|---|---|
| **RULE G14** — the gate | **PASS, 8 of 8 to 6 dp, bit-identical to all sixteen digits, on a FIFTH binary.** §1 |
| **item 1** — `MaxOutlierRejections` code default 1 → 0 | **SHIPPED, as an owner's product-coherence decision that OVERRIDES wave 13's RULE M13.** §2 |
| **item 2** — F45(b), the σ-tied MAD floor | **RULE F14 returns NO VERDICT. The failing gate is V4.** All six rungs ran; the numbers are recorded as diagnostics. §3 |
| **item 3** — the F62 audit | **DONE**, [`docs/wave14-f62-audit.md`](wave14-f62-audit.md). 12 hits: 2 INVALIDATED, 3 WEAKENED, 7 SURVIVES, 2 UNEVALUATED. §4 |
| **item 4** — the nine UI changes A1–A9 | **NOT ATTEMPTABLE.** Fifth wave. §5 |
| **item 5** — F57(d), RULE W14-D | **RUNNING at the time of writing.** §6 is a placeholder and contains no result |

---

## §1 — RULE G14: the coordinate system on a fifth binary, and the first one whose product default differs

`D:\hf_w14\gate_w14.sh`, **21:47:32Z–22:29:20Z = 41 m 48 s**, sequential, `--per-run --max-evals 250`, both pins
named in the script header, machine quiet.

| run | `BestJ` (exact) | 6 dp | expected | | bit == K8 | `BaselineJ` | wave 11 |
|---|---|---|---|---|---|---|---|
| `toml999` | 0.9957838768299878 | 0.995784 | 0.995784 | **MATCH** | **YES** | 0.983477 | ok |
| `CWhiteFocus` | 0.9960675916058808 | 0.996068 | 0.996068 | **MATCH** | **YES** | 0.994320 | ok |
| `uneven` | 0.9963677194179505 | 0.996368 | 0.996368 | **MATCH** | **YES** | 0.991794 | ok |
| `muggsie` | 0.9971948738498605 | 0.997195 | 0.997195 | **MATCH** | **YES** | 0.993288 | ok |
| `mccomiskey` | 0.9767460801208465 | 0.976746 | 0.976746 | **MATCH** | **YES** | 0.846082 | ok |
| `D18_m24_deep_shed` | 0.9998815090506263 | 0.999882 | 0.999882 | **MATCH** | **YES** | 0.997405 | ok |
| `D19_cygnus_deep_shed` | 0.9994870586135448 | 0.999487 | 0.999487 | **MATCH** | **YES** | 0.999187 | ok |
| `D20_m24_bright_control` | 0.9997378027339423 | 0.999738 | 0.999738 | **MATCH** | **YES** | 0.999454 | ok |

> **RULE G14: PASS, 8 of 8 to 6 dp, and every one bit-identical to all sixteen digits.** Wave 11's eight values
> have now reproduced across **five** binaries and two settings files. [F41](followups.md)'s free control
> (`BaselineJ`) passes 8 of 8 as well.

**The gate was a real control here, not a formality.** `astrodet` stores `MaxOutlierRejections` = 0
**explicitly**, and `pinned_settings_w11.json` sets it explicitly too, so item 1's changed *fallback* is never
consulted on this arm. A gate that moved would have meant the change reached somewhere it should not, and the
wave would have stopped before item 2 ran.

**The free controls, diffed rather than asserted.** `BuildId` `084e3485f9f94260a1cfc2d9ac99f453` — one value
across all eight, and **novel** against wave 11's `5cb7e474…`, wave 12's `103d61c4…` and wave 13's `62334f10…`,
so a build did happen. `DetectorVersion` **2 ×8**. `ProfileId` `astrodet` ×8 — the pin took. `FitInputs` one
distinct value, `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid`.
`ConcurrencyCheck` `exclusive` on **all eight, read across the whole arm** — one landing's `exclusive` proves
nothing, because `WaitOne(0)` is won by exactly one of N contenders.

Reproduce: `python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w14/gate --rule G14` (a **WSL** path; a Windows path
yields eight `UNEVALUATED` and looks exactly like a failed arm — wave 13 §1.1).

### §1.1 The free-controls scorer reports FAIL on a passing arm, and its PASS is unreachable

`prov_w14.py` — written for this wave to carry the `BuildId`-novelty check `score_w12.py` lacks — exits **1**
with `RULE G14 free controls: **FAIL**` on the passing gate. Its diagnostic gives it away:

```
the --profile-id pin did not take on every landing: ['astrodet (ce3f3e63-8fd3-4b72-a0ca-d90db9441382)']
```

The landing records `ProfileId` as `astrodet (ce3f3e63-…)` — **name and GUID**. The scorer compares it against
the bare GUID (`EXPECT_PROFILE = "ce3f3e63-…"`). The comparison is unequal for every landing that could ever be
produced, so **the PASS branch is unreachable**: the check fails identically whether the pin took or not.

Every other assertion in the file passes, and the pin genuinely took — `score_w12.py` establishes it by a
*cardinality* test (one distinct `ProfileId` across the arm) that does not depend on the format. **The
provenance conclusion is sound; the instrument that was supposed to certify it is not.**

> This is the third distinct shape of the same defect in three waves. Wave 12's scorer was wrong twice for want
> of a "could not look" state; wave 13's `RX_BUDGET` parsed 39 files as unreadable and printed **"PASS — 0 of 0
> rounds reproduced"**; wave 14's `prov_w14.py` fails closed instead of open. **Failing closed is the better
> direction and it is still a broken instrument** — a control that always says FAIL trains its reader to skip
> it, which is how a real failure gets through. *A check needs a demonstrated pass and a demonstrated fail
> before it is a check.* See §3.4 and §3.5 for two more of these in the same wave.

#### §1.1.1 It was repaired, and then shown to fail — because a repaired control is not a control either

The pin is now tested by **containment** of the GUID, with a separate clause asserting **exactly one** distinct
`ProfileId` across the arm — the cardinality test that was doing the real work in `score_w12.py` all along, now
stated rather than borrowed. Both directions were then demonstrated on the wave's own data:

| | result |
|---|---|
| the real gate arm, 8 landings | **PASS** — `BuildId` novel, Det 2 ×8, profile pinned ×8, one `FitInputs`, `ConcurrencyCheck` exclusive read ACROSS the arm |
| a copy with **one** landing's `ProfileId` rewritten to `Default (b10b1d6d-…)` | **FAIL**, on both clauses, naming both values |

**The first attempt at that demonstration silently proved nothing, and it is worth recording.** It wrote the
mutation to `<gate>/muggsie/optimized_settings.json`, which does not exist — the landings sit one level deeper,
under `<run>/<attempt>/`. `json.load` raised, the mutation never happened, and the scorer reported **PASS on an
unmutated copy**, which reads exactly like a control that failed to discriminate. The retry asserts the landing
list is non-empty and the `ProfileId` field is present **before** mutating, and prints the value before and
after. *A test that a control can fail is itself a check that can silently not run, and it needs the same guard
as everything else in this register.*

### §1.2 The free control that PASSED, and it is the first of its kind: the bank was not mutated

Wave 14's gate is the **first arm in this series to run on a post-F15-fix binary**, where the run-folder
write-back is opt-in behind `--update-run-folder` and no arm passes it. All **42** bank
`optimized_settings.json` files (both banks, plus `_prior_reports`) were fingerprinted by sha256 + size + mtime
to `/mnt/d/hf_w14/bank_landing_fingerprint_BEFORE.json` **before** any arm ran.

> **42 of 42 byte-identical after the gate AND after all six Stage B rungs. 0 changed, 0 could-not-look.**

That is wave 13's item 3 confirmed **in an arm**, not in a unit test. For thirteen waves every `optimize
--per-run` pass silently re-baselined both banks' stored settings ([F15](followups.md)); this is the first arm
in the series that provably did not.

**Read the fingerprint against the snapshot, not against the live bank.** `D:\hf_w14\bank_landing_snapshot_BEFORE_W14D`
holds all 42 files as they stood after Stage B and before item 5's arm started, and that snapshot is what the
42-of-42 result is scored against. The snapshot exists because item 5 runs **wave-10 binaries**, which predate
the F15 fix; the live bank is not a stable reference while that arm is executing.

### §1.3 The detector probe, and why this wave stopped trusting `strings`

Wave 13 established `DetectorVersion` 2 with `strings … | grep AtrousWaveletFast`. That probe works, and the
pre-registration reused the *idiom* on a string literal, where it does not. `strings` scans for runs of ASCII
bytes; a .NET assembly stores **type names** in the `#Strings` heap as UTF-8 (findable) and **string literals**
in the `#US` heap as UTF-16 (invisible, every character followed by a null). `AtrousWaveletFast` is a type name.
`--profile-id` is a literal — and `strings … | grep -c -- '--profile-id'` returns **0** against the current
binary, which demonstrably accepts the flag on every arm of waves 11–14. `strings -el` returns **28**.

**The register has been reading two different heaps as one instrument.** The consequence for this wave is
recorded at item 5 (§6); the general one is a new register entry.

---

## §2 — Item 1: `MaxOutlierRejections` code default 1 → 0

**Change:** `AutoFocusOptions.cs:62` `GetValueInt32(nameof(MaxOutlierRejections), 1)` → `0`, and
`AutoFocusOptions.cs:81` (`ResetDefaults()`) `MaxOutlierRejections = 1` → `0`. Profiles that store the key keep
their value. This changes the fallback and nothing else.

### §2.1 It overrides a pre-registration, and that is the honest description

> **Wave 13's RULE M13 pre-registered that the shipped default changes only if D1 dominates. D1 did not fire.**
> 16 of 20 synthetic datasets were ties, the best either value managed was 3 wins, the median displacement was
> 0.00128 step against a 0.10-step materiality floor — and **the pre-registered outcome was NO CHANGE**, which
> is what wave 13 published.
>
> **It ships anyway, as a product-coherence decision by the owner. The rule was not met; it was overridden, and
> the wave says so in the same paragraph as the evidence.**

### §2.2 The support, cited as support — with the F62 audit's corrections applied in place

Four lines were offered. Two of them were weakened by this wave's own item 3, and the weakened form is what is
quoted here.

1. **`MOR` = 1 won on 0 of 4** synthetic datasets where the rejection fires, scored against the generator's own
   truth (wave 13's D1). **Untouched by the F62 audit** — the arbiter is out of sample — and it remains the
   strongest line.
2. **The sensor fit prefers 0 on 9 of 10 like-for-like `sRMS` runs** ([F61](followups.md), wave 13 D3).
   *Not* "10 of 10 `sChi`". The wave-14 audit verified in code that the sensor paraboloid weights each star by
   `1/σ²` where σ is **that star's own hyperbola's `MinimumStdError`** (`SensorModel.cs:838-841` →
   `SensorParaboloidDataPoint.RegularizeStdDev` → `OutputStdDevs`). A fired per-star rejection *lowers* that σ,
   which *raises* the weight, which *raises* the paraboloid's reduced χ² — the observed direction, arithmetically,
   with no statement about the surface fitting worse. `sR2` inherits the same weighting. **`sRMS` is the
   unweighted half** (`NonLinearLeastSquaresSolver.RMSError`) and it is the half that carries the claim.
3. **The harness has measured waves 5–12 at 0**, so the product now matches eight waves of measurement.
4. **At budget 3 the Grubbs cascade takes `caboose` from R² 0.99987 → 0.91475 and reduced χ² 0.000317 → 3.946**
   ([F45](followups.md); reconfirmed in this wave's own `af-fit` control rung). The *headline σ_focus ratios*
   — 12.7× via `bank-verify`, 45.2× via `af-fit` — are **weakened on magnitude** by the F62 audit, because
   σ_focus inflates mechanically as `n − p` shrinks. R² and reduced χ² carry **no `n − p` term** and are quoted
   instead. **The direction survives outright**: F62's bias predicts a rejection makes σ_focus *improve*, and a
   bias cannot manufacture its own opposite.

*(The two `caboose` ratios are two pipelines, not two measurements of one number. Never quote one for the
other, and never quote either as an accuracy factor.)*

### §2.3 The measured reach on this machine, and what it does to users

`snapshot_profiles_w13.py`'s `DEFAULTS` table was updated 1 → 0; its `*` marker means "key absent in the
`.profile` ⇒ this code default applies".

| | at `MOR` = 0 | at `MOR` = 1 |
|---|---|---|
| under the **old** code default (1) | **2** | **7** |
| under the **new** code default (0) | **6** | **3** |

**The parse reproduces [F58](followups.md)'s published 2 / 7 exactly under the old default**, which is a free
reproduction control on the parse itself before either number is quoted.

- **Five of the nine profiles set the key explicitly and do not move**: `astrodet` = 0, `AA1600MM Copy` = 0,
  `Default` = 1, `AA1600MM` = 1, `40mm` = 1.
- **Four have it absent and therefore move**, all of them `Default-*` snapshots.
- `astrodet` — the pinned profile on every arm since wave 5 — **stores 0 explicitly**, which is why RULE G14
  was expected to be untouched and why its PASS is a control rather than a tautology.

> **Every profile with the key absent silently changes what the wizard recommends.**
> [F63](followups.md) measured the optimizer's **landing** moving on **6 of 8** runs between these two values at
> `--max-evals 250`, including the recommended AF step size by up to **17 %** (`CWhiteFocus` 101 → 118) and the
> brightness sensitivity by a **factor of two** on two runs. That is the user-visible consequence of this
> change, it belongs in the PR body, and it is not softened by the fact that `BestJ` is not comparable across
> the two values.

**Also carried by the change, because they pinned the old value on purpose:** `HarnessFitInputsTests`'
code-default assertions and `DEFAULTS` in `D:\hf_w13\snapshot_profiles_w13.py`. Three of those assertions were
verified to **fail** against the pre-change source before being called witnesses (`Failed: 3, Passed: 38` →
`Failed: 0, Passed: 41`). One new test — `TheTwoCodeDefaultSites_ResolveToTheSameBudget_OrAFreshInstallAndAResetAreTwoProducts`
— is deliberately *not* a change-witness (it passed before the change, correctly) and was verified against a
**mutant** with the two sites disagreeing.

---

## §3 — Item 2: F45(b), the σ-tied floor on `RejectionTest`'s MAD scale

An **off-by-default** optional parameter on `MathUtility.RejectionTest` (`double madFloor = 0.0`, plus
`double roundOneFloorFactor = 0.0` for family B), threaded through `AlglibHyperbolicFitting.SelectBestModel` and
exposed **only** on `af-fit` as `--mad-floor` / `--round1-floor`. No `*Options` class changed, no persisted
option, so no `Resources/OptionsDataTemplates.xaml` control is required. The floor is applied **after** the
existing degenerate-scale guards, so `max(scale, f) ≥ scale` and a floor can only ever **suppress** a rejection,
never add or redirect one.

The summary now prints the floor family and value, and the **effective scale actually used per round at full
precision** — so the next wave does not have to recover it from a 4-dp field:

```
MAD floor     : family A (absolute), f = 0.25
Round 1: N=7, ... Grubbs limit=2.020, median(r)=-0.0000, MAD(r)=0.2500
  effective scale (full precision) = 0.25   [floor applied this round = 0.25]
```

Six rungs, 39 runs each, all complete: `A0.00` (the control), `A0.25`, `A0.50`, `A1.00`, `B0.50`, `B1.00`.

### §3.0 RULE F14's clauses, each with its pre-registered threshold

| clause | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **V1** — the control rung | all **39** `af_fit_summary.txt` budget tables reproduce wave 13 **exactly** — winner model, `#rej`, rejected positions, σ_focus, redχ², R², `minPos`, at all four budgets | 39 readable of 39, **0 field differences** across 39 × 4 × 7 | **PASS** |
| **V2** — population by count | **39 of 39** per rung, plus the `Settings: D:\hf_w11\pinned_settings_w11.json` line in every log | 6 rungs × (20 syn + 19 real) = **39 of 39 each**, 0 not-pinned, 0 missing log, 0 missing summary | **PASS** |
| **V3** — Stage A's reproduction | **72 of 72** rounds reproduced at `f = 0`, **0 INCONSISTENT** | 72 parsed, **72 reproduced, 0 MISSED, 0 INCONSISTENT**; 161 INERT / 179 RESOLVED / **2 UNRESOLVABLE_FROM_ARTIFACT** | **PASS on the substance; the automated read is UNEVALUATED, §3.4** |
| **V4** — the prefix lemma | every Stage B production row equals one of that run's **wave-13** printed rows at budget ≤ B. **A row matching none refutes the lemma ⇒ Stage A is RETRACTED IN FULL** | **2 rows match none** (`Panos_attempt01` at `B1.00`, budgets 2 and 3), NaN-safe count. As implemented the scorer reports 4; two are spurious (§3.5) | **FAIL** |
| **C1** — containment | `max ρ ≤ 1.10` over the 13 budget-3 firing runs, `ρ = σ_focus(b3)/σ_focus(b0)`, `Panos` UNEVALUATED by name | A0.00 **45.1768** · A0.25 **1.1304** · A0.50 **1.0000** · A1.00 **1.0000** · B0.50 **45.1768** · B1.00 **45.1768** | passes at **A0.50, A1.00** only |
| **C2** — selectivity | `N_fire(3, f) ≥ 1` **and** `N_fire(1, f) ≥ 1` over 39 runs | see §3.7 | passes at **A0.00, A0.25, A0.50, B0.50, B1.00**; **fails at A1.00** (0/0/0) |
| **C3** — `caboose` at budget 3 | CONTAINS-SELECTIVELY / CONTAINS-BLUNTLY / DOES NOT CONTAIN | **CONTAINS-BLUNTLY** at A0.25, A0.50, A1.00; **DOES NOT CONTAIN** at A0.00, B0.50, B1.00 | as §2.10 predicted, for both families |
| **C4** — out-of-sample guard | veto only, median \|Δe\| ≥ **0.10 step** over 20 synthetic datasets at budgets 1 and 3 | median **0.00000** at every rung and budget; max **0.00993** (A0.50, A1.00) | **no veto anywhere** |
| **C5** — blast radius | reported, not a bar: runs whose **budget-1** row changes, per bank | 0 / 4 / 5 / 7 / 0 / 0 (§3.7) | reported |

### §3.1 V1 — the control rung, and this is what proves `madFloor` inert

```
python3 /mnt/d/hf_w14/score_affit_w14.py --control /mnt/d/hf_w14/affit_A0.00 --w13 /mnt/d/hf_w13
  wave 13 readable: 39 (unreadable 0) | control rung readable: 39 (unreadable 0)
  >>> V1 PASS: all 39 budget tables reproduce wave 13 EXACTLY at f = 0.00.
```

39 runs × 4 budgets × 7 fields, exact equality (with NaN treated as the same absence), against wave 13's frozen
artifacts. **Nothing moved.** This is the only measurement in the wave that could have caught a `madFloor` that
is not inert at its default, and it is the reason the two-binary split (see PROVENANCE) does not leave item 2
uncontrolled.

### §3.2 V2 — population by count, every rung, not just the control

`verify_affit_w14.sh` run on all twelve directories:

| rung | syn | real | not pinned | missing log | missing summary |
|---|---|---|---|---|---|
| `A0.00` | **20 of 20** | **19 of 19** | 0 | 0 | 0 |
| `A0.25` | **20 of 20** | **19 of 19** | 0 | 0 | 0 |
| `A0.50` | **20 of 20** | **19 of 19** | 0 | 0 | 0 |
| `A1.00` | **20 of 20** | **19 of 19** | 0 | 0 | 0 |
| `B0.50` | **20 of 20** | **19 of 19** | 0 | 0 | 0 |
| `B1.00` | **20 of 20** | **19 of 19** | 0 | 0 | 0 |

Every run on the pinned settings path. A short population would have made every number below a non-population
statistic, and the rung would be UNEVALUATED rather than "fewer runs fired".

### §3.3 Stage A's reproduction, and its prediction reproduces byte-for-byte

`stageA_w14.py` re-run from wave 13's artifacts:

```
SELF-TEST PASS: reproduces=True, flips-when-perturbed=True,
                scale-interval-brackets-printed-MAD=True, floor-suppresses-at-f=1.0=True
population: 39 readable + 0 UNREADABLE = 39, expected 39
Grubbs rounds parsed: 72
f = 0 REPRODUCTION GATE: 72 reproduced, 0 MISSED, 0 without a printed verdict
INCONSISTENT rounds (scorer bug, blocks Stage A): 0 []
round-state counts:  INERT 161   RESOLVED 179   UNRESOLVABLE_FROM_ARTIFACT 2
>>> STAGE A VALIDITY GATE: PASS
```

All three output files reproduce the committed prediction **byte-identically** (`predict.tsv`
`c6b3e51f60…`, `rounds.tsv` `9231a9e918…`, `sem_audit.tsv` `7b2adf8fd1…`), and the commit that carries them
(`5d1186b`, 2026-08-09T21:49:34Z) precedes Stage B's first rung (22:38:02Z) by 48 minutes. **The counterfactual
was committed before its measurement existed**, which is the only thing that makes §3.6 a test rather than a
description.

The **2 UNRESOLVABLE rounds** are both `caboose` round 3, at family B `α = 0.50` and `α = 1.00`: floored
`max z / limit` = 1.4157 with an interval that straddles the limit. They are counted, named, and carried as
intervals; they block nothing.

### §3.4 V3's automated read cannot pass, and it said so correctly

`score_affit_w14.py` implements V3 as *"Stage A's own gate, read from its report"* and looks for
`<stagea>/stageA_report.txt`. **`stageA_w14.py` never writes that file** — it writes `rounds.tsv`,
`predict.tsv`, `sem_audit.tsv` and prints its gate to stdout. So V3 reports:

```
V3 Stage A report: **ABSENT -- Stage A UNEVALUATED** (/mnt/d/hf_w14/stageA/stageA_report.txt)
```

**That is the correct behaviour and it is a broken gate.** "Could not look" got its own state and used it — the
discipline worked exactly as designed — but the producer and the consumer were never connected, so V3's PASS
branch is as unreachable as `prov_w14.py`'s (§1.1). V3 is recorded as **PASS on the substance**, established by
re-running the producer here, and **UNEVALUATED as an automated gate**. The two statements are not the same and
the doc does not collapse them.

### §3.5 V4 — FAIL. And what it caught is real, but it is not the thing the clause says it is

V4 is the clause that makes Stage A's cheapness legitimate, and it is the clause that fired.

**As implemented, V4 reports 4 failing rows.** Two of them are an artifact: `Panos_attempt01` at `B0.50`,
budgets 2 and 3, whose rows are **byte-identical** to wave 13's. `Panos`'s budget-2/3 σ_focus is the string
`"NaN"` (the design names this case), and V4 compares rows with plain tuple equality, under which
`NaN != NaN` — so a row can fail to match *itself*. The scorer's own `eq()` helper handles this correctly
(*"two NaNs are 'the same absence'"*) and V4 does not call it. **Re-run NaN-safe, V4 fails on exactly 2 rows.**

| rung | run | budget | wave-13 rows available (nrej) | Stage B's row |
|---|---|---|---|---|
| `B1.00` | `Panos_attempt01` | 2 | b0/b1: `0, (none)` · b2/b3: `2, {38396, 40396}` | **`1, {40396}`** |
| `B1.00` | `Panos_attempt01` | 3 | same | **`1, {40396}`** |

**`{40396}` is not any printed row. V4's condition is met and RULE F14's action follows: Stage A is retracted in
full and Stage B is reported as raw numbers.** That is applied below without argument.

**But the lemma V4 names is not refuted, and the difference matters.** The design states the lemma twice, in two
strengths:

- §2.6, the version it actually proves: a uniform scale floor multiplies every `z` in a round by the same
  constant, so the argmax never moves, so each model's floored rejection set is a **prefix** of its unfloored
  set, and since production removes the **intersection** across the four candidate models,
  `consensus^f_B ⊆ consensus_B`.
- V4, the version it tests: Stage B's row must **equal one of the printed budget rows**.

The second does not follow from the first. Checked directly across **585 (run, rung, budget) triples**:

> **`consensus^f_B ⊆ consensus_B` holds on 585 of 585. Zero violations. The prefix lemma survives.**
> `{40396} ⊆ {38396, 40396}`.

**The mechanism is printed in wave 13's own file, and nobody read that column.** `Panos`'s per-model table:

```
model            | ... | would-reject
Symmetric        | ... | 38396 40396
UnevenBlend      | ... | 38396 40396
TiltedHyperbola  | ... | 40396 38396     <-- a DIFFERENT ORDER
SmoothBlend      | ... | 38396 40396
```

Three models reject `38396` first; `TiltedHyperbola` rejects `40396` first. So at budget 1 the intersection is
**empty** (that is the printed `#rej = 0` row) and at budget 2 it is **both**. Under `α = 1.00` the round-2 floor
suppresses `TiltedHyperbola`'s second round and leaves it `{40396}`, while the other three keep both — and the
intersection becomes `{40396}`, a set that is a subset of the budget-2 consensus but is **not** the budget-1 or
budget-2 consensus.

> **The intersection of prefixes is not a prefix of the intersection when the models disagree on rejection
> ORDER.** That is the finding. §2.3 fact 2 already recorded that "the printed trace is not the production
> trajectory" and that Stage A's verdict must be three-valued because of it — the pre-registration saw the
> mechanism and then wrote a gate that assumed the property it had just disclaimed.

Per the design's own triage (§"WHY STAGE B IS NOT A FORMALITY"), this is cause **(ii) — Stage A's model of the
file is wrong, most likely the consensus mechanism** — and the design says Stage A is *"retracted, not
reconciled, if (i) or (iii)"*, i.e. not for (ii). **V4's mechanical condition retracts it anyway.** Both
statements are in the same document, they disagree, and this wave applies the one written into the clause. *A
pre-registration is allowed to be wrong; it is not allowed to be re-decided after the data.*

### §3.6 Stage A against Stage B, row by row

702 predictions in the committed `predict.tsv`, one per (run, family, rung, budget). Scored against the six
measured rungs:

| Stage A's prediction | n | confirmed | violated | deferred |
|---|---|---|---|---|
| **N/A** (production rejected nothing; a floor cannot add one) | 504 | **504** | 0 | — |
| **SUPPRESSED** (= the printed budget-0 row, labelled EXACT) | 78 | **78** | 0 | — |
| **INDETERMINATE** (bounded to the printed rows) | 116 | **114** | **2** | — |
| **UNRESOLVABLE** (Stage A refused to predict) | 4 | — | — | 4 |
| **total** | **702** | **696** | **2** | **4** |

> **696 of 702 confirmed. All 78 EXACT predictions were exact. The 2 violations are the two `Panos` rows of
> §3.5, and they are the same finding, not a second one.**

**The INDETERMINATE resolutions, and their direction.** Stage A could bound 116 rows but not resolve them,
because only one of the four candidate models' scales is printed. Where they landed, by the rejection count of
the wave-13 row they matched:

| landed on the printed row with | rows |
|---|---|
| `nrej = 0` (fully suppressed) | **6** |
| `nrej = 1` | **87** |
| `nrej = 2` | **18** |
| `nrej = 3` | **3** |

**The 2 UNRESOLVABLE rounds became 4 UNRESOLVABLE budget rows, and all 4 resolved inside the interval Stage A
allowed.** They are `caboose` at `B0.50` and `B1.00`, budgets 2 and 3, and every one landed on **wave 13's own
row** — the floor changed nothing on `caboose` at either family-B rung.

That is §2.5.2's pre-registered prediction, confirmed: *"Family B is pre-registered as UNABLE to contain
`caboose` at either rung."* `caboose`'s scale is already collapsed at round 1 (1.48e-5 against σ of 0.12–0.79),
so a floor anchored to round 1 is anchored to the collapse, and the budget-3 consensus `{7275, 6975}` is formed
in rounds 1 and 2 — both above any `α ≤ 1` anchor. **A family predicted to fail, measured, and confirmed to
fail is how a family stops being carried.**

**And §2.5.3's arithmetic reproduced to the digit.** The pre-registration computed that `caboose` round 1 at
`f = 0.25` gives `z = 0.095` against a limit of 2.020. Measured:

```
Round 1: N=7, Grubbs limit=2.020, MAD(r)=0.2500
  effective scale (full precision) = 0.25   [floor applied this round = 0.25]
  7275   |  4.340 |  0.756 |  4.358  |  0.0180  |    -0.0238 |        0.095  |
  -> RejectionTest: NO outlier above the Grubbs limit. Stable; stopping.
```

against wave 13's `z = 1609.641` for the same point. The rejected point sits **0.0180 HFR from the curve against
its own σ of 0.756 — 2.4 % of its own error bar** — and at `f = 0` it is discarded as an outlier.

**The one round §2.6.1 flagged as decided inside the printed precision resolved exactly.** `D01_ultrawide_40mm`
round 1 has a printed margin of 1.0005 (`max z` 2.1280 against limit 2.1270). Stage B prints its scale at full
precision — **0.9885273505684371** — so at `f = 1.00` the floor bites by 1.15 % and the round falls below the
limit: `D01` fires at `A0.00 / A0.25 / A0.50 / B0.50 / B1.00` and is silent at `A1.00`. *The rounding exposure
Stage A had to carry as an interval is simply absent from Stage B, which is the whole reason Stage B exists.*

### §3.7 The decision clauses — recorded as DIAGNOSTICS, because a validity gate failed

| rung | max ρ (13 firing runs) | C1 (≤ 1.10) | `N_fire@1` | `N_fire@2` | `N_fire@3` | C2 | blast@1 (syn/real) | C3 (`caboose` b3) |
|---|---|---|---|---|---|---|---|---|
| `A0.00` | **45.1768** (`caboose`) | fail | 7 | 13 | 13 | PASS | 0 (0/0) | DOES NOT CONTAIN |
| `A0.25` | **1.1304** (`D12`) | fail | 3 | 5 | 5 | PASS | 4 (1/3) | CONTAINS-BLUNTLY |
| `A0.50` | **1.0000** | **PASS** | 2 | 2 | 2 | PASS | 5 (2/3) | CONTAINS-BLUNTLY |
| `A1.00` | **1.0000** | **PASS** | **0** | **0** | **0** | **fail** | 7 (4/3) | CONTAINS-BLUNTLY |
| `B0.50` | **45.1768** | fail | 7 | 13 | 13 | PASS | 0 (0/0) | DOES NOT CONTAIN |
| `B1.00` | **45.1768** | fail | 7 | 11 | 11 | PASS | 0 (0/0) | DOES NOT CONTAIN |

`Panos_attempt01` is **UNEVALUATED BY NAME** in C1 at `A0.00` and `B0.50` (its budget-3 σ_focus parses as NaN),
never silently dropped. It becomes evaluable at `B1.00` — where the floor removed the very rejection that made
its σ degenerate — at ρ = 139.097 / 123.454 = **1.1267**.

**C1's companions, which the clause requires and which carry no `n − p` term:**

| rung | min R² at budget 3 | max redχ² at budget 3 |
|---|---|---|
| `A0.00` | 0.914749 (`caboose`) | 3.94645 (`caboose`) |
| `A0.25` | 0.992306 | 1.65831 |
| `A0.50` | 0.969489 | 1.65831 |
| `A1.00` | 0.907044 | 3.10602 |
| `B0.50` | 0.914749 (`caboose`) | 3.94645 (`caboose`) |
| `B1.00` | 0.914749 (`caboose`) | 3.94645 (`caboose`) |

> **The R² minimum getting *worse* as `f` rises is not the floor damaging a fit — a floor can only suppress.**
> At `A1.00` nothing is rejected anywhere, so every budget-3 row **is** that run's un-pruned budget-0 row: the
> minimum is `D01_ultrawide_40mm` at **R² 0.907044, `#rej` = 0**, which is exactly its own wave-13 *budget-0*
> R², and `max redχ² = 3.10602` is the same run's un-pruned reduced χ². At `A0.50` the minimum is `lumos` at
> **0.969489, `#rej` = 0** — again its own budget-0 value. *A monotone intervention whose aggregate summary
> moves both ways is a summary statistic changing population, not an effect.*

**C3 in detail.** `caboose` at budget 3 is `Symmetric / σ 16.4599 / R² 0.91475 / minPos 7112.74` at `A0.00`,
`B0.50` and `B1.00`; and `TiltedHyperbola / σ 0.3643 / R² 0.99999 / minPos 7101.58` at all three family-A rungs
≥ 0.25. The fix criterion is met at those three — **but budget 2's benign rejection of 7275 (σ 0.364 → 0.172,
R² → 0.999999) is suppressed with it**, so the verdict is CONTAINS-BLUNTLY and never CONTAINS-SELECTIVELY.
**§2.10 predicted exactly this, for both families, before the measurement:** *"The maximum attainable is
CONTAINS-BLUNTLY for family A and DOES NOT CONTAIN for family B."*

**C4 in detail.** Median `|Δe|` is **0.00000 step at every rung and both budgets**; the maximum anywhere is
**0.00993 step** (`A0.50` and `A1.00`, budgets 1 and 3), which is **exactly** the reachable maximum §2.10
derived from wave 13's tables before the arms ran (`D12_c14_585_afbin2`, 0.00142 → 0.01135). Against a 0.10-step
veto, nothing vetoes, and nothing could have: **the synthetic bank's out-of-sample arbiter cannot decide
F45(b)**, which the design said in advance and demoted it to a veto and to V4's falsifier rather than
re-weighting it until it could.

**C5 in detail.** Family B changes **0 budget-1 rows at both rungs** — by construction, since `α ≤ 1` leaves
round 1 bit-identical and the budget-1 consensus is formed in round 1 alone. Family A's blast radius at budget 1
grows 0 → 4 → 5 → 7 runs. At budget 3 (reported, not a clause) it is 0 / 8 / 11 / 13 for family A and 0 / 5 for
family B. **`A1.00` changes the budget-3 row of all 13 firing runs**, which is the same fact as `N_fire = 0`
seen from the other side.

### §3.8 THE VERDICT

> ## **RULE F14: NO VERDICT. The failing gate is V4.**
>
> The rule's own words: *"any validity gate fails ⇒ NO VERDICT, and the wave says which gate failed"*, and
> V4's: *"a row that matches none of them refutes the lemma, and if that happens Stage A is **retracted in
> full** and Stage B is reported as raw numbers with no counterfactual framework."*
>
> **Stage A is retracted. §3.6's 696-of-702 agreement is reported as an observation about two computations, not
> as a counterfactual result. §3.7 is raw numbers.**
>
> **No rung is named, and none will be.** RULE F14 closes with *"never resolved by preferring whichever rung
> agrees with something else"*, and the diagnostics in §3.7 are exactly the sort of thing it would be
> comfortable to resolve it with. They are recorded so a wave 15 that repairs V4 can score them under a gate
> that works; they are not a recommendation, and nothing about F45(b) ships.

For completeness, the three branches that did **not** fire, and why, so nobody re-derives them:

- **"the floor does not contain the cascade"** requires C1 or C3 to fail *at every rung of every family*. C1
  passes at `A0.50` and `A1.00`; C3 is CONTAINS-BLUNTLY at three rungs. Not this.
- **"the floor is a disguised off-switch"** requires *every* rung satisfying C1 and C3 to fail C2. `A0.50`
  satisfies C1, C3 **and** C2 (`N_fire` 2/2/2). Not this. **The window is one rung wide**, which is the honest
  description of the ladder's result and is not a verdict.
- **"RECOMMEND IN SEM UNITS"** requires C1/C3 to hold *only* at rungs failing C2. `A0.50` breaks the antecedent.
  **So the wave's strongest measurement — §3.9 — is not reachable by any branch of the rule that was written to
  consume it.** That is a defect in the pre-registration and is recorded as one.

### §3.9 Stage A(2) — the SEM audit, which is the wave's headline and cost nothing

§2.4's source read is the whole of this. `AlglibHyperbolicFitting.cs` documents its own σ:

> `ChiSquared`: *"per-point σ is the star-ensemble scatter (1.483·MAD), which **overstates the uncertainty of
> the plotted median HFR by roughly √(detected stars)**"*; `ReducedChiSquared`: *"runs ≪ 1 for star-rich fields
> (≈ 1/N\* scaling)"*.

So `r = residual/σ` is smaller than a unit-normal residual by ≈√N\* **by construction**, and F45(b)'s phrase
*"sitting well inside its own error bar"* names an error bar ≈√N\* too wide for the quantity being fitted. The
correction needs no code: `af_fit_points.csv` prints `Stars` per focuser position, the round tables print `r`
per focuser position, and **`s = |r| · √N\*` is the rejected point's residual in units of the standard error of
the plotted median**. 42 rejections across the 39 wave-13 runs, `COULD_NOT_LOOK` on 0.

The pre-registered separation clause, verified from `sem_audit.tsv`:

| half | threshold | measured |
|---|---|---|
| every rejection on a run with **ρ > 2** has `s < 1` | 100 % of them | **3 of 3** — `caboose` only, ρ = 45.18: `s` = **0.1010, 0.0403, 0.0000** |
| at least **half** the rejections on runs with **ρ ≤ 1.10** have `s ≥ 1` | ≥ 50 % | **32 of 33 = 97.0 %** |

The single exception is `D08_c11_2800mm`'s third rejection at `s = 0.3883`. The population spread over all 42 is
min **0.0000**, median **3.71335**, max **79.7824**.

> **The separation is real and it is large.** The rejections on the one run whose fit catastrophically degrades
> sit at a *tenth* of a standard error of the quantity being fitted; the rejections on runs whose fit is
> untroubled sit at three to eighty. **No floor in raw `r` units can tell those apart** — `caboose` round 1's
> scale is 1.48e-5 and `D01`'s is 0.9885, five orders of magnitude apart on the same ladder — **and a floor in
> SEM units can, with one threshold.** That is the finding: F45(b) is right about the mechanism and the
> register's own wording is wrong about the units.

**Carry the honest half in the same breath.** `D16_esprit550_ha3` — F45's headline reproduction, where the
discarded point at position 8000 **is the generator's true focus** and the vertex moves *away* from it — sits at
`r` = +1.3307, N\* = 43, **`s` = 8.7260**. A SEM-unit floor at any threshold near 1 leaves that rejection
untouched.

> **So the SEM criterion does NOT vindicate F45's original complaint.** It separates the *cascade* case from the
> benign ones; it does not save the in-focus point in the case F45 was opened for. A point can be a genuine
> large deviation in its own error bar and still be the one you must keep, and no scale floor of any kind knows
> which.

**Three caveats, all structural.** (1) **ρ is a containment measure, not an accuracy factor** — the F62 audit
established that σ_focus inflates mechanically as `n − p` shrinks (`s² = weighted RSS/(n−p)` and `(JᵀWJ)⁻¹` both
grow, and `caboose` at budget 3 is 5 points against 4 parameters), so **45.18 must never be quoted as "45× less
accurate"**; it is legitimate here only because `ρ = 1` exactly when nothing is removed. (2) The clause's two
buckets do **not** partition the population: 6 of the 42 rejections fall in neither — `D12` (ρ = 1.1304),
`D17` ×2 (ρ = 1.1193) sit in the 1.10 < ρ ≤ 2 gap, and `Panos` ×3 have ρ = NaN. (3) **`ρ > 2` is `caboose` and
nothing else.** This is an n = 1 clause on the high side, as §2.10 said before the data. What makes it worth
reporting is not a win count but a five-order-of-magnitude scale separation and a 0.10-vs-8.73 SEM separation
that do not depend on sample size the way a sign test does.

**Price of acting on it, unchanged from the design: ~2 h.** The SEM-unit floor needs the star count inside
`RejectionTest`, and `ScatterErrorPoint` carries `(X, Y, ErrorX, ErrorY)` and no star count, so `N*` must be
threaded from `MeasurePoint` construction through the fit, plus tests.

### §3.10 What item 2 cannot say, and it was said in advance

- **It cannot select `f`.** The ladder spans 4×; √N\* spans 10× across this bank (`caboose` N\* = 9–65 ⇒ √N\*
  3–8; `toml999` N\* = 217–1905 ⇒ √N\* 15–44). The ladder measures behaviour at fixed `f`; it cannot choose one.
- **It cannot decide for a rig worse than anything in the bank.** 20 well-formed synthetic sweeps and 19 real
  runs; a rig producing one wild point per sweep is where a rejection budget earns its keep and no frame in
  either bank is that rig.
- **It cannot separate the floor from `OutlierRejectionConfidence`**, pinned at 0.95 on every rung — which fires
  *more* readily than F45's own field case at 0.99.
- **It says nothing about the default user after item 1.** With `MaxOutlierRejections` defaulting to 0 the
  rejection does not run at all, so a floor reaches only profiles that explicitly store ≥ 1, and any future
  decision to raise the budget. **Item 1 makes item 2's ship value contingent.**

---

## §4 — Item 3: the F62 audit

Full result: [`docs/wave14-f62-audit.md`](wave14-f62-audit.md). Criteria were fixed at spawn by the controller
and are restated verbatim in that file's §1; this section does not re-decide them.

**12 hits: 2 INVALIDATED, 3 WEAKENED, 7 SURVIVES, 2 UNEVALUATED.** Register entries touched: F18, F20, F25,
F32, F35, F45, F48, F55, F57, F58, F61, F63.

The two that were live this wave were folded into the pre-registration **before** the arms ran, and appear above
in their corrected form: [F61](followups.md)'s `sChi` (§2.2, bullet 2) and [F45](followups.md)'s `caboose`
magnitude (§2.2, bullet 4, and C1's containment framing throughout §3).

Two of the audit's three findings about **F62 itself** are structural and are carried into the register here:

- **F62's DETECTOR carve-out is falsifiable.** F62 exempts detector changes because *"the point set is then
  common to both arms"*. That is true of the **stars** and not guaranteed of the **positions**: `JRun` applies a
  hard `NHard` floor per frame, so a detector knob that starves one frame removes a focuser position from the
  fit outright. F20/F35 document it for `MinHFR` (*"5 stars back on the vertex frame … clears the `NHard` = 3
  requirement"*) and wave 8 records `D17`'s `BaselineJ` going **0.000000 → 0.994830** under a *binning* change.
  **The test is not "was it the detector"; it is "did a frame or a star enter or leave the fit".**
- **"It must improve" is not a theorem, and F62's own table shows it.** `D12_c14_585_afbin2` is **+13.0 %**
  worse at budget 1. `s² = weighted RSS/(n−p)` loses degrees of freedom as points drop, and `(JᵀWJ)⁻¹` grows, so
  the bias is strong but not absolute. **The corollary is asymmetric and is worth more than the correction: a
  within-fit statistic that DEGRADES under a rejection is evidence; one that IMPROVES is not.** That is exactly
  why F45's `caboose` result is readable and F58's *"allowing one Grubbs rejection improves a fit"* is not.

---

## §5 — Item 4: the nine UI changes A1–A9

`query session` at wave start: session 1 (`ghili`) is **`Disc`**, console is `Conn`. Per wave 13 §3 that means
no composited desktop, so a WPF client area cannot be captured by any of the three routes wave 13 tried.
**Not attemptable. Fifth wave carrying it. Price unchanged: ~30 minutes on a connected session**, with wave 13's
procedure unchanged (its expensive step, comparing the deployed DLL's sha256 to the freshly built one *before*
launch, already passes).

---

## §6 — Item 5: F57(d) and RULE W14-D — **PLACEHOLDER, NO RESULT**

**This arm was still running when this document was written, and nothing about its outcome is stated here.**

What is fixed, and was fixed before any wave-14 data existed (design §item 5, AMENDMENT): two arms × 8 gate
runs, on `D:\hf_w10\exe` and `D:\hf_w10\exe_v1wav`, both `--settings D:\hf_w11\pinned_settings_w11.json` and
`--profile-id ce3f3e63-…`. Wave 10's stored landings stay VOID ([F57](followups.md) voided RULE G10-B) and are
not read; **both halves are re-run**, so the comparison is internally generated and is made **arm-to-arm, never
against wave 11's table**. Priced at ~84 m. RULE W14-D's three outcomes — 8 of 8 bit-identical ⇒ ANSWERED; any
run differs ⇒ ANSWERED-POSITIVE with the magnitude per run; an arm fails to run ⇒ NOT CONSTRUCTIBLE recorded as
a measured fact — are in the design and are not restated as a prediction here.

**The reason the arm exists at all is §1.3's probe defect.** The pre-registration argued `exe_v1wav` could not
be pinned, on `strings … | grep -c -- '--profile-id'` returning 0. The controller ran the same command against
the current binary, which demonstrably accepts the flag on every arm of waves 11–14, and got **0** there too.
`strings -el` returns **28** on all three binaries. **A test that reports "absent" for a binary known to have
the flag is not measuring presence**, the amendment retracted the reason with the old text struck rather than
deleted, and the item flipped from CLOSE-AS-NOT-CONSTRUCTIBLE to RUN. *An amendment that makes a wave do more
work, dated, with the refuted reason still visible, is not a moved goalpost — but it is only distinguishable
from one because those two properties hold.*

---

## §7 — The suite, the budget, and what was not run

### §7.1 The suite

| point | result |
|---|---|
| wave-13 baseline on this branch | 3755 |
| after item 1 alone | **3756** passed, 0 failed, 0 skipped (+1: the two-code-default-sites invariant test) |
| after item 2's Stage B code | **3781** passed, 0 failed, 0 skipped (+25: `MadFloorRejectionTests`, `MadFloorFitPlumbingTests`, `MadFloorArgsTests`) |

Verified by **COUNT**, not by tick ([F37](followups.md)); nothing piped to `tail`, which would mask the exit
code. Item 1's three changed assertions were verified to **fail** against the pre-change source; the Stage B
change was verified against two mutants (`Math.Max → Math.Min` ⇒ 8 failures including the prefix-lemma test on
all three shapes; floor moved above the degenerate-scale guards ⇒ exactly 2 failures, and exactly the two
claimed).

**One test had to change for a reason worth keeping.** `ToString_ChangesWhenANYONEInputChanges` perturbed the
budget with the literal `"0"`, which discriminated only because the code default happened to be 1. When the
default became 0 the perturbation silently became the baseline and the test failed. It now **derives** the
perturbation from the code default (`default + 1`) so it cannot coincide again. *It failed loudly here; the same
coincidence in a test asserting equality would have passed and proved nothing.*

### §7.2 The budget, against the estimate

| arm | estimated | actual |
|---|---|---|
| RULE G14 — the gate, 8 runs sequential | ~42 m | **41 m 48 s** |
| Stage A(1) + A(2) — scorers over wave 13's artifacts | < 1 m | **< 1 m**, no `TestApp` |
| Stage B — family A, `f = 0.00` (**the control**, V1) | ~11 m | **11 m 15 s** |
| Stage B — family A, `f = 0.25` | ~11 m | **11 m 06 s** |
| Stage B — family A, `f = 0.50` | ~11 m | **11 m 00 s** |
| Stage B — family A, `f = 1.00` | ~11 m | **10 m 53 s** |
| Stage B — family B, `α = 0.50` | ~11 m | **10 m 55 s** |
| Stage B — family B, `α = 1.00` | ~11 m | **10 m 53 s** |
| **RULE W14-D** — F57(d), 8 runs × 2 wave-10 binaries | ~84 m | *(§6, running)* |
| **Stage B total** | **~66 m** | **66 m 44 s** wall (22:38:02Z → 23:44:46Z) |

> **Six rungs in 66 minutes against a 66-minute estimate.** Wave 13 over-priced its `af-fit` arm by 16× and
> learned why: the instrument detects each frame **once** and then evaluates four budgets on those points. This
> wave priced a six-rung ladder from that measurement and landed within 1 %. *An estimate derived from a
> measured mechanism is a different object from an estimate derived from a similar-looking tool.*
>
> Family C was dropped **before measurement**, refuted on paper against the wave's named target for a cost of
> **zero minutes** (§2.5.1): `caboose` round 1's `stdev(r)` ≈ 0.0100 leaves `z` = 2.4 even at `β = 1.00`, still
> over the limit of 2.020. Its scale is small because its residuals are genuinely tiny relative to σ, not
> because the MAD broke down. *A family refuted on paper against the named target is worth more than the same
> family measured for 22 minutes.*

### §7.3 What was NOT run, and what it would cost

- **The SEM-unit floor** (`z` computed on `r · √N*`). **~2 h**: thread `N*` from `MeasurePoint` construction
  through the fit to `RejectionTest`, plus tests. §3.9 is the measurement that makes it the named next step, and
  it was deliberately not attempted in the same wave that measures whether a floor is wanted at all.
- **A repaired V4 and a re-scored RULE F14.** The clause needs to test the property the design proved
  (`consensus^f_B ⊆ consensus_B`, plus a cardinality bound) rather than equality to a printed row, and V4's NaN
  comparison needs the scorer's own `eq()`. **Cost: minutes of Python and zero `TestApp` time** — all six rungs
  are on disk. This is the cheapest unfinished thing in the wave.
- **F63(a) — extending D5's "6 of 8 landings move" to the 39-run population.** ~3 h sequentially (~2 ¼ h fanned
  out). Still the most interesting thing wave 13 left undone, and after item 1 it changes shape: it would now
  measure what the **new** default recommends against what the old one did.
- **F59's five knobs** stay at code defaults. Re-pinning moves the coordinate system RULE G14 has now reproduced
  a fifth time: ~42 m for a new gate baseline plus the re-derivation of every cross-wave comparison. A decision,
  not a cleanup.
- **F63(b) — pinning the SHIPPED default rather than `astrodet`'s.** After item 1 the shipped default **is**
  `astrodet`'s value, so this deferral is cheaper than it was last wave and should be re-priced next wave.
- **Item 4's nine UI changes**: ~30 m on a **connected** session (§5).
- **No out-of-sample vertex check at budget 3 on the real bank**, because none exists there. C4 covers budgets 1
  and 3 on the synthetic bank and cannot return "material" in either direction.
- **F61(b), F52(d), F46(b), F54, F50**: nothing depends on them.

---

## Lessons

**1. A gate that has never been shown to pass is not a gate.** Three of this wave's checks could not return
their own success: `prov_w14.py`'s profile assertion compares `astrodet (ce3f3e63-…)` against a bare GUID and
therefore fails on every landing that could exist (§1.1); `score_affit_w14.py`'s V3 reads a report file its own
producer never writes (§3.4); V4 compares rows containing `NaN` with plain equality, so a row can fail to match
itself (§3.5). All three failed **closed**, which is the right direction and is exactly what wave 13's
"could not look" discipline bought. **It is still three broken instruments in one wave**, and the shape is the
same every time: the check was exercised against the state it was meant to detect and never against the state it
was meant to accept. *Wave 13's scorer was smoke-run and that found two defects; smoke-running is necessary and
it only demonstrates one branch.*

**2. Prove the lemma you are going to test, or test the lemma you proved.** The design proved
`consensus^f_B ⊆ consensus_B` from a per-model prefix argument, and then wrote V4 to test the strictly stronger
`consensus^f_B ∈ {printed rows}`. On 585 of 585 triples the proved statement holds; on 2 rows the tested one
does not, because **the intersection of prefixes is not a prefix of the intersection when the four candidate
models disagree on rejection ORDER**. `Panos`'s per-model `would-reject` column has said `40396 38396` where the
others say `38396 40396` since wave 13, in a file the pre-registration read for three other purposes. *The gap
between the theorem and the assertion was one line of prose, and it cost the wave its verdict.*

**3. Apply the rule as written, and put the disagreement in the results doc instead.** RULE F14 says a V4 miss
retracts Stage A in full; the same design's triage says Stage A is retracted only for causes (i) and (iii), and
this is cause (ii). Both sentences are pre-registered and they disagree. **The wave returns NO VERDICT** — the
clause is the clause — and records the contradiction as a finding. *A pre-registration is allowed to be wrong.
The moment it may be re-decided after the data, it stops being one, and every earlier wave's verdict becomes
retroactively negotiable.*

**4. The wave's best measurement was not reachable by any branch of its own rule.** §3.9's SEM separation —
3 of 3 pathological rejections at `s < 1` against 32 of 33 substantive ones at `s ≥ 1` — is the strongest thing
here, it cost nothing, and RULE F14 can only report it through a branch gated on *"C1/C3 hold only at rungs that
fail C2"*, which one rung falsified. **A pre-registration should ask what happens to each measurement under
every branch, not only whether each branch is reachable.** §2.10 checked the second question carefully and never
asked the first.

**5. Score the change on something the change cannot move — and check the summary statistic is measuring one
population.** C1's `min R² at budget 3` gets *worse* as the floor gets stronger, on a monotone intervention that
can only suppress rejections. Nothing degraded: at `f = 1.00` every budget-3 row **is** the un-pruned budget-0
row, so the statistic changed which fits it was summarising. *An aggregate over "the runs that fired" is not a
fixed population when the treatment decides who fires.*

**6. Two probes, two heaps, one instrument in the register's head.** `strings` finds .NET **type names**
(`#Strings`, UTF-8) and not **string literals** (`#US`, UTF-16). Wave 13's `AtrousWaveletFast` provenance probe
works *because* it is a type name; the wave-14 pre-registration used the same idiom on `--profile-id` and
concluded a binary lacked a flag it has — and the control that would have caught it, running the probe against a
binary known to accept the flag, took one command. **An item survived four waves of deferral on that inference.**
*When a probe reports absence, run it against a known positive before you believe it.*

**7. The first arm in thirteen waves that did not mutate the bank.** 42 of 42 landings byte-identical after the
gate and all six Stage B rungs (§1.2). F15 was fixed in wave 13 with unit tests; this is the first time it was
demonstrated in an arm, and it needed a fingerprint taken **before** anything ran. *A fix confirmed only by the
tests written with it is confirmed by its author.*
