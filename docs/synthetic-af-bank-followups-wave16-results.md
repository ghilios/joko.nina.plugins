# Synthetic AF bank — followups wave 16 (results)

Design: [`docs/synthetic-af-bank-followups-wave16-design.md`](synthetic-af-bank-followups-wave16-design.md).
Plan: [`plans/synthetic-af-bank-followups-wave16-plan.md`](../plans/synthetic-af-bank-followups-wave16-plan.md).
Wave 15: [`docs/synthetic-af-bank-followups-wave15-results.md`](synthetic-af-bank-followups-wave15-results.md).
Wave 14: [`docs/synthetic-af-bank-followups-wave14-results.md`](synthetic-af-bank-followups-wave14-results.md).
Charter: [`docs/waves16-21-handoff-prompt.md`](waves16-21-handoff-prompt.md).
Register: [`docs/followups.md`](followups.md).

> ## PROVENANCE
>
> | input | value |
> |---|---|
> | tree | branch `ghilios/synthetic-af-bank-followups-wave13`, PR #191. Pre-registration `9fc80d4`, item A `9e95446`, item B `4fd613d` |
> | binary — **FINAL, the one every arm below ran** | `D:\hf_w16\exe`. `TestApp.dll` sha256 `3fdb02c1f534e6d1c91abf1e710dcdd1f0fd463b4839d3a1974432188b94501e` · `NINA.Joko.Plugins.HocusFocus.dll` sha256 `bc8f54b995ebf5d8fa564f45f9c04de66b4da0841aa8f0f189f40cb41bb40b22` · `BuildId` **`10bc1b47a5414645bd783af067ef3734`**. **A SEVENTH binary** |
> | binary — **BUILD 1, superseded, and it is a finding** | `D:\hf_w16\exe_build1_superseded`. `TestApp.dll` sha256 `6c4c0049d9a4b01c73dbdae0d5a2825d0cf571eae85d249f0ecb454d51abac36` · `NINA.Joko.Plugins.HocusFocus.dll` sha256 `f1b1d2881d319191c855a92db09683eb8f85a33b76a786bd1b027924f946a93b`. **RULE G16 passed on it too**, 8 of 8 bit-identical, and **no arm used it**. §1.1 |
> | detector | `DetectorVersion` **2**, read as a **FIELD** on all 8 gate landings. No `strings` probe is quoted as a detector check ([F66](followups.md)) |
> | settings **S0** | `D:\hf_w11\pinned_settings_w11.json` md5 **`a67ffc06164c81613aef5c4f8324b9b8`** — `MaxOutlierRejections` explicitly **0**. The only settings file this wave used |
> | profile | `astrodet` `ce3f3e63-8fd3-4b72-a0ca-d90db9441382`, pinned on **every** arm; one distinct value across all 8 gate landings |
> | banks | `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = **truth**), `D:\Autofocus Bank` (19 runs) |
> | F15 control | **42 of 42 bank landings BYTE-IDENTICAL**, re-verified at write-up time. 0 changed, 0 added, 0 removed, 0 could-not-look. `--update-run-folder` passed **nowhere** |
> | suite | **3824** (3781 baseline + 28 item A + 15 item B), verified by COUNT. §5.1 |
>
> **TWO BINARIES, AND THE DESIGN FORBADE THAT IN ITS OWN WORDS.** §7 fixed the order as *code first → build ONE
> binary → gate → probe → rungs* and said a second build would be *"a finding … disclosed in the results doc in
> wave 14's own words — it is not quietly done."* A second build happened, for the reason recorded in §1.1, and
> the repair chosen was to **re-run the gate on the final binary** rather than to ship an ungated arm. The gate
> passed on both. **Every arm below ran the final binary and only the final binary.**
>
> **No fan-out.** Every arm is `--profile-id`-pinned and a pinned arm cannot fan out at all (wave 11's K3); the
> authorisation is worth 1.33×, not 4× ([F60](followups.md)). Wave 5's φ table is quoted nowhere.
>
> **Every number in this document was re-derived from the artifacts at write-up time**, by re-running each scorer
> and diffing its output against the committed one. `score_sem_w16.py` and `score_params_w16.py` reproduce
> byte-identically. Re-running `score_sem_w16.py --w1` idempotently rewrites the 5-byte `W1_PASSED` marker with
> the same `PASS`; no arm directory was touched.

## Status of this document

| item | state |
|---|---|
| **RULE G16** — the gate | **PASS, 8 of 8 to 6 dp, bit-identical to all sixteen digits, on a SEVENTH binary** — and again on the superseded build 1. §1 |
| **item A** — F45(b) in SEM units, six rungs | **RULE S16: outcome 4, NO RECOMMENDATION, MECHANISM RECORDED.** All five validity gates PASS. The decision clauses split. **No rung is named a winner.** §2 |
| **S16-A(b)** | **FAILS on an ill-posed bar**, and this is the wave's sharpest finding: 12 of 12 against a threshold of ≥ 30 of 33, because the clause reads a population whose maximum attainable value is **12**. §2.4 |
| **S16-D at `V0.07`** | **CONTAINS-SELECTIVELY** — the branch the design called *plausible, not proved*, and foreclosed everywhere else. §2.5 |
| **family R** | **R-REDIRECTS on 12 of 39 runs**, fails S16-B, and produces the wave's only out-of-sample movement above the materiality floor. **Excluded from V4′ and from RECOMMEND before any of it was measured.** §2.6 |
| **item B** — F67, both sides' params through one formatter | **RULE P16: P-b.** 53 of 55 fields identical on all five datasets covering both population halves; the two that differ are inert by proof. **The disagreement is downstream of the params**, and F67 is recorded as a **PRODUCT** finding. §3 |
| **P16-INSTRUMENT-vs-PRODUCT** | **ILL-POSED ON THE BRANCH THAT OCCURRED** — it quantifies over a set that P-b makes empty, so it is vacuously true and contradicts its own rule's P-b row. Reported, not repaired. §3.4 |
| **item C** — the nine UI changes A1–A9 | **NOT ATTEMPTABLE. `Disc`, seventh consecutive wave.** §4 |

---

## §1 — RULE G16: the coordinate system on a seventh binary

`/mnt/d/hf_w16/gate_w16.sh`, **14:41:15Z–15:22:10Z = 40 m 55 s**, eight runs sequential,
`optimize --per-run --max-evals 250`, `--settings D:\hf_w11\pinned_settings_w11.json` **and**
`--profile-id ce3f3e63-…` both named in the script header, machine quiet, one `TestApp.exe`. Population asserted
**before** scoring: `aggregate_summary.json produced: 8   expected: 8`.

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

| clause | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **G16-1** | all eight `BestJ` reproduce to **6 dp**; a partial reproduction is a FAILURE and stops the wave | **8 of 8**, and all eight bit-identical to sixteen digits | **PASS** |
| **G16-2** | `aggregate_summary.json produced: 8, expected: 8` | 8 of 8 | **PASS** |
| **G16-3** `BuildId` | exactly one distinct value, **novel** against waves 11–15 | `10bc1b47a5414645bd783af067ef3734` ×8, novel | **PASS** |
| **G16-3** `DetectorVersion` | **2** on all eight, read as the FIELD | 2 ×8 | **PASS** |
| **G16-3** `ProfileId` | contains `ce3f3e63-…` on all eight **and** exactly one distinct value | containment ×8, cardinality 1 | **PASS** |
| **G16-3** `FitInputs` | one distinct value, `MaxOutlierRejections=0;OutlierRejectionConfidence=0.95;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid` | one value, exact | **PASS** |
| **G16-3** `ConcurrencyCheck` | `exclusive` on **all eight**, read across the arm | exclusive ×8 | **PASS** |
| **G16-3** `BaselineJ` | reproduces wave 11 ([F41](followups.md)) | 8 of 8 | **PASS** |
| **G16-4** | the scorer PASSES on the real arm and FAILS on a mutated copy, with the mutation asserted | PASS / FAIL on **both** clauses | **PASS** |

> **Wave 11's eight values have now reproduced across SEVEN binaries and two settings files**, and this wave adds
> a second observation the others could not make: they reproduce across **two builds of the same wave**, one of
> which added a diagnostic printer to `optimize`. §1.1.

### §1.1 The controller error, its price, and the repair — recorded before its consequences

The design's §7 fixed the step order as **code first → build ONE binary → gate → probe → rungs**, and said why:
wave 14 needed two binaries and had to disclose that RULE G14 was *not* a control on the binary its second item
ran. Wave 16 was written specifically not to repeat that.

**The controller briefed a code agent for item A only, built the binary, and ran the gate — and item B also ships
code.** `PARAMS-DUMP` must be emitted from *both* runners, so item B's commit changes `TestApp`, so the 41 m 36 s
gate that had just passed was a control on a binary no arm would use.

**The repair: re-run the gate on the final binary rather than disclose an ungated arm.** Build 1 was moved aside
to `exe_build1_superseded/` with its log, item B was committed, one final binary was built, and the gate ran again
— 40 m 55 s.

| gate run | binary | window | duration | result |
|---|---|---|---|---|
| build 1, **superseded** | `6c4c0049…` / `f1b1d288…` | 13:28:25Z–14:10:01Z | **41 m 36 s** | **PASS, 8 of 8, bit-identical** |
| **final, the one that counts** | `3fdb02c1…` / `bc8f54b9…` | 14:41:15Z–15:22:10Z | **40 m 55 s** | **PASS, 8 of 8, bit-identical** |

> **The gate passed on both builds, and that is a real observation rather than a formality.** Adding a reflective
> parameter printer to `optimize` moved **none** of the eight `BestJ` values — not at 6 dp and not at the
> sixteenth digit. That is exactly the claim the re-run existed to test, and the only reason it could be tested is
> that the repair chosen was the expensive one. **Price: 42 minutes**, inside the 6 h ceiling, and paid by the
> controller not reading its own plan's step order before spawning.
>
> *The first gate is not discarded. It is reported as what it is: a passing control on a superseded binary.*

**The flag-presence probe, with the correct instrument.** `strings -el` (UTF-16, the `#US` heap) on the final
`TestApp.dll` finds `--sem-veto` **2**, `--sem-rank` **2**, `--mad-floor` **2** as exact lines. Plain `strings`
finds **0 of each**. Re-verified at write-up time. This is F66's two-heaps lesson applied as a habit, and it is
quoted here as a *flag-presence* probe only — never as a detector-version check.

### §1.2 The F15 control

All **42** bank `optimized_settings.json` were fingerprinted by sha256 + size + mtime **before** the first arm and
re-checked after every stage and again at write-up:

> **42 of 42 BYTE-IDENTICAL. 0 changed, 0 added, 0 removed, 0 could-not-look.**

Third consecutive clean wave. `--update-run-folder` is passed nowhere in this wave.

Reproduce: `python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w16/gate --rule G16`;
`python3 /mnt/d/hf_w16/prov_w16.py --self-test /mnt/d/hf_w16/gate`;
`python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w16/bank_landing_fingerprint_BEFORE.json`.
All three take **WSL** paths; a Windows path yields eight `UNEVALUATED` and looks exactly like a failed arm.

---

## §2 — Item A: RULE S16, the SEM-unit ladder

### §2.1 What shipped, and the control that measures whether it is inert

`SemScaleSpec` (`9e95446`), modelled line-for-line on `MadFloorSpec`: `default` is `None`, private ctor, `Veto(t)`
and `Rank()` the only factories, so *"veto AND rank"* is unrepresentable. Threaded
`SelectBestModel → FitWithOutlierRejection → RejectionTest`, with `N*` carried as a `Func<double,double>` side map
keyed by `p.X` — the shape `BuildResidualWeights` already uses, and deliberately **not** `ScatterErrorPoint.Tag`,
because `WeightRegularization` rebuilds every point and forwarding `Tag` means editing a production class on the
production path. **28 new tests.**

Two findings from the code work, carried because they are about method rather than about this feature:

1. **The plan's mutant M1 is not killable.** Moving the veto check above the Grubbs comparison left all 28 tests
   passing, because both branches `return null` and the reordering is behaviour-preserving. The killable
   restatement — *"the SEM comparison returns a point"* — was written and verified, and the code comments
   repeating the plan's claim were corrected. **A mutant that does not bite is a claim nobody checked**, which is
   [F66](followups.md)(a)'s shape moved from checks to tests.
2. **`SelectBestModel` needed a guard beyond the plan, and it is kept.** Pass 1 wraps each candidate in
   `catch (Exception)`, so a throw from `RejectionTest` alone would be swallowed into *"no model solved"* → four
   empty budget rows that a scorer reads as **"the criterion fired on nothing."** An exception that becomes a
   clean null is worse than a crash.

**The exclusion of family R from V4′ and from RECOMMEND was re-verified by mutation, not asserted.** Making the R
rescale uniform (`Math.Sqrt(100.0)` instead of `Math.Sqrt(N*)`) fails **3 of 28** tests — so R's ability to move
the argmax is demonstrated. *The first attempt at that verification produced no test output at all and was re-run;
an empty result is not a passing one.*

### §2.2 The validity gates, each threshold beside its measurement

| gate | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **W1** — the control rung | rung `N`'s **39** `af_fit_summary.txt` **byte-identical** to wave 14's `affit_A0.00`, **and** 0 field differences over 39 × 4 × 7 | **39 of 39 byte-identical**, **0** field differences; 39 readable on both sides | **PASS** |
| **W2** — population by count | per rung: 20 syn + 19 real = **39**, each with `run.log`, `af_fit_summary.txt` and an `af_fit_points.csv` carrying a `Stars` column | **39 of 39 on all six rungs**; unreadable **0**; without `N*` **0** | **PASS** |
| **W3** — `N*` reached the test | printed `s` == \|printed `r`\| · √(CSV `Stars` at that position) to **1e-6** relative, **and** printed `N*` == the CSV's, on every round of every non-`N` rung | mismatches **0**, could-not-look **0**; rounds tied 39 / 37 / 28 / 16 / 60 at `V0.07` / `V1.00` / `V2.00` / `V4.00` / `R` | **PASS** |
| **V4′** — prospective | **468** triples (4 V-rungs × 39 × 3), **0** violations, **156** budget-0 rows empty both sides; NOVEL-CONSENSUS reported never barred | **468 / 0 / 156**, NOVEL-CONSENSUS **0** | **PASS** |
| **V4′-CONFORM** | reproduce wave 15's published **585 / 0 / 2 / 195** on wave 14's rungs | **585 / 0 / 2 / 195** | **PASS** |

> **W1 is the only clause in the wave that can catch a `SemScaleSpec` that is not inert at its default, and it is a
> BYTE comparison.** The gate structurally cannot do this job: it runs at `MaxOutlierRejections = 0`, where
> `RejectionTest` is never called at all. W1 is measured **where the rejection actually executes**, against wave
> 14's control rung, and it is stronger than wave 14's own field-level V1 — 39 of 39 files identical to the byte,
> not 39 × 4 × 7 fields agreeing. *The pre-registered tie-break (bytes differ, fields match ⇒ decide on fields and
> report the byte difference by name) was never needed.*
>
> **The W1 interlock is mechanical and it fired.** `affit_w16.sh:125-129` refuses to run any rung but `N` unless
> `/mnt/d/hf_w16/W1_PASSED` exists, and only `score_sem_w16.py`'s W1 PASS writes it. Before rung `N` was scored,
> **all five treatment rungs aborted in about a second each.** A ladder whose control has not been scored has no
> reference, and the driver is what enforces that rather than the operator's memory.
>
> **V4′ reports 0 NOVEL-CONSENSUS on wave 16's family-V rungs against 2 on wave 14's.** [F65](followups.md) is
> reported, never a bar, so this changes nothing — but it is the expected direction: a *veto* truncates each
> model's rounds as a prefix, whereas wave 14's MAD floor changed the scale every model saw.

### §2.3 The decision clauses, each threshold beside its measurement

| clause | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **S16-A(a)** | at `V1.00`, rejections surviving on runs with control-rung `ρ > 2` **== 0** | **0 of 2** survive. `[n = 1 run by construction: caboose]` | **PASS** |
| **S16-A(b)** | at `V1.00`, rejections surviving on runs with control-rung `ρ ≤ 1.10` **≥ 30 of 33** | **12 of 12** survive | **FAIL — and the bar is ill-posed.** §2.4 |
| | `Panos_attempt01` **UNEVALUATED BY NAME** (degenerate σ ⇒ ρ = NaN) | named, excluded from both buckets | as written |
| **S16-B** `V0.07` | over 39 runs, `min ΔR² ≥ −0.005` **and** `max κ ≤ 3.0` | min ΔR² **−0.000053** (`D17`), max κ **1.246** (`D17`) | **PASS** |
| **S16-B** `V1.00` | same | **−0.000053** / **1.246** (`D17`) | **PASS** |
| **S16-B** `V2.00` | same | **−0.000053** / **1.246** (`D17`) | **PASS** |
| **S16-B** `V4.00` | same | **+0.000000** / **1.000** (`CWhiteFocus`) | **PASS** |
| **S16-B** `R` | same | min ΔR² **−0.085240** (`caboose`), max κ **1.243e+04** (`caboose`) | **FAIL** |
| **S16-B** `N` *(control, never a rung under test)* | same | **−0.085240** / **1.243e+04** (`caboose`) | *fails — which is how the clause was shown to discriminate* |
| **S16-C** | `N_fire(budget 1) ≥ 1` **and** `N_fire(budget 3) ≥ 1` over 39 runs | `V0.07` 7/13 · `V1.00` 7/12 · `V2.00` 4/7 · `V4.00` 4/4 · `R` 9/15 · *(`N` 7/13)* | **PASS at every rung** |
| **S16-D** | three-valued on `caboose` at budget 3 | `V0.07` **CONTAINS-SELECTIVELY** · `V1.00` / `V2.00` / `V4.00` **CONTAINS-BLUNTLY** · `R` **DOES NOT CONTAIN** *(as does `N`)* | all three values observed |
| **S16-E(i)** veto | median \|Δe\| **≥ 0.10 step** vetoes the rung | median \|Δe\| **0.00000 step at every rung**, `R` included | **no veto anywhere** |
| **S16-E(ii)** magnitude | per dataset, with sign, no ratio column | §2.7 | reported |
| **S16-E(iii)** alarm | any `e ≥ 1.0 step` anywhere is a new register finding | max `e` anywhere **0.03000 step** (`D17_cdk14_oiii5` @ b1) on every V rung; **0.13150 step** (`D13_apo200_1800mm` @ b3) under `R` | **did not fire**, 33× under the alarm on V and 7.6× under it on `R` |
| **branch 5** | R-INERT / R-SUPPRESSES / **R-REDIRECTS** / R-UNEVALUATED | identical **21**, strict-subset **6**, **REDIRECTS 12** | **R-REDIRECTS** |

**σ_focus and ρ are reported only and are never a bar**, per the rule and per [F62](followups.md): σ_focus is
anti-informative when a rejection is what changed it, and it inflates mechanically as `n − p` shrinks.

> **S16-B deliberately uses statistics with no `n − p` term, against a reference the treatment cannot move.** `ΔR²`
> and `κ` are taken against **each run's own budget-0 row at rung `N`** — budget 0 never calls `RejectionTest`, so
> no rung can move the denominator. σ_focus would have been the natural choice and is the wrong one twice over:
> it grows mechanically as the surviving point count approaches the parameter count (`caboose` at budget 3 is 5
> points against 4 parameters), and F62 measured it improving by up to 88 % while the distance to a known truth
> improved on none. **R² and reduced χ² carry no `n − p` term: they say whether the refit fails on the points it
> KEPT**, which is the only thing the containment question is asking.

**The branch table, applied as written.** Outcome 2 requires S16-A(a) **and** S16-A(b) **and** S16-B **and**
S16-C **and** S16-D ∈ {SELECTIVELY, BLUNTLY} **and** no veto **and** no alarm, all at `V1.00`. **S16-A(b) fails**,
so outcome 2 is not reached. Outcome 3 requires S16-C to fail at every rung passing S16-B (it passes at all five)
or S16-D to be DOES NOT CONTAIN at every rung (it is SELECTIVELY at `V0.07`), so outcome 3 is not reached either.

> ### **RULE S16 returns outcome 4: NO RECOMMENDATION, MECHANISM RECORDED.** The clauses split; the split is the
> result. **This is a terminal state, not a failure, and no rung is named as a winner.**
>
> **RULE F14 is closed and nothing above re-opens it.** No rung of wave 14's MAD-floor ladder is named here as a
> baseline, a comparison or a support. Wave 14's `affit_A0.00` appears in exactly one place — as W1's **byte
> reference** — and carries no verdict from wave 14 with it.

### §2.4 S16-A(b) is a badly-formed bar, and it is the wave's sharpest finding

```
S16-A(a) rho > 2.0 : 0 of 2 rejections SURVIVE at V1.00 (threshold: 0)        -> PASS
S16-A(b) rho <= 1.1: 12 of 12 rejections SURVIVE at V1.00 (threshold: >= 30)  -> FAIL
```

**12 of 12 is 100 % on the property the clause was written to express, and it fails a bar of "≥ 30" because the
denominator turned out to be 12, not 33.** The clause asked: *does the criterion leave the benign rejections
alone?* Measured as a rate, the bar demands **30/33 = 90.9 %** and the criterion delivers **12/12 = 100 %**. It
fails on the absolute count and only on the absolute count.

**Why the two populations differ, exactly.** They are two different views of the same 39 runs, and the difference
is not subtle once named:

| | wave 14's SEM audit (`/mnt/d/hf_w14/stageA/sem_audit.tsv`) | wave 16's S16-A (`score_sem_w16.py:461-492`) |
|---|---|---|
| what a row is | one **round** of the **single winning model's** printed Grubbs trace that ended in a rejection | one position in the **budget-3 row of the production budget table** |
| what that set is | one model's greedy rejection sequence, run to the maximum budget | the **consensus** — the intersection of what all four Hybrid candidate models reject |
| total over 39 runs | **42**, across 20 runs | **18**, across 13 runs |
| in the `ρ ≤ 1.10` bucket | **33** | **12** |
| `ρ > 2` bucket (`caboose`) | **3** | **2** |
| `ρ` in (1.10, 2] (`D12`, `D17`) | 3 | 2 |
| `Panos_attempt01`, `ρ` = NaN | 3, unevaluated by name | 2, unevaluated by name |

**Every one of the 18 consensus rejections is in wave 14's 42 — the consensus is a strict subset — and the other
24 are rejections PRODUCTION NEVER PERFORMS.** One model wants them; the intersection does not. `CWhiteFocus` is
the clean case: its traced winner `Symmetric` rejects 20350, 20650 and 20800 in its own loop, while
`TiltedHyperbola` on the same data rejects nothing at all, so the consensus is `(none)` at **every** budget and
the production budget table reads `0` rejections on all four rows. Wave 14's audit counted three.

And the 24 phantoms are concentrated at the **top** of the s-distribution — 79.7824 (`D01`), 47.5143 / 25.6755 /
16.3351 (`CWhiteFocus`), 24.2482 / 8.6853 (`timmer_5`), 19.0993 / 18.2715 (`mccomiskey`). The largest s values in
the ladder's own design table describe rejections the product does not make.

**Now the thing that has to be said plainly.** The design's **§4.1 is the section written to compute every
clause's maximum attainable value before the data**, and its table reads:

| clause | max attainable | attainable? | failable? |
|---|---|---|---|
| S16-A(b) | **33 of 33** | yes | yes (an aggressive rung drops below 30) |

**The maximum attainable value of S16-A(b) on the instrument the clause actually reads is 12.** The bar of 30 was
**unsatisfiable before the wave started**, by a margin of 18, and §4.1 certified it as attainable by carrying a
denominator over from a *different measurement of the same runs* without checking that it transfers.

It is worse than an oversight, in a specific and instructive way: **the pre-registration named the population
change in the line immediately above the threshold.** `score_sem_w16.py` — the pre-registration in executable
form, written at 12:38Z, an hour before the first arm — has this in its own header:

```
S16-A  SEPARATION, on a NEW population (all four candidate models' consensus, not one printed trace).
       (a) at V1.00, rejections surviving on runs with control-rung rho > 2  ==  0     [n = 1: caboose]
       (b) at V1.00, rejections surviving on runs with control-rung rho <= 1.10  >=  30 of 33
```

and [F65](followups.md) — the register entry that exists precisely because the consensus is an intersection over
four models that need not agree — is cited **four times** elsewhere in the same design. The population change was
known, written down, and given its own register entry, and the absolute count was still carried across it.

**The same error is harmless in (a) and fatal in (b), and the asymmetry is the lesson.** S16-A(a) is a `== 0`
bar: shrinking the denominator from 3 to 2 cannot break a clause that demands *nothing survives*. S16-A(b) is a
`≥ 30` bar: shrinking the denominator from 33 to 12 makes the clause unreachable. **A bar stated as a count is
hostage to a denominator; a bar stated as a rate, or as a maximum, is not.** §4.1 checked the *value* the clause
could reach and never checked the *population* it would be computed over.

> **THE RULE IS APPLIED AS WRITTEN AND S16-A(b) STANDS AT FAIL.** It is not re-scored on a rate, it is not
> re-scored on the trace population, and no repaired form of it is evaluated anywhere in this document. *An
> unsatisfiable clause is a FINDING, not a licence to re-decide one* (wave 14 Lesson 3), and the finding is
> entered in the register as [F68](followups.md). Re-deciding it after the data is worth **zero minutes** of
> compute and would cost the register the only thing that makes its verdicts mean anything.

**This is the third consecutive wave whose satisfiability analysis missed a defect of its own class.**

| wave | the section's job | the defect it contained |
|---|---|---|
| 14 | check every clause can fire | **C4** was a decisive clause that arithmetic had already foreclosed — retained as decisive, and the ladder's out-of-sample arbiter could not decide anything |
| 15 | check every clause's PASS value is attainable | **G-d** could never observe *"converted files identical"*, because every landing carries `CreatedAtUtc` — the branch was unreachable, and the section written to catch unreachable branches contained one |
| **16** | check every clause's **maximum attainable value**, with the arithmetic, before the data | **S16-A(b)**'s maximum attainable value was computed on a population the clause does not read — the section written to compute maxima computed the wrong one |

Wave 16 contains **two more instances of the same omission in different shapes**: S16-E(i)'s veto was declared
*live for `R`* without computing that a median over 20 datasets needs 11 movers and R produced 6 (§2.7), and
`P16-INSTRUMENT-vs-PRODUCT` quantifies over a set that is **empty** on the branch that occurred, making it
vacuously true and putting it in direct contradiction with its own rule's P-b row (§3.4). **Three in one wave, in
three shapes: a denominator, an aggregation, and an empty domain.**

Wave 15's Lesson 4 was *"ask of every clause: what input makes this return each of its outcomes?"* — and wave 16
asked it, in §4.3, for every clause including S16-A. §4.3's answer for S16-A reads *"3 constructed populations"*,
which is true, and constructed populations cannot detect a denominator error, because the constructor chooses the
denominator. **The question that was never asked is the one added to the register as F68: *over what population is
this number computed, and is that the population the threshold was derived from?***

Reproduce: `python3 /mnt/d/hf_w16/score_sem_w16.py --root /mnt/d/hf_w16 --w13 /mnt/d/hf_w13 --w14root
/mnt/d/hf_w14 --out /mnt/d/hf_w16/s16_score.txt`; the two populations are `/mnt/d/hf_w14/stageA/sem_audit.tsv`
(42 rows) and the budget-3 rows of `/mnt/d/hf_w16/affit_N/*/*/af_fit_summary.txt` (18 positions).

### §2.5 S16-D fired the branch the design expected to be foreclosed

`V0.07` returns **CONTAINS-SELECTIVELY**. `caboose` at budget 3:

| rung | winner | #rej | rejected | σ_focus | redχ² | R² | minPos |
|---|---|---|---|---|---|---|---|
| `N` budget 0 *(the S16-B reference)* | TiltedHyperbola | 0 | — | 0.364344 | 0.000317494 | 0.999989 | 7101.58 |
| `N` budget 2 | TiltedHyperbola | 1 | `7275` | 0.17244 | 7.09138E-05 | **0.999999** | 7101.60 |
| `N` budget 3 *(the catastrophe)* | **Symmetric** | 2 | `7275 6975` | 16.4599 | 3.94645 | **0.914749** | 7112.74 |
| **`V0.07` budget 3** | **TiltedHyperbola** | **1** | **`7275`** | **0.17244** | **7.09138E-05** | **0.999999** | **7101.60** |
| `V1.00` / `V2.00` / `V4.00` budget 3 | TiltedHyperbola | 0 | — | 0.364344 | 0.000317494 | 0.999989 | 7101.58 |
| `R` budget 3 | Symmetric | 2 | `7275 6975` | 16.4599 | 3.94645 | 0.914749 | 7112.74 |

`V0.07`'s budget-3 row is **`N`'s budget-2 row, exactly** — the damaging rejection of `6975` (s = 0.0403) is gone
and the benign rejection of `7275` (s = 0.1010) is kept, which is the definition of CONTAINS-SELECTIVELY. Its κ
against `caboose`'s own budget-0 redχ² is **0.223** — the fit at budget 3 is four times *better* than the
unrejected reference.

**The pre-registered reservation was right to be stated, and the outcome exceeded it.** §4.1 derived the window
`[0.0403, 0.1010)` from `caboose`'s **winning-model trace** and then said so out loud:

> *"reachability is plausible, not proved: the window is derived from the winning model's trace, while the
> consensus is an intersection over four models that need not agree on round order (F65)."*

That reservation is the correctly-stated form of the very mistake §2.4 records — the design knew the trace and
the consensus are different instruments and said so *here*, and forgot it *there*. **It fired.** The four models
do agree closely enough on `caboose` for a prefix truncation at 0.07 to survive the intersection, and no MAD floor
could reach this outcome at all: wave 14 measured **every** floor rung that repaired budget 3 as also suppressing
budget 2's benign rejection.

> **`V0.07` IS A CABOOSE-DERIVED PROBE RUNG, `n = 1` BY CONSTRUCTION, AND OUTCOME 2 WAS DEFINED ON `V1.00` ALONE
> SO THAT IT COULD NEVER BECOME A RECOMMENDATION.** Its threshold was read off one run's own two s-values. It
> answers a **mechanism** question — *is selectivity attainable by ANY threshold in this family?* — and the answer
> is **yes, on one run**. That is the whole claim. It is not a recommendation, it is not a candidate default, and
> the fact that it is the only rung producing the outcome the criterion exists for does not promote it.

### §2.6 Family R redirects, and the exclusion was earned before it was measured

**R-REDIRECTS on 12 of 39 runs** — identical to rung `N` on 21, a strict subset on 6, and on 12 it rejects
positions rung `N` never rejected at **any** budget:

| run | positions `R` rejects that rung `N` never rejects at any budget |
|---|---|
| `CWhiteFocus_AutoFocus_20220429_000244_attempt01` | 20350, 20650 |
| `D01_ultrawide_40mm` | 5973, 6027 |
| `D02_rich_135mm` | 5982, 5994, 6018 |
| `D03_redcat_250mm` | 6952, 7016, 7048 |
| `D08_c11_2800mm` | 14000 |
| `D09_c14_3800mm` | 18000 |
| `D13_apo200_1800mm` | 12127 |
| `D14_cdk14_2563mm_e47` | 12000, 12120 |
| `D15_cdk20_3454mm_e47` | 14000, 14128 |
| `SorenVance_AutoFocus_20260711_014141_attempt01` | 28695, 28895, 29295 |
| `mufti_AutoFocus_20260607_030642-…_attempt01` | 2825 |
| `toml999_attempt01` | 4153 |

**R rejects MORE, not less.** Consensus rejections at budget 3: rung `N` **18**, `R` **30** — a 67 % increase from
a criterion built out of a measurement whose content was *"these rejections are too small to be real."* On
`CWhiteFocus` the mechanism is visible: at rung `N` the four models disagree so completely that the intersection
is empty at every budget, and under `R` the rescaling brings them into agreement on 20350 and 20650. **A
re-ranking does not merely reorder one model's queue; it can manufacture a consensus where there was none.**

**And R is not contained.** `S16-B [R]`: min ΔR² **−0.085240**, max κ **1.243e+04**, both on `caboose`, both
**identical to the control rung's own failing values** — R leaves `caboose`'s budget-3 catastrophe exactly as it
found it (S16-D: **DOES NOT CONTAIN**). Against the pre-registered bars of `ΔR² ≥ −0.005` and `κ ≤ 3.0`, R fails
by 17× and by four orders of magnitude.

**The largest out-of-sample movement in the whole series comes from R**, and it comes with a warning attached:

| | rung `N` | rung `R` |
|---|---|---|
| `D13_apo200_1800mm`, budget 3 | Symmetric, 0 rejections, R² **0.998952**, minPos **11999.9** | UnevenBlend, rejects `12127`, R² **0.999834**, minPos **11983.3** |
| `e` = \|minPos − truth\| / step (truth 12000, step 127) | **0.00079** | **0.13150** |

> **R's redirect on `D13` improves R² by 0.000882 and lands the vertex 16.7 focuser counts from a truth it was
> 0.1 counts from — Δe = +0.13071 step, the only movement anywhere in this wave above the 0.10-step materiality
> floor.** This is [F62](followups.md)'s claim reproduced on a mechanism F62 never touched: the fit-quality
> statistic improved, the answer got worse, and nothing in the artifact says which one to believe.

> **THE PRE-REGISTRATION EXCLUDED R FROM V4′ AND FROM THE RECOMMEND BRANCH BEFORE ANY OF THIS WAS MEASURED**, on
> a proof argument alone: wave 14 measured `s` at the point the *current* rule selects, a re-rank changes which
> point is selected, so the measurement stops describing it and the prefix/subset lemma V4′ tests does not hold.
> Every number above vindicates that call — R redirects on 12 runs, fails containment by four orders of
> magnitude, and owns the only material out-of-sample movement in the series. **Had R been gated on V4′ it would
> have been failed by a lemma that was never proved for it, which is wave 14's V4 exactly**, and the wave would
> have recorded a gate failure instead of the mechanism finding that R-REDIRECTS actually is.
>
> *Refusing to gate R is not softening the rule. Gating a mechanism on a lemma proved for a different one is.*

**R-REDIRECTS is the wave's only genuinely unpredictable measurement**, named as such in the design's §9, and it
is the one that decides whether a future wave pursues re-ranking. **It should not.** The mechanism is real, it is
not contained, and its single largest effect on a bank with ground truth is in the wrong direction.

### §2.7 Out of sample: the movements that go the right way, and a veto that never fires

`e = |minPos − truth| / step` over the 20 synthetic datasets, `truth = renderRequest.OptimalFocuserPosition`,
read from wave 13's own `affit_syn_score.txt` rather than re-derived. **Magnitudes with signs; no ratio column**
(wave 15's Lesson 5).

**Family V.** Nothing moves at `V0.07` or `V1.00`. At `V2.00` and `V4.00`, two datasets move and both move
**toward** truth:

| dataset | truth | step | `e` at rung `N` | `e` at `V2.00` / `V4.00` | Δe | direction |
|---|---|---|---|---|---|---|
| `D08_c11_2800mm` | 14000 | 82 | 0.01951 | **0.01829** | **−0.00122** | **toward truth** |
| `D12_c14_585_afbin2` | 20000 | 141 | 0.01135 | **0.00142** | **−0.00993** | **toward truth** |

Both are the pre-registered maximum: §4.1 computed `D12`'s reachable spread as **0.00993** and `D08`'s as
**0.00122** from wave 14's data, and both were attained exactly. `D12` is the largest movement family V can make
anywhere on this bank, and it is 10.1× under the materiality floor.

**Family R**, six datasets move — five toward and one far away:

| dataset | `e` at `N` | `e` at `R` | Δe | direction |
|---|---|---|---|---|
| `D08_c11_2800mm` | 0.01951 | 0.01707 | −0.00244 | toward truth |
| `D09_c14_3800mm` | 0.02797 | 0.02458 | −0.00339 | toward truth |
| `D12_c14_585_afbin2` | 0.01135 | 0.00142 | −0.00993 | toward truth |
| **`D13_apo200_1800mm`** | **0.00079** | **0.13150** | **+0.13071** | **away from truth** |
| `D14_cdk14_2563mm_e47` | 0.01333 | 0.00167 | −0.01167 | toward truth |
| `D15_cdk20_3454mm_e47` | 0.01406 | 0.00781 | −0.00625 | toward truth |

The five improvements sum to **0.03368 step**. The single regression is **0.13071 step** — **3.9× the sum of
everything R gets right.**

**S16-E's veto never fires and its 1.0-step alarm never fires, exactly as §4.1 predicted by arithmetic before the
data.** Median \|Δe\| is **0.00000 step at every rung**, and the largest `e` observed anywhere in the wave is
**0.03000 step** on family V (`D17_cdk14_oiii5` at budget 1, which is its budget-0 value: `D17`'s vertex does not
move at all) and **0.13150 step** under R — 33× and 7.6× under the 1.0-step alarm respectively. §4.1 called this
for family V and it held to the digit.

> **But the veto could not have fired for family R either, and the design said it was live.** §4.1 argued that R's
> `|Δe|` is not bounded by the reachable-spread table — correct — and concluded *"S16-E(i) is live for `R` and `R`
> alone."* It never computed what the **median** over 20 datasets requires: at least **11** datasets must each
> move by ≥ 0.10 step. R moved **6**, one of them by 0.13071. **A dataset cleared the materiality floor by 31 %
> and the veto stayed silent**, because the aggregation statistic cannot see an effect confined to fewer than half
> the population.
>
> This is F68 again, in a second place in the same section: §4.1 checked whether the *quantity* could reach the
> threshold and never whether the *statistic computed over the population* could. It is a weaker instance than
> S16-A(b) — the veto is not provably unreachable for R, only unreachable for any sparse effect — and it is
> recorded because the clause was labelled *live* on an argument that stopped one step short.

### §2.8 What item A establishes

1. **The SEM criterion separates cleanly at the top, on the population that matters.** S16-A(a) is `0 of 2` at
   `V1.00`: every rejection on the one run whose fit the rejection destroys is suppressed, and this is measured
   at the **production consensus level over all four candidate models** for the first time — the gap that refuted
   wave 14's V4. `[n = 1 run by construction: caboose, and it is labelled so everywhere it is quoted.]`
2. **It does not over-suppress.** 12 of 12 benign consensus rejections survive `V1.00`, 100 %, and 16 of the 18
   consensus rejections overall survive — the two suppressed are `caboose`'s.
3. **It is a criterion and not an off-switch.** S16-C passes at every rung, `V4.00` included: 4 runs still fire at
   budget 1 and 4 at budget 3 even when everything below `s = 4` is vetoed.
4. **Selectivity is attainable in this family**, on one run, at one threshold, inside a window **0.0607 wide in
   `s`** (`[0.0403, 0.1010)`) on a distribution that spans 0 to 79.78. §2.5.
5. **The added parameter is inert at its default, measured to the byte**, where the rejection executes. §2.2.
6. **And it does not vindicate F45's original complaint.** `D16_esprit550_ha3` — the case where the discarded
   point **is** the generator's true focus — sits at `s` = 8.7260. No threshold anywhere near 1 touches it, and
   none is proposed that would. *A point can be a genuine large deviation in its own error bar and still be the
   one you must keep, and no criterion of this shape knows which.*

**And it ships nothing.** The product-side `N*` plumbing does not exist (§5.3), so even outcome 2 would have been
a costed recommendation. Outcome 4 means there is not even that.

---

## §3 — Item B: RULE P16, and F67 narrowed to two candidates

### §3.1 The probe became an accidental two-directional demonstration

`STEP P0` runs two `optimize` runs — `D12_c14_585_afbin2` and `D17_cdk14_oiii5`, **both members of the
disagreeing set** `{D08, D09, D10, D12, D14, D15, D17}`, which is wave 15's Lesson 1 made mechanical: wave 15's
G-c was demonstrated 9-of-9 in both directions on `D18`, a member of the *agreeing* set, and reached 13 of 20.

| run | window | duration | result |
|---|---|---|---|
| P0, **before item B's code existed** | 14:11:09Z–14:11:48Z | **39 s** | `optimize params printouts: 0   expected: 2` → **COULD NOT LOOK on 2 of 2** |
| P0, **with the instrument present** | 15:23:09Z–15:23:48Z | **39 s** | `optimize params printouts: 2   expected: 2` → **P0 OK** |

> **The same driver, the same two datasets, the same 39 seconds, and the opposite verdict — because the thing it
> looks for now exists, and nothing else changed.** That is the cheapest possible demonstration that the probe
> discriminates, and it is stronger than a constructed one because the "absent" direction was the *real* absence
> of a real instrument, not a mutated file.
>
> **Neither run was designed as a two-directional demonstration.** The controller's sequencing error (§1.1)
> produced one by accident, and it is worth more than the one that would have been designed. It also shows the
> probe behaving as the design required: *"COULD NOT LOOK is NOT 'the params agree'; RULE P16 is UNEVALUATED on
> those datasets and must say so BY NAME."* It said so, by name, on both.

The same is visible in the two gate logs: build 1's reads `optimize params printouts: 0 expected: 8 → **ITEM B
COULD NOT LOOK** on 8 of 8 gate logs. RULE G16 is unaffected`, and the final gate's reads `8 expected: 8`. **The
gate's own population clause and item B's dump clause are separate reads on the same artifact, and one failing
does not silence the other.**

### §3.2 The `Region` predicate could not promote a cropped region, and it was fixed before any verdict was read

`score_params_w16.py`'s `F_CONDITIONAL` entry for `Region` is the mechanism that promotes a non-Full region from
*inert-by-condition* to **F-live**. As pre-registered it matched **any** region whose inner crop is null — which
is true of a genuinely **cropped** outer boundary. A cropped region would have been excused instead of promoted.
Only the outer-boundary clause discriminates, so it is now the only one:

```python
"Region": ("both sides Full", lambda v: "StartX=0, StartY=0, Height=1, Width=1" in v),
```

**The fix was made before any P16 verdict was read, and it does not change this wave's verdict** — both sides are
Full on all five datasets — *and that is precisely why it was safe to make there*. **A condition that cannot
promote is a check that cannot fail** ([F66](followups.md)(a)). It matters beyond this wave because `af-fit`'s
**primary** params path is `LoadOriginalDetectorParams` — the run's own saved detection JSON, which **can** carry
a non-Full region. The design's §5.2 describes only the fallback path.

Three further imprecisions in the pre-registration are recorded rather than silently patched: the design says
*"~45-field object"* where `StarDetectorParams` has **55** public readable properties; the pre-registered `Region`
rendering drops `StarDetectionRegion.Index`, so two regions differing only by index compare equal (kept, because
deviating would change the string the scorer's self-test pins); and `F_INERT_BY_PROOF` lists
`ModelPSFPixelScaleOnly`, **which is not a property of the object** — a dead entry suggesting the partition was
written against a slightly different field list. A dead entry in an exclusion list is F66's shape: it can never
exclude anything, and nothing would have told you.

The formatter itself is **reflective**, not a hand-written field list, and a test asserts that a property added by
a later wave appears in the dump without anyone editing the formatter — *because a hand list would reproduce the
very defect being measured: two printouts that drift apart.* It writes to the **console**, never through `Emit()`,
because clause W1 is a byte comparison of `af_fit_summary.txt` and a single stray line there would have silently
broken item A's control. There is a test whose only job is to assert that call site.

### §3.3 RULE P16, clause by clause

| clause | pre-registered threshold | measured | verdict |
|---|---|---|---|
| **coverage** | both sides dumped | `af-fit` **39 of 39** logs, could-not-look **0**; `optimize` **10 of 10** logs (8 gate + 2 probe), could-not-look **0** | ok |
| **P16-POP** | ≥ 1 dataset from the **disagreeing** set `{D08, D09, D10, D12, D14, D15, D17}` **and** ≥ 1 from the agreeing set | 5 datasets with both sides: **`D12`, `D17`** (disagreeing) + **`D18`, `D19`, `D20`** (agreeing) | **met** |
| **PER-RUN** | any field whose value varies across runs on either side is reported and not generalised over | **none** — every field is constant across runs on both sides | nothing to report |
| **P16-a** | at least one **F-live** field differing ⇒ P-a | **0 F-live differences on all five datasets.** 55 fields dumped per side; **53 byte-identical**; 2 differ | **P-b** |
| the 2 that differ | must be in **F-inert-by-proof** or be promoted | `PixelScale` (`af='1'` / `optimize='NaN'`) and `SuppressInfoLogging` (`False` / `True`) — both **inert by proof** | as pre-registered |
| **conditionals** | `Region`, `MeasurementAverage`, `ModelPSF` inert **only while a CHECKED condition holds**, promoted otherwise; checked even when EQUAL | `Region` Full both sides, `MeasurementAverage=Median` both sides, `ModelPSF=False` both sides — **all three conditions hold**, nothing promoted | as pre-registered |
| **P16-b** — F67(a), the free half | re-score wave 15's G-c on the knob-diff control; reproduce **40 runs, 205 overridden knobs, 0 could-not-look** | **40 / 205 / 0**, and **20 + 20 distinct preset sets** | **reproduced exactly** |

`PixelScale` is inert by proof because it is consumed only inside the `ModelPSF` block (`StarDetector.cs:902,
915`) and **both paths set `ModelPSF = false`** — checked, not assumed, in the same table. `optimize` dumps it as
`NaN`, which the design predicted from the gate logs before item B existed.

`optimize` dumps two sources. The scorer reads **`optimize/baseline`** — `ApplyAfContext(BuildStarDetectorParams(options))`,
the same call `af-fit` falls back to, and the only apples-to-apples side. `optimize/seed` is the default bundle
and differs from `af-fit` in exactly one field (`NoiseReductionRadius` 3 vs 4); **that difference is expected by
construction and is not a finding.** It is dumped for provenance and named here so it is never quoted as one.

> ### **RULE P16: P-b.** Every F-live field is identical on every dataset covering both halves of the population.
>
> **So F67's disagreement is DOWNSTREAM of the params.** F67(c) is narrowed from three candidates to **two, by
> construction and not by hypothesis**:
>
> 1. **the frame loading** — `DetectionSource` / `DiagnosticUtil.LoadRenderedImage` versus `RunEvaluationLoader`;
> 2. **the counting/gating stage** — `StarDetectorResult.DetectedStars.Count`, **pre**-filter, versus
>    `HocusFocusStarDetectionResult.DetectedStars`, **post** ROI-crop and post-`MeanOutliers`
>    (`HocusFocusStarDetection.cs:759`).
>
> **The two star counts are different measurements with the same name, and now that is proved rather than
> suspected.** They cannot be reconciled by pointing at the settings: the detectors were built from one settings
> file and they agree on all 53 live fields.

### §3.4 P16-INSTRUMENT-vs-PRODUCT: the two clauses disagree on the branch that occurred, and that is a finding

The rule fixed the test before the data: *if every F-live difference is a field that exists only because the
harness configures `af-fit` — i.e. the live app never sets it differently from itself — then F67 is an*
**instrument** *artifact. Otherwise it is a* **product** *finding.*

> **THE CLAUSE IS ILL-POSED ON P-b, AND THE RULE CONTRADICTS ITSELF THERE.** The test quantifies over *"every
> F-live difference"*, and under P-b **there are none**. A universal quantifier over the empty set is vacuously
> true, so read literally the clause returns **INSTRUMENT** — while the **P-b row of the same rule**, six lines
> earlier in the same pre-registration, says P-b is *"**Also a product finding**, and the more interesting one."*
> **Two clauses of one rule, opposite answers, on the branch that actually happened.**
>
> The clause was plainly written for the **P-a** branch, where there is a set of differing fields to classify. It
> was never asked what it returns when that set is empty — which is [F68](followups.md)'s omission in a third
> shape: not a denominator this time, but a **quantifier whose domain can be empty**, on a clause that §4.3's
> both-branches table does not cover at all (that table lists P16's outcomes as P-a / P-b / P-c / UNEVALUATED and
> never reaches the instrument-vs-product test).

**The verdict recorded is PRODUCT**, on the P-b row — the specific, non-vacuous clause written for this exact
outcome, against the general clause written for the other one. The choice is not arbitrary and it is not made
because it is the more interesting answer:

- The **counting stage** is not harness code. `HocusFocusStarDetection.BuildStarDetectionResult:759` — post-ROI,
  post-`MeanOutliers` — is what the **running application** reports as its star count, on every autofocus, on
  every frame. `af-fit`'s pre-filter count is the harness's.
- The **frame loading** difference is likewise a real pair of production paths (`RunEvaluationLoader` is the
  optimizer's, and the optimizer is a product feature).
- So the disagreement is not *"the harness misconfigures the detector"*, which is what the instrument verdict
  means and would have implied only that harness controls need rebuilding. It is *"two production entry points
  measure a differently-defined quantity and both call it the star count."* **The instrument reading is not
  merely vacuous, it is false on the facts**: the harness's configuration is identical to the product's on all 53
  live fields, which is precisely what P-b measured.

> **What the user gets: the `HocusFocusStarDetection` path.** The app runs it; `af-fit` is the harness. Every
> star count a user sees in NINA is post-ROI-crop and post-`MeanOutliers`. **The number that has been quoted in
> fourteen waves of `af_fit_points.csv` is the other one.**
>
> **And the register consequence is unchanged and now sharper: any future control built on either count inherits
> this, and neither number is wrong.** Nothing in either artifact says which definition it is using — which is why
> wave 15's G-c returned *could not look* on exactly the seven datasets where the intervention bit hardest, and
> lost RULE L15 its verdict.

Reproduce: `python3 /mnt/d/hf_w16/score_params_w16.py --gate /mnt/d/hf_w16/gate --probe /mnt/d/hf_w16/probe
--affit /mnt/d/hf_w16/affit_N --out /mnt/d/hf_w16/p16_score.txt`;
`python3 /mnt/d/hf_w16/score_params_w16.py --self-test` (13 demonstrations, every clause in both directions,
including *"a `Sensitivity` difference IS F-live"*, *"`ModelPSF` True on one side is PROMOTED"*, and *"report
refuses a verdict when only the AGREEING half is covered"*);
`python3 /mnt/d/hf_w16/score_params_w16.py --gc-rescore /mnt/d/hf_w15`.

---

## §4 — Item C: the nine UI changes A1–A9

`cmd.exe /c "query session"`, re-run at write-up time:

```
 SESSIONNAME               USERNAME                 ID  STATE
 services                                            0  Disc
>                          ghili                     1  Disc
 console                                             2  Conn
 rdp-tcp                                         65537  Listen
```

The user session (`ghili`, id 1) is **`Disc`**; the `console` row carries no username and is the
no-user-logged-in state. **NOT ATTEMPTABLE, seventh consecutive wave.** ~30 m on a connected session, wave 13 §3's
procedure unchanged. One command, one line, recorded.

---

## §5 — The suite, the budget, and what was not run

### §5.1 The suite

Wave 16 ships code in two commits, and each was gated on its own count:

| commit | what it added | suite |
|---|---|---|
| baseline | — | 3781 |
| `9e95446` item A | `SemScaleSpec`, the `RejectionTest` overload, `SemScaleArgs`, the SEM printout | **3809** (+28) |
| `4fd613d` item B | `ParamsDump`, both runners' call sites | **3824** (+15) |

> **3824 passed, 0 failed, 0 skipped, in 3 m 36 s, exit 0.** Verified by **COUNT**, not by tick
> ([F37](followups.md)), and not piped to `tail`, which would mask the exit code.
> `dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`.

**Re-run at write-up time on the unmodified tree and it reproduces: `Failed: 0, Passed: 3824, Skipped: 0,
Total: 3824`.** The flaky `SendAsync_WritesOnABackgroundThread` did not appear.

### §5.2 The budget, against the estimate

| step | population | estimate | actual |
|---|---|---|---|
| **RULE G16** — the gate, **build 1 (superseded)** | 8 runs | *not budgeted* | **41 m 36 s** (13:28:25Z → 14:10:01Z) |
| **STEP P0** — the probe, instrument absent | 2 `optimize` runs | *not budgeted* | **39 s** (14:11:09Z → 14:11:48Z) |
| **RULE G16** — the gate, **final binary** | 8 runs | **~42 m** | **40 m 55 s** (14:41:15Z → 15:22:10Z) |
| **STEP P0** — the probe, instrument present | 2 `optimize` runs | **~1 m** | **39 s** (15:23:09Z → 15:23:48Z) |
| **item A** — rung `N` (control) | 39 runs | ~11 m | **11 m 15 s** (15:24:07Z → 15:35:22Z) |
| clause W1 scoring, which unlocks the ladder | Python | — | **51 s** |
| **item A** — `V0.07` | 39 runs | ~11 m | **11 m 03 s** |
| **item A** — `V1.00` | 39 runs | ~11 m | **10 m 58 s** |
| **item A** — `V2.00` | 39 runs | ~11 m | **10 m 58 s** |
| **item A** — `V4.00` | 39 runs | ~11 m | **11 m 03 s** |
| **item A** — `R` | 39 runs | ~11 m | **11 m 02 s** |
| **item A** — six rungs, sum | 234 `af-fit` runs | **~66 m** | **66 m 19 s** (wall 15:24:07Z → 16:31:29Z = **67 m 22 s**) |
| scoring (S16, P16, prov, fingerprint, conform) | Python | < 10 m | **< 2 m** |
| **wave total `TestApp` wall** | | **~1 h 49 m** | **2 h 30 m 08 s**, against a 6 h ceiling |
| the suite | `dotnet.exe test` | ~4 m | see §5.1 |

> **THE LADDER CAME IN AT 0.5 % FROM ESTIMATE — six rungs in 66 m 19 s against 66 m — and the whole overrun is the
> second gate.** 2 h 30 m 08 s − 1 h 49 m = **41 m**, which is 41 m 36 s of superseded gate less a 39-second probe
> that was in the estimate once and ran twice.
>
> **The estimate was right because it was priced from the same instrument on the same population.** §7 refused the
> brief's ~5.2 m/run figure on the grounds that it is an `optimize --per-run --max-evals 250` rate and would have
> priced one `af-fit` rung at **203 minutes** against wave 14's measured **11** — an 18× error. Wave 14's own
> measurement of six rungs on these 39 runs was used instead, and it transferred to within 19 seconds over six
> rungs. **Wave 15's lesson was that a rate needs a representative population; wave 13's, that it needs the same
> instrument. Wave 16 applied both and the estimate stopped being interesting, which is the goal.**
>
> **The one budget line nobody wrote down is the one that blew it.** There is no row in §7 for *"re-run the gate
> because the step order was not followed"*, and there could not be — but there is also no row for *contingency*,
> and a wave that ships code from two items into one binary has an obvious failure mode with a known price. §1.1.

### §5.3 What was NOT run, and what it costs

- **The product-side `N*` plumbing** (design §2.3): a count map on `AutoFocusRegionState` cleared with the
  measurements; a **pooling rule** for multi-frame points that does not double-divide against
  `AverageMeasurement`'s existing `/√frames` (`CvImageUtility.cs:891-921`); the same again in `RunEvaluationData`
  (`:745, :783, :788`), which is the optimizer's independent point-building path; and a decision between
  `DetectedStars` and `hfrStars.Count`, which differ whenever saturated stars are excluded and only the second of
  which is σ's actual denominator. **~3–4 h plus tests.** It is the entire distance between any recommendation
  and a ship — **and outcome 4 means there is no recommendation for it to ride with.** The price is recorded so
  that a future wave reaching outcome 2 does not have to re-derive it. `N*` is available where σ is *computed*
  (`HocusFocusStarDetection.cs:804-806`) and gone by the time the fit's points are *built*
  (`AutoFocusEngine.cs:1071`), because the only carrier between them is `MeasureAndError` — an external NINA
  `struct` with two `double`s.
- **Any repaired form of S16-A(b).** Costs **zero minutes** — it is Python over data on disk — and is not done,
  because re-deciding a pre-registered clause after seeing the data is the one thing that would make every other
  verdict in this series worthless. The clause stands at FAIL and the defect is [F68](followups.md). *A wave that
  can fix its own bar for free and does not is the only kind whose NO VERDICTs mean anything.*
- **`V0.50`.** Dropped from the ladder because §4.1 computed it as arithmetically identical to `V1.00` — on the
  **winning-model trace** population. §2.4 shows that population is not the one the consensus is computed over, so
  the identity is unverified at the consensus level. **~11 m** to check. Nothing observed suggests it differs, and
  it would not change outcome 4.
- **A Stage-A committed counterfactual for family V.** Wave 14 built one; its V4 gate is what cost RULE F14 its
  verdict. Wave 16 measured Stage B directly and kept the **control rung** instead. **Cost: the wave has no
  committed prediction to be scored against — it has a byte-level control.** Stated, not hidden.
- **F67(c) — naming which of the two remaining candidates causes the pipeline disagreement.** P16 narrowed it to
  two; deciding between them needs an instrumented run of both loaders on one frame. **~1 h**, a different wave,
  and now much better posed than it was.
- **[F63](followups.md)(b)** — pinning the SHIPPED default rather than `astrodet`'s. **Re-priced at ~0** by wave
  15 (after wave 14 they are the same value). Not done here only because wave 16 pins both explicitly on every
  arm, so nothing is left to move. A documentation close for whichever wave next edits the register.
- **F63(a) on the 19 real bank runs at the landing level.** ~90 m per arm and **it buys no arbiter** — the real
  bank has no truth.
- **[F59](followups.md)'s five knobs** stay at code defaults. Re-pinning moves the coordinate system RULE G16 has
  now reproduced a **seventh** time: **~42 m** for a fresh baseline plus the re-derivation of every cross-wave
  comparison. A decision, not a cleanup.
- **The nine UI changes A1–A9**: ~30 m on a **connected** session (§4).
- **F18 / F21 / F25 / F26** — the step-size and sweep-width family, untouched for many waves. **F21's half-width
  instability is the load-bearing one.** Still unpriced, and still the strongest candidate for wave 17.
- **F61(b), F52(d), F46(b), F54, F50** — nothing depends on them.

---

## Lessons

**1. A threshold stated as a count carries a denominator, and the denominator is part of the claim.** S16-A(b)
demanded *"≥ 30 of 33 survive"* and measured **12 of 12** — 100 % of the property, failing a bar whose maximum
attainable value on the instrument it reads is 12. The 33 came from wave 14's **winning-model trace**; the clause
reads the **production consensus**, which is 18 positions of which 24 of wave 14's 42 are rejections the product
never performs. The design named the population change in the line above the threshold and cited
[F65](followups.md) — the entry that exists because those two views differ — four times elsewhere. *Ask of every
threshold: over what population is this number computed, and is it the population the threshold was derived from?
State bars as rates or as maxima wherever a count would do, because `== 0` survives a denominator error and
`≥ 30` does not.*

**2. Three waves running, the satisfiability section contained a defect of its own class.** Wave 14's C4 was a
decisive clause arithmetic had foreclosed; wave 15's G-d could never observe its own PASS; wave 16's §4.1 computed
a maximum attainable value over the wrong population — S16-A(b)'s denominator — and declared S16-E(i)'s median
veto *live for `R`* without computing that it needs 11 of 20 datasets to move, where R moved 6. A third shape sits
outside §4.1 entirely: `P16-INSTRUMENT-vs-PRODUCT` quantifies over *"every F-live difference"*, and on the P-b
branch that set is **empty**, so the clause is vacuously true and returns the opposite of what the same rule's P-b
row says (§3.4). Each section was written to prevent the previous wave's failure and did prevent it. *The pattern
is not that these sections are useless — they are the most productive part of each design — it is that each one
checks the previous wave's failure mode. Make the check generic: for every clause, name the population, the
statistic and the aggregation; compute the bar's reachable range under all three; and for anything quantified over
a set, say what it returns when the set is empty.*

**3. A reservation stated honestly in advance is worth more than a prediction.** §4.1 called `V0.07`'s
CONTAINS-SELECTIVELY *"plausible, not proved,"* because the window was derived from one model's trace while the
consensus intersects four. It fired. The same design forgot the same distinction one clause earlier. *When you
notice that two instruments are not interchangeable, write it down as a property of the wave, not as a caveat on
one paragraph — a caveat protects the paragraph it is attached to and nothing else.*

**4. Excluding a mechanism from a gate that was never proved for it is not softening the rule.** Family R was
excluded from V4′ and from RECOMMEND on a proof argument, before any measurement. It then redirected on 12 of 39
runs, failed containment by four orders of magnitude, and produced the wave's only material out-of-sample
movement — 0.13071 step away from truth on `D13`, **with R² improving**. Had R been gated on V4′ the wave would
have reported a gate failure instead of the mechanism finding, exactly as wave 14 did. *Gate a mechanism on a
lemma proved for that mechanism, or give it its own outcome. There is no third option that is honest.*

**5. A fit statistic that improves while the answer worsens is not a wave-14 curiosity.** [F62](followups.md)
measured σ_focus improving by up to 88 % while the distance to a known truth improved on none. R's `D13` redirect
reproduces it on a mechanism F62 never touched: R² 0.998952 → 0.999834, vertex 16.7 focuser counts further from a
truth the generator knows. *Any criterion that is scored on how well the surviving points fit will eventually
prefer removing the points that disagree with a wrong model. Only an out-of-sample arbiter can see it, which is
why the synthetic bank is on the never-drop list.*

**6. The controller's mistake produced the wave's best control, and that is not an argument for making mistakes.**
Briefing item A only, building, and gating meant a second binary and 42 lost minutes. It also produced a gate
result on **two** builds — proving that adding a diagnostic printer to `optimize` moves none of the eight `BestJ`
— and a probe demonstrated in both directions with the *real* absence of a real instrument rather than a mutated
file. *Both of those are worth more than the designed versions. Neither is worth 42 minutes, and the reason to
record them together is so the next controller reads the step order instead of hoping for a lucky accident.*

**7. An interlock in the driver is worth more than a rule in the design.** `affit_w16.sh` refuses to run any rung
but the control unless a marker only the scorer can write exists. Five treatment rungs aborted in about a second
each. The design also *said* the control must run first; the design says a lot of things. *The traps that held
this wave — WSL-path refusal, population asserted before scoring, "could not look" guarded before any field read,
`--profile-id` cardinality as well as containment, ASCII-only printouts — are all in shell and Python, not in
prose. Put the discipline where it executes.*

**8. Two numbers with the same name are still not the same measurement, and now it is proved from the params
side.** Wave 15 inferred it from a population disagreement; wave 16 dumped all 55 `StarDetectorParams` fields from
both entry points through **one reflective formatter** and found 53 identical, the other two inert by proof. The
detectors are configured identically and the counts still differ, so the cause is the frame loading or the
counting stage — and **what the user gets is the `HocusFocusStarDetection` path**, post-ROI and post-`MeanOutliers`,
not the number fourteen waves of `af_fit_points.csv` have printed. *When a diff must not drift, generate it
reflectively; a hand-written field list reproduces the defect it is measuring.*
