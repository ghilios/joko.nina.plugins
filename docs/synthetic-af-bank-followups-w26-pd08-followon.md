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
