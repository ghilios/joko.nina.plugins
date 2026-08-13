# Wave 27 — execution plan

**Design (the authority): `docs/synthetic-af-bank-followups-wave27-design.md`.** This file is the controller's
step-by-step. Where the two disagree, the design wins and the disagreement is a finding.

**Vessel:** PR #195 MERGED → `git checkout develop && git pull && git checkout -b ghilios/synthetic-af-bank-followups-wave27`
off `develop @ e19a779`.

**Hard stop: 2026-08-14T00:45:17Z.** Budget **~5 h against ~11.5 h remaining** — **time is NOT the binding
constraint, so do not cut scope for it.** Cut only what the evidence does not support. If it overruns anyway the
order is fixed: (D) → (A)'s build half; **(B) and (C) are never cut.**

**Measured rates, and they are new to this series** — the charter's §5 budget table has no `golden eval` row at
all: the **full 20-dataset arm is 349 s (~6 m)**; a cell ranges **6 s (`D06`) to 46 s (`D01`)**, mean ~17 s.
**Price a 7-cell arm from the RANGE, not the mean** — `D01` is 7.7× `D06`.

**Record ACTUAL against ESTIMATE in every step's row of `/mnt/d/hf_w27/TIMING.tsv`** (`step`, `est_min`,
`actual_min`, `note`), written by the controller as each step closes. The estimate/actual pair is itself a
deliverable ([F21](../docs/followups.md)).

---

## Step 0 — commit the pre-registration BEFORE any measurement (est 10 m)

```bash
cd /home/ghilios/src/hocus-focus
git checkout develop && git pull && git checkout -b ghilios/synthetic-af-bank-followups-wave27
git log --oneline -1                       # must be e19a779
git add docs/synthetic-af-bank-followups-wave27-design.md \
        plans/synthetic-af-bank-followups-wave27-plan.md \
        docs/wave27-register-correction-survey.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "W27 pre-registration: backlog item 1 SHIPPED on 2026-08-03 -- the wave audits the correction instead"
git push -u origin ghilios/synthetic-af-bank-followups-wave27
```

**Nothing below runs until this is pushed.**

---

## Step 1 — BLOCKING pre-flight (est 10 m)

### 1.1 Derive the instruments

```bash
mkdir -p /mnt/d/hf_w27
cd /mnt/d/hf_w27
sed -e 's/w26/w27/g' -e 's/W26/W27/g' -e 's/_26/_27/g' -e 's/\b26\b/27/g' \
    /mnt/d/hf_w26/verify_derivation_w26.py > /mnt/d/hf_w27/verify_derivation_w27.py
```

Then **hand-widen** `verify_derivation_w27.py`, and the widening must be **demonstrated**, not asserted:

| what | required |
|---|---|
| `WAVE` | `27`; `PREV` computed as `WAVE - 1`, never typed |
| `KNOWN_BAD_ROOT` | **`"/mnt/d/hf_w26"`**, `KNOWN_BAD_WAVE = 26` — **pinned to a NAMED older root in source.** *A regression fixture that is "whatever came before" has a shelf life of one wave* |
| token pattern | must match an identifier token with **both** an optional `_prefix` **and** an optional `_suffix` (`w26_gate_log_wsl` **and** `G26_START`). **`\b` cannot see `_`** ([F80](../docs/followups.md), three demonstrated gaps) |
| `EXPECTED_PATHS` | rewritten for wave 27: `/mnt/d/hf_w25/exe` (B15, the BEFORE binary), `/mnt/d/hf_w25/table18` (the population), `/mnt/d/hf_w27/exe` (B16, only if (A) builds), `D:\hf_w25\exe` and `D:\hf_w25\table18` (**Windows forms — `TestApp` takes Windows paths**), `/mnt/d/hf_w26` (the pinned FAIL fixture) |
| **interface check** | **verify the PREDECESSOR'S INTERFACE SURVIVED, not just its spelling.** `layout_w27.sh` must expose the same function names the drivers call; wave 25's `layout_w25.sh` exposed a different API from wave 24's and no renaming scheme could have repointed three call sites that no longer existed under any name |

### 1.2 Run it, BOTH directions, and READ THE OUTPUT

```bash
cd /mnt/d/hf_w27
python3 verify_derivation_w27.py /mnt/d/hf_w26 2>&1 | tee /mnt/d/hf_w27/v27_failend.txt   # MUST report drifts
python3 verify_derivation_w27.py /mnt/d/hf_w27 2>&1 | tee /mnt/d/hf_w27/v27_clean.txt     # MUST report none
grep -c 'DRIFT' /mnt/d/hf_w27/v27_failend.txt      # must be > 0
grep -c 'DRIFT' /mnt/d/hf_w27/v27_clean.txt        # must be 0
```

**`RULE V27` = `V-CLEAN` only when the second run reports zero drifts AND the first reports more than zero.**
A pre-flight that passes everything is not a pre-flight. **Read the printed lines, not `$?`** — wave 26's
self-test returned a clean rc from inside its caller's `source` line while doing nothing.

### 1.3 LF endings

```bash
cd /mnt/d/hf_w27 && file *.sh *.py | grep -i crlf && echo "FAIL: CRLF present" || echo "OK: LF only"
```

---

## Step 2 — the shared layout and the manifest (est 5 m)

**`/mnt/d/hf_w27/layout_w27.sh` — write it, and it contains NO `--self-test` dispatcher.** Wave 26's fired
inside its caller's `source` line, terminated it, and printed a clean PASS. The self-test lives in
`layout_w27_selftest.sh`, which sources the layout and runs the checks.

```bash
bash /mnt/d/hf_w27/layout_w27_selftest.sh 2>&1 | tee /mnt/d/hf_w27/layout_selftest.txt
grep -E '^SELFTEST .* [0-9]+ of [0-9]+$' /mnt/d/hf_w27/layout_selftest.txt   # READ THIS LINE
```

**`/mnt/d/hf_w27/t27_manifest.sh` — the driver that DISCOVERS the population and writes the manifest.** The
scorer reads paths **only** out of it and never rebuilds one from a template ([F74](../docs/followups.md)).

```bash
bash /mnt/d/hf_w27/t27_manifest.sh 2>&1 | tee /mnt/d/hf_w27/w27_manifest.log
wc -l /mnt/d/hf_w27/t27_manifest.tsv        # 180 data rows + header
cat /mnt/d/hf_w27/T27_MANIFEST_READY        # written by the DRIVER, last, only on success
```

Manifest columns: `dataset`, `focuser`, `detected_csv`, `fn_csv`, `truth_json`, `golden_json`, `fits`,
`capture_binning`, `derived_det_binning`, `scored_det_binning`, `naxis1`, `naxis2`.
Header lines: `ROOT=/mnt/d/hf_w25/table18`, `NEAR_MISS_ROOT=/mnt/d/hf_w25/table`,
`BANK=/mnt/d/SyntheticAutofocusBank`, `PARAMS_SOURCE=D:\hf_w18\seedA0`.

**Assertions the driver makes, and it writes `T27_MANIFEST_READY` only if ALL pass:**
`180` rows · `20` distinct datasets · exactly `9` frames each · every one of the 20 logs' `params:` line contains
`D:\hf_w18\seedA0\` (`T27-V5`, the path trap) · the root basename is `table18` · every `truth_json` and
`golden_json` exists.

**Use `-print0 | sort -z | xargs -0` for every file sweep.** `xargs` splits on the space in
`/mnt/d/Autofocus Bank`, and a fingerprint written the naive way once recorded 20 landings instead of 42.

---

## Step 3 — the scorer, and its self-test in BOTH directions (est 10 m)

### 3.1 What the controller must write: `/mnt/d/hf_w27/score_t27_w27.py`

**`--out` is MANDATORY. Without it the scorer must `exit 2` and write nothing** — not a warning, a refusal.
Wave 25 lost its scoring artifact twice to a stdout redirect into a job-scoped temp directory.

| flag | behaviour |
|---|---|
| `--manifest PATH` | required; the **only** source of artifact paths |
| `--out PATH` | **required; refuse with rc 2 if absent** |
| `--rule T27` | run `T27-V0`, `T27-V1`, `T27-V3`, S1/S2/S3, and emit the verdict |
| `--protection off` | `T27-V0`'s **FAIL end** and the correction's size, in one computation |
| `--coords native` | `T27-V1`'s FAIL arm |
| `--self-test` | must print `SELFTEST T27 <n> of <n>`; **never dispatched from a sourced file** |
| `--emit-marker` | write `/mnt/d/hf_w27/T27_PASSED` **from the code that computed the verdict**, on `rc == 0` and only then ([F75](../docs/followups.md)) |

**Predicates — mirror the product exactly** (`T27-V0` is what proves the mirror is faithful):

1. **TP** — `GoldenMatch.Match` centre mode: a detection's `(cx, cy)` inside a golden `stars` rect, greedy pairing.
2. **Unresolved exclusion** — `GoldenMatch.ExcludeUnresolved` with the **`Covers`** predicate: centre-in-box
   **OR** `IoU(rect, detection bbox) > 0`. The detection bbox comes from the CSV's `x,y,w,h`. The product keeps
   the dilated predicate here **deliberately**; do not "fix" it.
3. **Truth protection** — `ExcludeProtected` with the **centroid** predicate: Euclidean distance from `(cx, cy)`
   to a truth entry's `(BinnedCenterX, BinnedCenterY)` ≤ `12.0`, for `Tier ∈ {omitted, merged-into}` only.
4. **The null** — `TruthProtection.ShiftForNullControl` verbatim: shift the **DETECTIONS**, not the catalogue, by
   `(317, 211)` with `Wrap(v, extent) = ((v % extent) + extent) % extent`, at `Δ1 = (317, 211)`,
   `Δ2 = (-317,-211)`, `Δ3 = (317,-211)`, `Δ4 = (-317, 211)`. **The rule reads `Δ1`**; the spread over four is
   reported, not thresholded.
5. **`A_all`** — rasterise golden `stars` rects, golden `unresolved` rects and 12 px protection discs onto a
   lattice of stride **4 px**, offset **(2, 2)**; report `A_all`, `A_golden`, `A_protOnly`.

**Column reading, stated so it cannot drift** ([F68](../docs/followups.md) part 5): detections are the CSV's
**`cx,cy`** (the centroid), never `x,y` (the bbox origin); truth positions are **`BinnedCenterX/Y`**, never
`Star.CxPixels/CyPixels`; the reproduction target is each log's **`OVERALL:`** line, not the frames CSV
(re-summing the CSV is a second route and therefore a second bug).

**Empty-set and could-not-look, guarded BEFORE any field read** — the seven states of design §7, each printed by
name. **Newtonsoft writes NaN/Infinity as the STRINGS `"NaN"`/`"Infinity"`**; the reader must handle both.

**Every ordinal and count COMPUTED, never typed:** `19` = `len(datasets) - len(NOT_BLIND)`; `18`/`2` from
`captureBinning`; `7`/`13` from the logs' `NOTE:` lines; `180`/`20`/`9` from the manifest — each **asserted**.

### 3.2 Run the self-test, both directions

```bash
python3 /mnt/d/hf_w27/score_t27_w27.py --self-test 2>&1 | tee /mnt/d/hf_w27/t27_selftest.txt
grep -E '^SELFTEST T27 [0-9]+ of [0-9]+$' /mnt/d/hf_w27/t27_selftest.txt      # READ THE LINE
python3 /mnt/d/hf_w27/score_t27_w27.py --manifest /mnt/d/hf_w27/t27_manifest.tsv --rule T27 ; echo "rc=$?"
#   ^ NO --out: must print a refusal and rc=2, and write NOTHING
ls /mnt/d/hf_w27/t27_score.txt 2>/dev/null && echo "FAIL: wrote output without --out"
```

---

## Step 4 — `RULE T27`, half (B). Zero TestApp minutes (est 65 m)

**Every command passes WSL paths (`/mnt/d/...`). A Windows path yields UNEVALUATED and looks like a failed arm.**

```bash
S=/mnt/d/hf_w27/score_t27_w27.py ; M=/mnt/d/hf_w27/t27_manifest.tsv

# 4.1  T27-V0 reproduction -- PASS end, 20 of 20 exact
python3 $S --manifest $M --rule T27 --out /mnt/d/hf_w27/t27_v0.txt
grep -E 'T27-V0 .* 20 of 20' /mnt/d/hf_w27/t27_v0.txt

# 4.2  T27-V0 FAIL end AND the correction's size, one computation
python3 $S --manifest $M --rule T27 --protection off --out /mnt/d/hf_w27/t27_v0_failend.txt
grep -E 'DIFFERS' /mnt/d/hf_w27/t27_v0_failend.txt        # must be non-empty

# 4.3  T27-V1 coordinate gate, both arms
python3 $S --manifest $M --rule T27 --coords binned --out /mnt/d/hf_w27/t27_v1_binned.txt
python3 $S --manifest $M --rule T27 --coords native --out /mnt/d/hf_w27/t27_v1_native.txt
#      V1-a: bit-identical on the 18 captureBinning=1 datasets
#      V1-b: binned - native >= 0.30 on D11 and D12  <- the FAIL end, RUN and PRINTED

# 4.4  S1/S2/S3, four offsets, 180 frames, and the verdict
python3 $S --manifest $M --rule T27 --full --emit-marker --out /mnt/d/hf_w27/t27_score.txt
cat /mnt/d/hf_w27/T27_PASSED        # written by the scorer, on rc==0 only
```

### 4.5 `RULE R27` — ALREADY RUN. Adopt it, and DECLARE THE DEVIATION (est 0 m)

`R27 = R-IDENTICAL`: B15 regenerates the whole published population byte for byte — **20 cells, 420 files,
0 differences, 0 could-not-look** (`/mnt/d/hf_w27/repro_all_score.txt`, driver `repro_all_w27.sh`, scorer
`score_repro_w27.py` self-testing **6 of 6** with `--out` mandatory, paths through
`repro_all_manifest.tsv`).

**It ran BEFORE pre-registration.** The results document must quote `R-IDENTICAL` and say *"opportunistic, not
pre-registered"* **in the same sentence**. Design §5.6a carries the reasoning for why adopting it is legitimate
(it is a determinism control on the instrument, reads nothing in T27's verdict tree, and has exactly one
interesting outcome) and why the deviation is still declared rather than laundered.

**`R27` is load-bearing for `S27-4`:** without it a B16-vs-`table18` byte difference could be the treatment or
run-to-run noise. With `R-IDENTICAL` in hand, **any difference is the treatment.**

**Only one `TestApp.exe` at a time. Never run NINA during any arm. `TestApp.exe` eats a `while read` loop's stdin
unless you redirect `< /dev/null`.**

---

## Step 5 — `RULE D27`, half (D). B15, no build (est 25 m, ~2 m compute)

```bash
# 5.1 derive the arm driver from the one that made table18 -- then PRE-FLIGHT the derivation
sed -e 's/w25/w27/g' -e 's/table18/table18_b2/g' \
    /mnt/d/hf_w25/table_arm18_w25.sh > /mnt/d/hf_w27/d27_arm_w27.sh
python3 /mnt/d/hf_w27/verify_derivation_w27.py /mnt/d/hf_w27 2>&1 | tee /mnt/d/hf_w27/v27_d27.txt
#    ^ MANDATORY: this is exactly the sed-derivation path F80 exists for
```

Add `--detection-binning 2` to the invocation. Population: the **7** datasets computed from the `table18` logs'
`NOTE:` lines (`D08 D09 D10 D12 D14 D15 D17`), asserted `== 7`; 63 frames asserted. Binary: **B15** at
`/mnt/d/hf_w25/exe`, `TestApp.dll` sha256 `2fb0c8fdb26f11f9c912dce6d3bc3b599e8b9abab7ee4fdd0a61ace0aac42e67`
(**hash the dll, never the apphost** — `TestApp.exe` is byte-identical across six distinct binaries,
[F66](../docs/followups.md)). Pin `--settings` **and** `--profile-id` on every cell.

```bash
bash /mnt/d/hf_w27/d27_arm_w27.sh 2>&1 | tee /mnt/d/hf_w27/w27_d27.log     # ~2 m
grep -c 'derived detection binning' /mnt/d/hf_w27/table18_b2/*.log          # D27-V1: must be 0 on all 7
grep -h 'pixelScale:' /mnt/d/hf_w27/table18_b2/*.log                        # must carry '; detectionBinning 2'

# 5.2 D27-V1 FAIL END, run and printed
/mnt/d/hf_w25/exe/TestApp.exe golden eval --runs 'D:\SyntheticAutofocusBank\D09_c14_3800mm' \
  --params optimized --opt-results 'D:\hf_w18\seedA0\D09_c14_3800mm\attempt01\optimized_settings.json' \
  --match center --pixel-scale header --out 'D:\hf_w27\d27_failend\D09' \
  --settings 'D:\hf_w11\pinned_settings_w11.json' --profile-id 'ce3f3e63-8fd3-4b72-a0ca-d90db9441382' \
  < /dev/null 2>&1 | tee /mnt/d/hf_w27/d27_failend/D09.log
grep 'derived detection binning' /mnt/d/hf_w27/d27_failend/D09.log   # the NOTE MUST return -> FAIL end shown

python3 /mnt/d/hf_w27/score_t27_w27.py --manifest /mnt/d/hf_w27/t27_manifest.tsv --rule D27 \
  --b2-root /mnt/d/hf_w27/table18_b2 --out /mnt/d/hf_w27/d27_score.txt
```

`D27-V2`: `recall@all ≥ 0.25` on all 7. Verdict over the **6** non-`D08` datasets (count COMPUTED); `D08` printed
`[not blind]`.

---

## Step 6 — half (A): the port, the tests, the mutants (est 50 m)

Delegate to a **CODE agent**; the controller verifies every claim itself.

Port the four disclosures from `BankVerifyRunner` into `GoldenEvalRunner` — `scoringMode` + `protectedStars`
(`:287-306`, `:344`), `precisionNull` (`:478-489`, `:513`), `truthViolations` (`:470-474`, `:517-521`),
`scoredFraction` (`:510-512`) — into the console line, `golden_eval.txt` and `golden_eval_frames.csv`.
`GoldenEvalRunner.WriteReports` must now be **passed the truth dispositions**. Honour the house rule at
`GoldenEvalRunner.cs:669`: *"Report the mode actually used — a hardcoded label here silently misattributes every
stored report."*

Plus the **doc-comment-only** fix to `GoldenFromTruth.cs:34-36` vs `:76-84`. **Do NOT change the `Reason` string
generation and do NOT regenerate the 180 sidecars** (design §6.2).

**Every new test shown RED against a NAMED MUTANT** — a revert will not build, since this is new API:

| mutant | change | test that must go red |
|---|---|---|
| `M-S1` | delete the `protectedStars` accumulation | the protected-count test |
| `M-S2` | hard-code `scoringMode = "golden"` | the mode test |
| `M-S3` | return the unshifted detections from the null path | the `precisionNull` test |

**The mutation harness keeps a BYTE BACKUP taken before each mutation and restores it in a `finally`** — never
`git checkout --`, which once reverted a working tree carrying the feature under measurement and silently deleted
it. Assert source sentinels after every mutant and abort on `WORKING TREE DAMAGED`.

---

## Step 7 — build B16, provenance, `Q27-V1`, and `S27-4` (est 35 m)

```bash
cd /home/ghilios/src/hocus-focus
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w27\exe' \
  2>&1 | tee /mnt/d/hf_w27/build_b16.log
sha256sum /mnt/d/hf_w27/exe/TestApp.dll /mnt/d/hf_w27/exe/NINA.Joko.Plugins.HocusFocus.dll \
  | tee /mnt/d/hf_w27/binary_provenance_w27.txt        # DLLS, never the apphost
find Joko.NINA.Plugins -name '*.cs' -newermt "$(date -r /mnt/d/hf_w27/exe/TestApp.dll '+%F %T')"   # MUST be empty
```

Record the `BuildId` and assert it **novel against the sixteen** recorded ids (the count printed by the prov
script must be **computed from `len(PRIOR_BUILD_IDS)`**, never typed). **Never rebuild mid-wave.**

### `Q27-V1` — the reach-scoped gate discharge, with its FAIL end RUN

```bash
BASE=$(grep -oE '[0-9a-f]{7,40}' /mnt/d/hf_w25/binary_provenance_w25.txt | head -1)   # READ B15's tree hash
echo "B15 tree = $BASE"                                # do NOT assume the previous wave's HEAD (wave 24 did)
git diff --name-only "$BASE"..HEAD -- '*.cs' | tee /mnt/d/hf_w27/q27_diff.txt
# discharge iff the intersection with the reachable set is EMPTY:
grep -E 'Joko.NINA.Plugins.HocusFocus/|TestApp/(OptimizationDiagnosticRunner|HarnessSettingsStore|OptimizationRunDiscovery|ParamsDump|StubBehaviorSelectors|DiagnosticUtil|Program)\.cs' \
  /mnt/d/hf_w27/q27_diff.txt | tee /mnt/d/hf_w27/q27_reach.txt
# FAIL END, RUN: the same check against a diff known to include plugin files must NOT be empty
git diff --name-only <B14_tree>..HEAD -- '*.cs' | grep -cE 'Joko.NINA.Plugins.HocusFocus/'
```

**Empty `q27_reach.txt` ⇒ the gate is discharged**, replaced by `Q27-V0` (hash identity + BuildId novelty) and
`S27-4`. **Non-empty ⇒ run the full 42 m gate** (`score_w12.py`, `prov_w26.py --self-test`). Either way the wave
reports which happened and **never calls a discharge a pass**.

### `S27-4` — do-no-harm, provable by bytes

```bash
bash /mnt/d/hf_w27/s27_arm_w27.sh 2>&1 | tee /mnt/d/hf_w27/w27_s27.log   # B16, all 20 cells, ~4 m
python3 /mnt/d/hf_w27/score_t27_w27.py --manifest /mnt/d/hf_w27/t27_manifest.tsv --rule S27 \
  --b16-root /mnt/d/hf_w27/table18_b16 --out /mnt/d/hf_w27/s27_score.txt
```

Required: **360 of 360** `detected_f*.csv` + `false_negatives_f*.csv` byte-identical to `table18`'s;
`TP`/`FP`/`FN` unchanged on **20 of 20**; `S27-1` route agreement on four fields × 20 datasets;
`S27-5` `truthViolations = 0` on 20 of 20. **Any byte or count that moves blocks the ship.**

---

## Step 8 — the suite, by COUNT (est 5 m)

```bash
powershell.exe -NoProfile -Command "Get-Process testhost,vstest.console -EA SilentlyContinue | %{ \$_.Kill() }"
cd /home/ghilios/src/hocus-focus
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  2>&1 | tee /mnt/d/hf_w27/suite.log ; echo "SUITE_EXIT=$?"
grep -E 'Failed:.*Passed:.*Total:' /mnt/d/hf_w27/suite.log
```

**Baseline 3973** (`/mnt/d/hf_w27/SUITE_BEFORE_3973.txt`, this branch, HEAD). **Name every added test and the
delta BEFORE anything is called green** ([F37](../docs/followups.md): verify by COUNT, never the tick).
Do not pipe to `tail` — it masks the exit code. **`dotnet test <sln>` does NOT build `TestApp`**; Step 7's build
is the compile check. `SendAsync_WritesOnABackgroundThread` is a known flake — do not chase it.

---

## Step 9 — the AFTER fingerprints, KEPT in files (est 5 m)

```bash
bash /mnt/d/hf_w27/fp_w27.sh table18  > /mnt/d/hf_w27/fp_table18_AFTER.txt
bash /mnt/d/hf_w27/fp_w27.sh sidecars > /mnt/d/hf_w27/fp_sidecars_AFTER.txt
diff /mnt/d/hf_w27/fp_table18_BEFORE.txt  /mnt/d/hf_w27/fp_table18_AFTER.txt  && echo "CLASS4 table18 PRESERVED"
diff /mnt/d/hf_w27/fp_sidecars_BEFORE.txt /mnt/d/hf_w27/fp_sidecars_AFTER.txt && echo "CLASS4 sidecars PRESERVED"
```

**BEFORE and AFTER must be the same code** (`fp_w27.sh`, `-print0 | sort -z | xargs -0`, population size asserted
inside the file). **Any difference ⇒ `T-UNEVALUATED`** — the wave's denominators moved under it.

---

## Step 10 — analysis, the record, the push (est 75 m)

Spawn an **ANALYSIS agent**. **Its first action is `ls -la --time-style=full-iso /mnt/d/hf_w27/` with the
timestamp PRINTED at the top of the results document** — wave 19's doc was falsified by an artifact written 33
seconds before its own commit. Any row still open at that instant is marked open **in place**.

**It applies the pre-registered rules and MUST NOT re-decide one. An unsatisfiable rule is a FINDING.**

Deliverables:

1. `docs/synthetic-af-bank-followups-wave27-results.md` — `RULE V27`, `RULE T27`, `RULE D27`, `RULE S27`,
   `Q27-V0/V1`, every estimate-vs-actual from `TIMING.tsv`, and **what was not run, priced**.
2. **(C) — the register corrections, per `docs/wave27-register-correction-survey.md`, line by line:**
   * **C1** `docs/synthetic-af-bank-results-table.md:7-21` — replace the banner (four false statements); **fix
     "13 genuine junk detections" → 11**; keep the "recall is unaffected" paragraph.
   * **C2** `docs/followups.md` F84 item (2), plus new entries **F85** (three fields named "false positive") and
     **F86** (the pre-repair `Reason` string on disk in 180 sidecars).
   * **C3** `docs/waves22+-handoff-prompt.md:171,172,188` — strike item 1 with its reason.
   * **C4** `docs/waves27+-autonomous-prompt.md:154-160` — **the LIVE charter propagates it verbatim**; correct
     it or a successor run re-opens item 1 a third time.
3. Commit with the privacy email, push, open the PR, **verify CI by COUNT read out of the log**.

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "..."
git push
gh run list --branch ghilios/synthetic-af-bank-followups-wave27 --limit 2 \
  --json status,conclusion,headSha --jq '.[]|"\(.headSha[0:7]) \(.status) \(.conclusion // "-")"'
```

---

## Step 11 — close the run

Delete the status cron (`CronDelete`) so a finished session is not woken every half hour. Final summary: what
shipped, what was measured, what was refuted, what remains — **suite verified by COUNT, CI conclusion read out
of the log.** Correct your own errors in the record, in place, where the owner can see them.

---

## Standing traps, on one screen

* **READ LOGS, NOT EXIT CODES.** A `*_START` line alone is not a running arm — confirm a live `TestApp.exe`.
* **WSL paths (`/mnt/d/...`) to scorers; Windows paths (`D:\...`) to `TestApp.exe`.**
* **One `TestApp.exe` at a time. One `dotnet test` at a time. Never NINA during an arm.**
* **`< /dev/null`** on every `TestApp.exe` invocation inside a loop.
* **Assert the population size everywhere**; `-print0 | sort -z | xargs -0`.
* **Ordinals and counts COMPUTED, never typed.**
* **ASCII logs.** Newtonsoft writes NaN/Infinity as the strings `"NaN"`/`"Infinity"`.
* **Hash the dlls, never the apphost.** `git diff --name-only <old>..<new> -- '*.cs'` is mandatory provenance for
  any two-binary rule; **read the BEFORE binary's own provenance file for its tree hash.**
* **A control written after the arm is not a control.** The BEFORE fingerprints are already taken.
* **An unsatisfiable clause is a FINDING, not something to repair and re-score.**
