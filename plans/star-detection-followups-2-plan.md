# Star Detection Optimizer — Follow-ups (Round 2) Plan

**Context.** PR #56 (`ghilios/star-detection-followups`) is merged to `develop` (merge `60dc0ba`). It shipped the
9 wizard/labeling feedback items + F1/F2/F3/F5, a ~6.7× optimizer speedup (staged Phase-B search), and reverted
F6 (a verified wash). This round captures everything **deferred or left unvalidated** by that PR. Empirical
record: `docs/star-detection-optimization-wizard-results.md` (F2/F3/F6 + cache-health notes). Round-1 plan:
`plans/star-detection-followups-plan.md` (with its "Execution status (final)" section).

**Branch:** create `ghilios/<topic>` off `develop` per item (or one branch for the A-items, which are coupled).
Never push `develop` directly; PR to merge. **Commit identity:** `George Hilios
<322725+ghilios@users.noreply.github.com>` (author + committer), per CLAUDE.md.
**After each task:** `cmd.exe /c "dotnet build Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"` +
`dotnet test ... -c Debug`; fix root causes. New detector behavior stays **opt-in / default-OFF ⇒ detection
star counts AND objective `J` bit-identical when disabled** (unit-test against an independent re-impl, as F3 did).

**Tooling already in place.** `TestApp` harnesses (`optimize` / `review` / `diagnose-labels`) drive the same
optimizer the wizard uses; AF-runs bank at `D:\Autofocus Bank` (attempt-anchored; **Panos** has ground-truth
labels at `Panos\labels`; the user's live NINA profile copies live on `E:\WorkshopData\...`). Build TestApp:
`cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"`; exe at
`./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe`.

> **Known facts to carry in (from PR #56's empirical work):**
> - The defocus gate (`DefocusAwareGates`) is now a curated optimizer variable, BUT on Panos the optimizer
>   recovered recall via `Sensitivity`↓ + `StructureLayers`↑ and **left the gate OFF** — so the gate's real
>   utility and the F3 precision penalty (`SDefocusPrecision`) are **unit-tested only, never triggered on data**.
> - Panos labels are all `wrongly-rejected` (recall); there are **no `should-reject` (precision) labels** yet,
>   so the precision side of the objective is unexercised.
> - 4/5 flagged Panos donut misses are **NO CANDIDATE** structure gaps (no candidate forms) — not fixable by
>   any gate; needs detector-algorithm work.
> - **Wizard-saved labels are keyed by the sanitized ABSOLUTE run path** (e.g.
>   `E__WorkshopData_..._attempt01.json` under `<attemptFolder>\labels\`), so offline `optimize --labels`
>   against a different-path copy of the same run **will not auto-match** them (the in-wizard re-optimize is
>   fine — it uses in-memory labels). See G3.

---

## Priority A — quality work that changes detection/optimization (do these together; they share data needs)

### G1 — Precision validation + penalty tuning with `should-reject` labels (closes out F3)
F3's `SDefocusPrecision` near-focus penalty + the `DefocusAwareGates` curated variable are merged but **never
exercised on real data** (gate not selected on Panos; no `should-reject` labels exist). Goal: prove the
gate+penalty do something useful, and tune the constants against data.
- **Data:** label `should-reject` (false positives) on a near-focus-heavy setup (and/or a setup where the
  defocus relaxation IS the cleanest path to donuts) — e.g. open `TestApp review` (mode-less: click an
  **accepted** star to flag should-reject) or the in-wizard review, on a 2nd setup beyond Panos.
- **Verify:** `optimize --labels` shows the labeled loop improves recall *without tanking precision*; confirm
  the penalty actually bites when relaxation admits near-focus junk; the unlabeled path stays bit-identical.
- **Tune:** `OptimizationObjective.ObjectiveConstants.{NearFocusWindowSteps (1.5), DefocusPrecisionThreshold
  (0.20), DefocusPrecisionStrength (0.5), DefocusPrecisionMinFactor (0.5)}` — conservative defaults today.
- **Files:** `OptimizationObjective.cs`, `RunEvaluationData.cs` (per-frame relaxed counts/positions), the
  `review`/`diagnose-labels` harnesses. **Deliverable:** results note appended to the results doc.

### G2 — Structure-detection for NO-CANDIDATE dim/donut misses (F4 + #5)
The unsolved core: ~4/5 flagged Panos donut misses never form a candidate (donut flux removed by the à-trous
wavelet residual subtraction). Investigate a **defocus-aware `StructureLayers` increase** (#5: more/larger
wavelet scales survive larger structures; `StructureLayers` is an early-cache param + sets the post-wavelet
blur radius) and/or multi-scale handling. **Prototype opt-in / default-OFF.**
- **Verify:** `diagnose-labels` on Panos shows previously `NO CANDIDATE` boxes become candidates **without**
  exploding the near-focus candidate count; default-OFF stays bit-identical.
- **Files:** `StarDetector.cs` (wavelet/candidate path ~`ComputeResidualAtrousB3SplineDyadicWaveletLayer`,
  `CollectStarCandidates`, the early cache-key allow-list), `CvImageUtility.cs` (B3-spline filters). Exploratory
  — scope a spike first; likely its own PR. Pairs naturally with G1 (both want labeled Panos/2nd-setup data).

---

## Priority B — UX polish (small, surfaced by real use)

### G3 — Wizard label-save UX: surface the path + stable run key
Two sub-items found when labels were hard to locate after a real review:
1. **Surface the save location in the wizard** (the path is only in the NINA log today). After review/save,
   show "Labels saved to …" (ideally an open-folder affordance) in the Review/Summary step.
2. **Use a stable, non-absolute run key + cleaner filename.** Today `StarReviewLabelStore.FileNameFor` sanitizes
   the **absolute** run path → `E__WorkshopData_..._attempt01.json`, so offline `optimize --labels` against a
   different-path copy won't match (the embedded `runId` is the absolute path too). Pick a stable run identity
   (relative-to-runs-root, or a short hash, consistent between the wizard loader and `OptimizationRunDiscovery`)
   so the wizard's labels and the offline harness agree.
- **Files:** `StarDetectionOptimizerWizardVM.cs` (`DeriveLabelsDir`, the review-step VM/XAML), `Review/
  StarReviewLabels.cs` (`FileNameFor`/`Save`/`Load`), `RunEvaluationLoader.cs` (runId derivation),
  `OptimizationRunDiscovery.cs` (offline runId). **Keep the label JSON shape compatible** with existing files.

---

## Priority C — verify shipped-but-unconfirmed behavior (verification, fix only if off)

### G4 — Live-NINA verification of the bits not exercised in PR #56's manual pass
Confirmed already: #6 (wizard renders), #7 (embedded review), #9 (Accept applies). **Still unconfirmed:**
- **#8 re-optimize loop** — run review → "Re-optimize with my feedback" → updated Summary → Accept, end to end.
- **#3 imaging-pane button** — confirm the launch button appears + works from the *Imaging-tab* Star Detection
  Options dock (not just the Options page).
- **#1/#2 in the standalone `TestApp review`** dev tool (mode-less click + dashed markers; same shared control).
- **Wizard "Live" source mode** — running a fresh AF sweep instead of replay is hardware-dependent and was built
  "minimal, manually verified later"; never exercised. (`StarDetectionOptimizerWizardVM.RunLiveAttemptAsync`.)
Fix anything that's off; otherwise just check the boxes.

---

## Priority D — optional

### G5 — Optimizer perf: remaining headroom (only if optimize is still too slow on the biggest sensors)
T14 (staged search) got ~6.7×. Further levers documented in the cache-health note: **Option B** (bounded
multi-context LRU per frame — ~2×, bit-identical, but ~6.6 GB peak at 61 MP → needs a memory-budget guard);
**Option C** (early-stop early-axis probing). Also: the staged search's early-stop is a one-way latch (a
deliberate speed/quality trade) and its trajectory differs slightly from pre-T14 — revisit if quality matters.
**Files:** `StarDetectionOptimizer.cs` (staged `PatternSearch`), `RunEvaluationData.cs` (the per-frame context
cache), `StarDetector.cs` (`BuildDetectionContext`).

### G6 — Doc sync (minor)
`docs/star-detection-optimizer-performance-design.md` still describes the original 10–13× cache story; add the
staged-search fix + the early-probe-thrash caveat (the results doc + CLAUDE.md are already current).

---

## Suggested order & checkpoints
**G1 + G2 together** (one branch, or G1 first then G2) — the substantive next PR; both need labeled data, and
G1 should checkpoint before any objective-constant change ships. → **G3** (quick UX win, own PR) → **G4**
(verification, fold fixes into whichever PR is open) → **G5/G6** as time permits. Global: full `dotnet test`
green; default-OFF ⇒ bit-identical; PR each to `develop`.

---

## Execution status (2026-06-16, branch `ghilios/sd-followups2-g2-g6`)

**Key correction:** most of this plan's *code* had already shipped in PR #61 (`8e5124c`) + T14 (`a765c9a`),
which landed after the plan was written — verified against the code, not assumed. This session (user-selected
scope: G2 validation, G6 doc sync, G5 perf; G1 deferred) did:

- **G2 — DONE (validation).** The `DefocusAwareStructure`/`StructureLayerBoost` mechanism was already coded; this
  session validated it on Panos via new `diagnose-labels --defocus-structure/--structure-boost/--structure-layers`
  switches. Result: boost=1 recovers the 2 large NO-CANDIDATE *donuts* → candidates (then `TooSmall` fragments);
  the 2 remaining NO-CANDIDATE boxes are a **faint near-focus floor** (not structure-erased); bit-identical OFF
  proven (boost=0 ≡ baseline); boost=1 is the sweet spot; default-OFF correct. Complementary to gates, not
  sufficient alone. Written up in `docs/star-detection-optimization-wizard-results.md` (§ "G2 — Structure-boost
  validation").
- **G5 — partial (instrumentation shipped; B/C + early-stop rejected on evidence).** Added cache instrumentation
  (`RunEvaluationData.ContextBuilds/ContextReuses` + `optimize` runner readout). Measured: T14 already drives
  normal runs to **99.3% reuse / 9 builds (the floor)** ⇒ **Options B and C are moot**. A convergence early-stop
  was prototyped + unit-tested but measured **no benefit** on any bank run/budget/tolerance (the runs never
  converge before the eval budget) ⇒ **reverted, not shipped**. The only further lever is lowering the eval
  budget (a quality default decision, left to the user). See `docs/star-detection-optimizer-performance-design.md`
  § 8.
- **G6 — DONE.** `performance-design.md` § 8 + Status caveat: T12b cache-thrash → T14 staged-search fix → measured
  reuse → B/C moot → early-stop rejected → instrumentation.
- **G1 — DEFERRED** (needs `should-reject`/false-positive labels that don't exist; documented in the results doc).
- **G3 part-1 (wizard "Labels saved to…" surface) and G4 (live-NINA verification)** — not selected this session.
