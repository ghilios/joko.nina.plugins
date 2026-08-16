# UX Design — Inspector Run-History Return (Feature C) + Tilt-Worsening Banner (Feature D)

Target files (all under `/home/ghilios/src/hocus-focus/Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/`):
- `AutoFocus/InspectorVM.cs` (guidance, automatic adjustment, worsening check)
- `AutoFocus/DataTemplates.xaml` (Row 5 guidance `:2896-3204`, sensor-model history `:3952-4017`, tilt-plane history `:3748-3825`)
- `Inspection/SensorModel.cs` (`SensorParaboloidTiltHistoryModel :60-98`, append `:162-175`, selection `:1503-1519`)
- `AutoFocus/TiltScrewGuidanceRow.cs` (`TiltAdapterGuidanceVM.FormatAmount`)
- `TiltAdapterWizard/TiltScrewTargets.cs` (`ComputePerScrewTargets`)
- `TiltAdapterDevices/` (`TiltDeviceConnectionService`, `TiltDevicePlanPreviewBuilder`, `TiltMovePlanner.Decompose`, `Manual/ManualAdjustmentTarget.cs` `InWizardScrewOrder`)


---

## 0. The core formulation — evaluation of the "differential of two measured states" idea

**Verdict: adopt it, but as the THIRD rung of a truth hierarchy, not the headline mechanism.**

Each run's `ComputePerScrewTargets` output is a state description: "signed unit-counts from this state to flat". So the motion from the current state (run N, the newest measurement) back to run K is exactly `T_N − T_K`, per screw. This is strictly better than the user's stated "reverse all the recommendations since then":

- No assumption that intermediate recommendations were applied (users apply partially, or not at all).
- No summing of intermediate runs — only the two endpoint *measurements* matter.
- It is ~zero when nothing changed (self-consistent), and shared systematic biases (frame factor, paraboloid shrinkage — see memory: focuser-µm ≠ plate-µm, K/G shrinkage) appear in both terms and largely cancel.

**Where it is untrustworthy, and the design's honesty answer:**
1. The user never touched the screws between the runs → the differential is measurement drift. *Answer:* an always-visible caveat line + noise-floor suppression (reuse `FormatAmount`'s 0.005-turn floor; when every screw renders "—", replace the row with a "within measurement noise" line).
2. The calibration changed between runs (re-ran wizard, rotated camera, re-seated adapter) → angles/pitch no longer shared. *Answer:* named explicitly in the caveat.
3. Either fit is poor. *Answer:* the history grid already shows fit-quality columns; the caveat says "as trustworthy as the two fits it is built from".

**Truth hierarchy actually used:**
1. **Motorized, positions snapshot recorded at run K** → absolute counter targets (`snapshot − CurrentPositions` relative moves). Exact; needs no model at all. This is the one-click drive.
2. **Motorized, session journal from the adjustment just sent** (Feature D only) → inverse of each journalled move, reverse order. Exact for exactly those moves.
3. **Model differential `T_N − T_K`** → the only option for manual screws; display-only fallback for motorized runs with no snapshot.

Note on twist: two genuinely-reached device states differ by a rigid-plane delta, so the snapshot drive should show ~zero twist. A twist ≥ 1 step between snapshot and current counters is itself a red flag (lost steps / resync drift) and gets its own warning string (§5).

---

## 1. Entry point (Q1)

**A "Return to this run" panel that appears under the sensor-model history grid when a non-newest row is selected, containing the single action button.** No per-row buttons (the grid is auto-width, centered, 8+ columns already; a device-driving button per row invites misclicks). No right-click (undiscoverable, and NINA users don't expect context menus in these grids).

**Reconciling select-to-view with select-to-return:** selection stays 100% view-only and non-destructive, exactly as today (`SelectedTiltHistoryModel` setter re-displays the run). Selecting a row *additionally* populates the read-only Return panel below the grid — a preview, not an action. The only thing that moves hardware is the explicit button inside that panel. Because `UpdateModel` sets `SelectedTiltHistoryModel = null` on every new analysis (`SensorModel.cs:175`), the panel self-dismisses whenever a fresh measurement lands — no stale return targets can survive a new run.

**One collision must be defused explicitly:** when an old run is selected, Row 5's guidance table switches to that run's *flatten-from-there* numbers, which a user can easily misread as "how to get back there". Add a one-line italic strip at the top of the calibrated-guidance branch, visible only while `SelectedTiltHistoryModel != null`:

> "Viewing run #4 (21:14:05). The guidance above flattens the sensor from run #4's state — it is not how to get back to it. To return the adapter to run #4's state, use Return to this run under Sensor Model Tilt Measurement History."

XAML region: new row under the DataGrid inside the `Grid.Row="10"` expander (`AutoFocus/DataTemplates.xaml:3952`), i.e. a third `RowDefinition` in the grid at `:3957-3964`; panel bound to a new `InspectorVM.RunReturn` POCO (same swap-whole-object pattern as `TiltAdapterGuidanceVM` — no per-property INPC).

---

## 2. Which grid owns it (Q2)

**The sensor-model grid (`Grid.Row="10"`, bound to `SensorModel.SensorTiltHistoryModels`).** It is the only history carrying the fitted paraboloid (`SensorParaboloidTiltHistoryModel.SensorModel` with Gx/Gy/Kx/Ky/X0/Y0) that `TiltScrewTargets.ComputePerScrewTargets` needs; it is also what `DisplayedSensorModel` (the Automatic Adjustment input) is driven from.

**The tilt-plane grid (`Grid.Row="8"`, `TiltModel.SensorTiltHistoryModels`) gets no return affordance.** It keeps select-to-view, gains only the `Time` column (§4) so runs can be correlated across the two grids by timestamp. Do not add a second return path — two buttons that "return the adapter" from two different models would inevitably disagree by fit noise.

Cosmetic but worth fixing while in there: both expanders contain an inner title `TextBlock` reading "Tilt Adjustment History" (`:3767` and `:3971`) even though the expander headers differ — rename the inner titles to "Tilt Plane Runs" and "Sensor Model Runs" so error reports and screenshots are unambiguous.

---

## 3. What the return UI shows before acting (Q3)

**Recommendation: an inline panel, not the modal approval dialog.** Reasons: (i) the user has decided motorized return is one click; (ii) the modal's content (per-move list, twist, time estimate, positions-unknown warning) exists to *inform an approval* — here the panel shows all of it *before* the click, so the click is already informed; (iii) the modal's main interactive content is the tilt/backfocus group toggles, which are meaningless for "go to these absolute positions" (there is no decomposition to choose). The plan itself still comes from `TiltDevicePlanPreviewBuilder.Build` so what executes is exactly what the panel previewed (WYSIWYG preserved).

### Layout — motorized (snapshot available)

```
┌─ Return to run #4 (21:14:05) ─────────────────────────────────────┐
│ Drive each motor back to the position recorded at run #4:         │
│  ┌ TL · Motor 2 ────────┐  ┌ TR · Motor 1 ────────┐              │
│  │ Wizard screw 2       │  │ Wizard screw 1       │              │
│  │ 1240  (now 1258, −18)│  │ 980   (now 971, +9)  │              │
│  └──────────────────────┘  └──────────────────────┘              │
│  ┌ BL · Motor 4 ────────┐  ┌ BR · Motor 3 ────────┐              │
│  │ Wizard screw 3       │  │ Wizard screw 4       │              │
│  │ 1102  (now 1102, 0)  │  │ 1345  (now 1310, +35)│              │
│  └──────────────────────┘  └──────────────────────┘              │
│  (twist warning border here, only when |twist| ≥ 1 step)          │
│  6 moves · about 40 s. Re-run the Inspector afterwards to confirm.│
│  [ Drive Adapter to Run #4 Positions ]                            │
└───────────────────────────────────────────────────────────────────┘
```

- The 2×2 corner cards clone the existing motor-position readout style (`AutoFocus/DataTemplates.xaml:3141-3186`) so corners read identically everywhere. Each card's value line: `"{target}  (now {current}, {signed delta})"`. When twist ≥ 1 step, each card gains the existing `"will reach {n}"` sub-line (same as `TiltAdapterManualAdjustmentVM.RefreshTargetProjections:1373-1386`).
- Move count + time estimate come from the built preview (`TiltDevicePlanPreview.Plan.Moves.Count`, `.EstimatedSeconds`).
- The button is the ONE click. On click: `TryBeginOperation("Return to run #4")` → re-check `Connected` + same controller instance → execute the previewed plan's moves with a journal, reporting `"Return to run #4: move {i} of {n} — {desc}"` to the status bar (with the mandatory empty-report in `finally` — mvvm-patterns rule), calling `PublishControllerPositions()` after every move, and consuming the measurement generation (`lastExecutedMeasurementGeneration = measurementGeneration`) once ≥ 1 move is sent — the physical state changed, so Automatic Adjustment must not fire from the now-stale model. On completion: toast `"Return to run #4 complete: 6 move(s) sent. Re-run the Inspector to confirm."` On mid-plan failure: reuse the existing `HandleExecutionFailureAsync`-style journal-revert offer.
- Plan is built from `delta[wizardIndex] = snapshot[deviceIndex] − CurrentPositions[deviceIndex]` mapped through `ManualAdjustmentTarget.InWizardScrewOrder` — the exact math `TiltAdapterManualAdjustmentVM.TargetDeltaPerScrew:803-816` already uses.

### Layout — manual screws

```
┌─ Return to run #4 (21:14:05) ─────────────────────────────────────┐
│ Turn each screw as shown to bring the adapter from its current    │
│ state (run #7) back to the state measured at run #4:              │
│      Screw 1      Screw 2      Screw 3                            │
│      45° ⟲        —            120° ⟳                             │
│ Computed as the difference between run #7's and run #4's fitted   │
│ models — as trustworthy as those two fits. It assumes only the    │
│ tilt screws changed between the runs; if the camera was rotated,  │
│ the adapter re-seated, or the screws were never touched, this     │
│ difference is measurement drift, not screw motion. Re-run the     │
│ Inspector after adjusting.                                        │
└───────────────────────────────────────────────────────────────────┘
```

- Amounts: `FormatAmount(T_N[i] − T_K[i] /* TotalSteps */, steps:false, tiltAdapterOptions.AngleDisplayUnit)` — same glyph contract as the guidance table (⟳ = clockwise/tighten, positive; never motion arrows here — this row is rotation, per `.claude/docs/tilt-domain.md`). Columns reuse the guidance table's header/column grid (`:3076-3097` pattern), 4th column gated on `HasFourScrews`.
- The caveat is always visible (italic, not a warning border — it is a property of the method, not an anomaly).

### XAML sketch (house style, goes in the Row-10 expander's new grid row)

```xml
<Border Grid.Row="2" Margin="10,0,10,10" Padding="6"
        BorderBrush="{StaticResource BorderBrush}" BorderThickness="1" CornerRadius="3"
        Visibility="{Binding RunReturn.IsVisible, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}">
    <StackPanel>
        <TextBlock FontWeight="Bold" Text="{Binding RunReturn.Header}" />
        <TextBlock Margin="0,3,0,0" TextWrapping="Wrap" Text="{Binding RunReturn.BodyText}" />
        <!-- motorized: 2x2 corner cards cloned from :3141-3186, values from RunReturn.CornerLines -->
        <!-- screws: header+amount grid cloned from :3093-3097 / :3113-3118, IconizedText on amounts -->
        <Border Margin="0,6,0,0" Padding="5" BorderThickness="1"
                BorderBrush="{StaticResource NotificationWarningBrush}"
                Visibility="{Binding RunReturn.HasWarning, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}">
            <TextBlock Text="{Binding RunReturn.WarningText}" TextWrapping="Wrap" />
        </Border>
        <TextBlock Margin="0,4,0,0" FontStyle="Italic" TextWrapping="Wrap"
                   Text="{Binding RunReturn.CaveatText}"
                   Visibility="{Binding RunReturn.HasCaveat, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}" />
        <Button Margin="0,6,0,0" Height="25" HorizontalAlignment="Left"
                Command="{Binding ReturnToRunCommand}"
                Visibility="{Binding RunReturn.ShowDriveButton, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}"
                ToolTip="Sends relative moves to bring each motor to the position recorded at this run, with the same journaling and position publishing as Automatic Adjustment.">
            <TextBlock Margin="5,0" Text="{Binding RunReturn.DriveButtonText}" />
        </Button>
    </StackPanel>
</Border>
```

VM: `public TiltRunReturnVM RunReturn` (POCO, rebuilt on selection change / connection change / positions change, swapped whole like `TiltGuidance`); `ReturnToRunCommand` (AsyncRelayCommand, canExecute = `RunReturn.ShowDriveButton && !service.IsOperationActive`).

---

## 4. Making history rows identifiable (Q4)

Minimum additions — three columns on the sensor-model grid, one on the tilt-plane grid. No more; the grids are already wide.

Model changes:
- `SensorParaboloidTiltHistoryModel`: add `DateTime Timestamp` (ctor, `DateTime.Now` at append), `IReadOnlyList<int> DevicePositionsSnapshot` (device motor order; **null** unless `tiltDeviceConnectionService.PositionsKnown` at append time), `bool AdjustmentAppliedAfterwards` (mutable; set `true` on the entry a plan was computed from when that plan sends ≥ 1 move — Automatic Adjustment, a return-drive, or a banner revert).
- `SensorTiltHistoryModel` (TiltModel.cs): add `DateTime Timestamp` only.

Columns (sensor-model grid, after `Id`):
- **Time** — `{Binding Timestamp, StringFormat=HH:mm:ss}`. Session-scoped history; date is noise.
- **Adjusted** — bool → `"⚙"`/`""` converter, header tooltip: "A device adjustment was applied after this run — the adapter no longer matches this measurement."
- **Pos** — snapshot != null → `"✓"`/`"—"`, header tooltip: "Motor positions were recorded at this run; one-click return is available."

`Pos` is what makes the one-click promise legible *before* selecting a row; `Adjusted` is how the user finds "the run before I broke it" (the newest ⚙ row is the run the last adjustment was computed from — the state to go back to is that row itself, pre-move... precisely: ⚙ marks "measured, then moved away from", i.e. exactly the candidate return targets).

---

## 5. Honesty matrix — exact strings (Q5)

Panel body/warning strings by state. "Disabled" means the drive button is hidden and the string shown in its place (a hidden-with-reason pattern, matching `AutomaticAdjustmentRemediationText`'s approach of text-instead-of-capability); "caveat" means the action stays available.

Motorized rig (`AdjustmentType == StepperMotors`):
1. **Device disconnected** — no button. "Connect the tilt adapter device to drive it back to this run's positions. (Its recorded positions for this run: TL 1240 · TR 980 · BL 1102 · BR 1345.)"
2. **Connected, `PositionsKnown == false`** — no button. "The device's current motor positions are unknown, so the moves to reach this run's positions cannot be computed. Disconnect and reconnect the tilt adapter device to resync positions from the hardware."
3. **Device busy (`IsOperationActive`)** — button disabled (not hidden; transient). Tooltip/inline: "The tilt adapter device is busy with another operation. Try again when it finishes."
4. **Run has no snapshot (`DevicePositionsSnapshot == null`)** — no button; fall back to the model differential shown as signed steps ("Screw 1 −14 steps · Screw 2 +9 steps · …" via `FormatAmount(…, steps:true, …)`). "No motor positions were recorded at this run (the device was not connected when it was measured), so one-click return is unavailable. The step differences below are estimated from the two fitted models — treat them as guidance, not ground truth." + the standard screw caveat.
5. **Twist between snapshot and current ≥ 1 step** — caveat, warning border, button stays: "These recorded positions differ from the current positions by a twist of ±3 steps, which the adapter cannot make — position tracking may have drifted since run #4 (lost steps or a resync). Each corner shows the position it will actually reach." (`TiltMovePlanner.Decompose(delta).t`; "will reach" values from the built plan's applied moves, exactly like `RefreshTargetProjections`.)
6. **Selected row is the newest run** — no button. "This is the most recent run — the adapter should already be in this state. If you have adjusted anything since, re-run the Inspector first."
7. **All deltas zero** — no button. "The adapter is already at this run's recorded positions — nothing to send."

Manual screws:
8. **Selected row is the newest run** — "This is the most recent measurement — no turns needed."
9. **Every screw under the noise floor** (all render "—") — "Runs #7 and #4 are within measurement noise of each other — no meaningful turns to make."
10. **Normal case** — per-screw turn row + the standing caveat (§3): "…It assumes only the tilt screws changed between the runs; if the camera was rotated, the adapter re-seated, or the screws were never touched, this difference is measurement drift, not screw motion."

There is deliberately **no gate** on "did the user actually apply past guidance" — it is unknowable; the caveat says so instead of pretending.

---

## 6. Feature D — the worsening banner

### Placement
First child of the calibrated-guidance `StackPanel` in Row 5 (`AutoFocus/DataTemplates.xaml`, immediately after the `StackPanel` opening at `:2922`, above the direction legend at `:2925`). Rationale: it must be the first thing read in the panel whose numbers it disclaims, and it must sit next to the Automatic Adjustment button whose action caused it. It intentionally lives *outside* the `IsTiltDeviceConnected` block (`:3134`) so it survives a device disconnect (the revert button then disables with a fallback line; the banner does not vanish).

### Trigger — generalized from today's check
On **every completed analysis** (in `AnalyzeAutoFocusResult`, where `measurementGeneration` is incremented), compare the two newest history entries: if `TiltWorsened(TiltMagnitude(prev), TiltMagnitude(new))` (existing `:2204-2221` margins), raise the banner for the pair (prev = run K, new = run N). The post-Automatic-Adjustment case (`:2426-2448`) is the special case where a valid journal for prev exists; the modal (`confirmPromptAsync` at `:2432-2434`) is deleted, the `Notification.ShowError` at `:2429-2431` is kept verbatim. This generalization is what makes the banner useful on manual-screw rigs at all — nothing else ever detects a hand-turn that made things worse.

### Content — exact strings
Headline (bold, `NotificationErrorBrush` foreground): **"Tilt got worse after the last adjustment"**

Body, motorized with valid journal:
> "Tilt effect rose from 38 µm (run #6, 21:14:05) to 61 µm (run #7, 21:26:40) after the 6 moves Automatic Adjustment sent. This can indicate a stale calibration, a rotated camera/adapter, or an incorrect screw-direction setting — investigate before adjusting again."

Button: **"Revert the 6 moves"** — tooltip "Sends the inverse of each move, in reverse order." On click: acquire `TryBeginOperation("Worsening revert")`, verify `service.Connected && ReferenceEquals(service.Controller, journalController)`, then the existing `RevertJournalAsync(controller, journal, "post-adjustment worsening")`, then consume the confirming measurement (`lastExecutedMeasurementGeneration = measurementGeneration; AutomaticAdjustmentCommand?.NotifyCanExecuteChanged();` — the exact semantics of `:2445-2446`, preserved).

Fallback line replacing the button when the journal is invalid (disconnect, different controller instance, restart):
> "The moves from this session can no longer be reverted directly (the device was disconnected). Select run #6 under Sensor Model Tilt Measurement History and use Return to this run to drive the adapter back."
(If run #6 carries a positions snapshot the return path is exact; the banner does not need to say more — the return panel's own honesty states apply.)

Body, manual screws (or motorized with neither journal nor snapshot):
> "Tilt effect rose from 38 µm (run #6, 21:14:05) to 61 µm (run #7, 21:26:40). If you adjusted the screws between these runs, turn them as shown to return to run #6's state:"

followed by the concrete per-screw row (same grid as §3, `FormatAmount(T_7[i] − T_6[i], …)` — e.g. `Screw 1  45° ⟲   Screw 2  —   Screw 3  120° ⟳`), then:
> "If you changed nothing between the runs, dismiss this — the increase is measurement drift, not the screws."

Post-revert success state (border switches to `NotificationWarningBrush`, mirroring the wizard's severity-switching result panel `TiltAdapterWizard/DataTemplates.xaml:636-664`):
> "The 6 moves were reverted. Re-run the Inspector to confirm tilt is back near run #6's level."

### Lifecycle
- **Appears:** when the trigger fires (above). The `Notification.ShowError` toast still fires alongside — toast for immediacy, banner for persistence-with-context (the user's stated goal).
- **Disappears:**
  1. **Dismiss button — yes, it is dismissable.** Once read and decided ("that was drift", "I'll re-adjust instead"), a permanent red box becomes banner blindness; house precedent is the wizard's dismissable error result panel (`DismissResultCommand`). Dismissal is per-instance — the same pair never re-raises.
  2. **Automatically on the next completed analysis.** The banner describes exactly one consecutive pair (K → N); a new run supersedes it and the check re-evaluates for the new pair. This is the honest behavior: the banner can re-fire immediately if the new run is *also* worse than its predecessor.
  3. **After a successful revert** it does not vanish silently — it switches to the post-revert success state (dismissable, also cleared by the next analysis). Vanishing outright would leave no on-screen record that hardware just moved.
- It is **not** cleared by history-row selection, device disconnect (button degrades to the fallback line instead), or collapsing/expanding the guidance expander.

### XAML sketch (house style — the wizard's red action box `TiltAdapterWizard/DataTemplates.xaml:1797-1837` is the template)

```xml
<Border Margin="5,4,5,2" Padding="6" BorderThickness="1" CornerRadius="3">
    <Border.Style>
        <Style TargetType="Border">
            <Setter Property="Visibility" Value="Collapsed" />
            <Setter Property="BorderBrush" Value="{StaticResource NotificationErrorBrush}" />
            <Style.Triggers>
                <DataTrigger Binding="{Binding WorseningBanner.IsVisible}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
                <DataTrigger Binding="{Binding WorseningBanner.IsRevertedState}" Value="True">
                    <Setter Property="BorderBrush" Value="{StaticResource NotificationWarningBrush}" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Border.Style>
    <StackPanel>
        <TextBlock FontWeight="Bold" Foreground="{StaticResource NotificationErrorBrush}"
                   Text="{Binding WorseningBanner.Headline}" TextWrapping="Wrap" />
        <TextBlock Margin="0,3,0,0" Text="{Binding WorseningBanner.BodyText}" TextWrapping="Wrap" />
        <!-- screws / no-journal: per-screw amounts grid (clone of :3093-3097 header + IconizedText amounts) -->
        <TextBlock Margin="0,3,0,0" Text="{Binding WorseningBanner.FooterText}" TextWrapping="Wrap"
                   Visibility="{Binding WorseningBanner.HasFooter, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}" />
        <StackPanel Margin="0,6,0,0" Orientation="Horizontal">
            <Button Height="25" Command="{Binding RevertLastAdjustmentCommand}"
                    Visibility="{Binding WorseningBanner.ShowRevertButton, Converter={StaticResource BooleanToVisibilityCollapsedConverter}}"
                    ToolTip="Sends the inverse of each move, in reverse order.">
                <TextBlock Margin="5,0" Text="{Binding WorseningBanner.RevertButtonText}" />
            </Button>
            <Button Height="25" Margin="6,0,0,0" Command="{Binding DismissWorseningBannerCommand}"
                    AutomationProperties.Name="Dismiss this warning">
                <TextBlock Margin="5,0" Text="Dismiss" />
            </Button>
        </StackPanel>
    </StackPanel>
</Border>
```

VM: `public TiltWorseningBannerVM WorseningBanner` (swap-whole-object POCO carrying: pair ids/timestamps, before/after `TiltEffectMicrons`, journal + controller reference or per-screw strings, state flags); commands `RevertLastAdjustmentCommand`, `DismissWorseningBannerCommand`.

Note: `BorderThickness="1"` with `NotificationErrorBrush` matches the closest existing action-carrying red box (`TiltAdapterWizard/DataTemplates.xaml:1798-1802`); the `Red`/3px style (`AutoFocus/DataTemplates.xaml:2202-2216`) is the text-only fatal-error style and is deliberately not used for an actionable banner.

### Generation semantics (unchanged from today, restated)
Raising the banner consumes nothing — the worse confirming run is a genuine measurement and adjusting forward from it is legitimate. A successful banner-revert consumes the current generation (stale-model guard, `:2438-2446` comment applies verbatim). A return-drive from Feature C likewise consumes on ≥ 1 move sent.

---

## 7. How C and D compose (Q7)

**D's button stays a journal-based inverse revert; it does not invoke C.** The journal is exact for the moves this session just sent — it replays the actual commands inverted (including any prepended backfocus bias and the excursion-minimizing order), needs no `PositionsKnown`, no snapshot, and no model. Routing D through C's positions-targeting would trade an exact mechanism for one with more failure states (positions unknown, snapshot missing) for zero benefit in the case where the journal exists.

**When the journal is unavailable** (restart, disconnect/reconnect → different controller instance, or a manual-screw rig where nothing was journalled), **the banner's action degrades to C**: the fallback line points at run K in the history grid ("Select run #6 … and use Return to this run"), or — for screws — the banner embeds C's differential math directly as the per-screw turn row. So: D is the specialized fast path; C is the general mechanism and D's universal fallback. Both share one executor path (lease → verify → journalled moves → publish positions → consume generation → status-bar finally-empty), so an implementer builds the executor once.

---

## Implementation notes (for the eventual plan)
- No new persisted options → no `Resources/OptionsDataTemplates.xaml` obligation. No `StarDetectorMetrics` change.
- New VM surface on `InspectorVM`: `RunReturn` (`TiltRunReturnVM`), `WorseningBanner` (`TiltWorseningBannerVM`), `ReturnToRunCommand`, `RevertLastAdjustmentCommand`, `DismissWorseningBannerCommand`. All banner/panel rebuilds must publish via `applicationDispatcher.PostSynchronizationContext` (never a blocking dispatch — UI-thread rules in mvvm-patterns.md), matching `RebuildTiltGuidance:1969-1990`.
- History model ctor changes touch `SensorModel.UpdateModel:162-175` (stamp `Timestamp`, capture `DevicePositionsSnapshot` when `PositionsKnown`) and `TiltModel.UpdateTiltMeasurementsTable:200-208` (`Timestamp`).
- `RunAutomaticAdjustmentAsync:2426-2448` loses the modal; sets `AdjustmentAppliedAfterwards` on the source entry when ≥ 1 move sent; raises the banner instead.
- Unit-testable seams: pair-worsening trigger, differential math (`T_N − T_K` vs `ComputePerScrewTargets` on both models), snapshot→delta mapping, banner state machine (fired → reverted → dismissed/cleared), generation consumption on return-drive.
