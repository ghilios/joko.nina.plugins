# Wave 28 — results

## THE TIMESTAMP — the roll call, taken at an instant

```
$ ls -la --time-style=full-iso /mnt/d/hf_w28/
drwxrwxrwx  ...  2026-08-13 11:47:49.495626200 -0400 .
   returned at 2026-08-13T11:51:02-04:00  (= 2026-08-13T15:51:02Z)
```

The most recent artifact at that instant is `fp_AFTER.txt` (`11:50:17`, 45 seconds earlier); the newest before
it is `w28n_score.txt` (`11:47:05`). No `TestApp.exe` was running — the arm printed `W28N_ARM_END` at
`15:46:58Z` — and nothing was mid-write.

| row | state at **15:51:02Z** | artifact |
|---|---|---|
| `RULE W28-S` | **CLOSED** 11:35:00; self-test captured 11:37:24 | `w28s_score.txt`, `w28s_selftest.txt` |
| `RULE W28-B` | **CLOSED** 11:39:23; manifest 11:39:14, interlock written | `w28b_score.txt`, `w28b_manifest.tsv`, `W28B_MANIFEST_READY` |
| `RULE W28-N` | **CLOSED** 11:47:05; arm ended 11:46:58, 10 cells all `exit=0` | `w28n_score.txt`, `w28n_manifest.tsv`, `w28n_arm.log` |
| **ship (1)** | **CLOSED** — committed `6f2b62b`, suite log 11:38:20 | `suite_AFTER_ship1.log`, `suite_w28.marker` |
| `RULE V28` (pre-flight) | **CLOSED** 11:06:36, self-test 11:07:42, both FAIL fixtures 11:07:56 / 11:08:04 — **and its verdict is `V-UNEVALUATED`, not the plan's `V-CLEAN`** (§7.3) | `vdrift_w28.txt`, `vdrift_selftest_w28.txt`, `vdrift_failend_B.txt`, `vdrift_failend_C.txt` |
| `W28-FP` fingerprint | **BOTH SWEEPS CLOSED** — BEFORE 11:13:39, AFTER 11:50:17. **The verdict LINE was never captured to a file** and is recomputed in this document from the two sweeps (§6.2) | `fp_BEFORE.txt`, `fp_AFTER.txt` |
| ship (1)'s **mutant records** (`M1`–`M6`) | **NO ARTIFACT** — nothing under `/mnt/d/hf_w28/` records a mutant turning a test red; the claim is in the commit message only (§7.6) | — |
| `fp_w28.sh` **self-test** capture | **NO ARTIFACT** — `SELFTEST W28FP` appears in no file in the root (§7.7) | — |
| ship (2) | **NOT OWED** — its gate (`W28-B` = `B-BANDPASS`) did not hold, and it did not ship (§5.2) | — |

**Unlike wave 27 at its first timestamp, every scorer's self-test is on disk**: `SELFTEST W28S 35 of 35`,
`SELFTEST W28B 46 of 46`, `SELFTEST W28N 38 of 38`, plus `SELFTEST W28B-MANIFEST 11 of 11` and
`SELFTEST W28N-ARM 18 of 18`. Two smaller captures are missing and are named above rather than assumed.

**Vessel.** Wave 28 ran on `ghilios/synthetic-af-bank-followups-wave27` and its two commits (`37a2a06`
pre-registration, `6f2b62b` ship (1)) sit on **PR #196**, verified `OPEN MERGEABLE` on
`ghilios/synthetic-af-bank-followups-wave27` at the time of writing. **Design §0 pre-registered a fresh
`…wave28` branch and #196 staying on its own** — the wave did the opposite. Recorded as a controller deviation
in §13, not laundered.

---

## §0 — What this document is allowed to say

Every number below is quoted from an artifact under `/mnt/d/hf_w28/`, from `/mnt/d/hf_w25/table18/`, or from a
file in the repository, and each is cited. The pre-registered rules in
`docs/synthetic-af-bank-followups-wave28-design.md` are **applied as written**. Where a rule turned out to be
unsatisfiable, under-specified or gap-ridden, that is reported as a **finding** (§7, §8) — it is not repaired,
and no repaired version is scored. Wave 28 continues wave 27's branch and PR; this document is its fifth section
in substance and its own file in form.

**Everything below is synthetic-only.** The bank has no real under-sampled dataset
([F35](followups.md), [F43](followups.md)); design §13 says every wave-28 conclusion must be written that way,
and it is.

**Verdicts, in one table.**

| rule | verdict | population | artifact |
|---|---|---|---|
| **`RULE W28-S`** | **`S-WELDED`** — 3 of 3 clauses | 2 source files at HEAD, by sha256 | `w28s_score.txt` |
| **`RULE W28-B`** | **`B-SPLIT`** — and the band-pass hypothesis is **REFUTED on the half item 8 is about** | 14 predicted datasets of 17, `k = 14 of 14` | `w28b_score.txt` |
| **`RULE W28-N`** | **`N-RECOVERS`** — `k = 3 of 3` | 9 arm cells + 1 control | `w28n_score.txt` |
| **`W28-FP`** | **`PRESERVED`** — 1 080 of 1 080 byte-identical, 0 moved | 440 `table18` + 640 bank files | `fp_BEFORE.txt` / `fp_AFTER.txt` |
| **`RULE V28`** | **`V-UNEVALUATED`** over a root of **one file**; self-test **PASS**; both FAIL fixtures evidenced | 1 file checked | `vdrift_w28.txt` |
| **ship (1)** | **SHIPPED**, unconditional | suite **3997** by COUNT, `SUITE_EXIT=0` | `suite_AFTER_ship1.log` |
| ship (2) | **DID NOT SHIP** — gated on `B-BANDPASS`, which did not hold | — | — |

---

## §1 — THE HEADLINE: the same intervention splits the two regimes the design split, and one of them does not recover

`RULE W28-N` lowered a single field — `NoiseClippingMultiplier`, the binarization threshold at
`StarDetector.cs:653` that a blurred sub-pixel star must clear — on the three datasets the generator itself
flagged as un-renderable below 0.7 px. **The structure gap collapses on all three. The recall does not follow on
all three.**

| dataset | `K` px | `recall@high` landed → NC 2.0 → **NC 1.0** | `recall@all` landed → **NC 1.0** | high-tier `NO CANDIDATE` landed → **NC 1.0** | precision at 1.0 |
|---|---|---|---|---|---|
| `D01_ultrawide_40mm` | 0.700 | 0.183 → 0.189 → **0.195** | 0.123 → **0.183** | 42 991 → **8 931** (**−79.2 %**) | **1.000** |
| `D02_rich_135mm` | 0.700 | 0.446 → 0.523 → **0.687** | 0.377 → **0.598** | 3 732 → **1 071** (**−71.3 %**) | **1.000** |
| `D03_redcat_250mm` | 0.700 | 0.365 → 0.389 → **0.549** | 0.238 → **0.364** | 197 → **7** (**−96.4 %**) | **1.000** |

— read off `nc/<dataset>_nc<v>/attempt01/golden_eval.txt`; counts in parentheses are the
`FALSE-NEGATIVE ATTRIBUTION, HIGH TIER ONLY` block, never the aggregate copy.

**`D02` gains +0.241 of `recall@high` and `D03` +0.184 — a quarter and a fifth of the scale — at no measured
precision cost. `D01` does not.** Its `NO CANDIDATE` count falls by 79.2 % — 34 060 high-tier golden stars that
now form candidates where they formed none before — and its `recall@high` moves **+0.012**, from 11 400 to
12 162 of 62 411.

### 1.1 Where `D01`'s loss actually lives, and the sentence this wave is not allowed to write

The high-tier attribution block accounts for the missing 34 060 exactly:

| high-tier FN, `D01` | landed (`NC` 3.8125) | **`NC` 1.0** | Δ |
|---|---|---|---|
| `NO CANDIDATE (structure gap)` | 42 991 | 8 931 | **−34 060** |
| `REJECTED:TooDistorted` | 1 010 | 14 876 | **+13 866** |
| `REJECTED:LowSensitivity` | 786 | 13 612 | **+12 826** |
| `REJECTED:TooSmall` | 4 857 | 11 372 | **+6 515** |
| `ACCEPTED-elsewhere` | 462 | 701 | +239 |
| `REJECTED:OnBorder` | 17 | 77 | +60 |
| `REJECTED:Contaminated` | 692 | 533 | −159 |
| `REJECTED:NotCentered` | 196 | 147 | −49 |
| **total high-tier FN** | **51 011** | **50 249** | **−762** |

**33 298 of the 34 060 newly formed high-tier candidates — 97.8 % — were re-rejected by a later gate. 762
became detections.** `D01`'s attribution profile inverts: `NO CANDIDATE` falls from **84.3 %** of its high-tier
loss to **17.8 %**, while the three gates that moved — `TooSmall` + `TooDistorted` + `LowSensitivity` — rise from
**13.0 %** (6 653 of 51 011) to **79.3 %** (39 860 of 50 249). **At `NC` 1.0 `D01` looks like `D02` and `D03` did
at their landed settings** (design §2.3: tunable-gate block 64.6 % and 91.3 %).

**So the mechanism §2.2 proposed is real and it is not the whole story on the extreme rig.** The amplitude
account is confirmed — relieve the binarization threshold and the candidates form — and on `D01` the loss simply
moves one stage downstream. Its recovered stars are also mostly not high-tier: of `D01`'s **+6 552** total
true positives, **+762** are high tier and **+5 729** are medium tier.

**This wave did NOT localise `D01`'s residual loss, and cannot from these artifacts.** The gates fire in
sequence — `TooSmall` (`StarDetector.cs:1757`) → `OnBorder` (`:1766`) → `TooDistorted` (`:1807`) → `Degenerate`
(`:1823`) → `LowSensitivity` (`:1854`) — so the attribution is **first-rejection-wins** and the three counts are
an ordering, not independent shares. 11 372 stars never reached the distortion test at all. Identifying which
gate binds needs an arm that opens them one at a time; it is priced in §12 and it is **not** claimed here.

**And [F22](followups.md) still stands over the whole question:** below ~1.1 px, measured HFR over-reads by
+26 % to +234 %, and `D01`'s 0.231 px optical vertex measures 0.77 px. **Stars recovered below the band carry an
HFR that cannot resolve the V-curve's vertex.** Raising `D01`'s `recall@high` is not the same as improving its
focus, and the bank already reports `BestJ` 0.994825 on `D01` from a `BaselineJ` of 0.000000.

### 1.2 The two regimes are now measured to behave differently under the same intervention

That is the wave's most important result and it is worth stating plainly. `D02` and `D03` are the case where the
lever works: on `D02` the same edit relieves the downstream gates as well as the formation stage
(`LowSensitivity` 5 916 → 2 925, `TooSmall` 3 235 → 1 971), and on `D03` `TooSmall` falls 1 032 → 94. Lowering
the threshold there produces *better-formed* regions, not merely more of them. On `D01` the same edit produces
regions that fail the size gate 2.3× more often than before. **One knob, one direction, two outcomes — and the
covariate that separates them is not `K`, because all three datasets are rendered at `K = 0.700`.**

---

## §2 — `RULE W28-S` = **`S-WELDED`**, 3 of 3, at zero compute

> `>>> RULE W28-S = S-WELDED (3 of 3 clauses hold: ['S-a', 'S-b', 'S-c'])` — `w28s_score.txt`

| artifact | sha256 |
|---|---|
| `StarDetection/StarDetector.cs` | `1e20277797c1be05babab7365cff6a5a1d45a0637bff3f4e893715949663cce7` |
| `Interfaces/IStarDetector.cs` | `5304ed5f42a400593b06efcd132be45865406ae2f6ed5586089c4f28b153b910` |

Both hashes still hold at `HEAD 6f2b62b` — ship (1) touched neither file — so the verdict is current, not merely
current as of the scoring run.

| clause | measured |
|---|---|
| `S-a` | the blur width argument at line 632 is literally **`p.StructureLayers * 2 + 1`** — raw, not `effectiveStructureLayers` |
| `S-b` | (i) the only `StarDetectorParams` member reachable from that argument is `StructureLayers`; (ii) of the class's **55** properties, **none** matches the declared blur-width name pattern `(?i)(blur\|gauss\|convolve\|smooth\|kernel)` |
| `S-c` | `EffectiveStructureLayers(p)` binds at 611 and is used at 611/619/622, reaching the **residual** at 619 and 622 and appearing **0 times** inside the blur call's argument list; 0 unaccounted use sites |

Verdict tree: **27 regions enumerated, 0 uncovered.**

**What it means, and it is the finding the design promised at zero compute:** at the shipped
`StructureLayers = 4` the post-wavelet kernel is 9×9 and σ ≈ 1.44 px, so a 1-px delta loses roughly
`1/(2πσ²) ≈ ×0.077` of its peak before it is compared against the binarization threshold — and the width of that
blur is set by the knob that controls the *upper* scale cutoff. **One knob sets two opposing cutoffs.** The
defocus/donut boost already widens the residual and deliberately leaves the blur alone
(`StarDetector.cs:1567-1578`), so the codebase decouples them in one direction and has never decoupled them in
the other.

**Per design §4 and §9 this is a FINDING, not a ship gate**, and it is reported as one. It re-opens
[F43](followups.md): see §9's amendment, which is applied in place.

Worth recording for the next reader: **`D03`'s landed settings run `StructureLayers = 3`**, so its blur is 7×7
at σ ≈ 1.12 px while `D01` and `D02` run 9×9 at σ ≈ 1.44 px. The optimizer has already moved this knob on one of
the three floored datasets, and it moved both cutoffs when it did.

---

## §3 — `RULE W28-B` = **`B-SPLIT`**, and the unifying hypothesis is REFUTED below the band

> `>>> RULE W28-B = B-SPLIT   (k = 14 of 14 predicted; MID excluded by name: ['D05_tec140_1000mm', 'D06_sparse_1000mm', 'D19_cygnus_deep_shed'])`
> — `w28b_score.txt`

Populations were computed, not typed: 20 bank datasets − 3 `EXCLUDED` = **17**, less 3 MID = **14 predicted**,
`k = 14 of 14` with **0 datasets leaving the denominator**. The blindness guard held: the reader returns the
`PER-FRAME` slice alone and the log text never leaves it, demonstrated on a real published log that carries both
attribution blocks.

### 3.1 The clauses, every ordinal computed

| clause | measured | bar | result |
|---|---|---|---|
| `B-1` sign agreement | **11 of 14** | 13 of 14 | **FALSE** |
| `B-2` prediction correct | **9 of 14**, per class `HIGH 9` / **`LOW 0`** | 12 of 14, ≥ 1 per class | **FALSE** |
| `B-3` all 4 LOW have `Δacc < 0` | **0 of 4** | 4 of 4 | **FALSE** |

`B-1` fails, so the pre-registered verdict is **`B-SPLIT`**, and design §5.3's consequence applies: on a split
**neither route is quoted** as the verdict-bearing statistic. Verdict tree: 432 regions enumerated, 0 uncovered.

### 3.2 The refutation, stated without softening

**`Δacc` — the detector-only route, no golden involved — is POSITIVE on all 17 datasets in the population,
including all four LOW datasets.** The predicted sign flip at the band's lower edge **does not occur**:

| class | prediction | outcome |
|---|---|---|
| **HIGH** (`K ≥ 2.5`, 10 datasets) | `Δ > 0`, focus is the best frame | **9 of 10 correct**; only `D15_cdk20_3454mm_e47` fails, and it fails on route 1 alone (`Δrecall −0.0280`, `Δacc +9.0000`) |
| **LOW** (`K < 1.5`, 4 datasets) | `Δ < 0`, focus is the worst frame | **0 of 4.** `D04 +4.6120`, `D16 +0.5682`, `D18 +9.3125`, `D20 +0.1660` on `Δacc` — every one detects **more** stars at focus than in its wings |

`B-3` reads `Δacc` alone, so its failure does not depend on which route a reader trusts. **The "two edges of one
band" framing survives above the band and dies below it.** Design §5.3 named this exact failure in advance —
*"if the lower edge is sharper than 1.5 px, `B-3` fails and the wave says the band is narrower than claimed"* —
and it is the wave's job to say so rather than to re-draw the boundary.

### 3.3 The route disagreement is unidirectional, and that is a finding in itself

The two routes disagree on **5 of 17** datasets — `D05`, `D15`, `D16`, `D18`, `D19` — and **all five disagree in
the same direction: `Δrecall < 0 < Δacc`. Not one goes the other way.**

That is exactly the confound design §5.2 created route 2 to escape. [F31](followups.md) established that the
reference tiers by **peak-pixel** SNR, so the golden evaporates toward the sweep wings; the per-frame denominator
is therefore largest at focus, and route 1 is biased against the focus frame on every dataset where the effect is
large enough to flip a sign. **The disagreement is not noise, it is attributable, and route 1 is the route it
impeaches.** The design pre-registered *"a disagreement is a finding, not something to average"*; the finding is
that the disagreements have one sign and one named cause.

### 3.4 What the three read datasets do on the same statistics — diagnostic, and explicitly NOT part of the rule

`D01`/`D02`/`D03` are excluded from the population because the prediction was built on them. Computing the
pre-registered statistics on them anyway, from the arm's landed cells (which reproduce `table18` exactly, §4.3):

| dataset | `K` px | `Δrecall` | `Δacc` |
|---|---|---|---|
| `D01_ultrawide_40mm` | 0.700 | −0.202 | **−0.969** |
| `D02_rich_135mm` | 0.700 | −0.222 | **−0.973** |
| `D03_redcat_250mm` | 0.700 | −0.089 | **+0.253** |

**On the detector-only route the below-band dip exists on exactly two datasets in the whole bank, and a third
dataset at the same `K = 0.700` does not show it.** `D03`'s recall dips at focus while its accepted-star count
rises — the §3.3 confound again, on a read dataset this time.

**So `K` does not separate the dip from the non-dip even at the floored value.** That is the same refutation
shape the design itself applied to the controller's ordering by arcsec/px (§1.3), turned on the design's own
covariate. It is recorded as diagnostic. **It is NOT a repaired class boundary and nothing is re-scored on it** —
choosing a new boundary with these columns in hand is precisely what the [F14](followups.md) / `RULE D20` fence
forbids. Wave 29 may pre-register a sharper stratification; §12 prices it.

### 3.5 A limitation of the pre-registered statistic, named because `D03` is where it bites

`Δ` compares the focus frame against **the two extreme wing frames only**. The accepted-star profiles of the
floored datasets are humped, peaking at ±2 steps: `D01` reads `738, 1772, 3539, 953, 23, 944, 3572, 1798, 738`
and `D03` reads `104, 182, 269, 223, 136, 228, 263, 188, 113`. On `D03` the focus frame is far below its
neighbours (136 against 223/228) and above its extremes (104/113), so the pre-registered statistic returns
`+0.253` where a neighbour-based one would return a negative number. **The statistic is applied as written and
its coarseness is reported, not corrected.**

### 3.6 What `B-SPLIT` does and does not establish

**Does:** the sign flip is not a property of in-focus PSF size at the 1.5 px boundary the design drew;
above the band the prediction holds on 9 of 10 blind datasets; route 1 carries a measurable, unidirectional
reference confound.

**Does not:** anything about *why* the two `K = 0.700` datasets that dip do dip and the third does not; anything
about real frames; and — per design §9 — it does not license ship (2), which therefore did not ship.

---

## §4 — `RULE W28-N` = **`N-RECOVERS`**, `k = 3 of 3`

> `>>> RULE W28-N = N-RECOVERS   (N-1 True, N-2 True, N-3 True; k = 3 of 3)` — `w28n_score.txt`

No build. The arm copied the seedA0 optimized-settings tree per cell and edited **one field**; the arm's own
self-test proves the edit is surgical (`exactly 1 file changed, and it is the settings file`; an unedited tree
fingerprints identically twice). Binary: `/mnt/d/hf_w25/exe/TestApp.exe`, `TestApp.dll` sha256
`2fb0c8fd…` — the same B15 binary that produced `table18`.

| clause | measured | bar |
|---|---|---|
| `N-1` the edit took effect | **9 of 9** arm cells echo their intended `NoiseClip=` on the `key detector knobs:` line | all |
| `N-2` the mechanism | high-tier `NO CANDIDATE` falls **79.2 % / 71.3 % / 96.4 %** at `NC = 1.0` | ≥ 25 % on 3 of 3 |
| `N-3` the cost | `precision` = **1.000 / 1.000 / 1.000** from `OVERALL` at `NC = 1.0` | ≥ 0.90 on 3 of 3 |

**`N-1`'s control ran on a real cell and can fail.** An unedited copy of `D03`'s tree echoes `3.625` against a
landed `3.625`; the same reader returns `1` on `D03`'s `NC = 1.0` cell; and the scorer prints the counterfactual
— *"had this control been asked for 1.0 it would report MISMATCH"* — so the predicate is shown to track the file
rather than agreeing by construction. The control sits **outside** every arm denominator and is named as such.

**Which copy.** The `NO CANDIDATE (structure gap)` line appears twice in every report; the scorer anchors on the
`FALSE-NEGATIVE ATTRIBUTION, HIGH TIER ONLY` header and asserts it consumed exactly one. Self-test [3]
demonstrates the difference is real and would have changed the number: on `D01` landed the aggregate copy reads
**48 811** and the high-tier copy reads **42 991**, and the aggregate copy appears **first** in the file, so a
first-match reader would have taken the wrong one.

### 4.1 `N-3` is satisfied and is a weak instrument on this population — say so

`precision = 1.000` with `FP = 0` on **every** cell including all three landed baselines. It was 1.000 before the
intervention and 1.000 after; the clause cannot distinguish "cost none" from "cannot see cost". The per-frame
rows also show `acc` exceeding `TP` throughout — `D01` landed frame 5964 reads `acc 738, TP 709, FP 0` — so 29
accepted detections were neither confirmed nor charged. **`N-3` passes as written; a reader should treat it as
"no precision cost was detected by an instrument that reported no precision cost at baseline either", not as a
measured price of zero.** Wave 27's `scoredFraction` finding (§3 of that document) is the general form of this
and applies here unchanged.

### 4.2 `N-RECOVERS` is NOT a ship, and nothing in wave 28 says otherwise

**No wave-28 rule pre-registered a ship on the `NoiseClippingMultiplier` axis.** Design §9's ship table lists
four items and `NC` is not among them; §9(3) fences the sensitivity axis and §9(4) defers the blur decoupling by
price. More importantly: **the optimizer CHOSE the landed values** — 3.8125, 3.9375, 3.625 — under the objective
`J`, and [F83](followups.md) establishes that `J` charges nothing for a missed detection beyond `sStars` at
weight 0.20. A default or bound change on this axis therefore owes its own wave: a pre-registered ship rule, a
fresh `optimize` baseline (the landings themselves move), and a re-derivation. §12 prices it.

### 4.3 An unpre-registered control that came free, and it is worth keeping

**The three landed cells reproduce the published `table18` rows exactly** — `TP`/`FP`/`FN`, `recall@high`,
`recall@high+med` and `recall@all` all identical on `D01`, `D02` and `D03`, against a tree copied from seedA0 and
re-run through a pinned `--profile-id`. Design §2.3's `51 011` high-tier FN on `D01` and its 84.3 % `NO
CANDIDATE` share reproduce on the nose. **That makes every `NC` delta in §1 attributable to the one edited
field**, and it is a stronger control for this arm than the `--profile-id` inertness probe (`n = 1 dataset`,
carried forward from wave 27 and stated with its `n` as the charter requires). Reported as opportunistic, not as
a pre-registered gate.

---

## §5 — What shipped

### 5.1 ship (1) — the false in-band statement, **UNCONDITIONAL**, shipped

`6f2b62b`, `DetectionBinningResolver.cs` +64 −12, `DetectionBinningResolverTests.cs` +88.

The defect, provable by arithmetic on shipped source and needing no wave-28 statistic: `RecommendFromHfr(0.7)`
clamps to 1, `DiffersFromRecommendation` is therefore false, `ShouldShowRecommendation` returns **false**, and if
the user finds the tooltip anyway it tells them *"0.7 px is already in that range"* about a 2–4 px band. The
clamp is one-sided, so **every** under-sampled rig took that branch.

| changed | to |
|---|---|
| `MinCalibratedHfrPixels` | **new**, `= TargetHfrPixels - 1.0` — an expression, not a literal, so the edge cannot drift from the centre |
| `IsBelowCalibratedBand` | **new**, the ONE helper behind all three call sites |
| `DescribeRecommendationDetail` | a third arm: below the band, say so, say binning only **divides**, and point at `MinHFR` ([F35](followups.md), [F43](followups.md)) |
| `DescribeRecommendation` | a third arm on the **visible row** — see the scope extension in §13 |
| `ShouldShowRecommendation` | **true** below the band, so the row is shown to exactly the users it was hidden from |

**`RecommendFromHfr` is unchanged** and is pinned by a 16-HFR test spanning below/in/above band plus
`NaN`/0/negative. No recommended binning value moves for any input; this changes what the user is **told**.

**Suite: `Passed: 3997, Failed: 0, Total: 3997`, `SUITE_EXIT=0`, 3 m 33 s** — read by COUNT out of
`suite_AFTER_ship1.log` ([F37](followups.md)), never by the tick. **The baseline was 3992, not the design's
3973** (§7.5); the delta is **+5** and the commit names 5 new tests.

### 5.2 ship (2) — did not ship, and that is the rule working

Design §9(2) gated surfacing the below-band condition outside the wizard on `W28-B = B-BANDPASS`. The verdict is
`B-SPLIT`. **It does not ship and is recorded as a costed recommendation** (§11, §12). The rule was not
renegotiated after the number was seen; plan step 7 says so and it was followed.

---

## §6 — The controls

### 6.1 `RULE V28` — self-test PASS, both FAIL fixtures evidenced, and a verdict of `V-UNEVALUATED`

> ### CLOSED AFTER THIS DOCUMENT'S ROLL CALL — the pre-flight was RE-RUN over the populated root
>
> **`RULE V28` = `V-CLEAN`** (`vdrift_w28_FINAL.txt`, 2026-08-13T16:08Z): **7 files checked, `V28-A` 8 of 8
> siblings resolve, `V28-B` 0 unlicensed previous-wave tokens, `V28-C` 0 typed ordinals.**
>
> **So the six instruments ARE drift-clean, and the finding below narrows rather than disappears.** What
> [F95](followups.md) records is a **SEQUENCING** defect, not an unchecked population: the controller ran the
> blocking pre-flight at the moment the plan's Step 1 says to, which was **before the instruments it guards
> existed**. The honest reading of the original `V-UNEVALUATED` was always *"an empty population is never
> 1.0000"* — the checker said so in those words and refused to call it a pass. The remedy is ordering: **a
> blocking pre-flight must run BEFORE the measurement and AFTER the instruments**, which is a narrower window
> than "before anything", and no wave's plan has ever said so.
>
> The original artifact `vdrift_w28.txt` is KEPT unaltered beside the new one, so both states are on the record.


`vdrift_selftest_w28.txt` ends `>>> SELF-TEST PASS` with all six clause groups, including `[6]` in both
directions — *"the block is exempt (line 3) and the file after it is NOT (line 7)"* and *"the DEFINITION of
`HISTORICAL_BLOCKS` opens nothing — it is not an instance of itself"*, which is [F87](followups.md)'s repair
carried forward and proved not to have regressed. The three affix shapes are demonstrated in both directions, and
the wave's own affixed names are shown **not** to be reported, so the check discriminates.

**Both pinned FAIL fixtures refuse, with their counts printed:**

| fixture | clause | findings |
|---|---|---|
| `/mnt/d/hf_w27` | `V28-B` | **262** unlicensed previous-wave tokens (267 total findings: 4 × `V28-A`, 262 × `V28-B`, 1 × `V28-C`), first `d27_arm_w27.sh:51` |
| `/mnt/d/hf_w24` | `V28-C` | **2** typed ordinals in printed output — `gate_w24.sh:221` (`'thirteenth'`), `prov_w24.py:179` (`'11/12/13'`) |
| `/mnt/d/hf_w26` | `V28-B` | **0** — the expiry is demonstrated, which is why the `-B` fixture had to move |

The `-B` fixture moving to `/mnt/d/hf_w27` is a deviation from design §12's pair and is assessed in §13.

**And the run over the wave's own root certifies almost nothing** — §7.3.

### 6.2 `W28-FP` = **`PRESERVED`**, recomputed here because the verdict line was never captured

`fp_w28.sh` builds both sweeps from **one** `sweep()` function called twice ([F91](followups.md)), asserts
`POPULATION` inside each file, and refuses to compare when the two disagree. Both files are on disk and are
**byte-identical to each other**; each asserts `ROOTPOPULATION /mnt/d/hf_w25/table18 440`,
`ROOTPOPULATION /mnt/d/SyntheticAutofocusBank 640`, `POPULATION 1080`, and each carries 1 080 hash lines.

Re-running the script's own `compare` logic over the two artifacts:

```
BEFORE asserted 1080 hashed 1080 ; AFTER asserted 1080 hashed 1080
missing 0 extra 0 MOVED 0
>>> W28-FP = PRESERVED (1080 of 1080 byte-identical, 0 moved)
```

The read-only roots were read and not written. **The verdict line itself has no artifact** — the driver prints it
on `after` and nothing redirected it — so it is reproduced above from the two sweeps rather than quoted (§7.7).

---

## §7 — FINDINGS: each is a defect in a rule or an instrument. Reported, not repaired

### SEVERITY 1

#### 7.1 `W28-B` refutes the design's own unifying hypothesis on the under-sampled half

Stated in full in §3.2 and not softened here. `Δacc > 0` on **all 17** datasets; `B-3` is **0 of 4**; `B-2`'s LOW
class is **0 of 14 correct out of 4 members**. The design's framing — item 8 and `RULE D27` are *"two edges of
one band"* — **survives above the band (9 of 10) and dies below it.** The `LOW` class at `K ≈ 1.04–1.08` behaves
like the `HIGH` class, and the only datasets that behave as predicted are two of the three the prediction was
built on.

**The verdict is robust to the one place `B-2` was under-specified.** Counting a dataset "correct" on route 1
alone would give `LOW 2 of 4` (`D16`, `D18`) and `HIGH 9 of 10`, i.e. **11 of 14 — still below the 12 bar**, and
`B-1` does not depend on that reading at all. **Neither reading rescues the hypothesis.**

#### 7.2 `W28-N`'s verdict tree never gates on `N-1` — and this is the SECOND consecutive wave with a verdict-tree gap

> `the DESIGN's tree, enumerated over 135 regions, leaves 4 uncovered -- every one of them is a`
> `  region where N-1 is FALSE, which the design's tree never gates on. That is a finding about`
> `  the pre-registration, recorded here and NOT repaired mid-wave.`
> `the design's literal tree would return: N-RECOVERS`
> — `w28n_score.txt`

Design §8 defines `N-RECOVERS` as `N-1 ∧ N-2 ∧ N-3` in prose and then writes a tree — `N-REFUTED` if `N-2`
fails, `N-COSTED` if `N-2` holds and `N-3` fails, `N-UNDERPOWERED` if `k < 3` — in which **`N-1` appears
nowhere**. A run where the edit silently did not take effect would have been scored `N-RECOVERS` by the tree as
written. The scorer returns `N-TREE-GAP` in those four regions, enumerates them, and prints what the design's
literal tree would have said, so a reader can see both. **It did not fire this wave** (`N-1` is 9 of 9), and
self-test [5] demonstrates it firing on a fixture where a cell echoes the landed value while intending 1.0.

**This is a class, not an incident.** Wave 27 §8.1 carried the same defect on `RULE T27` (`SAT = 3, CHANCE = 3`
matched no clause), and wave 27's §12 item 6 asked for it to be fixed *in the next wave's pre-registration*.
Wave 28's design §12 duly made total coverage a standing rule — *"every verdict tree in this wave is checked for
total coverage of its outcome space at pre-registration"* — **and then shipped a tree with four uncovered
regions.** The standing rule was written and not executed. `W28-S` (27 regions) and `W28-B` (432 regions) both
enumerate clean; only the rule with a validity clause outside its own tree does not. Registered as
**F94**.

#### 7.3 The blocking pre-flight certified a root containing **one file** — itself

`vdrift_w28.txt` reads `files checked: 1` and `>>> V-UNEVALUATED: no sibling target was referenced by any driver
in this root`. It ran at **11:06:36**, when `/mnt/d/hf_w28/` held only `verify_derivation_w28.py`. **Every
instrument the wave then wrote — `fp_w28.sh`, `score_w28s.py`, `score_w28b.py`, `score_w28n.py`,
`w28b_manifest.sh`, `w28n_arm_w28.sh` — was created after that run and was never checked by it**, and the file
was never re-run.

Two consequences, both stated as they are:

1. **The plan's gate is not met by the artifact.** Plan step 1 requires *"`vdrift_w28.txt` verdict is `V-CLEAN`
   **and** its banner reads `RULE V28`"* before anything downstream runs. The banner does read `RULE V28` — the
   [F87](followups.md) half of the gate holds and matters. The verdict reads `V-UNEVALUATED`, which the design
   itself says is never a pass. The wave proceeded.
2. **What it would have caught is unknown, and one candidate is visible.** `V28-C` looks for typed ordinals in
   printed output; the drivers written afterwards are full of printed counts, all of which appear to be computed,
   and `w28b_manifest.log`'s `predicted=14 (LOW=7 HIGH=10)` mixes two populations in one printed line (§7.10).
   Whether the checker would have flagged that is not knowable without running it, and **this document does not
   run it** — a pre-flight run at the end of the wave is a different instrument from a pre-flight.

**This is [F80](followups.md)'s family in a new direction: not a pattern too narrow and not a population
containing the instrument, but a population that was EMPTY at the only moment the check was taken.** A blocking
gate that runs before the things it blocks exist blocks nothing. Registered as **F95**.

### SEVERITY 2

#### 7.4 The design's Step 4 count assertion was **wrong as written** and would have made `W28-S` permanently `S-UNEVALUATED`

Design §4 and plan step 4 both assert *"there is one `ConvolveGaussian` call in `BuildDetectionContextInternal`;
the scorer asserts the count of matching call sites is exactly 1 and refuses on any other count."* The body spans
lines 433–756 and contains **three**:

```
ConvolveGaussian call sites in that body, ALL: 3 at lines [535, 556, 632]
  of which post-wavelet STRUCTURE-MAP blurs, ConvolveGaussian(structureMap, structureMap, ...): 1 at lines [632]
```
— `w28s_score.txt`

The other two are the noise-reduction blurs keyed to `p.NoiseReductionRadius` — **a different knob at a different
stage**. Taken literally the pre-registered assertion fails at HEAD, and its declared consequence is refusal:
`S-UNEVALUATED`, forever, on correct source. The scorer scoped the count to the structure-map pair, which is
exactly 1, **printed both numbers**, and named the design's phrasing as loose. Self-test [1] proves the two
noise-reduction blurs are *seen and excluded* rather than invisible, and self-test [5] proves both ends of the
count assertion: two structure-blur sites refuse (naming the count they saw), zero sites refuse as well.

#### 7.5 Step 4 named ONE source artifact; `S-b` needs TWO

Design §4's POPULATION is *"the single artifact `StarDetector.cs`"*. `S-b`(ii) asks whether any
`StarDetectorParams` member controls the blur width independently — and `StarDetectorParams` is declared in
`Interfaces/IStarDetector.cs`. The scorer records **both** sha256s (§2) and self-test [6] makes the point
explicitly: *"an absent `StarDetectorParams` class REFUSES — `S-b`'s population is the SECOND artifact, and the
design naming only the first is under-specified."*

#### 7.6 `B-1`'s bar is **unsatisfiable at `k = 12`** taken verbatim, and `B-2`'s "correct" was undefined under a split

Two under-specifications in design §5.3, both resolved in the open in the scorer's header rather than guessed
silently:

1. **The bars at a reduced denominator.** The design fixes 13 of 14 and 12 of 14 and then permits a verdict down
   to `k = 10` without saying what the bar is below 14. **Taken verbatim, `B-1` is unsatisfiable at `k = 12`** —
   13 agreements out of 12 datasets — for a reason that has nothing to do with the routes agreeing. The scorer
   applies the pre-registered **rates**, ceilinged: `bar(k) = ceil(num·k/den)`; self-test [7] asserts that at
   `k = 14` this reproduces **13** and **12** exactly, so the scaling can only engage on a population the wave
   already lost rows from. It never engaged: `k = 14 of 14`.
2. **What "prediction correct" means when the routes disagree.** The design says a disagreement is *"not
   something to average"* and that on a split *"neither is quoted"*, so the scorer counts a dataset correct only
   when **both** route signs equal the prediction. §7.1 shows the verdict is the same under the alternative
   reading.

#### 7.7 Two captures that exist only for the session that ran them

**ship (1)'s six mutants (`M1`–`M6`) have no artifact.** The commit message states each of the 5 new tests was
shown RED against a named mutant, with byte backups, `touch` after restore ([F89](followups.md)) and sha256-verified
restores. **Nothing under `/mnt/d/hf_w28/` records a red run.** This is wave 27 §7.4 recurring one wave later:
green tests are not a mutant kill, and F89's own entry says untested testimony from a mutation harness is exactly
the class of claim that needs a file. Price to close: **~10 m**.

**`fp_w28.sh --self-test` has no artifact either.** The script has a four-clause self-test including F91's own
shape (*"differing MEMBERSHIP is UNEVALUATED, never a false VIOLATED"*), and `TIMING.tsv` records *"self-test 4
of 4"*, but no file in the root contains `SELFTEST W28FP`. Same for the `W28-FP` verdict line (§6.2). Price:
**~2 m** for both.

#### 7.8 The design's suite baseline was stale by 19 tests

Design §9 fixes *"Baseline **3973**"* and a gate of ≥ 3973 + 3. The real baseline entering wave 28 was **3992**
(wave 27 added 19 with `TruthDisclosureTests.cs`), and the suite closed at **3997**. The gate as written is
satisfied either way, so nothing turns on it — but a baseline quoted from two waves back would have hidden a
19-test regression, and the pre-registration is where that number is supposed to be current.

### SEVERITY 3 — traps the disciplines caught themselves

#### 7.9 A real bug the smoke run caught: locating a method by its first name occurrence starts at a **call site**

`score_w28s.py` originally located `BuildDetectionContextInternal` by the first textual occurrence of the name.
In the real source that occurrence is a **call**, not the declaration, so the "body" began at the call site and
contained **zero** blur calls — which, under the count assertion, would have refused with a confident and wrong
`S-UNEVALUATED`. Declarations are now discriminated from calls, and **an overloaded declaration refuses rather
than picking a body** (*"an ambiguous population is a could-not-look, not a coin toss"*). Self-test [10] asserts
both, and asserts that the first textual occurrence is genuinely earlier than the located declaration — so the
discrimination is doing work rather than agreeing by luck. The same group proves a `ConvolveGaussian` decoy
inside a string literal is blanked (length-preserving, so offsets stay aligned) and cannot inflate the count.

#### 7.10 Live traps: CRLF, and a fingerprint written inside the tree it sweeps

* **Every `table18` log carries CRLF.** `score_w28b.py` strips `\r` before any comparison and self-test [10]
  asserts the guard on a **real published log**, not a fixture: *"the real log carries CRLF and both attribution
  blocks"*. A parser that had compared the header line raw would have refused all 20 datasets on column identity.
* **F91's population assertion fires when a fingerprint is written inside the tree it fingerprints.**
  `w28n_arm_w28.sh:65-67` records it in place: *"Writing `fp_before.txt` inside the directory being swept makes
  the AFTER population one larger than the BEFORE one and reports three changed files where one changed — caught
  by the population assertion at this file's own self-test, which is what it is for (F91)."* The remedy is
  structural — the per-cell fingerprints live in `nc/fp/`, outside every tree they sweep.
* **A denominator defect caught before it could print.** `score_w28b.py` self-test [6]: *"a could-not-look on a
  MID dataset does NOT move the predicted denominator — it was never in it, and counting it there would print
  `k of 15` on a population of 14."* MID is excluded from every denominator by name, and a MID dataset that goes
  missing is still named rather than disappearing.

#### 7.11 `w28b_manifest.log` prints two populations in one line

`rows=20  excluded=3  population=17  MID=3  predicted=14  (LOW=7 HIGH=10)`. The `LOW=7 HIGH=10` is over all 20
rows (LOW includes the three excluded datasets); `predicted=14` is not `7 + 10`. Within the predicted population
LOW is **4** and HIGH is **10**, as `w28b_score.txt` prints unambiguously. Cosmetic, in a driver log, and named
so a reader does not try to reconcile 14 with 17.

---

## §8 — What the design left unsatisfiable, under-specified or gap-ridden

**Each is reported, not repaired. None was re-decided to make a verdict land.** The remedy in every case has the
same order: **fix the rule first, in a pre-registration, and re-score afterwards — on a population measured after
the fix** ([F14](followups.md) / `RULE D20`).

| # | defect | fired? | verdict-determining? |
|---|---|---|---|
| §7.2 | **`W28-N`'s tree never gates on `N-1`** — 4 of 135 regions uncovered | no (`N-1` 9 of 9) | would be, if it fired |
| §7.4 | **Step 4's `ConvolveGaussian` count assertion is wrong at HEAD** (3, not 1) | **yes** | **YES** — literally applied it forces `S-UNEVALUATED` on correct source |
| §7.5 | Step 4 names one source artifact; `S-b` needs two | yes | no — refusal, not a wrong pass |
| §7.6 | `B-1`/`B-2` bars undefined below `k = 14`; **`B-1` unsatisfiable at `k = 12`** | no (`k = 14`) | would be, on any could-not-look |
| §7.6 | "prediction correct" undefined when the routes disagree | **yes — on 5 of 17** | **no** — §7.1 shows both readings fail `B-2` |
| §3.5 | `Δ` reads only the two extreme wing frames, and the profiles are humped | yes (`D03`) | not in the population; diagnostic only |
| §4.1 | `N-3` is a bar on a statistic that is 1.000 at baseline | yes | no — it passes, weakly |
| §7.3 | the blocking pre-flight's population was empty when it ran | **yes** | no rule's numbers depend on it |
| §7.8 | the suite baseline was stale by 19 | yes | no |

**One rule shape is worth crediting rather than filing:** design §4 declared `W28-S` *"NOT a ship gate"* on the
ground that a conjunct known-true at pre-registration is a one-clause rule wearing two clauses. It held, as
predicted, and it is reported as a finding. That is the discipline working, in the same place wave 27 §8.7
recorded it working.

---

## §9 — Register entries, ready to paste into `docs/followups.md`

Continuing from [F91](followups.md). **`F43` is amended in place in the register itself** (its amendment is stated inside [F92](followups.md),
under "Consequence for the register"), because it
is an existing entry whose stated result is now known to be confounded.

### F92 — The post-wavelet blur is WELDED to `StructureLayers`, so one knob sets two opposing scale cutoffs

**Status:** **Open — source-derived, zero compute, and it re-opens [F43](#f43)** (2026-08-13, wave 28,
`RULE W28-S` = `S-WELDED`, 3 of 3)

`StarDetector.cs:631-632` blurs the structure map immediately after the à trous residual is subtracted:

```csharp
// Step 5: Excluding large structures can cut off the outsides of large stars, or leave holes when far out
//         of focus. Blurring smooths this out well for structure detection
CvImageUtility.ConvolveGaussian(structureMap, structureMap, p.StructureLayers * 2 + 1);
```

with `sigma = 0.159758 * kernelSize` when unset (`CvImageUtility.cs:80-82`). At the shipped
`StructureLayers = 4` the kernel is 9×9 and **σ ≈ 1.44 px**, so a 1-px source loses roughly `1/(2πσ²) ≈ ×0.077`
of its peak amplitude **before** it is compared against
`binarizeThreshold = median + NoiseClippingMultiplier · sigma` (`:653`).

**The blur's stated purpose is to help LARGE and defocused stars, and its entire cost falls on small ones.**
Its width is keyed to `StructureLayers`, which is the knob for the *upper* scale cutoff — the shipped tooltip
says so: *"At the default of 4 (2⁴=16), structures larger than 16 pixels in size are excluded"*
(`OptionsDataTemplates.xaml:444`). **Lowering `StructureLayers` therefore narrows the blur (helps a 1-px star)
and makes the residual less smoothed (hurts it), in one move.**

**Measured as a rule, both hashes recorded** (`/mnt/d/hf_w28/w28s_score.txt`, `StarDetector.cs`
`1e202777…`, `IStarDetector.cs` `5304ed5f…`): the width argument is literally `p.StructureLayers * 2 + 1`
(raw, **not** `EffectiveStructureLayers`); **no** member of `StarDetectorParams` — 55 properties — controls the
blur width independently; and `EffectiveStructureLayers(p)` reaches the residual at `:619`/`:622` and appears
**0 times** in the blur's argument list. The defocus/donut boost (`:1567-1578`) already decouples them in one
direction, so **there is precedent for decoupling the other.**

**Consequence for the register:** [F43](#f43)'s *"`StructureLayers` 4 → 2 makes it strictly worse"* moved both
cutoffs at once and is **confounded in source**. The experiment that separates them has never been run.

**Price to run it:** ~45 m code (a dedicated blur-width field defaulting to today's expression, so the default
is bit-identical) + ~10 m arm; **plus a fresh 42 m `optimize` baseline and a re-derivation if it is ever to
ship**, because the change reaches the detector. Design §11 defers it; `RULE W28-N` (see **F93**)
is the evidence that the amplitude account it rests on is correct.

### F93 — `NoiseClippingMultiplier` is the binding gate below the calibrated band on the bank too — with one dataset where it is not enough

**Status:** **Open — measured, and explicitly NOT a licence to change a default** (2026-08-13, wave 28,
`RULE W28-N` = `N-RECOVERS`, 3 of 3) · corroborates [F43](#f43) on synthetic frames · bounded by [F22](#f22)

[F43](#f43) found that on a reporter's **real** 61 MP 19.4″/px rig the binding gate was `NoiseClippingMultiplier`,
not `Sensitivity`. Wave 28 ran it on the bank: 3 floored datasets × `NC ∈ {landed, 2.0, 1.0}`, no build, one
edited field per cell, 432 s.

| dataset | `NC` landed | high-tier `NO CANDIDATE` landed → `NC 1.0` | `recall@high` landed → `NC 1.0` | `recall@all` | precision at 1.0 |
|---|---|---|---|---|---|
| `D01_ultrawide_40mm` | 3.8125 | 42 991 → **8 931** (−79.2 %) | 0.183 → **0.195** | 0.123 → 0.183 | 1.000 |
| `D02_rich_135mm` | 3.9375 | 3 732 → **1 071** (−71.3 %) | 0.446 → **0.687** | 0.377 → 0.598 | 1.000 |
| `D03_redcat_250mm` | 3.625 | 197 → **7** (−96.4 %) | 0.365 → **0.549** | 0.238 → 0.364 | 1.000 |

**The mechanism is confirmed and the payoff is not uniform.** `D02` and `D03` gain +0.241 and +0.184 of
`recall@high`. **`D01` does not:** 34 060 high-tier candidates now form, **97.8 % of them are re-rejected by a
later gate** (`TooDistorted` +13 866, `LowSensitivity` +12 826, `TooSmall` +6 515), and `recall@high` moves
+0.012. Its attribution profile inverts — `NO CANDIDATE` 84.3 % → 17.8 %, those three gates 13.0 % → 79.3 % — so
**at `NC = 1.0` `D01` looks like `D02`/`D03` did at their landed settings.** The gates fire in sequence
(`TooSmall` → `OnBorder` → `TooDistorted` → `Degenerate` → `LowSensitivity`), so those counts are
first-rejection-wins and **which gate binds on `D01` is NOT established.**

**Three things this entry does not say.** (1) It is **not a ship**: no wave-28 rule pre-registered a change on
this axis, and the optimizer *chose* the landed values under an objective that charges nothing for a missed
detection ([F83](#f83)). A default or search-bound change owes its own wave with a fresh baseline. (2) The
`precision = 1.000` is **1.000 at baseline too**, so `N-3` shows no detected cost rather than a measured cost of
zero. (3) [F22](#f22) still binds: below ~1.1 px measured HFR over-reads by up to +234 %, so **stars recovered
below the band carry an HFR that cannot resolve the vertex** — and `D01` already reaches `BestJ` 0.994825.

**Artifacts:** `/mnt/d/hf_w28/nc/<dataset>_nc<v>/attempt01/golden_eval.txt`, `w28n_score.txt`,
`w28n_manifest.tsv`. The three landed cells reproduce the published `table18` rows exactly, so every delta is
attributable to the one edited field.

### F94 — Verdict trees keep shipping with uncovered regions, and the standing rule against it did not stop the second one

**Status:** **Open — a class, now on its second consecutive wave** (2026-08-13, wave 28) · generalises wave 27
§8.1 · belongs beside [F68](#f68) part 5

Wave 27's `RULE T27` tree left `3 ≤ union ≤ 9` with neither set at 5 uncovered, and the scorer printed
`T-TREE-GAP` rather than papering over it. Wave 28's design §12 turned that into a standing rule — *"every
verdict tree in this wave is checked for total coverage of its outcome space at pre-registration"* — and then
**`RULE W28-N`'s tree shipped with 4 of 135 regions uncovered, every one of them a region where `N-1` is
false.** `N-1` is the clause that checks the edit took effect; a run where it silently did not would have scored
`N-RECOVERS` on the tree as written.

**The pattern is specific and predictable: the uncovered region is always the VALIDITY clause.** `N-2` and `N-3`
are substantive and appear in every branch; `N-1` is the gate that says the measurement happened at all, it is
expected to hold, and it fell out of the tree. `W28-S` (27 regions) and `W28-B` (432 regions) both enumerate
clean — and neither has a validity clause outside its own conjunction.

**Remedy, and it is mechanical rather than a reminder:** a design must **enumerate its own outcome space at
pre-registration** — the scorers already do this in code, and printing `regions enumerated: N   uncovered: 0`
costs nothing — and **every clause, including the ones expected to hold, must appear in the tree.** A scorer
that finds a gap prints `<RULE>-TREE-GAP`, enumerates the uncovered regions, prints what the design's literal
tree would have said, and **does not repair and re-score** (`/mnt/d/hf_w28/w28n_score.txt`, self-test [5]).

### F95 — A blocking pre-flight that runs before the instruments exist certifies an empty population

**Status:** **Open — remedy known, not applied this wave** (2026-08-13, wave 28) · [F80](#f80)/[F87](#f87)'s
family, third distinct shape

`verify_derivation_w28.py` ran at `11:06:36` over `/mnt/d/hf_w28/`, which then contained exactly one file —
itself. `vdrift_w28.txt` reads `files checked: 1` and `>>> V-UNEVALUATED: no sibling target was referenced by
any driver in this root`. The wave's six instruments (`fp_w28.sh`, `score_w28s.py`, `score_w28b.py`,
`score_w28n.py`, `w28b_manifest.sh`, `w28n_arm_w28.sh`) were all written afterwards and **were never checked**.
The plan's gate required `V-CLEAN`; the design says `UNEVALUATED` is never a pass; the wave proceeded.

**The self-test and both pinned FAIL fixtures are healthy** — `SELF-TEST PASS`, 262 `V28-B` findings on
`/mnt/d/hf_w27`, 2 `V28-C` findings on `/mnt/d/hf_w24`, and `/mnt/d/hf_w26` demonstrating expiry with 0. **The
instrument works; it was pointed at nothing.**

**The class, stated so it survives this wave:** F80's first three gaps were a *pattern* too narrow; F87's was a
*population containing the instrument*; this is a *population that was empty at the only moment the check was
taken*. **A blocking pre-flight must be re-run at the wave's close over the finished root, and the closing run
is the one whose verdict is quoted.** The pre-flight run proves the checker works; the closing run proves the
wave is clean. They are two runs and this series has been conflating them. Price: **~2 m per wave.**

### 9.5 Amendments owed to existing entries

* **[F43](#f43) — AMEND IN PLACE, and it is the amendment this wave owes most.** The entry's *"`StructureLayers`
  4 → 2 makes it strictly worse (0 at both central positions)"* is **confounded in source**, proved by
  `RULE W28-S` = `S-WELDED` at zero compute. Lowering `StructureLayers` moves **two** cutoffs in opposite
  directions: it narrows the post-wavelet Gaussian at `StarDetector.cs:632` (whose width is literally
  `p.StructureLayers * 2 + 1`, so 4 → 2 takes the kernel 9×9 → 5×5 and σ 1.44 → 0.80 px, which **helps** a 1-px
  star clear the binarization threshold) **and** shrinks the à trous residual's low-pass from ≈2⁴ to ≈2² px, so
  `src − residual` retains less of the star (which **hurts** it, and by more). The measurement stands; the
  **inference that the axis is useless does not**, because the two effects were never separated and no member of
  `StarDetectorParams` can separate them. **F43's "`MinHFR` is the ONLY axis that rescues it" must now be read as
  "the only axis that rescues it among those tested, on an axis set that contained a confounded knob."** See
  **F92**; and note **F93** finds a **second** axis that rescues `D01`'s
  candidate formation (`NoiseClippingMultiplier` 3.8125 → 1.0 cuts its structure gap 79.2 %) — though not its
  `recall@high`.
* **[F62](#f62)** — append that wave 28 tested the "one band-pass phenomenon" claim out of sample and **it did
  not survive below the band**. `RULE D27`'s upper-edge result is untouched and the prediction holds on **9 of 10**
  blind datasets above the band; the lower-edge prediction fails on **0 of 4** LOW datasets, with `Δacc > 0` on
  all 17. `RULE D27` and item 8 may still be two views of one mechanism, but the *evidence* for the unification is
  one-sided and must be quoted that way.
* **[F31](#f31)** — append a second, independent demonstration of the reference's wing evaporation: on wave 28's
  blind population the golden-denominated and detector-only routes disagree on **5 of 17** datasets and **all
  five disagree in the same direction** (`Δrecall < 0 < Δacc`). A per-frame recall statistic is biased against
  the focus frame wherever the golden's denominator peaks there.
* **[F21](#f21)** — extend the measured `golden eval` rate. Wave 27 recorded 6–46 s per 9-frame cell (full
  20-dataset arm 349 s). **Wave 28's `D01` at `NC = 1.0` took 179 s** — 3.9× the previous maximum — because
  lowering the threshold multiplies the candidate count. **Price a cell from the RANGE and from the knob
  setting, not from the dataset alone.** Wave 28's 10 cells totalled 381 s of `TestApp` time inside a 432 s wall.
* **[F35](#f35)** — its finding that *"the W class's recall is lost in candidate FORMATION — the structure map
  never proposes 49 % of them"* now has a lever: `NoiseClippingMultiplier` converts most of that loss into
  formed candidates (**F93**). On `D02`/`D03` they become detections; on `D01` 97.8 % of them
  are re-rejected downstream, which is also where F35's *"`MinimumStarBoundingBoxSize` rejects another 17 % as
  `TooSmall`"* now points.
* **[F84](#f84)** — unchanged and re-affirmed: **wave 28 proposed and measured nothing on the `Sensitivity`
  axis.** `D01`'s `LowSensitivity` count rising from 786 to 13 612 at `NC = 1.0` is a **downstream consequence**
  of more candidates existing, not evidence for or against a sensitivity floor.
* **`docs/synthetic-af-bank-results-table.md`** — add the **in-focus kernel HFR `K` (px)** column from
  `truthModel` (`max(hfrMin, hfrMinEffective)`), and the note that `D01`/`D02`/`D03` are the **only three floored
  by the generator** (`hfrMin < hfrMinEffective`), all at `K = 0.700` despite 19.4 / 5.7 / 3.1 ″/px. It reorders
  the recall column into a monotone story and costs nothing. **Do not annotate it with a 2–4 px band claim
  below the band** — `W28-B` is `B-SPLIT` and the lower edge is not established.

---

## §10 — Estimate vs actual ([F21](followups.md))

From `/mnt/d/hf_w28/TIMING.tsv`:

| step | est (m) | actual (m) | note |
|---|---|---|---|
| 0 vessel | 5 | **2** | continued on the w27 branch/PR #196 — §13 |
| 1 pre-flight `V28` | 30 | **14** | `SELF-TEST PASS`; caught a controller `sed` (`V27_OK`, `_` is not a word boundary) |
| 2 fingerprint BEFORE | 10 | **8** | 1 080 files (440 + 640), ONE sweep expression |
| 3 ship (1) | 60 | **38** | 5 tests, 6 mutants, scope extended to the visible row; suite 3997 |
| 4 `RULE W28-S` | 30 | **9** | `S-WELDED` 3 of 3; the count assertion caught the design's looseness |
| 5 `RULE W28-B` | 75 | **14** | `B-SPLIT`; band-pass refuted below the band |
| 6 `RULE W28-N` | 60 | **22** | `N-RECOVERS` 3 of 3; arm **432 s** against a 300–480 s estimate |
| 8 fingerprint AFTER | 10 | **7** | `PRESERVED` 1 080 of 1 080 |
| **rows with both figures** | **280** | **114** | **~59 % under** |

**This is the third consecutive wave in which the analysis halves are over-priced.** Wave 27 was ~36 % under and
named *"a pure-Python arm over artifacts already on disk is consistently over-priced by this series by a factor
of 3–4."* Wave 28 is worse: `W28-B` was priced at 75 m and delivered in 14, and it is the rule that carried the
verdict. **The one thing priced correctly was the only thing that ran a binary** — the `NC` arm, estimated
300–480 s, actual **432 s**, inside its own band. The lesson is now specific enough to act on: **price
compute from the measured rate and price analysis at a third of instinct.**

**Rates measured this wave, to carry into the charter's §5 budget table:**

| instrument | rate | how measured |
|---|---|---|
| `golden eval`, 9-frame cell, floored dataset at **landed** `NC` | **7 s** (`D03`) – **43 s** (`D01`) | `w28n_manifest.tsv` |
| `golden eval`, 9-frame cell, **`NC = 1.0`** | **12 s** (`D03`) – **179 s** (`D01`) | `w28n_manifest.tsv` — the knob, not the dataset, sets the top of the range |
| whole 10-cell `NC` arm incl. copies + fingerprints | **432 s** wall / 381 s in `TestApp` | `w28n_arm.log` |

---

## §11 — What was NOT run, and what it costs

| not run | price | status |
|---|---|---|
| **ship (2)** — surfacing the below-band condition outside the wizard | **~45 m + suite** | **GATED AND THE GATE DID NOT HOLD.** `W28-B` = `B-SPLIT`, so per design §9(2) it does not ship and is a costed recommendation. The machinery exists and is wizard-only: `HasUndersampledStars` (`StarDetectionOptimizerWizardVM.cs:237-238`), `MinHfrSeed.IsBelowGate` (`MinHfrSeed.cs:113-120`). **Note ship (1) has now put a below-band message on the options row**, which changes what this item is worth |
| **the blur/layer decoupling build** (**F92**) | **~45 m + ~10 m arm (+42 m baseline to ship)** | Deferred by price, not by verdict. `W28-N` says the amplitude account it rests on is **right**, which raises its value |
| any `StructureLayers` sweep without that build | ~5 m | **Refused** — `W28-S` proves in source that it re-measures [F43](followups.md)'s confound |
| **localising `D01`'s residual loss** (§1.1) | **~60 m + ~15 m compute** | **NOT RUN, and named as the wave's biggest un-answered question.** The gates are sequential, so it needs an arm that opens them one at a time |
| **a `NoiseClippingMultiplier` default/bound change** | **design 60 m + ~1.7 h `optimize` baseline + re-derivation** | **NOT LICENSED** by wave 28 (§4.2) |
| [F82](followups.md) — `D01`'s stalled step recommendation | ~45 m + ~10 m | Named by design §11 for wave 29; scores goal **2** |
| new datasets rendered between 1.4 and 19.4 ″/px | ≥ 1 h render, unpriced | The only route to a **blind** wide-field dataset (design §2.6); the bank's coverage hole is unchanged |
| [F83](followups.md)'s `P-D08` at `n = 3` | **~2 min** | Still *"the cheapest decision-moving measurement left in the register"*, still a precision question on a fenced axis |
| wave 27's open controls (`S27-1`, `M-S1`/`M-S2a`/`M-S2b`) | ~20 m | **STILL OPEN**, one wave later |
| **ship (1)'s `M1`–`M6` mutant records** | **~10 m** | **TESTIMONY** (§7.7) |
| **`W28-FP` verdict + `fp_w28.sh` self-test capture** | **~2 m** | **NO ARTIFACT** (§7.7); the verdict is recomputed in §6.2 |
| **a closing `V28` run over the finished root** | **~2 m** | **NOT RUN** (§7.3) — the only run happened over a root of one file |
| anything on real frames | — | The bank has no real under-sampled dataset. **Every conclusion above is synthetic-only** |

---

## §12 — Wave 29, priced and scored against the owner's three goals

Goals (charter §8): **(1)** optimization + autofocus works across a wide range of setups, bridging detected gaps;
**(2)** accuracy of step-size recommendations; **(3)** appropriate exposure adjustments and avoiding parameters
pinned to extremes.

| # | item | price | goals | why now |
|---|---|---|---|---|
| **1** | **[F82](followups.md) — `D01`'s step-size recommendation stalled at 3 against a truth of 9.** Diagnosed to a line, two candidate fixes, priced | **~45 m + ~10 m** | **2** ✓✓✓ | **The design's own recommendation, and wave 28's evidence strengthens it.** It is a product defect with a mechanism (`SearchSpan` over `bestFit.Inputs`, so a shrinking bound drags cap and floor down together), on the same rig item 8 is about, and it is the only open item scoring goal 2. Item 8's own headline dataset already reaches `BestJ` 0.994825, so its recall gap is not costing goal 1 on `D01`; its **step size** is |
| **2** | **Localise `D01`'s residual loss** — with `NC = 1.0` held fixed, open `MinimumStarBoundingBoxSize` (landed **6**, the bank's highest, against a shipped default of 5 and a `WideField` preset that *lowers* it) and `MaxDistortion` one at a time | **design 30 m + ~60 m + ~15 m compute** | **1** ✓✓, **3** ✓ | Wave 28 converted 34 060 `NO CANDIDATE` into 33 298 gate rejections and **did not say which gate binds**. The gates are sequential, so this needs an arm, not a re-read. It also scores goal 3: `MinBox = 6` on the two rigs with the smallest stars is a parameter pinned against the physics |
| **3** | **Pre-register the lower edge properly and re-score `W28-B`** — the `LOW` class at `K ≈ 1.04` behaves like `HIGH`, and only 2 of 20 datasets dip on the detector-only route | **~30 m, zero compute** | **1** ✓ | §3.4's bracket is **diagnostic, not authority**, and the [F14](followups.md) fence forbids re-drawing the boundary with these columns in hand. Fix the rule first, then re-score. Cheap, and it settles what `DetectionBinningResolver`'s band means below 2 px |
| **4** | **A `NoiseClippingMultiplier` ship rule, pre-registered, with a fresh baseline** | **60 m design + ~1.7 h baseline + re-derivation** | **1** ✓✓ | **F93** is a measurement, not a licence (§4.2). The optimizer chose the landed values; changing a default or a search bound moves the landings, so the baseline is not optional. **Do not run this before item 2** — the payoff on `D01` is currently +0.012 and item 2 decides whether it can be more |
| **5** | **Close the instrument debt, all of it in one pass** — ship (1)'s `M1`–`M6` records, `fp_w28.sh`'s self-test capture, the `W28-FP` verdict line, a closing `V28` run, and wave 27's `S27-1` + three mutants | **~35 m total** | instrument | Five items, each ≤ 10 m, and three of them are one wave old already. **F95** in particular is a two-minute habit change |
| **6** | **Make total tree coverage a mechanical step, not a standing sentence** (**F94**) | **~10 m** | instrument | The standing rule was written in wave 28's design §12 **and then not executed on wave 28's own tree**. A sentence did not work; enumerate the outcome space in the pre-registration |
| **7** | The blur/layer decoupling build (**F92**) | ~45 m + ~10 m (+42 m to ship) | **1** ✓✓ | Now better-motivated than at pre-registration — `W28-N` confirms the amplitude account — but it is a detector change and owes a baseline. **After items 1–3** |

**The recommendation, stated as a judgement rather than a table row.** Take **item 1 (F82)** first, as the design
asked: it scores the goal nothing else scores, it is diagnosed to a line, and wave 28's §11 argument that `D01`'s
recall gap is *not* costing goal 1 on `D01` is now stronger, not weaker, because the recall lever exists and buys
+0.012 there. Take **item 2** second, because it is what turns wave 28's `N-RECOVERS` from an interesting number
into a decision — without it, item 4 is a proposal to change a default on the strength of a dataset where the
default change does almost nothing. **Do not take item 4 before item 2**, and do not take it at all without the
baseline: `N-RECOVERS` is not a ship, nothing in wave 28 pre-registered a rule that would license changing a
default, and the values it would replace were chosen by the optimizer rather than by a person.

---

## §13 — Controller deviations, all recorded

| # | deviation | assessment |
|---|---|---|
| 1 | **ship (1)'s scope was extended to `DescribeRecommendation`** — the visible row — after the code agent flagged that it still read *"1x1 is right"* to a below-band rig. The plan scoped the step to the tooltip | **Deliberate, reasoned in the commit, and named here as a scope extension of an UNCONDITIONAL ship.** The commit records it in the body: *"the same defect on the surface the user actually reads … the ship is unconditional, so scope discipline here is about honesty, not about protecting a verdict."* It is the right call — a fix that leaves the visible row saying the opposite of the tooltip is not a fix — and it is exactly the kind of change that must be declared rather than absorbed. Both surfaces now reach the band edge through **one** helper, so they cannot drift |
| 2 | **The wave ran on `ghilios/synthetic-af-bank-followups-wave27` (PR #196), not the fresh `…wave28` branch design §0 pre-registered** | **Declared in `TIMING.tsv` step 0 and reported here.** The note says the change was *"recorded in the design"*; the design at HEAD still reads *"Wave 28 opens a fresh branch … and PR #196 stays open on its own branch"*, so the record and the design disagree and this document says so. The substantive cost is the one design §0 named: **#196 now carries two waves and a user-visible product change under a title about wave 27's truth-protection audit** — the accretion the charter's §1a warns about, one PR later |
| 3 | **`W28-N`'s arm took 432 s against a 300–480 s estimate** | **Inside the band, and the only estimate this wave got right.** Recorded per [F21](followups.md) with its per-cell breakdown; `D01` at `NC = 1.0` alone was 179 s, outside wave 27's published 6–46 s per-cell range, and the range is amended in [F21](followups.md) rather than in a section of this document |
| 4 | **The `V28-B` FAIL fixture moved to `/mnt/d/hf_w27`** — design §12 pinned `/mnt/d/hf_w24` and `/mnt/d/hf_w26` and warned that neither must be *"whatever came before"* ([F80](followups.md)) | **Forced, correctly handled, and worth watching.** `/mnt/d/hf_w26` now yields **0** `V28-B` findings — the self-test demonstrates the expiry rather than asserting it — so the `-B` clause needed a fixture that still refuses, and the nearest is the immediately preceding wave. Both design-named roots are **still pinned** (`hf_w24` for `-C`, `hf_w26` for the expiry demonstration), so nothing was swapped out. **But the `-B` fixture is now exactly what F80 said it should not be**, and it will expire the same way next wave. The durable fix is a **synthetic** frozen fixture that cannot expire; ~10 m, named in §12 item 5's bucket |
| 5 | **`W28-S` was scored while the suite ran** (score 11:35:00, suite 11:34:29 → 11:38:20), against the plan's step order | **Immaterial.** `W28-S` reads two source files by sha256 and both hashes still hold at `HEAD 6f2b62b`, which is *after* the suite and the ship commit. Recorded for completeness |

---

## §14 — One-paragraph summary

**The wave's assigned mechanism was decidable from source, and its one measurement split the population it was
aimed at.** `RULE W28-S` = `S-WELDED`, 3 of 3, at zero compute: the post-wavelet blur width is literally
`p.StructureLayers * 2 + 1`, no `StarDetectorParams` member controls it independently, and
`EffectiveStructureLayers` reaches the residual but not the blur — **one knob sets two opposing cutoffs**, which
is the confound sitting under [F43](followups.md)'s closed axis, and F43 is amended in place. `RULE W28-N` =
`N-RECOVERS`, 3 of 3: lowering `NoiseClippingMultiplier` to 1.0 cuts the high-tier structure gap by **79.2 % /
71.3 % / 96.4 %** at precision 1.000, and **`D02` and `D03` convert that into +0.241 and +0.184 of `recall@high`
while `D01` converts it into +0.012** — 97.8 % of its newly formed candidates are re-rejected by a later gate, so
on the extreme rig the loss moved one stage downstream and **this wave did not localise it**. `RULE W28-B` =
`B-SPLIT`, and on the half item 8 is about the band-pass hypothesis is **refuted**: `Δacc` is positive on all 17
blind datasets, `B-3` is 0 of 4, and the predicted sign flip at the lower edge does not occur — above the band
the prediction holds on 9 of 10, below it, not once. The read-only roots are `PRESERVED`, 1 080 of 1 080, and
ship (1) went out unconditionally at suite **3997** by COUNT: a user below the calibrated band is no longer told
they are inside it, and is no longer shown nothing at all.

**Three findings a later reader must not lose.** The design's Step 4 count assertion was **wrong at HEAD** —
three `ConvolveGaussian` sites, not one — and applied literally would have made `W28-S` permanently
`S-UNEVALUATED` on correct source. `W28-N`'s verdict tree **never gates on `N-1`**, leaving 4 of 135 regions
uncovered, which is the **second consecutive wave** to ship a tree with a gap and the first to do it under a
standing rule written in its own design forbidding exactly that. And the blocking pre-flight ran over a root
containing **one file — itself** — returning `V-UNEVALUATED` where the plan required `V-CLEAN`. **Re-run
after the roll call over the populated root it returns `V-CLEAN`, 7 files, 8 of 8 siblings (§6.1), so the defect
is one of SEQUENCING and the instruments are clean.** At the time it ran, none of the
six instruments the wave then wrote was ever checked for derivation drift. **`N-RECOVERS` is a measurement and
not a licence:** nothing in wave 28 pre-registered a rule that would permit changing a default, the optimizer
chose the values it would replace, and a default change owes its own wave with a fresh baseline.
