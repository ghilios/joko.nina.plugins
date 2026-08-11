# Synthetic AF bank — followups wave 19 (implementation plan)

Design / pre-registration:
[`docs/synthetic-af-bank-followups-wave19-design.md`](../docs/synthetic-af-bank-followups-wave19-design.md).
**Read it first. Every threshold, branch and denominator lives there and none of them is re-decided here.**
Charter: [`docs/waves17-21-handoff-prompt.md`](../docs/waves17-21-handoff-prompt.md).

> **The two things to keep in your head while executing this plan.**
>
> **1. This wave deliberately does NOT run the 8-minute out-of-sample pass over wave 18's 40 arm landings**, and
> there is no driver in `D:\hf_w19` that can. If you find yourself writing one, stop and read design §3: it is
> not a budget cut, it is a refusal to spend a population that a later wave can still decide with. The wave uses
> ~2 of its 6 hours; the spare time is not a reason to look.
>
> **2. `exit=0` means the wrapper exited, not that the arm ran** (wave 17 §7.1). Open the log. And wave 18's
> `part2_w18.patch` is on disk while the brief's `git status` claimed its files were already modified — they are
> not, HEAD is `28f02ce` and the tree is clean, but `gate_w19.sh` and `redo_w19.sh` both assert the seed value
> out of the `PARAMS-DUMP optimize/seed` block and will refuse. The refusal only helps if you read it.

---

## 0. Artifacts already written, and their self-test status

| path | what | `--self-test` |
|---|---|---|
| `/mnt/d/hf_w19/prov_w19.py` | RULE G19's free controls as FIELDS, read across the arm. **Wave 18's, carried forward**; only `PRIOR_BUILD_IDS` (now **nine**) and `KNOWN_DLL_SHA256` (now including wave 18's two binaries **and the byte-identical apphost**) changed | two directions, on the real arm + a mutated `copytree`, mutation asserted by read-back |
| `/mnt/d/hf_w19/gate_w19.sh` | RULE G19, with the population assertion, **three** fingerprint preconditions, the three-dump-block count, and `G19-P1`'s `seed == 3` assertion | n/a (driver) |
| `/mnt/d/hf_w19/arm_fingerprint_w19.py` | **NEW third control class**: wave 18's 40 arm landings + 8 gate landings | **4 of 4** — unchanged, content-changed, removed (a *population* failure, reported separately), missing root (COULD-NOT-LOOK, not "unchanged") |
| `/mnt/d/hf_w19/redo_w19.sh` | RULE R19's arm, 20 synthetic datasets, `G19_PASSED` interlock, `EXECUTED_DATASETS` written **before** the first run | n/a (driver) |
| `/mnt/d/hf_w19/score_r19_w19.py` | RULE R19, `--arm-g19-passed` | **19 of 19** — every branch of the R19 table, the primitives, the empty-set cases, and the "two roots share a BuildId" FAIL |
| `/mnt/d/hf_w19/convert_landing_w19.py` | wave 15's converter **copied forward** (wave 15's file is NOT edited) + F71(c)'s two-copy printing | **11 of 11**, including four new F71(c) assertions |
| `/mnt/d/hf_w19/probe_w19.sh` | RULE C19-D2's live demonstration, mutant asserted by read-back **and** the snapshot asserted unchanged | n/a (driver) |
| `/mnt/d/hf_w19/score_c19_w19.py` | RULE C19, `--retro` (free) and `--probe` | **9 of 9** — inverted pair rejected, `\|D\|=0`, `\|D\|=1`, missing dump, missing snapshot (**no fallback to `Options`**), mismatched snapshots, unaliased field named |
| `/mnt/d/hf_w19/f21_probe_w19.sh` | item F, rule-free, `timeout 1200` | n/a (driver) |

Reused unchanged: `/mnt/d/hf_w12/score_w12.py` (takes `--rule G19` as a label; the eight values are its own
constants), `/mnt/d/hf_w15/bank_fingerprint_w15.py`, `/mnt/d/hf_w17/aux_fingerprint_w17.py`.

**Deliberately absent: there is no `affit_w19.sh`.** Design §3.4.

All new files are **LF, ASCII-only** (verified: 0 non-ASCII bytes, 0 CRLF, all `py_compile` / `bash -n` clean).
All scorers take **WSL** paths and abort on a backslash.

**Already run at pre-registration time, and quoted in the design:** `score_c19_w19.py --retro` (|D| = 8, good
8/8, mutant 0/8), `convert_landing_w19.py` against wave 18's inputs (5 `BOTH-DIFFER`, 6 `SNAP-ONLY` of 25), and
the gate-vs-`seedA0` 33-key diff for `D18/D19/D20` (3 of 3 identical). None of them touched an arm.

---

## 1. Task list, in the fixed order of the design's §12

### T0 — commit the pre-registration
`docs/synthetic-af-bank-followups-wave19-design.md` + `plans/synthetic-af-bank-followups-wave19-plan.md`, with
the privacy email. **Before any build.** The drivers under `D:\hf_w19\` are not committed (no wave has committed
them); they are reproduced in the results doc's §Reproduce.

### T1 — item D (code)
Spawn a code agent with the design's §5 as its brief. **Nothing in this item may touch
`HocusFocusStarDetection.cs`'s `BuildDefaultStarDetectorParams` literal** — that is F70(a) and it is not this
wave's question.

1. **F69(b)** — in `OptimizationDiagnosticRunner`, emit a second `ParamsDump.Write` **after**
   `ApplyRunDetectionBinningIfRequested`, tag **`optimize/detected`**.
   - The tag **must contain no existing tag as a substring** (`optimize/baseline`, `optimize/seed`,
     `af-fit/detector`), so a prior wave's `grep -l "PARAMS-DUMP optimize/baseline"` cannot double-count it.
   - Add the constant beside `ParamsDump.OptimizeBaseline` / `OptimizeSeed`; `ParamsDump.Write`'s `\S+`
     BEGIN/END backreference contract is unchanged.
   - **Test**: the block exists, and on a run whose resolved detection-binning factor is not 1 its
     `DetectionBinning` **and** `PixelScale` differ from the pre-mutation block. *A dump identical to the one
     above it is a dump that has not been demonstrated.*
2. **F69(c)** — print a one-line notice when `--apply-run-detection-binning` is passed, saying it is an accepted
   no-op and that the behaviour has been on by default since wave 8. Test it.
3. **F70(b′)** — delete the preset-owned dead literals from `StarDetectionOptions.ResetDefaultsImpl`.
   - The shared set must be **derived from source** (the properties `DerivePresetSettings` assigns) and printed
     in the commit message. **Do not guess it.**
   - One parametrized test: for each name in the shared set, perturb the property, call `ResetDefaults()`,
     assert the derived value. **Any literal the test cannot cover STAYS.**
   - Demonstrate red against named mutant **M-R1**: *remove the unconditional `ConfigureSimpleSettings()` at the
     end of `ResetDefaultsImpl`*.
4. **The manual** — `documentation/docs/settings/preprocessing.md`: the table at **:23** (`| ... | 3 |` → `4`)
   **and the prose at :41** (`**Default:** \`3\`` → `4`) — **both**, each with a clause naming the Typical
   preset's 3 plus the `+1` hotpixel compensation at `StarDetectionOptions.cs:221-224`. **Unconditional**, and
   design §5.4 is why it is not evidence about F70(a). *Two lines, and wave 18's Part 2 patch touched only one
   of them — check the patch before assuming it is a template.*
5. Run the suite. Record the count and name every added test and every changed assertion.

**The controller verifies the mutant claims itself** (`git stash` the product change, run the named test, see
it red, restore). *A code agent asserting a test fails is not evidence.*

### T2 — build ONE binary
```
dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w19\exe'
sha256sum /mnt/d/hf_w19/exe/TestApp.dll /mnt/d/hf_w19/exe/NINA.Joko.Plugins.HocusFocus.dll
```
Record **both dll** hashes. **Do not hash `TestApp.exe`** — it is the apphost, and wave 18's was byte-identical
across two genuinely different binaries. The `BuildId` is **read from the first landing**, not guessed.
Expected cost **~25 s**, not ~6 m (wave 18 measured 20.62 s and 21.96 s).

### T3 — the BEFORE fingerprints, **all three classes**
```
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write /mnt/d/hf_w19/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --write /mnt/d/hf_w19/bank_aux_fingerprint_BEFORE.json
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --self-test
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --write /mnt/d/hf_w19/w18_arm_fingerprint_BEFORE.json
```
42 bank landings, 59 aux files (`harness_settings.json` ×39 + `synthetic_meta.json` ×20), and **48 prior-wave
landings** (`seedA0` 20 + `seedA1` 20 + wave 18's gate 8). The third writer **refuses to bless a short
population**. Every driver aborts without all three.

### T4 — RULE G19  (~42 m)
```
bash /mnt/d/hf_w19/gate_w19.sh > /mnt/d/hf_w19/gate_w19.log 2>&1
```
Background, with an `until`-loop wait. **Then read the log**, not the exit code.
```
python3 /mnt/d/hf_w12/score_w12.py /mnt/d/hf_w19/gate --rule G19
python3 /mnt/d/hf_w19/prov_w19.py  --self-test /mnt/d/hf_w19/gate
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w19/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w19/bank_aux_fingerprint_BEFORE.json
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --check /mnt/d/hf_w19/w18_arm_fingerprint_BEFORE.json
```
**A partial reproduction stops the wave, and F69(b) is the first suspect (design §1.3).** Only after all five
pass:
```
python3 /mnt/d/hf_w19/score_r19_w19.py --arm-g19-passed /mnt/d/hf_w19/gate
```
which re-reads the gate — 8 landings, and all 8 seed dumps at `NoiseReductionRadius=3` — and writes
`G19_PASSED`.

Also record, from the driver's own output: **how many of the 8 logs carry `PARAMS-DUMP optimize/detected`**.
That is F69(b)'s arm-level evidence. Reported, never a bar.

### T5 — RULE C19, the corrected conversion control  (~1 m)
```
bash /mnt/d/hf_w19/probe_w19.sh > /mnt/d/hf_w19/probe_w19.log 2>&1
python3 /mnt/d/hf_w19/score_c19_w19.py --self-test
python3 /mnt/d/hf_w19/score_c19_w19.py --retro --probe /mnt/d/hf_w19/probe
```
Must print `TOOK-CORRECTLY` for **both** `C19-D1` (retro, wave 18's saved probe) and `C19-D2` (live), then write
`C19_PROBE_PASSED`. **Read the marker file's body**: it says what it licenses and, more importantly, what it
does not.

**This is run before the 55-minute arm on purpose**, so a failure is known in one minute rather than in an hour.

### T6 — RULE R19's arm  (~55 m)
```
bash /mnt/d/hf_w19/redo_w19.sh > /mnt/d/hf_w19/redo_w19.log 2>&1
```
The driver asserts 20 landings against `EXECUTED_DATASETS`, that every `optimize/seed` block reads **3**, that
every `optimize/baseline` block reads **4**, and re-checks all three fingerprints. **Read the log.**

### T7 — score RULE R19
```
python3 /mnt/d/hf_w19/score_r19_w19.py --self-test
python3 /mnt/d/hf_w19/score_r19_w19.py --new /mnt/d/hf_w19/reA0 --old /mnt/d/hf_w18/seedA0 \
        --gate /mnt/d/hf_w19/gate --out /mnt/d/hf_w19/r19_score.txt
```
**Apply the branch table as written.** An unsatisfiable clause is a finding, not an invitation to restate it.

### T8 — the branch's consequence
- **`R-DETERMINISTIC`** ⇒ the register records that [F8](../docs/followups.md) is **bounded** at n = 20 on 33
  fields under `--settings` + `--profile-id` pinning, that wave 18's 40 landings are a **measurement**, and that
  the design's §3.6 item is **live for wave 20 at ~8 m**.
- **`R-J-ONLY`** ⇒ the register records that the eight-value gate is a control on **one field**, that every
  cross-wave landing comparison carries an unmeasured error term, and that wave 18's 40 landings are a
  **sample** — which kills the §3.6 item and closes F70(a) on the source argument alone. **This is the large
  finding, and it needs its own register entry.**
- **`R-NOT-DETERMINISTIC`** ⇒ as above and worse; name which datasets moved `J`, and note that `D18/D19/D20`
  were separately checked against K8 by RULE G19.
- **`R-UNEVALUATED`** ⇒ name the failing validity clause. If it is `R19-V2` or `R19-V3`, say **which mistake**
  (wave 18's binary, or wave 18's patch applied) — those are the two the clauses exist for.
- **`R19-A < 1.000`** ⇒ the pre-registered disambiguation, **at most five** datasets, on
  `/mnt/d/hf_w18/exe_b1` into `/mnt/d/hf_w19/reA0_b1` (~2.7 m each). Re-verify that binary's dll sha256 against
  `3c5b2a5c…` / `08aa0809…` **before** using it. More than five: record and stop.

### T9 — item F, optional tail (≤ 20 m)
Only if the wave is under 4 h at this point.
```
bash /mnt/d/hf_w19/f21_probe_w19.sh > /mnt/d/hf_w19/f21_w19.log 2>&1
```
Rule-free. Record the wall time, the round count, and the half-width / recommended step per round. **A timeout
is the deliverable, not a failure.** If it is dropped, say that the instrument has now been unpriced for three
consecutive waves.

### T10 — the suite, on the final tree
`dotnet.exe test ... -c Debug --nologo`, verified by **COUNT** (F37). Baseline **3831**. Name every added test.
*Do not pipe to `tail` — it masks the exit code.* `SendAsync_WritesOnABackgroundThread` is a known flake and is
not chased on a full-suite run.

### T11 — commit, push, PR, CI
Append the wave's section to PR #191, findings first. Verify CI by **COUNT read out of the log**.

---

## 2. Progress counting — and it has a could-not-look state

Wave 17's controller reported two fully successful arms as failures because its improvised counter looked for
the wrong filename in the wrong directory and **could not say so**. The exact commands, with the state:

```bash
d=/mnt/d/hf_w19/gate
if [ ! -d "$d" ]; then echo "COULD-NOT-LOOK: $d does not exist"
else echo "gate: $(find "$d" -mindepth 2 -name aggregate_summary.json | wc -l) of 8"; fi

d=/mnt/d/hf_w19/reA0
if [ ! -d "$d" ]; then echo "COULD-NOT-LOOK: $d does not exist"
else echo "R19 arm: $(find "$d" -name optimized_settings.json | wc -l) of $(wc -l < "$d/EXECUTED_DATASETS")"; fi

d=/mnt/d/hf_w19/probe
if [ ! -d "$d" ]; then echo "COULD-NOT-LOOK: $d does not exist"
else echo "probe: $(find "$d" -name af_fit_summary.txt | wc -l) of 2"; fi
```

The filenames are `aggregate_summary.json`, `optimized_settings.json` and `af_fit_summary.txt`. **They are
different for the three instruments; that is what caught wave 17.** Note the R19 denominator comes from
`EXECUTED_DATASETS`, so the counter cannot report a planned drop as a failure either.

---

## 3. Trap checklist, per step

| trap | where it bites here | the guard |
|---|---|---|
| Windows paths to a scorer | every scorer | all take WSL paths; `prov_w19.py`, `score_r19_w19.py`, `score_c19_w19.py`, `arm_fingerprint_w19.py` and `convert_landing_w19.py` all abort on a backslash |
| `TestApp` eats a `while read` loop's stdin | every loop in all four drivers | `< /dev/null` on every invocation |
| a short population reading as a null | all four drivers | population asserted **in the driver**, before any scorer; R19's denominator is `EXECUTED_DATASETS`, written before the first run |
| "could not look" with no state | scorers, fingerprints, the progress counter | three states everywhere; the guard precedes every field read; §2 above |
| `strings` is two instruments | the detector check | `DetectorVersion` is read as the **FIELD**; `strings -el` appears nowhere |
| a landing is not a settings file | the probe | `convert_landing_w19.py`, through the production Accept path, with a probe in both directions |
| **a converted settings file has TWO copies of every knob** | RULE C19 | the scorer reads `Options["OptimizedSettingsJson"]` and **refuses to fall back** to `Options[<knob>]`; corroboration on ≥ 3 fields; the converter prints both copies (F71) |
| **an `optimize` log now has THREE copies of every knob name** | `G19-P1`, `R19-V3` | the new tag is `optimize/detected` (no substring collision); every read is an `awk`/regex BEGIN…END range |
| `UseAdvanced=False` makes presets overwrite advanced knobs | the converted settings file | the converter sets `UseOptimizedSettings=True` and **aborts** on `UseAdvanced=True`; the mutant proves the flag is load-bearing |
| non-ASCII in a parsed file | `/mnt/d/hf_w19/*` | verified ASCII-only, LF |
| one `TestApp.exe`, no NINA | every arm | counted with the refuse-to-guess form in all four drivers |
| no fan-out | every arm | every arm is `--profile-id`-pinned and cannot fan out |
| rebuilding an arm's directory mid-wave | T6 in particular | every driver aborts on a populated output root, and **nothing writes under `/mnt/d/hf_w18`** |
| `"NaN"` is a string | `R19-A`, `R19-B` | `repr()` equality: the string `"NaN"` never equals the float, and a NaN column is not a permanent false difference |
| `Panos` UNEVALUATED **by name** | not in this population | the 20 synthetic datasets only; `Panos` is a real-bank run |
| `D17_cdk14_oiii5` finds zero stars at short exposures | it is **last** in the order, and it is item F's dataset | if it degenerates in R19 it is COULD-NOT-LOOK by name and out of the denominator, which is re-printed; in item F it is rule-free anyway |
| pricing one instrument from another | the budget | three rates, kept apart: `optimize` mixed 5.2 m/run, `optimize` all-synthetic **2.73 m/run (measured, wave 18)**, `af-fit` ~11 s/dataset |
| a stale `git status` | T1, T2 | HEAD is `28f02ce`, tree clean, **verified**; `part2_w18.patch` must NOT be applied; `G19-P1` and `R19-V3` catch it if it is |

---

## 4. What the results doc must contain

- The PROVENANCE block with the **dll** sha256 pair and the `BuildId`, the explicit statement that **one**
  binary was built and design §11.3's reason, and the three fingerprint classes with their counts (42 / 59 / 48).
- RULE G19 clause by clause, with `G19-P1`'s measured value, and the statement that its PASS **is** the
  measurement that item D is inert.
- RULE R19 clause by clause with the five-part statement beside each, the branch table as applied, the verdict,
  and — whatever the verdict — **`R19-A` and `R19-B` printed side by side**, because their difference is the
  number the wave exists to produce.
- RULE C19's two demonstrations with `|D|` and both counts, and the explicit note that `C19_PROBE_PASSED` was
  written and **not consumed**.
- **§3's decision, restated as a result**: (a) refuted by measurement, (b) refused as irreversible, (c) taken —
  and F70(a) recorded as **not decidable by measurement on this bank**, escalated to the owner with wave 14's
  `MaxOutlierRejections` precedent named, and the wave-20 item priced at ~8 m with its precondition.
- The register corrections this wave owes: the `af-fit/detector` dump carries **55** fields, not the 56 wave 18
  and F71 both state; and the manual line, with the note that wave 18's revert left a documentation bug its own
  Part 1 had created.
- The budget table, estimate against actual, per instrument — including the build at ~25 s against wave 18's
  ~6 m guess.
- §10's "what was not run" list, carried forward with actuals, **with the 8 unspent minutes at the top and the
  reason in one sentence**.
- If `R-J-ONLY` or `R-NOT-DETERMINISTIC`: **a loud, top-level note that every cross-wave landing comparison in
  this series carries an unmeasured error term**, and that the §3.6 wave-20 item is dead.
