# Per-Filter Auto-Focus Sweep Geometry — Design

## Summary

Per-filter star detection (`docs/per-filter-star-detection-design.md`) gives every filter its own complete
**detection** settings set. It deliberately left **auto-focus sweep geometry** alone: `AutoFocusStepSize` and
`AutoFocusInitialOffsetSteps` stayed single profile-wide values on `FocuserSettings`.

That left a hole with a real failure mode. The Optimization Wizard's Accept splits its writes — optimized
detection settings go to the **target filter's** set, while the recommended step size and offset steps go to the
**profile**. Optimize Ha, then optimize L with "Apply these auto-focus settings" checked both times, and L's
sweep geometry silently replaces Ha's. Detection settings stay separated per filter; sweep geometry did not.

Both numbers now become per-filter overrides, edited in the Star Detector settings and visible only when
per-filter detection is enabled. Blank means inherit the profile value.

## Decisions

1. **Both numbers, not just the step size.** They are recommended and applied as a pair by the optimizer, so
   splitting them across two scopes would be incoherent. They nonetheless **resolve independently**: a filter may
   override the step size and inherit the offset. The pairing is a wizard/UI convention, not a storage constraint.
2. **Storage is a sibling record**, `PerFilterSweepGeometry` on `PerFilterStarDetectionEntry` — *not* two more
   properties on `IStarDetectionOptions`. That interface is also the replay wire format and the import/export diff
   surface, and its coverage guard forces every property into "importable" or "machine-local". Importable would
   mean a star-detection export file silently rewriting a focuser sweep on another rig; machine-local would be a
   lie about a per-filter persisted setting. Neither bucket is honest, so the interface is untouched.
   *If an implementation finds itself editing `ImportableSettings` / `ExcludedFromImport`, this fork was taken wrongly.*
3. **Sentinel is `-1`**, copying core's own per-filter AF idiom (`FilterInfo.AutoFocusExposureTime > -1`). The
   ideal home really is `FilterInfo`; a plugin cannot add properties to a core type, so this is the closest copy.
4. **`SchemaVersion` stays 1.** A version number earns its keep only when a reader must behave differently, and it
   cannot here: the same blob legitimately holds a mix of entries with and without geometry (a v1 blob this build
   upserts one filter into becomes a hybrid), so a bump would encode a claim that is false for most real blobs.
   The missing half was the other direction — a newer-schema blob now logs and loads best-effort rather than being
   read as if it were ours, because silently wiping a user's sets is worse than ignoring an unknown field.
5. **Enable does not seed geometry**, unlike detection. Detection *must* seed because `ResolveEffectiveOptions`
   has no fallback; geometry falls back to the live profile value. A seeded copy would freeze and drift the moment
   the profile value changed — invisibly, since on day one the two numbers agree — and would destroy the
   "never set" state that makes clearing an override meaningful.
6. **Copy Settings From carries geometry; Export/Import does not.** The documented workflow is "tune one narrowband
   filter, then copy to the others", and a copy that reproduced only the detection half of one recommendation would
   drop the half the user notices first. Export is documented as star-detection only, and step size is a property of
   a specific focuser's step scale, so carrying it between machines would be actively wrong.
7. **Wizard Accept writes per-filter.** With per-filter on, the recommended geometry goes to the target filter. With
   no target filter it refuses and logs, rather than falling back to a profile write — that fallback would be the
   very clobber this design removes.
8. **The live sweep's AF exposure follows the same rule.** In per-filter mode it is written to core's own
   `FilterInfo.AutoFocusExposureTime` (honored in `AutoFocusEngine.TakeExposure`, where `> -1` beats the profile)
   rather than to `FocuserSettings`. Every "did the exposure change?" comparison — the summary's
   "(unchanged)" row and the Apply checkbox's enablement — resolves through the same target-filter-then-profile
   chain, or both would lie whenever the target filter already carries its own exposure. The editable sweep
   exposure is likewise seeded from the target filter, and re-seeded when the target changes unless the user has
   typed their own value.

## Resolution point — the crux

Geometry is resolved inside **`AutoFocusEngine.GetOptions`**, via optional `imagingFilter` /
`useExactImagingFilter` parameters, with a wheel fallback when no filter is passed.

Resolving later — in `InitializeState`, right after `SetAutofocusFilter` has determined the real filter — reads as
the obviously central place and is wrong. Three caller-side transforms run *after* `GetOptions`, and two are
**relative**:

| Transform | Kind |
|---|---|
| `InspectorVM.ApplySignalAmplification` | **relative** — divides the step size, multiplies the offset |
| `StarDetectionOptimizerWizardVM.ApplyFocusRecovery` | **relative** — widens the offset |
| `ApplyRecaptureGeometry` | absolute — replaces the step size |

Overwriting both fields in `InitializeState` would make a 3× amplified sensor-model sweep run at the
*un-amplified* step and offset — its entire reason for existing removed, with no exception and no wrong-looking
number — and would clobber `ApplyRecaptureGeometry`, re-introducing the F51(b) field bug. An "explicit override"
flag does not rescue it: amplification would already have transformed the wrong base. Separately,
`CaptureFixedSweepImpl` validates the geometry *before* `InitializeState`, so it would be validating numbers about
to be replaced.

Per-caller resolution is right about *when* and wrong about *where*: it would mean three copies of the resolution
plus a fourth copy of the AF-filter substitution rule. That risk was not hypothetical — two copies of the
substitution rule already existed. Hence `AutoFocusFilterResolver`, a pure static that both existing copies now
route through, guarded by a fixture that drives the full input matrix through the engine and asserts the resolver
picks the same `FilterInfo`.

`InitializeState` **warns** if the filter it ends up exposing through differs from the one geometry was keyed on,
and uses the already-resolved geometry. Re-resolving there is precisely the failure above.

### Precedence

- **Step size:** saved attempt (replay) → per-filter override → profile → caller transforms.
- **Offset steps:** per-filter override → profile → replay metadata → inspector `StepCount` → amplification → recovery.

Monotone, and every relative transform composes on a correctly-resolved base.

### Which filter is the key

With `UseFilterWheelOffsets` on, the **designated AF filter**. Those are the frames' actual optical path, and it is
the same name per-filter *detection* keys on for that run (detection reads the capture-time filter from image
metadata), so geometry and detection agree filter-for-filter within one run.

### Wheel disconnected

Fall back to the profile, **silently** (Debug log). The asymmetry with detection — which throws
`PerFilterSettingsUnavailableException` — is deliberate: detection has no fallback, geometry does, and hard-failing
an auto-focus run over a *sweep spacing* would be a regression. The owned entry points already refuse up front; this
path is reached by NINA's own auto-focus and the headless harness.

### Reloaded charts

`HocusFocusVM.SetCurveFittings` keys on the saved report's own `Filter` (written from the resolved AF filter), not
on whatever is in the wheel now — the chart on screen may be from another night through another filter.

## UI

Three rows in the existing per-filter grid, gated by the same `ShowOnPerFilterEnabled` row style: an italic header
("Auto-Focus Sweep for This Filter"), then Step Size and Initial Offset Steps. The header answers the genuine
"why are focuser numbers on the star-detection page" objection in place, which beats moving them somewhere the
per-filter visibility plumbing does not reach.

**Blank = inherit**, with the profile value shown as a dimmed `profile: N` hint. The hint is an overlaid
`TextBlock` at `Opacity="0.6"` rather than `HintTextBox`'s own hint: that control's `HintTextOpacity` is not
settable in the NINA version this plugin builds against (3.2.0.2001-beta), and its 0.4 default — fine for a static
`"(AutoFocus)"` label — is too faint for a number the user is meant to read and compare a typed value against.

**No validation rule** on either box: rules run on the raw text before the converter and would reject the
deliberately blank state. Coercion happens in the binder's setter instead. The bound properties are non-nullable
`int` with the `-1` sentinel, both to match the house idiom and because
`DoubleNegativeToEmptyStringConverter.Convert` dereferences without a null guard.

Clearing the box is the reset affordance, taught in the tooltips — the same way `FocuserStepSizeMismatch_Tooltip`
already teaches it. No revert button; it would be the only one on the page.

**Rejected as gold-plating:** override markers in the filter dropdowns. Their `ItemsSource` is a plain string list
shared by both combos, so markers would mean an ItemTemplate, per-item store lookups and change notification when
any filter's override moves — for a wheel of ≤ 8 filters whose every other per-filter setting has no such marker.

## Out of scope

- **Deriving a reloaded chart's step size from `report.MeasurePoints` spacing** (filter-agnostic, and right even
  for other auto-focusers' reports). A strict improvement over the per-filter lookup, but it changes behavior for
  existing charts too.
- Downgrading to an older plugin build drops the overrides (`MissingMemberHandling.Ignore`). Correct and
  unavoidable; release-note it.
