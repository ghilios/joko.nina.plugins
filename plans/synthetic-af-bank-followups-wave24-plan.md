# Wave 24 — plan (execution)

Pre-registration: [`docs/synthetic-af-bank-followups-wave24-design.md`](../docs/synthetic-af-bank-followups-wave24-design.md).
**The design is the authority. This file is the step order and the exact commands. It decides nothing.**

**The controller runs every measurement.** A subagent's background processes die with its session and tool
timeouts cap at 10 minutes. Agents read, write, design and analyse.

**Clock.** Hard stop `03:50Z` on 2026-08-13; the wave should land by **~02:30Z**. Every step below carries its
price and its cumulative finish assuming a **21:00Z** start. **If a step finishes late, take the drop order in
§10 — do not compress a step.**

---

## 0. Artifacts already written and already self-tested (no work owed)

| file | state |
|---|---|
| `/mnt/d/hf_w24/layout_w24.sh` | written. LF, ASCII. `w24_layout_self_test` passes, including **both** directions of the `--out` write-refusal guard |
| `/mnt/d/hf_w24/b24_arm_w24.sh` | written. LF, ASCII. `--self-test` **PASSES** and it verified B13's two published dll sha256s on disk |
| `/mnt/d/hf_w24/score_b24_w24.py` | written. LF, ASCII. `--self-test` **PASSES: 21 branches**, including `B-HARMFUL`, `B-REGRESSED`, `B-UNEXERCISED`, `R-BROKEN`, `R-EXPLAINED`, `R-UNEVALUATED`, `D-NO-DEGENERATE-ROUNDS`, and that a missing marker is UNEVALUATED and never a rate |
| `/mnt/d/hf_w24/ascii_census_w24.py` | written. LF, ASCII. `--self-test` **PASSES**, and its **FAIL end is already measured on real artifacts**: `--gate /mnt/d/hf_w23/gate` reports **8 of 8 files carrying one `0xE5`** (offsets 7975–8153) and exits **1** |

**Re-run all four self-tests at step 1 anyway.** A self-test that ran in another session is a claim.

---

## 1. Pre-flight and the vessel decision (~4 m, cumulative 21:04Z)

```bash
date -u +%FT%H:%M:%SZ
cd /home/ghilios/src/hocus-focus
git status --short && git log --oneline -3 && git branch --show-current
gh pr view 195 --json state,mergeable --jq '"\(.state) \(.mergeable)"'
tasklist.exe | grep -ciE "TestApp|NINA"          # MUST be 0
```

**The vessel, per design §0:** if PR #195 is **OPEN**, stay on `ghilios/synthetic-af-bank-followups-wave23`. If
it has **MERGED**, `git checkout develop && git pull && git checkout -b ghilios/synthetic-af-bank-followups-wave24`.
**Record which happened, in one line, in the results doc.**

```bash
bash  /mnt/d/hf_w24/b24_arm_w24.sh   --self-test          # expect rc 0
python3 /mnt/d/hf_w24/score_b24_w24.py --self-test        # expect rc 0, 21 PASS
python3 /mnt/d/hf_w24/ascii_census_w24.py --self-test     # expect rc 0
```

**Every `--self-test` runs BEFORE the thing it guards.** Wave 21 ran one while its arm was four datasets deep.

---

## 2. Build the three carried-forward instruments (~5 m, cumulative 21:09Z)

`gate_w24.sh`, `score_g24_w24.py` and `prov_w24.py` are wave 23's instruments **carried forward with only the
tables and path constants edited**. *Editing an instrument mid-series is how it stops being the same
instrument;* the gate must be read by the same code path as the thirteen binaries before it.

**These three `sed` lines were DRY-RUN at pre-registration time and their outputs verified** — the derived
`score_g24_w24.py` passes **24 of 24** self-test branches, the derived `gate_w24.sh` passes `bash -n` and
carries **zero** residual `w23`/`G23` references. Use them exactly as written; the ordering matters.

```bash
sed 's#hf_w23#hf_w24#g; s#G23_PASSED#G24_PASSED#g; s#\bG23\b#G24#g; s#g23_#g24_#g' \
    /mnt/d/hf_w23/score_v23_w23.py > /mnt/d/hf_w24/score_g24_w24.py
sed 's#hf_w23#hf_w24#g; s#G23_PASSED#G24_PASSED#g; s#G23_START#G24_START#g; s#G23_DONE#G24_DONE#g; s#\bG23\b#G24#g; s#g23_#g24_#g; s#score_v23_w23#score_g24_w24#g; s#prov_w23#prov_w24#g; s#layout_w23#layout_w24#g; s#w21_f21_fingerprint_BEFORE#v23_report_fingerprint_BEFORE#g; s#FP_W21#FP_W23#g; s#f21_fingerprint_w23#v23_fingerprint_w24#g; s#w23_#w24_#g' \
    /mnt/d/hf_w23/gate_w23.sh > /mnt/d/hf_w24/gate_w24.sh
sed 's#hf_w23#hf_w24#g' /mnt/d/hf_w23/prov_w23.py > /mnt/d/hf_w24/prov_w24.py
```

> **`s#G23_PASSED#G24_PASSED#g` in the GATE line is not cosmetic and it is the reason these were dry-run.**
> `\bG23\b` does **not** match `G23_PASSED` — `_` is a word character, so there is no boundary after `G23`.
> Without the explicit substitution, `gate_w24.sh:166`'s F75 self-guard would still grep its own text for
> **`G23_PASSED`** while the wave's marker is `G24_PASSED`: **a guard that checks for a string the file can no
> longer contain is a guard that cannot fire** ([F66](../docs/followups.md)'s exact shape). And
> `s#FP_W21#FP_W23#g` re-points the class-4 fingerprint variable at the file step 3a actually writes; without
> it the gate ABORTS on a missing fingerprint it was never going to be given.

Then **by hand, two lines only** — the thirteenth binary becomes a *prior*:

* into `PRIOR_BUILD_IDS` in `prov_w24.py`, after the wave-21 row:
  `    "7927513b6493438aa73da5b91715fe0d": "wave 23 (RULE G23's and RULE V23's; the THIRTEENTH binary)",`
  and change the comment `THE LIST IS TWELVE. Wave 23's binary is the THIRTEENTH.` to
  `THE LIST IS THIRTEEN. Wave 24's binary is the FOURTEENTH.`
* into `KNOWN_DLL_SHA256`:
  `    "82491ac79ce3d9216b4fa0d32c3daeb41aee460e845377c77f4ec9572cfc9ccf": "wave 23 TestApp.dll sha256",`
  `    "fe9db95c52f25e7e7fc6d9746a96986cfb545f0eb5011c4f03a8719292ddb998": "wave 23 HocusFocus.dll sha256",`

Also copy the class-4 fingerprint instrument, re-pointed at **wave 23's 40 `synth_validate_report.json`
files** (RULE R24's entire BEFORE side):

```bash
sed 's#hf_w23#hf_w24#g; s#f21_fingerprint_w23#v23_fingerprint_w24#g' \
    /mnt/d/hf_w23/f21_fingerprint_w23.py > /mnt/d/hf_w24/v23_fingerprint_w24.py
# then point its glob at /mnt/d/hf_w23/v23/*__S*/synth_validate_report.json and set its expected count to 40
```

**Verify the copies, in both directions, and KEEP the output** (wave 23 §8's recorded caveat):

```bash
python3 /mnt/d/hf_w24/score_g24_w24.py --self-test 2>&1 | tee /mnt/d/hf_w24/g24_scorer_selftest.txt
echo "rc=$?"        # expect 0, and "24 of 24 self-test branches passed"
bash    /mnt/d/hf_w24/gate_w24.sh --self-test 2>&1 | tee /mnt/d/hf_w24/gate_w24_selftest.txt
python3 /mnt/d/hf_w24/v23_fingerprint_w24.py --self-test 2>&1 | tee /mnt/d/hf_w24/v23fp_selftest.txt
grep -c 'hf_w23' /mnt/d/hf_w24/*.py /mnt/d/hf_w24/*.sh   # ONLY layout_w24.sh (R24's BEFORE root) and
                                                          # b24_arm_w24.sh (B13's exe) may be non-zero
```

**`prov_w24.py`'s self-test needs a gate root** (`prov_w24.py [--self-test] <gate-out-root>`), and wave 24's
gate does not exist yet — so point it at **wave 23's** gate, which is now the FAIL end and demonstrates the
novelty check in the refusing direction for free:

```bash
python3 /mnt/d/hf_w24/prov_w24.py --self-test /mnt/d/hf_w23/gate 2>&1 | tee /mnt/d/hf_w24/prov_w24_failend.txt
echo "prov FAIL-end rc=$?"   # MUST be non-zero: 7927513b is now a PRIOR id, so wave 23's gate is NOT novel
```

It must print **"novel against THIRTEEN recorded ids"**, with the word computed from `len(PRIOR_BUILD_IDS)` —
wave 20's copy printed "NINE" while checking ten — and it must **refuse** wave 23's gate. **A novelty list that
does not refuse the binary it was just extended with is not a novelty list.** The PASS end runs at step 5
against wave 24's own gate.

---

## 3. LAUNCH THE CODE AGENT AND THE A-BEFORE ARM AT THE SAME TIME (~45 m wall, cumulative 21:54Z)

**This is the only parallelism the charter permits and it is the wave's whole timing margin.** The code agent
launches no `TestApp.exe`; the A-BEFORE arm launches no build.

### 3a. Start the A-BEFORE arm first, in background

```bash
# fingerprint class 4 BEFORE -- R24's entire BEFORE side, taken before ANY wave-24 TestApp.exe runs
python3 /mnt/d/hf_w24/v23_fingerprint_w24.py --write /mnt/d/hf_w24/v23_report_fingerprint_BEFORE.json
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --write /mnt/d/hf_w24/bank_landing_fingerprint_BEFORE.json
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --write /mnt/d/hf_w24/bank_aux_fingerprint_BEFORE.json
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --write /mnt/d/hf_w24/w18_arm_fingerprint_BEFORE.json

nohup bash /mnt/d/hf_w24/b24_arm_w24.sh --phase before > /mnt/d/hf_w24/b24_before.log 2>&1 &
```

**READ LOGS, NOT EXIT CODES.** Confirm `B24_before_START` in the log **and** a live `TestApp.exe` before
believing it is running:

```bash
head -8 /mnt/d/hf_w24/b24_before.log; tasklist.exe | grep -c TestApp.exe
```

**Price: ~45 m.** 22 cells: S4 × 20 requested (7 expected applicable at ~2× those datasets' wave-23 S1 times
≈ 1 900 s; 13 inapplicable at ~10 s each), plus `D08`/S1 (~435 s) and `D11`/S1 (~48 s).
**Contingency, pre-registered:** if the first three *inapplicable* cells each take > 60 s, kill the arm, set
`DS` to the seven `S4_EXPECTED` datasets, and restart into a clean `before/`. That is a pre-registered
contingency, not a mid-wave repair, and the results doc records it.

### 3b. Simultaneously, spawn the CODE agent

Give it design §3 verbatim as its specification. Its deliverables, in this order:

1. **P1** — `StepSizeRecommendation.DegenerateReason`; `Degenerate(...)` sets it on all three exits
   (`StepSizeRecommender.cs` guards `:233` / `:239` / `:252`, returns `:234` / `:240` / `:253`) with the
   constants `"no-fit"`, `"non-finite-vertex"`, `"half-width-unresolved"`; `SampledHfrRange =
   MeasureSampledHfrRange(bestFit)` **when `bestFit != null`**; mirror `degenerateReason` into
   `StepRecommendationSnapshot` (`SynthValidationReport.cs:67-85`) and populate at
   `SynthValidateRunner.cs:785-790`; surface `StepSizeDegenerateReason` on `OptimizationSummary` and show it
   in the wizard's step block.
2. **P3** — `binningRecommendation.currentFactor` and `.appliedFactor` on `BinningRecommendationSnapshot`
   (`:109-113`); `applied.stepDeferredByBinning` and `.binningDeferralBoundReached` on `AppliedSnapshot`
   (`:117-126`); populated in `SynthValidateRunner.cs`.
3. **P2** — the binning-**revisit** deferral bound at `SynthValidateRunner.cs:828-848`, with
   `ScenarioRunState.VisitedDetectionBinningFactors` seeded at `:484-490`. **P2 must be confined to
   `SynthValidateRunner.cs` so a P2 revert cannot take P1 or P3 with it.**
4. **The six tests of design §3.4**, and **each of the four *failing* ones demonstrated red against the
   pre-change source**, with `Failed: N, Passed: M` captured to
   `/mnt/d/hf_w24/prechange_test_evidence.txt`. **Byte backups (`cp`), restored in a `finally`, never
   `git checkout --`** — the tree carries the uncommitted pre-registration and will carry the results doc.
5. **Build TestApp separately** and report `0 errors`: `dotnet test <sln>` does **not** build TestApp, so a
   compile break there never surfaces in the suite.

**The controller verifies the failing-test claim independently** — re-run one of the four against a restored
byte backup and read the count.

**Price: ~60–75 m.** If the code is not ready by **23:15Z**, cut **P3's two `applied` booleans last** (they are
the smallest); **never** cut P1, and **never** ship P2 without P3's `binningDeferralBoundReached` — the scorer
returns `B-UNEVALUATED` without it, by design ([F76](../docs/followups.md)).

### 3c. When the A-BEFORE arm finishes

```bash
tail -20 /mnt/d/hf_w24/b24_before.log
cat /mnt/d/hf_w24/B24_BEFORE_READY            # must exist; if the driver refused, THAT IS THE RESULT
wc -l /mnt/d/hf_w24/b24_before_manifest.tsv
awk -F'\t' '$2=="S4"' /mnt/d/hf_w24/b24_before_manifest.tsv | wc -l   # the binary-resolved S4 count
```

**A missing `B24_BEFORE_READY` means fewer than 5 S4 rows excluding `D08`. RULE B24 is `B-UNEVALUATED` by name
and the wave says so; it does not lower the minimum.**

---

## 4. Commit the pre-registration, then build B14 (~6 m, cumulative 22:00Z + code)

**The pre-registration is committed BEFORE the build and BEFORE any wave-24 `TestApp.exe` touches B14.** The
A-BEFORE arm runs on **B13**, whose gate passed in wave 23; committing after it is not committing after its own
measurement. **Record that ordering explicitly in the results doc.**

```bash
cd /home/ghilios/src/hocus-focus
git add docs/synthetic-af-bank-followups-wave24-design.md plans/synthetic-af-bank-followups-wave24-plan.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(W24): pre-register the wave -- the livelock is in the harness, not the product"
# then the code commit, once the code agent reports and its failing tests are verified
git add -A && GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "W24: a degenerate step recommendation says so; bound the harness's binning deferral"
W24_COMMIT=$(git rev-parse HEAD)
```

**Build B14, once, into a directory that is never rebuilt** ([F53](../docs/followups.md)(c)):

```bash
dotnet.exe build "$(wslpath -w /home/ghilios/src/hocus-focus/Joko.NINA.Plugins/TestApp/TestApp.csproj)" \
  -c Release -o 'D:\hf_w24\exe'
{
  echo "W24 binary provenance"
  echo "commit: $W24_COMMIT"
  sha256sum /mnt/d/hf_w24/exe/TestApp.dll /mnt/d/hf_w24/exe/NINA.Joko.Plugins.HocusFocus.dll
  echo "--- git diff --name-only 839c38b..$W24_COMMIT -- '*.cs'  (VERBATIM) ---"
  git diff --name-only 839c38b.."$W24_COMMIT" -- '*.cs'
  echo "--- counts ---"
  git diff --name-only 839c38b.."$W24_COMMIT" -- '*.cs' | wc -l
  git diff --name-only 839c38b.."$W24_COMMIT" -- '*.cs' | grep -vc 'Tests/'
  echo "--- freshness: this MUST be empty ---"
  find /home/ghilios/src/hocus-focus/Joko.NINA.Plugins -name '*.cs' \
       -newermt "$(date -r /mnt/d/hf_w24/exe/TestApp.dll '+%Y-%m-%d %H:%M:%S')"
} | tee /mnt/d/hf_w24/binary_provenance_w24.txt
sha256sum /mnt/d/hf_w23/exe/TestApp.dll   # B13 MUST still hash 82491ac7... -- it was not rebuilt
```

**Expect the diff to be LARGE and mostly cosmetic**: `e7a5ee6` alone is 20 TestApp files of ASCII substitutions
plus a 342-line test file. **A differing `BuildId` proves a rebuild happened; it has never proved the code
differs** — which is why the diff is recorded verbatim.

---

## 5. RULE G24 — the gate (~42 m + ~8 m scoring, cumulative ≈ 23:35Z)

```bash
nohup bash /mnt/d/hf_w24/gate_w24.sh > /mnt/d/hf_w24/gate_w24.log 2>&1 &
head -8 /mnt/d/hf_w24/gate_w24.log; tasklist.exe | grep -c TestApp.exe
```

**~42 m**, a seventh wave at ~5.2 m/run (41 m 11 s in wave 23). Then, **FAIL ends first, every output kept**:

```bash
# --- G24-P4: the ASCII census. FAIL end on WAVE 23's real gate FIRST -- a published prior result ------------
python3 /mnt/d/hf_w24/ascii_census_w24.py --gate /mnt/d/hf_w23/gate --expect 8 \
        --out /mnt/d/hf_w24/g24_asciifailend.txt          # MUST exit 1 and name 8 of 8 files
echo "ascii FAIL-end rc=$?"
python3 /mnt/d/hf_w24/ascii_census_w24.py --gate /mnt/d/hf_w24/gate --expect 8 \
        --out /mnt/d/hf_w24/g24_ascii.txt
echo "ascii PASS-end rc=$?"                               # 0 = F79 holds; 1 = F79 RE-OPENED, wave CONTINUES

# --- the gate proper --------------------------------------------------------------------------------------
python3 /mnt/d/hf_w12/score_w12.py     /mnt/d/hf_w24/gate --rule G24 2>&1 | tee /mnt/d/hf_w24/g24_score.txt
python3 /mnt/d/hf_w24/prov_w24.py      --self-test /mnt/d/hf_w24/gate 2>&1 | tee /mnt/d/hf_w24/prov_w24.txt
python3 /mnt/d/hf_w24/score_g24_w24.py --gate /mnt/d/hf_w19/gate --out /mnt/d/hf_w24/g24_failend.txt
echo "gate scorer FAIL-end rc=$?"                          # MUST be non-zero
python3 /mnt/d/hf_w24/score_g24_w24.py --gate /mnt/d/hf_w24/gate --out /mnt/d/hf_w24/g24_p2p3.txt
echo "gate scorer PASS-end rc=$?"

# --- G24-P3b: P1's inertness on the SHIPPING path, eight integers, exactly ---------------------------------
python3 - <<'PY' 2>&1 | tee /mnt/d/hf_w24/g24_p3b.txt
import glob, json, os
K = {'toml999':16,'CWhiteFocus':101,'uneven':488,'muggsie':550,'mccomiskey':31,
     'D18_m24_deep_shed':18,'D19_cygnus_deep_shed':35,'D20_m24_bright_control':19}
ok = cnl = 0
for run, want in sorted(K.items()):
    hits = sorted(glob.glob('/mnt/d/hf_w24/gate/%s/**/optimized_settings.json' % run, recursive=True))
    if not hits:
        print('  COULD-NOT-LOOK %-24s no landing found' % run); cnl += 1; continue
    got = json.load(open(hits[0])).get('RecommendedStepSize')
    same = (got == want)
    ok += same
    print('  %-24s got %-6r want %-6r %s' % (run, got, want, '' if same else '<<< MOVED'))
print('G24-P3b: %d of 8   could-not-look: %d' % (ok, cnl))
raise SystemExit(0 if (ok == 8 and cnl == 0) else 1)
PY
echo "G24-P3b rc=$?"

# --- the interlock. FAIL end first, and it must leave NO marker -------------------------------------------
python3 /mnt/d/hf_w24/score_g24_w24.py --arm-gate /mnt/d/hf_w19/gate 2>&1 | tee /mnt/d/hf_w24/g24_armfailend.txt
echo "arm-gate FAIL-end rc=$?"                             # MUST be non-zero (BuildId novelty)
test ! -f /mnt/d/hf_w24/G24_PASSED && echo "ASSERTED: the FAIL end left NO marker" \
  | tee -a /mnt/d/hf_w24/g24_armfailend.txt
python3 /mnt/d/hf_w24/score_g24_w24.py --arm-gate /mnt/d/hf_w24/gate 2>&1 | tee /mnt/d/hf_w24/g24_armpassend.txt
cat /mnt/d/hf_w24/G24_PASSED
```

**Apply design §4.3 as written.** `G24-1` or `G24-P3b` short of 8 of 8 → **`G-STOP`, the wave stops, no arm
runs, and the finding is named.** `G24-P4` non-zero → **reported, F79 re-opened, the wave continues.**

---

## 6. RULE B24's AFTER arm (~50 m, cumulative ≈ 00:25Z)

```bash
nohup bash /mnt/d/hf_w24/b24_arm_w24.sh --phase after > /mnt/d/hf_w24/b24_after.log 2>&1 &
head -8 /mnt/d/hf_w24/b24_after.log; tasklist.exe | grep -c TestApp.exe
```

22 S4-request cells + `D08`/S1 + `D11`/S1 + the three labelled degenerate-fit controls `D01`/`D02`/`D03` S1.
The driver **ABORTS without `/mnt/d/hf_w24/G24_PASSED`**. Then:

```bash
tail -20 /mnt/d/hf_w24/b24_after.log && cat /mnt/d/hf_w24/B24_AFTER_READY
python3 /mnt/d/hf_w24/score_b24_w24.py --b24 /mnt/d/hf_w24/B24_AFTER_READY \
        --out /mnt/d/hf_w24/b24_score.txt
echo "B24 rc=$?"     # read DIRECTLY from $?, never through a pipe (wave 20 shipped a scorer that exited 0
                     # on a FAIL, and a scorer read through a pipe reports the pipe's status)
```

---

## 7. RULE R24's regression arm (~45 m, cumulative ≈ 01:15Z)

```bash
nohup bash /mnt/d/hf_w24/b24_arm_w24.sh --phase regress > /mnt/d/hf_w24/r24.log 2>&1 &
```

S0 × 20 on B14, paired per cell against **wave 23's own S0 reports**, whose paths the driver writes into
manifest column 6 so the scorer never rebuilds them ([F74](../docs/followups.md)).

```bash
tail -20 /mnt/d/hf_w24/r24.log && cat /mnt/d/hf_w24/R24_READY
python3 /mnt/d/hf_w24/score_b24_w24.py --r24 /mnt/d/hf_w24/R24_READY --out /mnt/d/hf_w24/r24_score.txt
echo "R24 rc=$?"
```

**THIS IS THE SHIP GATE FOR P2.** `R-PRESERVED` or `R-EXPLAINED` → P2 ships. `R-BROKEN` or `R-UNEVALUATED` →
**restore the code agent's byte backup of `SynthValidateRunner.cs`, delete the P2 tests, re-run the suite by
COUNT, and record P2 as a costed recommendation.** P1 and P3 survive by construction (different files).

---

## 8. The S2 census (~18 m, cumulative ≈ 01:35Z) — **DROP D1**

```bash
nohup bash /mnt/d/hf_w24/b24_arm_w24.sh --phase census > /mnt/d/hf_w24/census.log 2>&1 &
tail -20 /mnt/d/hf_w24/census.log && cat /mnt/d/hf_w24/S2_CENSUS_READY
python3 /mnt/d/hf_w24/score_b24_w24.py --d24 /mnt/d/hf_w24/B24_AFTER_READY \
        --census /mnt/d/hf_w24/S2_CENSUS_READY --out /mnt/d/hf_w24/d24_score.txt
echo "D24 rc=$?"
```

If the census is cut, still run `--d24` **without** `--census`: `D24` then reports the narrow end only and the
scorer says so in its own words.

---

## 9. Close out (~1 h 15 m, cumulative ≈ 02:50Z; **start the analysis agent at 01:35Z at the latest**)

```bash
# AFTER fingerprints -- and unlike wave 23, THE OUTPUT IS KEPT
python3 /mnt/d/hf_w24/v23_fingerprint_w24.py --check /mnt/d/hf_w24/v23_report_fingerprint_BEFORE.json 2>&1 \
  | tee /mnt/d/hf_w24/v23_report_fingerprint_AFTER.txt
python3 /mnt/d/hf_w15/bank_fingerprint_w15.py --check /mnt/d/hf_w24/bank_landing_fingerprint_BEFORE.json 2>&1 \
  | tee /mnt/d/hf_w24/bank_landing_fingerprint_AFTER.txt
python3 /mnt/d/hf_w17/aux_fingerprint_w17.py  --check /mnt/d/hf_w24/bank_aux_fingerprint_BEFORE.json 2>&1 \
  | tee /mnt/d/hf_w24/bank_aux_fingerprint_AFTER.txt
python3 /mnt/d/hf_w19/arm_fingerprint_w19.py  --check /mnt/d/hf_w24/w18_arm_fingerprint_BEFORE.json 2>&1 \
  | tee /mnt/d/hf_w24/w18_arm_fingerprint_AFTER.txt
sha256sum /mnt/d/hf_w23/exe/TestApp.dll /mnt/d/hf_w24/exe/TestApp.dll \
  | tee /mnt/d/hf_w24/binaries_final.txt        # B13 unchanged, B14 distinct

# item L24 -- ~12 m, DROP D2, and only if the clock allows
nohup bash /mnt/d/hf_w24/gapprobe_w24.sh > /mnt/d/hf_w24/gap.log 2>&1 &   # copy gapprobe_w23.sh, sed w23->w24

# the full suite, by COUNT, never the tick (F37). Kill stragglers first.
powershell.exe -NoProfile -Command "Get-Process testhost,vstest.console -EA SilentlyContinue | %{ \$_.Kill() }"
dotnet.exe test "$(wslpath -w /home/ghilios/src/hocus-focus/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" \
  -c Debug --nologo 2>&1 | tee /mnt/d/hf_w24/suite.log | tail -20
grep -E "Passed!|Failed!|Passed:|Failed:" /mnt/d/hf_w24/suite.log
```

**Baseline 3960** (wave 23 §12, controller-verified). Name every added test and the delta. **If the count is
not 3960 + N, the delta is named before anything is called green.** The known-flaky
`SendAsync_WritesOnABackgroundThread` is unrelated — do not chase it, and do not pipe the run in a way that
masks the exit code.

**Then spawn the ANALYSIS agent.** Its first action is:

```bash
ls -la --time-style=full-iso /mnt/d/hf_w24; date -u +%FT%H:%M:%S.%NZ
```

**with the timestamp printed at the top of `docs/synthetic-af-bank-followups-wave24-results.md`**, and anything
still open at that instant marked open **in place**. It applies the pre-registered branch tables and **must not
re-decide one**; design §9 carries its five standing obligations.

**Commit, push, append a wave-24 section to PR #195, verify CI by COUNT read out of the log:**

```bash
gh pr view 195 --json body --jq .body > /tmp/pr195.md   # append, never replace
gh run list --branch ghilios/synthetic-af-bank-followups-wave23 --limit 2 \
  --json status,conclusion,headSha --jq '.[]|"\(.headSha[0:7]) \(.status) \(.conclusion // "-")"'
```

---

## 10. Drop order, and the clock checkpoints

| checkpoint | if it is later than | do |
|---|---|---|
| A-BEFORE arm done | **22:15Z** | take **D1** (the S2 census) now, before the gate |
| code committed + B14 built | **23:20Z** | take **D2** (item L24) |
| gate scored | **00:05Z** | take **D3** (the `D01`/`D02`/`D03` S1 controls from the AFTER arm) |
| A-AFTER arm done | **01:00Z** | take **D4** (R-AFTER's expensive S0 tail: `D04`, `D05`, `D13`, `D16`, `D19`), keeping **>= 17** paired or RULE R24 is `R-UNEVALUATED` **and P2 does not ship** |
| anything | **01:45Z** | stop starting arms. Score what exists, spawn the analysis agent, and name everything not run **with its price** |

**Never droppable:** the gate; the A-BEFORE/A-AFTER pairing; `D08`/S1 and `D11`/S1 on both arms; the P1 and P3
code. **A partial gate reproduction stops the wave.**

---

## 11. The traps, as a checklist the controller can tick

- [ ] `--self-test` before every arm, never after
- [ ] WSL paths to scorers, Windows paths to `TestApp.exe`, and never the reverse
- [ ] `$?` read **directly**, never through a pipe
- [ ] one `TestApp.exe` at a time; no NINA during any arm; no fan-out
- [ ] B13 never rebuilt, never written into; B14 built once
- [ ] the dlls hashed, never `TestApp.exe`; `git diff --name-only` recorded verbatim
- [ ] no `grep` on any redirected `TestApp` log for any clause ([F79](../docs/followups.md))
- [ ] `--out` never inside a bank root and never inside `/mnt/d/hf_w23`
- [ ] every marker written by the code that computes the verdict, on `rc == 0`, never by hand
- [ ] every FAIL-end demonstration `tee`'d to a file that survives the wave
- [ ] byte backups for every mutation; **never** `git checkout --`
- [ ] the suite read by **COUNT**; CI read by **COUNT** out of the log
- [ ] the results doc's first action is the artifact listing, with the timestamp printed at the top
