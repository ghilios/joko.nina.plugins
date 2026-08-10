# Waves 17–21 — continuation prompt

Run this after `/clear`. Waves 14, 15 and 16 have executed; this is the state after them.
**You are the controller, you delegate context-heavy work to agents, and you do not stop for approval.**

---

## 0. STANDING AUTHORISATION — unchanged from the prompt this replaces

1. **Waves MAY ship user-visible product-behaviour changes** when the wave's own ship rule, fixed and committed
   BEFORE the data, is satisfied. If the rule is not met, the change does not ship and is recorded as a costed
   recommendation.
2. **~6 hours of compute per wave.** A wave that wants more must cut scope or split across two and say which.
3. **ONE branch, ONE PR: `ghilios/synthetic-af-bank-followups-wave13`, PR #191.** Never push `develop`. Do not
   open new PRs. The PR body carries one section per wave, findings first; retitle as the scope grows.
4. **Proceed without asking.** A blocker is a result: record what was tried, what failed, what unblocking costs,
   and move on.

**Stop early** if the register runs dry, if the gate fails and cannot be explained, or if two consecutive waves
produce no finding worth a register entry.

---

## 1. WHERE THINGS STAND after waves 13–15

| | state |
|---|---|
| **the gate** | Eight values, **reproduced on every binary this series has built plus two wave-10 builds**, bit-identical to sixteen digits every time. It has never moved. |
| **`MaxOutlierRejections`** | **Code default now 0** (wave 14, owner's decision overriding wave 13's pre-registration). Profiles storing 1 keep 1. |
| **F63** | **Answered.** The landing moves on **19 of 20** synthetic datasets, with search noise measured at **zero** (wave 15's G-e). |
| **F45(b)** | **Measured in SEM units (wave 16) and the criterion WORKS**: 0 of 2 damaging rejections survive at `V1.00`, 12 of 12 benign ones do. **RULE S16 still returned NO RECOMMENDATION** because the confirming clause was unsatisfiable — see §3a. Shipping needs ~3–4 h of production plumbing (`N*` is not reachable in `AutoFocusEngine`). |
| **F67** | **Narrowed to a PRODUCT finding (wave 16).** 53 of 55 live params identical, so the disagreement is **downstream**: frame loading **or** the counting/gating stage, two candidates by construction. **The user gets the post-filter count**; `af_fit_points.csv`'s `Stars` is the pre-filter one. |
| **F68** | New standing discipline: **a threshold stated as a COUNT carries a denominator.** Three consecutive satisfiability sections checked the value a clause can reach without checking the population, the aggregation, or the empty case. |
| **RULE F14** | **NO VERDICT, permanently.** Do not re-open it — see §3. |
| **F57** | Closed, including (d). `D:\hf_w10\exe_v1wav` can be archived or deleted. |
| **F15** | Fixed and confirmed in three waves of arms: 42 of 42 bank landings byte-identical. |
| **the suite** | **3824** on this branch (`develop` was 3744). CI verified by COUNT out of the log. |

**Artifacts:** `D:\hf_w13\`, `D:\hf_w14\`, `D:\hf_w15\`. Reusable: `score_w12.py` (the gate),
`prov_w15.py` (free controls, self-testing), `score_f14_w15.py` (V4′), `convert_landing_w15.py` (landing →
harness settings, via the production Accept path), `bank_fingerprint_w15.py` (the F15 control).

---

## 2. THE BACKLOG, priced

Pick by **decision value per hour**: a question whose answer changes what ships, what the register believes, or
what the next wave does. Prefer questions that can be **refuted**. Prefer instruments **already printed**.

| # | candidate | why now | cost |
|---|---|---|---|
| **F67(c)** | **One step from closure and it is a PRODUCT question.** Wave 16 narrowed it to two candidates by construction: the frame loading (`DiagnosticUtil.LoadRenderedImage` vs `RunEvaluationLoader`) or the counting stage (`StarDetectorResult.DetectedStars.Count`, pre-filter, vs `HocusFocusStarDetectionResult.DetectedStars`, post-ROI-crop and post-`MeanOutliers`). Deciding between them tells the register **which number fourteen waves have been quoting** | ~1 h |
| **F45(b) production plumbing** | `N*` is NOT reachable in `AutoFocusEngine` — `:901` drops the count into `MeasureAndError`, a NuGet struct of two doubles. Needs a parallel count map, a pooling rule that does not double-divide against the existing `/√frames`, the same again in the optimizer's path, and a choice between `DetectedStars` and `hfrStars.Count` | ~3–4 h + tests |
| **F63(b)** | Pin the SHIPPED default rather than the harness file's. **It splits in two, and only half is cheap.** The **fit inputs** are now a no-op: after wave 14 all four values in `pinned_settings_w11.json` equal the shipped code defaults (`MaxOutlierRejections` 0, confidence 0.95, weighted True, Hybrid) — before wave 14, `MaxOutlierRejections` was the only mismatch. The **detector knobs** are NOT: the file was exported from `Default-2026-08-05T10:54:36` (**not `astrodet`** — the register's shorthand is wrong), it carries `UseAdvanced=False` with `Simple_*` all `Typical`, so its advanced knobs are **preset-derived**, not code defaults. That half is still a coordinate-system move | fit half ~0; detector half ~1 h + re-derivation | ~1 h |
| **F59** | Re-pin the five knobs at real values. Deliberate coordinate-system move; needs a fresh gate baseline | ~42 m + re-derivation | ~1 h |
| **item 2 (UI)** | **Run `query session` FIRST.** `Disc` on six consecutive waves. If `Conn`, ~30 m and wave 13 §3's procedure is correct | 0 or ~30 m |
| **F18/F21/F25/F26** | step-size and sweep-width family, untouched for many waves; **F21's half-width instability is the load-bearing one** | unpriced |

---

## 3a. RULE S16 RETURNED NO RECOMMENDATION, AND THE REASON IS NOT THE DATA

Wave 16 measured the SEM criterion and **it works**: `S16-A(a)` is exact — **0 of 2** rejections on the one run
with ρ > 2 survive at `V1.00`, so `caboose`'s damaging rejections are killed — and `S16-D` at `V0.07` reached
**CONTAINS-SELECTIVELY**, the branch §4.1 expected to be foreclosed.

**It returned NO RECOMMENDATION because `S16-A(b)` could not pass.** The clause demanded **≥ 30 of 33** benign
rejections surviving; the production consensus contains only **12**, and all **12 of 12** survived. The bar was
an absolute count against a denominator borrowed from a different instrument — wave 14's audit counted one
model's trace (42 rejections), the production budget table counts the **consensus** (18), and the 24 in between
are **rejections production never performs**.

**Do not simply restate the bar as a rate and re-score wave 16's rungs.** That is the harvest RULE F14 was
closed to prevent, one wave later. A wave that wants a recommendation out of the SEM criterion must:
1. fix the corrected clause **on the consensus population, as a rate, naming its aggregation** (F68), and
2. apply it to a population measured **after** the rule is fixed.

**And correct the record while you are there:** wave 14's published *"32 of 33 rejections at s ≥ 1"* counted
phantoms. On the consensus the separation is **12 of 12 benign / 0 of 2 damaging** — the direction is unchanged
and the N is smaller than published.

---

## 3. RULE F14 IS CLOSED. DO NOT HARVEST IT.

Wave 14's RULE F14 returned **NO VERDICT** because its V4 gate tested a stronger claim than the design proved.
Wave 15 built **V4′**, which tests the proved property, and **it passes** — 585 of 585 triples, 0 violations.
**The corrected diagnostic visibly points at family A, `f = 0.50`.**

**It is still NO VERDICT, permanently, and the next wave must not convert it.** Three reasons, all recorded:

1. The rule's own guard — *"never resolved by preferring whichever rung agrees with something else"* — cannot
   bind a reader who has already seen the published diagnostics.
2. **V4 was not the rule's only defect.** The branch built to consume the wave's best measurement (the SEM
   audit) is unreachable. A rule that cannot consume its own strongest evidence should not issue verdicts.
3. A wave that repairs a failed gate and immediately harvests a verdict teaches the next wave to do the same.

**What to do instead:** the SEM-floor wave writes a NEW rule with a branch table that CAN consume the SEM
result, and applies V4′ **prospectively** to a population measured after the rule is fixed.

---

## 4. THE STANDING DISCIPLINE — waves 14 and 15 added four

The inherited ones still hold: **score a change on something the change cannot move**; **a control that cannot
fail is not a control, and "nothing moved" needs one more than "something moved"**; **ask where a knob REACHES,
not only whether it matters**; **intervention beats correlation**; **fix the rule before the data, on the bar
the predecessors faced, and check it is satisfiable**; **the cheapest instrument is the one already printed**;
**price the payoff in both directions and record estimate AND actual**; **say what you did not run, and price
it**; **pin `--settings` AND `--profile-id` on every arm**.

**Added by waves 14–15, each paid for:**

- **A gate that has never been shown to PASS is not a gate.** Wave 14 shipped **three** checks whose PASS branch
  was unreachable; all failed closed, which is the safe direction and is still three broken instruments.
  **Every gate must be demonstrated to PASS on known-good input AND to FAIL on known-bad input before it is
  quoted** — and the demonstration must **assert the mutation actually happened**, because a test that a control
  can fail is itself a check that can silently not run. Wave 14's first attempt at one wrote its mutation to a
  path that did not exist and reported PASS on an unmodified copy.
- **Check that BOTH branches of a clause are reachable, not just that the PASS value is attainable.** Wave 15's
  G-d could never see "converted files identical" because a landing's `CreatedAtUtc` guarantees they differ.
  This appeared *inside the satisfiability section written to prevent exactly this*.
- **Refuting an argument is not refuting its conclusion.** Wave 14 demolished the stated reason for skipping
  F57(d), spent 81 minutes, and found the conclusion had been right on a ground nobody checked. **Before
  overturning a decision because its reason is wrong, ask what would make the decision right.**
- **A control demonstrated on one dataset is a control demonstrated on one dataset.** Wave 15's star-count
  control passed 9-of-9 on `D18` in both directions before any arm ran — and reached only 13 of 20 across the
  population, because `D18` sits in the agreeing set. **It varied the right axis on the wrong sample.** When a
  control's premise is an equality between two pipelines, test that equality across the population first; it is
  usually free.

### Traps

- **Pass scorers WSL paths** (`/mnt/d/...`). A Windows path yields UNEVALUATED and looks like a failed arm.
- **Silent truncation exits 0**: one bank path contains a SPACE, and `TestApp.exe` eats a `while read` loop's
  stdin unless you redirect `< /dev/null`. **Assert the POPULATION SIZE.**
- **"Could not look" needs its own state, and the guard must come BEFORE any field read.** Wave 14 broke this in
  its own scorer, which printed empty control sets that read as "all controls agree".
- **`strings` is TWO instruments.** Type/member names live in `#Strings` as UTF-8 and are findable; string
  literals live in `#US` as UTF-16 and are not — use `strings -el`. And **`grep AtrousWaveletFast` returns 1 on
  every binary this project has produced**, so it is not evidence of a detector version. Read the
  `DetectorVersion` **field**.
- **A landing is NOT a harness settings file.** It is flat; the harness reads `parsed["Options"]`. Passing one
  to `--settings` silently uses code defaults, and `SettingsFingerprint` is identical across landings because it
  appends nothing when `Options` is absent. Use `convert_landing_w15.py`.
- **`UseAdvanced=False`** on the pinned file means Simple-mode presets recompute the advanced knobs, so editing
  them in that file has no effect. A knob-lifting converter collapses both arms onto the preset baseline.
- **The fire rate is not one number** — `MaxOutlierRejections` fires at different rates through `optimize`,
  `af-fit` and `bank-verify`, and not on the same runs. Never quote a rate without its settings.
- **Never run NINA during a pinned arm.** Only one `TestApp.exe` at a time. No fan-out (1.33×, F60; a pinned arm
  cannot fan out at all).
- **Never rebuild an arm's directory mid-wave** (F53(c)). If the wave ships code that an arm must exercise,
  **plan the binaries up front** — wave 14 needed two and had to disclose that its gate was not a control on the
  binary its second item ran.
- Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"`.
- `D17_cdk14_oiii5` finds zero stars at short exposures; `lumos` exits rc=3; `astrodet` the DATASET is frameless
  (F14); `Panos` has a degenerate σ fit and must be UNEVALUATED **by name**.
- **Price an arm from a representative population.** Wave 15's estimate was 1.8× high because it was derived
  from the gate's mixed real+synthetic rate and the arm was all-synthetic.
- On CI, verify the **COUNT**, not the tick (F37).

---

## 5. THE GATE, unchanged

```
toml999 0.995784 | CWhiteFocus 0.996068 | uneven 0.996368 | muggsie 0.997195
mccomiskey 0.976746 | D18 0.999882 | D19 0.999487 | D20 0.999738
```

`--settings D:\hf_w11\pinned_settings_w11.json` (md5 `a67ffc06`) **and**
`--profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382` (astrodet), sequential, `--per-run --max-evals 250`, both
named in the script header. Score with `python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w<N>/gate --rule G<N>` and
`python3 /mnt/d/hf_w15/prov_w15.py --self-test /mnt/d/hf_w<N>/gate`. Free controls as FIELDS: `BuildId`,
`DetectorVersion`, `ProfileId`, `ConcurrencyCheck` read across the arm, `FitInputs`, `BaselineJ`. Wave 5's φ
table is invalid on three axes — never quote it.

**The `BuildId` novelty list, and read this before extending it.** The recorded ids are wave 12 `103d61c4…`,
wave 13 `62334f10…`, wave 14 (gate) `084e3485…`, wave 15 `df3a867d6fc74204a0dbfc75060b9a96`.
**Wave 14's `exe_floor` never recorded one** — `af-fit` writes no landing — so it cannot be listed.

> **The first version of this file listed `a2693968` here. That is wave 15's `TestApp.dll` **sha256**, not its
> `BuildId`.** A novelty list mixing a file hash with build ids can never fire on the entry it believes it
> guards — **F66's exact shape, committed into the document that warns about it.** Keep dll hashes in a
> separate, labelled table, and have the scorer fail loudly if a `BuildId` field ever matches a known dll hash.

**Budget — and price from the same INSTRUMENT, not merely a representative population.** `optimize --per-run
--max-evals 250` costs ~5.2 m/run on the gate's mixed real+synthetic set and ~2.6 m/run all-synthetic (the gate
is ~42 m; wave 15's 40-run all-synthetic pair was 1 h 42 m). **`af-fit` is a different order of cost and those
rates do not transfer to it**: it detects each frame ONCE and then evaluates four budgets on those points, so a
**39-run rung is ~11 m** (wave 14 measured 11 m 06 s). Applying the `optimize` rate to an `af-fit` rung
over-prices it by roughly an order of magnitude.

---

## 6. HOW TO RUN A WAVE

**A subagent's background processes die with its session and tool timeouts cap at 10 minutes, so YOU run every
measurement arm.** Agents read, write, design and analyse. **Only one `TestApp.exe` at a time.**

1. Spawn a **PRE-REGISTRATION** agent. It writes `docs/…-wave<N>-design.md` and `plans/…-wave<N>-plan.md` with
   every rule, threshold and validity gate fixed, plus drivers under `D:\hf_w<N>\` (LF endings). **Ask it to
   judge its own items' legitimacy and to say where your framing is wrong** — waves 14 and 15 both had the
   controller overruled on substance, correctly.
2. **You commit the pre-registration before any measurement.**
3. **You build** into `D:\hf_w<N>\exe`, record sha256 + `BuildId`. Never rebuild mid-wave.
4. **You run the gate** and score it. A partial reproduction stops the wave.
5. **You run the arms**, sequentially, in background with an `until`-loop wait.
6. Spawn an **ANALYSIS** agent. It applies the pre-registered rules and **must not re-decide one** — an
   unsatisfiable rule is a finding.
7. Spawn a **CODE** agent if the wave ships anything. **Every new test must be shown to fail against the
   pre-change source** — or, for new API where a revert fails the build, against a **named mutant**, and you
   verify that claim yourself.
8. **You run the full suite**, verified by COUNT, baseline **3781**. Name every added test.
9. **You commit and push**, append the wave's section to PR #191, and verify CI by COUNT read out of the log.

**Commit with the privacy email:**
```
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
```
Build: `dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w<N>\exe'`
Tests: `dotnet.exe test "$(wslpath -w <abs>/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`
Banks: `D:\SyntheticAutofocusBank` (20 datasets, `renderRequest.OptimalFocuserPosition` = **truth**),
`D:\Autofocus Bank` (19 runs).
