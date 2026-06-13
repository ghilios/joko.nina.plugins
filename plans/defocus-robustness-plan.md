# Defocus Robustness (Step 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the inverted WideRange sensitivity preset (analysis finding F2) and document the deliberate decision to keep the TooFlat gate active during AutoFocus (finding F1), with a regression test.

**Architecture:** Two small, independent changes in the star-detection options/pipeline. The WideRange fix flips one sign in `ConfigureSimpleSettings()` (a real behavior change, regression-tested). The TooFlat change is documentation only — comments at the AF-override point and the gate recording why it stays active. No new options, no UI, no engine plumbing.

**Tech Stack:** C# / .NET 8 (`net8.0-windows7.0`), NUnit 4.4.0. Build/test via `rtk dotnet` (or `cmd.exe /c "dotnet …"`).

**Branch:** `ghilios/defocus-robustness` (already created off `develop`; this plan + the roadmap progress marker are already committed on it). Continue on this branch.

---

## Design decisions (from the step-2 brainstorm)

These were decided with the user; do not re-litigate during execution:

1. **TooFlat gate (F1): no behavior change.** It is *intentionally* left active during AutoFocus. Relaxing it risks admitting flat noise blobs, and `PeakResponse` is reused in the sensitivity calc so loosening it has side effects. Record the decision in comments; revisit later with a real defocus dataset if AF star counts drop at sweep extremes.
2. **WideRange (F2): flip the sign.** `BrightnessSensitivity += 2.0` → `-= 2.0` (10→8). `BrightnessSensitivity` is a threshold where *smaller = more sensitive*, so WideRange should lower it to match its "be more sensitive" intent.
3. **StructureLayers-from-HFR: deferred** to a later step (needs engine→params plumbing).
4. **Validation: synthetic CI test** (the WideRange direction) **+ optional local focus-sweep** before/after on a real AF run.

---

## Conventions for every commit

Use the required identity (project CLAUDE.md):

```bash
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "<subject>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

Build/test (10-min timeout; rtk build mislabels success header as `fail` — trust `errors=0`/exit code):

```bash
rtk dotnet test  Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptionsTests"
cmd.exe /c "dotnet build Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus\Joko.NINA.Plugins.HocusFocus.csproj -c Debug --nologo"
```

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs` | Modify | New regression test pinning WideRange = more sensitive (8 < 10). |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs` | Modify (`ConfigureSimpleSettings`, line ~92) | Flip WideRange sensitivity sign + corrected comment. |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs` | Modify (`GetStarDetectorParams`, line ~324) | Comment: TooFlat intentionally active during AF. |
| `Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs` | Modify (TooFlat gate, line ~830) | One-line cross-reference comment. |

---

### Task 1: Fix the WideRange sensitivity direction (TDD)

**Files:**
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs`

- [ ] **Step 1: Write the failing test**

Add this method inside the `StarDetectionOptionsTests` class (e.g. right after `SimpleMode_FocusRangeWideRange_BoostsStructureLayers`, ~line 156). It mirrors the existing `Build()` + `UseAdvanced=false` + set-preset pattern:

```csharp
    [Test]
    public void SimpleMode_FocusRangeWideRange_IncreasesSensitivity() {
        // WideRange targets faint/defocused stars, so it must make detection MORE sensitive than Typical.
        // BrightnessSensitivity is a threshold where SMALLER = more sensitive (regression guard for the
        // previously-inverted sign: it used to be raised to 12, making WideRange LESS sensitive).
        var (typical, _, _) = Build();
        typical.UseAdvanced = false;
        typical.Simple_PixelScale = PixelScaleEnum.Typical;
        typical.Simple_FocusRange = FocusRangeEnum.Typical;

        var (wide, _, _) = Build();
        wide.UseAdvanced = false;
        wide.Simple_PixelScale = PixelScaleEnum.Typical;
        wide.Simple_FocusRange = FocusRangeEnum.WideRange;

        Assert.Multiple(() => {
            Assert.That(typical.BrightnessSensitivity, Is.EqualTo(10.0));
            Assert.That(wide.BrightnessSensitivity, Is.EqualTo(8.0));
            Assert.That(wide.BrightnessSensitivity, Is.LessThan(typical.BrightnessSensitivity));
        });
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~SimpleMode_FocusRangeWideRange_IncreasesSensitivity"
```
Expected: FAIL — `wide.BrightnessSensitivity` is currently `12.0`, not `8.0` (asserts `Is.EqualTo(8.0)` and `Is.LessThan(10.0)` both fail).

- [ ] **Step 3: Flip the sign in `ConfigureSimpleSettings`**

In `StarDetectionOptions.cs`, replace this block (≈ lines 89-93):

```csharp
            if (Simple_FocusRange == FocusRangeEnum.WideRange) {
                StructureLayers += 1;
                // As we get further from focus, we want to be more sensitive as the chance for bad data increases
                BrightnessSensitivity += 2.0;
            }
```

with:

```csharp
            if (Simple_FocusRange == FocusRangeEnum.WideRange) {
                StructureLayers += 1;
                // As we get further from focus, we want to be more sensitive as the chance for bad data
                // increases. BrightnessSensitivity is a threshold where SMALLER = more sensitive, so we LOWER it.
                BrightnessSensitivity -= 2.0;
            }
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter "FullyQualifiedName~StarDetectionOptionsTests"
```
Expected: PASS — the new test passes and the existing `SimpleMode_FocusRangeWideRange_BoostsStructureLayers` (StructureLayers ≥ 5) is unaffected.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Fix inverted WideRange sensitivity preset (F2)" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Document the TooFlat-during-AF decision (F1)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs`

Documentation only — no behavior change, so no test. A build confirms it compiles.

- [ ] **Step 1: Add the decision comment at the AF override point**

In `HocusFocusStarDetection.cs` (`GetStarDetectorParams`, ≈ lines 323-326), replace:

```csharp
            // For AutoFocus, don't save intermediate data or model PSFs
            if (isAutoFocus) {
                detectorParams.SaveIntermediateFilesPath = string.Empty;
                detectorParams.ModelPSF = false;
            } else {
```

with:

```csharp
            // For AutoFocus, don't save intermediate data or model PSFs
            if (isAutoFocus) {
                detectorParams.SaveIntermediateFilesPath = string.Empty;
                detectorParams.ModelPSF = false;
                // Design decision (accuracy analysis F1): the TooFlat gate (StarDetector rejects candidates whose
                // median >= PeakResponse*peak) is intentionally left ACTIVE during AutoFocus. It can reject bright,
                // heavily-defocused flat-top/donut stars, but relaxing it here risks admitting flat noise blobs, and
                // PeakResponse is also reused in the sensitivity (NormalizedBrightness) calc so loosening it has side
                // effects. Revisit with a real defocus dataset (TestApp focus-sweep) if AF star counts drop at sweep
                // extremes.
            } else {
```

- [ ] **Step 2: Add a cross-reference comment at the gate**

In `StarDetector.cs` (≈ line 830), replace:

```csharp
            // Too flat
            if (starCandidate.StarMedian >= (p.PeakResponse * starCandidate.Peak)) {
```

with:

```csharp
            // Too flat. Intentionally active during AutoFocus as well — see
            // HocusFocusStarDetection.GetStarDetectorParams (accuracy analysis F1).
            if (starCandidate.StarMedian >= (p.PeakResponse * starCandidate.Peak)) {
```

- [ ] **Step 3: Build the plugin to confirm it compiles**

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus\Joko.NINA.Plugins.HocusFocus.csproj -c Debug --nologo"
```
Expected: build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/HocusFocusStarDetection.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs
git -c user.name="George Hilios" -c user.email="322725+ghilios@users.noreply.github.com" \
  commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "Document intentional TooFlat-during-AF decision (F1)" \
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Full verification, optional real-data baseline, and PR

**Files:** none (verification only).

- [ ] **Step 1: Run the full test suite**

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo
```
Expected: all tests PASS (the new WideRange test plus the prior 858).

- [ ] **Step 2 (OPTIONAL — needs a real saved AF run): focus-sweep before/after baseline**

This validates the WideRange change on real defocused data. Skip if no real AF run is available; it is not required for the PR.

Build TestApp, then run the focus-sweep diagnostic against a real saved AF-run folder (the WideRange preset only affects detection when `Simple_FocusRange=WideRange` is the active profile setting, so this mainly exercises the structure-map sensitivity path):
```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe focus-sweep --af-run "C:\path\to\AutoFocus_run\attempt01" --out "C:\temp\hf-sweep-step2"
```
Expected: `focus_sweep.csv` shows star counts / median HFR vs focuser position; eyeball that defocused-extreme frames still detect a reasonable star count. (Purely informational; no assertion.)

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin ghilios/defocus-robustness
gh pr create --base develop --head ghilios/defocus-robustness \
  --title "Defocus robustness (step 2): fix WideRange sensitivity, document TooFlat-during-AF" \
  --body "$(cat <<'EOF'
Step 2 of the star-detection accuracy analysis (defocus robustness, findings F1/F2).

## Changes
- **F2 — WideRange sensitivity fix (behavior change):** `ConfigureSimpleSettings` lowered `BrightnessSensitivity` for WideRange instead of raising it (10→8), matching the preset's "be more sensitive at defocus" intent. Previously it was raised to 12 (less sensitive — the opposite). Regression-tested.
- **F1 — TooFlat gate (documentation only):** recorded the deliberate decision to keep the TooFlat gate active during AutoFocus (comments at the AF-override point and the gate). No behavior change.

## Out of scope (deferred)
- Deriving StructureLayers from expected max HFR (needs engine→params plumbing).

## Verification
- Full unit suite passes, including the new `SimpleMode_FocusRangeWideRange_IncreasesSensitivity`.
- Optional local `TestApp focus-sweep` before/after on a real AF run (not in CI).

Roadmap status updated in `docs/star-detection-hfr-autofocus-accuracy-analysis.md` (§10).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review (completed during planning)

- **Decision coverage:** WideRange fix → Task 1 ✓; TooFlat documentation → Task 2 ✓; StructureLayers deferred (no task, intentional) ✓; synthetic CI test → Task 1 ✓; optional real-data baseline → Task 3 Step 2 ✓.
- **Placeholder scan:** none. The only optional step (Task 3 Step 2) is explicitly marked optional with a real path placeholder the operator fills in.
- **Type/symbol consistency:** `ConfigureSimpleSettings`, `Simple_FocusRange`, `FocusRangeEnum.WideRange`, `BrightnessSensitivity`, `Build()`, `UseAdvanced`, `Simple_PixelScale`, `PixelScaleEnum.Typical`, `GetStarDetectorParams`, `isAutoFocus`, `PeakResponse`, `starCandidate.StarMedian`/`.Peak` — all verified against current source.

## Risks

- **WideRange + LongFocalLength stacking:** `LongFocalLength` adds +2.0 separately; with WideRange now −2.0, the combination is 10−2+2 = 10 (no negative-value risk; `BrightnessSensitivity` validation requires ≥ 0). The new test fixes `Simple_PixelScale=Typical` so it isolates the WideRange effect.
- **Comment-only change to core files:** Task 2 touches `StarDetector.cs`/`HocusFocusStarDetection.cs` with comments only; a build confirms no accidental code change.
