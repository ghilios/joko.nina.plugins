# Wave 27 — results

## THE TIMESTAMPS, AND THE SEQUENCE — what was open, and what closed after it

Wave 19's write-up was falsified by an artifact written 33 seconds before its own commit. So the roll call is
taken **at an instant**, a row open at that instant is marked open **in place**, and when a row closes later the
record shows the sequence rather than being rewritten.

### Timestamp 1 — the first analysis pass

```
$ ls -la --time-style=full-iso /mnt/d/hf_w27/
drwxrwxrwx  ...  2026-08-13 10:08:24.877146600 -0400 .
   returned at 2026-08-13T10:09:14-04:00  (= 2026-08-13T14:09:14Z)
```

| row | state at **14:09:14Z** | later |
|---|---|---|
| `RULE T27` | **CLOSED** — `T27_PASSED` written 10:07:55 by the scorer itself | — |
| `RULE D27` | **CLOSED** 09:31:37 | — |
| `RULE R27` | **CLOSED** 09:14:37 | — |
| `RULE S27-4` | **CLOSED** 10:02:40 | — |
| `Q27-V1` | **CLOSED** 09:55:16 | — |
| `RULE V27` | **CLOSED** 09:25:17 | — |
| **full suite, AFTER** | **OPEN** — `suite_AFTER.marker` held `SUITE_AFTER_START 14:08:24Z` and **no `SUITE_EXIT=` line**; `suite_AFTER.log` was still growing (3 302 B at 10:08:34 → 3 342 B at 10:11:09) | **closed 10:12:15** |
| **`T27-V4` fingerprint (class 4)** | **OPEN** — no `fp_table18_AFTER.txt`, no `fp_sidecars_AFTER.txt` existed | **closed 10:27:33 — and its first attempt was WRONG**, §7.1 |
| **`score_t27_w27.py` self-test** | **NO ARTIFACT** — `t27_selftest.txt` did not exist; no file in the root contained `SELFTEST T27` | **closed 10:26:22**, §7.2 |
| **`S27-2` mutants** | **NO ARTIFACT** — no run record of any mutant turning its tests red | **partly closed 10:26:22 — `M-S3` only**, §7.4 |
| **`S27-1` route agreement** | **NOT ADJUDICATED**, by the scorer's own printed statement | **still not adjudicated**, §7.3 |
| **half (A) itself** | **UNCOMMITTED** — 4 modified + 2 untracked files at `HEAD e95b7b0` | **still uncommitted**, §5.4 |

**The AFTER suite, recorded as post-timestamp and not folded into the table above:** it closed at
`2026-08-13 10:12:15 -0400` with `SUITE_EXIT=0`, `Failed: 0, Passed: 3992, Total: 3992`. Baseline was **3 973**
(`SUITE_BEFORE_3973.txt`), so the delta is **+19**, and `TruthDisclosureTests.cs` carries exactly **19**
`[Test]` attributes. The delta is named, as the design required, and it accounts for itself exactly. That is a
statement about 10:12:15, not about 10:09:14.

### Timestamp 2 — after the controller closed four of the open rows

```
$ ls -la --time-style=full-iso /mnt/d/hf_w27/
drwxrwxrwx  ...  2026-08-13 10:26:41.832595800 -0400 .
   returned at 2026-08-13T10:28:48-04:00  (= 2026-08-13T14:28:48Z)
```

Nothing was mid-write at that instant: the two most recent artifacts are `s27_score.txt` (10:27:52, the
controller's annotation) and `fp_table18_AFTER.txt` (10:27:33), both complete, and no process was running.

| closed between the two timestamps | new state | artifact |
|---|---|---|
| **`T27-V4`** | **`PRESERVED` — 800 of 800 byte-identical, 0 moved, 0 could-not-look.** **`RULE T27` now rests on 4 of its 4 gates.** Its *first* sweep returned a **false `VIOLATED`** — §7.1 | `fp_table18_AFTER.txt` |
| **scorer self-test** | **`SELFTEST T27 72 of 72`**, plus an evidenced `--out` refusal | `t27_selftest.txt`, `t27_out_refusal.txt` |
| **`S27-2`, `M-S3` only** | **RED, as designed**: `Failed: 3, Passed: 16, Total: 19`, the three named | `s27_2_mutant_MS3.log` |
| **the contradictory `R27` banner** | annotated at the top of the file, with the artifact left otherwise intact | `s27_score.txt` |
| **`TIMING.tsv`** | four missing rows filled in | `TIMING.tsv`, §10 |

**Still open at timestamp 2, and marked open here:** `S27-1`'s route agreement (§7.3); `M-S1` / `M-S2a` /
`M-S2b` mutant records (§7.4); half (A)'s commit (§5.4); and — the one that matters most — **the `S3`
denominator contradiction, which is verdict-determining and cannot be closed by running anything** (below).

---

## THE HEADLINE METHODOLOGICAL FINDING: the design contradicts itself, and the contradiction changes the verdict

**Applying the pre-registered design as written returns two different verdicts about the owner's precision
column, depending on which of its two definitions of `S3`'s denominator a reader uses.**

| where in the design | denominator | `SAT` | `CHANCE` | **verdict** |
|---|---|---|---|---|
| **§4.1**, statistics table | `Σ_f \|P_null(f)\| ÷ Σ_f \|R_null(f)\|` — the **shifted** at-risk pool | 1 (`D18`) | **0** | **`T-SOUND-WITH-NAMED-EXCEPTIONS`** |
| **§4.4**, population table | `Σ\|P_null\| ÷ Σ\|R\|` — the **unshifted** at-risk pool | 1 (`D18`) | **6** | **`T-CHANCE-DOMINATED`** (clause 5) |

The scorer computed §4.1's and **printed both**, naming the discrepancy rather than choosing silently. **This
document does not choose either**, and the reason is not fastidiousness: choosing now, with both columns in
hand, is repair-with-data-in-hand and the [F14](followups.md) fence forbids it.

**The one piece of independent evidence that bears on it, recorded as diagnostic and NOT as authority:** under
§4.4 three datasets return values **above 1.0** (`D17` = 10.0000, `D07` = 2.0000, `D12` = 1.0556), which no
quantity the design describes as *"the fraction of the shifted at-risk pool the unshifted catalogue still
protects"* can take — its numerator is drawn from the shifted pool and its denominator from the unshifted one, so
it is not a fraction of any pool. That is diagnostic that §4.1 is the intended reading. **Diagnostic is not
authority.** The next wave must **fix the rule first, in a pre-registration, and re-score afterwards** — on a
population measured after the fix, not on this one. Full working in §8.2.

---

## §0 — What this document is allowed to say

Every number below is quoted from an artifact in `/mnt/d/hf_w27/` or from a file in the repository, and each is
cited. The pre-registered rules in `docs/synthetic-af-bank-followups-wave27-design.md` are **applied as
written**. Where a rule turned out to be unsatisfiable, internally inconsistent, or gap-ridden, that is reported
as a **finding** (§8) — it is not repaired, and no repaired version is scored.

**Verdicts, in one table.**

| rule | verdict | population | artifact |
|---|---|---|---|
| **`RULE T27`** | **`T-SOUND-WITH-NAMED-EXCEPTIONS`** | 19 datasets (D08 not blind) | `t27_score.txt` |
| **`RULE D27`** | **`D-MOVED`, 6 of 6** | 6 datasets (D08 not blind) | `d27_score_controller.txt` |
| **`RULE R27`** | **`R-IDENTICAL`** — 20 cells, 420 files, 0 diffs | 20 datasets | `repro_all_score.txt` |
| **`RULE S27-4`** | **PASS** — 360/360 bytes, 20/20 counts, 20/20 sinks changed | 20 datasets | `s27_4_score.txt` |
| **`Q27-V1`** | **GATE DISCHARGED**, FAIL end run | B15→B16 diff | `q27_v1.txt` |
| **`RULE V27`** | **`V-CLEAN`** | 6 instruments | `vdrift_w27_FINAL.txt` |
| `T27-V0` / `V1` / `V3` | pass / pass / pass | 20 / 20 / 180 rows | `t27_score.txt` |
| **`T27-V4`** | **`PRESERVED`** — 800 of 800 byte-identical *(taken after timestamp 1; first sweep was a false `VIOLATED`, §7.1)* | 800 files | `fp_table18_AFTER.txt` |

---

## §1 — The headline is a correction to the backlog: item 1 was already done

**The assigned top backlog item — *"re-measure precision against `*.truth.json`, not `*.golden.json`"* — shipped
on 2026-08-03, ten days before it was written into the backlog.** Verified independently of the design:

```
$ git log --diff-filter=A -- Joko.NINA.Plugins/TestApp/SynthBank/TruthProtection.cs
aaf26e8 2026-08-03 14:57:13 -0400 fix(bank-verify): stop charging false positives for real stars the golden dropped (F31)
```

Wave 26's `91.3 %` counted a **pre-repair** quantity. Its 150 decomposes as **50 `unresolved` + 89
truth-protected + 11 surviving**, and the decomposition is now confirmed by a route the design did not use to
derive it. The scorer's `--protection off` arm — which is simultaneously `T27-V0`'s demonstrated FAIL end and the
measurement of the correction's size, one computation with two duties — reports:

```
D08_c11_2800mm  published TP=308 FP=11 FN=7   scorer TP=308 FP=100 FN=7   dFP=+89
```
— `t27_v0_failend.txt`

**`+89` on the nose**, against the design's separately-derived 89. Two routes, one number. Across the whole
population the same arm reports that **protection removed 350 detections from the false-positive pool**, and it
names all ten datasets that carry any of it:

| dataset | dFP with protection off |
|---|---|
| `D16_esprit550_ha3` | **+103** |
| `D08_c11_2800mm` | **+89** *(not blind)* |
| `D11_rc10_585_afbin2` | +44 |
| `D05_tec140_1000mm` | +42 |
| `D12_c14_585_afbin2` | +36 |
| `D15_cdk20_3454mm_e47` | +18 |
| `D09_c14_3800mm` | +11 |
| `D07_rc10_2000mm` | +5 |
| `D10_rc16_3250mm_sparse`, `D17_cdk14_oiii5` | +1 each |
| **total** | **350** |

The other **ten** datasets reproduce exactly even with protection off — they had no at-risk detection at all.
That fact is load-bearing again in §8.2.

**The banner on the owner's deliverable was false in the opposite direction from the truth.** It claimed the
precision column was a **lower** bound; the column is already truth-corrected, so `1.000` is a **ceiling**. It
was withdrawn in place in commit `e95b7b0`, together with the junk-count correction (**11, not 13**).

---

## §2 — `RULE T27` = **`T-SOUND-WITH-NAMED-EXCEPTIONS`**

> `>>> RULE T27 = T-SOUND-WITH-NAMED-EXCEPTIONS -- honest except on ('D18_m24_deep_shed',), which are annotated in place as uninformative`
> — `t27_score.txt`

**Verdict population 19**, computed as `len(manifest_datasets) − len(NOT_BLIND)` and asserted, with
`D08_c11_2800mm` measured and excluded. **`SAT` = 1, `CHANCE` = 0, union = 1, could-not-look = 0.** Clause 3 of
the pre-registered tree (`1 ≤ SAT + CHANCE ≤ 4`).

### 2.1 The gates, all three passed, both FAIL ends run

| gate | result | FAIL end |
|---|---|---|
| **`T27-V0`** reproduction | **exact on 20 of 20**, 0 could-not-look | **RUN**: `--protection off` differs on 10 of 20 (`t27_v0_failend.txt`, rc non-zero by design) |
| **`T27-V1`** coordinates | **V1-a bit-identical on 18 of 18**; **V1-b separated on 2 of 2** | **RUN**: the native arm on `D11` (1.000000 vs **0.002463**) and `D12` (1.000000 vs **0.025070**), both far past the 0.30 bar (`t27_v1_failend.txt`) |
| **`T27-V3`** pairing | **180 of 180** | see §8.4 — the clause as written names a field that does not exist |
| **`T27-V4`** fingerprint | **`PRESERVED` — 800 of 800 byte-identical, 0 moved** *(taken after timestamp 1)* | its own first sweep returned a **false `VIOLATED`**, caught by the population assertion — §7.1 |

**All four gates now pass, so the verdict below stands on the full pre-registered set.** At timestamp 1 it stood
on three of four, and that is recorded rather than overwritten.

`T27-V1`'s `D08` row is worth naming: its binned and native rates are both `0.971616` — **identical, therefore
passing V1-a**, but not 1.0. At `captureBinning = 1` the transform is the identity, so a rate below 1 is a
property of the truth catalogue and the 12 px radius, not of the reader. The gate is doing exactly what it was
built to do: it tests *agreement between the two copies*, not the absolute rate.

### 2.2 `D18_m24_deep_shed` is `SATURATED-BY-NULL`, exactly where the design predicted the FAIL end was reachable

| statistic | `D18` | bar |
|---|---|---|
| `precisionNull` (at the product's own `Δ1`) | **0.2820** | ≥ 0.25 → **fires** |
| `A_all` | 0.2372 | ≥ 0.25 → does not fire |
| `A_golden` / `A_protOnly` | 0.0435 / **0.1938** | — |
| `chanceProtected` (§4.1 form) | 0.1312 | ≥ 0.25 → does not fire |
| `Δ1` spread over the four offsets | 0.0158 | — |

**The two statistics agree in direction, which the design required.** `A_all` at 0.2372 sits just under the same
0.25 bar the null crosses — coverage is the mechanism, and **`A_protOnly` (0.1938) is four and a half times
`A_golden` (0.0435)**, so on the densest field in the bank the correction's own discs, not the reference's boxes,
are what fills the frame. `D18` carries 27 219 truth stars on a 6 248×4 176 frame — 1 043 stars/Mpx, the density
the design named in advance as the one place a 0.25 bar was reachable.

### 2.3 `D01_ultrawide_40mm` is the near-miss and must be named as such

| statistic | `D01` |
|---|---|
| `precisionNull` | **0.2182** — **0.0068 below the bar** |
| `A_all` / `A_golden` / `A_protOnly` | 0.1105 / 0.0360 / 0.0745 |
| `Δ1` spread | 0.0115 |

**The spread over the four offsets is 0.0115, and the distance to the bar is 0.0068.** A different single draw
from the same family of offsets could have put `D01` over the bar. The rule reads `Δ1` because `Δ1` is what
ships, and the rule is applied as written — but a reader who treats `D01` as "clean" is reading a margin
narrower than the instrument's own single-draw variance. **`D01` is named as an exception-adjacent dataset, not
a clean one.** The next densest, `D12` (`A_all` 0.1239), does not come close on the null (0.0199).

### 2.4 The prediction was recorded in advance, and it was right

> **Prediction P-W27.** `T-SOUND-WITH-NAMED-EXCEPTIONS`, with the exceptions drawn from
> `{D18_m24_deep_shed, D01_ultrawide_40mm}` and at most one other. — design §4.6

**Confirmed.** `D18` fired, `D01` is the near-miss, no third dataset came near. The design's two falsifiers both
held: `D04`/`D02` did **not** fire (`A_all` 0.0522 / 0.0105; `precisionNull` 0.1175 / 0.0712), so the mechanism
is density alone and no second explanation is owed; and `D10`/`D06` did **not** fire (`A_all` 0.0050 / 0.0026),
so `T27-V0` is not covering for a broken scorer.

### 2.5 What the owner gets

**The precision column is honest as published, on 18 of the 19 blind datasets.** `D18`'s `1.000` is
uninformative and must be annotated in place with its `A_all` 0.2372 / `precisionNull` 0.2820 / `chanceProtected`
0.1312. `D01`'s `1.000` carries the near-miss caveat of §2.3. **`D08`'s `0.966` stands as honest at factor 1** —
and see §4.3 for what happens to it at the optimizer's own factor.

---

## §3 — `scoredFraction` is the quiet finding, and no reader could see it before half (A)

`scoredFraction = Σ(|TP| + |FP|) ÷ Σ|D|` — the fraction of the detections that entered the precision ratio at
all. It was **reported alongside, not thresholded**, so it moves no verdict. It is nonetheless the most
consequential number in `t27_score.txt`:

| dataset | `scoredFraction` | published precision |
|---|---|---|
| **`D12_c14_585_afbin2`** | **0.6462** | 1.000 |
| **`D08_c11_2800mm`** | **0.6965** | 0.966 |
| **`D11_rc10_585_afbin2`** | **0.7315** | 1.000 |
| **`D16_esprit550_ha3`** | **0.7438** | 1.000 |
| **`D15_cdk20_3454mm_e47`** | **0.7531** | 1.000 |
| **`D09_c14_3800mm`** | **0.8132** | 1.000 |
| `D10_rc16_3250mm_sparse` | 0.8605 | 1.000 |
| `D17_cdk14_oiii5` | 0.9100 | 1.000 |
| the other twelve | 0.9413 – 0.9948 | — |

**More than a third of `D12`'s detections never entered its precision ratio, and its published precision is
`1.000`.** The number is a ratio over the two-thirds that were judged; the other third was neither confirmed nor
charged. That is not wrong — it is the documented behaviour of `ExcludeUnresolved` plus `TruthProtection` — but
**until half (A) it was unreportable**: a grep of `/mnt/d/hf_w25/table18/*.log`, `golden_eval.txt` and
`golden_eval_frames.csv` for `scoringMode` / `protected` / `precisionNull` returns zero hits. The instrument that
produced the owner's table could not say what fraction of the detections it had judged.

**This is the strongest single argument for half (A) and it is now measured rather than argued.**

### 3.1 `NULL-UNDER-POWERED` — a state the design did not anticipate, on 7 datasets

The design fixed the rule: *"if the spread on any dataset exceeds that dataset's own `Δ1` value, the shipped null
is recorded as UNDER-POWERED on that dataset and the dataset becomes a named caveat rather than a verdict
input"* (§4.3). It fired on **seven**: `D08` *(not blind)*, `D09`, `D11`, `D12`, `D13`, `D14`, `D16` — six of
them inside the verdict population.

**What it means for those rows.** On each, the four sign-variant offsets disagree with one another by more than
the value the rule reads. `D09` is the clearest: `precisionNull` = 0.0000 at `Δ1`, spread **0.0148**. The
shipped single-offset control cannot distinguish *"this dataset has no chance protection"* from *"this offset
landed in a quiet corner"*. **On those six the `precisionNull` column is a draw, not a measurement**, and the
correct reading of a `SOUND` result there is *"not shown to be saturated"*, never *"shown to be sound"*.

**It does not move the verdict**, and the check that it does not is worth stating: `D18`, the only dataset that
fires a bar, is **not** under-powered (0.2820 against a spread of 0.0158, an 18:1 ratio). Removing all six
under-powered datasets from the verdict population leaves `SAT` = 1, `CHANCE` = 0 and clause 3 still selected.

**But the design never says what the count denominator becomes when a dataset is demoted to "named caveat rather
than verdict input", and that is a gap** (§8.5). It is inert this wave only because the demoted six were all
non-firing.

### 3.2 Two `CNL-NODET` frames, named and not subtracted

```
CNL-NODET : 2
    D12_c14_585_afbin2 f19436  the detection dump has a header and zero data rows
    D12_c14_585_afbin2 f20564  the detection dump has a header and zero data rows
```

Both are `D12`'s extreme wing frames. They contribute 0 to every numerator **and** every denominator and are
counted in `frames_no_detections`, per §4.4 and §7. They are named here rather than quietly folded away —
`D12` is also the dataset with the lowest `scoredFraction` in the bank, and a reader comparing `D12` against
anything else is comparing 7 frames' detections against another dataset's 9.

---

## §4 — `RULE D27` = **`D-MOVED`, 6 of 6**

> `>>> RULE D27 = D-MOVED (6 of 6)` — `d27_score_controller.txt`

Seven published rows were scored at detection binning **1** while the optimizer's own physics-derived factor is
**2**; `golden eval` printed *"Pass `--detection-binning` to score it at its own factor"* on every one of them
and nobody acted. Re-scored on **B15** (`TestApp.dll` sha256 `2fb0c8fd…`, **no build**), 7 cells / 63 frames,
`13:29:15Z → 13:30:04Z` — **49 seconds of compute**.

### 4.1 Both gates passed, and `D27-V1`'s FAIL end was run on a real cell

| gate | result |
|---|---|
| **`D27-V1`** `PixelScale` carries the factor | logs printing `; detectionBinning 2`: **7 of 7**; logs still printing the `scored at 1` NOTE: **0 of 7** |
| **`D27-V1` FAIL END, RUN** | `D09` re-run *without* `--detection-binning` (`pinprobe/D09.log`): **NOTE present = True** |
| **`D27-V2`** coordinate collapse | min `recall@all` at factor 2 = **0.729** (bar 0.25) |

### 4.2 The moves, all six

| dataset | factor 1 (published) | factor 2 | delta |
|---|---|---|---|
| **`D14_cdk14_2563mm_e47`** | `Rall 0.641` `Rhi 0.862` | **`Rall 0.937`** `Rhi 0.995` | **+0.296** |
| **`D17_cdk14_oiii5`** | `Rall 0.875` | `Rall 1.000` | **+0.125** |
| `D09_c14_3800mm` | `Rall 0.949` `Rhi 0.911` | `Rall 0.991` **`Rhi 1.000`** | +0.042 |
| `D10_rc16_3250mm_sparse` | `Rall 0.965` | `Rall 1.000` | +0.035 |
| `D12_c14_585_afbin2` | `Rall 0.705` `Rhi 0.720` | `Rall 0.729` `Rhi 0.776` | +0.024 |
| **`D15_cdk20_3454mm_e47`** | `Rall 0.961` `Rhi 0.920` | `Rall 0.949` **`Rhi 0.885`** | **−0.012** |

Precision moved on none of the six (`dP = +0.000` throughout). **`D15` moved the wrong way** and is the reason
the bar was two-sided: at the optimizer's own factor `D15` finds *fewer* high-tier stars. `D-MOVED` is not
`D-BETTER`.

### 4.3 `D08` is not blind, and its factor-2 number needs a careful sentence

`D08` was measured and excluded from the count, as required. At factor 2 it reads:

```
OVERALL: TP=315 FP=1 FN=0  precision=0.997 recall=1.000   (table18_b2/D08_c11_2800mm.log)
OVERALL: TP=308 FP=11 FN=7 precision=0.966 recall=0.978   (published, factor 1)
```

**Ten of the eleven "genuine wing-frame junk" detections do not survive at binning 2.** The junk is a
factor-1 artifact.

**And the withdrawn banner's number reappears by coincidence, which must be said out loud so nobody mistakes it
for vindication.** The banner claimed *"D08's true precision is nearer 0.997 than the 0.966"* — and factor 2
gives exactly **0.997**. The banner's reasoning was wrong (it counted a pre-repair FP quantity and inferred a
lower bound); the agreement of the digits is an accident of a different, legitimate instrument change. **The
banner stays withdrawn.**

**Consequence for the record, flagged not fixed:** commit `e95b7b0` states that `F83`'s caveat 3 is discharged
in its own favour because *"the +0.034 is real"*. That is true **at factor 1**. At the optimizer's own factor the
same dataset's precision cost is **+0.003**. Any future quotation of `+0.034` must carry its factor label, or it
will be read as factor-independent when `RULE D27` — run in the same commit — shows it is not.

---

## §5 — Half (A): the port ships, proved at byte level, with the gate discharged rather than skipped

### 5.1 `RULE S27-4` = **PASS**, and the port is **not inert**

```
detection artifacts byte-identical : 360 of 360
TP/FP/FN unchanged                 : 20 of 20 datasets
report sinks CHANGED (expected)    : 20 of 20 datasets
>>> S27-4 = PASS -- the change is report-only, proved at byte level
>>> and the port is NOT INERT: 20 of 20 report sinks changed
```
— `s27_4_score.txt`

The raw byte comparison behind it (`s27_score.txt`) reports **420 files compared, 380 identical, 40 differ** —
and the 40 are **exactly the two report sinks × 20 datasets**, every one named in the artifact. *That is the
port working.* A reporting change that moved a score would have been a bug, and `S27-4` makes that statement
falsifiable rather than argued: 360 detection artifacts and 60 counts, unmoved.

### 5.2 `Q27-V1` = **GATE DISCHARGED**, with its FAIL end run

The 42-minute `optimize` gate is **owed** whenever the wave builds. It was discharged on the pre-registered
reach-scoped basis, not skipped:

```
--- INTERSECTION (B16) --- (empty)
>>> Q27-V1 = GATE DISCHARGED: no changed file is reachable by the gate's path.

=== Q27-V1 FAIL END, RUN: the same check against the B14 tree ===
intersection is NON-EMPTY (2 files) ... StarDetectionOptimizerWizardVM.cs, StepSizeRecommender.cs
>>> FAIL END DEMONSTRATED: the same predicate REFUSES on a diff that reaches the gate.
```
— `q27_v1.txt`

**The discharge is not a pass and is not reported as one.** What replaces it is `Q27-V0`-shape hash identity plus
`S27-4`'s byte-identical detection artifacts — a strictly sharper control *for this change* than an `optimize`
gate would have been, because it measures the actual instrument on the actual population.

### 5.3 Binary discipline

`binary_provenance_w27.txt`, `13:54:46Z`: B16 `TestApp.dll` **`2d8e11f4…`**, plugin dll **`01ba4b36…`**, both
distinct from B15's `2fb0c8fd…` / `3fd1fc4b…`; the shipped `synthetic-bank-spec.json` unchanged; **`find … -name
'*.cs' -newermt <build mtime>` empty**; the apphost recorded for the record only, per [F66](followups.md).

### 5.4 Half (A) is written and tested but **not committed**

At the timestamp — and now — the working tree carries 4 modified files (`BankVerifyRunner.cs`,
`GoldenEvalRunner.cs`, `GoldenFromTruth.cs`, the test `.csproj`) and 2 untracked
(`TestApp/SynthBank/TruthDisclosure.cs`, `Tests/Golden/TruthDisclosureTests.cs`) against `HEAD e95b7b0`, which
carries only halves (C) and (D). **The port is proved and unshipped.** Marked open in place.

### 5.5 `M-S2` reaches two sites, unequally guarded — say so plainly

`M-S2` (hard-code `scoringMode = "golden"`) has two change sites, and only one is guarded behaviourally.

* **The shared helper `TruthDisclosure.ScoringMode`/`ScoringLine`** is killed by real behaviour: three tests
  exercise it in both directions (`ScoringMode_SaysTruthProtected_IffAFrameCarriedATruthSidecar`,
  `ScoringLine_IsBankVerifysWording_Verbatim_InBothDirections`,
  `ReportLines_SayTheModeIsGoldenAlone_WhenNoSidecarExists`).
* **The `GoldenEvalRunner` call site is guarded only by a SOURCE-TEXT assertion.** `TruthDisclosureWiringTests`
  reads `GoldenEvalRunner.cs` off disk, strips comments, and asserts
  `Does.Not.Contain("scoringMode = \"golden\"")`. Its own class doc states the reason: the runner *"cannot be
  linked into this project (it loads NINA rendered images)"*.

**A source-text assertion catches the literal it was told to look for and nothing else.** A mutant that
hard-coded the label with different spacing, a different quote style, or through a local variable would pass
this test while producing exactly the defect. The guard is honest about being textual — it is the same mechanism
`TestAppOutputAsciiTests` uses — but it is **not** a behavioural mutant kill, and the two must not be counted as
one. The tests do read defensively: `ReadTestAppSource` fails rather than skips when the file is absent or
suspiciously short (*"a test that cannot look must not pass"*).

---

## §6 — The controls

### 6.1 `RULE R27` = **`R-IDENTICAL`**, and it ran **before** pre-registration

**20 cells, 420 files compared, 420 byte-identical, 0 differences, 0 could-not-look** (`repro_all_score.txt`).
`golden_eval.txt` is compared with its `reports ->` line removed — that line embeds the `--out` path and differs
by construction; the ignored substring is declared in source and self-test [3] demonstrates that a change
*outside* it is still caught, so the filter is not a blanket.

**Declared deviation, not laundered.** `R27` ran at `13:14:24Z`, before the pre-registration commit `df68232`.
The reasoning is recorded in `CONTROLLER_NOTE.md` §7 and adopted by design §5.6a: it is a determinism control on
the instrument, reads no statistic in T27's verdict tree, and a byte-comparison of a regenerated population has
exactly one interesting outcome. **It is reported as opportunistic rather than pre-registered, in the same
sentence that quotes `R-IDENTICAL`.**

What it buys is real and it is load-bearing twice: half (B) may treat the 180 `detected_f*.csv` as a stable
population, and **`S27-4` becomes attributable** — with `R-IDENTICAL` in hand, any byte difference between B16
and `table18` is the treatment and cannot be run-to-run noise.

### 6.2 `RULE V27` = **`V-CLEAN`**, after the pre-flight caught a fourth gap in itself (§7.5)

`vdrift_w27_FINAL.txt`: 6 files checked, `V27-A` 3 of 3 siblings resolve, `V27-B` **0** unlicensed
previous-wave tokens, `V27-C` **0** typed ordinals. Re-run after (D) (`v27_d27.txt`, 7 files) and after (A)
(`v27_s27.txt`, 9 files) — `V-CLEAN` on both. Self-test PASS with all six clause groups including the new
`[6]`, which demonstrates both ends: *"the block is exempt (line 3) and the file after it is NOT (line 7)"* and
*"the DEFINITION of `HISTORICAL_BLOCKS` opens nothing — it is not an instance of itself"*.

### 6.3 Layout and manifest

`layout_selftest.txt`: **`SELFTEST LAYOUT27 16 of 16`**, including four explicit FAIL ends (`assert_count`
rejecting a mismatch; the population assertion rejecting 19 datasets; the near-miss root named and refused).
`T27_MANIFEST_READY`: `datasets=20 rows=180 b2=7 root=/mnt/d/hf_w25/table18`. The `T27-V5` path trap held —
every artifact header names `/mnt/d/hf_w25/table18` and declares `/mnt/d/hf_w25/table` as the refused near miss.

---

## §7 — FINDINGS: each is a defect in an instrument or a rule. Reported, not repaired

### SEVERITY 1 — the control that was missing, then wrong, then right

#### 7.1 `T27-V4` (fingerprint class 4): **absent at timestamp 1, falsely `VIOLATED` on its first sweep, `PRESERVED` on its second**

**At timestamp 1 the gate had not been taken.** Design §5.5 requires AFTER fingerprints *"written at the wave's
close and kept"*, with any difference forcing `T-UNEVALUATED`; neither file existed. Verdict-tree clause 1 names
`T27-V4` alongside `V0`/`V1`/`V3`, so `RULE T27`'s verdict then rested on **three of its four** gates. The
scorer was scrupulous about not claiming what it had not done:

> `--- T27-V4 : fingerprint (class 4) is taken OUTSIDE this scorer, by the wave's fp driver.`
> `  This scorer does not evaluate it and does not claim it.`

**The first AFTER sweep then returned `T27-V4 = VIOLATED`, and it was WRONG — an addressability error in the
instrument, not a deletion in the population.** The AFTER sweep omitted the **20 `.log` files** that the BEFORE
sweep had captured, so it compared 780 paths against a BEFORE list of 800 and reported the 20 absentees as moved.
**Nothing on disk had changed.** The only reason it surfaced rather than shipping as a fake violation is the
design's standing rule that the **population size is asserted inside the file** — the mismatch was arithmetic,
not a judgement call.

**This is [F74](followups.md)'s shape in the BEFORE/AFTER direction**, and it generalises past this wave: *a
BEFORE/AFTER control whose two sweeps are built by different expressions is not comparing a population against
itself; it is comparing two populations, and every difference it reports is unattributable.* Registered as
[F91](#f91).

**The corrected sweep passes outright** (`fp_table18_AFTER.txt`, taken `14:27:17Z`):

```
--- class 4a: table18 artifacts, population 400 (expect 400) ---
BEFORE 800   AFTER 800   compared 800
missing from AFTER: 0   extra in AFTER: 0   DIFFERING: 0
>>> T27-V4 = PRESERVED -- 800 of 800 byte-identical, 0 moved, 0 could-not-look
```

800 = 400 `table18` artifacts + 20 logs + 180 `*.truth.json` + 180 `*.golden.json` + 20 `synthetic_meta.json`,
matching `fp_table18_BEFORE.txt` + `fp_sidecars_BEFORE.txt` exactly. **`RULE T27` now rests on 4 of its 4
pre-registered gates.** The wave read `table18` and the bank sidecars and wrote only into `/mnt/d/hf_w27/`, and
that is now proved rather than assumed.

**Both outcomes are recorded because the sequence is the finding.** A control that reported a false violation
and was corrected is a stronger record than one that passed first time, provided the false reading is kept.

**Independently corroborating, and it was already in hand:** `RULE R27` proves B15 *regenerates* all 420
`table18` files byte for byte — a stronger statement than "the bytes have not changed since someone wrote them".

### SEVERITY 2 — instruments whose evidence was not on disk

#### 7.2 The `T27` scorer's self-test had **no artifact at timestamp 1**; it now has one

At timestamp 1, `t27_selftest.txt` did not exist and no file under `/mnt/d/hf_w27/` contained the string
`SELFTEST T27` — it appeared only at `score_t27_w27.py:1920`, in the code that would print it. The design's §11
rule is explicit: *"every self-test in this wave prints an explicit `SELFTEST … <n> of <n>` line and the plan
checks that line"* — **read the OUTPUT, not the exit code**, which is precisely wave 26's lesson about a
self-test that exited its caller while printing PASS. The self-test had been run and read in the terminal; it
had **not been captured**, so no reader after the session could check the line.

**Closed at 10:26:22.** `t27_selftest.txt` ends:

```
SELFTEST T27 72 of 72
```

and the 72 include both ends of every clause on constructed fixtures — among them
`T27-V0/FAIL-end-protection-off-DIFFERS`, `T27-V1-b/FAIL-arm-native-copy-collapses`,
`S27-4/FAIL-end-one-moved-byte-blocks-the-ship`, `S27-1/an-absent-disclosure-is-CNL-FIELD-and-is-NAMED`,
`null/shifts-the-DETECTIONS-not-the-catalogue`, and `lattice/stride-four-offset-two-and-a-full-cover-reads-one`.
The `--out` discipline is separately evidenced in `t27_out_refusal.txt`: invoking the scorer without `--out`
produces `error: the following arguments are required: --out`, **rc 2, nothing written** — wave 25's defect,
closed and now demonstrated rather than asserted.

**The residual lesson stands and is worth carrying:** a self-test whose output is never redirected to a file is
a self-test that exists only for the session that ran it. The scoring path made `--out` mandatory; the
`--self-test` path did not, and that asymmetry is what produced a two-hour gap between the check being run and
the check being checkable. Contrast the sibling instruments, which captured theirs from the start:
`layout_selftest.txt` (`16 of 16`) and `vdrift_selftest_FINAL.txt`.

#### 7.3 `S27-1`'s route agreement was **not adjudicated**, by the scorer's own printed statement

```
S27-1 route agreement is NOT adjudicated here: this scorer does not know the port's chosen
  wording, and comparing against a guess would be a second route to the same wrong answer.
  Run --rule T27 --full and compare its per-dataset scoredFraction / precisionNull directly.
```
— `score_t27_w27.py:1252-1254`

The refusal is well-reasoned — guessing the port's wording would manufacture exactly the false agreement
[F79](followups.md) warns about — and the scorer does emit `S27-1 CNL-FIELD` for any disclosure it cannot find
under a spelling it knows. **But `S27-1` is the clause the design made a ship gate**: *"(A) does not ship until
the wave says which is right."* No artifact adjudicates it. The comparison it points at is straightforward
(`t27_score.txt` already carries per-dataset `scoredFraction` and `precisionNull` at `Δ1`, and B16's
`golden_eval.txt` files exist under `s27_after/`), so this is **~10 minutes of unblocked work**, not a
re-measurement.

#### 7.4 `S27-2`: **`M-S3` now has a run record; `M-S1`, `M-S2a` and `M-S2b` remain testimony**

At timestamp 1 no artifact recorded any mutant turning a test red. The tests existed and were well-aimed (19,
each doc-commented with the mutant it kills) and the suite was green at 3 992 — but *green tests are not a mutant
kill*, and the design's FAIL end for `S27-2` is *"a mutant that does not turn its test red means the test does
not reach the code."*

**`M-S3` is now evidenced** (`s27_2_mutant_MS3.log`, 10:26:22), re-verified by the controller rather than
inherited from the implementing agent. Returning the unshifted detections from the null path turns exactly three
tests red:

```
Failed!  - Failed: 3, Passed: 16, Skipped: 0, Total: 19
  PrecisionNull_IsWhatChanceAloneScores_NotTheLivePrecision
  ReportLines_CarryAllFourDisclosures_PlusTheExercisedCount
  CsvHeaderAndRow_AgreeOnFieldCountAndOrder
```

and the failure text shows the defect directly — the report block prints `precisionNull: 0.500` where the fixture
requires `0.000`, i.e. **the null has become the live precision**, which is the whole point of `M-S3`. The
mutant reaches the code.

**Three qualifications, all of which keep this short of a clean `S27-2` pass:**

1. **`M-S1`, `M-S2a` and `M-S2b` have no artifact.** They remain agent testimony. Given [F89](#f89) — a
   mutation harness that restored with `cp -p` and silently re-measured the mutant — untested testimony from
   that harness is exactly the class of claim that needs an artifact.
2. **The restore verification is also testimony.** The controller reports sha256 `7f3306ad…` before and after
   and a green re-run; **neither is in `s27_2_mutant_MS3.log`**, which contains only the RED run.
3. **The reported green re-run is `21/21`, but the RED run's own artifact says `Total: 19`.** A restore check
   that runs a different-sized population than the mutant run is not a restore check *of that population*. The
   discrepancy is unexplained by any artifact and is flagged, not resolved.

Related and unchanged: **§5.5's finding that `M-S2`'s second site is guarded only by a source-text assertion**.
Price to close the remainder: **~10 minutes** (three mutants, one build each, output redirected).

### SEVERITY 2 — a checker that had disabled itself

#### 7.5 [F80](followups.md)'s checker had a **FOURTH** gap, and this one had switched the check **OFF**

`in_historical_block()` scanned backwards for any line *containing* a `HISTORICAL_BLOCKS` token and treated it
as opening a historical literal — **but the line that DEFINES `HISTORICAL_BLOCKS` contains every one of those
tokens.** From that definition line to EOF, `V27-B` was off.

**The evidence, both ends, on disk:**

| | artifact | reading |
|---|---|---|
| **before** | `vdrift_w27_run1.txt` | header prints **`=== RULE V26 ===`** on a wave-27 root; `V26-B: unlicensed previous-wave tokens: **0**`; verdict **`V-CLEAN`** |
| **after** | `vdrift_w27_run2.txt` | **32** `[V26-B]` findings, every one on `verify_derivation_w27.py` itself; verdict `V-DRIFT: 32 finding(s). BLOCKING` |
| **raw** | `verify_derivation_w27.py.bak` | the pre-repair file contains **69** literal `V26` occurrences; the repaired file contains **2** |
| **fixture** | `vdrift_selftest_FINAL.txt` `[5b]` | the design's pinned `/mnt/d/hf_w26` root refuses with **255** `V27-B` findings |

**The checker declared itself clean while printing the previous wave's rule name in its own banner.** That is
the exact defect F80 exists to catch, catching nothing, on itself.

**The class is new, and it is not the `_`-word-boundary family of the first three gaps:**

> **A rule that describes itself must exclude itself from its own population, or it launders its own subject.**

The first three gaps were about a *pattern* being too narrow (`\b` cannot see `_`). This one is about a
*population* silently containing the instrument, so the instrument's own definition of what to look for became a
licence to stop looking. Closed, with self-test clause `[6]` demonstrating both ends. Two FAIL fixtures are now
pinned — `hf_w24` (kept) and the design's `hf_w26` (**added**, not swapped).

**Two figures in the controller's first account were challenged and are corrected here, with the controller's
agreement:**

* **"53 surviving `V26` tokens" is withdrawn.** It was a `grep -c` of *lines* containing `V26` taken before the
  repair — **a third quantity**, neither the findings count nor the occurrence count, and not evidenced as
  stated. **Quote instead: 32 findings after the repair, over 69 raw `V26` occurrences** in
  `verify_derivation_w27.py.bak`.
* **"204 → 255" becomes "255, plus testimony."** The **255** is on disk (`vdrift_selftest_FINAL.txt` `[5b]`) and
  stands. The **204** was really read, in the pre-repair run's stdout — but **that artifact was not kept**
  (`vdrift_selftest_run2.txt` has no `[5b]` clause; it was added later), so it is testimony, not evidence, and
  is labelled as such.

**Neither figure is load-bearing, and that is the point worth keeping.** The fully-evidenced fact needs no
count at all: **the checker certified its own file `V-CLEAN` while printing the previous wave's rule name in its
own banner, on a wave-27 root.** `vdrift_w27_run1.txt` against `vdrift_w27_run2.txt` proves it outright.

### SEVERITY 3 — traps worth registering

#### 7.6 `git diff --name-only <tree>` does **not** list untracked files

A two-binary provenance diff can therefore silently omit an entire new source file. **It did here**:
`binary_provenance_w27.txt`'s tracked diff lists 3 files, and `TruthDisclosure.cs` — the whole substance of half
(A) — is **not among them**, because it was untracked at build time. `q27_v1.txt` takes the **union** of the
tracked diff and `git ls-files --others`, and reports 5 files. Had `Q27-V1` used the tracked diff alone, it
would have computed its reachability intersection over a change set missing the new file. **This belongs beside
[F66](followups.md)** — same shape, different mechanism: a provenance instrument that reports confidently on an
incomplete population.

#### 7.7 A mutation harness that restores with `cp -p` restores the **pre-mutation MTIME**

MSBuild then skips the recompile and the next run silently re-measures the mutant. It made a correctly-restored
tree look broken. Remedy: `touch` after every restore; sha256 still guards the content.

**Reported as a controller process finding: no artifact in `/mnt/d/hf_w27/` records it.** It is registered
because the failure mode is real, general, and expensive — a mutation arm that cannot restore is a mutation arm
whose results are all suspect — but a reader should know its evidence is testimony, not a file. It is also the
likeliest reason §7.4 has no artifact.

#### 7.8 A reused scorer emitted **the other rule's verdict banner** — now annotated

`s27_score.txt` — the raw byte comparison behind `S27-4` — is `score_repro_w27.py`, the **`RULE R27`
comparator**, pointed at the S27 population. That scorer emits its own rule name **unconditionally**, so its
last line reads `>>> RULE R27 = R-DIFFERS (40 files)`.

**For a period the record carried two contradictory `R27` verdicts**: `R-IDENTICAL` in `repro_all_score.txt`
(the real `R27`, B15 vs `table18`) and `R-DIFFERS` here (a different comparison entirely, B16 vs `table18`).
Both computations are correct about their own populations — the 40 differing files are exactly the two report
sinks × 20 datasets, which is half (A) working — but a reader grepping `RULE R27 =` found a contradiction.

**Closed at 10:27:52 by annotation, not by deletion**, which is the right repair: the file now opens with a
controller banner reading *"THIS FILE'S `>>> RULE R27 = R-DIFFERS` BANNER IS WRONG FOR THIS ARTIFACT AND MUST NOT
BE QUOTED"*, explains the reuse, and points at `s27_4_score.txt` as the scored clause. The wrong banner is left
in place at the foot of the file so the record shows what happened.

**The lesson, and it belongs with [F74](followups.md)'s family rather than [F80](followups.md)'s:**

> **Reuse the computation, never the banner — or the artifact lies about which rule it evaluated.**

F80's class is *prose that stops describing the instrument*. This is sharper and more dangerous: the
**verdict line itself** — the one line a future reader greps for — was correct for the code and wrong for the
run. Registered as [F90](#f90).

---

## §8 — What the design left unsatisfiable or gap-ridden

**Each is reported, not repaired. None was re-decided to make a verdict land.** The common remedy is the same in
every case and it has an order: **fix the rule first, in a pre-registration, and re-score afterwards — on a
population measured after the fix.** Repairing a rule with this wave's columns in hand, then harvesting a verdict
from the same numbers, is the pattern the [F14](followups.md) / [RULE D20](waves22+-handoff-prompt.md) fence
exists to forbid.

| # | defect | fired this wave? | verdict-determining? |
|---|---|---|---|
| §8.2 | **`S3`'s denominator is defined twice, incompatibly** | **yes** | **YES — `T-SOUND-WITH-NAMED-EXCEPTIONS` vs `T-CHANCE-DOMINATED`** |
| §8.1 | the verdict tree has an uncovered region | no | would be, if it fired |
| §8.3 | `CNL-EMPTYPOOL` is unreachable under the denominator used | n/a | no |
| §8.4 | `T27-V3` names a field one sidecar does not have | yes | no |
| §8.5 | a demoted dataset's effect on the count denominator is undefined | yes (6 datasets) | no, this wave only |
| §8.6 | `S27-1` names four fields; five ship | yes | no — blocks a ship clause |
| §8.7 | one clause found unsatisfiable **at pre-registration** | n/a | the discipline working |

### 8.1 The verdict tree has a **GAP**, and the scorer emits `T-TREE-GAP` for it

`SAT = 3, CHANCE = 3` disjoint (union 6) matches **no clause**: clause 3 caps the union at 4, clause 4 needs
`SAT ≥ 5`, clause 5 needs `CHANCE ≥ 5`. The scorer refuses to paper over it:

```python
verdict = "T-TREE-GAP"
tail = ("the PRE-REGISTERED verdict tree does not cover SAT=%d CHANCE=%d union=%d. An incomplete rule"
        " is a FINDING, not something to repair and re-score.")
```
— `score_t27_w27.py:1040-1042`

**It did not fire this wave** (`SAT = 1`, `CHANCE = 0`). It is a real defect and the whole uncovered region is
reachable: any outcome with `3 ≤ union ≤ 9` where neither set alone reaches 5 falls into it.

### 8.2 `S3`'s denominator is inconsistent **inside the design**, and the discrepancy is **VERDICT-DETERMINING**

| where | denominator |
|---|---|
| §4.1, statistics table | `Σ_f \|P_null(f)\| ÷ Σ_f \|R_null(f)\|` — the **shifted** at-risk pool |
| §4.4, population table | `Σ\|P_null\| ÷ Σ\|R\|` — the **unshifted** at-risk pool |

The scorer used §4.1's and **printed both**, naming the discrepancy rather than choosing silently. Applying the
pre-registered tree to each:

| | `SAT` | `CHANCE` | union | verdict |
|---|---|---|---|---|
| **§4.1 denominator (used)** | 1 (`D18`) | **0** | 1 | **`T-SOUND-WITH-NAMED-EXCEPTIONS`** |
| **§4.4 denominator (as printed)** | 1 (`D18`) | **6** — `D17` 10.0000, `D07` 2.0000, `D12` 1.0556, `D05` 0.7381, `D09` 0.4545, `D16` 0.4078 | 7 | **`T-CHANCE-DOMINATED`** (clause 5) |

**Two readings of one pre-registered document return two different verdicts about the owner's precision
column.** This is **the single most important methodological finding of the wave** — stated in the headline
above, and repeated here with its working. It must not be resolved by picking the one that reads better.

What can be said without re-deciding: under §4.4 three datasets return values **above 1.0** (`D17` = 10.0000,
`D07` = 2.0000, `D12` = 1.0556), which no quantity the design describes as *"the fraction of the shifted at-risk
pool the unshifted catalogue still protects"* can take — its numerator is drawn from the shifted pool and its
denominator from the unshifted one, so it is not a fraction of any pool. **That is diagnostic that §4.1 is the
intended reading. Diagnostic is not authority.** The `T-CHANCE-DOMINATED` branch is recorded as **live** until a
pre-registration closes it, and the closing must come **before** the re-score, not after.

Also note the internal consistency the two forms expose: under §4.4 exactly **ten** datasets return `n/a`
(`Σ|R| = 0`), and they are exactly the ten the `--protection off` arm reproduced exactly (§1). The two
independent computations agree about which datasets had no at-risk detections.

### 8.3 `CNL-EMPTYPOOL` is **unreachable** under the denominator that was used

Design §7 defines `CNL-EMPTYPOOL` (`Σ|R(d)| = 0` ⇒ `chanceProtected = n/a`, the dataset leaves S3's denominator,
which prints as `k of 19` with `k` computed). Under §4.1's `R_null` denominator the pool is never empty, so the
state never fires and **no `k of 19` line was ever printed**. A pre-registered could-not-look state that the
chosen statistic cannot emit is a dead clause. Under §4.4 it would have fired on 10 of 20 and printed `9 of 19`.
Same root cause as §8.2.

### 8.4 `T27-V3` names a field that does not exist on one of the two sidecars

Design §5.4 requires each row's `detected_f<F>.csv` to pair with a `*.truth.json` **and** a `*.golden.json`
*"whose `focuserPosition` field (from the sidecar, never parsed from the filename) equals `<F>`"*. The scorer
reports:

> `paired 180 of 180 manifest rows (the truth sidecar carries no focuserPosition field; it is paired`
> `  through the manifest's own FITS column, and the GOLDEN's field is what is asserted)`

**The clause is unsatisfiable as written for the truth sidecar** — the field is not there to check. The scorer
named the substitution in place instead of silently weakening the gate, which is the right behaviour, and the
golden's field *is* asserted on all 180. But the pairing of the truth sidecar rests on the manifest's FITS
column, which is one route, not two — a weaker guarantee than the clause promises, and the FAIL-end fixture
(mutating a row's focuser) exercises the golden half only.

### 8.5 A demoted dataset's effect on the count denominator is undefined

§4.3 says an `UNDER-POWERED` dataset *"becomes a named caveat rather than a verdict input"*, but the verdict tree
counts over "the 19-dataset verdict population" and nothing says whether a demoted dataset leaves that
denominator. Six of the 19 were demoted (§3.1). **Inert this wave** — all six were non-firing, so `SAT` and
`CHANCE` are unchanged either way — but on a run where an under-powered dataset fired a bar, the verdict would
depend on an unwritten rule.

### 8.6 `S27-1` names four fields; the port emits **five**

The clause checks `protectedStars` / `scoredFraction` / `precisionNull` / `truthViolations`. The port also emits
**`protectedDetections`** — protection *exercised*, as against `protectedStars`' protection *available* — and
`TruthDisclosure.cs:128` states why it was added: *"(`protectedStars`) without exercise is not readable as a
correction size."* **It is the one number a reader actually needs, and it is outside `S27-1`'s population, so it
ships unchecked by that clause.**

**It is single-routed, and this document does not repair the clause with the data in hand.** For the record: a
candidate second route exists — the `--protection off` arm's per-dataset `dFP` (§1) is the same quantity by a
different path, and its total is 350 — but that comparison was **not pre-registered and was not performed**, and
running it now and declaring agreement would be exactly the repair-and-harvest pattern the [F14](followups.md)
fence forbids. **Priced for wave 28: ~10 minutes**, pre-registered as a clause, not asserted here.

### 8.7 One clause was found unsatisfiable **at pre-registration**, and that is the discipline working

The design rejected `excess = (p_obs − p_null)/(1 − p_null)` before any data, on the ground that `FP = 0` on 19
of 20 datasets forces `p_obs = 1` and collapses the expression to `≡ 1` whatever the data say. **The published
`FP` column confirms the premise exactly**: `t27_score.txt`'s `T27-V0` table shows `FP = 0` on 19 of 20, `D08`
alone at 11. The clause would have passed silently on every dataset. Catching it before the wave rather than
after is the one place this series' machinery worked as designed with no cost at all.

### 8.8 Minor: a legend for a marker that never appears

`t27_score.txt` prints `legend: (e) marks precisionNull = 0 BY DEFINITION (no detection could be scored), not
measured` — the ASCII stand-in for the design's `†`. **No row carries it.** All five printed zeros (`D06`,
`D08`, `D09`, `D10`, `D16`) are therefore *measured* zeros, not definitional ones. Harmless, but a reader
scanning for the marker should know it is absent because nothing qualified, not because it was forgotten.

---

## §9 — The register entries §9 calls for, ready to paste into `docs/followups.md`

### F85 — A closed, shipped correction was re-measured as an open defect, because two fields share a name

**Status:** **Done** (2026-08-13, wave 27) — the disclosure gap is fixed; the register contradiction is
corrected in place · relates to [F31](#f31), [F68](#f68) part 1, [F79](#f79), [F84](#f84)

`golden eval`'s `FP` and a Python re-derivation from the golden's `stars` list alone are **both** "false
positives": **11** and **150** on the same detections. Wave 26 read the second and compared it against the
first, concluding the owner's precision column was a lower bound when it was already truth-corrected. The 150
decomposes as **50** excluded by the golden's own `unresolved` boxes + **89** excluded by `TruthProtection` +
**11** surviving genuine wing-frame junk; its `137` ("within 12 px of ANY truth star") is a **third** quantity,
neither of the other two. **The junk count is 11, not 13** — two of the "13" sit inside an `unresolved` box.

**Proximate cause:** `GoldenEvalRunner` applied `TruthProtection` and never reported that it did, while
`BankVerifyRunner` reported it in three places. A grep of the 20 published logs, `golden_eval.txt` and
`golden_eval_frames.csv` for `scoringMode` / `protected` / `precisionNull` returned **zero hits**.

**Independent confirmation of the 89:** `score_t27_w27.py --protection off` gives `D08` FP `11 → 100`
(`/mnt/d/hf_w27/t27_v0_failend.txt`), and **350** across the population.

**Remedy, shipped:** `TestApp/SynthBank/TruthDisclosure.cs` ports all four of `bank-verify`'s disclosures into
`golden eval` — `scoringMode` + `protectedStars`, `precisionNull`, `truthViolations`, `scoredFraction` — plus a
fifth, `protectedDetections` (protection *exercised*). Wording is shared between the two harnesses so they
cannot drift apart. **`RULE S27-4` proves the change is report-only at byte level: 360 of 360 detection
artifacts byte-identical, `TP`/`FP`/`FN` unchanged on 20 of 20, and 20 of 20 report sinks changed** (so the port
is not inert). **Open sub-item:** `S27-1`'s route agreement was not adjudicated, and `protectedDetections` is
single-routed — see wave 27 results §7.3 and §8.6.

### F86 — The golden sidecars on disk carry the PRE-REPAIR policy in their own words

**Status:** **Open — a named caveat, deliberately not fixed** (2026-08-13, wave 27) · relates to [F31](#f31),
[F85](#f85)

`GoldenFromTruth.cs:744-748` writes *"a detection here should count as a false positive"* into the `Reason`
field of every `omitted` component, and that string is sitting in **all 180 `*.golden.json` in the bank**. The
behaviour it describes was repaired on **2026-08-03** and the string was not, because it is a diagnostic field
nothing reads programmatically. **It is the single most likely thing that actually misled wave 26.**

Wave 27 fixes the two contradicting XML doc comments (`GoldenFromTruth.cs:34-36` *"a detector reporting
something at its location should be scored as a false positive"* vs `:76-84` *"scoring a detection there as a
false positive would be punishing a detector for something the reference itself cannot certify either way"*) and
**deliberately does NOT regenerate the sidecars**, for three reasons in this order:

1. regenerating 180 goldens collides with **fingerprint class 2 preservation** and would invalidate every
   precision/recall number this series has published;
2. changing only the generator string would make future sidecars disagree with the 180 on disk — a silent
   divergence, the same class of defect being fixed;
3. nothing reads `Reason` programmatically.

**Anyone reading a `Reason` field in a `*.golden.json` must read this entry first.** The correct policy is:
`omitted` and `unresolved` are both *visibility* judgements, visibility bears on **recall**, and a rendered star
is not a false positive at any SNR.

### F87 — A rule that describes itself must exclude itself from its own population, or it launders its own subject

**Status:** **Done** (2026-08-13, wave 27) — closed with a both-ends self-test · a **fourth** gap in
[F80](#f80)'s checker, and a **new class**, not the `_`-word-boundary family

`verify_derivation_w27.py`'s `in_historical_block()` scanned backwards for any line *containing* a
`HISTORICAL_BLOCKS` token and treated it as opening a historical literal — **but the line that DEFINES
`HISTORICAL_BLOCKS` contains every one of those tokens**, so from that line to EOF the `V27-B` clause was
switched **off**.

**Evidence, both ends:** before the repair the checker reported **`V-CLEAN`, 0 unlicensed tokens** on its own
file **while printing `=== RULE V26 ===` as its banner on a wave-27 root** (`/mnt/d/hf_w27/vdrift_w27_run1.txt`);
after the repair it reported **32 findings on itself** (`vdrift_w27_run2.txt`, the pre-repair copy is preserved
as `verify_derivation_w27.py.bak` and contains **69** literal `V26` occurrences), and **255** on the pinned
`/mnt/d/hf_w26` fixture (`vdrift_selftest_FINAL.txt` `[5b]`). *(A "53 tokens" figure quoted during the wave was
a `grep -c` of lines, a third quantity, and is withdrawn; a pre-repair "204" on the `hf_w26` fixture was read but
its artifact was not kept, so it is testimony. **Neither is load-bearing** — the run1-versus-run2 pair proves the
defect without any count.)*

**The lesson, and it generalises past this checker:** the first three F80 gaps were a *pattern* being too narrow.
This one is a *population* silently containing the instrument, so the instrument's own definition of what to look
for became a licence to stop looking. **Any self-describing rule must state whether its own source is in its
population, and prove the answer with a fixture.** Closed with self-test clause `[6]`, which demonstrates that a
real historical block is exempt, that the file after it is not, and that the definition line opens nothing. Two
FAIL fixtures are now pinned: `hf_w24` (kept) and `hf_w26` (added, not swapped).

### F88 — `git diff --name-only <tree>` omits UNTRACKED files, so a two-binary provenance diff can miss a whole new source file

**Status:** **Open — remedy known and applied once** (2026-08-13, wave 27) · belongs beside [F66](#f66)

Wave 27's B16 adds `TestApp/SynthBank/TruthDisclosure.cs` — the entire substance of the change — and it was
**untracked at build time**, so `binary_provenance_w27.txt`'s tracked diff lists 3 files and omits it.
`Q27-V1`, which intersects the change set against the `optimize` gate's reachable set to decide whether a
42-minute gate is owed, would have computed that intersection over an incomplete population.

**Remedy, applied:** take the **union** of `git diff --name-only <tree> -- '*.cs'` and `git ls-files --others
--exclude-standard -- '*.cs'`. `q27_v1.txt` does this and reports 5 files.

**Same shape as [F66](#f66):** a provenance instrument reporting confidently on a population that silently
excludes the thing under examination. Every future two-binary provenance step must take the union.

### F89 — A mutation harness that restores with `cp -p` restores the pre-mutation MTIME, and MSBuild then skips the recompile

**Status:** **Open — remedy known** (2026-08-13, wave 27, controller process finding; **no artifact**)

`cp -p` preserves mtime. After a mutant is restored with it, MSBuild sees a source no newer than its object and
**skips the rebuild**, so the next run silently re-measures the mutant. In wave 27 it made a correctly-restored
tree look broken.

**Remedy:** `touch` the file after every restore; keep the sha256 check, which guards content but says nothing
about mtime. **Recorded honestly: this finding's evidence is the controller's account, not a file.** One mutant
(`M-S3`) was subsequently re-verified with its run record kept (`s27_2_mutant_MS3.log`, 3 red of 19); `M-S1`,
`M-S2a` and `M-S2b` remain testimony, and this defect is the likeliest reason their records were lost.

### F90 — Reuse the computation, never the banner, or the artifact lies about which rule it evaluated

**Status:** **Open — remedy known, one instance annotated** (2026-08-13, wave 27) · belongs with
[F74](#f74)'s family, not [F80](#f80)'s

`score_repro_w27.py` — the `RULE R27` comparator — was reused to perform wave 27's `S27-4` byte comparison. It
emits its own rule name **unconditionally**, so `/mnt/d/hf_w27/s27_score.txt` ends
`>>> RULE R27 = R-DIFFERS (40 files)` while `/mnt/d/hf_w27/repro_all_score.txt` ends
`>>> RULE R27 = R-IDENTICAL`. **For a period the record carried two contradictory verdicts for the same rule
name**, on two different populations, and a reader grepping `RULE R27 =` would have found the contradiction with
no way to tell which was which.

**This is sharper than F80's prose drift.** F80 is about *commentary* that stops describing the instrument.
Here the **verdict line itself** — the one line a future reader greps — was correct for the code and wrong for
the run.

**Remedy:** a reusable scorer must take its rule label as an **argument** and assert it against the manifest it
was handed, exactly as this series already computes every ordinal rather than typing it. **Fixed for wave 27 by
annotation, not deletion**: `s27_score.txt` now opens with a banner saying its own verdict line must not be
quoted and naming `s27_4_score.txt` as the scored clause, with the wrong line left in place at the foot so the
record shows what happened.

### F91 — A BEFORE/AFTER control whose two sweeps are built by different expressions reports a FALSE violation

**Status:** **Done** (2026-08-13, wave 27) — caught by the population assertion, corrected, both readings kept ·
[F74](#f74)'s shape in the BEFORE/AFTER direction

Wave 27's first `T27-V4` AFTER sweep returned **`T27-V4 = VIOLATED`**. **Nothing on disk had changed.** The
AFTER expression omitted the **20 `.log` files** that the BEFORE expression had captured, so it compared 780
paths against a BEFORE list of 800 and reported the 20 absentees as moved. A fingerprint gate is the control
that decides whether a wave's denominators moved under it; a false `VIOLATED` there forces `T-UNEVALUATED` on a
wave whose population was in fact untouched.

**The only reason it surfaced** is the standing rule that the **population size is asserted inside the file** —
`BEFORE 800 / AFTER 780` is arithmetic, not a judgement call. The corrected sweep reads **`BEFORE 800 AFTER 800
compared 800 … DIFFERING: 0 → T27-V4 = PRESERVED`**.

**The rule, general:** *a BEFORE/AFTER control must build both populations from the SAME expression.* Two
expressions intended to describe one population are two populations, and every difference the control reports is
then unattributable — in either direction. Prefer one function, called twice, with the population size asserted
equal before any hash is compared.

### Amendments owed to existing entries

* **[F31](#f31)** — append `RULE T27`'s verdict: **the repair it made is SOUND on 18 of the 19 blind datasets,
  saturated on `D18_m24_deep_shed` alone (`precisionNull` 0.2820, `A_all` 0.2372, `A_protOnly` 0.1938), with
  `D01_ultrawide_40mm` a named near-miss at 0.2182.** F31 is **not** reopened — the verdict is neither
  `T-SATURATED` nor `T-CHANCE-DOMINATED`, so the density guard is not triggered. Also record that F31's null was
  demonstrated on one dataset (`D09`) and is now generalised to 20, at four offsets, and that the shipped
  single-offset control is **under-powered on 7 of them**.
* **[F83](#f83)** — the decision proceeds on real numbers. **Caveat 3's `+0.034` must carry a factor label**: it
  is `+0.034` at detection binning 1 and `+0.003` at binning 2, where `D08` reads `P=0.997 FP=1` (§4.3).
* **[F62](#f62)** — append `RULE D27` = `D-MOVED`, 6 of 6: scoring at the optimizer's own detection-binning
  factor moves `recall@all` by up to **+0.296** (`D14`) and by **−0.012** on `D15`. The no-`BestJ`-across-factors
  landmine now has a measured magnitude on the recall side.
* **[F21](#f21)** — add the measured `golden eval` rate (§10) so no future wave prices a golden-eval arm off the
  `optimize` rate.
* **[F80](#f80)** — cross-reference [F87](#f87) as the fourth gap and the first of a new class; and add the
  reuse-direction case from wave 27 results §7.8, where a scorer re-aimed at a new population kept the original
  rule's verdict banner, so `s27_score.txt` claims `RULE R27 = R-DIFFERS` while `repro_all_score.txt` claims
  `RULE R27 = R-IDENTICAL`.
* **`docs/synthetic-af-bank-results-table.md`** — annotate `D18`'s precision cell in place as **uninformative**
  with its three statistics; annotate `D01` with the near-miss; add the `scoredFraction` column or at minimum
  the six low rows of §3; re-state the seven `RULE D27` rows at both factors with each labelled.

---

## §10 — Estimate vs actual, which is itself a deliverable ([F21](followups.md))

From `/mnt/d/hf_w27/TIMING.tsv`:

| step | est (m) | actual (m) | note |
|---|---|---|---|
| 0 pre-registration | 10 | **8** | committed `df68232`, pushed |
| 1 pre-flight `V27` | 10 | **26** | **OVERRUN ×2.6** — found and closed the fourth F80 gap (§7.5) |
| 2 layout + manifest | 5 | **4** | layout 16 of 16; manifest 180/20/9, b2=7 |
| 3 scorer (agent) | 10 | **—** | *"in flight"* — **still never closed** |
| 4 `RULE T27` (B) | 95 | **22** | **UNDERRUN ×4.3** — `T-SOUND-WITH-NAMED-EXCEPTIONS`; scorer `--full` 80 s |
| 5 `RULE D27` | 25 | **14** | `D-MOVED` 6 of 6; both gates PASS, V1 FAIL end run |
| 6 half (A) port | 50 | **46** | 19 tests, 4 mutants; controller re-verified `M-S3` |
| 7 B16 + `Q27-V1` + `S27-4` | 35 | **18** | gate DISCHARGED with FAIL end; 360/360 bytes identical |
| 8 half (C) corrections | 20 | **18** | 7 documents corrected in place |
| 8 suite by COUNT *(duplicate ordinal)* | 5 | **5** | 3 992 (was 3 973), +19 named, `SUITE_EXIT=0` |
| **rows with both figures** | **250** | **161** | **~36 % under**, and the shape is the finding below |

**The dominant term is half (B), estimated at 95 m and delivered in 22.** The design priced the S1/S2/S3 arm at
40 m for *"numpy rasterisation; the dominant cost is `D01`/`D18`'s ~12 k objects per frame"* and the whole of (B)
at ~95 m; the scorer's `--full` pass ran in **80 seconds**. **This is [F21](followups.md) in the cheap direction
for the second wave running** — the same error the design warned about when it added the measured `golden eval`
rate. The lesson generalises past `golden eval`: **a pure-Python arm over artifacts already on disk is
consistently over-priced by this series by a factor of 3–4**, and over-reserving it is what pushes genuinely
expensive items (item 8) out of a wave.

**The bookkeeping itself was partly undelivered at timestamp 1 and was largely closed after it.** At timestamp 1
`TIMING.tsv` carried six rows and four estimates had no actual; four rows (`4`, `6`, `7`, suite) were filled in
at 10:14:37. Two residuals remain, both minor and both recorded rather than fixed: **step 3's actual is still
`-`**, and **two rows share the ordinal `8`** — the same typed-ordinal hazard `V27-C` exists to catch, in a
hand-maintained file the checker does not read. The results write-up is still unpriced, by construction.

**The one rate the charter's §5 budget table did not have, now MEASURED and to be carried forward:**

| instrument | rate | how measured |
|---|---|---|
| **`golden eval`, full 20-dataset arm, `table18` settings, B15** | **349 s (~6 m)** | measured, whole arm |
| **`golden eval`, one 9-frame cell** | **6 s (`D06`) – 46 s (`D01`)**, mean ~17 s | measured; the spread tracks star count, not frame count |
| `RULE D27`, 7 cells at factor 2 | **49 s** | `w27_d27.log`, `13:29:15Z → 13:30:04Z` |
| for comparison, `optimize --per-run --max-evals 250` | ~5.2 m **per run** | charter §5 — **~18× the whole arm** |

**Price a cell from the RANGE, not the mean.** `D01` is 7.7× `D06`, so a 7-cell arm's price depends entirely on
which seven.

---

## §11 — What was NOT run, and what it costs

| not run | price to close | status |
|---|---|---|
| **the 42 m `optimize` gate** | **42 m** | **DISCHARGED by `Q27-V1`, not skipped silently** — empty intersection with the reachable set, FAIL end demonstrated against the B14 tree. The discharge is **not** reported as a pass; what replaces it is hash identity + `S27-4`'s 360 byte-identical detection artifacts, a sharper control for this change |
| ~~`T27-V4` AFTER fingerprints~~ | ~~~4 m~~ | **CLOSED after timestamp 1** — `PRESERVED`, 800 of 800, after a false `VIOLATED` on the first sweep (§7.1) |
| ~~scorer self-test capture~~ | ~~~2 m~~ | **CLOSED after timestamp 1** — `SELFTEST T27 72 of 72`, plus an evidenced `--out` refusal (§7.2) |
| **`S27-1` route agreement, all five fields** | **~10 m** | **STILL NOT ADJUDICATED** (§7.3). The four registered fields are comparable directly from `t27_score.txt` against `s27_after/*/golden_eval.txt`; the fifth (`protectedDetections`) is **single-routed** and needs a **pre-registered** second route, not a post-hoc one (§8.6) |
| **`S27-2`: `M-S1` / `M-S2a` / `M-S2b` run records** | **~10 m** | **STILL TESTIMONY** (§7.4). `M-S3` is now evidenced (3 red of 19); the other three are not, and `M-S2`'s runner site is textually guarded only (§5.5) |
| **settling `S3`'s denominator** | **~30 m, and it must come FIRST** | **CANNOT be closed by running anything** (§8.2) — it is a defect in the rule, and the fix must be pre-registered *before* the re-score |
| item 8 — **wide-field recall, re-denominated** | **unpriced; needs a design** | untouched by wave 27 **by construction**, see §12 |
| regenerating the 180 `*.golden.json` for the `Reason` string | ~15 m compute, **unbounded credibility cost** | refused — fingerprint class 2 collision, F86 |
| changing the shipped null to multiple offsets | ~20 m | refused — would change every `bank-verify` number in the series; the variance bound is computed in the scorer instead |
| the integrated-SNR discriminator | ~30 m | **rejected on the merits** at pre-registration: degenerate within-frame (`SNR_int = SNR_peak/√p` with `p` near-constant per frame), and already superseded by `docs/golden-tier-plausibility-design.md` §2 |
| a truth-based **recall** denominator | ~30 m marginal | `TruthProtection` leaves recall untouched *by design* |

---

## §12 — What wave 28 should do, priced, and scored against the owner's three goals

The owner's goals (charter §8): **(1)** optimization + autofocus works across a wide range of setups, bridging
detected gaps; **(2)** accuracy of step-size recommendations; **(3)** appropriate exposure adjustments and
avoiding parameters pinned to extremes.

| # | item | price | goals | why now |
|---|---|---|---|---|
| **1** | **Item 8 — wide-field recall.** `recall@high` **0.183** (11 400 / 62 411) on the 40 mm rig, `recall@all` **0.123**. Needs its own pre-registration | **design 60 m + arm ~2 h** | **1** ✓✓✓ | **The largest untouched product gap in the bank.** See below — nothing wave 27 measured touches it |
| **2** | **Close wave 27's two remaining controls** — `S27-1` (incl. the fifth field, pre-registered) and the `M-S1`/`M-S2a`/`M-S2b` mutant records. *(`T27-V4` and the self-test capture closed after timestamp 1.)* | **~20 m total** | instrument | Both gate ship-clauses already relied upon |
| **3** | **Settle `S3`'s denominator in a pre-registration, THEN re-score** — the fix must precede the measurement, not follow it | **~30 m** (the scoring pass is minutes) | instrument | §8.2 — **two readings of the pre-registered design give two different verdicts about the owner's precision column.** The highest-value instrument item in the series right now |
| **4** | **Ship half (A)** — it is written, tested (+19), byte-proved and uncommitted | **~20 m** | 1, 3 | §5.4. Every future bank number becomes self-describing |
| **5** | **Re-state the 7 `RULE D27` rows at both factors** in the results table, each labelled | **~20 m** | 1, 2 | `D14` `recall@all` 0.641 → **0.937** is a published number that is wrong about the configuration the optimizer actually uses |
| **6** | **Fix the verdict-tree gap** in the next wave's pre-registration (clause for `3 ≤ union ≤ 9` with neither set at 5) | **~10 m** | instrument | §8.1 — inert this wave, live next |
| **7** | **Multi-offset null in the scorer as a standing statistic** — 7 datasets are under-powered at the shipped single offset | **~20 m** | instrument | §3.1; do **not** change the shipped null |

### 12.1 Item 8 is unaffected by everything wave 27 measured, and this needs saying explicitly

**Three independent reasons, all from source or from this wave's own numbers:**

1. **`TruthProtection` leaves recall untouched by design.** Its own scope statement: *"Recall is deliberately
   untouched … a protected star is not promoted to a required find."* Recall's denominator is the golden's
   `stars` list at tier ≥ high, which nothing in wave 27 moved. `RULE T27` is a statement about the **precision**
   column only.
2. **`RULE D27` does not reach the wide-field datasets.** Its population is the 7 datasets whose *derived*
   detection binning is 2. `D01_ultrawide_40mm`, `D02_rich_135mm` and `D03_redcat_250mm` are **factor-1**
   datasets and are not in it. `D01`'s `recall@all 0.123` is exactly as published.
3. **`D01`'s precision is `1.000` at an `FP` of 0 and a `scoredFraction` of 0.9678** — it is not a
   precision-side story at all. `D01` carries **96 760 false negatives** against 13 624 true positives. The gap
   is entirely recall.

**And the fence still binds.** Choosing new tier boundaries with the data in hand is exactly what
[F14](followups.md) / [RULE D20](waves22+-handoff-prompt.md) forbid, so item 8 needs its own pre-registration
before any statistic is read — which is why it is priced at *design 60 m plus arm*, not as an arm.

**Scored against the goals, item 8 is the only candidate that scores ✓✓✓ on goal 1**, and goal 1 is the goal the
bank exists to serve. Items 2, 3, 6 and 7 score zero on all three goals directly and are justified only as
instrument work — which the charter permits, but they should be run *because they are cheap and they gate
claims already made*, not because they advance the product.

---

## §13 — Controller deviations, all recorded, none laundered

| # | deviation | recorded where | assessment |
|---|---|---|---|
| 1 | **`RULE R27` ran BEFORE pre-registration** (`13:14:24Z`, against commit `df68232`) | `CONTROLLER_NOTE.md` §7, adopted by design §5.6a | **Legitimate and correctly declared.** A determinism control that reads no statistic in T27's tree; its FAIL end is demonstrated in both directions in `repro_all_w27.sh`'s self-test. Reported as *opportunistic*, in the same sentence as `R-IDENTICAL` |
| 2 | **The D27 arm dropped the plan's `--settings` pin** and pinned only `--profile-id` | `d27_arm_w27.sh:20-34` | **Correct, and better than the plan.** The published `table18` run passed no `--settings`; adding one alongside `--detection-binning 2` would change two things at once and make every delta unattributable. The charter's pin rule exists for `optimize` arms, where the settings file drives the search |
| 3 | **The `--profile-id` pin was demonstrated inert first**, not assumed | `pinprobe/`, `d27_arm_w27.sh:32-34` | **Correct in kind; state its `n` precisely.** The demonstration is **one dataset (`D09`), 20 of its 20 artifacts byte-identical** — *not* 20 datasets. By this series' own standing rule, *a control demonstrated on one dataset is a control demonstrated on one dataset* |
| 4 | **The derivation checker's FAIL fixture kept `hf_w24` and ADDED the design's `hf_w26`** rather than swapping | `vdrift_selftest_FINAL.txt` `[1]`, `[5b]` | **Strictly better than the design asked for.** Two pinned fixtures, neither of which is "whatever came before" |
| 5 | **`RULE T27` was scored in two arms** (`--gates` then `--full`), and the gates-only artifact prints `RULE T27 = NOT EVALUATED (gates-only run)` | `t27_gates.txt` | **Correct.** A gate a given arm cannot discharge prints as `n/a for this arm`, never as a pass — the scorer has a dedicated `gate_line()` for exactly this |

### 13.1 The controller was wrong twice, and both corrections are in the record

* **The junk count is 11, not 13.** Two of the "13" sit inside a golden `unresolved` box and were already
  excluded by `ExcludeUnresolved`; `golden eval`'s printed `FP = 11` is the correct figure. Corrected in
  `CONTROLLER_NOTE.md` §8 and propagated into `docs/synthetic-af-bank-results-table.md`.
* **The rival-cause framing (a)/(b)/(c) and the integrated-SNR discriminator were both rejected on substance** by
  the pre-registration agent (design §2). (a) OMISSION and (b) MIS-SPECIFICATION are not rival causes but the
  same fact at two levels of description; the diagnostic question is a fourth one — **(d) MIS-ASSIGNED SIDE**,
  decidable from source at zero compute, and it is what `TruthProtection` already fixed. The discriminator is
  degenerate within a frame and was already superseded by name in
  `docs/golden-tier-plausibility-design.md` §2.

**Both corrections strengthen rather than weaken the wave's conclusions**, and the second one saved ~30 minutes
of re-treading a fenced result.

---

## §14 — One-paragraph summary

**The assigned backlog item was already done, and the wave's real product was finding that out and proving the
shipped correction sound.** `RULE T27` = `T-SOUND-WITH-NAMED-EXCEPTIONS`, now on **4 of 4** gates: the owner's
precision column is honest as published on 18 of 19 blind datasets, uninformative on `D18_m24_deep_shed`
(`precisionNull` 0.2820 against a 0.25 bar), with `D01_ultrawide_40mm` a named near-miss at 0.2182 — a margin
narrower than the shipped null's own single-draw spread. `RULE D27` = `D-MOVED` on 6 of 6: seven published rows
were scored at the wrong detection binning and `D14`'s `recall@all` moves 0.641 → 0.937. Half (A) is written,
byte-proved report-only on 360 artifacts, and **still uncommitted**. The quiet finding is `scoredFraction`: a
third of `D12`'s detections never entered its precision ratio, and no reader could have seen that before this
wave.

**Between the two timestamps four open rows were closed** — `T27-V4` (`PRESERVED`, 800 of 800, *after a false
`VIOLATED` that its own population assertion caught*), the scorer's self-test (`72 of 72`), `M-S3`'s red run, and
the contradictory `R27` banner (annotated, not deleted). **Three things remain open and are marked open in
place:** `S27-1`'s route agreement, three mutant records that are still testimony, and half (A)'s commit.

**And one finding cannot be closed by running anything: the design defines `S3`'s denominator twice,
incompatibly, and the two readings return `T-SOUND-WITH-NAMED-EXCEPTIONS` and `T-CHANCE-DOMINATED`
respectively.** That is the wave's most important methodological result. It is reported, not repaired; the
evidence that §4.1 is the intended reading is recorded as diagnostic and explicitly not as authority; and the
next wave must fix the rule **before** it re-scores.
