# Tilt Adapter Screw Labels — Implementation Plan

Design spec: [docs/tilt-screw-labels-design.md](../docs/tilt-screw-labels-design.md) — read it first; it
carries the index-space table, the persistence contract, and the display grammar this plan executes.

## Tasks

**0. Project bookkeeping.** Design spec written to `docs/tilt-screw-labels-design.md`, this plan to
`plans/tilt-screw-labels-plan.md`. Work on branch `ghilios/tilt-screw-labels`; never push `develop`.

**1. Scheme + preset wiring.** `ScrewLabelScheme` in `TiltAdapterDevicePreset.cs`; `ScrewLabels`
property on the preset defaulting to `Generic`; the two EAT entries get `AsgEat`. Tests: `AsgEat`
yields M1/M2/M4/M3 for screws 1..4 **and** is derived from `TiltAdapterCorner`, not a literal; every
preset in `All` has a non-null scheme.

**2. Persistence.** `ScrewLabelsJson` on `ITiltAdapterOptions` + `TiltAdapterOptions` (load in
`InitializeOptions`, write via `optionsAccessor.SetValueString`). Typed get/set for
(schemeId, screwNumber). Tests: round-trip through `InMemoryPluginOptionsAccessor`; corrupt JSON warns
and yields empty, never throws; per-scheme isolation (setting an `AsgEat` label leaves `Generic`
untouched).

**3. Resolver.** `TiltScrewLabels.cs` + `IScrewLabelProvider`; `TiltAdapterOptions` exposes one.
Tests: override wins; blank falls back to scheme default; changing `DeviceName` swaps the active set;
`ScrewCount == 3` never asks for screw 4.

**4. Editor UI.** The "Screw Labels" Expander in `TiltAdapterWizard/DataTemplates.xaml` + four
bindable override properties on `TiltAdapterWizardVM` that delegate to the options store, raising
`PropertyChanged` on `DeviceName` change (extend the existing raise-list in `ApplyDevice`,
`TiltAdapterWizardVM.cs:2789-2803`).

**5. Inspector guidance tables.** Add `Screw{1..4}Header` to `TiltAdapterGuidanceVM`
(`AutoFocus/TiltScrewGuidanceRow.cs`), populate in `InspectorVM.FillNumericGuidance`/
`RebuildTiltGuidance`, bind both header rows in `AutoFocus/DataTemplates.xaml` (lines ~3086-3104 and
~3211-3214). **That VM is a no-INPC swap-whole-object DTO** — assert the binding refreshes on guidance
rebuild, not just that the property is set.

**6. Wizard prose, titles, chips, diagram.** Thread `IScrewLabelProvider` into `StepInstructionsText`,
`BaselineRecoveryText`, `StepTitleText`, `StepDescription`. Reword the Baseline step to name the
labels and point at the editor. Diagram: `TiltScrewDiagramItem` gains `Label`; `RebuildDiagram`
(`:3837`) sets it; canvas grows to 260×260 per the geometry note. ~28 assertions in
`TiltAdapterWizardVMTests` pin these strings and will need updating — update them, do not weaken them.

**7. Approval dialog, motion lines, 2×2 grids, pad tooltips.** `TiltDeviceAdjustmentPromptVM`
(`FormatScrews` :414, `BuildCornerResiduals` :430 — and drop its duplicate hardcoded
`ScrewCornerLabels`, resolving through `TiltAdapterCorner` instead), `TiltRunReturnVM.DescribeMotion`
(:169-185), both copies of the 2×2 grid, `TiltAdapterManualAdjustmentVM` cell tooltips. `CellHeading`
moves from a static `TiltAdapterCorner` property to a call taking the label provider.

**8. Camera simulator panel.** `SimulatedTiltAdapterVM` already holds `realAdapter`
(`ITiltAdapterOptions`, :130) — resolve labels through it so the sim's row labels (`RowNameFor` :767,
`MotionSummary`'s `S{n}` form :783, coherence rows :570) match the rest of the UI.

**9. Docs.** `documentation/docs/overview/tilt-adapter-wizard.md` and `motorized-tilt-adapter.md` —
describe the feature and the EAT defaults. `motorized-tilt-adapter.md:38-39` currently spells out the
corner↔screw map in prose and should reference labels. Follow `.claude/docs/documentation-style.md`.

**10. Full suite + live check**, then PR.

---

## Verification

**Unit tests** (no `dotnet` in WSL — invoke the Windows binary via interop; suite baseline is ~4052):

```
dotnet.exe test "$(wslpath -w Joko.NINA.Plugins/Joko.NINA.Plugins.sln)" -c Debug --nologo
```

Per the user's cadence preference, run at milestones (after tasks 3, 6, 9) and as a final gate — not
after every edit. `SendAsync_WritesOnABackgroundThread` is a known flaky EAT serial-transport test,
unrelated to this work.

**Live check in NINA** — the payoff is visual, and several of these surfaces have no test coverage.
Launch via `Start-Process` (not MCP `launch_executable`); the build's PostBuild step already xcopies
the plugin into NINA's plugin folder.

1. Tilt Adapter Wizard → settings → select **ASG Electronic EAT - 90mm**. Confirm the guidance
   headers, calibration angle rows, and diagram all read M1/M2/M4/M3 with **no** typing, and that
   screw 3 shows **M4** (the permutation is the whole point).
2. Type "Top Left" into screw 2's box. Confirm it propagates to the Inspector guidance header
   (ellipsized, tooltip intact), the wizard prose, and the diagram.
3. Switch to **Manual**. Confirm labels revert to "Screw N" and the custom "Top Left" is *not* lost.
   Switch back to the EAT — it returns. This is the per-device store working.
4. Restart NINA; confirm labels persist per profile.
5. With the simulated EAT connected, open the Automatic Adjustment approval dialog and confirm the
   move lines read "M1 & M4 …". Check the 3×3 pad faces are **unchanged** and its tooltips gained labels.

**Git**: feature branch + PR (never push `develop`); commit with
`322725+ghilios@users.noreply.github.com` as both author and committer.
