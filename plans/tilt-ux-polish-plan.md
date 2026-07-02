# Tilt UX Polish & Screenshot Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close out the cosmetic/UX items deliberately deferred from PR #117's reviews (stale label on profile swap, dirty-input Apply, "Measurements"/"Measurement" label collision, duplicated tooltips) and refresh the manual screenshots that PR #117's UI changes made stale.

**Architecture:** Small, independent C#/XAML polish changes on the existing HocusFocus plugin patterns (CommunityToolkit.Mvvm, PluginOptionsAccessor), each TDD'd where a test can pin it; then a Windows-side session (NINA + windows-mcp) that runtime-verifies the XAML-only changes and captures new screenshots per the repo's capture pipeline.

**Tech Stack:** .NET 8 (`net8.0-windows7.0`), NUnit 4.4 + NSubstitute, WPF XAML, MkDocs, Windows MCP screenshot pipeline (`.claude/docs/nina-mcp-screenshots.md`).

---

## Context for a zero-context engineer

- **Repo root:** `/home/ghilios/src/hocus-focus`.
- **Precondition:** PR #117 (`ghilios/tilt-calibration-ux`) merged into `develop`. Branch from up-to-date develop:
  ```bash
  git checkout develop && git pull && git checkout -b ghilios/tilt-ux-polish
  ```
  **Never push to `develop` directly.** Task 5 additionally requires the Windows machine with NINA, the windows-mcp server, and a locally built plugin installed (see `.claude/docs/nina-mcp-screenshots.md`).
- **Build + full test suite** (run after every task; on WSL the binary is `dotnet.exe`):
  ```bash
  dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
  ```
- **Committing** (author and committer must both use the noreply address):
  ```bash
  GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
    git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>"
  ```
- **Key files:**
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs` — wizard VM; its `profileService.ProfileChanged` handler already re-raises a block of wrapper properties inside `OnUIThread`.
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml` — wizard UI (Manual Calibration Entry expander; the "Measurements" label at ~line 283; Measurement section with literal tooltips).
  - `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` — inspector UI; defines `SignalAmplification_Tooltip` / `CenterFocuserBeforeRun_Tooltip` resources near the top.
  - Tests: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs` (has a `Build()` fixture; profile-change tests exist from PR #117 — follow their style).
- **Non-goals (decided during PR #117 reviews — do not do these):** renaming `WizardStep` enum members (serialized in saved-run metadata); surfacing physical vs stored screw angles in the saved-calibration panel (needs a design pass first — brainstorm before implementing); a setter-level `StepCount` clamp (load-time healing shipped in #117 is sufficient); an upper clamp on `SignalAmplification` (finding was refuted).

---

### Task 1: `CalibrationAppliedAmountDisplay` refreshes on profile switch

A profile swap that changes `AdjustmentType` (screws ↔ steppers) leaves this one label's turns/steps vocabulary stale — it was the only wrapper property missed by PR #117's ProfileChanged re-raise block.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs`

- [ ] **Step 1: Write the failing test.** Locate the existing PR #117 profile-change test (search the test file for `ProfileChanged`) and add a sibling following its exact fixture pattern:
```csharp
    [Test]
    public void ProfileChanged_RaisesCalibrationAppliedAmountDisplay() {
        var (vm, options, profileService) = Build(); // adapt to the fixture's actual signature/style
        var raised = new List<string>();
        vm.PropertyChanged += (s, e) => raised.Add(e.PropertyName);
        profileService.ProfileChanged += Raise.Event<EventHandler>(profileService, EventArgs.Empty); // match the existing test's raise style
        Assert.That(raised, Does.Contain(nameof(TiltAdapterWizardVM.CalibrationAppliedAmountDisplay)));
    }
```
- [ ] **Step 2: Run to verify it fails.** `dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~ProfileChanged_RaisesCalibrationAppliedAmountDisplay"` → FAIL (property not raised).
- [ ] **Step 3: Implement.** In the wizard VM's `profileService.ProfileChanged` handler, add one line in the existing re-raise block, next to `RaisePropertyChanged(nameof(CalibrationAmountLabel));`:
```csharp
                    RaisePropertyChanged(nameof(CalibrationAppliedAmountDisplay));
```
- [ ] **Step 4: Run the filtered test → PASS, then the full suite → all green.**
- [ ] **Step 5: Commit:** `fix(wizard): refresh CalibrationAppliedAmountDisplay vocabulary on profile switch`

---

### Task 2: Disable manual-entry Apply while the angle box has a validation error

PR #117 added a 0–360 `DoubleRangeRule` to the Screw 1 angle TextBox, so garbage input shows the red error adorner — but Apply still persists the last valid VM value. Disable Apply while the box is in error.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml`

- [ ] **Step 1: Name the TextBox.** In the Manual Calibration Entry expander, add `x:Name="ManualScrew1AngleBox"` to the Screw-1-angle `TextBox` (the one whose binding carries the `DoubleRangeRule`).
- [ ] **Step 2: Gate the Apply button.** Replace the Apply `Button`'s opening tag content so it carries a style trigger (keep the existing `Command` and inner `TextBlock` exactly as-is):
```xml
                            <Button
                                Margin="0,8,0,0"
                                HorizontalAlignment="Left"
                                Command="{Binding ApplyManualCalibrationCommand}">
                                <Button.Style>
                                    <Style BasedOn="{StaticResource {x:Type Button}}" TargetType="Button">
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding ElementName=ManualScrew1AngleBox, Path=(Validation.HasError)}" Value="True">
                                                <Setter Property="IsEnabled" Value="False" />
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Button.Style>
                                <TextBlock
                                    Margin="10,5,10,5"
                                    Foreground="{StaticResource ButtonForegroundBrush}"
                                    Text="Apply" />
                            </Button>
```
  (Verify the file's Button style resource key by looking at how sibling buttons set styles; use `BasedOn` exactly as sibling styled controls do so the NINA theme is preserved.)
- [ ] **Step 3: Build + full suite** (compiles the XAML): `dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` → all green. Runtime behavior (button actually greys out on garbage input) is verified in Task 5's NINA session — note this in the commit body.
- [ ] **Step 4: Commit:** `fix(wizard): disable manual-calibration Apply while the angle input is invalid`

---

### Task 3: Resolve the "Measurements" / "Measurement" label collision

The `MeasurementAverageCount` field labeled **Measurements** sits a few rows above the new bold **Measurement** section header — two adjacent measurement-named things with different meanings.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml` (label at ~line 283 — anchor on `Text="Measurements"`)
- Modify: `documentation/docs/overview/tilt-adapter-wizard.md` (two `**Measurements**` occurrences, ~lines 58 and 72)

- [ ] **Step 1:** In the XAML, change `Text="Measurements"` to `Text="Measurements to average"` (keep its ToolTip; extend the tooltip's first sentence if it doesn't already say the value is a per-step repeat count).
- [ ] **Step 2:** Update both doc occurrences: `set **Measurements** above 1` → `set **Measurements to average** above 1`, and `Raising **Measurements** above 1` → `Raising **Measurements to average** above 1`. Grep for stragglers: `grep -rn '\*\*Measurements\*\*' documentation/docs/` → expect zero hits afterwards.
- [ ] **Step 3:** `mkdocs build --strict` → PASS; full suite → all green.
- [ ] **Step 4: Commit:** `fix(wizard): rename Measurements label to avoid collision with the Measurement section`

---

### Task 4: Consolidate the shared Signal Amplification / Center Focuser First tooltips

The wizard's Measurement section carries literal-text copies of the inspector's `SignalAmplification_Tooltip` / `CenterFocuserBeforeRun_Tooltip` resources, and they have already drifted. Move both to a shared merged dictionary so there is one source of truth.

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/SharedTooltips.xaml`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml`
- Modify (probably): `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Joko.NINA.Plugins.HocusFocus.csproj` — check whether XAML pages are globbed or listed; add the new Page item if listed explicitly.

- [ ] **Step 1:** Create `Resources/SharedTooltips.xaml` containing exactly the two `TextBlock` resources currently defined near the top of `AutoFocus/DataTemplates.xaml` (`x:Key="SignalAmplification_Tooltip"`, `x:Key="CenterFocuserBeforeRun_Tooltip"`), using the inspector wording as canon:
```xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--  Tooltips shared by the Aberration Inspector and the Tilt Adapter Wizard (single source of truth).  -->
    <TextBlock x:Key="SignalAmplification_Tooltip" Text="[copy the exact current Text from AutoFocus/DataTemplates.xaml]" />
    <TextBlock x:Key="CenterFocuserBeforeRun_Tooltip" Text="[copy the exact current Text from AutoFocus/DataTemplates.xaml]" />
</ResourceDictionary>
```
  (The bracketed placeholders are copy instructions, not content to invent — lift the two `Text` attributes verbatim from `AutoFocus/DataTemplates.xaml` at HEAD.)
- [ ] **Step 2:** In BOTH `AutoFocus/DataTemplates.xaml` and `TiltAdapterWizard/DataTemplates.xaml`, add at the top of the root `ResourceDictionary`:
```xml
    <ResourceDictionary.MergedDictionaries>
        <ResourceDictionary Source="pack://application:,,,/NINA.Joko.Plugins.HocusFocus;component/Resources/SharedTooltips.xaml" />
    </ResourceDictionary.MergedDictionaries>
```
  Then delete the two now-duplicate resource definitions from `AutoFocus/DataTemplates.xaml`, and in the wizard's Measurement section replace the two literal `ToolTip="..."` attributes with `ToolTip="{StaticResource SignalAmplification_Tooltip}"` / `ToolTip="{StaticResource CenterFocuserBeforeRun_Tooltip}"`. If `StaticResource` fails to resolve at XAML-compile time from the merged dictionary, switch those references to `DynamicResource` and note it.
- [ ] **Step 3:** Full suite (compiles both XAML files) → all green. **Runtime verification is mandatory in Task 5** (pack-URI merged dictionaries only fully resolve when the plugin loads in NINA): if Task 5 shows missing tooltips or a load failure, revert this task's commit rather than shipping broken resources.
- [ ] **Step 4: Commit:** `refactor(ui): single shared source for Signal Amplification / Center Focuser First tooltips`

---

### Task 5: Windows session — runtime verification + screenshot refresh

Requires: Windows machine, NINA with the branch-built plugin installed, windows-mcp. **Read `.claude/docs/nina-mcp-screenshots.md` first and follow its capture/annotate pipeline exactly** (it defines the build-install steps, capture resolution, theme, and file conventions).

**Files:**
- Replace: `documentation/docs/assets/screenshots/inspector-tilt-guidance.png` (currently shows the pre-#117 IN/OUT guidance table; must show the motion-anchored arrows, per-row rotation glyphs, and the legend at the top of the section)
- Replace: `documentation/docs/assets/screenshots/inspector-options-empty.png` (must show the new always-visible sweep-settings block above the Options expander)
- Create: `documentation/docs/assets/screenshots/wizard-measurement-section.png` (the wizard's Measurement section incl. the adapter-direction ComboBox) — embed it in `documentation/docs/overview/tilt-adapter-wizard.md` in "The calibration loop" section, after the paragraph introducing the Measurement section, MkDocs image syntax matching the page's existing images (`{ width=620 }`).
- Modify: `documentation/docs/overview/tilt-adapter-wizard.md` (embed line for the new screenshot)

- [ ] **Step 1: Runtime-verify Tasks 2 and 4.** In the running NINA: type garbage into the Screw 1 angle box → red adorner appears AND Apply greys out (Task 2); hover the wizard's Signal Amplification and Center Focuser First labels → tooltips render (Task 4). If Task 4's tooltips are missing, revert that commit (per Task 4 Step 3) before capturing screenshots.
- [ ] **Step 2: Capture the three screenshots** per the pipeline doc. For `inspector-tilt-guidance.png` a calibrated profile with a completed measurement (or a replayed saved run) is needed so the guidance table + legend render — the pipeline doc's NINA navigation map covers loading a saved AF run.
- [ ] **Step 3: Embed the new image** in `tilt-adapter-wizard.md` and verify all three images render: `mkdocs build --strict` → PASS, then `mkdocs serve` and eyeball the three pages.
- [ ] **Step 4:** Full suite (invariant) → all green.
- [ ] **Step 5: Commit:** `docs: refresh tilt guidance/options screenshots; add wizard Measurement section capture`

---

### Task 6: Push + PR

- [ ] **Step 1:** Full suite one final time → all green; `mkdocs build --strict` → PASS.
- [ ] **Step 2:**
```bash
git push -u origin ghilios/tilt-ux-polish
gh pr create --base develop --title "Tilt UX polish: deferred review minors + screenshot refresh" --body "Closes out the cosmetic items deferred from PR #117's reviews: profile-swap label refresh, invalid-input Apply gating, Measurements label rename, shared tooltip source; refreshes the manual screenshots the new UI made stale.

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```
