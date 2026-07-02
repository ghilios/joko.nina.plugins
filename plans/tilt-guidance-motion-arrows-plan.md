# Tilt Guidance Motion Arrows & Rotation Glyphs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `docs/tilt-guidance-motion-arrows-design.md`: re-anchor the guidance ⬆/⬇ arrows to adapter motion (⬆ = toward the objective, fixed on every rig), put ⟳/⟲ rotation glyphs on all three numeric rows, and move a simplified legend to the top of the guidance section.

**Architecture:** Presentation-layer only. One new pure formatter static (`FormatAmount`) replaces `FormatMagnitude`/`FormatTotal`; `BuildDirectionLegend` becomes fixed-text; `InspectorVM.RebuildTiltGuidance`/`FillNumericGuidance` compute motion signs and per-row rotation signs from existing state (σ = `ScrewInwardCurvatureSign`); the wizard step summary swaps its ↑/↓ rotation glyphs for ⟳/⟲ so ⬆-as-rotation survives nowhere. No calculator, storage, prompt, or TestApp changes.

**Tech Stack:** .NET 8 (`net8.0-windows7.0`), NUnit 4.4 + NSubstitute, WPF XAML, MkDocs.

---

## Context for a zero-context engineer

- **Repo root:** `/home/ghilios/src/hocus-focus`. Work on branch `ghilios/tilt-calibration-ux` (already checked out; PR #117 is open on it — these are additional commits). **Never push to `develop`.**
- **Build + full test suite** (run after every task; on WSL the binary is `dotnet.exe`; it also compiles the XAML):
  ```bash
  dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
  ```
  Baseline at plan time: 1688 passed / 0 failed.
- **Committing** (author and committer must both use the noreply address):
  ```bash
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>"
  ```
- **Domain semantics (read `docs/tilt-guidance-motion-arrows-design.md` first — it is the contract):**
  - σ = `ITiltAdapterOptions.ScrewInwardCurvatureSign` ∈ {+1, −1} (0 unreachable from options; resolve defensively to `TiltScrewGeometry.DefaultScrewInwardCurvatureSign`). +1 = a clockwise screw turn raises the curvature effect.
  - "Adapter moves toward the objective" ⇔ the local best-focus position decreases. Consequences used below (derivations in the design spec):
    - Tilt motion sign = `−σ × (CW-positive tilt turns value)`.
    - Backfocus motion is σ-free: ⬆ (toward objective) ⇔ `CurvatureEffectMicrons > 0` (the σ in "rotation needed" and the σ in "what rotation does" cancel).
    - Rotation signs: Tilt `sign(TiltMicrons)` (σ-free); Backfocus `sign(σ·BackfocusMicrons)`; Total `sign(TiltMicrons + σ·BackfocusMicrons)` (= `SignedTotalAdjustment`).
  - Glyphs: ⟳ = U+27F3 (clockwise/tighten), ⟲ = U+27F2 (counter-clockwise/loosen). The em dash "—" and minus "−" (U+2212) in snippets are intentional — copy exactly.
- **Line numbers below are positions at plan time** — use the quoted code as anchors.

---

### Task 1: Formatter statics — `FormatAmount` and the fixed-text legend

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/TiltScrewGuidanceRow.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` (only the two call sites that must change to keep the build green: the legend call and the three `FormatMagnitude`/`FormatTotal` row calls)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/TiltAdapterGuidanceVMTests.cs`

- [ ] **Step 1: Rewrite the formatter tests (failing first).** In `TiltAdapterGuidanceVMTests.cs`, DELETE these tests: `FormatMagnitude_Screws_ShowsTwoDecimalTurns`, `FormatMagnitude_Steppers_RoundsToWholeSteps`, `FormatTotal_Screws_UsesClockwiseWording`, `FormatTotal_Steppers_UsesSignedSteps`, `FormatTotal_NearZeroAndBoundary_TakesAbsBeforeSign` (and its TestCase attributes), and `BuildDirectionLegend_DescribesAdapterMotionAndProvenance`. ADD:

```csharp
    [Test]
    public void FormatAmount_Screws_ShowsMagnitudeWithRotationGlyph() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(1.25, steps: false), Is.EqualTo("1.25 ⟳"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.5, steps: false), Is.EqualTo("0.50 ⟲"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.001, steps: false), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.004, steps: false), Is.EqualTo("—"));
        });
    }

    [Test]
    public void FormatAmount_Steppers_ShowsSignedSteps() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(35.2, steps: true), Is.EqualTo("+35 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-35.2, steps: true), Is.EqualTo("−35 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.5, steps: true), Is.EqualTo("+1 steps"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(0.4, steps: true), Is.EqualTo("—"));
            Assert.That(TiltAdapterGuidanceVM.FormatAmount(-0.2, steps: true), Is.EqualTo("—"));
        });
    }

    [Test]
    public void BuildDirectionLegend_IsFixedTextWithProvenanceSuffix() {
        Assert.Multiple(() => {
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: true),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in turns"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: false, signIsMeasured: false),
                Is.EqualTo("⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in turns (assumed — set or measure in the Tilt Adapter Wizard)"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: true, signIsMeasured: true),
                Is.EqualTo("⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts"));
            Assert.That(TiltAdapterGuidanceVM.BuildDirectionLegend(steps: true, signIsMeasured: false),
                Is.EqualTo("⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts (assumed — set or measure in the Tilt Adapter Wizard)"));
        });
    }
```
Keep `FreshGuidance_HasNoDirectionLegend` unchanged.

- [ ] **Step 2: Run to verify failure.** `dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~TiltAdapterGuidanceVMTests"` → FAIL (compile errors: `FormatAmount` missing, `BuildDirectionLegend` wrong arity).

- [ ] **Step 3: Implement in `TiltScrewGuidanceRow.cs`.** Replace `BuildDirectionLegend` (lines ~70–81), `FormatMagnitude` (~83–94), and `FormatTotal` (~96–113) with:

```csharp
        /// <summary>
        /// Fixed legend for the guidance table: ⬆/⬇ describe adapter-plate motion (toward the
        /// objective / toward the camera — pure physics, identical on every rig), and ⟳/⟲ (screws)
        /// or the +/− step sign (steppers) describe the rig-specific rotation that produces it.
        /// "(assumed)" flags a direction setting never verified by a wizard measurement.
        /// </summary>
        public static string BuildDirectionLegend(bool steps, bool signIsMeasured) {
            string body = steps
                ? "⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts"
                : "⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in turns";
            string assumed = signIsMeasured ? string.Empty : " (assumed — set or measure in the Tilt Adapter Wizard)";
            return body + assumed;
        }

        /// <summary>
        /// Format a signed per-screw adjustment. Positive = clockwise / the wizard-prompt "+" step
        /// direction. Screws render the magnitude with a rotation glyph ("1.25 ⟳" / "0.50 ⟲" — units
        /// live in the legend); steppers render signed whole steps ("+35 steps"). Values that round
        /// to nothing render as an em dash with no direction mark.
        /// </summary>
        public static string FormatAmount(double signedAmount, bool steps) {
            if (steps) {
                long rounded = (long)Math.Round(Math.Abs(signedAmount), MidpointRounding.AwayFromZero);
                if (rounded == 0) return "—";
                return signedAmount >= 0 ? $"+{rounded} steps" : $"−{rounded} steps";
            }
            if (Math.Abs(signedAmount) < 0.005) return "—";
            return $"{Math.Abs(signedAmount):0.00} {(signedAmount >= 0 ? "⟳" : "⟲")}";
        }
```

Also update the two stale comments on the field blocks: line ~45 (`// Per-screw tilt and backfocus magnitudes (direction is shown by the arrows above), and the` / `// signed total with an explicit direction (CW/CCW turns for screws, +/− steps for steppers).`) becomes:

```csharp
        // Per-screw signed adjustments, all three rows carrying their own rotation direction
        // (⟳/⟲ glyphs for screws, +/− signs for steppers). The arrows grid above describes adapter
        // MOTION (⬆ = toward the objective), not rotation — the two answer different questions.
```
and line ~66 (`// One-line legend tying the arrows/totals to physical adapter motion; empty when unknown.`) becomes `// One-line legend defining the motion arrows and rotation glyphs; empty until guidance renders.`

- [ ] **Step 4: Keep the build green — minimal `InspectorVM.cs` call-site fixes.** In `FillNumericGuidance` (~line 2004–2016), replace the loop body's three formatter calls (semantics land properly in Task 2; this step only restores compilation with equivalent-for-σ=+1 behavior):

```csharp
                var corr = TiltScrewGeometry.ScrewCorrectionMicrons(
                    model.Gx, model.Gy, model.Kx, model.Ky, model.X0, model.Y0, angles[i], radiusMicrons);
                tiltText[i] = TiltAdapterGuidanceVM.FormatAmount(corr.TiltMicrons / unitMicrons, steps);
                backText[i] = TiltAdapterGuidanceVM.FormatAmount(resolvedSign * corr.BackfocusMicrons / unitMicrons, steps);
                // The curvature sign applies ONLY to the backfocus component: the tilt component's
                // direction is already encoded by the stored response-convention screw angle (the same
                // convention the tilt arrows invert), so multiplying the whole total by the sign would
                // flip the tilt part on sign = -1 rigs and contradict the arrows.
                double totalSigned = TiltScrewGeometry.SignedTotalAdjustment(
                    corr.TiltMicrons, corr.BackfocusMicrons, unitMicrons, resolvedSign);
                totalText[i] = TiltAdapterGuidanceVM.FormatAmount(totalSigned, steps);
```
and above the loop replace
```csharp
            int curvatureSign = tiltAdapterOptions.ScrewInwardCurvatureSign;
            bool hasDirection = curvatureSign != 0;
```
with
```csharp
            // σ is never 0 from persisted options (defaulted since the direction-setting feature);
            // resolve defensively to the assumed default so every row can carry a direction.
            int curvatureSign = tiltAdapterOptions.ScrewInwardCurvatureSign;
            int resolvedSign = curvatureSign == 0 ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign : curvatureSign;
```
In `RebuildTiltGuidance` (~line 1962–1967), update the legend call:
```csharp
            if (guidance.HasTiltGuidance || guidance.HasNumericGuidance) {
                guidance.DirectionLegend = TiltAdapterGuidanceVM.BuildDirectionLegend(
                    steps: tiltAdapterOptions.AdjustmentType == TiltAdjustmentType.StepperMotors,
                    signIsMeasured: tiltAdapterOptions.ScrewInwardCurvatureSignIsMeasured);
            }
```

- [ ] **Step 5: Run the fixture filter → PASS, then the full suite.** Expect the wizard/inspector suites green; if any other test referenced `FormatMagnitude`/`FormatTotal` (grep the Tests project), update it to `FormatAmount` with the new expected strings and report it.

- [ ] **Step 6: Commit:**
```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): rotation-glyph amount formatter and fixed-text guidance legend"
```

---

### Task 2: InspectorVM — motion-anchored arrows

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/InspectorVM.cs` (`RebuildTiltGuidance`, ~lines 1883–1971)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/InspectorVMBehavioralTests.cs`

- [ ] **Step 1: Write the failing σ-flip behavioral test.** Read `InspectorVMBehavioralTests.cs` first — `TiltGuidance_CalibratedButUnmeasured_ShowsNoDirectionLegend` (added recently) shows how the fixture builds an InspectorVM via `BuildInspectorVM`/MediatorBundle and drives `RebuildTiltGuidance` state. Determine whether the fixture can also inject a `TiltModel.TiltPlaneModel` (for tilt arrows) and a `SensorModel` result (for the backfocus arrow + numeric rows).
  - **If it can** (preferred): add a test that runs the same guidance rebuild twice — identical model inputs, options mock returning σ = +1 then σ = −1 — and asserts the matrix: `Screw1TiltArrow` FLIPS between the runs; `Screw1BackfocusArrow` is IDENTICAL between the runs; `Screw1TiltAmount`'s glyph is IDENTICAL; `Screw1BackfocusAmount`'s glyph FLIPS. Name it `TiltGuidance_SigmaFlip_FlipsMotionArrowsNotTiltGlyphs`.
  - **If injecting a fitted model requires scaffolding the fixture doesn't have**: instead assert the backfocus-arrow σ-independence and tilt-arrow σ-dependence at whatever level the fixture DOES reach, and pin the remaining mapping with a comment in the production code referencing the design spec. State in your report which branch you took and why.
- [ ] **Step 2: Run to verify the new test fails** (arrows currently rotation-anchored: tilt arrows do NOT flip with σ; backfocus arrows DO).
- [ ] **Step 3: Implement in `RebuildTiltGuidance`.**
  1. Hoist the sign resolution above the tilt block (before `var tiltPlane = ...`, ~line 1890):
```csharp
                // σ resolved once for the whole rebuild; see FillNumericGuidance for the 0 rule.
                int curvatureSign = tiltAdapterOptions.ScrewInwardCurvatureSign;
                int resolvedSign = curvatureSign == 0 ? TiltScrewGeometry.DefaultScrewInwardCurvatureSign : curvatureSign;
```
     (Delete the now-duplicate `int curvatureSign = ...` declaration at ~line 1933; `FillNumericGuidance` keeps its own locals.)
  2. Tilt arrows become motion-anchored — replace the classifier input (~line 1913):
```csharp
                                // turns[i] is CW-positive (the stored response-convention angles encode
                                // the rig direction), so adapter MOTION toward the objective = −σ·turns.
                                double ratio = (-resolvedSign * turns[i]) / maxAbs;
```
     (Only this line changes; thresholds and glyph tiers stay.) Update the comment above the `turns` loop (~1901) to note the arrows show adapter motion, ⬆ = toward the objective.
  3. Backfocus arrows drop σ — replace the block at ~lines 1929–1946 with:
```csharp
                // Backfocus row: adapter MOTION needed to null the curvature effect. Toward the
                // objective ⇔ the local best-focus position must decrease; the σ in "which rotation
                // is needed" and the σ in "what a rotation does" cancel, so the motion arrow is
                // sign(CurvatureEffectMicrons) — rig-independent physics (see the design spec).
                if (SensorModel?.DisplayedSensorModel != null) {
                    double curvatureEffectMicrons = SensorModel.SensorModelResult.CurvatureEffectMicrons;
                    double absMicrons = Math.Abs(curvatureEffectMicrons);

                    string backfocusArrow;
                    if (absMicrons < BackfocusNoiseThresholdMicrons) {
                        backfocusArrow = "—";
                    } else {
                        bool towardObjective = curvatureEffectMicrons > 0;
                        string bigArrow = towardObjective ? "⬆" : "⬇";
                        string smallArrow = towardObjective ? "↑" : "↓";
                        backfocusArrow = absMicrons >= BackfocusLargeArrowThresholdMicrons ? bigArrow : smallArrow;
                    }

                    guidance.Screw1BackfocusArrow = backfocusArrow;
                    guidance.Screw2BackfocusArrow = backfocusArrow;
                    guidance.Screw3BackfocusArrow = backfocusArrow;
                    if (n == 4) guidance.Screw4BackfocusArrow = backfocusArrow;
                    guidance.HasBackfocusRow = true;
                }
```
     Sanity-check the `towardObjective` polarity yourself before committing: with `CurvatureEffectMicrons > 0`, the per-screw correction `BackfocusMicrons = −(kx·cx²+ky·cy²)` is negative = the local best-focus position must decrease = toward the objective ⇒ ⬆. If your reading of the model's sign conventions disagrees, STOP and report rather than guessing.
  4. Update the stale `FillNumericGuidance` doc comment (~1975–1979): the total no longer "carries an explicit direction (CW/CCW or +/− steps)" — all three rows carry a rotation glyph / signed steps, and the arrows above are adapter motion.
- [ ] **Step 4: Run the new test → PASS, then the full suite → all green.** The legend-gating behavioral test must still pass unchanged.
- [ ] **Step 5: Commit:**
```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(tilt): guidance arrows describe adapter motion; rotation carried by per-row glyphs"
```

---

### Task 3: Wizard step summary uses ⟳/⟲

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs` (`StepDescription`, ~lines 1127–1143)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs` (`StepDescription_ArrowsFollowClockwiseUpConvention`)

- [ ] **Step 1: Update the pinning test (failing first).** Rename `StepDescription_ArrowsFollowClockwiseUpConvention` to `StepDescription_UsesRotationGlyphs` and change every expected `↑` to `⟳` and `↓` to `⟲` (both 3- and 4-screw cases; e.g. `"All screws ⟳"`, `"Re-baseline (all ⟲)"`, `"Screw 1 ⟳, Screw 3 ⟲"`).
- [ ] **Step 2: Run to verify it fails**, filter `FullyQualifiedName~StepDescription_UsesRotationGlyphs`.
- [ ] **Step 3: Implement.** In `StepDescription`, replace `↑` → `⟳` and `↓` → `⟲` in all five case strings, and replace the method comment with:
```csharp
        // Description shown on each summary row, reflecting the single move performed for the step.
        // Rotation glyphs match the guidance legend (⟳ = clockwise / + steps, ⟲ = counter-clockwise
        // / − steps) — screw rotation, never adapter-plate motion (⬆/⬇ are reserved for motion in
        // the guidance table). Perturbation steps (all CW per StepInstructionsText) carry ⟳ and the
        // re-baseline undo moves carry ⟲. Internal for tests.
```
- [ ] **Step 4: Run the filter → PASS, then the full suite → all green.**
- [ ] **Step 5: Commit:**
```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(wizard): step-summary rotation glyphs match the guidance legend"
```

---

### Task 4: XAML — legend to the top, prominent

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml`

- [ ] **Step 1: Move the legend.** Delete the legend TextBlock currently after the numeric grid (~lines 3007–3014, anchored by the comment `<!--  Direction legend: what ⬆ / CW / + steps mean for this adapter  -->`). Insert this block inside the calibrated-guidance StackPanel as its FIRST child — immediately after `<StackPanel Visibility="{Binding HasTiltAdapterCalibration, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}">` and before the `<!--  No measurement yet  -->` TextBlock:

```xml
                            <!--  Direction legend: ⬆/⬇ = adapter motion, ⟳/⟲ = screw rotation. First
                                  element so the conventions are read before the table.  -->
                            <TextBlock
                                Margin="5,4,5,2"
                                Text="{Binding TiltGuidance.DirectionLegend}"
                                TextWrapping="Wrap"
                                Visibility="{Binding TiltGuidance.HasDirectionLegend, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}" />
```
  Note the deliberate styling change vs the old block: no `FontStyle="Italic"`, no `Opacity="0.7"` — prominence per the design spec. The `HasDirectionLegend` gating is unchanged, so the "Run a measurement to see guidance." placeholder state still shows no legend.
- [ ] **Step 2: Also fix the numeric-grid header comment** (~line 2961): `<!--  Precise numeric adjustments (turns / steps) — shown when adapter hardware is configured  -->` → `<!--  Precise numeric adjustments, each with its rotation glyph / step sign — shown when adapter hardware is configured  -->`.
- [ ] **Step 3: Build + full suite** (compiles the XAML) → all green.
- [ ] **Step 4: Commit:**
```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(inspector): guidance legend moves to the top of the section, non-italic"
```

---

### Task 5: Docs + follow-up-plan touch-up + final verification

**Files:**
- Modify: `documentation/docs/overview/tilt-aberration-inspector.md`
- Modify: `plans/tilt-ux-polish-plan.md` (screenshot expectations reference the now-obsolete "turns CW" layout)
- Check (grep only): all of `documentation/docs/`

- [ ] **Step 1: Rewrite the guidance-presentation paragraph** in `tilt-aberration-inspector.md` ("Correcting tilt with a tilt adapter" section — locate it with `grep -n "turns CW" documentation/docs/overview/tilt-aberration-inspector.md`). Replace the sentences describing totals/legend (added by PR #117) with wording that matches the new UI; keep the surrounding house voice:

  > The arrows describe what the adapter must do: ⬆ means that corner of the adapter plate moves toward the objective, ⬇ toward the camera — the same on every rig. The numeric rows each carry the screw rotation that produces the move: `1.25 ⟳` means 1.25 turns clockwise (tighten), `0.50 ⟲` counter-clockwise (loosen); stepper adapters show signed steps (`+35 steps`) matching the wizard's prompts. A legend at the top of the section defines both conventions and is marked "(assumed)" until the adapter direction has been measured in the wizard.

  Adapt the splice points to the actual current paragraph — do not duplicate content it already states (e.g. the "(assumed)" sentence exists today; merge rather than repeat).
- [ ] **Step 2: Sweep for stale examples:** `grep -rn "turns CW\|turns CCW\|⬆ = clockwise\|⬆ = + steps" documentation/docs/` → update every hit to the new vocabulary (expected hits: the inspector page; possibly the wizard page tip block). `grep -rn "1.25 turns" documentation/docs/` for stray examples.
- [ ] **Step 3: Touch up `plans/tilt-ux-polish-plan.md`** Task 5: change the `inspector-tilt-guidance.png` expectation text `(currently shows the pre-#117 IN/OUT guidance table; must show CW/CCW totals + the direction legend line)` to `(currently shows the pre-#117 IN/OUT guidance table; must show the motion-anchored arrows, per-row rotation glyphs, and the legend at the top of the section)`.
- [ ] **Step 4: Verify:** `mkdocs build --strict` (from repo root) → clean; full suite → all green.
- [ ] **Step 5: Commit and push (updates PR #117):**
```bash
git add -A
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs: motion-anchored guidance arrows and rotation glyphs"
git push
```
