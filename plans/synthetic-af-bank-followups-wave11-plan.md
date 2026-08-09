# Synthetic AF bank — followups wave 11 (plan)

Design: [`docs/synthetic-af-bank-followups-wave11-design.md`](../docs/synthetic-af-bank-followups-wave11-design.md).
Register: [`docs/followups.md`](../docs/followups.md).

**Branch:** `ghilios/synthetic-af-bank-followups-wave11` off `develop` @ `a54c48b`. Never push `develop`.
**Artifacts:** `D:\hf_w11\` — `gate/`, `det/`, `bisect/`, `pop/`, and the scripts named below.

**Two binaries, and the split is load-bearing:**

| directory | what it is | used for |
|---|---|---|
| `exe/` | `a54c48b` — **pre-fix**, built once, **never rebuilt** (F53(c)) | the gate; the bisect (which must measure what the PROFILE supplies); probe phases S/C/P |
| `exe_fixed/` | the same tree **plus** the F58 fix | K5/K7/K8 — the re-measurement, which only means something against a pre-fix reference |

**Two settings files, and the second one is a deliberate break with F42's byte-identity rule:**

| file | md5 | contents |
|---|---|---|
| `pinned_settings.json` | `df7c7cd1…` | a byte-copy of wave 10's, itself identical across waves 5–10 |
| `pinned_settings_w11.json` | `a67ffc06…` | the same **plus** the four fit inputs at `astrodet`'s effective values (`MaxOutlierRejections=0`, `OutlierRejectionConfidence=0.95`, `WeightedHyperbolicFitEnabled=True`, `HyperbolicFitModel=Hybrid`) |

The break is justified and must be stated in the results: **the old file pinned less than it claimed.** A file
that also pins the fit inputs to the values wave 10's coordinate system was measured under is *more* comparable,
not less — and K8 is the clause that proves it rather than asserting it.

**Build:**
`dotnet.exe build "$(wslpath -w /home/ghilios/src/hocus-focus/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w11\exe'`
**Tests:**
`dotnet.exe test "$(wslpath -w /home/ghilios/src/hocus-focus/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`

**Standing rules for every task below.** Every `optimize` invocation passes **both** `--settings` and
`--profile-id`. Every script states its pinned settings file **and its profile id** in its header. Nothing runs
concurrently unless a task says so deliberately, and `ConcurrencyCheck` is read out of the landing afterwards
regardless. Tests run at the milestones marked **[TEST]** and as a final gate — not after every edit.

---

## Task 0 — Branch, artifact tree, and the pre-gate probe

**This runs before anything is built or changed, because it can invalidate the wave's headline for 40 seconds.**

1. `git checkout -b ghilios/synthetic-af-bank-followups-wave11` (from `develop` @ `a54c48b`).
2. `mkdir -p /mnt/d/hf_w11/{gate,det,bisect,pop}`; copy `pinned_settings.json` from `hf_w10` and **verify the
   md5 is `df7c7cd14181b4ace59b92b89e219f67`**.
3. Snapshot, read-only, into `D:\hf_w11\profiles_before.txt`: every `*.profile`'s `Name`, `Id` and `LastUsed`,
   plus each one's four fit inputs (`MaxOutlierRejections`, `OutlierRejectionConfidence`,
   `WeightedHyperbolicFitEnabled`, `HyperbolicFitModel`). This is the wave's record of the machine state that
   F58 is about, and it is taken **before** any run perturbs `LastUsed`.
4. **The pre-gate probe** (design §1.2), on wave 10's `D:\hf_w10\exe` so that no new build is involved: one
   `optimize --per-run --max-evals 1` on a **copy** of `toml999`, with **no `--profile-id`**, output to
   `D:\hf_w11\pregate\`.

**Verify:** the run prints `Profile: Default (b10b1d6d-80eb-4dc4-9d89-f1fbf64c2b34)`.

**Stop condition.** If it prints anything else, **stop and re-derive §0.1** before continuing: the model of
profile selection is wrong, F58 is not filed as written, and items 1 and 2 revert to wave 10's framing. Record
what it did print either way.

---

## Task 1 — File F58 in the register

Write **F58 — `optimize` acquires a DIFFERENT NINA profile per concurrent process, and the profile decides the
fit, so F55's "two attractors" are two values of `MaxOutlierRejections`** into `docs/followups.md`, containing:

- the four-step mechanism read out of `NINA.Profile` (`FileShare.Read` lock → `SelectProfile` false →
  `TryLoad`'s `SkipWhile` fall-through → `LastUsed = Now` **and save** on load);
- the wave-9 `C_*.log` profile/`BaselineJ` table and the five-profile fit-input table (design §0.1);
- what it explains and what it retires: the plugin `Parallel.For` sweep, the build probe, folder state, the
  bimodality, wave 9's "stable within a session, variable across them" clue, and wave 9's fan-out arm;
- the two operational consequences: **a pinned run rewrites the default for the next unpinned one**, and
  **`--profile-id` + fan-out currently fails loudly** (which is the right trade, not a fix);
- what is **not** claimed yet: this is a correlation over five profiles that already existed; Task 5's bisect is
  the intervention, and Task 4's K5 is the test that a fix removes the phenomenon.

Cross-link from **F55** (which keeps its measurements and loses its named mechanism — the `KappaSigmaNoiseEstimate`
gain stays as a measured fact about the function, withdrawn only as *this* defect's explanation) and from
**F57** (whose (c) this answers, subject to RULE C).

**Verify:** `F58` resolves from both cross-links; F55's and F57's status lines are updated to point at it.

---

## Task 2 — Build `D:\hf_w11\exe` and run the gate (RULE G11)

1. Build to `D:\hf_w11\exe`. Record `git rev-parse HEAD`, and the sha256 of `TestApp.dll` and
   `NINA.Joko.Plugins.HocusFocus.dll`, into the script header.
2. `strings D:\hf_w11\exe\NINA.Joko.Plugins.HocusFocus.dll | grep AtrousWaveletFast` — **must hit** (v2).
3. Write `D:\hf_w11\gate_repro_w11.sh` from wave 10's, with **`--profile-id ce3f3e63-8fd3-4b72-a0ca-d90db9441382`
   added to every invocation and named in the header** beside the settings file, and the build provenance above.
4. Run it sequentially. Score from `aggregate_summary.json`, **never** from the console
   `Optimization complete` line (`uneven` has none).

**Verify — RULE G11, the wave's ONE pass/fail clause.** All eight `BestJ` to 6 dp:
`toml999` 0.995784 · `CWhiteFocus` 0.996068 · `uneven` 0.996368 · `muggsie` 0.997195 · `mccomiskey` 0.976746 ·
`D18` 0.999882 · `D19` 0.999487 · `D20` 0.999738. **A partial reproduction is a failure. If it fires, STOP.**

**Record (measurements, not gates):** `ProfileId` = astrodet on all eight · `ConcurrencyCheck` = `exclusive` on
all eight · `DetectorVersion` = 2 · **`BuildId` DIFFERS from wave 10's** (a match means the build did not happen)
· `toml999` `BaselineJ` = 0.983477 · and, if every `BaselineJ` reproduces while any landing moves, say so — that
is F8 and it must not be absorbed.

---

## Task 3 — The `--cv-threads` flag and the provenance thread count

Small, and it lands before the probes so they can use it.

- `optimize --cv-threads <n>` → `Cv2.SetNumThreads(n)`; `optimize` prints `Cv2.GetNumThreads()` in its provenance
  line **unconditionally**, so the knob's connectedness is visible whether or not it was passed.
- Replaces wave 9's `HF_STAREVAL_PAR` env-var route, which measured the same configuration five times because
  WSL env vars do not reach a Windows process without `WSLENV`.

**Verify — empirically, not by unit test.** `--cv-threads 1` prints `OpenCV threads: 1`; omitting it prints the
machine default. **A unit test here would test `int.TryParse`**; what actually needs proving is that the knob
reaches OpenCV, and only a printed `Cv2.GetNumThreads()` from a real process shows that. This is deliberately
the *weaker*-looking option and the stronger check — wave 9's env-var sweep passed every unit test it could have
had and still measured one configuration five times.

---

## Task 4 — Item 1: F55(b) — confirm, fix, re-measure

### 4a — Confirm (K1–K4)

Write `D:\hf_w11\det\determinism_probe_w11.sh` from `D:\hf_w9\det\determinism_probe.sh`, changed only by: the
wave-11 exe, the wave-11 pinned settings, and **capturing each repeat's `Profile:` line and landing `ProfileId`**.
Each repeat keeps its own pristine copy of `D16_esprit550_ha3`; **the banks are never touched**. Then run phase C
a second time with `--profile-id` (K3).

**Verify:**
- **K1** — distinct phase-C `BaselineJ` values == distinct `MaxOutlierRejections` across the profiles actually
  loaded; repeats sharing a profile share a value to full double precision.
- **K2** — phase S loads one profile and returns one value, 5 of 5.
- **K3** — pinned phase C: exactly one success, four hard failures (`No active NINA profile could be loaded`,
  non-zero exit).
- **K4** — phase S landings stamp `exclusive`; phase C landings stamp `concurrent`. **If phase C stamps
  `exclusive`, wave 10's guard does not work and that is a finding of its own.**

### 4b — Fix: pin the fit inputs the way the detector knobs are pinned

1. Build the harness's `AutoFocusOptions` on `harnessSettings.Accessor` (the existing
   `internal AutoFocusOptions(IProfileService, IPluginOptionsAccessor)` seam) instead of on the profile.
2. Carry `MaxOutlierRejections`, `OutlierRejectionConfidence`, `WeightedHyperbolicFitEnabled` and
   `HyperbolicFitModel` in the harness settings file, exported from the profile on bootstrap exactly as the
   detector knobs are.
3. Add `ProfileInputs` to `OptimizerProvenance` as **values, not a hash** —
   `MaxOutlierRejections=1;OutlierRejectionConfidence=0.99;WeightedHyperbolicFitEnabled=True;HyperbolicFitModel=Hybrid`
   — printed by `optimize` and stored in every landing.
4. **The live plugin is not changed.** The wizard and the AF engine keep reading `AutoFocusOptions` from the
   profile; those are the user's settings. Only the harness's notion of "pinned" changes.

**[TEST]** — and each must be confirmed discriminating by neutralizing the behaviour and re-running:
- the four keys round-trip through the harness settings file and reach the fit;
- an existing pinned file **without** the keys behaves as the documented code defaults (1 / 0.95 / true / Hybrid)
  and the file's own bootstrap warning names them;
- the four keys are **not** in the `UseAdvanced = False` preset-override set (F42(3)) — asserted by measurement
  against `SimpleModePresetOverrides`, not by a comment;
- `ProfileInputs` changes when any one of the four changes, and is stable when none do.

### 4c — Re-measure (K5, K7, K8) and the residual (K6)

Re-run on `exe_fixed/` with `--settings pinned_settings_w11.json`.

**Verify — K5, the clause this item is judged on:** phase C returns **one** `BaselineJ`, identical to phase S to
full double precision, **while the five `Profile:` lines still differ**. If the profiles stop differing, the test
has stopped exercising the hazard and the result means nothing.

**K7 — cross-profile identity, and it is the crispest statement of "pinned".** `toml999` at `--max-evals 1`
under `--profile-id astrodet` and under `--profile-id Default`, on the fixed binary with the extended file, must
return **identical `BaselineJ` to full double precision**. Before the fix these two differ by **0.0144**, which
is larger than any Δ`J` this project has argued about. Two runs, ~1 minute.

**K8 — the coordinate system survives its own fix.** The eight gate runs, re-run on `exe_fixed/` with
`pinned_settings_w11.json`, must return **RULE G11's eight values**. Without this, every future wave inherits an
unverified discontinuity at the exact commit that claimed to remove one.

> **K8 costs nothing, because the instrument already exists in this wave's own plan.** Task 6c's population pass
> is the *shipped-default invocation* over both banks — the same invocation as the gate — and all eight gate runs
> are among its 39. So running Task 6c on `exe_fixed/` with the extended pinned file makes K8 a free read of
> eight of its rows, exactly as wave 10's population pass was RULE G10-A at 8-fold redundancy. **Do not spend a
> separate 40 minutes on it.**

**Fan-out stays OFF for the population pass, and this is deliberate.** K5 establishes agreement at the SEED
level (`--max-evals 1`). F55's landing rate was **44 %**, triple its seed rate, and nothing in this wave has
measured landings under concurrency with the fix in place. Fanning out a 3-hour pass on the strength of a
seed-level result is precisely the over-reach this register keeps punishing. The pass runs **sequentially**, and
the ~3 h is paid.

**K6 — the residual, run once either way:** phase C with `--verbose`, comparing the multiset of
`Structure Map K-Sigma Noise Estimate: <σ> … NumIterations=<n>` trace lines between runs.
- Instrument control first: **phase C must still split with `--verbose` on** when the fix is disabled. If it does
  not, the instrument destroys what it measures and that is the reported result.
- σ multiset identical ⇒ `KappaSigmaNoiseEstimate` exonerated; its measured gain stays as a fact about the
  function and is withdrawn as this defect's explanation.
- σ differs **and** iterations differ ⇒ a direct observation of the named mechanism.
- σ differs and iterations identical ⇒ the flip is inside `Cv2.MeanStdDev`, a *different* mechanism, and F55 is
  rewritten to say so. Only then run `--cv-threads 1` as corroboration.

Claims are limited to what the instrument supports: the trace carries **no frame id**, so this is a multiset
comparison and a per-detection rate, never a per-frame attribution.

---

## Task 5 — Item 2: F57(c) — RULE C, the one-field bisect

On **copies** of `toml999` (F15), `--max-evals 1`, on the **pre-fix** code path or with the harness override
bypassed, so that the profile is still what supplies the fit inputs — this measures the profile, not the fix.

Synthetic bisect profiles: copy `astrodet`'s `.profile`, give it a fresh GUID (matching the filename and the
inner `<Id>`) and a distinguishable `Name`, change **one** field, drop it into the profile folder.
**The user's real profiles are never edited.** Delete the synthetic files afterwards and re-verify
`profiles_before.txt`'s two real profiles are byte-unchanged apart from `LastUsed`.

**Verify — RULE C:**
- **C0** `astrodet ⇒ J_astro`, `Default ⇒ J_def`, measured today. If either differs from wave 10's 0.983477 /
  0.997840, the bisect is void and *that* is the finding.
- **C1** `astrodet` + `MaxOutlierRejections=1` + `OutlierRejectionConfidence=0.99` == `J_def` to full double
  precision. A residue means an unenumerated profile read; report it, do not explain it away.
- **C2** `astrodet` + `MaxOutlierRejections=1` alone — equal to `J_def` ⇒ single-field cause; short of it ⇒ both
  are load-bearing.
- **C3 (negative control)** `astrodet` + `OutlierRejectionConfidence=0.99` alone == **exactly `J_astro`**. **Any
  movement discards C1 and C2.**
- **C4** every bisect run prints its synthetic profile in `Profile:` and stamps it into `ProfileId`.

**Then ship (b):** add the `--profile-id` rule to `.claude/docs/testapp-cli.md` beside F42's `--settings` rule,
with F58's reason — the default is LRU-by-last-load, so an unpinned arm is seeded by whatever the previous arm
pinned.

---

## Task 6 — Item 3: F19's successor, `WingRejectedRatio`

### 6a — Implement the measurement, with the divide-by-zero contract

`WingRejectedRatio` = wing-third ÷ inner-third, computed from the per-frame table the harness already writes.
Four distinguishable states, and the scorer must be shown a **positive control for each**:

| state | value | meaning |
|---|---|---|
| not measured | `UNEVALUATED` | the scorer refuses to present a partial pass as a population verdict |
| cannot be placed on the wing axis | `NaN` | *"we could not look"* — never 0 |
| inner third populated, rejects nothing, wings do | `+∞` | the strongest real signal |
| otherwise | the ratio | — |

`Newtonsoft` writes the middle two as the **strings** `"NaN"` and `"Infinity"`. A scorer that coerces either to a
number disables its own falsification rule.

**[TEST]** — the four states, each confirmed discriminating by neutralizing.

### 6b — Fix the threshold BEFORE the pass — and the free read says there may be no threshold to fix

W1–W4/W5/W6 are quoted verbatim from wave 10 and are **not** renegotiated. The threshold comes from W1–W4 on
wave 7's exposure ladder and W5's floor (**above 1.39**), and is written into `wing_pop_w11.sh`'s header before
the pass runs.

**The ladder is on disk with its per-frame table** — not in wave 7's own aggregates (they predate
`FrameDiagnostics`) but in **`D:\hf_w9\wing2\`**, the 10-rung probe wave 9 left behind. Read at zero compute:

| dataset | rung | wing | inner | **ratio** |
|---|---|---|---|---|
| `D02_rich_135mm` | 0.5 s | 0.6729 | 0.4290 | **1.5683** |
| | 1 s | 0.2909 | 0.2816 | 1.0329 |
| | 2 s | 0.3048 | 0.3465 | 0.8798 |
| | 4 s / 8 s | 0.0000 | 0.0027 / 0.0235 | 0.0000 |
| `D16_esprit550_ha3` | 0.5 s | 0.0000 | 0.0000 | 1.0000 |
| | 1 s | 0.0000 | 0.0000 | 1.0000 |
| | **2 s** | **0.0064** | **0.0000** | **+∞** |
| | 4 s / 8 s | 0.0000 | 0.0000 | 1.0000 |

> **W2 IS UNSATISFIABLE AT ANY FINITE THRESHOLD.** `D16` at 2 s — the rung where its σ_focus is minimised, and
> the control the whole statistic exists to pass — has an inner rejected fraction of **exactly 0.000** against a
> wing fraction of 0.0064, so its ratio is **+∞** and it fires at every threshold. W1 ∧ W5 leaves the window
> `(1.39, 1.5683]`; **W2 empties it.**
>
> **And the mechanism is worse than the arithmetic.** On a clean, well-exposed narrowband run the core rejects
> *nothing*, so **any** wing rejection at all — 0.64 % here — becomes an infinite ratio. The ratio form is
> maximally unstable exactly where the statistic is required to be silent. The absolute fraction got this rung
> **right** (0.0064 ≪ 0.20). On the one control that matters, the successor is not merely no better than its
> predecessor — it is **strictly worse**.
>
> *(W4 does not constrain the threshold: wave 9's table shows the wing probe ALONE failing W4, with the clause
> satisfied by the accepted-star half's 3.00× through `max(shipped, probe)`. Recorded so W4 is not miscounted as
> a second contradiction — W2 is sufficient on its own.)*

**But `D:\hf_w9\wing2\` was measured on the v1 binary under an UNRECORDED profile**, which is F58's own hazard,
and F58 is this wave's headline. So the refutation is not published from it. The table above is instead the
**pre-registered prediction** for a pinned re-measurement:

**6b(i) — re-run the 10 rungs** on `exe_fixed/` with `--settings pinned_settings_w11.json` **and** `--profile-id`,
from the frames still at `D:\hf_w7\armE\t{0.5,1,2,4,8}` (**no re-render**). ~20 minutes.

**Verify:** `D16`@2 s reproduces **+∞** (inner third rejects exactly 0) and `D02`@0.5 s reproduces **≈1.57**.
- Reproduced ⇒ **W2 is unsatisfiable on pinned provenance**, the successor is refuted before implementation, and
  §6c's population pass is no longer needed *to decide the verdict* — it still runs, for K8 and for the
  measurement's own population statistics, but it is not what decides.
- **Not** reproduced ⇒ the ladder moved between binaries or profiles, which is F54's shape and a finding in its
  own right; the threshold is then fixed from the new ladder and §6c decides as originally planned.

### 6c — The population pass, alone and last

39 runs, both banks, sequential, shipped-default invocation, `--settings` **and** `--profile-id` pinned. Reuse
`D:\hf_w10\wing_pop.sh`'s **validated** concurrency guard rather than rewriting it. Nothing else may be in flight
(F15). Check `ConcurrencyCheck` in every landing afterwards regardless.

**Verify — RULE P, unchanged:** P1 (`D10`/`D17` — at least one fires) · **P2 (> 50 % of 39 ⇒ REFUTED)** ·
P3 (`D16` at its derived exposure must not fire) · P4 (NaN on > 25 % ⇒ the instrument is not connected).

**Named exclusions, never silent:** `lumos` (rc=3 reproducibly, `WingRejectedFraction` NaN) · `astrodet` the
dataset (frameless, F14) · `Panos` (degenerate sigma fit).

**Expected, and pre-stated so that meeting it is not a surprise:** `D17` fires at 0.59 and cannot fire on a
ratio ≥ threshold; `D20_m24_bright_control` has an inner fraction of exactly 0.000. **Refutation is the likely
outcome and is priced in.** If it is refuted, `WingRejectedRatio` follows its predecessor: the **measurement**
stays, the **action** does not ship, and **`WingRejectedExcess` = wing − inner** is named as the next candidate
and **barred from this pass's rows**.

---

## Task 7 — Full suite, results, PR

1. **[TEST]** `dotnet.exe test … -c Debug --nologo`. **Verify the COUNT, not the tick** (F37): `develop` is at
   **3722**; the new total must be 3722 + (tests added), and every added test accounted for by name. Do not pipe
   to `tail` — it masks the exit code. `SendAsync_WritesOnABackgroundThread` is a known-flaky EAT serial-transport
   test and is unrelated; re-run it individually rather than chasing it.
2. Write `docs/synthetic-af-bank-followups-wave11-results.md` with the provenance banner carrying **`BuildId`,
   `DetectorVersion`, `ProfileId` and `ConcurrencyCheck` as fields** — wave 10 wrote that banner by hand for the
   last time.
3. Update `docs/followups.md`: F58 (Task 1, with the intervention result folded in), F55 (narrowed to what
   survives), F57 ((c) answered, (b) shipped as a run rule), F19 (the successor's verdict), F15 (still open, and
   still what forces the population pass to run alone).
4. Commit with the privacy email; push the branch; open the PR. **Never push `develop`.**

---

---

## CLOSED AGAINST WHAT ACTUALLY HAPPENED (2026-08-08)

Results: [`docs/synthetic-af-bank-followups-wave11-results.md`](../docs/synthetic-af-bank-followups-wave11-results.md).

| task | planned | what happened |
|---|---|---|
| 0 pre-gate probe | predict `Default` | **as predicted**, and it returned `toml999 = 0.99784` — wave 9's value. An unpinned gate would have reproduced the wrong wave |
| 1 file F58 | from wave 9's logs | done, and **two** more datasets confirmed it from `ctl_conc_*.log` at zero compute |
| 2 gate | RULE G11 | **PASS 8/8 to 6 dp**; all eight `BaselineJ` reproduce wave 10 too |
| 3 `--cv-threads` | flag + printed count | shipped; positive control passes (1→1, 4→4, absent→48). **Never needed** — recorded, not used |
| 4a confirm | K1–K4 | **all PASS**, and K4 exposed a real limit in wave 10's guard (§3.2) |
| 4b fix | harness accessor + `FitInputs` | shipped — **and the density test found F59**, a separate defect that dropped five detector knobs from the pinned file for six waves |
| 4c re-measure | K5, K7, K8, K6 | **K5/K7 PASS**. **K6 was not needed**: no residual divergence exists to explain, so `KappaSigmaNoiseEstimate` is withdrawn as F55's mechanism |
| 5 bisect | RULE C | **C0/C1/C3 PASS, C2 SINGLE-FIELD** — one integer, residue exactly `0.0` |
| 6a–6b measurement + threshold | ship, then size | measurement shipped; **the threshold could not be sized — RULE W2 is unsatisfiable** |
| 6c population pass | 39 runs, ~3 h | **NOT RUN.** Item 3 was refuted first on a necessary clause; the pass could not change the verdict and W6 bars its rows from sizing the next candidate. **K8 was run standalone instead** (8 runs, ~40 min) |
| 7 suite | count, not tick | **3740 = 3722 + 18**, every added test named |

**Three things went differently from the plan, and all three are recorded in the results rather than absorbed:**

1. **F59 is a defect the plan did not anticipate**, found by a test written for *density* rather than presence.
   `pinned_settings.json` is missing `MaxDistortion`, `StarCenterTolerance`, `SaturationThreshold`,
   `HotpixelThreshold` and `Sensitivity`. Waves 5–11 stay internally valid (all used the same file and the same
   code defaults); what is false is that the file *describes* the detector.
2. **The 39-run pass was dropped**, and what that costs is stated in the results (§5.6): no population
   distribution for `WingRejectedRatio`.
3. **`exe_fix2` exists** because F59 landed after `exe_fixed` was built. Per F53(c) an arm's directory is never
   rebuilt, so a new one was made and K5/K7 were re-run on it — both reproduced exactly.

---

## What would make this wave wrong, in one place

- The pre-gate probe prints `astrodet` ⇒ §0.1's model is wrong and F58 does not get filed as written.
- RULE G11 fires ⇒ nothing downstream is readable; stop.
- **C3 moves** ⇒ the bisect harness is not doing what it claims and C1/C2 are discarded.
- **K5 fails while the five profiles still differ** ⇒ the fit inputs were not the whole cause and something below
  the fit is still moving.
- K5 "passes" but the five `Profile:` lines are identical ⇒ the test stopped exercising the hazard and proves
  nothing.
- The suite total is not 3722 + (named new tests) ⇒ an absent check, which is more dangerous than a red one.
