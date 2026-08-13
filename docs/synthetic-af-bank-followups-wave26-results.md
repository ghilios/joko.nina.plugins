# Wave 26 — results

**Write-up timestamp: `2026-08-13T07:11:52Z`.** Everything below is the state of `/mnt/d/hf_w26/` at that
instant. **Phase `d` was still RUNNING at that timestamp** and is marked **OPEN in place** in §14; its resolution
is appended there rather than by editing a row — wave 23 §12's pattern, kept because a results document that
back-fills its own open rows cannot be audited afterwards.

Pre-registration: [`docs/synthetic-af-bank-followups-wave26-design.md`](synthetic-af-bank-followups-wave26-design.md)
(committed `8b7603a`, **before any wave-26 measurement**).
Plan: [`plans/synthetic-af-bank-followups-wave26-plan.md`](../plans/synthetic-af-bank-followups-wave26-plan.md).
Binary: **B15**, `BuildId d79dae73582e4a6c95e5c81f1f143ffc`, tree `29665a62`, reused from `/mnt/d/hf_w25/exe`.
**No C# was compiled by this wave.**
Predecessor: [`docs/synthetic-af-bank-followups-wave25-results.md`](synthetic-af-bank-followups-wave25-results.md).
The question this wave finishes: **`RULE P23`**, [wave 23 results §5](synthetic-af-bank-followups-wave23-results.md).
Direct input: [`docs/synthetic-af-bank-results-table.md`](synthetic-af-bank-results-table.md) (`7e98915`).

**This is the last wave of the run.** §16 gives the state of the register.

---

## Status

| | |
|---|---|
| **vessel** | **CONTINUED** on `ghilios/synthetic-af-bank-followups-wave23`, **PR #195**. Decided in writing in design §0, before any measurement |
| **binary** | **B15 reused. Nothing built.** `Q26-V0`: `TestApp.dll` sha256 `2fb0c8fd…`, `G25_PASSED` present with the matching BuildId, and **the comparator demonstrated on a real different published binary (B14)** |
| **the gate** | **DELIBERATELY SKIPPED**, under design §5's four conditions, and replaced by `Q26-V0` + `Q26-V3` + `Q26-V5`. §1 says why that is a control and not a shortcut |
| **`RULE V26`** (pre-flight, blocking) | `V-DRIFT` (8 findings) → **`V-CLEAN`** after fixes. **A third derivation-drift shape found and demonstrated** (§2) |
| **prep** | **40 landings resolved, 20 + 20 readable, 0 could-not-look. 8 at the resolved floor** — independently reproducing `RULE P23`'s published count. **32 settings vectors** |
| **`Q26-V3`** | **8 of 8** base cells reproduce **wave 18's `FinalJ` EXACTLY** (`repr()` identity), B1/B2 → B15. First cross-binary objective comparison in the series |
| **`Q26-V4`** | echo-back **24 of 24** perturbed cells |
| **`Q26-V5`** | determinism **IDENTICAL**, and the same comparator on `(base, both)` reports NOT-IDENTICAL — it discriminates |
| **`Q26-A`** | **`A-RESPONSIVE`** — **0 flat of 8**, F6 diagonal-sign signature **0 of 8**. **The controller's hypothesis is REFUTED** (§4) |
| **`Q26-B`** | **4 of 4 COSTED.** 0 lost the hard floor, 0 equal-or-better |
| **`Q26-C`** | `D08` precision **0.966 → 1.000 (+0.034)**, `recall@all` **0.978 → 0.803 (−0.175)**. The other three: precision already 1.000, recall −0.235 / −0.039 / −0.112. **PREDICTION P-D08 CONFIRMED, n = 1** |
| **`RULE L26`** | **COULD-NOT-LOOK** — `af-fit` produced no `af_fit_points.csv`; the instrument named the absence, wrote 0 rows and refused |
| **`RULE Q26`** | **`Q-PIN-COSTED`** |
| **the headline** | **`J` on this bank carries NO precision term**, established at **zero compute** before any arm ran (`OptimizationObjective.cs:484-490`). §6 |
| **ships** | **nothing.** No plugin code, no TestApp code, no test, no binary |
| **suite** | **OPEN at the timestamp.** Expected `3973` by COUNT ([F37](followups.md)); no C# changed, so any other number is an environment problem, not this wave's |

**Open at `07:11:52Z`:** phase `d` (running, deadline `10:10Z` — §14); the full suite; the AFTER fingerprints and
their diff; the commit, the push and PR #195's body.

---

## §0 — What this document is allowed to say

Design §11 fixes it in advance and it is obeyed here.

- **No claim that the pin is "cosmetic" on the strength of `Q26-A` alone.** `Q26-A` is a statement about the
  neighbourhood of one point; §7's inertness reading is a *different* argument from a *different* artifact and is
  labelled as such.
- **No pooling of `Q26-D` into `Q26-B`'s counts**, and no rate whose denominator mixes Q2 and Q4.
- **No pooling of seedA0 and seedA1.** `Q26-A` reports both halves separately; the seedA1 half is a blind
  replication (design §10) and is never averaged with the seedA0 half.
- **No bar invented for a "reported, no bar" clause**: `Q26-A`'s magnitudes and ratios, `Q26-B`'s `dJ`, `Q26-C`
  entirely, `Q26-D` entirely.
- **No goal-2 accuracy rate.** Wave 25 §16.1 retired `13 of 17`; nothing here replaces it.
- **No claim about the wide end of [F25](followups.md)**, which remains unmeasured.
- **`RULE F14` / `RULE S16` / `RULE D20` are three permanent fences.** No clause in this wave reads any of them.
- **No recommendation to change `DefaultSensitivityLower` stated as anything but a costed recommendation.** §9
  lays out three options with their evidence and prices and **picks none**.

---

## §1 — The gate was SKIPPED, and `Q26-V0` is a control, not a shortcut

The distinction matters enough to state before any number.

**A sixteenth gate would have certified a binary that nothing in this wave changed.** B15 was built by wave 25,
`G25` certified it (eight `FinalJ` values bit-identical at sixteen digits, `BuildId` novel against fourteen), and
wave 26 compiled no C#. Re-running `G25` would have re-derived the same eight numbers from the same bytes: it
tests determinism, and determinism was already tested, on this binary, by the wave that produced it.

**The risk wave 26 actually carries is a different one, and no gate in this series has ever tested it.** Every
`Q26-A` cell compares a fresh B15 evaluation against wave 18's landings, which were produced on **B1 and B2,
fifteen binaries ago**. What licenses that comparison is not B15's internal determinism but whether **B15's
objective evaluates the same vector to the same `J` as B1/B2 did**.

So the gate's ~42 minutes were replaced by three controls that each bind on *this wave's own instrument*:

| control | what it establishes | its FAIL end, measured |
|---|---|---|
| **`Q26-V0`** | the bytes on disk are `G25`'s certified binary | **B14's `TestApp.dll` hashes differently** (`5398582d…` vs B15's `2fb0c8fd…`). The comparator is *shown* to distinguish two real published binaries, so an accepting result is not vacuous |
| **`Q26-V5`** | the arm's own instrument is deterministic | the **same comparator** applied to `(base, both)` reports NOT-IDENTICAL |
| **`Q26-V3`** | **the thing the gate never checked**: cross-binary objective comparability | a miss would have been NAMED and quantified without voiding the rule |

**`Q26-V0` is a control because it can refuse and was shown refusing.** The wave's own artifact carries the
demonstration in its last line: *"ok: the comparator DISTINGUISHES B15 from B14, so an accepting result is not
vacuous."* A hash check that has never been shown to fail on a real different input is a ritual; this one was.

**The decision reverses on any of three triggers**, pre-registered in design §5: one line of C# changing, a hash
or BuildId mismatch, or a non-deterministic `Q26-V5`. **None fired.**

**`Q26-V0` also carries the mandatory two-binary provenance**, B14's tree `476369ac` → B15's tree `29665a62`,
with the eight changed `.cs` files listed by name — recorded even though this wave ships no code, because the
comparison it licenses is what makes `Q26-V3` interpretable.

---

## §2 — `RULE V26`: the pre-flight fired again, and it found a THIRD shape of the same defect

`V-DRIFT`, **8 findings**, on instruments written from scratch an hour earlier — three `V26-A` header-prose
references and five `V26-B` hits on `G25_PASSED` and `/mnt/d/hf_w25/table18`. The last two are **legitimate**
cross-wave references (this wave reads wave 25's gate certificate instead of re-gating, and the scorer's
self-test parses a published `golden_eval.txt`) and were **declared in `ALLOW` with reasons** rather than
suppressed. Re-run: **`V-CLEAN`**, 2 of 2 sibling targets resolve, 0 tokens, 0 typed ordinals.

### 2.1 The third shape: the rule LETTER, and it is the same underscore for the third time

Wave 25 closed two gaps on opposite affixes — `G24_START` (suffix) and `w24_gate_log_wsl` (prefix) — both because
`_` is a word character and `\b` cannot see it. The pattern that closed them still enumerated the rule letters
`[GBRD]` and gave the bare `\bW%d\b` arm **no suffix arm at all**. Waves 21–25 used `W`, `N`, `M`, `V`, `P`, `S`.
Measured against wave 25's own pattern, not asserted:

| token | a real artifact of wave 25? | wave 25's pattern |
|---|---|---|
| `G25_START` | yes | matches |
| `w25_layout_self_test` | yes | matches |
| **`W25_BEFORE_READY`** | **yes — wave 25's own arm interlock marker** | **MISS** |
| `g25_score.txt`, `v25_passend.txt`, `n25e_score.txt`, `p3c_score.txt` | yes, all four on disk | **MISS** |

`W25_BEFORE_READY` is **exactly the load-bearing class on a different letter**: a surviving `*_READY` marker is
what hangs the controller's waiter. `n25e_score` matters separately — a letter follows the digits with **no
separator**, so an optional `_SUFFIX` is not enough and the tail must be `[a-z0-9_]*`. The pattern is now matched
by **shape**, not by an enumeration of the letters that happened to have been used:

```python
r"(?:\b[a-z]%d[a-z0-9_]*\b|\b[A-Z]%d(?:_[A-Z0-9][A-Z0-9_]*)?\b|prov_w%d|hf_w%d|score_\w*_w%d|_w%d\b)"
```

**Demonstrated in both directions**: all five shapes are reported, and this wave's own affixed names
(`Q26_START`, `w26_layout_self_test`) are **not**, so the check discriminates rather than merely firing.

### 2.2 The FAIL end had to be PINNED, because wave 25 repaired its own root

Wave 25's self-test used *"the previous wave's root"* as its known-bad input. **That works exactly once.** Wave 25
then ran the checker over its own root and repaired all 21 findings, so `/mnt/d/hf_w25` is `V-CLEAN` and a
wave-26 checker pointed there would have found nothing and reported itself broken. **A checker whose known-bad
input is "the previous wave" stops having one the moment the previous wave adopts the checker.** The known-bad
root is now named in the source (`/mnt/d/hf_w24`, declared in `ALLOW` with its reason).

### 2.3 A control that exited its caller while printing PASS

`layout_w26.sh`'s trailing `--self-test` dispatcher **fired when the file was SOURCED.** A sourced bash file sees
the parent's `$1`, so `q26_arm_w26.sh --self-test` sourced the layout, the layout's own dispatcher matched,
`w26_layout_self_test` ran, and **`exit "$?"` terminated the arm driver inside its own `source` line** — printing
a clean passing layout self-test and running **not one line** of the arm's own checks. It looked exactly like a
pass. Fixed with `[ "${BASH_SOURCE[0]}" = "$0" ]`.

**This is wave 24's `[ now > "23:59" ]` in a new costume: a control that cannot fail, printing PASS.** It was
found only by *reading the output* of the self-test rather than its exit code — the charter's own
"READ LOGS, NOT EXIT CODES", applied to an instrument instead of an arm.

Second finding, smaller: the arm's F15 guard (*"the bank-writing flag must appear on no invocation line"*) greps
its own file and therefore **reported the defect it was made of**, on its first run. Excluded by an inline marker
on its own three lines.

### 2.4 And the plan's own fingerprint command was broken by a space in a path

Plan §2's class-1 fingerprint is
`find "/mnt/d/Autofocus Bank" … | sort | xargs sha256sum`. `xargs` splits on whitespace, so **`/mnt/d/Autofocus`
and `Bank/...` became two arguments** and the command produced **20 landings instead of 42**. It was caught only
by the population assertion (`wc -l` against the expected count), not by an exit code — `xargs` and `sha256sum`
both succeeded on the paths they could resolve. The artifact on disk now carries **42 of 42**
(`fingerprints/bank_landing_BEFORE.txt`, 22 of them under `/mnt/d/Autofocus Bank`).

**A command that half-succeeds on a path it mis-split is the same class as §2.3's dispatcher**: the failure is in
the output, and only a population assertion sees it.

### 2.5 `--out` is now mandatory in the scorer, and this is wave 25's defect fixed structurally

Wave 25 lost its central scorer's output file twice to a `tee` that was written into the plan but never executed,
and had to re-derive every `W25`/`N25`/`M25` number from the arms' own reports (wave 25 §0.2).
`score_q26_w26.py` **refuses to run without `--out`**. The artifact is written by the scorer itself, not by a
redirect a controller has to remember, and `Q26_PASSED` is written by the scorer on `rc == 0` only.

**That is a defect fixed by construction rather than by remembering** — the same move [F75](followups.md) asks
for on interlocks, applied to a scorer's own evidence.

---

## §3 — The population: 8 at the floor, resolved independently, reproducing `RULE P23`

| | |
|---|---|
| landings found and readable | **20 of 20** on `seedA0`, **20 of 20** on `seedA1` |
| **COULD-NOT-LOOK** | **0** |
| landings resolved | **40** |
| **at the resolved sensitivity floor** | **8** |
| settings vectors written | **32** = 8 at-floor landings × {base, sens, clip, both} |

The floor is resolved **per landing from that landing's own `Provenance.CommandLine`** (`--sensitivity-floor`),
never defaulted; the knob is read from the flat `optimized_settings.json` copy and **cross-checked to `repr()`
bit-identity against the two camelCase copies** in `hocusfocus_star_detection.json` (design §8's `Q26-V2b`). No
landing disagreed with itself.

**`RULE P23` published `8 of 40 = 0.2000` and this wave resolved 8 of 40 from source predicates, independently.**
That is a reproduction across two waves and two instruments, and it is the reason the rest of the wave has a
population it can trust.

The eight, with their effective gates:

| root | dataset | `Sensitivity` | `StarClippingMultiplier` | `PeakResponse` | **effective gate** |
|---|---|---|---|---|---|
| seedA0 | `D08_c11_2800mm` | 0.0 | **0.75** | 0.75 | **0.5625** |
| seedA0 | `D10_rc16_3250mm_sparse` | 0.0 | 3.1875 | 0.75 | 2.390625 |
| seedA0 | `D11_rc10_585_afbin2` | 0.0 | 3.375 | 0.7 | 2.3625 |
| seedA0 | `D12_c14_585_afbin2` | 0.0 | 2.75 | 0.775 | 2.13125 |
| seedA1 | `D01_ultrawide_40mm` | 0.0 | **0.25** (axis floor) | 0.89375 | **0.22343750000000004** |
| seedA1 | `D09_c14_3800mm` | 0.0 | 3.375 | 0.7375 | 2.4890625 |
| seedA1 | `D12_c14_585_afbin2` | 0.0 | 2.0 | 0.8 | 1.6 |
| seedA1 | `D16_esprit550_ha3` | 0.0 | **0.25** (axis floor) | 0.7875 | **0.196875** |

The four seedA0 rows are the owner's published set. **The four seedA1 rows were unknown to the design** — the
only seedA1 landing its author read is `D08`'s, which lands at `7.75` and is therefore *not* at floor. So seedA0
and seedA1 do not pin on the same datasets, and `Q26-A`'s seedA1 half is a **blind replication**.

---

## §4 — `Q26-A`: **`A-RESPONSIVE`**. The controller's hypothesis is REFUTED, and that is a good outcome

### 4.1 The validity clauses first

| clause | result |
|---|---|
| **`Q26-V4`** — the perturbation reached the LIVE copy | **24 of 24** perturbed cells' emitted landings carry the intended `BrightnessSensitivity` by `repr()`. [F71](followups.md)'s dead-copy trap did not fire |
| **`Q26-V5`** — determinism | `seedA0/D08`'s base cell run twice: `0.9963959372017032` vs `0.9963959372017032`, **IDENTICAL**; the same comparator on `(base, both)` reports **NOT-IDENTICAL** |
| **`Q26-V3`** — cross-binary comparability | **8 of 8 IDENTICAL** — see §4.2 |

### 4.2 `Q26-V3`: B15 reproduces wave 18's `FinalJ` exactly, on all eight, across fifteen binaries

| root | dataset | B15 `BaselineJ` | wave 18 `FinalJ` | |
|---|---|---|---|---|
| seedA0 | `D08_c11_2800mm` | 0.9963959372017032 | 0.9963959372017032 | IDENTICAL |
| seedA0 | `D10_rc16_3250mm_sparse` | 0.9934488063105885 | 0.9934488063105885 | IDENTICAL |
| seedA0 | `D11_rc10_585_afbin2` | 0.9961036483632415 | 0.9961036483632415 | IDENTICAL |
| seedA0 | `D12_c14_585_afbin2` | 0.9957418877445253 | 0.9957418877445253 | IDENTICAL |
| seedA1 | `D01_ultrawide_40mm` | 0.9978172446397623 | 0.9978172446397623 | IDENTICAL |
| seedA1 | `D09_c14_3800mm` | 0.9962785589845005 | 0.9962785589845005 | IDENTICAL |
| seedA1 | `D12_c14_585_afbin2` | 0.9954983450392348 | 0.9954983450392348 | IDENTICAL |
| seedA1 | `D16_esprit550_ha3` | 0.9968654432578308 | 0.9968654432578308 | IDENTICAL |

**8 of 8, at all sixteen digits.** This is the **first time this series has compared an objective evaluation
across binaries** — wave 18's landings were produced on B1/B2 (`BuildId e745c958…` and `7a3a03ba…`); this
evaluation is B15. It is reported as a **labelled diagnostic**, and what it licenses is narrow and worth stating:
**a wave 18 `J` and a wave 26 `J` are the same quantity**, which is what makes every wave-18 number quoted in this
document a legitimate reference scale rather than a historical curiosity.

### 4.3 The 2×2, per landing. **0 flat of 8**

`dJ = J(base) − J(variant)`; **positive means the perturbation COSTS objective**. The reference scale beside each
row is that landing's **own** wave-18 search gain (`FinalJ − BaselineJ`) — a named denominator, **not a bar**.

| root | dataset | `J(base)` | `dJ(sens)` | `dJ(clip)` | `dJ(both)` | its own w18 search gain |
|---|---|---|---|---|---|---|
| seedA0 | `D08_c11_2800mm` | 0.9963959372017032 | 0.0011316330906263605 | 0.001985603821304749 | 0.0015213858682600057 | 0.0012207384978685232 |
| seedA0 | `D10_rc16_3250mm_sparse` | 0.9934488063105885 | **0.04938049536132738** | 0.0028585089162932453 | 0.0492593571234784 | 0.03624753384191459 |
| seedA0 | `D11_rc10_585_afbin2` | 0.9961036483632415 | 0.0009243274807033686 | 0.00014710391834671377 | 0.000882733896822252 | 0.010116654140490455 |
| seedA0 | `D12_c14_585_afbin2` | 0.9957418877445253 | 0.004616296933997011 | **−0.00011365209714420121** | 0.0046155444162234716 | 0.010078900722933604 |
| seedA1 | `D01_ultrawide_40mm` | 0.9978172446397623 | 0.0027618319027647997 | 0.00006495588345856174 | 0.009167831842185392 | — |
| seedA1 | `D09_c14_3800mm` | 0.9962785589845005 | 0.0026557570710346035 | 0.0020258018588109605 | 0.0027305894329926472 | — |
| seedA1 | `D12_c14_585_afbin2` | 0.9954983450392348 | 0.01081576649377558 | **0.0** | 0.01081576649377558 | — |
| seedA1 | `D16_esprit550_ha3` | 0.9968654432578308 | 0.012801231313750105 | 0.0028309196132634273 | 0.013056930407982503 | — |

- **`A-RESPONSIVE`: 0 flat, 8 responsive.** Not one of the eight has `dJ(sens) == 0.0` and `dJ(both) == 0.0`.
- **F6 diagonal-sign signature: 0 of 8.** No landing has `dJ(both)` with the opposite sign to `dJ(sens)`. Under
  design §13, [F6](followups.md) is therefore **CITED, not extended** — its diagonal-valley ablation is not
  replicated on the synthetic bank by this instrument, and this document does not claim it is.
- **Two degenerate cells, named rather than absorbed.** `seedA1/D12`'s `clip` variant is byte-identical to its
  base and its `both` variant byte-identical to its `sens` variant, because that landing's
  `StarClippingMultiplier` is **already** the shipped `2.0` — so `dJ(clip)` is **exactly 0.0 by construction**,
  not by measurement, and it is not evidence of a flat direction. `seedA0/D12`'s `dJ(clip)` is very slightly
  **negative** (−0.00011), i.e. restoring the shipped clip is a microscopic *improvement* — reported, no bar.

### 4.4 What this refutes, said plainly

**The wave was proposed on the hypothesis that the optimizer wanders down a flat direction** — that `J` is
indifferent to `BrightnessSensitivity`, so a coordinate-wise search slides to the wall and records a value that
changes nothing. Wave 23 §5.2 registered that reading as the tempting over-claim it declined to make, and named
this arm as the thing that would separate it from the alternative.

**It is refuted.** Moving sensitivity off the floor costs objective on **8 of 8** landings, and §5 shows that
forbidding the region costs `J` on **4 of 4** re-searches. **The pin is load-bearing in `J`'s terms.** "Pinned
because flat" is not what is happening.

A refuted controller hypothesis is a good outcome and it is not softened here: the arm was built so that this
answer was reachable, the answer arrived, and it is the more useful of the two — a pin that is free to remove
would have been a one-line fix, and a pin that costs something forces the harder and more interesting question,
which is **what the thing it costs is actually made of.** That is §6.

---

## §5 — `Q26-B`: forbidding the extreme costs `J` on **4 of 4**

The instrument is a **paired re-search**, not a perturbation: the same invocation, verbatim from wave 18's own
`Provenance.CommandLine`, with `--sensitivity-floor 10.0` added on the constrained side **and nothing else**. It
is the only instrument that can answer the owner's actual verb — *avoid* is a constraint, and the cost of a
constraint is what the search finds **instead**.

**Paired datasets: 4. Unpaired: none.**

| dataset | unconstrained `BestJ` (sens) | constrained `BestJ` (sens) | `dJ` | that run's own search gain |
|---|---|---|---|---|
| `D08_c11_2800mm` | 0.9963959372017032 (0.0) | 0.9962920262241155 (**11.0**) | **0.00010391097758766232** | 0.0012207384978685232 |
| `D10_rc16_3250mm_sparse` | 0.9934488063105885 (0.0) | 0.9741799265643882 (**10.0**) | **0.019268879746200285** | 0.03624753384191459 |
| `D11_rc10_585_afbin2` | 0.9961036483632415 (0.0) | 0.9953022891499348 (**10.0**) | **0.0008013592133067071** | 0.010116654140490455 |
| `D12_c14_585_afbin2` | 0.9957418877445253 (0.0) | 0.994402557449106 (**10.0**) | **0.0013393302954193276** | 0.010078900722933604 |

| count | value |
|---|---|
| **hard floor lost** under the constraint while the partner kept it | **0** |
| **equal-or-better** under the constraint | **0** |
| **costed** (`dJ > 0`) | **4** |

**The branch table, applied as written.** No validity clause failed; no constrained run lost the hard floor
(which would have been `Q-PIN-LOAD-BEARING`); no dataset was equal-or-better (which would have been
`Q-PIN-UNNECESSARY`, the branch that would most directly have served the owner). **Therefore
`Q-PIN-COSTED`** — per-dataset `dJ` printed against the named reference scale, **no bar**, and the owner decides.

Three readings that are in the numbers and are not bars:

1. **The constrained search does not merely sit on the floor.** `D08` lands at **11.0**, above the imposed 10.0 —
   the search moved *up* off its own constraint, which is what a genuine optimum above the bound looks like.
2. **The price is dataset-shaped, and `D10` is an order of magnitude above the others.** `dJ` spans
   0.0001 → 0.0193, i.e. 8.5 % of that run's own gain on `D08` against 53 % on `D10`. **A single pooled "the pin
   is worth X" number would be a fiction**, and none is offered.
3. **`Q26-B` is the one part of the owner's question that no amount of reading the landings could have answered.**
   Everything in §7 is arithmetic on files already on disk; whether the optimizer can find an equally good vector
   under a constraint requires a search, and this is it.

---

## §6 — **`J` is the wrong objective, and that was established at ZERO COMPUTE before any arm ran**

This is the wave's headline and, in this document's judgement, the run's most important goal-3 finding. It costs
no minutes and it is read out of the shipping source.

### 6.1 The mechanism, in the source

`JRun` composes the objective in two branches (`OptimizationObjective.cs:484-490`):

```csharp
if (effRecall.HasValue && effPrecision.HasValue) {
    var sLabel = LabelScore(effRecall.Value, effPrecision.Value);
    num = c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit + c.Wl * sLabel;
    den = c.Wf + c.Ws + c.Wc + c.Wl;
} else {
    num = c.Wf * sFocus + c.Ws * sStars + c.Wc * sFit;
    den = c.Wf + c.Ws + c.Wc;
}
```

`LabelScore` is `0.5·recall + 0.5·precision` (`:426-428`), weighted `Wl = 0.25` (`:33`) — **the only place
precision enters the weighted sum, and it is taken only when the run carries labels.**

**The bank runs unlabelled.** Wave 18's own aggregate summary prints it in line 4:

```
Labels: (none — unlabeled)
```

So on every optimize result in this bank, `J = (Wf·sFocus + Ws·sStars + Wc·sFit) / (Wf + Ws + Wc)`, with
`Wf = 0.55`, `Ws = 0.20`, `Wc = 0.25`. **`sStars` counts stars, not correct stars** (`SStars`, `:400`), and
nothing in the sum charges for a false positive.

**The one false-positive term that exists ships OFF.** [F23](followups.md)'s successor, `SMarginalSnr` — the
comment at `:519` calls it *"the objective's only false-positive cost"* — is a multiplicative penalty gated on
`MarginalSnrStrength`, whose shipped default is **`0.0`** (`:238`), i.e. **disabled**, with a source comment that
says so in capitals and explains why (F31 showed the metric F23 was built against could not support it). The
other multiplicative terms (`SDefocusPrecision`, `SFitGuard`, `SHfrOutlier`) return exactly `1.0` at this
baseline.

**Therefore, on this bank, `J` cannot see precision at all.** Driving `BrightnessSensitivity` to its floor admits
more detections, `sStars` rises, and **nothing charges for the ones that are wrong.** Stars are bought for free.

### 6.2 What that explains

**It is the mechanical cause of `RULE P23`'s "22 of 24 driven to the bound".** P23 established that **0** pinned
axis-instances were seeds sitting where they started and **22** were search outcomes — the optimizer *walked*
each of these axes to its wall. §6.1 says why the walk is downhill on this axis specifically: the gradient in
`sStars` points at the floor, and the term that would push back does not exist on an unlabelled run.

**And it reframes `Q26-A` and `Q26-B` without contradicting them.** The pin is load-bearing *in `J`'s terms*, and
`J`'s terms are the problem. §5's four `dJ` values are real numbers about a real objective; they are also, every
one of them, denominated in a currency that has no precision in it.

**This applies to every `optimize` result on this bank**, including wave 18's 40 landings, the owner's results
table, and every landing quoted in this series since wave 5. It does not invalidate them — they are correct
computations of `J` — but any sentence of the form *"the optimizer chose this because it is better"* means
"better in a score with no false-positive cost".

---

## §7 — The zero-compute reading: at the landing the pin is **provably inert on 8 of 8**

Read from the landings' own `ExposureRecommendation` block. **No TestApp minutes.**

| root | dataset | effective gate | `GateIsProvablyInert` | `InertGateBound` | `GateRejectedCount` | `LowSensitivityRejections`, all frames |
|---|---|---|---|---|---|---|
| seedA0 | `D08_c11_2800mm` | 0.5625 | **True** | 0.5625 | 0 | 0 |
| seedA0 | `D10_rc16_3250mm_sparse` | 2.390625 | **True** | 2.390625 | 0 | 0 |
| seedA0 | `D11_rc10_585_afbin2` | 2.3625 | **True** | 2.3625 | 0 | 0 |
| seedA0 | `D12_c14_585_afbin2` | 2.13125 | **True** | 2.13125 | 0 | 0 |
| seedA1 | `D01_ultrawide_40mm` | 0.2234375 | **True** | 0.2234375 | 0 | 0 |
| seedA1 | `D09_c14_3800mm` | 2.4890625 | **True** | 2.4890625 | 0 | 0 |
| seedA1 | `D12_c14_585_afbin2` | 1.6 | **True** | 1.6 | 0 | 0 |
| seedA1 | `D16_esprit550_ha3` | 0.196875 | **True** | 0.196875 | 0 | 0 |

`EffectiveSensitivityGate = max(Sensitivity, PeakResponse × StarClippingMultiplier)`
(`StarDetector.cs:1532-1533`, `:1552-1553`). **When `Sensitivity` is 0 the `max` is always taken by the
clip-derived term, so the gate lands exactly on `InertSensitivityBound` by construction** — and
`GateIsProvablyInert` is `True` on all eight, with **zero** gate rejections on **every frame of every run**.

**Said plainly: at the landed vector, `BrightnessSensitivity = 0` is doing no work.** The gate is being set by
`StarClippingMultiplier`. On the at-floor subpopulation the inert rate is **8 of 8 = 100 %**, which sharpens
`RULE P23`'s pooled `9 of 40` considerably: *every* landing that pins sensitivity has a sensitivity knob that
rejects nothing.

**This does not contradict §4.** `dJ(sens) > 0` is a statement about `Sensitivity = 10`, where the gate is **not**
inert and does start rejecting real detections; it says nothing about whether `0` was doing anything. The two
readings are about two different points, and pooling them would be exactly the over-claim design §11 forbids.

**And there is an F6-shaped pattern in the table, reported with no bar.** The three landings whose gate sits
below the source's *"provably inert at shipped defaults"* boundary of ~1.5 (`OptimizerVariable.cs:126-131`,
0.75 × 2.0) — `D08` at 0.5625, `D01` at 0.2234, `D16` at 0.1969 — are **exactly the three where the search also
drove `StarClippingMultiplier` down** (to 0.75, and to **0.25, the axis's own lower bound**, twice). On these
three the search walked **both** knobs of F6's diagonal toward their floors; on the other five, sensitivity 0 is
masked by a clip the search raised. **`n = 3`, reported, no bar** — but it is the cheapest available lead on which
at-floor landings are behaviourally distinct, and §8 is where it meets the precision instrument.

---

## §8 — `Q26-C`: **PREDICTION P-D08 is CONFIRMED**, at `n = 1`, and the caveats are larger than the effect

### 8.1 `Q26-C0`, the reproduction control

All four `base` cells reproduce the owner's published table exactly: `D08` 0.966 / 0.978, `D10` 1.000 / 0.965,
`D11` 1.000 / 0.892, `D12` 1.000 / 0.705 — **4 of 4**, precision and `recall@all`, against
`docs/synthetic-af-bank-results-table.md`. Every `sens` cell's `key detector knobs: Sensitivity=` line echoes
`10`, **4 of 4**.

### 8.2 The measurement

| dataset | precision base → sens | `recall@all` base → sens | `recall@high` base → sens | `FN:LowSensitivity` |
|---|---|---|---|---|
| `D08_c11_2800mm` | **0.966 → 1.000 (+0.034)** | 0.978 → 0.803 (**−0.175**) | 1.000 → 1.000 (**unchanged**) | None → **55** |
| `D10_rc16_3250mm_sparse` | 1.000 → 1.000 (+0.000) | 0.965 → 0.730 (**−0.235**) | 1.000 → 1.000 (**unchanged**) | None → **28** |
| `D11_rc10_585_afbin2` | 1.000 → 1.000 (+0.000) | 0.892 → 0.853 (**−0.039**) | 0.850 → 0.850 (**unchanged**) | None → **16** |
| `D12_c14_585_afbin2` | 1.000 → 1.000 (+0.000) | 0.705 → 0.593 (**−0.112**) | 0.720 → 0.720 (**unchanged**) | None → **37** |

**PREDICTION P-D08 was fixed before the data** (design §7.4) and it stated four things: `D08`'s precision rises
above 0.966; the other three stay at 1.000; `recall@all` falls on all four; `REJECTED:LowSensitivity` rises in the
FN attribution. **All four hold, on all four datasets.**

### 8.3 The mechanism it was predicted from, and the honest `n`

The prediction did not come from the numbers. It came from a **boundary the source draws**: of the four seedA0
at-floor gates, `D08`'s **0.5625 is the only one below the ~1.5 "provably inert at shipped defaults" bound**
(`OptimizerVariable.cs:126-131`); the other three (2.39, 2.36, 2.13) are above it. `D08` is the one landing of
the four where the combined gate is genuinely low — and it is the single precision miss in the owner's whole
20-dataset table.

**It is `n = 1`. One confirmed prediction at `n = 1` is a lead, not a law**, and this document does not upgrade
it. Three specific reasons to hold it there:

1. **The comparison population is three datasets where precision cannot rise.** "The other three stay at 1.000"
   is the prediction's weakest half: 1.000 is the ceiling. The prediction survives, but on three of four cells it
   survives by being unfalsifiable in that direction.
2. **The precision instrument has almost no dynamic range on this bank at these vectors.** 19 of the owner's 20
   datasets read exactly 1.000. **[F31](followups.md) warns about precisely this**: *"Before trusting a
   re-baseline, check that precision still SPREADS across datasets; all-1.000 means the metric is saturated
   again, not that the detector is perfect."* One dataset off the ceiling out of twenty is thin spread.
3. **`D08`'s 11 false positives sit exactly where [F31](followups.md) says the reference is incomplete.** They are
   **3 on frame `13672` and 8 on frame `14328` — the two extreme wing frames — and 0 on all seven interior
   frames.** F31's measured mechanism is that the golden tiers by native **peak-pixel** SNR and drops sub-3.5σ
   stars to `omitted`, which lands in neither `stars` nor `unresolved`, so defocus makes the reference evaporate
   toward the wings; F31 quantified it **on this very dataset** — *"`D08` holds 81 golden stars at focus and 9 at
   the extreme frame, against 123–126 truth stars per frame throughout."* The wing frames here hold **9 golden
   stars** each and the base cell accepts 10 and 15. **So the +0.034 may be the detector finding real faint
   stars the golden omits, rather than junk.** Distinguishing the two requires re-scoring against each frame's
   own `*.truth.json` — F31's own method — **which this wave did not run**.

**What survives all three caveats, and is not subject to any of them:** the FN attribution. `REJECTED:LowSensitivity`
is the *detector's own* count of candidates the sensitivity gate rejected, independent of the golden's
completeness, and it goes None → **55 / 28 / 16 / 37**. The gate at 10 demonstrably rejects, at the landing it
demonstrably does not (§7), and the recall it costs is real.

### 8.4 The sharpest thing in `Q26-C`, and nobody predicted it

**`recall@high` does not move on any of the four.** 1.000 → 1.000, 1.000 → 1.000, 0.850 → 0.850, 0.720 → 0.720 —
and `recall@high+med` is likewise unchanged on all four. **Every star the pin buys is in the faint tier.**

This is the same shape the F23 calibration table records at `OptimizationObjective.cs:180-195` (*"recall@high does
not move ANYWHERE in the whole sweep — the bright tier is never at risk from this gate"*), reproduced here on a
different population, at the landed vectors rather than a synthetic sweep. **Reported, no bar**, but it bounds
the whole argument: the sensitivity pin is a trade **entirely within the faint tier**, on both sides.

---

## §9 — What this licenses for the owner's goal 3

The owner's request was to **avoid** parameters pinned to extreme values, `sensitivity = 0` named specifically.
The measurement says the pin is **not cosmetic in `J`** — forbidding it costs measurable objective on 4 of 4 —
**but the objective it costs is one that cannot see precision** (§6), and at the landing the pinned knob rejects
nothing (§7).

Three options follow. **Each is stated with its evidence and its price. None is picked, and none has been
measured.**

**(a) Raise `DefaultSensitivityLower`** (`OptimizerVariable.cs:145`, `:150`; shipped `0.0`).
*Evidence for:* it is one line, and it is exactly the shape of "avoid this region".
*Evidence against:* `Q26-B` prices it at `dJ` 0.0001 – 0.0193 on four datasets, and
[F23](followups.md)'s wave-1 mechanism (b) measured a hard floor at 6 as **worse than doing nothing** — it broke
four healthy datasets while helping three, because *"restricting the domain does not remove the incentive to buy
star count; the search simply loosens other gates to win the stars back, admitting junk through a different
door."* §7's F6 pattern is that same escape hatch visible in the landings: on three of eight the search reached a
sub-1.5 gate **by driving `StarClippingMultiplier` down**, and a sensitivity floor does not close that route.
*Price:* one line of code, **plus a fresh 42-minute baseline** — a floor change invalidates every landing in the
bank.

**(b) Give `J` a precision term on unlabelled runs.**
*Evidence for:* §6 is the mechanism, and it is structural rather than statistical.
*Evidence against:* [F23](followups.md) is the register's own record of this being attempted and shelved, and
[F32](followups.md) is why — `J` sits at 0.98–0.999 before the search starts, so a new term competes for an
exhausted fourth decimal place. `SMarginalSnr` is already implemented, tested and flag-selectable at
`--marginal-snr-strength`, and it is **structurally escapable**: the gate statistic's own lower bound is
`PeakResponse × StarClippingMultiplier` and **both are searchable axes**, so the search can lift itself past any
fixed floor (measured: `D12` landed at 6.25 and `D15` at 6.75 against a floor of 6.0).
*Price:* this is a **coordinate-system move and it owes a fresh baseline** — [F62](followups.md)/[F59](followups.md)'s
recorded costing lesson: a change that moves the quantity every prior number is denominated in costs **42 minutes
of baseline plus the re-derivation**, not the edit.

**(c) Label the bank, so the existing `Wl · sLabel` path activates.**
*Evidence for:* it changes **no product code at all** — the branch is already there and already tested; it is the
`else` at `:488` that this bank takes. The synthetic bank is the one place in the project where per-frame truth
exists, which is exactly what the labelled branch needs.
*Evidence against:* it makes `J` depend on the golden, and §8.3 plus [F31](followups.md) say the golden's
precision is a **lower bound** that evaporates at the sweep wings. Feeding a saturating, wing-biased precision
into the objective would optimise against the metric's defect.
*Price:* **unmeasured**, and it is the arm **phase `d` is not testing** — phase `d` extends `Q26-B` to the rest of
the bank, not this. Costing it honestly needs a labelled `optimize` run, which nobody has performed.

**The one thing the wave will say as a recommendation**, and it is a recommendation and not a measurement: **(a)
alone is the option the evidence is most hostile to**, because it is the only one of the three that F23 has
already measured and rejected on this bank, and because §7 shows the escape route it does not close.

---

## §10 — `RULE L26`: **COULD-NOT-LOOK**, and that is not a failure

The question, narrow and with exactly two answers: is `lumos`'s zero-star frame a property of the **FRAME** (no
usable signal at any settings) or of the **PARAMETER VECTOR** (the optimizer's landing gates it out)? `af-fit`
was the instrument because it applies **no run detection binning** and reads the run's own detection result
rather than an optimized snapshot.

```
2026-08-13T07:09:10Z  L26_START
2026-08-13T07:09:11Z    NAMED: af-fit produced no af_fit_points.csv
2026-08-13T07:09:11Z  L26_ROWS 0
2026-08-13T07:09:11Z  L26 REFUSED: no artifact.
```

`L26-V1` requires `af_fit_points.csv` to exist and parse. **It does not exist.** The driver named the absence,
wrote **zero rows**, wrote **no `L26_DONE` marker**, and the scorer reported
`COULD-NOT-LOOK: no L26_DONE marker with a resolvable manifest= payload`.

**This is the instrument working.** A driver that had defaulted the row count to 0 and branched on it would have
returned `L-FRAME` — "zero rows with `Stars == 0`" — which is the **wrong answer produced by an absent
measurement**. The refusal is what stops that. It is the same discipline that made wave 24's `score_g24_w24.py`
refuse on a missing module ([F80](followups.md) #4), and [F75](followups.md)'s rule that the writer must be the
code that computes the verdict is why no marker exists to mislead the next reader.

**`lumos` remains unexplained after eleven waves and stays priced.** What is known: wave 24 reproduced `rc=3` and
explained it as a **hard-floor FAIL** (`bestJ = currentJ = 0`, *"at least one frame has < 3 stars under optimized
params (min observed = 0)"*); [F20](followups.md)'s wave-3 correction established that `lumos` and `SorenVance`
detect **zero stars at C0**, so their `J = 0` is *"having almost no stars at all"* and **not** the `MinHFR`
defect F20 describes. What is still open is exactly `L26`'s question. **Price: ~10 m**, and the next attempt owes
a diagnosis of why `af-fit` emitted nothing before it re-runs the same command.

---

## §11 — `Q26-D`: the labelled extension. **OPEN at the timestamp**

Started `07:10:02Z` with a self-truncating deadline of `10:10Z`. It is in **no denominator of `RULE Q26`'s
verdict** and its dataset order was fixed in advance as **ascending wave-18 optimize seconds** — a published
quantity independent of anything this wave measures — so a clock stop truncates at a point the outcome did not
choose.

At `07:11:52Z` it had begun `D17_cdk14_oiii5` and `D09_c14_3800mm`, and had correctly **named its first skip**
(*"skip `D12_c14_585_afbin2`: it is in `Q26-B`'s population"*) rather than silently omitting it — design §7.1's
Q4 construction, which lists all 20 and removes the at-floor ones from the **resolved** manifest, so no dataset
can become unreachable if the resolved at-floor set differs from the published one ([F68](followups.md)(e)'s
defect).

**No `Q26-D` number is reported in this document.** The scorer is idempotent and rewrites `--out`; the
controller appends the resolution below.

> **`Q26-D` RESOLUTION — appended by the controller after `07:11:52Z`:**
>
> _(to be filled in: datasets reached, datasets NAMED as not reached, and the labelled table. This row is
> deliberately left as written rather than back-filled into §11's prose.)_

---

## §12 — The controls on the bank, and what this wave wrote

| control | state at `07:11:52Z` |
|---|---|
| fingerprint class 1 — the 42 bank landings | **BEFORE written, 42 of 42** (`fingerprints/bank_landing_BEFORE.txt`). AFTER + diff **OPEN** |
| fingerprint class 3 — wave 18's landings | **BEFORE written, 48** across `seedA0` (20), `seedA1` (20) and `gate` (8). AFTER + diff **OPEN** |
| `--update-run-folder` | passed **nowhere**; the arm's own self-test asserts it appears on no invocation line, two-directionally |
| every `--out` | under `/mnt/d/hf_w26/`; the arm's self-test asserts no cell directory resolves inside a bank |
| markers | `W26_PREP_READY`, `Q26_A_DONE`, `Q26_B_DONE`, `Q26_C_DONE` written by their drivers **last**, only on the pre-registered minimum row count; `Q26_PASSED` written by the scorer on `rc == 0` only. **No marker was written by a human** ([F75](followups.md)) |
| the suite | **OPEN.** Expected `3973` by COUNT |

---

## §13 — Register entries this wave wrote

| entry | action |
|---|---|
| **NEW — [F83](followups.md), `J` has no precision term on an unlabelled run** | **NEW ENTRY**, with the source lines and the consequence for every optimize result on this bank. It also carries `Q26-B`'s price for the floor, the 8-of-8 provably-inert reading, `PREDICTION P-D08` with its three caveats, the three options of §9, and **discharges `RULE P23`'s owed arm** |
| **[F80](followups.md)** | **EXTENDED** with §2.1's third drift shape (demonstrated on `W25_BEFORE_READY`, wave 25's own interlock marker, and four score files), §2.2's pinned FAIL end, §2.3's sourced-dispatcher control and §2.4's whitespace-split fingerprint |
| **[F20](followups.md)** | **EXTENDED** with `RULE L26`'s could-not-look and its price — the `lumos` zero-star question is F20's correction (b), and it stays open |
| **[F32](followups.md)** | **EXTENDED** — design §13 makes this conditional on `P-D08` holding, and it held. `J`'s saturation is not only a recall problem: on an unlabelled bank `J` contains no precision term **at all** |
| **[F6](followups.md)** | **CITED, not extended.** The F6 diagonal-sign signature is **0 of 8**; the synthetic replication design §13 made conditional on it did not occur, and no extension is written |
| **[F23](followups.md)** | **CITED** as the register's existing name for the missing precision term, and as the record of a floor already measured and rejected |
| **[F31](followups.md)** | **CITED** — §8.3's caveat is F31's mechanism, on F31's own named dataset |
| **[F71](followups.md)** | **CITED and DEFENDED THREE WAYS** (design §8). Not closed: the two-copy conversion is unchanged, and `Q26-V4` is why the wave can prove the perturbation reached the live copy |
| **[F68](followups.md)** | the fifth part now has a case with **three** input copies cross-checked and two output copies; recorded inside the new entry rather than as a separate edit |
| **[F82](followups.md)** | **CHOICE PRE-REGISTERED** in design §14 — **(1), the monotone floor** — with the condition that would reverse it. **Not measured, not fixed.** |
| **`RULE P23`** | **DISCHARGED.** Note the correction in §15: **P23 has no entry in `followups.md`** — it lives only in wave 23 results §5 — so its answer is recorded in the new entry rather than by extending an entry that does not exist |
| **the `A4` truth-model gap** | **NOT TOUCHED**, deliberately: fixing it rebuilds `TestApp`, which forfeits §1's argument for skipping the gate |
| **[F81](followups.md)** | **NOT DISTURBED.** No clause here reads a `stepRecommendation` field |
| **the charter's §1c** | **CORRECTED**: `N25-E` is discharged (design §1.3, artifact `/mnt/d/hf_w25/n25e_score.txt`, published in wave 25 §16.1), and priority 1 of the backlog is struck |

---

## §14 — Open at `07:11:52Z`

| | item | why |
|---|---|---|
| 1 | **phase `d`** | running since `07:10:02Z`, deadline `10:10Z`. §11 |
| 2 | **the full suite** | plan step 10.2. Expected `3973` by COUNT. No C# changed |
| 3 | **the AFTER fingerprints and their diff** | plan step 10.3. Any difference is a **finding** — this wave writes nowhere near either population |
| 4 | **commit, push, PR #195 body, CI by COUNT** | plan step 10.4 |

---

## §15 — Three things this document had to correct against the artifacts

Recorded here rather than absorbed, because each was stated as fact in the wave's own instructions.

1. **`RULE P23` has no entry in `docs/followups.md`.** `grep -n 'P23'` over the register returns **zero
   matches**. P23 exists only as a rule in `docs/synthetic-af-bank-followups-wave23-results.md` §5 and in that
   wave's design. The instruction to *"extend P23's entry"* therefore has no target; its answer is written into
   the new entry, which cross-references wave 23 §5.2 by name. **A rule whose finding never reached the register
   is a rule the next reader cannot find** — and this is the second-order version of the same defect the register
   catalogues about interlocks and scorer output.
2. **The design's §7.3 claim that `GateIsProvablyInert`, `InertGateBound` and `GateRejectedCount` are "already in
   `aggregate_summary.json`" is right about the file and imprecise about the location.** They are **nested inside
   the `ExposureRecommendation` block**, not top-level; the top-level knobs are `SensitivityIsAtFloor` and
   `EffectiveSensitivityGate`, and `LowSensitivityRejections` is per-frame under `FrameDiagnostics`. §7's table
   reads them from the right place. It changes no verdict, and it is exactly [F68](followups.md)'s fifth part
   applied to an artifact this wave only read.
3. **There is no `/mnt/d/hf_w26/CONTROLLER_DEVIATIONS.md`.** The plan's preamble requires one — *"record the
   disagreement … as it happens, not afterwards"* — and waves 24 and 25 both have theirs on disk. Wave 26's does
   not exist. Nothing in the execution visibly owed an entry (the prep's at-floor count came back at the
   pre-registered **8**, so the plan's *"a different number is a FINDING, not a repair"* branch never fired), and
   the three instrument defects in §2 were all found at **pre-registration** time and are recorded in the design.
   **But "no deviations occurred" and "no log was kept" are not the same statement**, and only the second is
   verifiable from disk. The three defects in §2.1–§2.4 are therefore sourced from the design's §4 and the plan's
   §2, and named as such.

---

## §16 — Goal by goal, and the state of the register

### Goal 1 — coverage and the gaps

**Nothing gained.** `RULE L26` is **could-not-look**: `af-fit` produced no artifact and the instrument refused
(§10). `lumos` remains the same shape of open it has been for eleven waves, still priced at ~10 m, and the next
attempt owes a diagnosis of the missing CSV before it re-runs anything. `Panos` was not touched.

### Goal 2 — step-size accuracy

**Untouched by design.** No clause in this wave reads a `stepRecommendation` field, and **no goal-2 accuracy rate
is quoted** — wave 25 §16.1 retired `13 of 17` and nothing here replaces it. The one goal-2 act is a decision at
zero compute: **[F82](followups.md)'s fix choice is pre-registered** as (1), the monotone floor, before anyone
looks, together with the condition that would reverse it.

### Goal 3 — avoiding pinned extremes

**This is where the wave landed, and it is the most it has had in four waves.**

- The controller's flat-direction hypothesis is **refuted**: `A-RESPONSIVE`, 0 flat of 8.
- Forbidding the region is **costed**: `Q-PIN-COSTED`, 4 of 4, `dJ` 0.0001 – 0.0193 against each run's own gain,
  with **no bar** and the owner deciding.
- **The objective the price is denominated in cannot see precision at all** — source-derived, zero compute, and
  it is the mechanical cause of P23's 22-of-24 driven pins.
- At the landing the pin is **provably inert on 8 of 8** with zero gate rejections on every frame, and the three
  landings with genuinely low combined gates are exactly the three where the search also drove
  `StarClippingMultiplier` toward its floor.
- **`PREDICTION P-D08` confirmed at `n = 1`**, with three caveats larger than the effect (§8.3), and one
  unpredicted finding that bounds the whole trade: **`recall@high` does not move on any of the four** — the pin
  is a faint-tier trade on both sides.

### Is there a finding worth a register entry?

**Yes, and it is not a close call.** §6 — `J` composed without a label term on an unlabelled run, so the objective
has no false-positive cost and star count is bought for free — is a **structural fact about every optimize result
this project has produced on this bank**, established from source at zero compute, and it explains a published
finding (`RULE P23`'s 22-of-24) that had no mechanism before today. It is registered as
**[F83](followups.md)**. §2's third drift shape and the sourced-dispatcher control are two more, both
demonstrated, and both extend [F80](followups.md). **Three findings, one of them load-bearing.**

### The state of the register, honestly

**The owner has been told the remaining backlog is thin. That is broadly right, and it needs one correction and
one addition.**

**Genuinely open, with a price:**

| item | price | note |
|---|---|---|
| **[F82](followups.md)** — the floor-not-sticky fix | ~45 m code + ~10 m 3-cell re-run + **~42 m mandatory gate** | The choice is now **pre-registered** (monotone floor), so the expensive half of the honesty cost is already paid |
| **the `A4` truth-model gap** | ~20 m | *Fix the assertion, not the product.* Rebuilds `TestApp` |
| **[F73](followups.md)'s code axis** | ~53 m + a second binary | Wave 19's arm was a **null** arm |
| **`lumos`** (`RULE L26`) | ~10 m + a diagnosis | §10 |
| **F67's residual** | ~30 m + a rule | Needs the [F62](followups.md) no-`BestJ`-across-factors landmine pre-registered around |
| **NEW — the `J`-has-no-precision-term decision** | **a decision, then 42 m of baseline** | §9's three options. The measurement is done; what is missing is an owner's call |

**Blocked or fenced, and should not be re-attempted:** `RULE F14`, `RULE S16` and `RULE D20` are **permanent
fences**. [F70](followups.md)(b′) was rejected by the owner. [F45](followups.md)(b) is behind the S16 fence with
`N*` unreachable in `AutoFocusEngine`. [F59](followups.md) is rejected on the merits. The wide end of
[F25](followups.md) is **unmeasured, not untriggered**, and remains so.

**The correction:** every open item above **needs a binary**, and this wave deliberately built none. That is why
the run stops here rather than starting one at `11:00Z` — not because there is nothing left, but because nothing
left fits.

**The addition, and it is the one thing a further wave would be worth:** §9's option (b)/(c) is now a **decision
with its evidence assembled**, and the cheapest measurement that would move it is **not** another arm on `J`. It
is `Q26-C` at `n = 3` instead of `n = 1`: the seedA1 root carries two more at-floor landings with sub-1.5
combined gates — **`D01_ultrawide_40mm` at 0.2234 and `D16_esprit550_ha3` at 0.1969, both with
`StarClippingMultiplier` pinned at its own 0.25 floor** — and neither has ever been scored for precision at a
raised sensitivity. **Two `golden eval` cells, ~2 minutes, no binary, no gate.** If `D08`'s +0.034 replicates
there, the lead becomes a finding; if it does not, the co-occurrence is a coincidence and §9's option (b) loses
its only measured support. **That is the highest decision-value-per-minute item left in the register**, and it is
recorded here because this wave found it and did not have the population in scope to run it.

**Is a further wave worth anyone's time?** For the six-hour shape the charter assumes: **no** — the remaining
items are a decision, a fix with a mandatory gate, and two probes. For a **~30-minute** session: **yes, one** —
the `n = 3` replication above, which needs no binary and settles whether the wave's sharpest lead is real.
