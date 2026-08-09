# Synthetic AF bank — followups wave 13 (plan)

Design: [`docs/synthetic-af-bank-followups-wave13-design.md`](../docs/synthetic-af-bank-followups-wave13-design.md).
**RULE G13 and RULE M13 (D0–D5 + the ship rule) are fixed in the design and this plan does not restate them
loosely — it executes them.** This file is committed **before any measurement**, so the pre-registration is in
git history before the data.

Artifacts root: `D:\hf_w13\`. Nothing in `D:\hf_w12\`, `D:\hf_w11\`, `D:\hf_w10\` or `D:\hf_w7\` is rebuilt,
overwritten or deleted (F53(c)).

---

## Step 0 — done before this file was committed

- [x] PR #190 **VERIFIED MERGED** (`f9f2074`, 2026-08-09T13:24:54Z); `develop` is at it.
- [x] `D:\hf_w13\exe` built from `develop` @ `f9f2074`. `TestApp.dll` sha256 `c4bf3280…`;
      `NINA.Joko.Plugins.HocusFocus.dll` sha256 `4c057544…`; `strings … | grep AtrousWaveletFast` hits ⇒ v2.
- [x] `D:\hf_w13\profiles_before_w13.txt` — the profile set snapshotted **before** any arm.
- [x] Feasibility smoke of `af-fit` on `D:\hf_w12\ladder\t8\D05_tec140_1000mm\attempt01` — a wave-12 ladder rung
      that is **in neither population**, chosen precisely so a feasibility check cannot contaminate D1. It
      confirmed: the budget table prints, `--settings` is honoured on a folder with no saved detection JSON,
      `minPos` prints at `G6`, and the runner exits 0.
- [x] Branch `ghilios/synthetic-af-bank-followups-wave13`.

## Step 1 — commit the pre-registration (this file + the design)

Commit message states that no measurement has run yet. **Nothing below starts before this commit exists.**

---

## Step 2 — RULE G13, the gate

`D:\hf_w13\gate_w13.sh`, modelled on `D:\hf_w12\gate_w12.sh`, with **both pins named in the script header** and
the same refuse-to-guess process check (`grep -c` returning `0` *and* exit 1 is how F55(c) got its false SAFE).

```
TestApp.exe optimize --per-run --runs <bank>\<run> --out D:\hf_w13\gate\<run> \
    --max-evals 250 --settings D:\hf_w11\pinned_settings_w11.json \
    --profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382
```

5 real (`toml999`, `CWhiteFocus`, `uneven`, `muggsie`, `mccomiskey`) + 3 synthetic (`D18`, `D19`, `D20`),
**sequential**, machine quiet.

Score: `python3 /mnt/d/hf_w12/score_w12.py 'D:\hf_w13\gate' --rule G13` → `D:\hf_w13\gate_score.txt`.

- **PASS** = 8 of 8 to 6 dp. Report bit-identity separately as the stronger observation.
- **FAIL** = any miss ⇒ **STOP the wave** and investigate the coordinate system before running item 1. A partial
  reproduction is a failure.
- Read `ConcurrencyCheck` **across the arm**; `BuildId` must differ from `103d61c4…`.

> **Before the gate runs, snapshot every bank run's current `optimized_settings.json`** into
> `D:\hf_w13\bank_settings_snapshot\` (39 small files, seconds). This wave is the last one that has to suffer
> F15, and it should at least not lose anything to it. Record that the gate and I4/I5 will overwrite the
> run-folder copies, and that **no instrument in this wave reads them** (`bank-verify` runs at `C0@nc4`, not
> `--opt-a/--opt-b`; nothing calls `golden eval --params optimized`).

---

## Step 3 — Item 1, in instrument order

### 3.1 Write S1 and record it

`D:\hf_w13\pinned_settings_w13_mor1.json` = S0 with `"MaxOutlierRejections": "1"`. **Verify by diff that exactly
one line differs**, and record both md5s. A settings sweep that changed two things is not a settings sweep.

### 3.2 I1 — `af-fit` over the 20 synthetic datasets (PRIMARY)

Per dataset `D:\SyntheticAutofocusBank\<d>\attempt01`:

```
TestApp.exe af-fit --af-run <d>\attempt01 --settings <S0> --profile-id <astrodet> \
    --confidence 0.95 --weighted true --step-size <spacing from the frames> \
    --out D:\hf_w13\affit_syn\<d>
```

`--step-size` is the sweep spacing read from `synthetic_meta.json`'s own `sweep.positions` (all 20 are 9-point
symmetric sweeps; the value must also equal `expectedOptimal.stepSizeSteps` — **cross-check both and fail loudly
if they disagree**). S0 vs S1 is irrelevant here and S0 is used for both budgets: **the budget table evaluates
0..3 on ONE detection pass**, which is what makes I1 perfectly paired.

Score with `D:\hf_w13\score_affit_w13.py`:
- parse the budget table out of `af_fit_summary.txt` (budget, model, #rej, rejected positions, σ_focus, redχ²,
  R², `minPos`);
- `truth` = `renderRequest.OptimalFocuserPosition`, `step` = sweep spacing;
- `e_b = |minPos_b − truth| / step`;
- report **the tie count first** (datasets where budget 1 rejected nothing), then D1(a) and D1(b).
- **The scorer must have a state for "could not look"** distinct from "no difference" and distinct from a
  hard-floor failure — wave 12's scorer was wrong twice by conflating exactly those (a dataset whose fit failed,
  a dataset where the product declined, and a dataset where nothing was rejected are three different facts).
- **Prove the scorer can fail**: perturb one parsed `minPos` by 1 step in a self-test and require the verdict to
  flip. A comparator that cannot fail is not a comparator.

### 3.3 I2 — `af-fit` over the 19 real bank runs

Same command shape against `D:\Autofocus Bank\<run>`, **without** `--settings` binding the detector: each real
run has its own saved `*_star_detection_result.json` and `af-fit` prefers it. That is the product-faithful
choice and, because I2 compares budgets **within** one run on one point set, the detector choice cancels
exactly. `--step-size` from the frame spacing, explicitly.

Quantities: fire rate (how many of 19 reject at budget 1); vertex displacement
`|minPos_1 − minPos_0| / step`; Δσ_focus; ΔR²; and **`|rejected_position − minPos_0| / step`** — F45's specific
claim is that the rejected point is the *in-focus* one, and this is the number that says whether that
generalises.

### 3.4 I3 — `bank-verify` ×2

```
TestApp.exe bank-verify --runs 'D:\Autofocus Bank' --out D:\hf_w13\bv_mor0 --nc-sweep 4 \
    --settings <S0> --profile-id <astrodet>
… and the same with <S1> into D:\hf_w13\bv_mor1
```

Compare with `D:\hf_w13\compare_bv_w13.py` (start from `D:\hf_w12\compare_bv_w12.py`, which already knows the
report shape and the excluded-field list):
- **D0 first.** `recallHigh`, `recallAll`, `precision` identical to full double precision on all 19 × both arms.
  If not ⇒ **the wave reports no decision on item 1** and says why.
- then D2: `sigmaFocus`, `afR2`, `afChi`; then D3: `sStars`, `sR2`, `sRMS`, `sChi`, `sTheta`.
- Newtonsoft writes NaN/Infinity as the **strings** `"NaN"`/`"Infinity"` — use `score_excess_w12.py`'s `num()`.
- The comparator gets the same 1e-12 perturbation self-test as in 3.2.

### 3.5 I4 — `optimize --max-evals 1` ×2 over the 39-run population

Both banks, all 39 runs, sequential, pinned both ways, `--max-evals 1` so **no search runs** and the recorded
`BaselineJ` / `BaselineSigmaFocus` are the seed evaluation: identical frames, identical detector settings, only
the fit differs. `--out D:\hf_w13\pop_mor{0,1}\<run>`.

Known hard cases, from the register, so they are not read as this wave's news: `lumos` exits rc=3 reproducibly;
`astrodet` the DATASET is frameless (F14); `Panos` has a degenerate sigma fit; `D17_cdk14_oiii5` is a starvation
extreme. Each is recorded as **UNEVALUATED**, never as 0.

### 3.6 I5 — the landing at S1 on the 8 gate runs

Identical to step 2 but `--settings <S1>`, into `D:\hf_w13\land_mor1`. **MOR=0's half is the gate arm itself**,
so the marginal cost is one 8-run pass. Compare the landed knob vector, `BrightnessSensitivity`,
`RecommendedStep` and `ChangedParams`. **`BestJ` is NOT compared across arms** (D4's reason), and the scorer
must refuse to print a cross-arm `BestJ` delta so nobody quotes one later.

### 3.7 Decide

Apply RULE M13 exactly as written. Write the verdict — **including "no change", which is pre-registered as a
first-class result** — and, if D1 and D3 disagree, record the asymmetry rather than a compromise.

---

## Step 4 — Item 2, the app confirmation (HARD BOUND 60 MINUTES)

1. `tasklist.exe | grep -i nina` ⇒ if running, close it and **verify it is gone** before building.
2. Build the plugin; then **compare the DLL's timestamp AND sha256 in NINA's plugin folder against the freshly
   built one**. If they differ, the xcopy failed silently — that is the whole defect, and the wave records it.
3. Launch with `Start-Process` (not the MCP `launch_executable`), connect the **simulator camera first**.
4. Walk A1–A9 from the design's table, recording per item: **confirmed / not confirmed / could not reach**.
5. On the bell: stop, and write the pre-registered blocked-report — what was tried, what failed, which of A1–A9
   remain unconfirmed **by name and by count**. "Not this wave's item" is not an available outcome.

---

## Step 5 — Item 3, F15

1. `--update-run-folder` opt-in in `OptimizationDiagnosticRunner`: the per-run source-folder writes
   (`optimized_settings.json` **and** its `hocusfocus_star_detection.json` sibling) happen **only** with the
   flag; the `--out` copy is unconditional and unchanged.
2. When the flag IS given and a file is already there, **snapshot it** beside itself before overwriting.
3. The console says which of the two it wrote, every time — an absent write must be visible, not inferred.
4. Tests: (a) without the flag, no run-folder write; (b) with the flag, the write happens **and** the previous
   file survives under its snapshot name; (c) the `--out` copy is written in both cases. Each test must be shown
   to fail against the current behaviour before it is called a test.
5. Update F15's register entry, and name the `--opt-a/--opt-b` + `golden eval --params optimized` interaction as
   the reason the capability is kept rather than deleted.

---

## Step 6 — close the wave

1. **Full suite, verified by COUNT, not by tick** (F37). `develop`'s baseline is **3744**; any delta is named
   test by test. Nothing piped to `tail` (it masks the exit code). The known flaky
   `SendAsync_WritesOnABackgroundThread` is not chased on a full-suite run.
2. `docs/synthetic-af-bank-followups-wave13-results.md`, with the provenance banner, every rule's verdict, and a
   **§ on what was NOT run and what it would cost**.
3. New findings flagged in `docs/followups.md`; F15 and F57(d) updated; F45 updated with whatever M13 says about
   it; the "waves 5–12 measured at a non-default `MaxOutlierRejections`" fact recorded wherever the register
   describes the pinned file.
4. PR against `develop` (never push `develop`), commit with the privacy email, **CI verified by test COUNT read
   out of the log**, not by its green tick.

---

## Traps carried into this wave, kept where they will be read

- `ConcurrencyCheck == "exclusive"` on a **single** landing is not evidence the machine was quiet.
- The profile set is machine state **and it moves**; it moved again between wave 12 and this one.
- Do **not** budget fan-out at 4×; it is 1.33× (F60), and a pinned arm cannot fan out at all.
- Newtonsoft writes NaN/Infinity as **strings**.
- "Could not look" needs its own state **at every level, including the scorer's**.
- `D17_cdk14_oiii5` finds ZERO stars at 0.5 s and 2 s — a starvation extreme, useless as a measurement.
- The console "Optimization complete" line is not reliably emitted; `aggregate_summary.json` is the instrument.
- Never rebuild an arm's directory mid-wave (F53(c)).
- **A control that cannot fail is not a control**, and "identical" is the easiest way for one to hide.
- **The flagged-but-deferred site was the one that mattered** — wave 11's "×2" was the sensor model.
- **Measure the payoff you are buying, not the one you assumed.**
- **Fix the rule before the data, and on the bar the predecessors faced.**
- Say what was not run, and price it.
