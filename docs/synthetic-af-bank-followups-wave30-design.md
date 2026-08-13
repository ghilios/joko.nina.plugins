# Wave 30 — backlog item 2: which gate actually binds on `D01`, measured as a FLOW

**Pre-registration. Written before any wave-30 statistic is computed.** §2 records where the controller's
framing is wrong; there are seven such places and **four of them change what the wave does**. Two of them
delete work the controller asked for, because it is already done.

---

## 0. THE VESSEL — decided in writing, before any measurement

**Wave 30 opens a fresh branch `ghilios/synthetic-af-bank-followups-wave30` off
`ghilios/synthetic-af-bank-followups-wave29` at `59e27ab`, and opens its own PR with
`--base ghilios/synthetic-af-bank-followups-wave29`.**

The base matters more than the branch, and this is a finding rather than a preference.

**Measured at pre-registration time, `gh pr view`:**

| PR | head | **base** | state |
|---|---|---|---|
| #196 | `ghilios/synthetic-af-bank-followups-wave27` | **`develop`** | OPEN |
| #197 | `ghilios/synthetic-af-bank-followups-wave29` | **`develop`** | OPEN |

**#197's base is `develop`, not #196's branch.** Wave 29's results §0 describes it as *"stacked on #196"*; in
git it is not stacked at all. `gh pr view 197 --json files` returns **25 files**, and they include
`TruthDisclosure.cs`, `GoldenEvalRunner.cs`, `DetectionBinningResolver.cs`, wave 27's design and results, wave
28's design and results, and wave 29's — **#197's own diff carries waves 27, 28 and 29, plus wave 27's C# ship.**

Wave 29's design §0 opened a fresh branch specifically so that *"#196 does not carry a third section"*, and it
succeeded at that. **It did not stop the accretion; it moved it.** The mechanism that stops accretion is the
PR **base**, and nobody set it. A fourth wave committed anywhere in this family with base `develop` produces a
28-file diff spanning four waves.

**So wave 30 sets `--base ghilios/synthetic-af-bank-followups-wave29`**, which makes its PR's diff **wave 30
only**. That is the first PR in this family whose diff is one wave.

**Not done, and named rather than done quietly:** re-targeting #197's base to `ghilios/…wave27` (and #196's to
`develop`) would retroactively give both PRs one-wave diffs. It is a ~2 m `gh pr edit --base` on two PRs the
owner may already be reading, and **wave 30 does not edit another wave's open PR.** It is recommended in §16
and left to the owner.

**PR #197's title has already been corrected** — it now reads *"Wave 29: F82 IS a product defect (its own rule
refuted the pre-registration), and the step-size truth is a fixed point of the recommender"*. Wave 29's results
§0 listed the correction as owed before merge; it is **paid**, verified by `gh pr view 197 --json title`, and
wave 30 does not re-pay it.

**Wave 30 changes no C# and builds no binary** (§10), so the vessel is not load-bearing for any verdict. It is
load-bearing for whether this family's PRs can ever be reviewed one wave at a time.

---

## 1. THE PRODUCT QUESTION, in one sentence

> **On `D01_ultrawide_40mm` — the bank's most under-sampled rig, where `MinStarBoundingBoxSize` landed at 6
> against a shipped default of 5 — does opening the `TooDistorted` gate *in the correct direction*, alone and
> jointly with `LowSensitivity`, deliver a material share of the 50 249 high-tier stars the detector misses, or
> is everything those gates release re-captured downstream, so that no landed knob is pinned against the
> physics in any recoverable way?**

That is a **goal 3** question — *"appropriate exposure adjustments, no parameters pinned to extremes"* — and it
is the question wave 29 §13 named as one of the last two this bank can answer without new data. It is not a
recall question; §2.7 fences that.

The wave has a product question. It does not recommend stopping. §17 says what would.

---

## 2. WHERE THE CONTROLLER'S FRAMING IS WRONG

### 2.1 `P-D08` at `n = 3` is NOT owed. It was paid at 09:06Z, seven hours before wave 29 started

The controller's item 1 lists *"`P-D08` at `n = 3`"* as one of wave 29's four named debts, citing wave 29's
§8 (iii) — *"no artifact under the root"* — and §11's *"Owed by charter §4."*

**It is done, it is published, and it was published before wave 29 opened its branch.**

- `docs/synthetic-af-bank-followups-w26-pd08-followon.md` carries a section headed
  **`# RESULT — P-D08 is REFUTED at n = 3`**, measured *"2026-08-13T09:06-09:08Z on B15"*, with both blind
  sub-boundary landings (`D01_ultrawide_40mm` gate 0.2234, `D16_esprit550_ha3` gate 0.1969) at **base precision
  1.000, `FP = 0`**, i.e. the confirming branch's requirement failed on both.
- `git log -- docs/synthetic-af-bank-followups-w26-pd08-followon.md` dates the result commit `e42e2df` at
  **2026-08-13 05:09:37 -0400 = 09:09:37Z**, after the pre-registration commit `c4fc0c2` at 09:06:02Z.
- The artifacts are on disk: `/mnt/d/hf_w26/pd08/` holds `D01_ultrawide_40mm__{base,sens}` and
  `D16_esprit550_ha3__{base,sens}`, four cells with their logs, plus `F31_truth_rescore.txt`.

Wave 29's §8 (iii) is literally true — there is no `P-D08` artifact **under `/mnt/d/hf_w29/`** — and the
inference drawn from it is false. **The debt was carried forward on a search of the wrong directory.**

This is [F85](followups.md)'s class recurring — *a closed, shipped correction re-measured as an open defect* —
and this time the gap between paying and re-listing is **under twelve hours, inside a single day's run.**

**Consequence for wave 30:** re-running it would spend two minutes of `TestApp` producing a second copy of a
published refutation. Wave 30 does not re-run it. It does something better and cheaper: §5 makes wave 29's
whole "not run" ledger into **`RULE W30-D`**, a rule that checks every row rather than inheriting it.

### 2.2 The closing `V29` is paid, and it is a genuine closing run — verified, not taken on trust

The controller says so and it checks out with a stronger claim than the controller made.
`/mnt/d/hf_w29/vdrift_w29_CLOSING.txt` has mtime **`2026-08-13 14:18:01.915 -0400 = 18:18:01Z`**, which is
**after** `fp_AFTER.txt` closed at 18:04:27Z and after the cross-wave check at 18:05:04Z. It is 1 189 bytes,
identical in structure to `vdrift_w29.txt`, and ends `>>> V-CLEAN`. **It is a run at the close, not a copy of
the run at the open**, and [F95](followups.md)'s remedy is now demonstrated in both halves. Wave 30 inherits
the obligation, not the debt.

### 2.3 "`MaxDistortion` LOWERED (0.5 → 0.3, 0.2)" — the direction is right and the DESIGN is wrong

The direction is correct and §4.1 verifies it in source rather than quoting [F98](followups.md).

But **two doses of one knob cannot test a joint hypothesis.** [F99](followups.md)'s claim — the one this wave
exists to test — is *"`D01`'s binding constraint is `TooDistorted`, **jointly with** `LowSensitivity`, and no
single-knob change recovers it."* A `{0.5, 0.3, 0.2}` ladder on `MaxDistortion` measures a dose-response on one
axis and says **nothing** about a joint. Wave 29 already made the mirror-image error: it ran three single-knob
cells and one all-three cell and could not decompose them, and its own §2.4 had argued that a one-at-a-time
design *"ranks and lower-bounds, and cannot attribute."*

**Wave 30 runs a 2×2 factorial on the two gates F99 names**, which is the smallest design that yields an
interaction term:

| | `Sensitivity` landed | `Sensitivity` opened |
|---|---|---|
| `MaxDistortion` landed | **`M0`** | **`M2`** |
| `MaxDistortion` opened | **`M1`** | **`M3`** |

Four cells, exactly the budget the controller allowed. The second `MaxDistortion` dose survives as **`M4`**,
**fenced out of every verdict branch** and first on the cut list (§13), so cutting it changes no verdict. Full
cell table in §4.3.

### 2.4 "one notch" of `Sensitivity` is too small to be in a joint test at all

Wave 29 relaxed `BrightnessSensitivity` 36.333 → 35.333 — one axis step read from
`OptimizerVariable.cs:168` — and recorded the result as **2.75 %**, *"which is why `exclusive(Sensitivity)` is
only 101 and is weak evidence for anything."*

Putting that same 2.75 % into a joint cell guarantees a null on the joint arm by construction, and a
pre-registered null by construction is not a test. **Wave 30 opens `Sensitivity` by four axis steps**
(36.333 → 32.333, **11.0 %**), computed at run time as `landed − 4 × step` with `step` read from the product's
own axis declaration, **never typed**. Wave 29's one-notch cell `L3` is imported unchanged as the low dose, so
the wave gets a two-point dose curve on that axis for free.

**[F84](followups.md) / `RULE S16` is re-affirmed, not touched.** The `Sensitivity` axis here is a **diagnostic
dial** and nothing more. §4.5 fences it in the scorer: no verdict branch may name `Sensitivity` as a knob to
change, and no register entry this wave produces may cite `M2`, `M3` or `L3` as evidence for or against a
sensitivity bound.

### 2.5 "Item 5: clause populations and `None`" is not a fifth item. It is how this wave's own rules are built

The controller lists [F102](followups.md)'s populations and [F94](followups.md)'s `None` enumeration as a
separate deliverable worth ~25 m. **Built as a separate deliverable it is exactly the failure it is trying to
fix** — a checker improved in a wave whose own rules do not use it, which is how `V28-C` shipped with an empty
population for three waves.

**Wave 30 does not build it beside the rules. It builds the rules out of it.** Every clause in `W30-G`, `W30-D`
and `V30` prints `candidates: N   findings: M` and **refuses** on an unexpected `N = 0`; every tree enumerates
over the clause's **actual** value domain including `None`; and every scorer asserts **its own realized clause
tuple is a member of the enumerated set** and prints that tuple. §11 and §12. The marginal cost is ~20 m of
instrument writing, not a 25 m item, and the remedy is exercised by the wave that writes it.

**And `None` is not decorative here.** `RULE W29-L` died on a ratio with a negative denominator and landed at
`(True, None, None)` while printing `uncovered: 0`. **`W30-G`'s `G-3` is a ratio with a denominator that can be
non-positive, and §11 enumerates its `None` region and names the measured condition that reaches it.**

### 2.6 The 42 GB root set is not what wave 30 reads. It is 2.4 GB, and every entry is justified

The controller is right that the root list must be trimmed and each entry justified. The trim is larger than
"trim": **wave 30 reads one dataset, and `/mnt/d/hf_w25/after` — the 17 GB root — it does not read at all.**

The results-table `K` column needs `truthModel.max(hfrMin, hfrMinEffective)`. **`truthModel` is not in
`synth_validate_report.json`.** It is in `synthetic_meta.json`, per `SynthBankSpec.cs:263` (*"the dataset's
geometric truth model — `synthetic_meta.json`'s `truthModel` block"*), and a key-name-only probe confirms
`truthModel` → `['hfrMin', 'hfrMinEffective', 'kappa', 'arcsecPerPixel', 'captureBinning', 'readNoiseNote',
'perFrame']` in `/mnt/d/SyntheticAutofocusBank/<dataset>/synthetic_meta.json`, **20 of 20 present**. So the `K`
column reads 20 small JSON files under the bank, not 17 GB of sweeps under `hf_w25/after`.

Full root table with the justification per entry, and the four roots dropped with reasons, in §6. Measured
consequence: **~2.4 GB, ~40 s per sweep at wave 29's measured 61 MB/s, priced at 3 m per side** against wave
29's measured 11 m 42 s and its ~24 m two-sided owing.

### 2.7 "[F22](followups.md) bounds the payoff" — agreed, and it is a hard constraint on what §4 may conclude

`D01` reaches `BestJ` **0.994825**; below ~1.1 px measured HFR the detector's HFR over-reads by up to
**+234 %**; its 0.231 px optical vertex measures 0.77 px. **Working autofocus and terrible recall coexist on
this rig, and recovering `D01`'s recall cannot be justified as a focus improvement.**

**Fixed here, before the data:** the results section is **forbidden** from quoting a recall gain as a product
benefit. What survives F22 untouched is goal 3 — a knob landed at an extreme against the physics — and that is
the only frame in which `W30-G`'s verdict may be read. The scorer prints the F22 caveat beside every recall
number it emits, so a reader quoting the number out of context has to delete the caveat to do it.

---

## 3. THE ITEMS, AND WHAT LOSES

**Taken: item 2 whole (`RULE W30-G`), item 5 folded into the instruments (§2.5), and item 1 re-scoped to what
is actually owed (`RULE W30-D` + two real debts).**

| candidate | verdict | why |
|---|---|---|
| **2 — `D01` localised** | **TAKEN, whole, and re-designed to a 2×2 factorial** | The wave's product question (§1). The direction is now verified in source (§4.1), the statistic is the per-gate flow with an exact conservation identity (§4.2), and the hypothesis under test is F99's and can be refuted (§4.6) |
| **5 — populations + `None`** | **TAKEN, folded** | Not a separate item (§2.5). Built into `W30-G`, `W30-D` and `V30` |
| **1 — the debts** | **TAKEN, re-scoped, and now a RULE** | Two of the five are already paid (§2.1, §2.2, §0). What remains is the three scorer self-test captures and the `K` column. The ledger itself becomes `RULE W30-D` (§5) — a rule that can be wrong, replacing a list that rotted |
| 3 — choose among F82's three fixes | **REJECTED for wave 30** | Wave 29 §13 calls it the natural wave 31 and it deserves its own pre-registration. Mixing a fix-selection with a detector arm makes one wave carry two unrelated verdicts, which is how #197 came to carry three waves |
| 4 — `MaxDistortion`'s axis and name | **REJECTED for wave 30** | It is plugin C#, it owes the 42 m gate, and **its evidence base is exactly what `W30-G` is about to change.** F98 part 2 claims the top ~21 % of the axis is detection-killing; wave 30 measures the *bottom* of that axis for the first time. Shipping an axis bound in the wave that first measures the axis is the [F14](followups.md) / `RULE D20` fence in a new costume |
| 6 — `S27-1` and wave 27's three mutants | **REJECTED, and it should be formally WITHDRAWN** | Open four waves running. §16 registers the withdrawal rather than listing it a fifth time |

**Is there a wave here?** Yes, and unlike wave 29 it is not narrow. `W30-G` opens a gate **nobody has ever
opened in the correct direction**, on a hypothesis stated by the previous wave in a form that can be refuted,
with a conservation identity that makes the whole attribution model of these reports falsifiable for the first
time (§4.2). Three of its five cells are **genuinely blind** (§8) — wave 29's `W29-L` had none.

---

## 4. `RULE W30-G` — the gate-flow decomposition on `D01`. One arm, no build

### 4.1 What I verified in source MYSELF, and what I verified it against

[F98](followups.md) is taken as a lead, not as a fact. Every claim below was read at `HEAD` during
pre-registration, and the instrument re-asserts each of them at run time at **both** `HEAD` **and** the B15
tree the arm's binary was built from.

**(a) `MaxDistortion` is a MINIMUM fill ratio. Lowering it relaxes the gate.**
`StarDetector.cs:1802-1810`:

```csharp
var fillRatio = (starPoints.Count + donutHoleArea) / d / d;
var effectiveMaxDistortion = ComputeEffectiveMaxDistortion(p, d);
if (fillRatio < effectiveMaxDistortion) {
    metrics.TooDistortedBounds.Add(starBounds);
    …RecordRejection(…, RejectionGate.TooDistorted, fillRatio, effectiveMaxDistortion, …);
    return null;
}
```

The comparison is `fillRatio < threshold ⇒ REJECT`. **The threshold is a floor on fill ratio; raising it
tightens, lowering it relaxes.** The file's own comment at `:1786-1787` gives the ceiling: *"a perfect disk
fills ~PI/4 ≈ 0.79."* Wave 29 raised it to **0.9**, above that ceiling, and both cells that carried the raise
returned `TP=0 FP=0 FN=110384`.

**(b) The three defocus-aware master flags are OFF in `D01`'s landed tree, so effective == strict and the gate
chain is the legacy chain.** Read from `/mnt/d/hf_w29/l/opt/L0/attempt01/optimized_settings.json`:
`DefocusAwareGates: false`, `DefocusAwareDonutDetection: false`, `DefocusAwareStructure: false`,
`DonutMaxStreakEccentricity: 1.0`. Consequences, each from source:

- `ComputeEffectiveMaxDistortion` returns `p.MaxDistortion` verbatim (`:1459`), so the effective threshold is
  the edited value exactly.
- `donutHoleArea` is `0` (`:1799`), so `fillRatio` is the plain `points / d²`.
- The `TooElongated` gate at `:1775` **does not exist in the chain** — it is guarded by
  `p.DefocusAwareDonutDetection`.
- The integrated-SNR branch inside the sensitivity gate (`:1844-1850`) does not run, so
  `sensitivity == NormalizedBrightness / noiseSigma` exactly.

**This is a precondition of the whole design and the arm asserts all four and REFUSES if any is not as
recorded.** Wave 29 spot-checked one of them.

**(c) The gate order, by first `RecordRejection` line in `StarDetector.cs`:**

| # | gate | line |
|---|---|---|
| 1 | `TooSmall` | `:1757` |
| 2 | `OnBorder` | `:1766` |
| — | *(`TooElongated` `:1780` — absent, flag OFF)* | |
| 3 | `TooDistorted` | `:1807` |
| 4 | `Degenerate` | `:1823` |
| 5 | `LowSensitivity` | `:1854` |
| 6 | `NotCentered` | `:1876` |
| 7 | `TooFlat` | `:1891` |

**The instrument does not read this table.** It extracts the order from source by line number at run time,
at `HEAD` and at the B15 tree `29665a62e4f8` (read out of `/mnt/d/hf_w25/binary_provenance_w25.txt`, never
typed), and **refuses if the two disagree.** Wave 29 established the pattern for the `1.5` multiple; this
extends it to an ordering. Note that `NotCentered` and `TooFlat` are **downstream of `LowSensitivity`**, which
wave 28's and wave 29's prose ("`TooSmall` → … → `LowSensitivity`") never said and which matters to §4.2.

**(d) The landed knob values**, from the same file: `MaxDistortion 0.5`, `MinStarBoundingBoxSize 6`,
`BrightnessSensitivity 36.33333333333333`, `NoiseClippingMultiplier 1.0`.

### 4.2 THE STATISTIC: a flow with an exact conservation identity, and it can FAIL

Wave 29's `RULE W29-L` differenced `FN_total`. Its own results §3.2 measured why that throws the answer away:
`MinBox 6→3` releases **9 537** candidates from `TooSmall`, **5 697** are re-caught by `TooDistorted`, **3 814**
by `LowSensitivity`, and the net is **−3**.

**The flow model, stated as a specification of the report that can be wrong.** The attribution is
first-rejection-wins over the source order in §4.1(c). Relaxing exactly one gate `g` changes no other
threshold, therefore:

1. candidates rejected **upstream** of `g` never reach `g`, so those buckets are **invariant**;
2. candidates rejected **at** `g` are re-tested by everything downstream;
3. candidates that passed `g` before still pass it, so downstream buckets can only **grow**.

Write `R_g = FN_g(M0) − FN_g(cell)` for the **release**, and let the terminal classes be `TP`, `Contaminated`
and `ACCEPTED-elsewhere` (a released candidate that is accepted may be matched, matched elsewhere, or fall in a
contaminated region). Then:

> **`ΔNO-CANDIDATE = 0`, `ΔFN_h = 0` for every gate `h` strictly upstream of `g`, and**
>
> **`R_g  =  Σ_{h downstream of g} ΔFN_h  +  ΔContaminated  +  ΔACCEPTED-elsewhere  +  ΔTP`, EXACTLY, in integers.**

**Checked against wave 29's published artifacts before pre-registration, twice, and it balances on the nose
both times.**

`L0 → L1`, `MinBox 6 → 3`, `g = TooSmall` (first gate, so no upstream terms):

| term | value |
|---|---|
| `R_TooSmall` = 11 372 − 1 835 | **9 537** |
| `ΔOnBorder` | +10 |
| `ΔTooDistorted` | +5 697 |
| `ΔDegenerate` | +5 |
| `ΔLowSensitivity` | +3 814 |
| `ΔNotCentered` | +7 |
| `ΔTooFlat` | +1 |
| `ΔContaminated`, `ΔACCEPTED-elsewhere`, `ΔNO CANDIDATE` | 0, 0, 0 |
| `ΔTP` = 12 165 − 12 162 | +3 |
| **Σ downstream + terminals** | **9 537** ✓ |

`L0 → L3`, `Sensitivity 36.333 → 35.333`, `g = LowSensitivity` (gate 5, so gates 1–4 must be invariant — and
wave 29's decomposition lists no change on any of them):

| term | value |
|---|---|
| `R_LowSensitivity` | **122** |
| `ΔNotCentered` | +12 |
| `ΔContaminated` | +4 |
| `ΔACCEPTED-elsewhere` | +5 |
| `ΔTP` = 12 263 − 12 162 | +101 |
| **Σ** | **122** ✓ |

**Two exact balances on artifacts nobody built for this purpose.** That makes the identity a live specification
rather than an assumption, and it makes wave 29's `L0/L1` and `L0/L3` pairs available as **real fixtures with
known answers** for `score_w30g.py`'s self-test — not synthetic mutants.

**And it exposes a confound the naive reading would walk into.** `π_g = ΔTP / R_g` is **not comparable across
gates**: `MinBox` sits first and yields `3 / 9 537 = 0.0003`; `Sensitivity` sits fifth of seven and yields
`101 / 122 = 0.83`. The last gate before acceptance always has a high pass-through *because of its position*.
**§4.4 therefore never compares `π` across gates.** It pools.

### 4.3 THE CELLS — a 2×2 factorial, one fenced dose, two imports

`NoiseClippingMultiplier` held at **1.0** throughout, matching wave 28's `N-RECOVERS` cell and wave 29's whole
arm. No build. Binary `/mnt/d/hf_w25/exe`, `TestApp.dll` sha256 asserted against
`binary_provenance_w25.txt` **before the first cell**. Settings tree copied per cell from the same seed wave
29's arm used, edited fields only, `cp` **without** `-p`, `touch` after every copy ([F89](followups.md)).

| cell | `MaxDistortion` | `BrightnessSensitivity` | `MinStarBoundingBoxSize` | role |
|---|---|---|---|---|
| **`M0`** | 0.5 | 36.333 | 6 | **baseline / control** — must reproduce wave 29's `L0` high-tier attribution block field-for-field |
| **`M1`** | **0.3** | 36.333 | 6 | the gate never opened in the correct direction |
| **`M2`** | 0.5 | **32.333** | 6 | `Sensitivity` at four axis steps (11.0 %) — **diagnostic, fenced** |
| **`M3`** | **0.3** | **32.333** | 6 | the **joint** cell F99's hypothesis is about |
| **`M4`** | **0.2** | 36.333 | 6 | second `MaxDistortion` dose — **fenced out of every verdict branch, first to cut** (§13) |

**Imported from wave 29, not re-run** (`/mnt/d/hf_w29/l/out/L{1,3}/attempt01/golden_eval.txt`):

| import | edit | what it supplies |
|---|---|---|
| `L1` | `MinStarBoundingBoxSize` 6 → 3 | the third gate's flow, already measured |
| `L3` | `BrightnessSensitivity` 36.333 → 35.333 | the low dose of `M2`'s axis |

**What licenses the import, stated before the data.** Same binary, same frames, same seed tree, one field
differing. **`M0` re-running `L0` and reproducing it field-for-field IS the licence.** If `M0 ≠ L0`, the
pipeline is not reproducing and **the imports are void, `G-0` fails, and the verdict is `G-UNEVALUATED`** — the
wave does not fall back to using them anyway. This is stated here so that the licence cannot be granted after
seeing whether it was needed.

**Every edited field's JSON key is RESOLVED, never typed.** Wave 29 §9 recorded that the design named
`StarDetectorParams` spellings while the file declares `MinStarBoundingBoxSize` and `BrightnessSensitivity`.
The arm searches the tree's own declared keys for a case-insensitive match against a candidate list, **refuses
on zero or more than one match**, and prints the resolved key per cell. `MaxDistortion` resolves to
`MaxDistortion`; the arm still resolves it.

**One guard wave 29 did not need and wave 30 does.** The landed tree also declares
`EffectiveSensitivityGate: 36.33333333333333`, mirroring `BrightnessSensitivity`. Wave 29's `L3` edited
`BrightnessSensitivity` alone and the `LowSensitivity` bucket moved by 122, so the edit demonstrably takes;
wave 30 edits the same field, leaves the mirror alone, and **asserts from each cell's own `key detector knobs:`
echo that the intended value is the one in force.** A cell whose echo disagrees REFUSES.

**Wall-clock band, priced from wave 29's measured per-cell times** (177 / 198 / 152 / 168 / 145 s; the two
fastest were the two dead cells): predicted **~950 s** for five cells, pre-registered band **725–1 600 s**. A
run outside the band is reported, not silently accepted.

### 4.4 THE STATISTIC, fixed before the data

Per cell, read **only** the `FALSE-NEGATIVE ATTRIBUTION, HIGH TIER ONLY` block, anchored on that header
([F68](followups.md) part 5: the line appears twice per report and a first-match reader takes the wrong one).

For each single-knob cell `c` opening gate `g`:

```
R_g        = FN_g(M0) − FN_g(c)                       the release
survive_g  = ΔTP(c)                                   what reached acceptance
capture_g  = R_g − survive_g                          what a downstream gate re-caught
row C_{g→h} = ΔFN_h(c)  for every h downstream of g   WHERE it went
```

and the two aggregate quantities the verdict is taken on:

```
recapture  = Σ_g capture_g / Σ_g R_g                  pooled over ALL single-knob cells
material   = 0.01 × FN_total(M0)                      the bar, COMPUTED not typed
```

`material` is one percent of the misses the wave is trying to explain. At `FN_total(M0) = 50 249` (expected,
from `L0`) that is **≥ 503 stars**; the scorer computes it from `M0` and prints it.

Pooling `recapture` across gates rather than comparing `π_g` is deliberate — §4.2's positional confound makes
per-gate `π` incomparable, while the pooled fraction answers exactly the question F99 asks: *of everything
these knobs release, how much survives to acceptance?*

**The full capture matrix `C` is published** — every gate's release and where each unit of it went — because it
is the wave's product, and because publishing it is what makes a later reader able to check §4.2's identity.

### 4.5 THE CLAUSES, their populations, and their `None` conditions

| clause | statement | population it declares | value domain |
|---|---|---|---|
| **`G-0`** *(validity, first column of the tree)* | seven sub-assertions: (i) all cells `exit=0`; (ii) each echoes its intended knobs; (iii) `M0` reproduces `L0` field-for-field, fields compared ≥ 8; (iv) the source gate order is identical at `HEAD` and `29665a62e4f8`; (v) the four defocus preconditions of §4.1(b) hold in the landed tree; (vi) every edited JSON key resolved uniquely; (vii) dll sha256 matches the recorded provenance | cells run, fields compared, gates ordered, keys resolved — **each printed as `candidates: N`, each refusing on `N = 0`** | `{True, False}` |
| **`G-1`** *(the model)* | on **every** single-knob cell (`M1`, `M2`, `M4`, and imports `L1`, `L3`): `ΔNO-CANDIDATE = 0`, `ΔFN_h = 0` for all `h` upstream of `g`, `R_g ≥ 0`, and §4.2's balance holds as **integer equality with no tolerance** | single-knob cells | `{True, False}` |
| **`G-2`** *(the flow)* | `recapture ≥ 0.90` — at least 90 % of everything the single knobs release is re-caught downstream rather than accepted | released candidates, `Σ R_g` | `{True, False, None}` — **`None` iff `Σ R_g = 0`** |
| **`G-3`** *(the joint)* | `ΔTP(M3) ≥ material` **and** `ΔTP(M3) ≥ 2 × [ΔTP(M1) + ΔTP(M2)]` | the joint cell against the two singles | `{True, False, None}` — **`None` iff `ΔTP(M1) + ΔTP(M2) ≤ 0`**, because the ratio's denominator is then non-positive and a ratio against it is an artefact |

**`G-2`'s `None` region is reachable** (no knob releases anything) and **`G-3`'s `None` region is reachable**
(a single knob can *lose* accepted stars — `ACCEPTED-elsewhere` shows the matching is competitive, so `ΔTP < 0`
is possible). Both are named here, before the data, with the measured condition that reaches them. **This is
the region `RULE W29-L` fell into while its tree said `uncovered: 0`.**

**The `Sensitivity` fence, enforced in the scorer.** `M2`, `M3` and the `L3` import contribute to `G-2` and
`G-3` as *diagnostics of where flow goes*. The scorer **refuses to name `Sensitivity`** in any verdict string,
even if it carries the largest release, and prints the fence text beside the number.
[F84](followups.md) / `RULE S16` stands; no register entry from this wave may cite these cells as evidence for
or against a sensitivity bound.

### 4.6 THE PREDICTIONS — two of them, differing, both fixed before the data

**Prediction A, inherited — [F99](followups.md)'s hypothesis: `G-JOINTLY-BOUND`.** `TooDistorted` and
`LowSensitivity` bind jointly; no single knob recovers `D01`; opening the pair recovers materially more than
the sum of the singles.

**Prediction B, this document's own, and it differs: `G-NOT-RECOVERABLE`.** The reasoning, stated so it can be
wrong: `D01` is a 19.4 ″/px rig whose kernel is ~0.7 px, and `LowSensitivity`'s threshold is **36.3 σ**. A
candidate released by `TooDistorted` at 0.3 is a small, low-fill blob; a 11 % relaxation of a 36 σ bar does not
turn a 5 σ blob into a detection. I expect a large `R_TooDistorted`, `recapture` well above 0.90, and
`ΔTP(M3)` **below** the 503-star material bar — i.e. F99's mechanism confirmed and F99's *joint recovery* claim
refuted.

**Registering both is the point.** If `G-JOINTLY-BOUND` comes back, F99 was right and this document was wrong,
in writing, before the data. If `G-NOT-RECOVERABLE` comes back, F99's hypothesis is **refuted** and the wave
did not discover that after the fact. Neither outcome lets the wave claim a hit it did not call.

**What `G-SINGLE-BINDS` would mean, and it is the outcome with the largest product consequence.** If
`recapture < 0.90` — more than a tenth of what the knobs release reaches acceptance — then a single landed knob
is holding real stars, the largest `survive_g` names it, and **that is a parameter pinned against the physics,
goal 3 verbatim, worth its own wave.** Neither prediction expects it.

---

## 5. `RULE W30-D` — wave 29's "not run" ledger, CHECKED rather than inherited

§2.1 found a debt that had been paid seven hours before it was listed. **The remedy is not to fix that one row.
It is to stop inheriting the list.**

`RULE W30-D` parses wave 29's results §11 table (*"WHAT WAS NOT RUN, AND WHAT IT COSTS"*) out of the committed
document, resolves each row to a checkable predicate, and searches a declared set — `/mnt/d/hf_w2{5,6,7,8,9}`,
`/mnt/d/hf_w30`, and the repository at `HEAD` — for an artifact that satisfies it. Zero compute.

| clause | statement | population | domain |
|---|---|---|---|
| **`D-0`** *(validity)* | the §11 table parses; every row resolves to exactly one predicate; the search set exists | rows parsed (`candidates: N`, **refuses on `N = 0`**) | `{True, False}` |
| **`D-1`** | **no** row claimed "not run" has a satisfying artifact | rows claimed not-run | `{True, False, None}` — `None` iff every row is a "not applicable" row |

**Verdicts:** ¬`D-0` ⇒ `D-UNEVALUATED`. `D-0` ∧ `D-1` ⇒ `D-LEDGER-CLEAN`. `D-0` ∧ ¬`D-1` ⇒ **`D-STALE`**, a
FINDING, with every already-paid row named and its artifact's path and mtime printed.

**The prediction, and the honest disclosure that comes with it: `D-STALE`, with `P-D08 at n = 3` as a named
row — because §2.1 already found it.** That row is **read and is not blind.** **The blind quantity is the
count of *other* stale rows**, which nobody has checked, and `D-1`'s verdict is carried by the whole table, not
by the one row. This is [F100](followups.md)'s lesson applied at pre-registration time: the ledger says exactly
which quantity it has spent and which it has not.

**A row that cannot be resolved to a predicate leaves the denominator as `CNL-UNRESOLVABLE` and is named, never
scored as clean.** Wave 29's `CNL-NOREPORT` pattern.

---

## 6. `W30-FP` — the fingerprint, trimmed to what wave 30 reads, taken BEFORE anything

**The BEFORE sweep is the FIRST thing any instrument does — Step 2, immediately after Step 1 writes the
instruments, before `V30`'s fixture probes read `/mnt/d/hf_w29` and before the arm's first cell.** Wave 29 ran
it last and it cost `W29-FP` its verdict on four of six roots; the failure is not recoverable retroactively,
because a BEFORE taken afterwards is an AFTER.

**This deliberately puts the sweep ahead of the blocking pre-flight, and [F95](followups.md) is not weakened by
it.** F95's remedy is *instruments before the pre-flight, and the pre-flight before the measurements* — both
still hold. The sweep is not a measurement; it is the baseline the measurements are judged against, and it must
predate every read of a declared root, including `V30`'s own probes.

**The declared population is a list of (root, predicate) pairs, defined ONCE in `layout_w30.sh` and consumed by
both sides** ([F91](followups.md): one expression, called twice). Population size asserted **equal** before any
hash is compared. Paths deduplicated by `realpath` so the D01 overlap between rows 1 and 2 is counted once.

| # | root | predicate | why wave 30 opens it | size |
|---|---|---|---|---|
| 1 | `/mnt/d/SyntheticAutofocusBank/D01_ultrawide_40mm` | all files | the arm's 9 frames and golden sidecars — **the only dataset wave 30 runs** | 630 M |
| 2 | `/mnt/d/SyntheticAutofocusBank` | `-name synthetic_meta.json` | the `K` column's `truthModel` (§2.6), 20 files | ~1 M |
| 3 | `/mnt/d/hf_w25/exe` | all files | the B15 `TestApp` the arm runs; its dll sha256 is separately asserted | 196 M |
| 4 | `/mnt/d/hf_w29` | all files | `L0`/`L1`/`L3` `golden_eval.txt` (the control and the two imports), the three scorers (self-test captures), `verify_derivation_w29.py` (`V30`'s parent), and the §11 table's search target | 635 M |
| 5 | `/mnt/d/hf_w28/nc` | all files | the published `NC = 1.0` block — the second leg of the `M0 → L0 → wave 28` control chain | 889 M |
| 6 | `/mnt/d/hf_w25/table18` | all files | provenance of the results table the `K` column edits | 14 M |
| | **total** | | | **~2.4 G** |

**Dropped, each with its reason — this is the part wave 29 could not write:**

| dropped | size | why wave 30 does not read it |
|---|---|---|
| `/mnt/d/hf_w25/after` | **17 G** | `W29-R`'s population. Wave 30 runs no `SearchSpan` rule, and the `K` column's `truthModel` is **not** in `synth_validate_report.json` (§2.6) |
| `/mnt/d/hf_w25/before` | **15 G** | Nothing in wave 30 reads it. It was in wave 29's list without a stated reader either |
| `/mnt/d/SyntheticAutofocusBank/D02…D20` frames | ~8.1 G | The arm is **one dataset**. Only their `synthetic_meta.json` is read, and row 2 covers exactly that |
| `/mnt/d/hf_w26/pd08` | small | §2.1: `RULE W30-D` **reads** it, so it would belong here — **and it is added as row 7 if `D-0` resolves any row to that path.** The root list is computed from the resolved predicates, not typed |

**Price, from wave 29's measured rate.** 2.4 GB at **61 MB/s** is **~40 s per sweep**. Priced at **3 m per
side**, ~4× the measured expectation, because the roots are on DrvFs and the small-file rows carry per-file
overhead the rate does not capture. Wave 29 owed ~24 m for both sides and did not pay it; wave 30 owes ~6 m and
pays it. **That is what "justify each entry" buys.**

---

## 7. `RULE V30` — the pre-flight, three generations carried, fixtures MEASURED

Derived from `/mnt/d/hf_w29/verify_derivation_w29.py`, **not** from any `.bak`.

**Carry forward and demonstrate all THREE generations of the `historical_ranges` repair**
([F87](followups.md) → wave 27 → wave 29's string-literal blanking, [F101](followups.md)):

1. bracket-depth top-down with `is_definition` excluded (wave 27's repair);
2. the `opens > 0` requirement (wave 28's);
3. **length-preserving blanking of string literals before counting** (wave 29's, `verify_derivation_w29.py:224`
   `code_of`, `:229-238` `historical_ranges`) — the fix for the phantom block that made 29 of 599 lines of wave
   28's own checker exempt.

Self-test clause `[6]` asserts **all four ends**, including *"a token INSIDE the declared historical literal is
NOT reported"*. **F101's durable point is that each generation's self-test tested the ends the previous
generation broke**, so `[6]` keeps every end all three generations established and adds none of its own
invention.

**Per-clause fixtures are MEASURED, not inherited** ([F103](followups.md)). The probe runs over
`/mnt/d/hf_w{24,26,27,28,29}` — five roots, one more than wave 29 — and pins from the measurement.

**The forward prediction, written here one wave before the measurement, which is the strongest evidence class
this series has:** `PREV` is now **29**, so

- **`/mnt/d/hf_w28` EXPIRES** — wave 29's live `-B` fixture, which yielded **100** `-B` findings at wave 29,
  goes to **0** at wave 30;
- **`/mnt/d/hf_w29` becomes the new live `-B` fixture**, yielding a large non-zero `-B` count;
- `/mnt/d/hf_w24` remains the durable `-C` fixture (2 findings, fourth wave running);
- `/mnt/d/hf_w26` and `/mnt/d/hf_w27` remain asserted-silent at `-B = 0`.

**A clause with no live fixture is a FAIL, not a pass.** The expired roots are kept and asserted silent so the
expiry is demonstrated rather than described.

**`V30` runs TWICE: at Step 3 over the populated root, and at the close** (`vdrift_w30_CLOSING.txt`). The
closing run is **not cut at any budget** (§13). Wave 29 proved the first half of [F95](followups.md)'s remedy
and, at 18:18:01Z, the second; wave 30 does both from the start.

**Every `V30` clause prints its population and refuses on an unexpected `N = 0`** — [F102](followups.md)'s
remedy, and the direct fix for `V28-C` returning zero over 2 of 126 printing lines.

---

## 8. BLINDNESS LEDGER — stated once, and CHECKED AGAINST THIS DOCUMENT'S OWN INSTRUMENTS

[F100](followups.md)'s remedy, executed: **every quantity this ledger promises stays unread is checked against
every clause's per-member output in §4 and §5 before this document is committed.** The check is recorded below,
row by row, and it is why the `W30-D` row says what it says.

**READ, and by whom:**

| quantity | by whom | scope of the burn |
|---|---|---|
| `D01`'s `L0` high-tier attribution block, every field | wave 28 §1.1, wave 29 §3.2, and §4.2 above | `M0`'s baseline half — it is a **control**, not a statistic |
| `L1`'s and `L3`'s full per-gate deltas | wave 29 §3.2 and [F99](followups.md), and §4.2 above | the two imported cells. Their flows are **published inputs**, not blind measurements |
| `D01`'s landed knob values and the three defocus flags | §4.1(b), (d) — read from `L0`'s settings tree at pre-registration | none: they are the arm's inputs |
| `StarDetector.cs`'s gate order and the `MaxDistortion` comparison | §4.1 above | none: source, not data |
| the `synthetic_meta.json` **schema** (`truthModel`'s key names only, no values) | §2.6 | none |
| **`P-D08` at `n = 3`'s result** | §2.1 — **read, in full, at pre-registration** | **one named row of `RULE W30-D`'s population.** Disclosed in §5 |

**BLIND, and carrying `RULE W30-G`'s verdict:**

> **`M1`, `M3` and `M4` — every number they produce.** `MaxDistortion` has **never** been run at a value below
> 0.5 on this dataset by anyone. `M2` at four axis steps has never been run either. Their releases, capture
> rows, `ΔTP` and the pooled `recapture` are unread by any human or agent at the time this document is
> committed.

**Checked against the instruments, per F100:** §4.4 and §4.5 specify that the scorer computes and prints
exactly these quantities and nothing else about `M1`–`M4`. No clause in §4.5 requires reading any `M1`–`M4`
value before the rule is scored, and no clause in §5 or §7 reads them at all. **The ledger and the instruments
are consistent.** Wave 29's were not, and nothing checked.

**BLIND, and carrying `RULE W30-D`'s verdict:** every row of wave 29's §11 table **except** `P-D08`.

**The burn wave 30 will spend, named in advance:** publishing `W30-G`'s capture matrix reads `M1`–`M4`
completely. **`D01`'s gate behaviour at these settings will have no blind population left.** That is the
correct trade — the matrix is the wave's product — and it means any further arm on `D01` at these knobs must
say plainly that it is unblinded, exactly as §7 of wave 29 had to.

**`RULE V30` has no blind population and this is said plainly.** Its honesty mechanism is the §7 forward
prediction about fixture expiry, which is falsifiable and is checked one wave after it was written.

---

## 9. THE GATE — not owed, and the three conditions that reverse it

**Wave 30 changes no C# and builds no binary.** The arm runs `/mnt/d/hf_w25/exe`, dll sha256 asserted against
`binary_provenance_w25.txt` before the first cell. Wave 26's four conditions for an honest skip all hold:
nothing is compiled; `G25` already answered what a re-gate asks; a re-gate does not reach this wave's risk; and
**no clause in any verdict tree in this document reads a gate landing.** *A control no clause consumes is a
ritual.*

**The three conditions are checked at Step 8 and any one of them stops the wave:** a single line of C# changing
for any reason; a hash or `BuildId` mismatch on the arm's binary; or the arm's own instrument proving
non-deterministic. The check is `git diff --name-only <base> HEAD -- '*.cs'` **unioned with**
`git ls-files --others --exclude-standard -- '*.cs'` ([F88](followups.md)), and it is **run**, not asserted.

**If wave 30 proposed C# — and it deliberately does not (§3, item 4) — the 42 m `optimize` gate would be owed,
certainly and not probably.** `Q27-V1`'s reachable set is a directory prefix over the whole plugin assembly,
and `StarDetector.cs` sits under it by a wide margin. Price: **42 m** measured (wave 25: 42 m 43 s, ~5.2 m/run,
seven consecutive waves), predicted verdict `G-PASS`, 8 of 8 bit-identical to K8. The unit suite would run **by
count** ([F37](followups.md)) against baseline **3997** — wave 28's measured close, **not** the charter's stale
3973.

---

## 10. WHAT SHIPS — nothing, fixed here, before the data

| candidate ship | status |
|---|---|
| bounding the `MaxDistortion` axis at ~π/4 (F98 part 2) | **DOES NOT SHIP.** §3: wave 30 is the first wave to measure the *bottom* of that axis; shipping a bound in the wave that first measures it is the `RULE D20` fence |
| renaming or re-documenting `MaxDistortion` | **DOES NOT SHIP.** Same wave, same fence |
| anything on the `Sensitivity` axis | **DOES NOT SHIP.** [F84](followups.md) / `RULE S16` is permanent; §4.5 fences the scorer |
| anything on `MinStarBoundingBoxSize` | **DOES NOT SHIP.** Its flow is an *import*; a default change owes its own wave with a fresh baseline ([F93](followups.md)'s three refusals apply verbatim) |
| any F82 fix | **DOES NOT SHIP.** Wave 31's item, its own pre-registration (§3) |

**Consequence:** no C# changes, so the unit suite is not run, and that is stated rather than skipped silently.
The condition that reverses it is in §9 and in the plan's Step 8.

**Documentation does change:** `docs/synthetic-af-bank-results-table.md` gains the `K` column (§13, Step 7).
That is a `.md` edit and reaches no gate.

---

## 11. VERDICT TREES, ENUMERATED OVER THE ACTUAL VALUE DOMAIN — `None` INCLUDED

Every clause appears in its own tree, **including every validity clause**, over its **actual** domain from
§4.5 / §5 / §7 — `{True, False}` where the clause is a conjunction of assertions, `{True, False, None}` where
it can be undefined. Every scorer re-derives the enumeration in code, prints `regions enumerated: N
uncovered: 0`, **prints its own realized clause tuple, and ASSERTS that tuple is a member of the enumerated
set.** A gap prints `<RULE>-TREE-GAP`, enumerates the uncovered regions, prints what this document's tree would
have said, and **does not repair and re-score**.

**This is the [F94](followups.md) append executed.** Wave 29 proved totality over `{True, False}` and then
landed on `(True, None, None)`. **Proving totality over the wrong domain is not proving totality.** The
membership assertion is the half that catches it.

**`RULE W30-G` — 4 clauses, 2 × 2 × 3 × 3 = 36 regions, 0 uncovered**

| `G-0` | `G-1` | `G-2` | `G-3` | verdict | regions |
|---|---|---|---|---|---|
| `F` | `*` | `*` | `*` | `G-UNEVALUATED` | 18 |
| `T` | `F` | `*` | `*` | **`G-MODEL-BROKEN`** — a FINDING: the report's first-rejection-wins attribution does not conserve, and **every gate-count difference in waves 28 and 29 is unsafe.** No repair, no re-score | 9 |
| `T` | `T` | `None` | `*` | **`G-NO-RELEASE`** — a FINDING: no knob released anything; these three gates are not where `D01`'s loss is | 3 |
| `T` | `T` | `F` | `*` | **`G-SINGLE-BINDS`** — more than a tenth of the released mass reaches acceptance; the largest `survive_g` names the knob (subject to §4.5's `Sensitivity` fence). **The goal-3 product finding** | 3 |
| `T` | `T` | `T` | `T` | **`G-JOINTLY-BOUND`** — **prediction A** (F99) | 1 |
| `T` | `T` | `T` | `F` | **`G-NOT-RECOVERABLE`** — **prediction B** (this document): re-capture dominates *and* the joint is immaterial | 1 |
| `T` | `T` | `T` | `None` | **`G-SINGLES-DEAD`** — the singles delivered ≤ 0 to acceptance, the ratio is undefined, the joint is published raw and **no ratio is quoted** | 1 |
| | | | | **total** | **36** |

**`RULE W30-D` — 2 clauses, 2 × 3 = 6 regions, 0 uncovered**

| `D-0` | `D-1` | verdict | regions |
|---|---|---|---|
| `F` | `*` | `D-UNEVALUATED` | 3 |
| `T` | `T` | `D-LEDGER-CLEAN` | 1 |
| `T` | `F` | **`D-STALE`** — a FINDING, rows named — **the prediction** | 1 |
| `T` | `None` | `D-VACUOUS` — every row was "not applicable"; **a FINDING about the ledger's shape**, not a pass | 1 |

**`RULE V30` — 3 clauses, `A` ∈ `{T,F,None}`, `B`,`C` ∈ `{T,F}`, 12 regions, 0 uncovered.** Inherited from
wave 29's checker unchanged (`vdrift_w29_CLOSING.txt` prints all 12, including the `A=None` empty-population
region), plus the per-clause population line and the `N = 0` refusal from §7.

**`W30-FP` — `PRESERVED` / `VIOLATED` / `UNEVALUATED` on differing membership**, with `UNEVALUATED` reached
when the BEFORE and AFTER populations differ in size — asserted before any hash is compared.

**Why the pattern F94 names does not recur here.** F94's diagnosis was *"the uncovered region is always the
VALIDITY clause"*. `G-0` and `D-0` are the **first column** of their own tables, and `V30`'s `A` is declared
validity-with-a-`None`. And the new failure mode wave 29 found — a realized tuple outside the enumerated set —
is caught by the membership assertion, not by a count.

---

## 12. CLAUSE POPULATIONS — [F102](followups.md)'s remedy, in every instrument this wave writes

**Every clause, in every instrument, prints:**

```
  <clause>: candidates: <N>   findings: <M>       [or   value: <x>]
```

and **REFUSES** — verdict `<RULE>-POPULATION-EMPTY`, a FINDING — when `N = 0` on a clause that declares a
non-empty population. `N` and `M` are **computed**, never typed (`V30-C`).

The populations each clause declares are named in §4.5, §5 and §7. The class F102 identifies — *"no verdict
tree in this series distinguishes zero findings from zero candidates"* — is closed by making the distinction
**printable and refusable**, not by widening one regex. `V30`'s regex widening (wave 29's, carried forward)
remains, and is now *checked* by the population line rather than trusted.

**A clause whose population is unsatisfiable is a FINDING and is not repaired.** Standing rule, re-stated
because wave 30 writes five new clauses and the temptation is proportional.

---

## 13. PRICE, scored against the owner's three goals, and what is cut first

Goals (charter §8): **(1)** optimization + autofocus across a wide range of setups; **(2)** accuracy of
step-size recommendations; **(3)** appropriate exposure adjustments, no parameters pinned to extremes.

| step | est | compute | goals |
|---|---|---|---|
| 0 vessel + pre-registration commit + PR with the right base | 12 m | — | — |
| 1 write **all 7 instruments** before any of them runs ([F95](followups.md)) | 60 m | — | — |
| 2 **`W30-FP` BEFORE** — the first thing any instrument does | 5 m | — | instrument |
| 3 `RULE V30` pre-flight over the **populated** root + 5 fixture probes | 15 m | — | instrument |
| 4 `RULE W30-G` arm, 5 cells | 25 m | **~15 m** | **3** ✓✓ |
| 5 `RULE W30-G` scoring | 12 m | — | **3** ✓✓ |
| 6 `RULE W30-D` + the three retrospective scorer self-test captures | 15 m | ~2 m | instrument |
| 7 the results-table `K` column | 12 m | — | **3** ✓ |
| 8 `W30-FP` AFTER + **closing `V30`** + the §9 reversal checks | 8 m | — | instrument |
| 9 results, register, handoff (folded) | 80 m | — | — |
| **total** | **~4 h 04 m** | **~17 m** | |

**Against a hard stop of `2026-08-14T00:45:17Z` and a start near `18:25Z`, that is ~6 h 20 m available and
~2 h 15 m of margin.** The margin is deliberate: this is the fifth consecutive wave in which the analysis
halves are over-priced (wave 29 came in under on Steps 1, 2, 4, 5 and 6; wave 28 ~59 % under; wave 27 ~36 %).
**The two figures priced from a measured rate — Step 4 from wave 29's 145–198 s per cell, Step 3/8 from wave
29's 61 MB/s — are the two expected to be accurate**, and wave 29 demonstrated that the steps priced from
instinct are the only ones that overran.

**What is cut first, in order:**

1. **`M4`** — the second `MaxDistortion` dose. It is **fenced out of every verdict branch** by construction, so
   cutting it changes no verdict and no register entry. −3 m compute, −3 m wall. *Named first because it is
   genuinely free to cut, which is why it was designed to be fenced.*
2. **Step 7, the `K` column.** −12 m. **And if it is cut, it must be WITHDRAWN or paid, not re-listed** — it
   has now been owed by waves 28, 29 and 30, and a debt carried three waves without being paid is a debt
   nobody intends to pay. §16 registers that either way.
3. **The three retrospective scorer self-test captures.** −6 m. They are a *retrospective* capture of a gate
   wave 29's plan wrote and did not take; running them now proves the scorers' self-tests pass **now**, which
   is weaker than the gate would have been, and the results document must label them as retrospective.
4. **Step 9's handoff document**, folded into the results file's final section rather than written separately.

**What is NOT cut, at any budget:**

- **the closing `V30`** (~2 m) — [F95](followups.md)'s remedy is both halves or neither;
- **the `W30-FP` BEFORE sweep** (~3 m) — it cannot be recovered retroactively, and wave 29 paid the whole cost
  of learning that;
- **`RULE W30-D`** (~10 m of the 15) — it is the only thing that stops the debt list rotting further, and §2.1
  is the measurement that says it is already rotting.

**Prices this wave hands forward rather than paying:**

| item | price |
|---|---|
| F82's three-way fix choice (wave 29 §13 item 3) | ~40 m to choose; ship price ~45 m + 3-cell re-run + **42 m gate** for candidate (3), ~2–4 h + gate for candidate (1) in the product |
| `MaxDistortion` axis bound + rename (F98 part 2) | ~20 m code **+ 42 m gate**, and **it should now wait for `W30-G`'s capture matrix**, which is the first measurement of that axis's low end |
| the `A4` truth-model gap | 20 m code **+ 42 m gate** — it rebuilds `TestApp` |
| new datasets between 1.4 and 19.4 ″/px | ≥ 1 h render. The only route to a **blind** wide-field dataset; the coverage hole is unchanged after eleven waves |
| a full paired 18-cell S1 arm | ~2 h 40 m. `W-UNEXERCISED` twice: a second arm buys a second `SAME 13` |

---

## 14. INSTRUMENTS AND CONSTRAINTS

Seven files, all under `/mnt/d/hf_w30/`, **LF**, **ASCII**, derived from `/mnt/d/hf_w29/`, **all written at
Step 1 before any of them runs** ([F95](followups.md)):

| file | derived from | role |
|---|---|---|
| `layout_w30.sh` | `layout_w29.sh` | shared layout, the `(root, predicate)` array, `w30_sweep` (**one** expression, called twice), the shared self-test counter |
| `verify_derivation_w30.py` | `verify_derivation_w29.py` | `RULE V30` — three generations of `historical_ranges` (§7), populations, `None` |
| `fp_w30.sh` | `fp_w29.sh` | `W30-FP` |
| `w30g_arm_w30.sh` | `w29l_arm_w29.sh` | the 5-cell arm + manifest + `READY` marker |
| `score_w30g.py` | `score_w29l.py` | `RULE W30-G` — the flow scorer |
| `score_w30d.py` | *(new)* | `RULE W30-D` — the ledger checker |
| `kcol_w30.py` | *(new)* | the `K` column extractor |

**Standing constraints, every one of them earned:**

- **`--out` mandatory and REFUSING without it** on every instrument. The plan's command lines in
  `plans/synthetic-af-bank-followups-wave30-plan.md` §0 are **checked against each instrument's own `usage:`
  line at Step 1** — wave 29's plan wrote `bash fp_w29.sh after` with no `--out` against its own standing
  constraint, and the controller had to deviate.
- **`--rule` asserted against the file's own declared rule name** ([F90](followups.md)) — a hard refusal.
  `verify_derivation_w29.py` accepts `--rule` (its `argparse` declares it at `:748`); `V30` keeps it and
  asserts it.
- **Populations asserted; ordinals and counts COMPUTED, never typed** (`V30-C`), including the **gate order**
  (§4.1(c)), the **axis step** (§2.4) and the **material bar** (§4.4).
- **Paths handed to scorers through a MANIFEST** ([F74](followups.md)), never rebuilt from a template.
- **BEFORE/AFTER from ONE expression called twice** ([F91](followups.md)); population size asserted **equal**
  before any hash is compared; the fingerprint written **outside** every tree it sweeps.
- **`find … -print0 | sort -z | xargs -0`** — `/mnt/d/Autofocus Bank` contains a space.
- **`cp` without `-p`; `touch` after every copy or restore** ([F89](followups.md)).
- Provenance: `git diff --name-only <tree> -- '*.cs'` **unioned with** `git ls-files --others
  --exclude-standard -- '*.cs'` ([F88](followups.md)); the B15 tree hash read out of
  `/mnt/d/hf_w25/binary_provenance_w25.txt`, **never assumed from a previous HEAD**. Note wave 29 §9's
  correction: that file's `tree:` line holds a **commit**, and the field name is wrong wherever it is quoted.
- **Pass scorers WSL paths.** A Windows path yields `UNEVALUATED` and looks like a failed arm.
- `TestApp.exe` gets `< /dev/null`; `--settings` **and** `--profile-id` pinned on the arm, `--settings` pinned
  to the file wave 28's log records (`…\HocusFocusHarness\harness_settings.json`, sha256 `b7e81813…`) and
  recorded in the manifest; **one `TestApp.exe` at a time**; **no directory rebuilt mid-wave.**
- **Read-only for the whole wave:** every root in §6's table.

**Every conclusion in this wave is synthetic-only.** The bank has no real under-sampled dataset
([F35](followups.md), [F43](followups.md)) and `W30-G` is **one dataset, `D01_ultrawide_40mm`**, named as one
dataset everywhere it is quoted.

---

## 15. WHAT WAVE 30 WILL NOT RUN, AND WHAT IT COSTS

| not run | price | why, and is it owed |
|---|---|---|
| the 42 m `optimize` gate | 42 m | **NOT OWED** — no C# (§9), checked rather than asserted at Step 8. Owed by the wave that ships anything from §10 |
| the unit suite | ~4 m | Vacuously satisfied. Baseline for the wave that changes C# is **3997**, not 3973 |
| `P-D08` at `n = 3` | ~2 m | **NOT OWED — already paid**, 09:06–09:09Z, `e42e2df`, `/mnt/d/hf_w26/pd08/`. §2.1 |
| the closing `V29` | ~2 m | **NOT OWED — already paid**, 18:18:01Z, `vdrift_w29_CLOSING.txt`. §2.2 |
| PR #197's title correction | ~1 m | **NOT OWED — already paid.** §0 |
| re-targeting #196/#197's PR bases | ~2 m | **Recommended to the owner, not done by wave 30** (§0). It edits open PRs the owner may be reading |
| `S27-1` and wave 27's three mutants | ~20 m | **FOUR waves open.** §16 registers its **withdrawal** rather than listing it a fifth time |
| a second `MaxDistortion` dose beyond `M4` | ~3 m each | The axis has 10 steps; wave 30 measures 3 of them and says so |
| a full paired 18-cell S1 arm | ~2 h 40 m | Unchanged: a second arm buys a second `SAME 13` |
| new datasets between 1.4 and 19.4 ″/px | ≥ 1 h render | The coverage hole, eleven waves running |
| the blur/layer decoupling build ([F92](followups.md)) | ~45 m + 10 m (+42 m to ship) | Unchanged |
| the `A4` truth-model gap | 20 m code **+ 42 m gate** | Made more urgent by [F96](followups.md), not less |
| anything on real frames | — | The bank has no real under-sampled dataset |

---

## 16. REGISTER ENTRIES OWED, whatever the verdicts

The register's highest existing entry is **F103**.

- **F104** — `RULE W30-G`'s verdict, with the full capture matrix, `Σ R_g`, `recapture`, `survive_g` per gate,
  `ΔTP(M3)` against the computed material bar, and the F22 caveat inline. Framed as **goal 3** per §2.7;
  **forbidden** from quoting a recall gain as a product benefit.
- **F105** — the **gate-flow conservation identity** (§4.2) as a *specification of the attribution report*,
  with `G-1`'s verdict. If `G-1` holds, this is the first checked model of these reports in eleven waves. If it
  fails, it is the largest instrument finding this series has produced and it invalidates every gate-count
  difference waves 28 and 29 published.
- **F106** — **the stale debt.** `P-D08` at `n = 3` was measured, published and committed at 09:06–09:09Z on
  2026-08-13 and was carried on wave 29's owed list at 18:05Z the same day, because the search was for an
  artifact *under the wave's own root* rather than for the measurement. [F85](followups.md)'s class, recurring
  inside a single day. With `RULE W30-D`'s verdict and every other stale row it names.
- **F107** — **the vessel.** #196 and #197 both target `develop`, so #197's own diff carries waves 27, 28 and
  29 plus wave 27's C# ship (25 files, measured). Opening a fresh branch relocates accretion; **only the PR
  base prevents it.** With the recommendation to re-target both.
- **[F99](followups.md) — AMEND IN PLACE** with `W30-G`'s verdict: its hypothesis confirmed or refuted, named
  as such, and its "the statistic must be the per-gate flow" half either vindicated by §4.2's identity or
  superseded by it.
- **[F98](followups.md)** — append the first measurement of the *low* end of the `MaxDistortion` axis, and
  whether part 2's axis-bound proposal gains or loses support.
- **[F103](followups.md)** — append wave 30's five-root fixture measurement against §7's forward prediction.
  **This is the second consecutive test of the series' only falsifiable forward claim about its own
  instruments.**
- **[F94](followups.md)** — append whether the `None` enumeration plus the **realized-tuple membership
  assertion** caught anything the count would have missed.
- **[F102](followups.md)** — append whether any clause's `candidates: N` printed `0` and refused, i.e. whether
  a vacuous clause existed and was caught at the moment it was written rather than three waves later.
- **[F95](followups.md)** — append that wave 30 ran the pre-flight over a populated root **and** at the close,
  both from the pre-registration rather than from a debt.
- **[F21](followups.md)** — estimate vs actual per step, plus the measured constant for the **trimmed** root
  set (~2.4 GB) against wave 29's ~41.8 GB, so a later wave can price a sweep from its own declared roots.
- **`S27-1` and wave 27's three mutants — WITHDRAWN**, formally, after four waves on the cut list. Wave 29 §13
  said cutting it a fifth time should mean withdrawal; wave 30 withdraws it rather than listing it again.
- **`docs/synthetic-af-bank-results-table.md`** — the `K` column from `truthModel.max(hfrMin, hfrMinEffective)`
  (source: `synthetic_meta.json`, §2.6), the note that `D01`/`D02`/`D03` are the only three floored by the
  generator, and **no 2–4 px band claim below the band** (`W28-B` = `B-SPLIT`). Owed by waves 28 and 29; §13
  says it is paid or withdrawn this wave, not re-listed.

---

## 17. THE STOP QUESTION, answered

Wave 29 §13 wrote the honest forward-looking sentence: *"items 2 and 4 above are the last two product questions
this bank can answer without new data, and once they are closed, the next wave that cannot name a product
question should stop rather than audit its own instruments a fourth time."*

**Wave 30 is item 2. It names its product question in §1 and it is a real one:** a knob landed at an extreme on
the rig where the physics is hardest, and nobody has ever opened the gate that plausibly holds it. So wave 30
runs.

**What wave 30 owes the wave after it, stated now rather than discovered later.** After `W30-G` lands, the
bank's remaining product questions are:

1. **item 4** — bound the `MaxDistortion` axis and rename it (F98 part 2), which `W30-G` will have supplied the
   evidence for;
2. **item 3** — choose among F82's three fixes, which needs no new measurement at all;

and **both are ships, not measurements.** Neither needs another arm on this bank. So the honest statement wave
30 hands forward is: **after wave 30, this bank has no unanswered product question that another synthetic arm
can address.** Waves 31 and 32 should be the two ships, each with its 42 m gate, and **the wave after those
should stop** — or render the 1.4–19.4 ″/px datasets that [F35](followups.md)/[F43](followups.md) have named as
the coverage hole for eleven waves, which is the only thing that would give this series a new blind population.

**A wave that cannot name a product question should stop rather than audit its own instruments a fifth time.**
Wave 30 can name one. It is the last time that sentence is expected to be true without new data.
