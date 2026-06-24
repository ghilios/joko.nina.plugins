# Pre-Release UI Feedback — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Incorporate three pieces of pre-release UI feedback — review-UI layout (header/footer full width, legend fits content, canvas fills the rest), consolidate the tilt wizard's two replay buttons into one driven by the shared replay-settings modal, and rename the AutoFocus "Reprocess Saved Run" button to "Replay Saved AF".

**Architecture:** Items 1 and 3 are pure XAML attribute/text edits. Item 2 reuses the existing `ReplaySettingsPrompt` modal (not the whole `AutoFocusReplayCoordinator`, whose `AutoFocusEngineOptions` output the tilt path doesn't use): the single button shows the modal, a small testable `TiltReplayModeResolver` maps the `ReplaySettingsChoice` to two independent flags (geometry source + per-step star-detection override) plus an optional one-time profile mutation, and `ReplayAsync` consumes those flags.

**Tech Stack:** C# / .NET 8.0-windows, WPF, NUnit 4.4.0 + NSubstitute, CommunityToolkit.Mvvm, MEF.

**Branch:** `ghilios/ui-feedback-prerelease` (already created; the design doc is committed there). All commits use the privacy email — see the commit command in each task.

**Design doc:** `docs/ui-feedback-prerelease-design.md`.

**Build/test commands (WSL → Windows toolchain):**
- Build: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
  - NOTE: `rtk dotnet build` prints `fail dotnet build: ...` in its header even on success — trust the `errors=0` count and exit code 0, not the header word.
- Test: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
- If you need full warning text, re-run via `cmd.exe /c "dotnet build Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"`.
- Set the Bash `timeout` to `600000` for build/test calls.

**Commit command pattern (use for every commit step):**
```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 1: Item 3 — Rename "Reprocess Saved Run" → "Replay Saved AF"

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml` (lines ~620, ~625, ~637, ~644)

No unit test — these are literal XAML text changes (verified by build). Pure string edits.

- [ ] **Step 1: Rename the button label (line ~620)**

Replace:
```
                ButtonText="Reprocess Saved Run"
```
with:
```
                ButtonText="Replay Saved AF"
```

- [ ] **Step 2: Reword the button's own tooltip (line ~625)**

Replace:
```
                ToolTip="Reprocesses AutoFocus images saved by having Save enabled in Hocus Focus -&gt; Auto Focus Options. This produces an auto focus curve using whatever the current star detection and fit settings are" />
```
with:
```
                ToolTip="Replays saved AutoFocus images (Save enabled in Hocus Focus -&gt; Auto Focus Options) to produce an auto focus curve using whatever the current star detection and fit settings are" />
```

- [ ] **Step 3: Update the "Review Frames" cross-reference tooltip (line ~637)**

In the `ToolTip` that begins `"Open the Review Frames window..."`, replace the substring:
```
a live run or a Reprocess Saved Run
```
with:
```
a live run or a Replay Saved AF
```

- [ ] **Step 4: Update the "Keep frames for review" cross-reference tooltip (line ~644)**

In the `ToolTip` that begins `"When on, the per-frame images..."`, replace the substring:
```
a live run or a Reprocess Saved Run
```
with:
```
a live run or a Replay Saved AF
```

- [ ] **Step 5: Confirm no stray "Reprocess Saved Run" user-facing strings remain**

Run:
```bash
grep -rn "Reprocess Saved Run" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/
```
Expected: no matches (the only remaining "reprocess" references are internal `HocusFocusVM.cs` log/comment strings, which are intentionally left unchanged — verify any hits are those non-UI strings only).

- [ ] **Step 6: Build**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: `errors=0`, exit code 0.

- [ ] **Step 7: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/DataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(autofocus-ui): rename Reprocess Saved Run button to Replay Saved AF

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Item 1 — Review UI layout (header/footer full width, legend fits content)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml` (lines ~113, ~318)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml` (lines ~112, ~333)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/StarReviewControl.xaml` (lines ~111, ~401, ~407)

No unit test — XAML layout (verified by build + manual). The uniform transformation: header (and footer where present) gain `Grid.ColumnSpan="2"` to span the full window width; the legend moves out of the header row to `Grid.Row="1" Grid.RowSpan="2"` and gains `VerticalAlignment="Top"` so it fits its content height; the toolbar and canvas are unchanged.

### AutoFocusFrameReviewControl.xaml (3-row grid, no footer)

- [ ] **Step 1: Make the header span both columns (line ~113)**

Replace:
```
        <Border Grid.Row="0" Grid.Column="0" Margin="0,0,0,6" Padding="8,5" CornerRadius="3" Background="#FF262B31">
```
with:
```
        <Border Grid.Row="0" Grid.Column="0" Grid.ColumnSpan="2" Margin="0,0,0,6" Padding="8,5" CornerRadius="3" Background="#FF262B31">
```

- [ ] **Step 2: Move the legend below the full-width header and let it fit its content (line ~318)**

Replace:
```
        <ScrollViewer Grid.Row="0" Grid.RowSpan="3" Grid.Column="1" Width="240" Margin="12,0,0,0"
```
with:
```
        <ScrollViewer Grid.Row="1" Grid.RowSpan="2" Grid.Column="1" VerticalAlignment="Top" Width="240" Margin="12,0,0,0"
```
(The `VerticalScrollBarVisibility="Auto"` on the following line stays — it now only scrolls if the legend is taller than the toolbar+image band, instead of forcing full height.)

### FrameReviewControl.xaml (3-row grid, no footer)

- [ ] **Step 3: Make the header span both columns (line ~112)**

Replace:
```
        <Border Grid.Row="0" Grid.Column="0" Margin="0,0,0,6" Padding="8,5" CornerRadius="3" Background="#FF262B31">
```
with:
```
        <Border Grid.Row="0" Grid.Column="0" Grid.ColumnSpan="2" Margin="0,0,0,6" Padding="8,5" CornerRadius="3" Background="#FF262B31">
```

- [ ] **Step 4: Move the legend below the full-width header and let it fit its content (line ~333)**

Replace:
```
        <StackPanel Grid.Row="0" Grid.RowSpan="3" Grid.Column="1" Width="240" Margin="12,0,0,0">
```
with:
```
        <StackPanel Grid.Row="1" Grid.RowSpan="2" Grid.Column="1" VerticalAlignment="Top" Width="240" Margin="12,0,0,0">
```

### StarReviewControl.xaml (4-row grid, has a footer)

- [ ] **Step 5: Make the header span both columns (line ~111)**

Replace:
```
        <DockPanel Grid.Row="0" Grid.Column="0" LastChildFill="False" Margin="0,0,0,6">
```
with:
```
        <DockPanel Grid.Row="0" Grid.Column="0" Grid.ColumnSpan="2" LastChildFill="False" Margin="0,0,0,6">
```

- [ ] **Step 6: Make the footer span both columns (line ~401)**

Replace:
```
        <StackPanel Grid.Row="3" Grid.Column="0" Margin="0,6,0,0">
```
with:
```
        <StackPanel Grid.Row="3" Grid.Column="0" Grid.ColumnSpan="2" Margin="0,6,0,0">
```

- [ ] **Step 7: Move the legend into the toolbar+image band and let it fit its content (line ~407)**

Replace:
```
        <StackPanel Grid.Row="0" Grid.RowSpan="4" Grid.Column="1" Width="230" Margin="12,0,0,0">
```
with:
```
        <StackPanel Grid.Row="1" Grid.RowSpan="2" Grid.Column="1" VerticalAlignment="Top" Width="230" Margin="12,0,0,0">
```

- [ ] **Step 8: Build**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: `errors=0`, exit code 0. (XAML errors surface as build errors, so a clean build validates the markup.)

- [ ] **Step 9: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/Review/AutoFocusFrameReviewControl.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/FrameReviewControl.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/Optimization/Review/StarReviewControl.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(review-ui): full-width header/footer and content-height legend

Header (and footer, where present) span both columns to the full window
width; the legend pane drops its all-rows RowSpan and is top-aligned in the
toolbar+image band so it sizes to its content. The image canvas continues to
take the remaining space.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Item 2a (TDD) — `TiltReplayModeResolver` choice mapping

This is the one piece of item 2 with branching logic, and the part the user cares about (all three modes doing the right thing). Extract it into a small `internal` resolver and test it first (the test project reaches `internal` types via `[assembly: InternalsVisibleTo("Joko.NINA.Plugins.HocusFocus.Tests")]`).

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltReplayMode.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltReplayModeResolverTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltReplayModeResolverTests.cs`:
```csharp
using System;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TiltAdapterWizard {

    [TestFixture]
    public class TiltReplayModeResolverTests {

        [Test]
        public void UseCurrentSettings_CurrentGeometry_NoOverride_NoMutation() {
            var mode = TiltReplayModeResolver.Resolve(ReplaySettingsChoice.UseCurrentSettings);
            Assert.Multiple(() => {
                Assert.That(mode.UseMetadataGeometry, Is.False);
                Assert.That(mode.ApplyCaptureTimeOverridePerStep, Is.False);
                Assert.That(mode.UpdateProfileToCaptureTime, Is.False);
            });
        }

        [Test]
        public void UseCaptureTimeInMemory_MetadataGeometry_PerStepOverride_NoMutation() {
            var mode = TiltReplayModeResolver.Resolve(ReplaySettingsChoice.UseCaptureTimeSettingsInMemory);
            Assert.Multiple(() => {
                Assert.That(mode.UseMetadataGeometry, Is.True);
                Assert.That(mode.ApplyCaptureTimeOverridePerStep, Is.True);
                Assert.That(mode.UpdateProfileToCaptureTime, Is.False);
            });
        }

        [Test]
        public void UpdateProfile_MetadataGeometry_NoPerStepOverride_Mutates() {
            var mode = TiltReplayModeResolver.Resolve(ReplaySettingsChoice.UpdateProfileToCaptureTime);
            Assert.Multiple(() => {
                Assert.That(mode.UseMetadataGeometry, Is.True);
                Assert.That(mode.ApplyCaptureTimeOverridePerStep, Is.False);
                Assert.That(mode.UpdateProfileToCaptureTime, Is.True);
            });
        }

        [Test]
        public void Cancel_Throws() {
            Assert.Throws<ArgumentOutOfRangeException>(() => TiltReplayModeResolver.Resolve(ReplaySettingsChoice.Cancel));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails (does not compile — type missing)**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: build/compile failure — `TiltReplayModeResolver` / `TiltReplayMode` do not exist yet.

- [ ] **Step 3: Implement the resolver**

Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltReplayMode.cs`:
```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// How a tilt-calibration replay sources its settings, derived from the user's choice in the shared
    /// replay-settings modal. Decouples the two independent decisions (which geometry, which star-detection settings)
    /// that the old two-button UI coupled into a single bool.
    /// </summary>
    internal readonly struct TiltReplayMode {

        public TiltReplayMode(bool useMetadataGeometry, bool applyCaptureTimeOverridePerStep, bool updateProfileToCaptureTime) {
            UseMetadataGeometry = useMetadataGeometry;
            ApplyCaptureTimeOverridePerStep = applyCaptureTimeOverridePerStep;
            UpdateProfileToCaptureTime = updateProfileToCaptureTime;
        }

        /// <summary>Recompute the calibration from the run's stored geometry (true) or the current profile/tilt settings (false).</summary>
        public bool UseMetadataGeometry { get; }

        /// <summary>Pass the per-step capture-time star-detection snapshot as an in-memory override (true) or use the live profile (false).</summary>
        public bool ApplyCaptureTimeOverridePerStep { get; }

        /// <summary>Persist the run's capture-time star-detection settings to the live profile before replaying.</summary>
        public bool UpdateProfileToCaptureTime { get; }
    }

    internal static class TiltReplayModeResolver {

        /// <summary>
        /// Maps a <see cref="ReplaySettingsChoice"/> to a <see cref="TiltReplayMode"/>.
        /// <see cref="ReplaySettingsChoice.Cancel"/> must be handled by the caller before resolving (it has no replay mode).
        /// </summary>
        public static TiltReplayMode Resolve(ReplaySettingsChoice choice) {
            switch (choice) {
                case ReplaySettingsChoice.UseCurrentSettings:
                    return new TiltReplayMode(useMetadataGeometry: false, applyCaptureTimeOverridePerStep: false, updateProfileToCaptureTime: false);
                case ReplaySettingsChoice.UseCaptureTimeSettingsInMemory:
                    return new TiltReplayMode(useMetadataGeometry: true, applyCaptureTimeOverridePerStep: true, updateProfileToCaptureTime: false);
                case ReplaySettingsChoice.UpdateProfileToCaptureTime:
                    return new TiltReplayMode(useMetadataGeometry: true, applyCaptureTimeOverridePerStep: false, updateProfileToCaptureTime: true);
                default:
                    throw new ArgumentOutOfRangeException(nameof(choice), choice, "Cancel must be handled before resolving a replay mode.");
            }
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: all `TiltReplayModeResolverTests` pass; whole suite still green.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltReplayMode.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltReplayModeResolverTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(tilt-wizard): add TiltReplayModeResolver mapping replay choice to mode

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Item 2b — Single "Replay" button driving the shared modal

Wire the consolidated button: add the window-service factory, reshape `ReplayAsync` to show the modal + resolve the mode + optionally mutate the profile, rewire the per-step override / geometry / warning to the mode flags, remove the second command, and delete the second XAML button.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs` (using ~16; field ~69; ctor ~170-171; `ReplayAsync` ~1130-1282; command decl ~485-486; notify ~1436-1437)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml` (buttons ~312-330)

- [ ] **Step 1: Add the `WindowService` using (after line ~16)**

Replace:
```
using NINA.Core.Utility.Notification;
```
with:
```
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
```

- [ ] **Step 2: Add the window-service-factory field (after the `applicationDispatcher` field, line ~69)**

Replace:
```
        private readonly IApplicationDispatcher applicationDispatcher;
        private readonly IProgress<ApplicationStatus> progress;
```
with:
```
        private readonly IApplicationDispatcher applicationDispatcher;
        // WindowServiceFactory is not a MEF export (NINA exposes the concrete type with a default ctor only), so it is
        // constructed directly, matching InspectorVM / HocusFocusVM. Used to show the shared replay-settings modal.
        private readonly IWindowServiceFactory windowServiceFactory = new WindowServiceFactory();
        private readonly IProgress<ApplicationStatus> progress;
```

- [ ] **Step 3: Collapse the two replay commands to one in the constructor (lines ~170-171)**

Replace:
```
            ReplayCommand = new AsyncRelayCommand(() => ReplayAsync(useMetadataSettings: true), () => !IsWizardRunning && !IsMeasuring);
            ReplayCurrentSettingsCommand = new AsyncRelayCommand(() => ReplayAsync(useMetadataSettings: false), () => !IsWizardRunning && !IsMeasuring);
```
with:
```
            ReplayCommand = new AsyncRelayCommand(ReplayAsync, () => !IsWizardRunning && !IsMeasuring);
```

- [ ] **Step 4: Remove the second command declaration (lines ~485-486)**

Replace:
```
        public ICommand ReplayCommand { get; }
        public ICommand ReplayCurrentSettingsCommand { get; }
```
with:
```
        public ICommand ReplayCommand { get; }
```

- [ ] **Step 5: Remove the second command's `NotifyCanExecuteChanged` (lines ~1436-1437)**

Replace:
```
            ((AsyncRelayCommand)ReplayCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ReplayCurrentSettingsCommand).NotifyCanExecuteChanged();
```
with:
```
            ((AsyncRelayCommand)ReplayCommand).NotifyCanExecuteChanged();
```

- [ ] **Step 6: Update the `ReplayAsync` doc comment + signature (lines ~1130-1138)**

Replace:
```
        // ---- Replay -----------------------------------------------------------------------------------------

        // Replays a saved calibration run from a folder: reads metadata.json, re-analyzes each step from its saved
        // frames, and recomputes the calibration.
        // - useMetadataSettings = true ("Replay"): apply the run's stored star-detection settings transiently
        //   (restored afterward) and use the run's stored geometry — reproduces the original calibration exactly.
        // - useMetadataSettings = false ("Replay Current Settings"): leave the current profile's star-detection and
        //   tilt-calibration settings in place, so metadata.json does not override what is configured now.
        private async Task ReplayAsync(bool useMetadataSettings) {
```
with:
```
        // ---- Replay -----------------------------------------------------------------------------------------

        // Replays a saved calibration run from a folder: reads metadata.json, re-analyzes each step from its saved
        // frames, and recomputes the calibration. A single "Replay" button opens the shared replay-settings modal,
        // which offers three modes (see TiltReplayModeResolver):
        // - Use current settings: current profile star-detection + current tilt geometry (metadata does not override).
        // - Use capture-time settings in memory: the run's stored star-detection as a transient per-step override
        //   (profile untouched) + the run's stored geometry — reproduces the original calibration exactly.
        // - Update profile to capture-time: persist the run's star-detection settings to the live profile, then replay
        //   against it + the run's stored geometry.
        private async Task ReplayAsync() {
```

- [ ] **Step 7: Insert the modal + mode resolution + optional profile mutation (after the per-step presence check, before the screw-count warning, line ~1175)**

Find this existing block (the `byStep` presence-check loop ending at line ~1174) and insert the new block immediately after its closing `}` and before the `// "Replay Current Settings" applies...` comment:
```
            foreach (var step in MeasurementSteps) {
                if (!byStep.ContainsKey(step.ToString())) {
                    Notification.ShowError($"metadata.json has no saved folder for step '{step}'. Cannot replay.");
                    return;
                }
            }

            // "Replay Current Settings" applies the current screw geometry to the saved deltas; if the saved run
```
Replace it with (adds the modal/mode block in the gap, and rewords the warning comment + condition):
```
            foreach (var step in MeasurementSteps) {
                if (!byStep.ContainsKey(step.ToString())) {
                    Notification.ShowError($"metadata.json has no saved folder for step '{step}'. Cannot replay.");
                    return;
                }
            }

            // Consolidated replay: one button, three modes resolved by the shared replay-settings modal (seeded from a
            // representative per-step AutoFocus replay snapshot). Cancelling / closing the modal aborts.
            var representativeStepFolder = byStep[MeasurementSteps.First().ToString()];
            AutoFocusReplayMetadata.TryLoad(representativeStepFolder, out var replayMetadata, out _);
            var choice = await ReplaySettingsPrompt.ShowAsync(windowServiceFactory, replayMetadata);
            if (choice == ReplaySettingsChoice.Cancel) {
                return;
            }
            var mode = TiltReplayModeResolver.Resolve(choice);

            // "Update profile to capture-time": persist the run's capture-time star-detection settings to the live
            // profile, then replay against it (no per-step override needed). ApplyFullSnapshot raises INPC -> UI thread.
            if (mode.UpdateProfileToCaptureTime) {
                var captureSnapshot = BuildTiltReplayDetectionOverride(representativeStepFolder, metadata);
                if (captureSnapshot != null) {
                    OnUIThread(() => HocusFocusPlugin.StarDetectionOptions?.ApplyFullSnapshot(captureSnapshot));
                }
            }

            // Replaying with current geometry applies the current screw count to the saved deltas; if the saved run
```

- [ ] **Step 8: Rewire the screw-count-mismatch warning condition (line ~1178)**

Replace:
```
            if (!useMetadataSettings && metadata.NumberOfScrews != tiltAdapterOptions.ScrewCount) {
```
with:
```
            if (!mode.UseMetadataGeometry && metadata.NumberOfScrews != tiltAdapterOptions.ScrewCount) {
```

- [ ] **Step 9: Rewire the per-step detection override (lines ~1205-1210)**

Replace:
```
                    // No-mutation replay: when using the run's stored settings, pass a detached capture-time
                    // star-detection snapshot as an override so the profile is never modified (and never needs
                    // restoring). "Replay Current Settings" passes null and uses the current profile settings.
                    var detectionOverride = useMetadataSettings
                        ? BuildTiltReplayDetectionOverride(byStep[step.ToString()], metadata)
                        : null;
```
with:
```
                    // Capture-time-in-memory replay passes a detached per-step star-detection snapshot as an override so
                    // the profile is never touched. "Use current settings" and "update profile" both pass null (the
                    // latter has already mutated the live profile to the capture-time settings).
                    var detectionOverride = mode.ApplyCaptureTimeOverridePerStep
                        ? BuildTiltReplayDetectionOverride(byStep[step.ToString()], metadata)
                        : null;
```

- [ ] **Step 10: Rewire the geometry selection (line ~1241)**

Replace:
```
                if (useMetadataSettings) {
                    calibrationAppliedAmount = metadata.CalibrationAppliedAmount;
```
with:
```
                if (mode.UseMetadataGeometry) {
                    calibrationAppliedAmount = metadata.CalibrationAppliedAmount;
```

- [ ] **Step 11: Update the `finally` comment (lines ~1271-1273)**

Replace:
```
            } finally {
                // No profile mutation to undo: capture-time detection settings are passed to the replay as an
                // in-memory override (see BuildTiltReplayDetectionOverride), so the user's profile is never touched.
                isReplaying = false;
```
with:
```
            } finally {
                // "Use capture-time in memory" passes detection settings as an in-memory override, so nothing is undone.
                // "Update profile to capture-time" intentionally persisted the capture-time settings to the profile (the
                // user opted in via the modal), so it is likewise not restored.
                isReplaying = false;
```

- [ ] **Step 12: Remove the second XAML button and reword the surviving "Replay" tooltip (DataTemplates.xaml, lines ~312-330)**

Replace:
```
                        <Button
                            Margin="6,0,0,0"
                            Command="{Binding ReplayCommand}"
                            ToolTip="Replay a saved calibration run from a folder containing metadata.json. Uses the run's stored star-detection and tilt-calibration settings (restoring your settings afterward).">
                            <TextBlock
                                Margin="10,5,10,5"
                                Foreground="{StaticResource ButtonForegroundBrush}"
                                Text="Replay" />
                        </Button>
                        <Button
                            Margin="6,0,0,0"
                            Command="{Binding ReplayCurrentSettingsCommand}"
                            ToolTip="Replay a saved calibration run using your CURRENT star-detection and tilt-calibration settings instead of the ones stored in metadata.json. Your profile settings are left unchanged.">
                            <TextBlock
                                Margin="10,5,10,5"
                                Foreground="{StaticResource ButtonForegroundBrush}"
                                Text="Replay Current Settings" />
                        </Button>
```
with:
```
                        <Button
                            Margin="6,0,0,0"
                            Command="{Binding ReplayCommand}"
                            ToolTip="Replay a saved calibration run from a folder containing metadata.json. Choose how in the prompt: your current settings, the run's capture-time settings held in memory (profile untouched), or after updating your profile to the run's capture-time settings.">
                            <TextBlock
                                Margin="10,5,10,5"
                                Foreground="{StaticResource ButtonForegroundBrush}"
                                Text="Replay" />
                        </Button>
```

- [ ] **Step 13: Confirm no dangling references to the removed command**

Run:
```bash
grep -rn "ReplayCurrentSettingsCommand\|useMetadataSettings" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/
```
Expected: no matches (the command is gone and the bool parameter is fully replaced by `mode.*`).

- [ ] **Step 14: Build**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: `errors=0`, exit code 0. If `IWindowServiceFactory`/`WindowServiceFactory` is unresolved, re-check the Step 1 using (`NINA.Core.Utility.WindowService`).

- [ ] **Step 15: Run the full test suite**

Run: `rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: all tests pass (existing `TiltAdapterWizardVMTests` still build/pass — they construct the VM via the unchanged 7-arg constructor; the new `windowServiceFactory` is a field initializer like the one InspectorVM already uses headlessly in `BuildInspector()`).

- [ ] **Step 16: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "feat(tilt-wizard): consolidate two replay buttons into one using ReplayVM modal

Replace the separate Replay / Replay Current Settings buttons with a single
Replay button that opens the shared replay-settings modal (ReplaySettingsPrompt).
The chosen ReplaySettingsChoice maps via TiltReplayModeResolver to current vs
capture-time geometry, an optional per-step in-memory star-detection override,
and (new for tilt) an opt-in profile update to the run's capture-time settings.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Final verification

- [ ] **Step 1: Full clean build + test**

Run: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` then
`rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: `errors=0`, all tests pass.

- [ ] **Step 2: Manual smoke check (in NINA, optional but recommended pre-release)**

Because items 1 and 3 are not unit-testable, verify visually if a NINA session is available:
- AutoFocus pane: the button reads **"Replay Saved AF"**; its tooltip and the Review-Frames / Keep-frames tooltips read "Replay Saved AF".
- Review windows (AutoFocus Review Frames, Inspector Review Frames, Star Detection review): resize the window — header (and the Star-Detection-review footer) stretch to the full width; the legend pane sits at the top and is only as tall as its content; the image fills the rest.
- Tilt Adapter Wizard: a single **"Replay"** button; clicking it (after picking a saved run folder) shows the Replay Settings modal with the three choices, and each behaves per its label.

- [ ] **Step 3: Push and open the PR** (only when the user asks to publish)

```bash
git push -u origin ghilios/ui-feedback-prerelease
gh pr create --base develop --head ghilios/ui-feedback-prerelease \
  --title "Pre-release UI feedback: review layout, tilt replay consolidation, AF rename" \
  --body "$(cat <<'EOF'
Incorporates three pieces of pre-release UI feedback.

1. **Review UIs** — header (and footer, where present) span the full window width; the legend pane is top-aligned and sizes to its content; the image canvas takes the remaining space. (AutoFocusFrameReviewControl, FrameReviewControl, StarReviewControl.)
2. **Tilt Adapter Wizard** — the two replay buttons collapse into one that drives the shared replay-settings modal (`ReplaySettingsPrompt`); `TiltReplayModeResolver` maps the choice to geometry/override/profile-update behavior.
3. **AutoFocus** — "Reprocess Saved Run" button renamed to "Replay Saved AF" (label + related tooltips).

Design: `docs/ui-feedback-prerelease-design.md`. Plan: `plans/ui-feedback-prerelease-plan.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review (against the design doc)

**Spec coverage:**
- Item 1 (header/footer full width, legend fits content, canvas fills rest) → Task 2 (all three controls; header+footer `ColumnSpan=2`, legend `Row=1 RowSpan=2 VerticalAlignment=Top`). ✓
- Item 2 (one button + ReplayVM modal, all three choices incl. profile update) → Tasks 3 (resolver, TDD) + 4 (modal wiring, profile mutation via `ApplyFullSnapshot`, command/XAML removal). ✓
- Item 3 (rename + tooltips) → Task 1 (label + own tooltip + two cross-reference tooltips). ✓
- Project invariant "run the full suite" → Tasks 3/4/5 run `dotnet test`. ✓

**Placeholder scan:** every code step shows the exact before/after text; no TBD/TODO. ✓

**Type consistency:** `TiltReplayMode` properties (`UseMetadataGeometry`, `ApplyCaptureTimeOverridePerStep`, `UpdateProfileToCaptureTime`) and `TiltReplayModeResolver.Resolve` are referenced identically in the resolver (Task 3), the tests (Task 3), and `ReplayAsync` (Task 4 steps 7–10). `ReplaySettingsPrompt.ShowAsync(IWindowServiceFactory, AutoFocusReplayMetadata)`, `AutoFocusReplayMetadata.TryLoad`, `BuildTiltReplayDetectionOverride`, and `StarDetectionOptions.ApplyFullSnapshot(IStarDetectionOptions)` match the verified signatures. ✓

**Scope notes (carried from the design):** window default sizes, the optimizer wizard's fixed per-step width, the legend's fixed widths (230/240), and internal `HocusFocusVM.cs` reprocess log/comment strings are intentionally untouched.
