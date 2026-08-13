# Wave 30 — results

## THE TIMESTAMP — the roll call, taken at an instant

```
$ date -u '+%Y-%m-%dT%H:%M:%SZ'; ls -la --time-style=full-iso /mnt/d/hf_w30/
2026-08-13T19:56:41Z
drwxrwxrwx  ...  2026-08-13 15:55:35.089538800 -0400 .
```

**No row was open at that instant, and that is asserted rather than assumed.** The newest artifact under the
root is `vdrift_w30_CLOSING.txt` at `15:55:59.941 -0400` (`= 19:55:59Z`), **42 seconds before the roll call**;
a `ps` taken in the same call returned **zero** `TestApp` and zero `python3` processes. Wave 29's document had
to mark one row open mid-write; wave 30 has none, and the check was still made, because "nothing looked open"
is not the same fact as "nothing was running."

| row | state at **19:56:41Z** | artifact |
|---|---|---|
| `RULE W30-G` arm | **CLOSED** — `W30G_ARM_END 19:52:10Z`, 5 of 5 cells `exit=0`, 2 cells imported, interlock written by the driver | `w30g_arm.txt`, `w30g_manifest.tsv`, `W30G_MANIFEST_READY`, `g/out/M{0..4}.log` |
| `RULE W30-G` scoring | **CLOSED** 19:52:25Z | `w30g_score.txt` |
| `RULE W30-D` | **CLOSED** 19:53:49Z; manifest + interlock 19:52:56Z | `w30d_score.txt`, `w30d_manifest.tsv`, `W30D_MANIFEST_READY` |
| `RULE V30` (pre-flight, populated root) | **CLOSED** 19:34:09Z; an earlier populated run at 19:27:34Z | `vdrift_w30.txt`, `instrumentcheck/vdrift_populated.txt` |
| `RULE V30` (**closing**) | **CLOSED** 19:55:59Z — **byte-identical** to the opening run | `vdrift_w30_CLOSING.txt` |
| `W30-FP` BEFORE sweep | **CLOSED** 19:24:56Z, `POPULATION 1230` | `fp_BEFORE.txt`, `fp_before_run.txt` |
| `W30-FP` AFTER sweep + verdict | **CLOSED** 19:55:34Z | `fp_AFTER.txt`, `fp_after_run.txt` |
| the six instrument self-tests | **ALL CAPTURED** 19:19:11–19:33:44Z | `w30g_selftest.txt` (59), `w30g_arm_selftest.txt` (58), `w30d_selftest.txt` (37), `vdrift_selftest_w30.txt` (37), `fp_selftest.txt` (27), `instrumentcheck/kcol_selftest.txt` (21) |
| the five `V30` fixture probes | **CLOSED** 18:58:02–18:58:52Z — and see §7.4 on their ordering | `instrumentcheck/probe_hf_w2{4,6,7,8,9}.txt` |
| **Step 7, the results-table `K` column** | **NOT RUN.** The instrument exists and self-tests `21 of 21`; no column was emitted and `docs/synthetic-af-bank-results-table.md` is unchanged | `kcol_w30.py` only |
| `instrumentcheck/vdrift_selftest.txt` | **EMPTY ARTIFACT**, 0 bytes, 19:27:53Z. The real capture is `vdrift_selftest_w30.txt` (§7.5) | — |

**Vessel, and it changed mid-wave at the owner's instruction.** Wave 30 did **not** open a fresh branch.
Waves 27, 28, 29 and 30 all sit on the single branch `ghilios/synthetic-af-bank-followups-wave27` =
**PR #196** (`OPEN`, base `develop`, title *"Waves 27-30: audit the truth-protection correction, the wide-field
gap, F82's carrier, and which gate binds on D01"*). **PR #197 is `MERGED`** (into `…wave27`, i.e. re-targeted
exactly as the design §0 recommended) and **PR #199 is `CLOSED`**. The branch was **rebased onto
`develop @ ec06bec` and force-pushed** with `--force-with-lease`; `ec06bec` is verified an ancestor of `HEAD`
`1e7de32`. **Design §0 argued for a fresh branch with a stacked base, and that decision was overruled by the
owner.** §9 records it as such.

---

## §0 — What this document is allowed to say

Every number below is quoted from an artifact under `/mnt/d/hf_w30/`, from `/mnt/d/hf_w29/`, `/mnt/d/hf_w27/`,
`/mnt/d/hf_w26/` or `/mnt/d/hf_w25/`, or from a file in the repository at `HEAD`, and each is cited. The
pre-registered rules in `docs/synthetic-af-bank-followups-wave30-design.md` are **applied as written**. Where a
rule turned out to be under-specified or directionally wrong, that is reported as a **finding** (§5, §8) — it is
not repaired, and no repaired version is scored.

**Everything below is synthetic-only.** The bank has no real under-sampled dataset
([F35](followups.md), [F43](followups.md)), and `RULE W30-G` is **one dataset, `D01_ultrawide_40mm`**, named as
one dataset everywhere it is quoted.

**The [F22](followups.md) fence, and it is enforced in the instrument, not only in this prose.**
`score_w30g.py` prints the caveat **before its first number**, tags **every** line carrying a recall or
true-positive count with `[F22]`, prints the caveat **again at the verdict**, and closes with:

> *"FORBIDDEN, in writing, by design section 2.7: nothing this wave writes may quote a recall gain on this
> dataset as a product benefit. The deliverable is which gate binds, a goal-3 question."*

On this rig the detector's HFR over-reads by a large factor below ~1.1 px measured HFR and its autofocus
already lands (`BestJ 0.994825`). **Recall recovered here is not a focus improvement and is not quoted as
one.** Every count in §2–§4 carries the `[F22]` tag in its source artifact and is read in the goal-3 frame
only: *is a landed knob sitting at an extreme against the physics?*

**Verdicts, in one table.**

| rule | verdict | population | artifact |
|---|---|---|---|
| **`W30-G`** | **`G-NOT-RECOVERABLE`** — `G-0` True, `G-1` True, `G-2` True, `G-3` False | 5 run cells + 2 imports on `D01_ultrawide_40mm`; 45 frame evaluations | `w30g_score.txt` |
| **`W30-D`** | **`D-STALE`** — `D-0` True, `D-1` False; 6 of 10 debt-claiming rows flagged | wave 29 §11's 14-row table | `w30d_score.txt` |
| **`W30-FP`** | **`PRESERVED`** — 1230 of 1230 byte-identical, 0 moved | 6 declared roots, 1230 files after dedup | `fp_BEFORE.txt` / `fp_AFTER.txt` |
| **`V30`** | **`V-CLEAN`** at the open **and** at the close, the two runs byte-identical | `-A` 12, `-B` 5026, `-C` 667 | `vdrift_w30.txt`, `vdrift_w30_CLOSING.txt` |

The realized clause tuple was **asserted a member of the enumerated set** in all four: `W30-G`
`(True, True, True, False)` over **36** regions, `W30-D` `(True, False)` over **6**, `V30`
`(True, True, True)` over **12**, `W30-FP` `(True, True, True)` over **18**. `uncovered: 0` in every case.

---

## §1 — THE HEADLINE: the outcome was called either way, in writing, before the data

`RULE W30-G` returned **`G-NOT-RECOVERABLE`**. That is **PREDICTION B — this wave's own design §4.6 — and it
refutes PREDICTION A, [F99](followups.md)'s joint-recovery claim.**

Both predictions were written into the pre-registration, in the same section, one paragraph apart, before the
arm ran:

> **Prediction A, inherited — F99's hypothesis: `G-JOINTLY-BOUND`.** `TooDistorted` and `LowSensitivity` bind
> jointly; no single knob recovers `D01`; opening the pair recovers materially more than the sum of the singles.
>
> **Prediction B, this document's own, and it differs: `G-NOT-RECOVERABLE`.** […] I expect a large
> `R_TooDistorted`, `recapture` well above 0.90, and `ΔTP(M3)` **below** the 503-star material bar — i.e. F99's
> mechanism confirmed and F99's *joint recovery* claim refuted.

**Why this is the strongest evidential form this series has produced.** A wave that registers **one** prediction
and then finds it has demonstrated only that its author could guess. The reader has no way to price the guess,
because the counterfactual — what the wave would have written had the number gone the other way — was never
committed to paper. A wave that registers **two rival predictions, attached to two distinct terminal verdicts of
the same enumerated tree**, has no such escape: whichever region the data lands in, some named document is
wrong, in writing, and the wave cannot claim a hit it did not call. Wave 30 landed in `G-NOT-RECOVERABLE`, so
**F99 is refuted at the pre-registered bar and the design is vindicated at it — and had the tuple come back
`(T,T,T,T)`, the design would have been refuted by its own rule** in exactly the way wave 29's headline claim
was (see wave 29's CORRECTION block).

Compare wave 29, which registered `L-JOINTLY-BOUND` alone and got `L-UNEVALUATED`: the rule could not evaluate
its own prediction, and the wave learned that only after the fact. Wave 30's two-prediction form makes the
"after the fact" impossible by construction.

**And Prediction B was right for the reason it gave, not by luck.** Its stated mechanism was: *"a candidate
released by `TooDistorted` at 0.3 is a small, low-fill blob; a 11 % relaxation of a 36 σ bar does not turn a 5 σ
blob into a detection."* The measurement says exactly that, in numbers §4 gives: of the **12 090** candidates
`TooDistorted` releases at 0.3, **9 676 (80.0 %)** are taken straight back by `LowSensitivity`; opening
`LowSensitivity` by 11 % on top of that converts only **92** of those 9 676 (**0.95 %**) into acceptances. The
interaction term is real and is negligible.

---

## §2 — `G-0` HOLDS, and it is load-bearing beyond this wave

`G-0` is seven sub-assertions, each with its own printed population, each able to drive the rule to
`G-UNEVALUATED` on its own. All seven held.

| sub-assertion | population | result |
|---|---|---|
| (i) all run cells `exit=0` | 5 | 0 findings |
| (ii) each cell echoes its intended knobs, read from the report's own `key detector knobs:` line | 7 | 0 mismatched |
| (iii) **`M0` reproduces the published `L0` block field-for-field** | 8 fields (bar: ≥ 8) | **0 disagreeing** |
| (iv) the source gate order is identical across the trees the arm read | 3 trees, chain length 11 | 0 findings |
| (v) the four defocus preconditions of design §4.1(b) hold in the landed tree | 4 | 0 violating |
| (vi) every edited JSON key resolved uniquely | 4 | `MAXDIST→MaxDistortion`, `MINBOX→MinStarBoundingBoxSize`, `NC→NoiseClippingMultiplier`, `SENS→BrightnessSensitivity` |
| (vii) dll sha256 matches the recorded provenance | 2 | manifest, provenance and on-disk dll all agree |

**The control that can fail, and did not.** `M0` re-ran wave 29's `L0` on the same binary, same frames, same
seed tree, and reproduced it **exactly**:

| field | published `L0` | `M0` |
|---|---|---|
| `ACCEPTED-elsewhere` | 701 | 701 |
| `NO CANDIDATE (structure gap)` | 8 931 | 8 931 |
| `REJECTED:Contaminated` | 533 | 533 |
| `REJECTED:LowSensitivity` | 13 612 | 13 612 |
| `REJECTED:NotCentered` | 147 | 147 |
| `REJECTED:OnBorder` | 77 | 77 |
| `REJECTED:TooDistorted` | 14 876 | 14 876 |
| `REJECTED:TooSmall` | 11 372 | 11 372 |

Those eight sum to **50 249**, which is `FN_total(M0)` on the nose. **This is what licensed the two imports**
(`L1`, `L3`), and the licence was granted by a measurement, not by an argument, and the design fixed before the
data that failing it would void the imports and force `G-UNEVALUATED` rather than a fallback.

**Corroborating evidence that all five cells did real work.** Wave 29's two dead cells (`MaxDistortion` raised
to 0.9, above the π/4 ceiling) were its two *fastest*, because nothing survived the gate to do downstream work.
Wave 30's five cells ran **187 / 227 / 194 / 208 / 204 s** against wave 29's 177 s reference — every one of them
**slower** than the reference, i.e. every one of them doing downstream work. Total **1 057 s**, inside the
pre-registered band **725–1 600 s**, against a point prediction of ~950 s.

**The `MaxDistortion` direction was verified in source by the arm itself, at run time.** The arm printed:

> *"distortion gate: rejects when `'fillRatio < effectiveMaxDistortion'`, so the threshold is a MINIMUM fill
> ratio and RELAXING IT MEANS LOWERING IT; the perfect-disk ceiling is PI/4 = 0.7853981634"*

and the **11-gate order was extracted from the per-candidate routine and asserted identical at the working
tree, at `HEAD`, and at B15's commit `29665a62e4f8`** — three trees, `GATE_ORDER_AGREES=yes` in the manifest.
Wave 29 spot-checked one precondition and typed its gate order into a design table; wave 30 typed neither.

**Why `G-0` matters past this wave.** The whole point of (iii) is that it is the same measurement waves 28 and
29 published, taken again on a different day by a different instrument. It reproduced to the digit on eight
independent counts. **So the bank's `golden_eval` attribution block is deterministic and reproducible across
waves on this binary**, which is the premise every gate-count difference in waves 28 and 29 silently relied on.
Combined with `G-1` (§3), it is now checked rather than assumed.

---

## §3 — `G-1` HOLDS: the conservation identity balances EXACTLY on real data, 5 of 5

Design §4.2 specified the attribution model as something that could **fail**:

> `R_g = Σ_{h downstream of g} ΔFN_h + ΔContaminated + ΔACCEPTED-elsewhere + ΔTP`, **exactly, in integers**,
> with `ΔNO-CANDIDATE = 0` and `ΔFN_h = 0` for every `h` strictly upstream of `g`.

**Five single-knob cells, five exact balances, no tolerance, integer equality.**

| cell | knob | gate (position of 11) | `R_g` | balance |
|---|---|---|---|---|
| `M1` | `MaxDistortion` 0.5 → 0.3 | `TooDistorted` (4) | **12 090** | 12 090 == 12 090 ✓ |
| `M2` | `BrightnessSensitivity` 36.333 → 32.333 | `LowSensitivity` (6) | **486** | 486 == 486 ✓ |
| `M4` | `MaxDistortion` 0.5 → 0.2 | `TooDistorted` (4) | **14 425** | 14 425 == 14 425 ✓ |
| `L1` *(import)* | `MinStarBoundingBoxSize` 6 → 3 | `TooSmall` (1) | **9 537** | 9 537 == 9 537 ✓ |
| `L3` *(import)* | `BrightnessSensitivity` 36.333 → 35.333 | `LowSensitivity` (6) | **122** | 122 == 122 ✓ |

Every cell's invariants — `NO-CANDIDATE`, the pre-chain stage `BloomSuppressed`, and every gate strictly
upstream — came back **all zero**, as the model requires.

**This is the first checked model of these reports in eleven waves, and it should be said plainly what it
buys.** The design pre-computed the identity against wave 29's `L0/L1` and `L0/L3` pairs and it balanced
twice; that was a check against **published inputs**. Wave 30 balanced it three more times against cells
**nobody had ever run** — `M1`, `M2` and `M4` are blind by design §8 — and it held to the integer.

**The consequence is the one design §11 named as the `G-MODEL-BROKEN` branch's inverse: waves 28's and 29's
gate-count differences are safe to read.** Had `G-1` failed, the verdict would have been
`G-MODEL-BROKEN`, *"a FINDING: the report's first-rejection-wins attribution does not conserve, and every
gate-count difference in waves 28 and 29 is unsafe."* It did not fail. **First-rejection-wins is the correct
model of `golden_eval`'s attribution block, and every Δ-on-a-bucket that waves 28 and 29 published can be read
as a flow.** That is a licence granted by measurement to two earlier waves' arithmetic, and it is worth more
than this wave's own product answer.

**And the identity's fail end is exercised, not merely asserted.** `score_w30g.py`'s self-test clause `[2]`
moves one bucket by one on a live fixture and the balance breaks; clause `[9]` drives a non-conserving cell all
the way to `G-MODEL-BROKEN` with a non-zero exit. A balance that cannot fail is not evidence.

### 3.1 A reported difference from the design, named by the instrument rather than repaired

The design's §4.1(c) table listed **7** gates and asserted `TooElongated` absent. The arm's run-time extraction
found **11**:

```
TooSmall, OnBorder, TooElongated, TooDistorted, Degenerate, LowSensitivity,
NotCentered, TooFlat, HFRAnalysisFailed, TooLowHFR, Contaminated
```

plus a **pre-chain** stage `BloomSuppressed`, asserted invariant and never spliced into the sequence. The
scorer printed the difference at the top of its own artifact:

> *"the destinations are partitioned ONCE from the chain above. The design's formula names the contamination
> bucket as a separate terminal while source makes it the LAST GATE of this chain; counted twice it would
> double, so it is counted once, as a downstream gate. **This is a reported difference from the design, not a
> repair.**"*

That is the correct handling and it is worth recording as a positive: the design's hand-typed 7-gate table was
wrong, the instrument's extraction was right, the instrument said so in its own output rather than silently
conforming, and the double-count that the design's formula would have produced was avoided. **`G-1`'s exact
balances are themselves the proof that the 11-gate partition is the right one** — the design's 7-gate partition
could not have balanced, because `Contaminated` would have appeared on both sides.

---

## §4 — `G-2` True and `G-3` False: re-capture dominates, and the joint is material but ADDITIVE

### 4.1 The capture matrix — this wave's product, published in full

`C[g → h]`: where every released candidate went. A `.` is a bucket **upstream** of that cell's gate, which the
model says cannot move, and the zero-invariants above are what assert it. **Every count below carries `[F22]`
in the source artifact.**

| cell | gate `g` | `R_g` | OnBorder | TooDistorted | Degenerate | LowSensitivity | NotCentered | TooFlat | Contaminated | ACCEPTED-elsewhere | **ΔTP** |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `L1` | `TooSmall` | 9 537 | 10 | 5 697 | 5 | 3 814 | 7 | 1 | 0 | 0 | **3** |
| `L3` | `LowSensitivity` | 122 | . | . | . | . | 12 | 0 | 4 | 5 | **101** |
| `M1` | `TooDistorted` | 12 090 | . | . | 0 | **9 676** | 931 | 0 | 57 | 330 | **1 096** |
| `M2` | `LowSensitivity` | 486 | . | . | . | . | 52 | 0 | 6 | 44 | **384** |
| `M4` | `TooDistorted` | 14 425 | . | . | 0 | **11 652** | 1 130 | 0 | 61 | 390 | **1 192** |

*(`HFRAnalysisFailed` and `TooLowHFR` are columns of the published matrix and are `0` on every row; they are
omitted here for width and are in `w30g_score.txt` in full.)*

### 4.2 `G-2` = True: **re-capture dominates — what the gates release is taken by gates downstream**

```
sum R_g over ALL single-knob cells   : 36 660
sum capture_g over ALL single-knob cells : 33 884
recapture = sum capture_g / sum R_g  : 0.9243   (bar 0.90)
recapture EXCLUDING the fenced cell M4 : 0.9288
```

**92.4 % of everything these knobs release is re-caught by a gate further down the chain.** The two figures
agree on the verdict, so the fenced cell does not carry it.

The mechanism is legible in one column of the matrix. `TooDistorted` at 0.3 releases 12 090 candidates and
**`LowSensitivity` alone absorbs 9 676 of them — 80.0 %**. At 0.2 it releases 14 425 and `LowSensitivity`
absorbs 11 652 — 80.8 %. **This is [F99](followups.md)'s mechanism, confirmed with a number and on the gate F99
named**, and it is the same shape F99 measured for `MinBox`: `L1` releases 9 537 from `TooSmall` and
`TooDistorted` + `LowSensitivity` take back 9 511 of them.

The design pooled `recapture` rather than comparing per-gate pass-through, and the artifact says why on the
line itself: *"per-gate pass-through is NOT comparable, because the last gate before acceptance has a high
pass-through by POSITION."* That fence is honoured here. **Within one gate across two doses** the comparison is
legitimate, and it is informative:

| gate | dose | `R_g` | `ΔTP` | pass-through |
|---|---|---|---|---|
| `TooDistorted` | 0.5 → 0.3 | 12 090 | 1 096 | 9.1 % |
| `TooDistorted` | 0.5 → 0.2 | 14 425 | 1 192 | 8.3 % |
| `LowSensitivity` | 36.333 → 35.333 | 122 | 101 | 82.8 % |
| `LowSensitivity` | 36.333 → 32.333 | 486 | 384 | 79.0 % |

**The second `MaxDistortion` dose buys almost nothing.** Going 0.3 → 0.2 releases **2 335 more** candidates and
converts **96 more** of them — a **4.1 % marginal pass-through**, less than half the first dose's already-low
9.1 %. The axis's low end is exhausted well before its floor of 0.1.

### 4.3 `G-3` = False: the joint clears the material bar and misses the interaction bar

```
dTP(M3), the joint cell                    : +1 572   [F22]
sum of dTP over the unfenced singles       : +1 480   [F22]
FN_total(M0), the misses being explained   : 50 249   [F22]
the material bar, COMPUTED as 0.01 x FN_total(M0) : 502.5 star(s)   [F22]

dTP(joint) >= material bar (502.5)          : True    [F22]
dTP(joint) >= 2.0 x sum(dTP singles) (2960.0) : False [F22]
```

**Both halves matter and they say different things.**

- **The joint IS material.** 1 572 clears a bar of 502.5 by 3.1×. Opening these two gates together is not a
  null result. It reaches **3.1 % of `FN_total(M0)`**, and 5.5 % of the 28 488 high-tier misses those two gates
  jointly hold.
- **The joint is essentially ADDITIVE, not synergistic.** `1 572` against `1 096 + 384 = 1 480` is an
  interaction term of **+92**, i.e. **+6.2 % over pure additivity**. F99's claim was that the pair binds
  *jointly* — that the whole exceeds the sum because each gate re-catches the other's release. The design set
  the bar for "jointly" at **2× the sum**. The measured multiple is **1.06×**.

`(True, True, True, False)` → **`G-NOT-RECOVERABLE`**, one region of thirty-six, asserted a member of the
enumerated set.

### 4.4 What this answers, in the goal-3 frame the F22 fence permits — and it is a NEGATIVE

The product question of design §1 was: *is any landed knob on `D01` pinned against the physics in a recoverable
way?*

**Answer: no.** The reasoning, entirely from the pre-registered statistic:

1. **`MinStarBoundingBoxSize` landed at 6 against a shipped default of 5** — the one knob that visibly sits at
   an extreme. Its flow (`L1`) releases 9 537 candidates and **3** reach acceptance. Lowering it recovers
   nothing; it is not holding real stars.
2. **`MaxDistortion` landed at 0.5, mid-axis** (declared axis 0.1–1.0, step 0.1). It is not at an extreme, and
   opening it two and three steps yields 9.1 % and 8.3 % pass-through with the balance absorbed one gate later.
3. **`BrightnessSensitivity` is fenced** ([F84](followups.md) / `RULE S16`) and the scorer refuses to name it in
   any verdict string. No statement here is evidence for or against a sensitivity bound.
4. **The misses are structural, not knob-shaped.** Of `FN_total(M0)` = 50 249, the `NO CANDIDATE (structure
   gap)` bucket holds **8 931** and `TooSmall` holds **11 372** — **40.4 % of all misses live upstream of the
   two gates F99 named**, and no setting of either gate can reach them.

**So the wave's own product question closes with a negative, and that is a real answer.** `D01`'s recall gap is
the physics of a 19.4 ″/px rig with a ~0.7 px kernel, not an over-tightened optimizer landing. Design §1 said
this is *"not a recall question; §2.7 fences that"* — and the F22 fence is what makes the negative honest
rather than disappointing: even the joint's 1 572 could not have been quoted as a user benefit on a rig whose
autofocus already lands at `BestJ 0.994825`.

### 4.5 One thing the pre-registered statistic does NOT answer, named rather than back-filled

**Whether opening `MaxDistortion` costs precision was not measured**, and it is the question wave 31's ship
(item 4) actually needs.

The statistic reads only the `FALSE-NEGATIVE ATTRIBUTION, HIGH TIER ONLY` block, which carries no false-positive
count. What it *does* carry is the `ACCEPTED-elsewhere` terminal — a released candidate that was accepted but
did not match the golden star it was released from — and that column is suggestive:

| cell | gate | accepted & matched (`ΔTP`) | accepted, not matched | unmatched share of acceptances |
|---|---|---|---|---|
| `M1` | `TooDistorted` 0.3 | 1 096 | 330 | **23.1 %** |
| `M4` | `TooDistorted` 0.2 | 1 192 | 390 | **24.7 %** |
| `M2` | `LowSensitivity` 32.333 | 384 | 44 | 10.3 % |
| `L3` | `LowSensitivity` 35.333 | 101 | 5 | 4.7 % |
| `L1` | `TooSmall` 3 | 3 | 0 | 0 % |

**The distortion gate's releases arrive with a two-to-five-times higher unmatched-acceptance share than the
sensitivity gate's.** `ACCEPTED-elsewhere` is *not* a false positive — design §4.2 defines it as a candidate
that may be matched elsewhere or fall in a contaminated region — so **this is a signal, not a measurement, and
it is not scored.** It is recorded here because a ship that lowers `MaxDistortion`'s default or floor needs the
real precision number, and §10 prices it as a new debt this wave created.

---

## §5 — `RULE W30-D` = `D-STALE`, and I VERIFIED ITS SIX ROWS MYSELF

`D-0` held: **14 rows parsed**, heading matched exactly once, **0 unresolvable**, 6 of 6 declared roots present,
ledger sha256 and resolution-table sha256 both re-asserted at scoring time. `D-1` came back **False** — **6 of
10 debt-claiming rows** have a satisfying artifact — and the verdict is `D-STALE`, a FINDING, which is the
pre-registered prediction.

**The verdict stands and is not re-decided.** But the design charged this wave with naming every already-paid
row, and **a false "already paid" is exactly as damaging as a false "owed" — worse, in the direction that
matters, because a false "owed" costs you a re-run while a false "already paid" retires an obligation nobody
ever met.** So each of the six was checked against what the ledger row actually claimed.

### 5.1 The six rows, verified

| # | row | matched artifact | genuine? |
|---|---|---|---|
| 1 | **the 42 m `optimize` gate** | `/mnt/d/hf_w29/l/opt/L{0..4}/attempt01/optimize_summary.txt` | **NO — matcher over-reach, twice over** |
| 2 | **the closing `V29`** | `/mnt/d/hf_w29/vdrift_w29_CLOSING.txt`, 18:18:01Z | **YES — genuine, paid 15 min AFTER the ledger** |
| 3 | **`P-D08` at `n = 3`** | `/mnt/d/hf_w26/pd08` + the published `RESULT` section | **YES — genuine, paid 7 h BEFORE the ledger** |
| 4 | **localising `D01`** | `/mnt/d/hf_w30/w30g_manifest.tsv`, 19:52:10Z | **TRUE BUT CIRCULAR — paid by this wave, 13 min before it scored itself** |
| 5 | a full paired 18-cell S1 arm | `/mnt/d/hf_w25/s1_arm_w25.sh` | **NO — matcher over-reach, twice over** |
| 6 | `S27-1` and wave 27's three mutants | `/mnt/d/hf_w27/s27_*` (6 files) | **NO — matcher over-reach** |

**Tally: 2 genuine, 1 true-but-circular, 3 false.**

**Row 1 — FALSE.** The matched files are per-cell optimizer runs from wave 29's own `L`-arm. `L0`'s summary
opens *"Runs root: `D:\SyntheticAutofocusBank\D01_ultrawide_40mm`; **Runs: 1 (attempt01)**"* — **one dataset,
one run.** The row means the `Q27-V1` **8-dataset** gate: 42 m measured, ~5.2 m/run, predicted `G-PASS`, 8 of 8
bit-identical to K8. A single-dataset optimize is not that gate. **And the row does not claim a debt at all** —
its own third column reads *"**NOT OWED this wave, and it is checked rather than asserted.**"* Two independent
errors: wrong referent, and a row that disclaims the debt scored as claiming one.

**Row 2 — GENUINE, and it is the class the rule was built for, one clock-tick displaced.** The spec
`^vdrift_w\d+_CLOSING\.txt$` under the ledger-scoped root is exactly right. `vdrift_w29_CLOSING.txt` has mtime
18:18:01Z. Wave 29's roll call was **18:03:36Z**. **The ledger was true when it was written and became stale
fourteen minutes later.** Design §2.2 had already verified independently that it is a genuine closing run — it
post-dates `fp_AFTER.txt` (18:04:27Z) and the cross-wave check (18:05:04Z) — so the row is correctly flagged.

**Row 3 — GENUINE, and it is the strongest one.** `P-D08` at `n = 3` was measured 09:06–09:08Z, published in
`docs/synthetic-af-bank-followups-w26-pd08-followon.md` under a heading *"`# RESULT — P-D08 is REFUTED at n =
3`"*, and committed as `e42e2df` at 09:09:37Z — **seven hours before wave 29 opened its branch.** The rule
resolved it to scope `any` and found it under `/mnt/d/hf_w26/pd08` and in the docs directory. **This is the row
that justifies the whole rule**, and it is found precisely by the scope distinction the rule makes mechanical.

**Row 4 — TRUE BUT CIRCULAR, and this is a predicate defect, not a matcher defect.** The satisfying artifact is
`w30g_manifest.tsv`, written at **19:52:10Z by wave 30's own arm**, and scored as "already paid" at 19:53:49Z —
**ninety-nine seconds later.** The row was honestly open when wave 29 wrote it; wave 30 discharged it; the rule
then reported the discharge as evidence that the ledger was stale. **`D-1` has no time ordering against the
ledger document's own timestamp**, so a row paid seven hours *before* the ledger (row 3), a row paid fourteen
minutes *after* it (row 2), and a row paid *by the checking wave itself* (row 4) are all scored identically.

**Row 5 — FALSE, twice over.** (a) The match is `s1_arm_w25.sh`, the arm's **shell script**. A script is not a
run; the file's own header describes what it *would* do. (b) The row's own text is *"Unchanged:
`W-UNEXERCISED` means a **second** arm buys a second `SAME 13`."* **The row explicitly concedes that wave 25's
arm ran and declines to run another.** Wave 25's arm is the row's premise, not its discharge.

**Row 6 — FALSE.** The glob `^s27.*$` matched six files from wave 27's **`S27-4` do-no-harm arm**
(`s27_after/`, `s27_arm_w27.sh`, `s27_manifest.tsv`, `s27_score.txt`, `s27_4_score.txt`,
`s27_2_mutant_MS3.log`). The debt is two specific and quite different things, both recorded as open by wave
27's own results:

- **`S27-1`'s route agreement** — §7.3: *"`S27-1`'s route agreement was **not adjudicated**, by the scorer's
  own printed statement"*, and §8.6 adds that the fifth field (`protectedDetections`) is single-routed and needs
  a **pre-registered** second route.
- **`M-S1` / `M-S2a` / `M-S2b` run records** — §7.4: *"`M-S3` now has a run record; `M-S1`, `M-S2a` and `M-S2b`
  remain testimony."*

**The one mutant that IS evidenced is `M-S3`, and `M-S3` was never the debt** — `s27_2_mutant_MS3.log` is
precisely the record wave 27 said it *did* have. Worse, `s27_score.txt` opens with a controller annotation
stating *"THIS FILE'S `>>> RULE R27 = R-DIFFERS` BANNER IS WRONG FOR THIS ARTIFACT AND MUST NOT BE QUOTED"* —
so one of the six matches is a file the series has already marked unquotable. **This row would have retired a
control that has been open for four waves and that two wave-27 ship-clauses relied upon.**

### 5.2 What this says about `W30-D`'s own predicate

**The verdict `D-STALE` survives** — it needs one genuine stale row and there are two (three if row 4 counts) —
but the finding's magnitude is **2, not 6**, and the report must say so.

Four things follow, and none of them is a repair:

1. **`D-1` conflates three different facts** into one boolean: *the ledger was wrong when written* (row 3),
   *the ledger has since been discharged by someone else* (row 2), and *the checking wave discharged it itself*
   (row 4). Only the first is what "a hand-carried ledger rots" means. **A `paid_at < ledger_written_at`
   comparison is the missing predicate**, and both timestamps are already in the rule's own manifest
   (`LEDGER_DOC_SHA` pins the document; every match prints an mtime).

2. **The rule replaced a hand-carried list with a hand-carried regex.** The resolution table's specs —
   `^.*optimize.*\.(txt|log)$`, `^s1_arm.*$`, `^s27.*$` — are **filename globs typed by a human**, exactly the
   kind of artefact the rule exists to stop trusting. Three of the six matched a file whose *name* contains the
   row's word and whose *content* is a different thing entirely. **The list rots the same way; it just rots
   silently, and in the more dangerous direction.**

3. **`D-0` cannot see this, by construction, and that is the transferable class.** `D-0` asserts every row
   resolves to **exactly one** predicate — and it did, 14 of 14, unresolvable 0. **A validity clause that checks
   a row resolves *uniquely* cannot check that it resolves *correctly*.** This is the same shape as §8.1's
   stale-`split()`-needle finding: an assertion that a key is *present* is not an assertion that it is the
   *right* key.

4. **The half of the rule that works is the scope distinction, and it should be kept.** It is what found row 3
   (a MEASUREMENT, scope `any`, discharged wherever its artifact lives), and it is what correctly left **the
   wave-29 BEFORE fingerprint** and **the three scorers' self-test captures** unsatisfied — both scoped
   `ledger`, both genuinely not paid, both correctly reported so. **Every unsatisfied result the rule produced
   is right.** The rule's errors are entirely one-directional: toward false "already paid."

**`RULE W30-D` is not repaired and is not re-scored.** The above is reported, per design §12's standing rule
that *"a clause whose population is unsatisfiable is a FINDING and is not repaired."*

---

## §6 — `W30-FP` = `PRESERVED`, and the trimmed root set paid for itself

**1 230 of 1 230 byte-identical, 0 moved.** Six declared roots, per-root populations identical on both sides,
asserted equal **before any hash was compared** ([F91](followups.md)):

| root | population |
|---|---|
| `/mnt/d/SyntheticAutofocusBank/D01_ultrawide_40mm` | 32 |
| `/mnt/d/SyntheticAutofocusBank` (`-name synthetic_meta.json`) | 19 |
| `/mnt/d/hf_w25/exe` | 248 |
| `/mnt/d/hf_w29` | 181 |
| `/mnt/d/hf_w28/nc` | 310 |
| `/mnt/d/hf_w25/table18` | 440 |
| **after `realpath` dedup (1 of 1 231 dropped)** | **1 230** |

Self-test `27 of 27`, including F91's own shape (*"differing MEMBERSHIP is UNEVALUATED, never a false
VIOLATED"*), the empty-tree refusal, and an explicitly named design finding it did **not** repair:

> *"DESIGN FINDING, NAMED NOT REPAIRED: `/mnt/d/hf_w18/seedA0` is READ by every cell (the seed tree the
> settings are copied from) and is NOT in the declared read-only population, so an accidental write there would
> not be caught by this wave's fingerprint."*

**The §2.6 trim is measured and it delivered.** The sweeps' durations are not directly recorded — see §8.3 —
but they are bounded by the surrounding artifact mtimes: **BEFORE ≤ 60 s** (bounded below by `fp_selftest.txt`
at 19:23:56Z, above by `fp_BEFORE.txt` at 19:24:56Z) and **AFTER ≤ 105 s**. Against **wave 29's measured
11 m 42 s** over 41.8 GB and its ~24 m two-sided owing, and against this design's own 3 m-per-side price. The
17 GB `hf_w25/after` and 15 GB `hf_w25/before` roots were dropped with stated reasons and neither was read.
**Justifying each entry cut a ~24 m obligation to under 3 m, and both sides were actually taken.**

---

## §7 — `RULE V30` = `V-CLEAN` twice, and the series' only falsifiable forward claim hit 4 of 4

### 7.1 Both runs, and they are byte-identical

| run | when | `-A` | `-B` | `-C` | verdict |
|---|---|---|---|---|---|
| open, over the **populated** root | 19:34:09Z | 12 of 12 resolve | 5 026 candidates, **0** findings | 667 candidates, **0** findings | **`V-CLEAN`** |
| **closing** | 19:55:59Z | identical | identical | identical | **`V-CLEAN`** |

`diff` of the two files: **identical**. Realized tuple `(True, True, True)`, **12** regions enumerated,
`uncovered: 0`, membership asserted. Self-test **`37 of 37`**.

**[F95](followups.md)'s remedy ran in both halves from the pre-registration rather than from a debt**, which is
what wave 29 could not say: wave 29 ran the pre-flight over an empty root and paid its closing run late.

### 7.2 [F103](followups.md)'s forward prediction — written one wave early, and all four legs confirmed

Design §7 wrote, before the measurement:

| leg | predicted at wave 29 | **measured at wave 30** | |
|---|---|---|---|
| `/mnt/d/hf_w28` **expires** as the `-B` fixture | 100 findings → **0** | **`-B` = 0** | ✓ |
| `/mnt/d/hf_w29` becomes the new live `-B` fixture | large non-zero | **`-B` = 311**, first at `fp_w29.sh:47` | ✓ |
| `/mnt/d/hf_w24` remains the durable `-C` fixture | **2**, fourth wave running | **`-C` = 2** (`gate_w24.sh:221`, `prov_w24.py:179`) | ✓ |
| `/mnt/d/hf_w26` and `/mnt/d/hf_w27` stay asserted-silent | `-B` = 0 both | **0 and 0** | ✓ |

**Four of four.** The load-bearing artifact is `vdrift_selftest_w30.txt` clause `[5b]`, which re-measures all
five fixtures inline at 19:26:49Z and prints the expiry as a demonstration rather than a description:
*"`/mnt/d/hf_w26` yields ZERO `V30-B` findings — **THE EXPIRY IS DEMONSTRATED**, which is why the `-B` fixture
is pinned separately and re-measured every wave."* The standalone probe captures corroborate it.

**This is the second consecutive test of the only falsifiable forward claim this series makes about its own
instruments, and it passed both times.** A pinned per-clause fixture goes silent exactly one wave later, on
schedule, and an instrument that inherits its fixture pin instead of re-measuring it will report a clean zero
over a dead population.

### 7.3 All three generations of the `historical_ranges` repair are carried and all four ends demonstrated

Self-test clause `[6]` keeps every end all three generations established and invents none:

- generation 1 (wave 27): the block is exempt (index 2) and the file after it is **not** (index 6);
- generation 1 again: the **definition** of `HISTORICAL_BLOCKS` opens nothing — it is not an instance of itself;
- generation 2 (wave 28): a line that *mentions* a marker without opening a bracket opens nothing;
- generation 3 (wave 29, [F101](followups.md)): an unbalanced brace **inside a string** opens nothing, so the
  tail of the file stays inside the checked population;
- and the end a bare *"it reports findings now"* would miss: **a token INSIDE the declared historical literal is
  NOT reported**, so the exemption survives the repair.

F101's durable point — *each generation's self-test tested only the ends the previous generation broke* — is
answered by keeping all four ends in one clause.

### 7.4 A deviation: the BEFORE sweep did NOT precede every read of a declared root

Design §6 put this in bold and priced it:

> **"The BEFORE sweep is the FIRST thing any instrument does — Step 2 […] before `V30`'s fixture probes read
> `/mnt/d/hf_w29` […]. The sweep is not a measurement; it is the baseline the measurements are judged against,
> and it must predate every read of a declared root, including `V30`'s own probes."**

**It did not.** The five fixture probes ran at **18:58:02–18:58:52Z**; `fp_BEFORE.txt` closed at **19:24:56Z**,
**twenty-six minutes later**. `/mnt/d/hf_w29` is declared root #4 and the probe read it (311 `-B` findings).

**Measured consequence: none.** `W30-FP` came back `PRESERVED`, 1 230 of 1 230, 0 moved, so nothing was
written by the probes. **But the requirement the design put in bold was not honoured, and it is the same shape
as wave 29's failure one step milder** — wave 29 took no BEFORE at all and it cost `W29-FP` its verdict on four
of six roots. The lesson is unchanged and the ordering must be enforced by the driver, not by intent.

### 7.5 Two smaller artifact defects, named not repaired

- **`instrumentcheck/vdrift_selftest.txt` is 0 bytes** (19:27:53Z). The real capture,
  `vdrift_selftest_w30.txt`, is 4 637 bytes and says `SELFTEST V30 37 of 37`. Harmless here because the real one
  exists — but **an empty capture in an instrument-check directory reads exactly like a run**, and it is the
  same family as [F95](followups.md)'s empty-population certification.
- **The five probe captures predate the final `verify_derivation_w30.py`** (probes 18:58Z; checker mtime
  19:01:05Z). They were produced by a version of the instrument that is no longer on disk. This does **not**
  weaken §7.2, because clause `[5b]` of the self-test re-measures all five fixtures at 19:26:49Z with the final
  checker and returns the same numbers — but the standalone captures should not be cited on their own.

---

## §8 — FINDINGS, by severity

### 8.1 HIGH — a stale `split()` needle in `score_w30g.py`'s own self-test, which no checker in this apparatus can see

**The defect.** `score_w30g.py`'s self-test clause `[6]` — the [F84](followups.md) / `RULE S16` sensitivity
fence, one of the two fences the design declared load-bearing — sliced the artifact with

```python
reading = txt.split("READING: more than a tenth")[-1].split("FORBIDDEN")[0]
```

**The emitter stopped producing that text when the bar became computed.** `G-SINGLE-BINDS`'s reading line is now
formatted from `G2_BAR` (`score_w30g.py:857`): *"READING: more than %.0f%% of the released mass reached
acceptance…"*. The literal *"a tenth"* no longer appears anywhere in the output.

**Why it was silent.** `str.split()` on an **absent** needle returns a **one-element list**, so `[-1]` handed
back **the whole document**. The check below it — *"the fenced name appears in no verdict sentence"* — stopped
being about a verdict sentence at all and became a document-wide absence check that passes trivially, because a
correct run never emits the phrase anywhere. **It would have passed forever, on every future wave, while
asserting nothing.**

**What caught it, and what could not — this is the transferable half.**

- The **blocking pre-flight** flagged the stale phrase **only where it was printed.** It found the emitter's
  copy, not the self-test's.
- **`V30-C` structurally cannot see this copy.** `V30-C` scans **printing lines** for typed ordinals and counts;
  its population this wave was **667 lines** — *"printing lines 980, of which 273 carry a format slot and are
  skipped."* **A needle passed to `split()` is not printed output.** It is excluded from `V30-C`'s population by
  construction, not by accident.

> **A checker that looks for typed ordinals in printed output cannot find a stale ordinal used as a KEY.**

**The remedy, local and permanent.** Assert the anchor is **present**, and assert the slice is a **proper**
substring so it can never widen to the whole text again:

```python
READING_ANCHOR = "READING: more than "
check(READING_ANCHOR in txt, "the verdict-sentence anchor is PRESENT, so the slice below cannot silently widen")
reading = txt.split(READING_ANCHOR)[-1].split("FORBIDDEN")[0]
check(reading != txt and len(reading) < len(txt), "and the slice is a PROPER substring of the output, not the whole document")
```

**The self-test went `57 of 57` → `59 of 59`** — `instrumentcheck/score_g_selftest.txt` (19:19:11Z) versus
`w30g_selftest.txt` (19:33:44Z) — and the anchor was shortened to the stable prefix so a change in the bar's
formatting cannot re-break it.

**Class.** This is [F102](followups.md)'s class — *a clause whose population quietly went empty while still
printing a pass* — recurring in the wave that built F102's remedy, and in a form F102's remedy does not cover.
F102's remedy makes populations **printable and refusable**; that works for populations the instrument
*computes*. **The slice above is a population the instrument never counted**, so there was nothing to print.
The generalisation: **`str.split()`, `str.partition()`, `re.search().group()` and every other "locate by
literal" idiom silently degrades to a no-op or to the identity when its literal goes stale, and a population
counter placed on the *output* cannot observe the *key*.**

### 8.2 HIGH — `W30-D`'s row predicates are filename globs, and 3 of its 6 findings are over-reach

Fully set out in §5.2. The one-line form: **a ledger checker built to stop a hand-carried list from rotting
was itself built on a hand-carried list of filename regexes, and it rots in the direction that retires
obligations nobody met.** Its scope machinery is sound and every *unsatisfied* result it produced is correct;
every one of its three errors is a false "already paid."

### 8.3 MEDIUM — the fingerprint records no start time, so the sweep rate could not be re-measured

`fp_BEFORE.txt` and `fp_AFTER.txt` open directly with `ROOT …` and close with the population assertion. Neither
carries a start timestamp or an elapsed figure, so the ~61 MB/s constant wave 29 measured could only be
**bounded** this wave (§6), not re-measured. Wave 29 paid ~12 m per sweep on instinct-priced roots precisely
because that constant did not exist yet; **losing the ability to refresh it is a slow leak.** Two lines in
`w30_sweep` fix it.

### 8.4 MEDIUM — `M4` is inside `G-2`'s pooled `recapture` despite being declared "fenced out of every verdict branch"

Design §4.3 and §13 both say `M4` is *"fenced out of every verdict branch by construction, so cutting it changes
no verdict."* In the implementation `M4` is in `G-1` (correctly — it is a conservation check) **and in `G-2`'s
pooled `Σ R_g`**, which does carry a verdict. **The scorer handled this honestly**, printing both figures and
the reconciliation:

```
recapture = sum capture_g / sum R_g       : 0.9243  (bar 0.90)
recapture EXCLUDING the fenced cell(s) M4 : 0.9288
the two agree on G-2, so the fenced cell does not change this verdict
```

So no verdict moves and the claim *"cutting it changes no verdict"* is true **as measured**. But it was not
true **by construction**, which is what the design asserted, and had the two figures straddled 0.90 the wave
would have had a fenced cell deciding a verdict. Named, not repaired.

### 8.5 LOW — the design's hand-typed 7-gate chain was wrong; the instrument's extraction was right

Covered in §3.1. Recorded as a **positive** about the instrument and a **negative** about typing an ordering
into a design table. Design §14's own standing constraint — *"ordinals and counts COMPUTED, never typed,
including the gate order"* — is exactly why the arm caught it.

### 8.6 LOW — an empty capture and stale probe captures

Covered in §7.5.

---

## §9 — CONTROLLER DEVIATIONS, all five, stated plainly

**(i) ONE PR, at the owner's instruction, overruling the design.** Design §0 pre-registered a fresh branch
`ghilios/…wave30` off `…wave29` with `--base ghilios/…wave29`, and argued at length that *"the mechanism that
stops accretion is the PR base, and nobody set it,"* so that wave 30's PR would be *"the first PR in this family
whose diff is one wave."* **The owner instructed instead: one PR for all four waves.** #197 was re-targeted to
`…wave27` and **merged**; **#199 was closed**; waves 27–30 now sit on the single branch
`ghilios/synthetic-af-bank-followups-wave27` = **PR #196**, base `develop`.

**Recorded as an overrule, not as a design success.** The honest split is: the design's **diagnosis** — that the
PR *base*, not the branch, is what stops accretion, and that #197's `develop` base made its own diff carry three
waves — was correct and was acted on (the re-targeting the design recommended and declined to do itself was
done). The design's **prescription** — a fourth branch with a stacked base, one wave per PR — was rejected in
favour of consolidation. **Design §0's stated goal of a one-wave PR diff was not achieved and will not be.**

**And the consolidation has one measured consequence for this wave's own gate check.** Design §9's reversal
check is `git diff --name-only <base> HEAD -- '*.cs'`. Against the **PR's** base it is **not empty** — it
returns **7 C# files** from wave 27's ship (`3584c30`) and wave 28's ship (`5cdef12`):

```
Joko.NINA.Plugins.HocusFocus.Tests/Golden/TruthDisclosureTests.cs
Joko.NINA.Plugins.HocusFocus.Tests/Utility/DetectionBinningResolverTests.cs
Joko.NINA.Plugins.HocusFocus/Utility/DetectionBinningResolver.cs
TestApp/BankVerifyRunner.cs
TestApp/GoldenEvalRunner.cs
TestApp/SynthBank/GoldenFromTruth.cs
TestApp/SynthBank/TruthDisclosure.cs
```

Against **wave 30's own base** (`0b86a2e`, wave 29's close) it is **empty**, and
`git ls-files --others --exclude-standard -- '*.cs'` is **empty**. **Wave 30 changed no C# and the 42 m gate is
not owed by wave 30**; those seven files' gate obligations belong to waves 27 and 28. **But `<base>` in design
§9 was written assuming one wave per PR, and the consolidation makes it ambiguous** — a later reader running
§9's check as literally written against the PR base would conclude wave 30 owes a gate it does not. The check
must name the **wave's** base explicitly from here on.

**(ii) The branch was rebased onto `develop @ ec06bec` and force-pushed mid-wave**, at the owner's standing
instruction, with `--force-with-lease`. `ec06bec` (*"Merge pull request #198 from ghilios/ghilios/af-row-wrap-narrow"*)
is verified an ancestor of `HEAD` `1e7de32`. **It was done between waves, with no wave-30 measurement yet
taken** — the pre-registration commit was the only wave-30 commit in the branch at the time — so no artifact,
manifest, provenance hash or binary reference is affected, and `W30-FP` (`PRESERVED`) confirms nothing on disk
moved. Recorded because a force-push during a measurement wave is the kind of thing that has to be either
declared or discovered.

**(iii) A false alarm on the arm, and the alarm was mine.** I checked for a live `TestApp` process **6 seconds
after launch**, saw zero, and reported a possible abort — **before confirming the arm was alive in its
settings-edit phase.** The arm copies and edits a settings tree before it invokes the binary, so a zero process
count at t+6 s is the expected reading, not a symptom. **The check was right to make** — an arm that dies
silently and leaves a partial manifest is a real failure mode this series has paid for — **and the alarm was
mine, not the arm's.** The remedy is a threshold, not fewer checks: the arm's own first cell header
(`=== W30G M0 … 19:34:40Z ===`) appeared 7 s after `W30G_ARM_START`, and *that* line, not `ps`, is the liveness
signal.

**(iv) The BEFORE fingerprint did not precede `V30`'s fixture probes** — §7.4. Design §6's bolded ordering
requirement was not met; measured consequence none.

**(v) Step 7, the results-table `K` column, was not run.** `kcol_w30.py` was written and self-tests `21 of 21`;
no column was emitted and `docs/synthetic-af-bank-results-table.md` is unchanged. Per design §13's own rule for
this item — *"if it is cut, it must be WITHDRAWN or paid, not re-listed"* — **§10 withdraws it.**

---

## §10 — WHAT WAS NOT RUN, AS A CHECKABLE LEDGER

**`W30-D` is the reason this section has a different shape.** Wave 29's §11 was prose in a third column, and
`W30-D` showed what happens next: a later wave inherits it, and — however the checking is done — some rows rot
in one direction and some in the other. **So every row below carries a literal search path that returns the
answer**, and the rows this wave will not pay are **withdrawn by name**, not carried.

| # | not run | price | scope | **search path — run this to check the row** | state |
|---|---|---|---|---|---|
| 1 | the 42 m `optimize` gate (`Q27-V1`, 8 datasets) | 42 m | wave 30 | `git diff --name-only 0b86a2e..HEAD -- '*.cs'` ∪ `git ls-files --others --exclude-standard -- '*.cs'` → **both empty** | **NOT OWED** — run, not asserted (§9). Owed by the wave that ships anything from design §10 |
| 2 | the unit suite | ~4 m | wave 30 | same command as row 1 | **VACUOUSLY SATISFIED.** Baseline for the wave that changes C# is **3997**, not 3973 |
| 3 | **the results-table `K` column** | 12 m | cross-wave | `ls /mnt/d/hf_w30/w30k_*.txt` → absent; `grep -c hfrMinEffective docs/synthetic-af-bank-results-table.md` → **0** | **WITHDRAWN.** Owed by waves 28, 29 **and** 30 and unpaid by all three. Design §13: a debt carried three waves is one nobody intends to pay. `kcol_w30.py` stays on disk (self-test `21 of 21`); any later ship can run it in ~2 m |
| 4 | **`S27-1`'s route agreement + `M-S1`/`M-S2a`/`M-S2b` records** | ~20 m | wave 27 | `grep -l 'S27-1' /mnt/d/hf_w27/*.txt`; `ls /mnt/d/hf_w27/s27_2_mutant_MS{1,2a,2b}.log` → absent | **WITHDRAWN**, formally, after **four** waves on the cut list — per design §16 and wave 29 §13. §5.1 row 6 shows `W30-D` falsely reported it paid |
| 5 | wave 29's BEFORE fingerprint | ~12 m | wave 29 | `ls /mnt/d/hf_w29/fp_BEFORE.txt` → absent | **WITHDRAWN — UNRECOVERABLE.** A BEFORE taken now is an AFTER. Correctly reported unsatisfied by `W30-D` |
| 6 | wave 29's three scorer self-test captures | ~5 m | wave 29 | `ls /mnt/d/hf_w29/w29{s,r,l}_selftest.txt` → absent | **WITHDRAWN.** A retrospective capture proves the scorers pass *now*, which is not the gate. Wave 30 captured its **own six** (§ roll call), which is the habit that matters |
| 7 | **precision at the opened `MaxDistortion` settings** | ~5 m | **NEW, created by wave 30** | `grep -n 'precision\|FALSE POSITIVE' /mnt/d/hf_w30/g/out/M{1,3,4}/attempt01/golden_eval.txt` | **OWED by any ship that lowers `MaxDistortion`.** §4.5: 23–25 % of the distortion gate's acceptances did not match the star they were released from. The pre-registered statistic cannot resolve it |
| 8 | `MaxDistortion` doses below 0.2 | ~3 m each | wave 30 | `grep maxdist_intended /mnt/d/hf_w30/w30g_manifest.tsv` → 0.5, 0.3, 0.2 | **NOT OWED.** The axis has 10 steps; wave 30 measured **3** and says so. §4.2 shows the marginal dose already yields 4.1 % |
| 9 | a full paired 18-cell S1 arm | ~2 h 40 m | cross-wave | `ls /mnt/d/hf_w25/{before,after}` → present, from wave 25's arm | **NOT OWED.** `W-UNEXERCISED`: a **second** arm buys a second `SAME 13`. §5.1 row 5 shows `W30-D` falsely reported it paid |
| 10 | new datasets between 1.4 and 19.4 ″/px | ≥ 1 h render | cross-wave | `ls -d /mnt/d/SyntheticAutofocusBank/D*` → 20, none in the band | **OPEN, twelve waves.** The coverage hole, and the only route to a **blind** wide-field dataset. Outside `W30-D`'s declared search set, which the rule correctly reported as `CNL-OUT-OF-REACH` rather than clean |
| 11 | the blur/layer decoupling build ([F92](followups.md)) | ~45 m + 10 m (+42 m to ship) | cross-wave | `ls /mnt/d/hf_w3*/exe` → only `/mnt/d/hf_w25/exe` and `/mnt/d/hf_w27/exe` | **OPEN**, unchanged |
| 12 | the `A4` truth-model gap | 20 m code **+ 42 m gate** | cross-wave | `grep -rn 'truthModel' Joko.NINA.Plugins/TestApp/SynthBank/` | **OPEN**, and [F96](followups.md) makes it more urgent, not less |
| 13 | anything on real frames | — | permanent | — | The bank has no real under-sampled dataset ([F35](followups.md), [F43](followups.md)) |

**Four withdrawals in one section is the point, not an accident.** Rows 3–6 have been carried between two and
four waves each. `W30-D` proves a carried row is not free: it accumulates until something has to check it, and
the checking is where the errors are. **A row that will not be paid should be deleted with a reason, and these
four are.**

---

## §11 — REGISTER ENTRIES, READY TO PASTE

The register's highest existing entry is **F103**. These continue from it. Design §16 sketched F104–F107 under
a different assignment; the numbering below is the one that ships, and the mapping is noted where it differs.

---

### F104 — `D01`'s two candidate gates release 36 660 high-tier candidates and re-capture takes 92 %: the joint is material and ADDITIVE, so F99's joint-recovery claim is refuted at a pre-registered bar

**Status:** Closed — the product question is answered, and answered negatively · found 2026-08-13, wave 30,
`RULE W30-G` = **`G-NOT-RECOVERABLE`** · `/mnt/d/hf_w30/w30g_score.txt`, `w30g_manifest.tsv`

**[F22](#f22) FENCE, inline and mandatory:** on `D01_ultrawide_40mm` the detector's HFR over-reads by a large
factor below ~1.1 px measured HFR and its autofocus already lands at `BestJ 0.994825`. **No count in this entry
may be quoted as a recall or focus improvement for a user.** The only frame in which it may be read is goal 3 —
*is a landed knob sitting at an extreme against the physics?*

A 2×2 factorial on `MaxDistortion` (0.5 → 0.3) and `BrightnessSensitivity` (36.333 → 32.333, four axis steps
**computed** from the product's own declaration), five run cells and two imported from wave 29, on
`D01_ultrawide_40mm`, 9 frames per cell, on the B15 binary already on disk.

| quantity | value |
|---|---|
| `Σ R_g` over all single-knob cells | **36 660** |
| `Σ capture_g` | **33 884** |
| `recapture` (pooled) | **0.9243** — bar 0.90 → **`G-2` True** |
| `recapture` excluding the fenced `M4` | 0.9288 (agrees) |
| `FN_total(M0)` | **50 249** |
| material bar, **computed** as `0.01 × FN_total(M0)` | **502.5** |
| `ΔTP(M3)`, the joint | **+1 572** |
| `Σ ΔTP` over the unfenced singles `M1`+`M2` | **+1 480** |
| `ΔTP(joint) ≥ 502.5` | **True** |
| `ΔTP(joint) ≥ 2 × 1 480 = 2 960` | **False** → **`G-3` False** |

**The joint IS material** (3.1× the bar; 3.1 % of all misses) **and it is essentially additive**: the
interaction term is **+92**, a multiple of **1.06×** against a pre-registered bar of 2×.

**Re-capture dominates, and the mechanism is one column of the published matrix.** `TooDistorted` at 0.3
releases **12 090** candidates and **`LowSensitivity` absorbs 9 676 of them — 80.0 %.** Relaxing
`LowSensitivity` by 11 % on top converts **92** of those 9 676 — **0.95 %**. A 36 σ bar relaxed by 11 % does not
turn a low-fill blob into a detection, which is exactly the mechanism the pre-registration gave for predicting
this outcome.

**Goal-3 answer, and it is a negative.** No landed knob on `D01` is pinned against the physics in a recoverable
way. `MinStarBoundingBoxSize` at 6 (against a shipped default of 5) is the one knob at an extreme, and its flow
releases 9 537 candidates of which **3** reach acceptance. `MaxDistortion` landed mid-axis. **40.4 % of the
misses live upstream of both gates** — `NO CANDIDATE (structure gap)` 8 931 and `TooSmall` 11 372 — where no
setting of either gate can reach them. `D01`'s recall gap is the physics of a 19.4 ″/px rig with a ~0.7 px
kernel.

**Two rival predictions were registered before the data**, in the same pre-registration section, attached to
two distinct terminal verdicts of the same 36-region tree: `G-JOINTLY-BOUND` (F99's) and `G-NOT-RECOVERABLE`
(the design's). The data landed in the second. **Neither document could have claimed a hit it did not call**,
and this is the strongest evidential form the series has produced.

**Not measured, and owed by any ship that acts on this:** precision at the opened settings. 23–25 % of the
distortion gate's acceptances were `ACCEPTED-elsewhere` — accepted but not matched to the star they were
released from — against 5–10 % for the sensitivity gate. That is a signal, not a measurement; the pre-registered
statistic reads only the false-negative attribution block. ~5 m to resolve.

*(Design §16 assigned F104 to this verdict; the assignment holds.)*

---

### F105 — `golden_eval`'s first-rejection-wins attribution CONSERVES on real data, 5 of 5 cells, integer-exact — so waves 28's and 29's gate-count differences are safe to read

**Status:** Closed — the model is checked · found 2026-08-13, wave 30, `RULE W30-G` clause `G-1` = HOLDS ·
`/mnt/d/hf_w30/w30g_score.txt`

Relaxing exactly one gate `g` changes no other threshold, so the report's attribution must satisfy, **exactly,
in integers**:

> `ΔNO-CANDIDATE = 0`; `ΔFN_h = 0` for every `h` strictly upstream of `g`; and
> `R_g = Σ_{h downstream of g} ΔFN_h + ΔContaminated + ΔACCEPTED-elsewhere + ΔTP`

**Five single-knob cells, five exact balances, no tolerance**, with every upstream invariant and the pre-chain
`BloomSuppressed` stage returning zero:

| cell | gate (position of 11) | `R_g` | balance |
|---|---|---|---|
| `M1` | `TooDistorted` (4) | 12 090 | ✓ |
| `M2` | `LowSensitivity` (6) | 486 | ✓ |
| `M4` | `TooDistorted` (4) | 14 425 | ✓ |
| `L1` | `TooSmall` (1) | 9 537 | ✓ |
| `L3` | `LowSensitivity` (6) | 122 | ✓ |

Two of these (`L1`, `L3`) were pre-computed from wave 29's published artifacts before the pre-registration and
balanced then. **Three (`M1`, `M2`, `M4`) were blind by design §8 and balanced on first contact.**

**What it licenses.** The `G-MODEL-BROKEN` branch of the tree read: *"the report's first-rejection-wins
attribution does not conserve, and **every gate-count difference in waves 28 and 29 is unsafe.**"* It did not
fire. **First-rejection-wins is the correct model of the `FALSE-NEGATIVE ATTRIBUTION` block, and every
Δ-on-a-bucket waves 28 and 29 published can be read as a flow rather than as a coincidence.** This is the first
checked model of these reports in eleven waves, and it was granted by measurement, not by argument.

**It can fail, and the failure is exercised.** Self-test `[2]` moves one bucket by one on a live fixture and the
balance breaks; `[9]` drives a non-conserving cell to `G-MODEL-BROKEN` with a non-zero exit.

**A design difference the instrument reported rather than repaired.** The design's hand-typed §4.1(c) chain had
**7** gates; the arm's run-time extraction found **11** (`…, TooFlat, HFRAnalysisFailed, TooLowHFR,
Contaminated`) plus the pre-chain `BloomSuppressed`. The design's formula named contamination as a separate
terminal **and** the chain contains it as the last gate; counted twice it would have doubled. The scorer
partitioned once, printed the difference, and **the five exact balances are themselves the proof that the
11-gate partition is right** — the 7-gate one could not have balanced.

---

### F106 — A hand-carried debt ledger rots in BOTH directions, and the rot is measured: `P-D08` was published seven hours before the wave that listed it as owed

**Status:** Open — the class is structural · found 2026-08-13, wave 30, `RULE W30-D` = **`D-STALE`** ·
`/mnt/d/hf_w30/w30d_score.txt`

`RULE W30-D` parsed wave 29's §11 *"WHAT WAS NOT RUN"* table out of the committed document (sha256 re-asserted
at scoring time), resolved each of **14** rows to exactly one predicate with a declared **scope**, and searched
six roots plus the docs directory. `D-0` held (14 parsed, heading matched once, 0 unresolvable, 6 of 6 roots
present). `D-1` returned **False**: 6 of 10 debt-claiming rows have a satisfying artifact.

**Two rows are genuinely stale**, and one of them is why the rule exists:

- **`P-D08` at `n = 3`** — measured 09:06–09:08Z, published under a heading *"`# RESULT — P-D08 is REFUTED at
  n = 3`"*, committed `e42e2df` at 09:09:37Z, artifacts at `/mnt/d/hf_w26/pd08/`. Wave 29's ledger listed it
  **owed** at 18:05Z the same day. **Seven hours, inside a single day's run.** The cause: the search was for an
  artifact *under the wave's own root* rather than for the measurement wherever it lives.
- **the closing `V29`** — `vdrift_w29_CLOSING.txt`, 18:18:01Z, fourteen minutes **after** wave 29's roll call.
  The ledger was true when written and stale by the time it was read.

**This is [F85](#f85)'s class recurring** — a closed, shipped correction re-measured as an open defect — **and
the gap is now under twelve hours.** The remedy is not to fix a row. It is to stop inheriting the list: resolve
each row to a **checkable predicate with a declared scope**, where a **per-wave obligation** is discharged only
under the ledger wave's own root and a **measurement** is discharged wherever its artifact lives. Applying the
per-wave scope to a measurement row is the exact error that produced this stale debt, and the scope distinction
is what found it.

**Wave 30's own §10 ledger is written in that form** — one literal search command per row — and **withdraws
four rows** rather than carrying them a further wave.

*(Design §16 assigned F106 to the stale debt; the assignment holds. See F107 for the rule's own defect.)*

---

### F107 — A ledger checker whose row predicates are FILENAME globs converts a false "owed" into a false "already paid", which is the worse error: 3 of 6 on its first run

**Status:** Open — diagnosed, unrepaired by design · found 2026-08-13, wave 30, verifying `RULE W30-D`'s own
output · `/mnt/d/hf_w30/w30d_score.txt`, `/mnt/d/hf_w29/l/opt/L0/attempt01/optimize_summary.txt`,
`/mnt/d/hf_w25/s1_arm_w25.sh`, `/mnt/d/hf_w27/s27_*`

`RULE W30-D` named **six** already-paid rows. Checked against what each ledger row actually claimed:
**two genuine, one true-but-circular, three false.**

| row | matched | verdict on the match |
|---|---|---|
| **the 42 m `optimize` gate** | `hf_w29/l/opt/L{0..4}/…/optimize_summary.txt` | **FALSE.** Each is *"Runs root: `D01_ultrawide_40mm`; **Runs: 1**"* — a per-cell optimize from wave 29's own arm, not the 8-dataset `Q27-V1` gate. **And the row's own text reads "NOT OWED this wave"** — a row that disclaims the debt was scored as claiming one |
| a full paired 18-cell S1 arm | `hf_w25/s1_arm_w25.sh` | **FALSE, twice.** The match is a **shell script**, not a run record; and the row's own text says *"a **second** arm buys a second `SAME 13`"* — wave 25's arm is the row's premise, not its discharge |
| `S27-1` and wave 27's three mutants | `hf_w27/s27_*` (6 files) | **FALSE.** The glob matched wave 27's **`S27-4`** do-no-harm arm. The debt is `S27-1`'s **route agreement** (wave 27 §7.3: *"not adjudicated, by the scorer's own printed statement"*) and the **`M-S1`/`M-S2a`/`M-S2b`** records (§7.4: *"remain testimony"*). The one evidenced mutant, `M-S3`, was **never the debt**. One of the six matches, `s27_score.txt`, is a file the series has already annotated as unquotable |
| localising `D01` | `hf_w30/w30g_manifest.tsv` | **TRUE BUT CIRCULAR.** Written by **wave 30's own arm** at 19:52:10Z and scored "already paid" at 19:53:49Z — **99 seconds later** |

**Three defects, in increasing order of transferability.**

1. **The specs are hand-typed filename regexes** — `^.*optimize.*\.(txt|log)$`, `^s1_arm.*$`, `^s27.*$`. The
   rule replaced a hand-carried *list* with a hand-carried *regex*. **It rots the same way and it rots
   silently**, because a glob that matches too much produces a confident wrong answer where a glob that matches
   nothing produces a visible "not satisfied."

2. **`D-1` has no time ordering.** A row paid seven hours *before* the ledger, a row paid fourteen minutes
   *after* it, and a row paid *by the checking wave itself* all score identically. `paid_at <
   ledger_written_at` is the missing comparison, and **both timestamps are already in the rule's own
   manifest.** Without it, a wave that discharges an inherited debt will report itself as evidence that the
   ledger rotted.

3. **The validity clause cannot see any of this, by construction.** `D-0` asserts every row resolves to
   **exactly one** predicate — 14 of 14, unresolvable 0, correctly. **A validity clause that checks a row
   resolves *uniquely* cannot check that it resolves *correctly*.** This is the same shape as [F108](#f108):
   asserting a key is *present* is not asserting it is the *right* key.

**The direction of the error is what makes this high severity.** A false "owed" costs a re-run. **A false
"already paid" retires an obligation nobody met** — row 1 would have retired the 42 m gate, row 6 a control
open four waves that two wave-27 ship-clauses relied upon.

**What works and should be kept:** the **scope distinction**. Every *unsatisfied* result the rule produced is
correct — the wave-29 BEFORE fingerprint and the three scorer self-test captures were both scoped `ledger` and
both correctly reported not satisfied — and the scope is what found `P-D08`. **The rule is not repaired and not
re-scored**; `D-STALE` stands on its two genuine rows.

---

### F108 — A stale literal passed to `split()` silently widened a self-test's slice to the whole document, and no checker in this apparatus can see it: a stale ordinal used as a KEY is not printed output

**Status:** Closed in the instrument (fixed and the fix asserted) · Open as a class · found 2026-08-13, wave 30
· `/mnt/d/hf_w30/score_w30g.py:1176-1192`, `instrumentcheck/score_g_selftest.txt` (57) vs
`w30g_selftest.txt` (59)

`score_w30g.py`'s self-test clause `[6]` — the [F84](#f84) / `RULE S16` sensitivity fence, one of two fences the
design declared load-bearing — sliced the artifact with the needle **`"READING: more than a tenth"`**. **The
emitter stopped producing that text when the material bar became computed**: the `G-SINGLE-BINDS` reading line
is now formatted from `G2_BAR` (`:857`, *"READING: more than %.0f%% of the released mass reached acceptance…"*)
and the literal *"a tenth"* appears nowhere in the output.

**`str.split()` on an absent needle returns a ONE-ELEMENT list**, so `[-1]` handed back **the whole document**.
The assertion below it — *"the fenced name appears in no verdict sentence"* — stopped being about a verdict
sentence and became a document-wide absence check that passes trivially. **It would have passed forever while
asserting nothing.**

**What caught it, and what structurally could not:**

- the **blocking pre-flight** flagged the stale phrase **only where it was printed** — it found the emitter's
  copy, not the self-test's;
- **`V30-C` cannot see this copy at all.** `V30-C` scans **printing lines** for typed ordinals and counts; its
  population this wave was **667** (*"printing lines 980, of which 273 carry a format slot and are skipped"*).
  **A needle passed to `split()` is not printed output**, so it is outside `V30-C`'s population by construction.

> **A checker that looks for typed ordinals in output cannot find a stale ordinal used as a KEY.**

**The remedy is local and permanent:** assert the anchor is **present**, and assert the slice is a **proper**
substring so it can never widen to the whole text again. The anchor was shortened to the stable prefix
`"READING: more than "`. **Self-test `57 of 57` → `59 of 59`.**

**Class and generalisation.** This is [F102](#f102)'s class — *a clause whose population quietly went empty
while still printing a pass* — recurring **in the wave that built F102's remedy**, in a form that remedy does
not cover: F102 makes populations printable and refusable, which works for populations the instrument
**computes**, and **this slice was a population nothing ever counted.** The general rule: **`split()`,
`partition()`, `find()`, `re.search(...).group()` and every other locate-by-literal idiom degrades silently to a
no-op or to the identity when its literal goes stale, and a population counter on the *output* cannot observe
the *key*.** Every such call needs its key asserted present and its result asserted a proper part.

---

### F109 — Waves 27–30 ship as ONE PR at the owner's instruction, and the design's stacked-base prescription was overruled: only the PR base prevents accretion, but consolidation is the owner's call

**Status:** Closed — decided by the owner · 2026-08-13, wave 30 · `gh pr list`, `git merge-base`

**Measured at wave 30's pre-registration:** #196 (`…wave27`) and #197 (`…wave29`) both targeted **`develop`**,
so #197's own diff carried waves 27, 28 **and** 29 plus wave 27's C# ship — **25 files.** Wave 29 had opened a
fresh branch specifically so *"#196 does not carry a third section"* and succeeded at that; **it did not stop
the accretion, it moved it.** The design's diagnosis was that the mechanism is the PR **base**, not the branch.

**The diagnosis was acted on; the prescription was rejected.** The design pre-registered a fourth branch with
`--base ghilios/…wave29`, to make *"the first PR in this family whose diff is one wave."* **The owner instructed
one PR instead.** #197 was re-targeted to `…wave27` (the design's own §0 recommendation, which it had declined
to perform itself) and **merged**; **#199 was closed**; the branch was **rebased onto `develop @ ec06bec` and
force-pushed** with `--force-with-lease`. Waves 27–30 now sit on `ghilios/synthetic-af-bank-followups-wave27` =
**PR #196**, base `develop`, titled *"Waves 27-30: …"*.

**Design §0's goal of a one-wave PR diff was not achieved and will not be.** Recorded as an overrule, not as a
design success.

**One measured consequence for the gate check.** Design §9's reversal check is
`git diff --name-only <base> HEAD -- '*.cs'`. Against the **PR's** base it returns **7 C# files** from waves
27's and 28's ships. Against **wave 30's own** base (`0b86a2e`) it is **empty**, as is
`git ls-files --others --exclude-standard -- '*.cs'`. **Wave 30 changed no C# and owes no gate** — but `<base>`
was written assuming one wave per PR, and a later reader running the check literally against the PR base would
conclude otherwise. **From here on the check must name the wave's base explicitly.**

---

### AMENDMENTS TO EXISTING ENTRIES

**[F99](#f99) — AMEND IN PLACE.** Append:

> **AMENDED 2026-08-13, wave 30, `RULE W30-G` = `G-NOT-RECOVERABLE`
> (`/mnt/d/hf_w30/w30g_score.txt`).**
>
> **The mechanism holds and is now quantified on the gate this entry named.** `TooDistorted` at
> `MaxDistortion` 0.3 releases **12 090** high-tier candidates on `D01` and **`LowSensitivity` re-captures
> 9 676 of them — 80.0 %.** At 0.2: 14 425 released, 11 652 re-captured. **This entry's central observation —
> that a gate's entire yield can be absorbed one and two gates downstream, and that a `FN_total` difference
> cannot see it — is confirmed on a second knob and a second gate.** Its methodological half, *"wave 30's rule
> over sequential gates must read the per-gate flow, not the net,"* is **vindicated and superseded**: the flow
> is now a conservation identity that balances exactly and can fail ([F105](#f105)).
>
> **The substantive hypothesis is REFUTED at the pre-registered bar.** This entry claimed *"`D01`'s binding
> constraint is `TooDistorted`, **jointly with** `LowSensitivity`, and no single-knob change recovers it."*
> Measured on the 2×2: `ΔTP(joint) = +1 572` against `Σ ΔTP(singles) = +1 480` — an interaction of **+92**, a
> multiple of **1.06×** against the pre-registered bar of **2×**. **The pair does not bind jointly; the joint
> is essentially additive.** It *is* material (1 572 ≥ the computed bar of 502.5) — so the second half of the
> claim, *"no single-knob change recovers it,"* is also wrong in the direction that matters: `M1` alone
> delivers 1 096, itself above the material bar. **[F22](#f22) fence: none of these counts may be quoted as a
> recall or focus benefit; the frame is goal 3 only.**
>
> **Status → Closed.** The hypothesis was stated so it could be wrong, it was pre-registered as `PREDICTION A`
> against a rival `PREDICTION B` in wave 30's design §4.6, and the data chose. That is the entry working as
> intended.

**[F98](#f98) — APPEND.** Wave 30 is the **first measurement of the low end** of the `MaxDistortion` axis.
Measured at 0.3 and 0.2 on `D01`: no collapse, `R_g` = 12 090 and 14 425, pass-through 9.1 % and 8.3 %, marginal
pass-through on the second dose **4.1 %**. **Part 1 (the instrument finding) is untouched.** **Part 2 (bound the
axis at ~π/4 and rename) gains support at the top and gains a NEW precondition at the bottom**: 23–25 % of the
distortion gate's acceptances at the opened settings were `ACCEPTED-elsewhere` (accepted, not matched to the
star they were released from) against 5–10 % for the sensitivity gate. **A ship that bounds the axis at the top
is supported; a ship that lowers the default at the bottom is not yet supported and owes a ~5 m precision
measurement** (§10 row 7). The axis has 10 steps and wave 30 measured 3 of them.

**[F103](#f103) — APPEND.** **The forward prediction hit 4 of 4, second consecutive wave.** Predicted at wave
29 and measured at wave 30 (`vdrift_selftest_w30.txt` `[5b]`, 19:26:49Z): `/mnt/d/hf_w28` expired from **100
`-B` findings to 0**; `/mnt/d/hf_w29` became the live `-B` fixture at **311**; `/mnt/d/hf_w24` held as the
durable `-C` fixture at **2**, fourth wave running; `/mnt/d/hf_w26` and `/mnt/d/hf_w27` stayed silent at
**0**. **A pinned per-clause fixture goes silent exactly one wave later, on schedule.** The expired roots are
kept and asserted silent, so the expiry is demonstrated rather than described. **Wave 31's prediction, if there
is one:** `/mnt/d/hf_w29` expires to 0 and `/mnt/d/hf_w30` becomes the live `-B` fixture.

**[F94](#f94) — APPEND.** The `None` enumeration plus the **realized-tuple membership assertion** ran in **four**
instruments this wave (`W30-G` 36 regions, `W30-D` 6, `V30` 12, `W30-FP` 18), every one printing `uncovered: 0`
**and** its own realized tuple **and** `member of the enumerated set: True`. **It caught nothing this wave** —
every tuple was inside its domain — and that is the honest report. What it *did* demonstrate is that the
assertion can discriminate: `score_w30g.py`'s self-test `[13]` shows a tuple carrying `None` **is** a member and
a tuple outside the declared domains **is not**, so the assertion can fail. **A check that cannot fire in a
given wave is still worth its cost only if its firing end is exercised, and it was.**

**[F102](#f102) — APPEND.** Every clause in every wave-30 instrument printed `candidates: N   findings: M`.
**No clause printed an unexpected `N = 0` and no instrument refused with `POPULATION-EMPTY`** — the populations
were 5/7/8/3/4/4/2 (`G-0`'s seven), 5 (`G-1`), 36 660 (`G-2`), 2 (`G-3`), 14 and 10 (`W30-D`), 12/5 026/667
(`V30`), 1 230 (`W30-FP`). **But F102's class recurred anyway, in a place populations cannot reach** — see
[F108](#f108). **F102's remedy covers populations the instrument computes; it does not cover a literal used as
a key, which nothing counts.** The remedy is not weakened, it is bounded, and the bound should be written into
it.

**[F95](#f95) — APPEND.** Wave 30 ran the pre-flight **over a populated root** (7 files, 12 `-A` candidates,
5 026 `-B`, 667 `-C`) **and at the close**, both from the pre-registration rather than from a debt, and the two
runs are **byte-identical**. **Both halves of the remedy, from the start, for the first time.** One deviation
against F95's neighbour requirement is recorded in §7.4: the `W30-FP` BEFORE sweep ran **26 minutes after**
`V30`'s five fixture probes read `/mnt/d/hf_w29`, against design §6's bolded ordering. Consequence measured:
none (`PRESERVED`, 0 moved). **The ordering must be enforced by the driver, not by intent.**

**[F21](#f21) — APPEND.** Estimate vs actual, wave 30, sixth consecutive wave under on the analysis halves:

| step | est | actual |
|---|---|---|
| 1 — write all 7 instruments | 60 m | **~39 m** (18:54:08 → 19:33:44Z) |
| 2 — `W30-FP` BEFORE | 5 m | **≤ 60 s** |
| 3 — `V30` pre-flight + 5 fixture probes | 15 m | **~7 m** |
| 4 — the 5-cell arm | 25 m wall / ~15 m compute | **17 m 37 s wall / 1 057 s compute** |
| 5 — `W30-G` scoring | 12 m | **~15 s** to emit |
| 6 — `W30-D` | 15 m | **~2 m** |
| 7 — the `K` column | 12 m | **NOT RUN** (§10 withdraws it) |
| 8 — AFTER + closing `V30` | 8 m | **~2 m** |
| **Steps 1–8 excluding 7** | **~140 m** | **~62 m — 56 % under** |

**Two figures were priced from a measured rate and one of them was mildly optimistic.** The arm was priced at
~950 s from wave 29's 145–198 s per cell and came in at **1 057 s, 11 % over** — because wave 29's two fastest
cells were its two *dead* ones, so the reference rate was biased low. **A rate measured over an arm containing
dead cells under-prices an arm with none.** The sweep price (3 m/side, from 61 MB/s over a trimmed 2.4 GB root
set) was **generous by ~3×**: both sides completed in ≲ 2 minutes. **New constant handed forward:** 1 230 files
across 6 declared roots sweep in **under a minute** on DrvFs. **And it could only be bounded, not measured** —
see §8.3: the fingerprint records no start time.

**`S27-1` and wave 27's three mutants — WITHDRAWN**, formally, after four waves on the cut list, per design §16
and wave 29 §13. **And note that [F107](#f107) shows `RULE W30-D` falsely reported this row already paid** — it
matched wave 27's `S27-4` arm. The withdrawal is on the merits, not on that match.

**`docs/synthetic-af-bank-results-table.md`'s `K` column — WITHDRAWN**, per design §13's own rule for this item.
Owed by waves 28, 29 and 30; unpaid by all three. `kcol_w30.py` remains on disk with a `21 of 21` self-test and
can be run in ~2 m by any later ship.

---

## §12 — WAVE 31, PRICED AND GOAL-SCORED — AND THE RECOMMENDATION IS TO STOP

**Run hard stop `2026-08-14T00:45:17Z`. It is `2026-08-13T19:56:41Z`. Remaining: 4 h 48 m.**

Wave 29 §13 wrote: *"items 2 and 4 above are the last two product questions this bank can answer without new
data, and once they are closed, the next wave that cannot name a product question should stop rather than audit
its own instruments a fourth time."* **Item 2 is now closed** (§4, F104). Design §17 committed in advance to
the consequence: *"after wave 30, this bank has no unanswered product question that another synthetic arm can
address."* **The measurement bears that out.**

### 12.1 What is actually left

| # | candidate | price | compute | goals | state after wave 30 |
|---|---|---|---|---|---|
| **3** | choose among F82's three fixes, pre-registered before reading | ~40 m to choose | — | **2** ✓✓ | **Ready. Needs no new measurement.** `W29-S`/`W29-R` supplied everything a choice needs. Ship price is separate: ~45 m + a 3-cell re-run + **42 m gate** for candidate (3) |
| **4** | bound the `MaxDistortion` axis at ~π/4 and rename it | ~20 m code **+ 42 m gate** | — | **3** ✓ | **The top bound is supported and needs no new arm.** The bottom is **not** — F98's amendment adds a ~5 m precision precondition (§10 row 7) |
| **7** | precision at the opened `MaxDistortion` settings | ~5 m | ~0 | **3** ✓ | **New, created by this wave.** Cheap. Blocks any bottom-end default change |
| — | new datasets 1.4–19.4 ″/px | ≥ 1 h render | ≥ 1 h | 1, 3 | **The only route to a new blind population.** Twelve waves open |

**Every remaining item is a SHIP or a 5-minute rescore. Not one of them is a wave.**

### 12.2 The recommendation: **STOP the wave series.**

**Four reasons, in order of weight.**

1. **The series has run out of measurements, by its own pre-registered accounting.** Items 3 and 4 are C#
   changes with a 42 m gate and a 3997-baseline unit run. They belong in ordinary feature branches with ordinary
   pre-registration, not in a numbered wave carrying rules, verdict trees, fingerprints and a pre-flight. **A
   wave is an apparatus for taking a verdict from a measurement. Neither remaining item has a verdict to take.**

2. **This wave's two NEW rules both shipped with a defect their own self-tests passed over.** `score_w30g.py`
   carried a stale `split()` needle that no checker in the apparatus can see ([F108](#f108)), and `RULE W30-D`
   produced **3 false "already paid" out of 6** on its first outing ([F107](#f107)) — in the direction that
   retires obligations nobody met. That is not a reason to distrust `G-NOT-RECOVERABLE`, which rests on a
   control that reproduced 8 of 8 and an identity that balanced 5 of 5. **It is a reason to stop building new
   rules.** The apparatus's defect rate is not falling: F95 (wave 28), F101/F102/F103 (wave 29), F107/F108
   (wave 30), each one subtler than the last and each found only because the *next* wave went looking.

3. **The findings have shifted decisively toward the apparatus, which is the pattern wave 29 named as the stop
   trigger.** Of this wave's six new entries, **two are about HocusFocus** (F104's negative answer, F105's
   licence for waves 28–29's arithmetic) and **four are about the instruments** (F106, F107, F108, F109). Wave
   29 called this out at a milder ratio and said the wave after should stop rather than *"audit its own
   instruments a fourth time."* Wave 30 was the fourth. A wave 31 would be the fifth.

4. **The clock does not fit a ship.** 4 h 48 m holds **one** C# change with its 42 m gate and its unit run, at
   best, with no margin for the gate failing or the build differing. This series has never run a ship wave; the
   last four have been measurement waves with no build, so the failure modes of a ship wave are untested by this
   apparatus. **A ship that overruns the hard stop is worse than a ship deferred.**

**The charter's formal stop condition — two consecutive waves producing no register-worthy finding — is NOT
met**, and that should be said plainly rather than glossed. Waves 29 and 30 both produced product findings.
**The recommendation to stop is therefore a judgement, not a trigger**, and it rests on reason 1: the bank has
no unanswered product question that another synthetic arm can address, which is what design §17 predicted in
advance and what §4 confirms.

### 12.3 If the remaining time is to be spent, spend it here — and not as a wave

In priority order, each independently valuable and none requiring the wave apparatus:

1. **Item 3 — choose among F82's three fixes (~40 m, goal 2).** The highest value per minute on the list. **The
   choice can be written and committed without shipping**, so it owes no gate and cannot overrun. `RULE D20`'s
   fence forbade choosing in the wave that proposed candidate (3); it does not forbid this.
2. **§10 row 7 — the ~5 m precision rescore** at `M1`/`M3`/`M4`'s opened settings. It closes the one gap wave
   30 knowingly left and it unblocks the bottom half of item 4.
3. **Item 4's top bound only (~20 m + 42 m gate, goal 3).** Supported by F98 part 1 analytically and by wave
   29's measured collapse at 0.9. **Do not touch the bottom of the axis until row 7 is paid.**

**And if none of that is done, the honest position is that wave 30 closed the bank's last arm-answerable
product question with a negative, and the right next move is either new data (the 1.4–19.4 ″/px render, ≥ 1 h)
or nothing.**

---

## §13 — HANDOFF

**Branch** `ghilios/synthetic-af-bank-followups-wave27`, **PR #196** (`OPEN`, base `develop`), carrying **waves
27, 28, 29 and 30**, rebased onto `develop @ ec06bec`. #197 `MERGED`, #199 `CLOSED`. Commits: `1e7de32`
(wave 30 pre-registration), plus this results commit.

**Read first, in this order:** §1 (why two predictions is the strongest form) · §4 (the capture matrix and the
additive joint) · §5 (`W30-D`'s six rows, verified) · F105, F107, F108 · §12's stop recommendation.

**Do not repeat:**

- **(a)** slicing an artifact with a literal you do not assert is present — `str.split()` on an absent needle
  returns the whole document and your check silently stops checking ([F108](#f108));
- **(b)** resolving a ledger row to a **filename** glob — three of six matched a file whose name carried the
  row's word and whose content was a different thing ([F107](#f107));
- **(c)** scoring a debt "already paid" without comparing the artifact's mtime to the **ledger's own**
  timestamp — otherwise the wave that pays a debt reports itself as evidence the ledger rotted;
- **(d)** typing a gate ordering into a design table — the design's 7-gate chain was wrong and the arm's
  run-time extraction of 11 was right (§3.1);
- **(e)** taking the fingerprint after any read of a declared root — §7.4, one step milder than wave 29's
  failure and the same shape;
- **(f)** pricing an arm from a reference rate measured over an arm containing **dead** cells — it under-prices
  by ~11 % (F21 append);
- **(g)** inheriting a fixture pin ([F103](#f103)) — `/mnt/d/hf_w29` is next to expire.

**Standing:** never push `develop`; commit with `322725+ghilios@users.noreply.github.com` as author **and**
committer; pass scorers WSL paths; `--out` mandatory and refusing; `--rule` asserted against the file's own
declared name; populations printed and refusing on an unexpected zero; trees enumerated over the **actual**
domain with `None` included and the realized tuple asserted a **member**; one `TestApp.exe` at a time; no
directory rebuilt mid-wave; **name the wave's own base explicitly in every `git diff` gate check**, because the
PR's base now spans four waves (§9).

**The instruments are on disk under `/mnt/d/hf_w30/`. All six with their own self-test are clean** —
`score_w30g.py` (59), `w30g_arm_w30.sh` (58), `score_w30d.py` (37), `verify_derivation_w30.py` (37),
`fp_w30.sh` (27), `kcol_w30.py` (21); the seventh, `layout_w30.sh`, has no self-test of its own because it
**is** the shared population expression and counter the other six assert through. `kcol_w30.py` in particular is a paid-for, unused instrument: it emits the
results-table `K` column from `truthModel.max(hfrMin, hfrMinEffective)` in ~2 minutes, and §10 withdrew the
column rather than pretending it will be run.
