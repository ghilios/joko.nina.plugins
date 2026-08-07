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
- [ ] **RULE G:** all 8 `BestJ` reproduce to 6 dp against wave 5's feature-OFF arm. Any miss ⇒ **STOP**, the arm is
      unreadable against wave 5's φ table and the wave reports that instead.
- [ ] Record the `detection binning (F39b)` line for each of the 8 — the 5 real runs must read
      `1 from harness_settings.json … Bin1`, the 3 synthetic `1 from synthetic_meta.json`.

### 1b. The three arms

- [ ] `D:\hf_w9\f32_arms.sh` — arms in the order **C → B → A** (§0.3: A last, so the banks end holding the
      shipped-default landing), fan-out **3 concurrent runs**, each arm's runs on distinct bank folders (F15-safe).
  - **A** — no flag (shipped default)
  - **B** — `--keep-floor 0.50`
  - **C** — `--continue-rounds 2`, on the **binding set ∪ the 8-run comparability subset** (pre-registered
    narrowing, §1.2)
- [ ] Population: 20 synthetic datasets + 19 real-bank runs (`astrodet` excluded — F14, frameless).
- [ ] **Concurrency control (free):** arm A's 8 comparability runs must still match the sequential gate to 6 dp.
      A disagreement is a fan-out artifact, not a result.

### 1c. Recall — the measurement wave 5 could not make

- [ ] `bank-verify --runs "D:\Autofocus Bank" --opt-a` against arm A's and arm B's landings, for
      `recall@SNR≥12` per run.

### 1d. Score against the pre-registered rules

- [ ] **R1** (adoption): median Δ`J` ≥ 0 on binding runs **and** recall ≥ on a majority with no run −0.05
      **and** no σ_focus regression > 20 %.
- [ ] **R2** (attribution): arm C recovering ≥ 50 % of B's median gain ⇒ expose restarts, not the floor.
- [ ] **R3** (population): the floor regressing `J` on more newly-covered runs than it improves ⇒ R1 fails
      regardless of the binding-set median.
- [ ] **F15 obligation:** if R1 adopts φ = 0.50 as a default, **re-run arm B alone** so the banks hold the adopted
      landing. If not, arm A already left them correct. Record which happened.

---

## Step 2 — F19(c): the free check, before scheduling anything expensive (item 2)

- [ ] **RULE F19c** — resolve the floor from data already on disk. Every floor-clamped dataset asks for *less*
      than 0.5 s, so a lower floor moves all 11 **down**; the one measured exposure ladder (`D02`, arm E) has
      σ_focus falling as exposure **rises** (44 % better at 8 s). ⇒ **the floor stays, nothing re-renders.**
- [ ] **RULE CEIL** — the ceiling direction (`D10` 335.6 s → 30, `D17` 40.4 → 30), never examined before.
      Product: cap stays (a 9-point sweep at 335 s is ~50 min; `AutoFocusTimeout` would kill it) and
      `DescribeExposureDerivation` already names which bound bound it — **verify, do not change**.
      Bank fidelity: `D10`/`D17` are *rendered* photon-starved relative to their own physics — **flag, do not
      re-render**.
- [ ] Record both in F19 with the deferral's price stated (the mistake wave 8's §0.1 made was deferring on a
      prediction instead of a price).

---

## Step 3 — F19's real remainder: the wing statistic (item 3) — everything downstream depends on this

### 3a. Instrument first (measurement, not code)

- [ ] Per-frame wing diagnostics in the harness: focuser position, accepted-star count, `NTarget`-th SNR,
      gate/flat rejections — **computed unconditionally**, never gated on a product display condition (wave 8
      lesson 4).
- [ ] Re-run arm X's 10 optimizations over `D:\hf_w7\armE\t{0.5,1,2,4,8}` (~8 min, **no re-render**).
- [ ] **Look at the wing structure before choosing a form.** If `D02`'s wing frames are indistinguishable from its
      near-focus frames, the wing hypothesis is refuted and the wave says so.

### 3b. Choose from the pre-declared candidate family (C1 `S_wing` / C2 `n_wing` / C3 `min(S_now, S_wing)` /
### C4 C1-gated-by-C2), then implement in `ExposureRecommender`

- [ ] **RULE W**, all four clauses, on `D:\hf_w7\armE`:
      **W1** `D02`@0.5 s asks ≥ 2× · **W2** `D16`@2 s asks < 1.25× · **W3** `D02`'s ask is non-increasing across
      0.5→8 s and < 1.25× by 8 s · **W4** `D16`@0.5 s still asks for more.
      Fires on both or neither ⇒ **not validated**.
- [ ] Unit tests that **discriminate** — each confirmed by neutralizing the change and re-running, not asserted.
- [ ] **§3.5 population check:** verdict measured on all 20 synthetic datasets + the real bank; every newly-firing
      dataset needs a stated reason. `D10`/`D17` are the pre-registered expected fires.
      **BLOCKED BEHIND STEP 1, and the block is structural rather than a scheduling choice.** `optimize --per-run`
      writes `optimized_settings.json` back into the bank's run folders (F15) and there is no flag to suppress it,
      so a population pass run while F32's arm is in flight would race it on every folder. It runs **after arm A**,
      with `exe2` at arm A's exact invocation — which makes it **free as an inertness control too**: `exe2` differs
      from the arm's `exe` only by the inert `FrameDiagnostics` diagnostic, so its landings must be bit-identical
      to arm A's, and the bank keeps the shipped-default landing either way.

---

## Step 4 — F52(c): the abort/re-expose advice (item 4), gated on Step 3

- [ ] **RULE A:** ships only if Rule W passed. If not, F52 records a second wave blocked on F19 — a result, not a
      gap.
- [ ] Computed from the **seed evaluation** (available in the first minute, which is the point — the user asked
      two hours in).
- [ ] Keep wave 8's separation: **facts about COST are already shown and need no statistic; ADVICE needs one.**
- [ ] Names **Cancel**, which exists (house rule: never describe an action whose control is hidden). **Absent**
      when the wings are healthy.

---

## Step 5 — F49 and F51 (item 5), both downstream of Step 3

- [ ] **F49(a)** — a fourth `RemedyFor` branch for floored-gate + not-exposure-limited, triggered against the
      **new** statistic. Names a control that EXISTS: **Brightness Sensitivity** + the wizard's **use-current**
      mode. It may **not** name `MinDetectionKeepFraction` (no XAML binding anywhere) and would not bind here
      anyway (this landing keeps ~7× *more*, the admission end of the axis).
- [ ] **F51(a)** — gate `ShowCaptureNewSweep` on *any* material recommendation, exposure **or step size**.
- [ ] **F51(b)** — carry the recommended **step size** into the re-capture (`AutoFocusEngineOptions.AutoFocusStepSize`,
      beside the existing exposure override), or say plainly that it will not.
- [ ] **F51(c)** — re-capturing must not require Accept.
- [ ] **House-rule check:** if the button becomes visible on a step-size-only recommendation, the body copy must
      gain a sentence naming it.

---

## Step 6 — Verification and PR

- [ ] Full suite locally: `dotnet.exe test … -c Debug --nologo`. **Record the COUNT**, not just "green".
      Baseline: `develop` @ `018eaa0` = **3674**.
- [ ] Do **not** run the suite while an optimize arm is in flight (wave 7: a competing full-suite run produced a
      spurious failure). Note `SendAsync_WritesOnABackgroundThread` is a known-flaky EAT test, unrelated.
- [ ] Inertness control: `synth-bank --dry-run` derived parameters vs `develop`, all 20 datasets — with a filter
      that is checked for **what it matches** (wave 8's greps `detectionBinning=` and the `exposure definition`
      PROSE contains that substring).
- [ ] Write `docs/synthetic-af-bank-followups-wave9-results.md`: every pre-registered rule reported **as it
      landed**, including the ones that fire against this wave.
- [ ] **FLAG** new findings in `docs/followups.md`; do not fix inline.
- [ ] PR to `develop`. **Never push `develop`.** Commit with
      `322725+ghilios@users.noreply.github.com` as author **and** committer.
- [ ] At PR time: `gh pr checks` **and** githubstatus.com. An **absent** check is reported as absent, never as
      passed.

---

## Known deferrals, priced rather than predicted

| item | why not this wave | price of waiting |
|---|---|---|
| F19(c) re-render | Rule F19c resolves "floor stays" from data on disk | none — a re-render would relocate the symptom, not the cause |
| the 30 s ceiling | no exposure ladder exists above 30 s; rendering one re-renders the item-1-critical bank | `D10`/`D17` stay rendered photon-starved; flagged in F19 |
| F46(b) — present binning's cost | needs its own before/after on live detection behaviour | unchanged from wave 8 |
| F52(d) — a cost term in `J` | same shape as F32 in wall time; wants F32's verdict first | one wave |
