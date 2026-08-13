# P-D08 from n = 1 to n = 3 — the prediction, fixed BEFORE the data

**Written and committed before the two cells are scored.** This is a follow-on to wave 26, not a wave: no
binary is built, no gate is run, and it costs two `golden eval` cells.

## Why it is worth thirty minutes

Wave 26 confirmed `PREDICTION P-D08` **as a number at n = 1**: `D08_c11_2800mm` was the only at-floor landing
whose combined effective gate (`0.5625`) fell below `OptimizerVariable.cs`'s ~`1.5` provably-inert boundary, and
it is the only one of the four scored landings where raising `BrightnessSensitivity` from `0.0` to `10.0` moved
precision (`0.966 -> 1.000`). On the other three — gates `2.39`, `2.36`, `2.13`, all **above** the boundary —
precision was already `1.000` and only recall moved.

**n = 1 is a lead, not a law.** Two more sub-boundary landings exist and have never been scored.

## The population, and why it is blind

| landing | seed | combined effective gate | scored before? |
|---|---|---|---|
| `D01_ultrawide_40mm` | **seedA1** | **0.2234** | **no** |
| `D16_esprit550_ha3` | **seedA1** | **0.1969** | **no** |

**Both are blind.** The owner's results table scored `D01` and `D16` at their **seedA0** landings, where neither
is at the floor; the at-floor landings for these two datasets are on **seedA1**, and `seedA1` has never been put
through `golden eval`. The controller has not read either number.

## The prediction, and both branches

`P-D08` says the pin costs real precision **only** where the combined gate falls below the inert boundary. So:

- **CONFIRMING at n = 3:** `D01`'s and `D16`'s base precision (at `Sensitivity = 0.0`) is **below 1.000**, and
  rises when sensitivity is raised to `10.0`.
- **REFUTING:** either base precision is already `1.000` — i.e. a sub-boundary gate does **not** imply a
  precision cost, and `D08` was one dataset behaving one way.

**Both are reachable and neither is a failure.** A refutation is the more useful outcome, because it would mean
the run's sharpest goal-3 lead is `D08`-specific and must not be generalised.

## The caveat that survives either result, restated so it is not lost

Wave 26 established that `D08`'s 11 false positives are **3 on frame 13672 and 8 on frame 14328 — the two
extreme wing frames — and 0 on all seven interior frames**, which is [F31](followups.md)'s measured mechanism
quantified on `D08` by name. **A precision number that moves may be the golden reference omitting real faint
stars at extreme defocus rather than the detector admitting junk.** Separating those needs a `*.truth.json`
re-score, which is **not** run here. This follow-on can therefore confirm or refute the *pattern*; it cannot by
itself establish that the pin admits false detections.

## Method, fixed here

```
TestApp.exe golden eval --runs "D:\SyntheticAutofocusBank\<DS>" \
  --params optimized --opt-results "D:\hf_w18\seedA1\<DS>\attempt01" \
  --match center --match-radius 12 --pixel-scale header --out <out>
```
and the same again with `--sensitivity 10.0` appended. B15, the already-gated binary. **The snapshot source line
in each log must be verified to name `seedA1`** — `--opt-results` falls back to the run folder silently when it
does not resolve, and reports success either way (wave 25 lost three attempts to that).

---

# RESULT — `P-D08` is REFUTED at n = 3

Measured `2026-08-13T09:06-09:08Z` on B15. All four cells verified from their own log's
`source: optimized (...)` line to have used the **`seedA1`** snapshot, not a silent fallback to the run folder.

| landing | combined gate | base precision | FP at base | precision at `--sensitivity 10.0` | `recall@all` | `recall@high` |
|---|---|---|---|---|---|---|
| `D01_ultrawide_40mm` | **0.2234** | **1.000** | **0** | 1.000 | 0.520 -> 0.342 | 0.295 -> 0.234 |
| `D16_esprit550_ha3` | **0.1969** | **1.000** | **0** | 1.000 | 0.473 -> 0.469 | 0.821 -> **0.821** |

**Both blind sub-boundary landings carry ZERO false positives at `Sensitivity = 0.0`.** The prediction's
confirming branch required base precision below `1.000`; it is `1.000` on both, with `FP = 0` on both. **A
combined effective gate below `OptimizerVariable.cs`'s ~`1.5` provably-inert boundary does NOT imply a precision
cost.**

## What this retires, and what it leaves

- **RETIRED: the generalisation.** `D08_c11_2800mm` is **one dataset behaving one way**, not the visible member
  of a class. No claim of the form *"landings with sub-boundary gates admit false positives"* is supported, and
  wave 26's results document must be read with that correction — its `Q26-C` numbers stand, its `P-D08`
  confirmation is now **n = 1 and refuted at n = 3**.
- **STRENGTHENED: [F31](followups.md)'s explanation of `D08`.** `D08`'s 11 false positives were **3 on frame
  13672 and 8 on frame 14328 — the two extreme wing frames — and 0 on all seven interior frames.** Two further
  sub-boundary landings now produce **no false positives at all**. That the only precision movement in the whole
  bank occurs on one dataset, concentrated entirely on its extreme-defocus frames, is much better explained by
  the golden reference omitting real faint stars there than by a sensitivity pin admitting junk. **Still not
  established** — it needs the `*.truth.json` re-score, which remains unrun.
- **UNCHANGED: [F83](followups.md).** `J` still carries no precision term on an unlabelled run, and the pin is
  still a real optimum of that objective (`Q-PIN-COSTED`, `A-RESPONSIVE` 0 flat of 8). **What changes is the
  motive for acting on it.** The case is no longer "the pin costs precision" — on 3 of 4 sub-boundary landings
  it demonstrably does not. The case is that **the objective cannot see precision at all**, which is an argument
  about what `J` should contain, not about damage already measured.
- **A second-order observation, reported without a bar:** raising sensitivity to `10.0` costs `D01` a great deal
  of recall (`0.520 -> 0.342`) and `D16` almost none (`0.473 -> 0.469`, with `recall@high` **identical** at
  `0.821`). The cost of forbidding the extreme is not uniform even among landings that pin, which is consistent
  with `Q26-D`'s finding that it is nearly free wherever the search was not going there anyway.

**The pre-registration's own words, met:** *"A refutation is the more useful outcome, because it would mean the
run's sharpest goal-3 lead is `D08`-specific and must not be generalised."* It is, and it must not.

---

# F31's caveat, SETTLED — 91.3 % of `D08`'s "false positives" are real stars the golden omits

Measured `2026-08-13T09:29Z`, zero TestApp minutes. The detections are **the ones `golden eval` itself scored**,
read out of its own `detected_f*.csv`; they are re-matched against each frame's `*.truth.json` — the renderer's
complete star list — at **the same 12.0 px radius** the golden scoring uses, taken from `synthetic_meta.json`'s
`matchRadiusPx` rather than chosen here. Population: the `D08_c11_2800mm` row of the owner's results table
(the **seedA0** landing, precision `0.966`). Artifact: `/mnt/d/hf_w26/pd08/F31_truth_rescore.txt`.

| focuser | truth stars | golden stars | detected | FP vs golden | of those, REAL in truth |
|---|---|---|---|---|---|
| 13672 | 126 | **9** | 10 | 4 | **0** — junk |
| 13754 | 126 | 14 | 16 | 3 | 3 — all real |
| 13836 | 125 | 29 | 48 | 19 | 19 — all real |
| 13918 | 125 | 65 | 92 | 27 | 27 — all real |
| 14000 | 123 | 81 | 119 | 38 | 38 — all real |
| 14082 | 125 | 65 | 97 | 32 | 32 — all real |
| 14164 | 125 | 29 | 45 | 16 | 16 — all real |
| 14246 | 126 | 14 | 16 | 2 | 2 — all real |
| 14328 | 126 | **9** | 15 | 9 | **0** — junk |
| **total** | | | | **150** | **137 = 0.913** |

## Two conclusions, and they point in different directions

**1. Precision measured against the golden is systematically PESSIMISTIC, and the owner's table's precision
column is a LOWER BOUND.** The golden lists **9** stars on a frame where the renderer placed **126**. Of the 150
detections the golden calls false, **137 are real rendered stars**. `D08`'s true precision is nearer **0.997**
than the measured `0.966`. This is [F31](followups.md)'s mechanism, now quantified as a rate rather than
described: the reference under-lists, most heavily at extreme defocus, and the detector is penalised for finding
what the reference omitted.

**2. But the 13 genuine junk detections are REAL, and they are perfectly localised.** All 13 fall on the **two
extreme wing frames** (`13672`, `14328`) and **zero** on any of the seven interior frames. So the detector does
produce spurious detections — only at the extremes of the sweep, and only there. That is a small, precisely
located defect worth its own entry, and it is **not** an artifact of the reference.

## What this does to the sensitivity story

It removes the last support for reading `D08` as evidence that the pin admits junk. Taken with `P-D08`'s
refutation at n = 3 immediately above — two further sub-boundary landings with **precision 1.000 and zero false
positives** — the position is now:

- the pin does **not** measurably cost precision anywhere on this bank;
- `D08`'s apparent precision cost is **91.3 % reference omission**;
- what remains is 13 junk detections confined to extreme-defocus frames, which is a **detector-at-its-limit**
  observation, not a sensitivity-pin one.

**[F83](followups.md) is unaffected and remains the finding.** `J` still carries no precision term on an
unlabelled run. The argument for acting on it was never that measured damage exists — it is that **nothing in
the objective would report the damage if it did**, which this whole exercise illustrates: it took a truth
re-score, outside `J` entirely, to find out that the one precision number on the bank was mostly an artifact.
