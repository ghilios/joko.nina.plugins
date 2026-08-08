# Synthetic AF bank — followups wave 9 (plan)

Design: [`docs/synthetic-af-bank-followups-wave9-design.md`](../docs/synthetic-af-bank-followups-wave9-design.md).

Branch: `ghilios/synthetic-af-bank-followups-wave9`, off `develop` @ `018eaa0` (PR #184's merge).
Artifacts: `D:\hf_w9\`. Wave-7 artifacts that must survive: `D:\hf_w7\armE\t{0.5,1,2,4,8}` (4.7 GB — item 3's
validation set), `D:\hf_w7\f18arms\`, `D:\hf_w7\golden\`.

Build: `dotnet.exe build "$(wslpath -w <abs>/Joko.NINA.Plugins/TestApp/TestApp.csproj)" -c Release -o 'D:\hf_w9\exe'`
Tests: `dotnet.exe test "$(wslpath -w <abs>/Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo`

---

## Step 0 — Pre-flight ✅ DONE

- [x] **PR #184 merged?** Yes — 2026-08-06T23:33:27Z → `018eaa0`.
- [x] **Did its CI ever run?** Yes — `gh pr checks 184` → 1 passed, run `31130194942` on head `57e759e`,
      **`Passed: 3674, Failed: 0, Skipped: 0`**. Count verified per F37, not just the green tick.
- [x] **The red runs on the branch/`develop`** are job-level `cancelled` with zero test output — outage, not
      regression.
- [x] **githubstatus.com** — indicator `major`, **`Actions: major_outage` still**. `develop` @ `018eaa0` has
      **0 checks**. Wave 9's PR must be gated on a LOCAL suite run and a re-read of the status page.
- [x] NINA not running (PostBuild `xcopy` trap clear). Branch created. `TestApp` built to `D:\hf_w9\exe`.
- [x] `D:\hf_w9\pinned_settings.json` copied — **byte-identical** to waves 5/6/7/8 (`md5 df7c7cd1…`).

---

## Step 1 — F32's confirmation arm (item 1, the long pole)

### 1a. The comparability gate — RUN FIRST, blocks everything else in this step

- [x] `D:\hf_w9\gate_repro.sh` — the 8-run wave-5/6 subset, arm-A invocation exactly
      (`--per-run --max-evals 250 --settings <pinned>`, no binning flag), **sequential**.
- [x] **RULE G (sequential gate): PASS, 8 of 8 to 6 dp** against wave 5's feature-OFF arm.
- [x] **RULE G re-applied under the FAN-OUT (arm A): FAILS 2 of 8.** Applied as written ⇒ the arm is unreadable
      against wave 5's φ table, and the wave reports that instead of a φ verdict. Filed as **F55**.
- [x] Recorded the `detection binning (F39b)` line for each of the 8 — the 5 real runs must read
      `1 from harness_settings.json … Bin1`, the 3 synthetic `1 from synthetic_meta.json`.

### 1b. The three arms

- [x] `D:\hf_w9\f32_arms.sh` — ran as **B → C → A** at fan-out 4 (design §1.2 for why the order changed; A last,
      so the banks end holding the shipped-default landing). **The fan-out is what F55 indicts.**
  - **A** — no flag (shipped default)
  - **B** — `--keep-floor 0.50`
  - **C** — `--continue-rounds 2`. The pre-registered narrowing did NOT bind: the predicate ("the floor rejected
    a candidate mid-search") matched all 39, where wave 5's was about the LANDING. Recorded as a definitional
    miss; C ran full scope.
- [x] Population: 20 synthetic datasets + 19 real-bank runs (`astrodet` excluded — F14, frameless).
- [x] **Concurrency control (free): FAILED.** Arm A's 8 comparability runs do not all match the sequential gate.
      This is **F55**, and it is the wave's most consequential finding.

### 1c. Recall — the measurement wave 5 could not make

- [ ] ~~`bank-verify --opt-a` for `recall@SNR≥12`~~ **NOT RUN**: RULE G voided the arm, so there is no landing
      pair worth scoring recall against.

### 1d. Score against the pre-registered rules

- [ ] ~~**R1** (adoption)~~ **NOT REPORTED** — RULE G fired; publishing it would use a failed instrument.
- [ ] ~~**R2** (attribution)~~ **NOT REPORTED**, same reason.
- [ ] ~~**R3** (population)~~ **NOT REPORTED**, same reason.
- [x] **F15 obligation discharged:** arm A ran last, so both banks hold the shipped-default landing — which is
      the correct resting state given no φ was adopted.

---

## Step 2 — F19(c): the free check, before scheduling anything expensive (item 2)

- [x] **RULE F19c** — resolve the floor from data already on disk. Every floor-clamped dataset asks for *less*
      than 0.5 s, so a lower floor moves all 11 **down**; the one measured exposure ladder (`D02`, arm E) has
      σ_focus falling as exposure **rises** (44 % better at 8 s). ⇒ **the floor stays, nothing re-renders.**
- [x] **RULE CEIL** — the ceiling direction (`D10` 335.6 s → 30, `D17` 40.4 → 30), never examined before.
      Product: cap stays (a 9-point sweep at 335 s is ~50 min; `AutoFocusTimeout` would kill it) and
      `DescribeExposureDerivation` already names which bound bound it — **verify, do not change**.
      Bank fidelity: `D10`/`D17` are *rendered* photon-starved relative to their own physics — **flag, do not
      re-render**.
- [x] Recorded both in F19 with the deferral's price stated (the mistake wave 8's §0.1 made was deferring on a
      prediction instead of a price).

---

## Step 3 — F19's real remainder: the wing statistic (item 3) — everything downstream depends on this

### 3a. Instrument first (measurement, not code)

- [x] Per-frame wing diagnostics in the harness: focuser position, accepted-star count, `NTarget`-th SNR,
      gate/flat rejections — **computed unconditionally**, never gated on a product display condition (wave 8
      lesson 4).
- [x] Re-ran arm X's 10 optimizations over `D:\hf_w7\armE\t{0.5,1,2,4,8}` (~8 min, **no re-render**).
- [x] **Looked first — and it refuted this wave's own pre-registered candidate family** before implementation,
      via the gate floor theorem plus data already on disk.

### 3b. Choose from the pre-declared candidate family (C1 `S_wing` / C2 `n_wing` / C3 `min(S_now, S_wing)` /
### C4 C1-gated-by-C2), then implement in `ExposureRecommender`

- [x] **RULE W** — passed by `max(shipped, wing probe)` ONLY; 5 of 7 candidates failed, 3 of them after passing
      W1. Applied to this binary's own ladder after checking all four premises survived. All four clauses:
      **W1** `D02`@0.5 s asks ≥ 2× · **W2** `D16`@2 s asks < 1.25× · **W3** `D02`'s ask is non-increasing across
      0.5→8 s and < 1.25× by 8 s · **W4** `D16`@0.5 s still asks for more.
      Fires on both or neither ⇒ **not validated**.
- [x] Unit tests that **discriminate** — 8, four of them discriminating, each confirmed by neutralizing the
      change and re-running rather than asserted. Two of my own comments' claims were corrected that way.
- [ ] **§3.5 population check — STILL OWED.** The verdict across all 20 synthetic datasets + the real bank;
      every newly-firing dataset needs a stated reason, and `D10`/`D17` are the pre-registered expected fires.
      **EFFICIENCY FOR THE NEXT WAVE: this pass and F55's sequential arm-A re-run are the SAME PASS.** A
      sequential `optimize --per-run --max-evals 250 --settings <pinned>` over both banks with `exe2` reproduces
      arm A's landing (leaving the F15 state correct), gives the F55 control a clean answer, and writes the
      per-frame wing table the population check needs — three obligations, one ~7 h run.
      **BLOCKED BEHIND STEP 1, and the block is structural rather than a scheduling choice.** `optimize --per-run`
      writes `optimized_settings.json` back into the bank's run folders (F15) and there is no flag to suppress it,
      so a population pass run while F32's arm is in flight would race it on every folder. It runs **after arm A**,
      with `exe2` at arm A's exact invocation — which makes it **free as an inertness control too**: `exe2` differs
      from the arm's `exe` only by the inert `FrameDiagnostics` diagnostic, so its landings must be bit-identical
      to arm A's, and the bank keeps the shipped-default landing either way.

---

## Step 4 — F52(c): the abort/re-expose advice (item 4), gated on Step 3

- [x] **RULE A:** Rule W passed, so (c) ships.
- [x] Computed from the **seed evaluation** (available in the first minute, which is the point — the user asked
      two hours in).
- [x] Kept wave 8's separation: **facts about COST are already shown and need no statistic; ADVICE needs one.**
- [x] Names **Cancel**, which exists (house rule: never describe an action whose control is hidden). **Absent**
      when the wings are healthy.

---

## Step 5 — F49 and F51 (item 5), both downstream of Step 3

- [x] **F49(a)** — a fourth `RemedyFor` branch for floored-gate + not-exposure-limited, triggered against the
      **new** statistic. Names a control that EXISTS: **Brightness Sensitivity** + the wizard's **use-current**
      mode. It may **not** name `MinDetectionKeepFraction` (no XAML binding anywhere) and would not bind here
      anyway (this landing keeps ~7× *more*, the admission end of the axis).
- [x] **F51(a)** — and it had TWO causes, not one. — gate `ShowCaptureNewSweep` on *any* material recommendation, exposure **or step size**.
- [x] **F51(b)** — carries the recommended **step size** (NOT the offset; an existing test caught that) into the re-capture (`AutoFocusEngineOptions.AutoFocusStepSize`,
      beside the existing exposure override), or say plainly that it will not.
- [x] **F51(c)** — re-capturing must not require Accept.
- [x] **House-rule check:** if the button becomes visible on a step-size-only recommendation, the body copy must
      gain a sentence naming it.

---

## Step 6 — Verification and PR

- [x] Full suite locally: **3693 passed / 0 failed** (baseline 3674, +19). Count recorded, not just 'green'.: `dotnet.exe test … -c Debug --nologo`. **Record the COUNT**, not just "green".
      Baseline: `develop` @ `018eaa0` = **3674**.
- [x] Note: the suite DID overlap the arm, and the one failure it found was a **real regression of mine**
      (F51(b) carrying the offset), not a flake — so the overlap cost nothing and caught something. (wave 7: a competing full-suite run produced a
      spurious failure). Note `SendAsync_WritesOnABackgroundThread` is a known-flaky EAT test, unrelated.
- [x] Inertness control: `synth-bank --dry-run` reproduces the saturated set exactly (13 of 20) derived parameters vs `develop`, all 20 datasets — with a filter
      that is checked for **what it matches** (wave 8's greps `detectionBinning=` and the `exposure definition`
      PROSE contains that substring).
- [x] Wrote `docs/synthetic-af-bank-followups-wave9-results.md`: every pre-registered rule reported **as it
      landed**, including the ones that fire against this wave.
- [x] **FLAGGED** new findings (F53, F54, F55) in `docs/followups.md`; do not fix inline.
- [x] PR #185 to `develop`. **Never push `develop`.** Commit with
      `322725+ghilios@users.noreply.github.com` as author **and** committer.
- [x] At PR time: githubstatus.com **all operational** (unlike wave 8), and PR #185's check scheduled and ran **and** githubstatus.com. An **absent** check is reported as absent, never as
      passed.

---

## Known deferrals, priced rather than predicted

| item | why not this wave | price of waiting |
|---|---|---|
| F19(c) re-render | Rule F19c resolves "floor stays" from data on disk | none — a re-render would relocate the symptom, not the cause |
| the 30 s ceiling | no exposure ladder exists above 30 s; rendering one re-renders the item-1-critical bank | `D10`/`D17` stay rendered photon-starved; flagged in F19 |
| F46(b) — present binning's cost | needs its own before/after on live detection behaviour | unchanged from wave 8 |
| F52(d) — a cost term in `J` | same shape as F32 in wall time; wants F32's verdict first | one wave |
