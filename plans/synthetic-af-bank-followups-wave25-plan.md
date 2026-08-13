# Wave 25 — plan (execution)

Pre-registration: [`docs/synthetic-af-bank-followups-wave25-design.md`](../docs/synthetic-af-bank-followups-wave25-design.md).
**Commit the design BEFORE step 3's build and BEFORE the gate.** The BEFORE arm (step 2) runs on B14, a binary
that already exists on disk and that the pre-registration cannot influence, exactly as wave 24's A-BEFORE arm ran
on B13.

All paths are **WSL paths**. Every driver is **LF, ASCII only** — wave 23 shipped `TestAppOutputAsciiTests`, which
fails on any non-comment non-ASCII byte in TestApp, and this wave's drivers are held to the same bar so they can
be quoted verbatim into TestApp diagnostics without a fold.

**Times below are cumulative from `T0` = the moment step 1 starts. The absolute clock triggers in design §12
(03:15Z / 03:30Z / 04:40Z / 05:10Z) are the authority; the cumulative offsets are for sequencing only.**

---

## 0. Artifacts already written (no work owed)

**All five new instruments are written, ASCII-and-LF clean (0 bytes >= `0x80`, 0 `CR`), and every `--self-test`
was run to `exit 0` before this plan was finished.**

| file | state, verified |
|---|---|
| `docs/synthetic-af-bank-followups-wave25-design.md` | written, **uncommitted** |
| `plans/synthetic-af-bank-followups-wave25-plan.md` | this file, **uncommitted** |
| `/mnt/d/hf_w25/layout_w25.sh` | the ONE shared layout function. `--self-test` **exit 0**: refuses a bad arm; the two arms resolve to different binaries and different out roots; the deadline resolver demonstrated in **three** directions (a 2 h-past deadline rolls to tomorrow, a future one does not, garbage is refused) — the branch wave 24's **D2** could never fire |
| `/mnt/d/hf_w25/verify_derivation_w25.py` | `RULE V25`. `--self-test` **exit 0**. **FAIL end on `/mnt/d/hf_w24` -> `V-DRIFT`, naming `gate_w24.sh:221` (`thirteenth` on a FOURTEENTH binary, D7) and `prov_w24.py:179` (a typed nine-wave enumeration while thirteen are checked, drift #5).** PASS end on `/mnt/d/hf_w25` -> **`V-CLEAN`**, 2 of 2 sibling targets resolving |
| `/mnt/d/hf_w25/s1_arm_w25.sh` | the paired arm. `--self-test` **exit 0**: the out-root guard refuses `/tmp` and accepts `${W25_ROOT}`; the binary guard refuses a wrong hash and **accepts B14's recorded dll pair, verified on disk**; the gate guard refuses with `G25_PASSED` absent |
| `/mnt/d/hf_w25/score_p3c_w25.py` | `G25-P3c` + its marker. `--self-test` **exit 0** in **five** directions on copies of real wave-24 landings: self-scored 8 UNCHANGED; one decrement -> `NARROWED`, rc=1; one increment -> `WIDENED`, rc=0; a missing landing -> UNEVALUATED; a corrupted baseline -> refused. **It also re-verified that wave 24's eight published `RecommendedStepSize` integers are exactly what the landings say** |
| `/mnt/d/hf_w25/score_w25.py` | `RULE W25` + `RULE N25` + `RULE M25` + `G25-P4` + `N25-E`. `--self-test` **exit 0**. **`M25`'s FAIL end reproduced on real published artifacts: 25 cells scanned, exactly 1 contradiction, `D01_ultrawide_40mm__S1`** — the pre-registered expectation, measured. `G25-P4` FAIL end on wave 23's gate, PASS end on wave 24's, UNEVALUATED on an empty root |

Three instruments — `gate_w25.sh`, `score_g25_w25.py`, `prov_w25.py` — are **derived** in step 1 and then checked
by `RULE V25`. `V25-A` currently reads **2 of 2** and will read more once they land; **if it ever reads `0`, the
answer is `V-UNEVALUATED`, never `1.0000`.**

---

## 1. Pre-flight (~15 m, cumulative T0)

```bash
mkdir -p /mnt/d/hf_w25/{gate,before,after,exe,w25_code_backups/pre_P4,w25_code_backups/post_P4_pre_P5}
bash /mnt/d/hf_w25/layout_w25.sh --self-test   | tee /mnt/d/hf_w25/layout_selftest.txt

# --- derive the three carried-forward instruments ------------------------------------------------------------
cd /mnt/d/hf_w25
cp /mnt/d/hf_w24/gate_w24.sh        gate_w25.sh
cp /mnt/d/hf_w24/score_g24_w24.py   score_g25_w25.py
cp /mnt/d/hf_w24/prov_w24.py        prov_w25.py

for f in gate_w25.sh score_g25_w25.py prov_w25.py; do
  sed -i -e 's#hf_w24#hf_w25#g' -e 's#\bG24\b#G25#g' -e 's#\bB24\b#W25#g' \
         -e 's#G24_PASSED#G25_PASSED#g' -e 's#prov_w24#prov_w25#g' \
         -e 's#g24_#g25_#g' -e 's#score_g24_w24#score_g25_w25#g' -e 's#gate_w24#gate_w25#g' \
         -e 's#WAVE 24#WAVE 25#g' -e 's#wave 24#wave 25#g' -e 's#wave-24#wave-25#g' "$f"
done
# The blanket rewrite will have moved the INTERLOCK'S FAIL-END root too, and it must stay pointed at a PRIOR
# wave -- a novelty check aimed at its own gate cannot refuse anything. Put it back, and let RULE V25's ALLOW
# list (which declares /mnt/d/hf_w19/gate with its reason) be what proves it is deliberate.
grep -n 'hf_w19' /mnt/d/hf_w25/score_g25_w25.py || \
  sed -i 's#--self-test /mnt/d/hf_w25/gate#--self-test /mnt/d/hf_w19/gate#' /mnt/d/hf_w25/score_g25_w25.py

# PRIOR_BUILD_IDS: add B14 by hand, and let the printed word be computed
python3 - <<'PY'
import re
p = "/mnt/d/hf_w25/prov_w25.py"
s = open(p, encoding="utf-8").read()
assert "dd6ca32ef7f941c2a54753398cc2cf6b" not in s, "B14 already present -- do not add it twice"
s = s.replace('PRIOR_BUILD_IDS = {', 'PRIOR_BUILD_IDS = {\n    "dd6ca32ef7f941c2a54753398cc2cf6b": '
              '"wave 24 (RULE G24, RULE B24, RULE R24; the FOURTEENTH binary)",', 1)
open(p, "w", encoding="utf-8").write(s)
PY
grep -c '"' /mnt/d/hf_w25/prov_w25.py >/dev/null
python3 -c "
import re;s=open('/mnt/d/hf_w25/prov_w25.py',encoding='utf-8').read()
import ast;m=re.search(r'PRIOR_BUILD_IDS = \{.*?\n\}', s, re.S)
print('PRIOR_BUILD_IDS entries:', len(ast.literal_eval(m.group(0).split('=',1)[1])))"
# MUST print 14. If it prints 13 the B14 insert failed; if 15, it ran twice. STOP either way.

# --- RULE V25: the pre-flight. FAIL END FIRST, on wave 24's real instruments -----------------------------------
python3 /mnt/d/hf_w25/verify_derivation_w25.py --self-test   | tee /mnt/d/hf_w25/v25_selftest.txt
python3 /mnt/d/hf_w25/verify_derivation_w25.py /mnt/d/hf_w24 | tee /mnt/d/hf_w25/v25_failend.txt
#   MUST exit 1 (V-DRIFT) and MUST name prov_w24.py:179 (typed nine-wave list), prov_w24.py's prov_w23.py
#   self-name, and v23_fingerprint_w24.py's wave-21 labels. If it exits 0, the checker is broken -- STOP.
python3 /mnt/d/hf_w25/verify_derivation_w25.py /mnt/d/hf_w25 | tee /mnt/d/hf_w25/v25_passend.txt
#   MUST exit 0 (V-CLEAN). Fix every finding and re-run until it does. NO MEASUREMENT STARTS BEFORE THIS.
```

**Vessel check, before anything else:**

```bash
git -C /home/ghilios/src/hocus-focus rev-parse --abbrev-ref HEAD    # ghilios/synthetic-af-bank-followups-wave23
gh pr view 195 --json state,number                                   # OPEN
# contingency (design §0): only if #195 was merged --
grep -q DegenerateReasonHalfWidthUnresolved Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/StepSizeRecommender.cs \
  && test -f Joko.NINA.Plugins/TestApp/SynthBank/BinningRevisitPolicy.cs && echo "P1/P2/P3 present by CONTENT"
```

---

## 2. LAUNCH THE BEFORE ARM AND THE CODE AGENT AT THE SAME TIME (~80 m wall, cumulative T0+15m)

### 2a. Start the BEFORE arm first, in background

```bash
# fingerprint class 4 BEFORE -- N25-E's evidence, taken before ANY wave-25 write
python3 /mnt/d/hf_w25/score_w25.py --fingerprint-v23 /mnt/d/hf_w23/v23 \
  > /mnt/d/hf_w25/fp_v23_BEFORE.txt 2>&1

nohup bash /mnt/d/hf_w25/s1_arm_w25.sh --before > /mnt/d/hf_w25/w25_before.log 2>&1 &
```

**Status sweep, every ~10 m** (charter §2 — a background job reporting `exit 0` has repeatedly meant a driver
aborted in one second):

```bash
head -3 /mnt/d/hf_w25/w25_before.log            # must carry W25_before_START and its RESOLVED deadline
tasklist.exe 2>/dev/null | grep -c TestApp      # must be >= 1 while the arm is live
wc -l < /mnt/d/hf_w25/w25_before_manifest.tsv   # must grow
```

**A `*_START` line alone is not evidence the arm is running.** Wave 24's D1 had one and had skipped all 22 cells.

### 2b. Simultaneously, spawn the CODE agent

Give it design §3 verbatim. It **must**:

1. Take byte backups **first**: `cp` the four files to `/mnt/d/hf_w25/w25_code_backups/pre_P4/`.
2. Land **P4** (design §3.1): `BandDemonstrablyUnsampled`, the P4a floor after the cap block, the P4b
   `half-width-unresolved` diversion, `WasBandFloored` on `StepSizeRecommendation`, `OptimizationSummary` and
   `StepRecommendationSnapshot`, the `StepSizeText` gate change. **`WasCapped` is not written by the new branch.**
3. Take the second byte backup set: `cp` to `w25_code_backups/post_P4_pre_P5/`.
4. Land **P5** (design §3.2): the stall message reads `DegenerateReason` and branches.
5. Write the **seven** tests (design §3.4) and demonstrate **T1-T4 RED by named mutants**, never `git checkout --`
   (the tree carries this uncommitted pre-registration). Each mutant applied by `cp` from a byte backup, run,
   restored, and the restoration verified `cmp`-identical. Capture assertion messages to
   `/mnt/d/hf_w25/prechange_test_evidence.txt`.
6. Report. **It does not commit and it does not build.**

---

## 3. Close the BEFORE arm, commit, build B15 (~10 m, cumulative T0+95m)

```bash
tail -20 /mnt/d/hf_w25/w25_before.log
test -f /mnt/d/hf_w25/W25_BEFORE_READY && cat /mnt/d/hf_w25/W25_BEFORE_READY
wc -l < /mnt/d/hf_w25/w25_before_manifest.tsv     # cells that produced a report; NAME any that did not

# the pre-registration commit -- BEFORE the build, BEFORE the gate
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(W25): pre-register the wave -- the step recommender has a ceiling and no floor" \
  -- docs/synthetic-af-bank-followups-wave25-design.md plans/synthetic-af-bank-followups-wave25-plan.md

# then the code commit, once the code agent reports and T1-T4 are demonstrated red
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "W25: a sweep that never sampled the 3x band gets the widest step it supports"

# --- build B15, ONCE, into its own directory, never rebuilt mid-wave (F53(c)) ---------------------------------
W25=$(git rev-parse --short HEAD)
dotnet.exe build "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
# copy the TestApp publish output to /mnt/d/hf_w25/exe (same route wave 24 used for /mnt/d/hf_w24/exe)

# --- two-binary provenance, against BOTH bases, recorded VERBATIM (wave 24 s 0.2 recorded the wrong base) -----
{
  echo "--- 839c38b..$W25 -- '*.cs' (B13 -> B15) ---"; git diff --name-only 839c38b..$W25 -- '*.cs'
  echo "--- 476369a..$W25 -- '*.cs' (B14 -> B15; THE PAIRED ARM'S ONLY DELTA) ---"
  git diff --name-only 476369a..$W25 -- '*.cs'
  echo "--- counts ---"; git diff --name-only 476369a..$W25 -- '*.cs' | wc -l
  echo "--- freshness: any .cs newer than the B15 build? (MUST be empty) ---"
  find Joko.NINA.Plugins -name '*.cs' -newermt "$(date -r /mnt/d/hf_w25/exe/TestApp.dll '+%Y-%m-%d %H:%M:%S')"
  echo "--- dll sha256 (NEVER the apphost -- F66's seventh counter-example) ---"
  sha256sum /mnt/d/hf_w25/exe/TestApp.dll /mnt/d/hf_w25/exe/NINA.Joko.Plugins.HocusFocus.dll
} | tee /mnt/d/hf_w25/binary_provenance_w25.txt
```

**The `476369a..$W25` list is the one that matters** and it must contain **only** P4's and P5's files
(`StepSizeRecommender.cs`, `StarDetectionOptimizerWizardVM.cs`, `SynthValidationReport.cs`,
`SynthValidateRunner.cs`, plus the test file). Anything else means the paired arm has more than one variable.

---

## 4. RULE G25 — the gate (~42 m + ~8 m scoring, cumulative T0+147m)

```bash
# --- G25-P4: the ASCII census. FAIL END on WAVE 23's real published gate FIRST. DROP D1 after 03:15Z ---------
python3 /mnt/d/hf_w25/score_w25.py --ascii-census /mnt/d/hf_w23/gate | tee /mnt/d/hf_w25/g25_asciifailend.txt
#   MUST report 8 of 8 files carrying a 0xE5 -> P4-NON-ASCII-PRESENT. Bytes in Python, never grep.

# --- G25-P3c FAIL END, on a MUTATED COPY of a real wave-24 landing, before the gate runs ---------------------
python3 /mnt/d/hf_w25/score_p3c_w25.py --self-test | tee /mnt/d/hf_w25/p3c_selftest.txt

# --- the gate proper ----------------------------------------------------------------------------------------
nohup bash /mnt/d/hf_w25/gate_w25.sh > /mnt/d/hf_w25/gate_w25.log 2>&1 &
# ~42 m. Sweep as in 2a.

# --- scoring, self-tests BEFORE the thing they guard ---------------------------------------------------------
python3 /mnt/d/hf_w25/score_g25_w25.py --self-test        | tee /mnt/d/hf_w25/g25_scorer_selftest.txt
python3 /mnt/d/hf_w25/prov_w25.py --self-test /mnt/d/hf_w25/gate | tee /mnt/d/hf_w25/prov_w25.txt
python3 /mnt/d/hf_w25/score_g25_w25.py /mnt/d/hf_w25/gate | tee /mnt/d/hf_w25/g25_score.txt
python3 /mnt/d/hf_w25/score_w25.py --ascii-census /mnt/d/hf_w25/gate | tee /mnt/d/hf_w25/g25_ascii.txt
python3 /mnt/d/hf_w25/score_p3c_w25.py /mnt/d/hf_w25/gate  | tee /mnt/d/hf_w25/p3c_score.txt
#   G25_P3C_OK is written by score_p3c_w25.py itself, on rc == 0 only.
#   NARROWED >= 1 -> G-DIRECTION-VIOLATED -> STOP. Unship per design 3.3 and report.

# --- the interlock. FAIL END FIRST, and ASSERT THE ABSENT MARKER (wave 24 specified this line and lost it) ---
python3 /mnt/d/hf_w25/score_g25_w25.py --arm-gate /mnt/d/hf_w19/gate | tee /mnt/d/hf_w25/g25_armfailend.txt
test ! -f /mnt/d/hf_w25/G25_PASSED  && echo "ASSERTED: the FAIL end left NO G25_PASSED marker" \
  | tee -a /mnt/d/hf_w25/g25_armfailend.txt
python3 /mnt/d/hf_w25/score_g25_w25.py --arm-gate /mnt/d/hf_w25/gate | tee /mnt/d/hf_w25/g25_armpassend.txt
cat /mnt/d/hf_w25/G25_PASSED /mnt/d/hf_w25/G25_P3C_OK
```

---

## 5. RULE M25's FAIL END, on real published artifacts (~2 m, cumulative T0+150m)

**Before the AFTER arm, so the instrument is validated against a known answer first.**

```bash
python3 /mnt/d/hf_w25/score_w25.py --m25-failend /mnt/d/hf_w24/after | tee /mnt/d/hf_w25/m25_failend.txt
#   MUST find >= 1 contradiction and MUST name D01_ultrawide_40mm/S1. If it finds none -> M-UNEVALUATED.
```

---

## 6. RULE W25 / RULE N25 — the AFTER arm (~85 m, cumulative T0+235m)

```bash
nohup bash /mnt/d/hf_w25/s1_arm_w25.sh --after > /mnt/d/hf_w25/w25_after.log 2>&1 &
```

The driver **aborts unless both `G25_PASSED` and `G25_P3C_OK` exist** and refuses to write an `--out` outside
`/mnt/d/hf_w25/`. It writes `W25_AFTER_READY` **last**, and only if the manifest has >= 17 rows.

**Drop D3** (design §12): if this arm has not started by **03:30Z**, pass `--drop-controls`, which removes
`D08`/S1 and `D11`/S1 **from the AFTER arm only** and prints both names. No denominator moves.

**HARD STOP 05:10Z**: kill the arm, score what paired cells exist, apply `W25-V1`/`N25-V1`. A failed validity gate
is `W-UNEVALUATED`/`N-UNEVALUATED` and **that is the result** — P4 does not ship on an unrun rule.

---

## 7. Close out (~25 m, cumulative T0+260m; **start the analysis agent as soon as scoring lands**)

```bash
# AFTER fingerprints -- and the output is KEPT
python3 /mnt/d/hf_w25/score_w25.py --fingerprint-v23 /mnt/d/hf_w23/v23 > /mnt/d/hf_w25/fp_v23_AFTER.txt 2>&1

# the scorer: self-test in BOTH directions BEFORE it is quoted
python3 /mnt/d/hf_w25/score_w25.py --self-test | tee /mnt/d/hf_w25/w25_scorer_selftest.txt
python3 /mnt/d/hf_w25/score_w25.py --score /mnt/d/hf_w25/before /mnt/d/hf_w25/after \
  | tee /mnt/d/hf_w25/w25_score.txt
# RULE W25, RULE N25, RULE M25 and W25-K all land in that one file, each with its denominator printed.

# N25-E -- zero TestApp minutes. DROP D2 after 04:40Z.
python3 /mnt/d/hf_w25/score_w25.py --n25e /mnt/d/hf_w25/before /mnt/d/hf_w23/v23 | tee /mnt/d/hf_w25/n25e.txt

# the full suite, by COUNT, never the tick (F37). Kill stragglers first.
taskkill.exe /F /IM TestApp.exe 2>/dev/null
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo \
  > /mnt/d/hf_w25/suite.log 2>&1; echo "SUITE_EXIT=$?" >> /mnt/d/hf_w25/suite.log
grep -E 'Passed!|Failed!|Passed:|Failed:' /mnt/d/hf_w25/suite.log | tail -5
#   EXPECTED 3973 = 3966 + 7. Read the COUNT out of the log. A crash with no failing assertion is NOT the known
#   flaky SendAsync_WritesOnABackgroundThread -- different signature, and F37 forbids conflating them.
```

Then: register entries per design §13, the results document, the PR #195 section, and the wave-26 hand-off.

---

## 8. Drop order and the clock checkpoints

| | item | trigger | what is lost |
|---|---|---|---|
| **not scheduled** | `lumos`'s zero-star frame | decided in advance (design §1.5) | goes to wave 26's opener |
| **D1** | `G25-P4` ASCII census | gate not started by **03:15Z** | a re-verification of a wave-24 fix; `G-ASCII-INCOMPLETE` is non-blocking anyway |
| **D2** | `N25-E` | scoring not started by **04:40Z** | the comparability question returns to wave 26 |
| **D3** | `D08`/S1, `D11`/S1 from the AFTER arm | AFTER arm not started by **03:30Z** | two labelled controls, both **named**; no denominator moves |
| **HARD STOP** | the AFTER arm | not finished by **05:10Z** | kill; score the paired cells that exist; a failed validity gate is the result |

**The ladder never shrinks a blind denominator.** Every rung is an instrument, a control, or a scoring
convenience.

---

## 9. Standing discipline, grepped for before the wave starts

- **No human writes a marker.** Every `*_READY` / `*_PASSED` / `*_OK` in this plan is written by the code that
  computes the verdict, on `rc == 0` only ([F75](../docs/followups.md)).
- **Every marker carries its evidence path** and every consumer parses it out of the marker; **no `os.walk`
  fallback** ([F74](../docs/followups.md)).
- **Every `--self-test` runs BEFORE the thing it guards**, and derived instruments are re-self-tested **on the
  copy** before being quoted.
- **Every gate is demonstrated in BOTH directions on real inputs before it is quoted** ([F66](../docs/followups.md)),
  and where a control reduces to one field, a second field must move the same way.
- **Binary identity is the dll sha256 pair, never the apphost `TestApp.exe`** ([F66](../docs/followups.md)).
- **`RULE F14` / `RULE S16` / `RULE D20` are permanent fences.** No clause reads them.
- **Byte backups, `cmp`-verified, never a VCS revert.**
