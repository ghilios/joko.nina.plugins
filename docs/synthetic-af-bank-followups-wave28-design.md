# Wave 28 — backlog item 8: recall where the star is smaller than the detector's calibrated band

**Pre-registration. Written before any wave-28 statistic is computed.** Section 2 records where the
controller's framing is wrong; there are seven such places and four of them change what the wave measures.

---

## 0. THE VESSEL — decided in writing, before any measurement

**Wave 28 opens a fresh branch off `ghilios/synthetic-af-bank-followups-wave27`, named
`ghilios/synthetic-af-bank-followups-wave28`, and PR #196 stays open on its own branch.**

- PR #195 (`…wave23`, waves 23–26) is **MERGED**.
- PR #196 (`…wave27`) is **OPEN / MEREABLE** at `915aa50`.

Reason for a fresh branch rather than a fifth section on #196: #196 carries a *single* wave and is
reviewable as one; the charter's §1a warning is against letting one PR silently accrete waves, which is
exactly what #195 did. Wave 28 also proposes a **user-visible product change** (§9), and a product change
should not ride in on a PR whose title says "audit the truth-protection correction".

---

## 1. THE HEADLINE: the item's premise is wrong in three ways, and one of them is the same shape as wave 27's

The backlog calls item 8 *"the item nobody has ever worked"* and *"the LARGEST product gap in the owner's
table"*. Taking those in order:

### 1.1 It has been worked. Five times. The mechanism the controller asks me to find is already in the register

| entry | status | what it already established about `D01`/wide field |
|---|---|---|
| **[F20](followups.md)** | Done | `D01`'s truth HFR per frame `[2.27,1.71,1.15,0.61,0.24,0.61,1.15,1.71,2.27]` px; the middle **5 of 9** at/below `MinHFR=1.2`; `Current settings J: 0` is a cold-start plateau |
| **[F35](followups.md)** | Done (w3), re-measured w6 | `MinHFR` 1.2→0.1 on `D01`/`D02`/`D03`, **32 configurations, `FP = 0` in all of them**; gain saturates by 0.5; largest gain is **`D03` (+0.192)**, not `D01` (+0.036) |
| **[F43](followups.md)** | Done (w5) | `D01` at default gate yields **0 accepted stars at best focus** vs 1859–4182 in the wings. *"`MinHFR` is the ONLY axis that rescues it — `MinimumStarBoundingBoxSize` 5→3 changes nothing, `StructureLayers` 4→2 makes it strictly worse."* On the reporter's real 61 MP 19.4″/px rig, **the binding gate is `NoiseClippingMultiplier`, not `Sensitivity`** |
| **[F44](followups.md)** | Done (w6) | `D01` was rendering starless outside a central patch (ASTAP cell clamp); re-render 8 107 → **19 212** truth stars |
| **[F22](followups.md)** | Open | The **pixelization floor**: at HFR ≲ 1.1 px measured HFR **over-reads by +26 % to +234 %**; `D01`'s optical vertex `0.231 px` measures `0.77 px` |

**And [F35](followups.md) already published the false-negative attribution the controller presents as the
wave's free starting point**, three years of waves earlier, in its own words:

> *"**The W class's recall is lost in candidate FORMATION** — the structure map never proposes 49 % of them,
> and `MinimumStarBoundingBoxSize` rejects another 17 % as `TooSmall` — so anyone expecting the `MinHFR` fix
> to lift `D01` toward the 0.90 W-class band will be disappointed, and **the band stays missed for a reason
> this entry does not address**."*

**This is wave 27's headline shape repeating: the assigned item was substantially worked, and the wave's
first product is finding that out.** What is genuinely un-worked is narrower and is named in §3.

### 1.2 "Wide field" is not the covariate. **In-focus PSF size in detection pixels is**, and the product already says so

`DetectionBinningResolver` states the detector's supported sampling band in source, as a constant and in
user-facing prose:

```csharp
/// <summary>The in-focus HFR (in binned pixels) the recommendation aims for: the center of the detector's
/// calibrated 2-4 px band.</summary>
public const double TargetHfrPixels = 3.0;
```
— `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/DetectionBinningResolver.cs:42-44`, prose at `:101-104`

Ranking the bank by **in-focus kernel HFR in captured pixels** (`truthModel.hfrMin`, floored at
`hfrMinEffective`) rather than by arcsec/px reproduces the recall column and, critically, **puts wave 27's
`RULE D27` and item 8 on one axis, on opposite sides of the same band**:

| in-focus HFR (px) | datasets | relation to the 2–4 px band | observed |
|---|---|---|---|
| **0.700** (all three floored) | `D01`, `D02`, `D03` | **4.3× below** | `recall@high` 0.183 / 0.446 / 0.365 — item 8 |
| 1.038–1.080 | `D04`, `D16`, `D18`, `D20` | 2× below | 0.855–0.937 |
| 1.804 | `D05`, `D06`, `D19` | just below | 0.964–0.989 |
| **3.168–3.477** | `D13`, `D07` | **in band** | 0.973–1.000 |
| 4.861–10.195 | `D08`–`D12`, `D14`, `D15`, `D17` | above | `RULE D27`: **binning 2 moves them back into band and recall rises**, `D14` `recall@all` 0.641 → **0.937** |

**`RULE D27` measured the upper edge of this band last wave without naming it.** Item 8 is the lower edge.
The difference between the two edges is the whole product story: **above the band there is a remedy
(`DetectionBinningResolver` recommends a factor, the user applies it, recall recovers). Below the band there
is none — you cannot bin fractionally, and the resolver's `Clamp` floors at 1 and says nothing.**

### 1.3 The bank's own generator already declared these three datasets outside the representable regime

```
dataset                  asec/px  seeing   hfrMin  hfrMinEff
D01_ultrawide_40mm        19.389    2.50   0.2307      0.700   <- FLOORED
D02_rich_135mm             5.745    2.50   0.2800      0.700   <- FLOORED
D03_redcat_250mm           3.102    2.50   0.5766      0.700   <- FLOORED
D04_esprit_550mm           1.410    2.50   1.0380      1.038
… 16 more, every one with hfrMin == hfrMinEffective
```

**Exactly three datasets are floored, and they are exactly the three with the recall gap. 3 of 3 and 17 of
17.** The renderer refuses to render a PSF below 0.7 px because it cannot; the detector faces the same wall
one stage later. This separation was sitting in `synthetic_meta.json` the whole time and no wave has quoted
it.

It also means something the wave must not lose: **`D01`, `D02` and `D03` are all rendered at the SAME
in-focus PSF (0.700 px) despite pixel scales of 19.4, 5.7 and 3.1 ″/px.** Their `recall@high` differs
(0.183 / 0.446 / 0.365). **So arcsec/px cannot be what separates them at focus** — which is a direct
refutation of the controller's ordering-by-pixel-scale, and is why §2.1 rejects that axis.

---

## 2. WHERE THE CONTROLLER'S FRAMING IS WRONG

### 2.1 "At 19.4″/px with 2.5″ seeing a star is sub-pixel, so ask whether the à trous layers can represent a source narrower than one pixel" — **the answer is YES, and the question is the wrong one**

Read from source. `CollectStarCandidates` has **no minimum size at all**:

```csharp
const float ZERO_THRESHOLD = 0.001f;
```
— `StarDetection/StarDetector.cs:1342`; region seeding at `:1356`/`:1370`, `candidates.Add(...)` at `:1425`

**A single surviving pixel produces a 1×1 candidate.** And the wavelet does not erase small sources — it
does the opposite. The à trous residual is a low-pass at ≈ `2^StructureLayers` px and is **subtracted**, so
*large* structures are removed and point sources survive. The shipped tooltip says exactly this:
*"At the default of 4 (2⁴=16), structures larger than 16 pixels in size are excluded"*
(`Resources/OptionsDataTemplates.xaml:444`).

**So the structure stage's representational capacity is not the limit. The limit is amplitude, and it is one
line further on.**

### 2.2 The real lower-scale cutoff is the **post-wavelet Gaussian blur**, and it is **welded to a knob that means the opposite thing**. This is the wave's finding

```csharp
// Step 5: Excluding large structures can cut off the outsides of large stars, or leave holes when far out
//         of focus. Blurring smooths this out well for structure detection
CvImageUtility.ConvolveGaussian(structureMap, structureMap, p.StructureLayers * 2 + 1);
```
— `StarDetection/StarDetector.cs:631-632`

with

```csharp
if (sigma <= 0.0) { sigma = 0.159758 * kernelSize; }
```
— `Utility/CvImageUtility.cs:80-82`

At the shipped `StructureLayers = 4` the kernel is 9×9 and **σ ≈ 1.44 px**. A 1-px delta is spread over
σ ≈ 1.44, so its **peak amplitude falls by roughly 1/(2πσ²) ≈ ×0.077** before it is compared against

```csharp
double binarizeThreshold = structureMapStats.Median + p.NoiseClippingMultiplier * noiseReducedImageNoise.Sigma;
```
— `StarDetector.cs:653`

**Three things follow, and all three are new:**

1. **The blur's stated purpose is to help LARGE and defocused stars** (the comment says so), **and its entire
   cost falls on small ones.** Its width is keyed to `StructureLayers`, a knob about the *upper* scale
   cutoff. One knob sets two opposing cutoffs.
2. **It uses raw `p.StructureLayers`, not `EffectiveStructureLayers(p)`.** The defocus/donut boost
   (`StarDetector.cs:1567-1578`) widens the *residual* and deliberately leaves the blur alone — so the
   codebase **already decouples them in one direction**, and there is precedent for decoupling the other.
3. **It explains [F43](followups.md)'s dead end.** F43 measured `StructureLayers 4 → 2` on `D01` as
   *"strictly worse"* and concluded the axis was useless. But lowering `StructureLayers` moves **both**
   cutoffs at once: it narrows the blur (helps a 1-px star) *and* makes the residual less smoothed, so
   `src − residual` retains less of the star (hurts more). **F43's result is confounded in source, and
   nobody has run the experiment that separates them.**

**This is the one genuinely un-worked mechanism in item 8, it is decidable from source at zero compute, and
it re-opens an axis the register had closed.**

### 2.3 "The optimizer drives sensitivity UP on wide fields" — **direction confirmed, gate set wrong**

Direction is right and worth recording, because the name reads backwards: `BrightnessSensitivity` is a
**threshold**, so **higher is LESS permissive**.

```csharp
var sensitivity = starCandidate.NormalizedBrightness / srcImageNoiseSigma;   // :1834
if (sensitivity <= p.Sensitivity) { … }                                      // :1851
```
— `StarDetector.cs`; corroborated at `IStarDetector.cs:338` (*"Smaller values increase sensitivity"*) and
`GateRecommender.cs:92` (`LowerToAdmit`).

**But the controller's table attributes the moderate-wide loss to `REJECTED:LowSensitivity` alone, and that
undercounts it.** Recomputing the shares from the same published blocks:

| | `NO CANDIDATE` | **tunable-gate block**† | of which `LowSensitivity` | `TooSmall` |
|---|---|---|---|---|
| `D01` (51 011 high-tier FN) | **84.3 %** | 11.1 % | 1.5 % | 9.5 % |
| `D02` (14 585) | 25.6 % | **64.6 %** | 40.6 % | 22.2 % |
| `D03` (2 937) | 6.7 % | **91.3 %** | 53.9 % | 35.1 % |

† `LowSensitivity + TooSmall + TooFlat + TooLowHFR` — the four gates the optimizer holds a knob for.

**`TooSmall` is the #2 gate on `D01` and `D03` and #3 on `D02`, and the controller's split does not mention
it.** `MinimumStarBoundingBoxSize` landed at **6** on `D01` and `D02` — the highest in the bank — on the two
rigs with the smallest stars, against a shipped default of 5 and a `WideField` preset that *lowers* it
(`StarDetectionOptions.cs:209-212`). **The two-mechanism split survives; the second mechanism is the
four-gate block, not one gate.**

Also worth correcting for the record: the shipped **`WideField` preset does not raise sensitivity**. It
lowers `StructureLayers` and `MinStarBoundingBoxSize` by one each. The high sensitivity is entirely the
optimizer's landing.

### 2.4 "This is [F83](followups.md)'s objective from the opposite end — work out what the high sensitivity IS being bought with" — **already answered, and the answer is `J`'s fit term**

[F83](followups.md) (Open, source-derived, zero compute) establishes that on an unlabelled run
`J = (0.55·sFocus + 0.20·sStars + 0.25·sFit)/(Σw)`, `SMarginalSnr` ships at `0.0`, and *"nothing charges for
a wrong detection"*. **The corollary the controller wants is the contrapositive and it is equally
mechanical: nothing charges for a MISSED detection either, beyond `sStars`' count term at weight 0.20.**
Raising sensitivity trades `sStars` (weight 0.20) against `sFocus` + `sFit` (weight 0.80) — and on a rig
whose marginal stars are HFR-noise, discarding them tightens the sequence and buys more than the count term
loses. **The optimizer is behaving correctly for the objective it was given.** No wave-28 measurement is
needed to establish that, and none is proposed.

**And the remedy the controller is circling is already fenced.** [F84](followups.md): *"any floor on the
Sensitivity axis alone"* is **KILLED** (clip-axis escape); [F23](followups.md)'s hard floor at 6 was measured
**worse than doing nothing**; `MarginalSnrStrength` is **FENCED** on void-calibrated evidence. **Wave 28
proposes nothing on the sensitivity axis.**

### 2.5 "The `NO CANDIDATE` half may be answerable from SOURCE at zero compute — if so, price it at zero" — **agreed, and taken further: TWO of the three halves are zero-compute**

Accepted without reservation, and extended: §2.2 (the welded cutoff) **and** §7 (the shipped message that is
factually false below the band) are both source findings at zero compute, and the blind empirical test
(§5, `RULE W28-B`) reads artifacts already on disk. **Wave 28's only compute is one ~8-minute arm.**

### 2.6 "Say which population carries the verdict and keep it distinct from what has been looked at" — **the bank cannot supply a blind wide-field DATASET, and this is a hard structural fact**

Ranked by arcsec/px, the three widest datasets in the bank are `D01` (19.389), `D02` (5.745) and `D03`
(3.102). **They are exactly the three the controller has read.** The fourth-widest is `D04` at 1.410 ″/px —
**a factor of 2.2 down from `D03` and 13.8× down from `D01`.**

> **There is no unread dataset anywhere in the bank in the regime where the gap lives. Any dataset-level
> verdict about wide-field recall is a verdict on rows that have been read.**

That kills the obvious rule shape. **The verdict is therefore relocated to the FRAME**, where a blind
population does exist: 20 datasets × 9 frames = 180 frames, of which the controller has read the aggregate
attribution for 5 datasets and this pre-registration has read the per-frame block for 3. **153 frames across
17 datasets remain unread, and every one carries a published per-frame recall and a published truth PSF
size.** §5 builds the rule there, and §6 states the blindness ledger exactly.

### 2.7 "Wave 27's `RULE D27` moved recall on 7 rows but `D01`/`D02`/`D03` are factor-1 and unaffected — confirm from the artifacts" — **CONFIRMED, and it is stronger than "unaffected"**

From `/mnt/d/hf_w27/d27_score_controller.txt`, verbatim: `manifest rows: 7`, and the seven are
`D08 D09 D10 D12 D14 D15 D17`. `D01`, `D02`, `D03` appear nowhere in the file. The controller is right that
the gap is unchanged.

**The stronger statement, which §1.2 derives:** `D27` is not merely *silent* about the wide-field rows, it is
**the same measurement from the other side of the same band**. Both are "recall recovers as in-focus PSF
approaches 2–4 detection pixels". `D27` could act because binning divides; item 8 cannot, because nothing
multiplies.

---

## 3. WHAT IS ACTUALLY OPEN, AFTER §1 AND §2

| # | open question | reachable how | cost |
|---|---|---|---|
| **(i)** | Is the lower-scale cutoff the **blur**, welded to `StructureLayers`? Is [F43](followups.md)'s "fewer layers is worse" a confound? | **source**, then one arm | **0** + arm |
| **(ii)** | Is the 2–4 px band **real out-of-sample**, or is it a story fitted to three read datasets? | 153 unread frames already on disk | **0** |
| **(iii)** | Does relieving the **binarization threshold** (`NoiseClippingMultiplier`) convert `NO CANDIDATE` into detections, as [F43](followups.md) measured on a *real* 19.4″/px rig but nobody has measured on the bank? | one no-build arm | **~8 min** |
| **(iv)** | Does the product **tell** a user their rig is below the band? | source | **0** |

Everything else in item 8 is either done (§1.1) or fenced (§2.4).

---

## 4. `RULE W28-S` — the welded cutoff, decided from source

**Verdict domain:** `S-WELDED` · `S-SEPARATE` · `S-UNEVALUATED`.

Three clauses, each a mechanical assertion against HEAD, each with both branches reachable (any future edit
flips them — which is the point of writing it as a rule rather than as prose):

| clause | assertion | FAIL end |
|---|---|---|
| `S-a` | `StarDetector.cs:632`'s blur width argument is `p.StructureLayers * 2 + 1`, i.e. **raw**, not `effectiveStructureLayers` | the argument is `effectiveStructureLayers` or a dedicated field |
| `S-b` | no member of `StarDetectorParams` controls the post-wavelet blur width independently of `StructureLayers` | such a member exists |
| `S-c` | `EffectiveStructureLayers(p)` reaches the **residual** (`:611`, `:619`, `:622`) and **not** the blur | it reaches both |

**F68's five parts.** POPULATION: the single artifact `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs`
at the wave's HEAD commit, recorded by sha256 in the scorer's output; FIELD: the literal argument
expressions at the three cited sites. STATISTIC: boolean per clause. AGGREGATION: conjunction — all three.
EMPTY: the file or a cited line not being present is `S-UNEVALUATED`, **never a pass**. WHICH COPY: there is
one `ConvolveGaussian` call in `BuildDetectionContextInternal`; the scorer asserts the **count of matching
call sites is exactly 1** and refuses on any other count, so a second copy cannot be silently read past.

**`S-WELDED` iff all three hold.** I expect it to hold — it holds at the commit I read. **Because I expect
it to hold, it is NOT a ship gate** (§9): a conjunct known-true at pre-registration is a one-clause rule
wearing two clauses, and the charter's discipline forbids exactly that.

---

## 5. `RULE W28-B` — the band, tested BLIND on 153 frames nobody has read, at zero compute

**This is the rule that carries the wave's verdict.** It takes the theory built on the three read datasets
and makes it predict the *sign* of a quantity on 14 datasets whose per-frame numbers this document has not
opened — and the prediction **flips sign** across the band, which no trivial confound produces.

### 5.1 The prediction

The three read datasets sit **below** the band and their recall **rises with defocus** — worst at best
focus, because that is where the PSF is smallest:

| | in-focus | ±1 | ±2 | ±3 | ±4 |
|---|---|---|---|---|---|
| `D01` per-frame recall | **0.001** | 0.049 | 0.211 | 0.238 | 0.203 |
| `D02` | **0.006** | 0.220 | **0.791** | 0.456 | 0.228 |
| `D03` | **0.142** | 0.237 | 0.280 | 0.252 | 0.231 |

Datasets **above** the band must do the **opposite**: their in-focus PSF is already at or past 3 px, so
defocus takes them *further* out of the band and recall must be **highest at focus**.

### 5.2 The statistics — two routes, and the second removes the reference detector entirely

For each dataset `d`, with `f*` = the frame of minimum `|defocusMicrons|` and `f_lo`/`f_hi` the two extreme
frames:

```
Δrecall(d) = R(d,f*) − ½·( R(d,f_lo) + R(d,f_hi) )        route 1: golden-denominated
Δacc(d)    = A(d,f*) / max(1, ½·( A(d,f_lo) + A(d,f_hi) )) − 1     route 2: DETECTOR ONLY
```

**Route 2 exists because route 1 has a named confound and route 2 does not.** The golden's per-frame
denominator is itself PSF-dependent — [F31](followups.md) established that the reference tiers by
**peak-pixel** SNR, so it *"evaporates toward the sweep wings"*, and on `D01` the golden runs 3 500 at the
wings against 18 846 at focus. **`Δrecall` therefore mixes detector behaviour with reference behaviour.**
`Δacc` reads the `acc` column — accepted-star count, pure detector output, no golden involved — and is
immune to it. **Both are pre-registered; the verdict requires them to AGREE IN SIGN.** A disagreement is a
finding, not something to average.

### 5.3 The classes and the bars

Class from `K(d) = max(truthModel.hfrMin, truthModel.hfrMinEffective)`, the in-focus kernel HFR in captured
pixels — and `table18` was scored at detection binning **1** on all 20 rows, so `K(d)` is what the detector saw.

| class | condition | prediction | blind members |
|---|---|---|---|
| **LOW** | `K < 1.5` | `Δ < 0` (focus is the WORST frame) | `D04`, `D16`, `D18`, `D20` — **4** |
| **MID** | `1.5 ≤ K < 2.5` | **none** — reported, excluded from the verdict | `D05`, `D06`, `D19` — 3 |
| **HIGH** | `K ≥ 2.5` | `Δ > 0` (focus is the BEST frame) | `D07`–`D15`, `D17` — **10** |

`D01`/`D02`/`D03` are **LOW** and are **excluded from the verdict population** (§6) — they are what the
prediction was built on.

| clause | bar | reachable FAIL |
|---|---|---|
| `B-1` | `sign(Δrecall) == sign(Δacc)` on **≥ 13 of 14** | the two routes disagree on ≥ 2 |
| `B-2` | prediction correct on **≥ 12 of 14** predicted datasets, **and ≥ 1 correct in each of LOW and HIGH** | 3+ wrong, or a class entirely wrong |
| `B-3` | **all 4 LOW datasets have `Δacc < 0`** | any LOW dataset detects more stars at focus than in its wings — which falsifies the lower edge on data this document has not read |

**`W28-B` = `B-BANDPASS`** iff `B-1` ∧ `B-2` ∧ `B-3`. **`B-REFUTED`** if `B-2` or `B-3` fails.
**`B-SPLIT`** if `B-1` fails — the two routes disagree, and then neither is quoted.

**Why the FAIL end is genuinely reachable, stated before the data:** the sign flip is a *strong* claim.
`B-3` in particular is one dataset away from failing at any time, and `D04`/`D16`/`D18`/`D20` sit at
`K ≈ 1.04–1.08` — only 1.4× below the LOW boundary and **2× above** the 0.700 the prediction was built on.
If the lower edge is sharper than 1.5 px, `B-3` fails and the wave says the band is narrower than claimed.

### 5.4 F68's five parts, per clause

| part | `B-1` / `B-2` / `B-3` |
|---|---|
| **POPULATION** (artifact) | `/mnt/d/hf_w25/table18/<dataset>.log`, the `PER-FRAME (sorted by focuser)` block; and `/mnt/d/SyntheticAutofocusBank/<dataset>/synthetic_meta.json`, `truthModel`. Both **read-only**; wave 28 writes nothing under either root |
| **POPULATION** (membership) | the 17 datasets excluding `D01`/`D02`/`D03`; **computed** as `set(all) − set(EXCLUDED)` with `EXCLUDED` a named constant, never typed as a list of 17 |
| **STATISTIC** | `Δrecall` from the `recall` column; `Δacc` from the `acc` column; `K` from `max(hfrMin, hfrMinEffective)` |
| **AGGREGATION** | count of sign-matches over the 14 predicted datasets; MID's 3 are computed and printed, and **excluded by name from every denominator** |
| **EMPTY** | a dataset whose log has no `PER-FRAME` block, or fewer than 3 frames, or no `truthModel.perFrame`, is **`CNL-NOFRAMES`**: it leaves the denominator, which prints as `k of 14` with **k computed**, and the wave states which datasets left. A verdict on `k < 10` is **`B-UNDERPOWERED`**, not a pass |
| **FIELD — which copy** | **The `recall` column of the `PER-FRAME` block is ALL-TIER.** The log also prints `recall@high` — but only in `OVERALL`, never per frame. `Δrecall` is therefore **not** a statement about `recall@high`, and the wave must not write it as one. `Δacc` has no tier at all. The scorer asserts the header line reads exactly `focuser  dsteps  golden  acc   TP   FP   FN   prec   recall   f1` before parsing a single row, and refuses otherwise |

### 5.5 What `B-BANDPASS` would and would not establish

**Would:** that the detector's recall is band-pass in in-focus PSF size with the band where
`DetectionBinningResolver` says it is, established out-of-sample with a sign flip; and that item 8 and
`RULE D27` are one phenomenon.

**Would not:** anything about *why* the lower edge is where it is (that is `W28-S`, source), anything about
whether it is fixable (that is `W28-N`), and anything at all about **real** frames — [F35](followups.md)'s
standing caveat applies unchanged: *"its real-bank population on this bank is **empty** … the user-facing
benefit is **unproven on real frames**. Do not claim otherwise."*

---

## 6. BLINDNESS LEDGER — stated once, exactly, before any wave-28 statistic

**READ (and therefore excluded from every wave-28 verdict population):**

| what | by whom | scope of the burn |
|---|---|---|
| aggregate high-tier FN attribution, `D01` `D02` `D03` `D07` `D18` | the controller, in the invoking brief | those 5 datasets, that block |
| full `golden eval` block incl. `PER-FRAME`, `D01` `D02` `D03` | **this document**, §1/§2/§5.1 | those 3 datasets, all fields |
| `truthModel` (pixel scale, `hfrMin`, `hfrMinEffective`, per-frame kernel HFR) — **all 20 datasets** | **this document**, §1.2/§1.3 | **none** — see below |
| `d27_score_controller.txt` in full | this document, §2.7 | the 7 `D27` datasets, that file |

**Why reading the truth model on all 20 costs no blindness:** it is **generator configuration**, fixed
before any detector ran, and it is the rule's **x-axis**. The rule's y-axis — per-frame `recall` and `acc` —
is unread on 17 datasets. Reading the independent variable is how a population is *stratified*; reading the
dependent variable is how a rule is *pre-empted*. **The wave must not blur these, and this table exists so a
later reader can check that it did not.**

**BLIND, and carrying the verdict: the `PER-FRAME` blocks of the 17 datasets other than `D01`/`D02`/`D03` —
153 frames.** `D07` and `D18` are burned for the aggregate attribution block and blind for `PER-FRAME`;
`W28-B` reads only `PER-FRAME`, so they stay in. **This distinction is load-bearing and is asserted in the
scorer**, which refuses to read any `OVERALL` or `ATTRIBUTION` line from the 17.

---

## 7. THE PRODUCT DEFECT, found in source at zero compute, and it needs no wave-28 measurement

Take a rig below the band — `D01` at an in-focus HFR of 0.7 px — and walk
`DetectionBinningResolver` as it ships:

```
RecommendFromHfr(0.7)            = Clamp(round(0.7 / 3.0)) = Clamp(0) = 1
DiffersFromRecommendation(1,0.7) = (1 != 1) = false
ShouldShowRecommendation(1,0.7)  = !IsUsableHfr(0.7) || false = FALSE
```

**So the recommendation row is not shown at all.** And if the user finds the tooltip anyway,
`DescribeRecommendationDetail` takes its `recommended > 1 ? … : …` else-branch
(`DetectionBinningResolver.cs:100-104`) and tells them:

> *"Star detection is calibrated for in-focus stars of roughly 2 to 4 px, and **0.7 px is already in that
> range**, so binning is not needed."*

**0.7 px is not in the 2–4 px range. The product states the band correctly and then asserts a value 4.3×
below it satisfies the band.** The clamp is one-sided — `Clamp` floors at 1 — so *every* under-sampled rig
takes this branch and is told it is in range.

**This is not a candidate finding awaiting a measurement.** It is a shipped false statement, provable by
arithmetic on 8 lines of source, on the exact class of rig item 8 is about. It is a ~15-line fix with a unit
test and no behaviour change.

**Related, and deliberately NOT bundled:** the machinery to say the true thing already exists but is
**wizard-only** — `HasUndersampledStars` (`StarDetectionOptimizerWizardVM.cs:237-238`) with its text at
`:2330-2343`, and `MinHfrSeed.IsBelowGate` (`MinHfrSeed.cs:113-120`). **A plain autofocus run is silent.**
Surfacing it outside the wizard is a larger change and is gated in §9.

---

## 8. `RULE W28-N` — the one arm: does relieving the binarization threshold recover the structure gap?

**The only measurement in the wave, and it can kill the wave's own proposal.**

[F43](followups.md) found that on the reporter's **real** 61 MP 19.4″/px rig *"the binding gate is
`NoiseClippingMultiplier`, not `Sensitivity`"* — `NC → 1.0` took a starless in-focus frame to 3 511 stars.
**Nobody has run that on the bank, and `NC` is precisely the bar the blurred sub-pixel star must clear
(`StarDetector.cs:653`).** If §2.2's amplitude account is right, lowering `NC` must convert
`NO CANDIDATE` into detections; if it does not, the loss is upstream in the residual subtraction and §2.2 is
wrong.

**No binary.** `golden eval --params optimized --opt-results <dir>` reads each dataset's
`optimized_settings.json`; the arm copies the seedA0 tree into `/mnt/d/hf_w28/` and edits **one field**.
`table18`'s tree and every bank sidecar are read-only.

**Arm:** `D01`, `D02`, `D03` × `NC ∈ {landed, 2.0, 1.0}` = **9 cells**. `NC` landed at 3.8125 on `D01`.

| clause | statistic | bar | FAIL end |
|---|---|---|---|
| `N-1` **the edit took effect** | the `key detector knobs:` line each log echoes, field `NoiseClip=` | prints the intended value on **9 of 9** | run one cell from an **unedited** copy and confirm it echoes the landed value — a positive control, on a real cell, not a fixture |
| `N-2` **the mechanism** | high-tier `NO CANDIDATE (structure gap)` count | at `NC = 1.0`, falls by **≥ 25 %** vs the landed `NC`, on **3 of 3** | it does not fall ⇒ §2.2 is **refuted**, the loss is in the residual, and the wave says so |
| `N-3` **the cost** | `precision` from `OVERALL` | **≥ 0.90 on 3 of 3** at `NC = 1.0` | precision collapses ⇒ `NC` is not a remedy, and the wave reports it as a dead end **against its own proposal** |

**`W28-N` = `N-RECOVERS`** iff `N-1` ∧ `N-2` ∧ `N-3`. **`N-REFUTED`** if `N-2` fails.
**`N-COSTED`** if `N-2` holds and `N-3` fails — *the mechanism is real and the lever is unusable*, which is a
different and more useful result than either pass or fail.

**F68's five parts.** POPULATION: 9 `golden_eval.txt` under `/mnt/d/hf_w28/nc/<dataset>_nc<v>/`; FIELD: for
`N-2` the count on the `NO CANDIDATE (structure gap)` line **inside the `HIGH TIER ONLY` block** — the log
prints that identical line twice, once per block, and **the aggregate copy is the wrong one**
(`GoldenEvalRunner.cs:763` vs `:774`); the scorer anchors on the block header and asserts it consumed
exactly one. STATISTIC/AGGREGATION: per-dataset ratio, counted over 3. EMPTY: a missing or truncated
`golden_eval.txt` is `CNL-NOLOG`, leaves the denominator, `k of 3` with k computed; `k < 3` ⇒
`N-UNDERPOWERED`, never a pass.

**Price.** `golden eval` is **6–46 s per 9-frame cell** and a full 20-dataset arm was **349 s**
(wave 27's measured rate; the charter's §5 budget table still has **no `golden eval` row** — [F21](followups.md)).
`D01` is the bank's largest golden (62 411 high-tier stars) and prices at the top of that range. **9 cells ≈
5–8 minutes**, plus the tree copy and the scorer.

**Deliberately NOT in this arm:** a `StructureLayers` sweep. It is the confounded axis (§2.2) and sweeping it
without a build would re-measure [F43](followups.md)'s confound and re-learn its wrong lesson. **The
decoupling experiment needs a build that separates the blur width from the layer count, and it is priced and
deferred in §11.**

---

## 9. WHAT SHIPS, AND THE SHIP RULE — fixed here, before the data

| # | change | ship rule | both branches reachable? |
|---|---|---|---|
| **(1)** | **Fix the false in-band statement** (§7): `DescribeRecommendationDetail` must not assert a sub-band HFR "is already in that range", and `ShouldShowRecommendation` must return true when the measurement is **below** the band | **UNCONDITIONAL** — charter §1a's *"ship unconditionally where the ship is not what the rule decides"*. Its correctness is arithmetic on shipped source and depends on **no** wave-28 statistic | n/a by design, and **said so here rather than dressed as a gate** |
| **(2)** | **Surface the below-band condition outside the wizard** | **GATED on `W28-B` = `B-BANDPASS`.** If `B-REFUTED` or `B-SPLIT` or `B-UNDERPOWERED`, it does **not** ship and is recorded as a costed recommendation | **YES** — `B-2`/`B-3` are one dataset from failing, on 14 datasets this document has not read |
| **(3)** | anything on the `Sensitivity` axis | **DOES NOT SHIP.** Fenced by [F84](followups.md)/[F23](followups.md) (§2.4) | n/a — not proposed |
| **(4)** | decoupling the blur from `StructureLayers` | **DOES NOT SHIP IN WAVE 28.** A detector change of this reach owes a fresh 42 m `optimize` baseline and a re-derivation. §11 | n/a — deferred by price, not by verdict |

**Change (1) is why this wave is worth running even if every rule fails**: a user below the band is currently
shown nothing, and told they are in range if they look.

**Suite discipline.** Baseline **3973**, verified **by COUNT out of the log**, never by the tick
([F37](followups.md)). Change (1) adds unit tests for `DescribeRecommendationDetail` and
`ShouldShowRecommendation` **below**, **inside** and **above** the band — three cases, so the below-band case
cannot pass by accident. `dotnet test <sln>` does **not** build `TestApp`; it is built separately or a
compile break never surfaces.

---

## 10. PRICE, PER HALF, AND SCORED AGAINST THE OWNER'S THREE GOALS

| half | what | binary? | compute | wall | goals |
|---|---|---|---|---|---|
| **(S)** the welded cutoff | `RULE W28-S`, source | no | **0** | 30 m | **1** ✓✓ |
| **(B)** the band, blind | `RULE W28-B`, 153 unread frames on disk | no | **~1 min** (parse) | 75 m (scorer + self-test) | **1** ✓✓✓ |
| **(N)** the `NC` arm | `RULE W28-N`, 9 cells | **no** | **5–8 min** | 60 m | **1** ✓✓ |
| **(D)** ship (1), the false statement | product + 3 unit tests | build | 0 | 60 m + suite ~4 m | **1** ✓✓, **3** ✓ |
| **(R)** the record | design + results + register + results-table correction | no | 0 | 45 m | — |

**Total ≈ 4.5 h wall, ≈ 10 minutes of compute**, inside the charter's ~6 h. **The wave is 98 % analysis and
2 % measurement, and that is the correct shape for an item whose evidence was already on disk.**

**Goal scoring.** Every half scores on **goal 1** (*works across a wide range of setups, bridging detected
gaps*) — this is the bank's widest-setup failure. (D) also scores **goal 3** (*avoiding parameters pinned to
extreme values*) obliquely: it is the first product surface that tells a user their rig is outside the
regime the knobs were calibrated for. **Nothing here scores goal 2.**

---

## 11. IS `D01` A LEGITIMATE CONFIGURATION? — the controller's question, answered, and the answer is not the one asked for

The controller asks whether the honest answer is *"this rig is outside the detector's sampling regime and
the product should SAY so"*, and offers it as an alternative to a recall fix. **Three corrections:**

1. **The product does not need to decide this — it has already decided it, in `DetectionBinningResolver`'s
   2–4 px band, and then failed to apply its own decision below the band (§7).** The question is not "should
   we state a supported range"; the range is stated, in shipped user-facing prose. The question is "why is
   it enforced on one side only", and the answer is a one-sided `Clamp`.
2. **`D01` is legitimate as a *rig* and illegitimate as a *demand*.** A 40 mm lens at 19.4″/px is an
   ordinary wide-field imaging setup and HocusFocus should focus it. But item 8's *statistic* asks the
   detector to find 18 846 stars in a frame where each is one pixel across, and **finding them would not
   help**: [F22](followups.md) measures the pixelization floor — below ~1.1 px, HFR **over-reads by up to
   +234 %**, and `D01`'s 0.231 px optical vertex measures 0.77 px. **Stars recovered below the band carry
   HFR that cannot resolve the V-curve's vertex.** Recovering them raises `recall@high` and does not
   necessarily improve focus.
3. **And the bank already says autofocus on `D01` works**: `BestJ` **0.994825** from a `BaselineJ` of
   `0.000000`, `σ` 0.1514 from 0.6145 — the largest baseline-to-best move in the table. **So `recall@high =
   0.183` and a working autofocus coexist on this rig.** The gap is real and it is **not, on this evidence,
   costing the owner goal 1 on `D01`.** Where it demonstrably *does* cost something is
   [F82](followups.md) — `D01`'s step-size recommendation **STALLED at 3 against a truth of 9** — and that
   is goal **2**, is open, is priced at ~45 m + ~10 m, and **is a better-evidenced product gap than item 8
   is.** Wave 28 does not take it, and says so here so the next wave can.

**So: score both against the goals, as asked.** A recall fix on the structure stage scores goal 1 on a
dataset whose autofocus already works, costs a detector change and a 42 m re-baseline, and cannot improve
HFR quality below the pixelization floor. **A diagnostic scores goal 1 on every under-sampled rig, costs 15
lines, and is the difference between a user seeing nothing and a user seeing the reason.** The diagnostic
wins on this evidence, and §9(1) ships it.

**Deferred, priced, not run:** the blur/layer decoupling build — **~45 m code, ~10 m arm, and a fresh 42 m
`optimize` baseline plus re-derivation** if it were ever to ship. Wave 28 measures whether it is worth that
(via `W28-N`) and does not spend it.

---

## 12. INSTRUMENTS AND CONSTRAINTS

**Location** `/mnt/d/hf_w28/`, **LF endings**, derived from `/mnt/d/hf_w27/`.

**Blocking pre-flight: `verify_derivation_w28.py`**, derived from `verify_derivation_w27.py`. **Carry
forward [F87](followups.md)'s repair and do not regress it** — `in_historical_block()` must not treat the
**definition line of `HISTORICAL_BLOCKS`** as opening a historical literal, which is what switched `V27-B`
off from that line to EOF. **Self-test clause `[6]` is carried forward verbatim in intent**: a real
historical block is exempt, the file after it is not, and the definition line opens nothing.
**BOTH pinned FAIL fixtures are kept** — `/mnt/d/hf_w24` **and** `/mnt/d/hf_w26` — neither is "whatever came
before" ([F80](followups.md)). Token matching by **SHAPE, not enumeration**, both affixes, `_` is a word
character.

**Standing rules, applied without exception:**

- any scorer taking `--out` **refuses without it** (rc 2, nothing written)
- `--self-test` is **never dispatched from a sourced file**; every self-test prints `SELFTEST <RULE> <n> of <n>`
  and its output is **redirected to a file** — a self-test nobody captured exists only for its session
- **populations asserted inside the file**; **ordinals computed, never typed**
- paths through a **manifest**, never a template; `-print0 | sort -z | xargs -0`
- [F88](followups.md): any provenance diff takes the **union** of `git diff --name-only` and
  `git ls-files --others --exclude-standard`
- [F89](followups.md): `touch` after every restore
- [F90](followups.md): **reuse the computation, never the banner** — every scorer takes its rule label as an
  **argument** and asserts it against the manifest it was handed
- [F91](followups.md): a BEFORE/AFTER control builds **both** sweeps from **ONE expression** — one function,
  called twice, population sizes asserted equal before any hash is compared
- §8.1's verdict-tree gap: **every verdict tree in this wave is checked for total coverage of its outcome
  space at pre-registration, and prints `<RULE>-TREE-GAP` rather than papering over an uncovered region**

**Arm hygiene:** one `TestApp.exe` at a time; no NINA during a pinned arm; scorers take **WSL paths**;
`TestApp.exe` gets `< /dev/null`; logs stay ASCII; `--profile-id` pinned on the arm and its inertness stated
with its `n`, not assumed. `/mnt/d/hf_w25/table18/` and `/mnt/d/SyntheticAutofocusBank/` are **read-only**
and a fingerprint class over both is taken BEFORE and AFTER **from one expression**.

---

## 13. WHAT WAVE 28 WILL NOT RUN, AND WHAT IT COSTS

| not run | price | why |
|---|---|---|
| the blur/layer **decoupling build** | ~45 m + ~10 m arm (+42 m baseline to ship) | §11. `W28-N` decides whether it is worth it |
| any `StructureLayers` sweep without that build | ~5 m | confounded in source (§2.2); would re-learn [F43](followups.md)'s wrong lesson |
| [F82](followups.md) — `D01`'s stalled step recommendation | ~45 m + ~10 m | **better-evidenced than item 8** and scores goal 2; named for wave 29 (§11.3) |
| new datasets rendered between 1.4 and 19.4 ″/px | unpriced, ≥ 1 h render | the only way to get a **blind wide-field dataset** (§2.6). `W28-B`'s frame-level route makes it unnecessary for *this* verdict; it stays the honest fix for the bank's coverage hole |
| [F83](followups.md)'s `P-D08` at n = 3 (`D01` + `D16` at raised sensitivity) | **~2 min** | *"the cheapest decision-moving measurement left in the register"* — genuinely cheap, but it is a **precision** question on a fenced axis and wave 28 is a recall wave. **Named so it is not lost** |
| wave 27's open controls (`S27-1`, three mutant records) | ~20 m | instrument debt, scores zero on all three goals; §12 of the wave-27 results prices them |
| anything on real frames | — | the bank has **no** real under-sampled dataset ([F35](followups.md), [F43](followups.md)). **Every wave-28 conclusion is synthetic-only and must be written that way** |
