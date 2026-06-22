# Review Frames — Feedback Round 1

## Context

The "Review Frames" diagnostic (PR #88, branch `ghilios/frame-review`) lets a user step through a Sensor Curve Model sweep and see each frame's accepted stars with HFR + registration overlays. After trying it, the user asked for 10 refinements that (a) surface *why* a star did or didn't contribute to the sensor-model fit (registration + focus-fit status per star), (b) make the registration geometry and focus quality directly visible (per-star focus graph on hover, optimal-focus offsets), and (c) fix usability (window too small, summary buried in an in-image HUD, "deg" instead of "°"). 

Two findings shape the work:
- **Per-star fit data is currently discarded.** `RegisteredStar.Fitting` is declared but never assigned; `SensorModel.FitImages` computes each star's hyperbolic fit (R², best-focus, curve) into locals and throws it away. Items 2/7/8 require persisting it.
- **Overlays are mis-registered on aligned frames.** The displayed image is the *raw* per-frame bitmap, but `ApplyAlignmentTransform` overwrites each star's `Position`/`BoundingBox` to *reference* coordinates, so with RANSAC on the boxes sit offset from the actual stars. We fix overlays to draw in raw-frame coordinates (item 3).

**Execution note:** On starting execution, copy this plan into the repo at `plans/frame-review-feedback-plan.md` (per CLAUDE.md), branch from the current PR branch `ghilios/frame-review` (continue the same PR #88, or a stacked branch if preferred), and run the suite via Windows dotnet: `dotnet.exe test 'C:\Users\ghili\src\nina.plugins\Joko.NINA.Plugins\Joko.NINA.Plugins.sln' -c Debug --nologo`.

### Decisions already made with the user
- **Item 3 target** = the star's *aligned* `Position` (registration displacement); overlays drawn in raw-frame coords.
- **Item 8** = per-star **offset from the field-mean** best-focus (e.g. "+18"), legend-colored text label, toggle off by default.
- **Item 7 overlay** = a **fixed corner panel** (never clipped), reusing the existing `OptimizationCurve` OxyPlot DataTemplate.

---

## Step 1 — Persist per-star data (enabling change)

**`StarDetection/HocusFocusStarDetection.cs`** (`HocusFocusDetectedStar`, ~line 210): add
`public System.Drawing.Rectangle OriginalBoundingBox { get; set; }` next to `OriginalPosition`.

**`Inspection/SensorModel.cs`**:
- ~line 407 (the pre-alignment brightness loop, right after `star.OriginalPosition = star.Position;`): add `star.OriginalBoundingBox = star.BoundingBox;`. This is the one place that runs for every star before `ApplyAlignmentTransform`, so it captures the raw box.
- `RegisteredStar.Fitting` (~line 1317): widen the field type `HyperbolicFittingAlglib` → `AlglibHyperbolicFitting` (superclass; matches `OptimizationCurve.Fit`). **Verify zero existing readers** (grep `.Fitting` on RegisteredStar — exploration found none).
- `FitImages` `Parallel.For` body: insert `registeredStar.Fitting = fitting;` **between line 724 and 726** — i.e. only on the success path, after both discard gates (`!solveResult || fitting == null` at 714, and `RSquared < PerStarAcceptableRSquared` at 720). This makes `RegisteredStar.Fitting != null` an exact proxy for **MatchedWithFit**, leaves the surface-fit discard logic untouched, and is race-free (each iteration writes a distinct `registeredStar`).
- Make `EstimateHfrStdDev` (~line 589) `internal static` so the snapshot builder can reconstruct faithful error bars for the hover chart (same assembly).

**Risk to verify during execution:** confirm the `registeredStars` array `FitImages` mutates is the same one that flows into `SensorModel.SensorModelResult.RegisteredStars` (the brightness-search loop keeps `bestReg`). If `FitImages` runs per brightness-iteration on different arrays, ensure the winning array's objects carry `Fitting`.

## Step 2 — Snapshot DTOs + builder + tests (`Inspection/FrameReviewSnapshot.cs`)

Extend the DTOs (keep init-only, immutable, disposal-safe):
- `enum FrameReviewRegistrationState { Unmatched, MatchedNoFit, MatchedWithFit }`.
- `FrameReviewStar`: box coords now from **`OriginalBoundingBox`** + center from **`OriginalPosition`** (raw); add `TargetX/TargetY` (the aligned `Position`), `RegistrationState`, `double? FocusOffsetFromMean`. Keep `RegistrationId`, `HasArrow`→rename to `HasRegistrationLine` (= `ransacEnabled && !IsReference && RegistrationId != null && (TargetX/Y != raw center)`).
- New `FrameReviewFocusCurve` DTO: `int RegistrationId`, `AlglibHyperbolicFitting Fit`, `IReadOnlyList<ScatterErrorPoint> Points`, `double RSquared`, `double BestFocus`, `double? OffsetFromMean`.
- `FrameReviewSnapshot`: add `IReadOnlyDictionary<int, FrameReviewFocusCurve> FocusCurvesByRegistrationId` (one per matched-with-fit registered star, shared across frames).

`FrameReviewSnapshotBuilder.Build`:
- Map each accepted star's `RegistrationState`: `Unmatched` if `RegistrationId == null`; else read `registeredStars[id].Fitting` — `MatchedWithFit` if non-null, `MatchedNoFit` otherwise.
- Compute **field-mean best-focus** = mean of `registeredStars[*].Fitting.Minimum.X` over all matched-with-fit stars; per-star `FocusOffsetFromMean = Fitting.Minimum.X - mean` (focuser steps; null for non-fitted).
- Build `FocusCurvesByRegistrationId`: for each matched-with-fit registered star, `Points` from `MatchedStars.Select(s => new ScatterErrorPoint(s.FocuserPosition, s.Star.HFR, 0, EstimateHfrStdDev(s.Star)))`, `Fit = registeredStars[id].Fitting`, plus `RSquared`/`BestFocus`/`OffsetFromMean`.
- Transform text: add a Review-specific `FrameReviewTransformFormatter.Format(Matrix3x2)` mirroring `ToFullString` but emitting **`°`** (degree symbol). **Do NOT edit `Matrix3x2.ToFullString`** (3 other callers depend on it). Call it from the builder.

**Tests** (`Tests/Inspection/FrameReviewSnapshotBuilderTests.cs`):
- *Change*: the transform-text test → assert the new formatter + `°`; the box-coords + immutability tests → set/assert `OriginalBoundingBox` (raw) rather than the transformed box.
- *Add*: registration-state mapping (unmatched / matched-no-fit / matched-with-fit), `FocusOffsetFromMean` (signed offset vs computed mean), target = aligned `Position`, focus-curve population (one per fitted star, points + RSquared), formatter `°`. Constructing a real `AlglibHyperbolicFitting` for "with-fit" needs an alglib solve — check for an existing test helper / `IAlglibAPI` fake; if none, fit a tiny synthetic point set.

## Step 3 — `FrameReviewVM`

- Toggles (default **false**): `bool ShowRegistration`, `bool ShowFocusOffset` (raise PropertyChanged + re-evaluate marker/legend visibility on change).
- New frozen brushes: `RegisteredNoFitBrush` (red, e.g. `#FF5252`), `UnregisteredBrush` (grey, e.g. `#9E9E9E`), `FocusOffsetBrush` (violet, e.g. `#E040FB`). Keep AcceptedBrush green / RegistrationBrush cyan / ArrowBrush orange.
- `FrameReviewMarker`: raw `BoxX/Y/Width/Height`; `Brush BoxBrush` (green/red/grey per `RegistrationState`); `RegistrationIdText`; `HasRegistrationLine` + `CenterX/Y` (raw, cyan "+") and `TargetX/Y` (aligned, orange "+"); `FocusOffsetText` (e.g. "+18"/"−12"/""). Drop the old `ArrowHead`.
- Hover: `OptimizationCurve HoverCurve`, `string HoverRSquaredText`, `string HoverOptimalFocusText`, `bool ShowHover`; `SetHover(int registrationId)` (looks up `snapshot.FocusCurvesByRegistrationId`, builds/owns an `OptimizationCurve { Points, Fit }`, sets R² + optimal-focus text) and `ClearHover()`.
- Header summary props already exist (`DetectedCountText`, `ReferenceNote`, `TransformText`) — keep; they move to the header in the View.
- Legend: extend `StarReviewLegendEntry` (StarReviewVM.cs) with `bool Enabled { get; set; } = true` (additive; StarReview wizard unaffected). Rebuild `LegendEntries` (raise PropertyChanged; drop init-only) on toggle change, setting `Enabled` per toggle: rows = Accepted(fit)/RegisteredNoFit/Unregistered (always enabled) + Registration ID & path (cyan) + Registration target (orange) [enabled = ShowRegistration] + Best-focus offset (violet) [enabled = ShowFocusOffset].

## Step 4 — `FrameReviewControl.xaml(.cs)`

- **Header (item 5):** add a summary header above the image (extend the row-0 area) showing `FrameHeader` + `PositionLabel` + `DetectedCountText` + `ReferenceNote` + wrapped `TransformText`. **Delete the in-image HUD `Border`.**
- **Overlays in raw coords (item 3):** box layer `Stroke="{Binding BoxBrush}"`; replace the arrow layer with: a cyan "+" at `CenterX/Y`, an orange `Line` (`CenterX/Y`→`TargetX/Y`) + orange "+" at `TargetX/Y`. Render each "+" as two short crossing `Line`s (or a `Path`) sized constant on-screen via the inverse-zoom `MarkerTextScale`. Gate the reg-id label + the cyan/orange "+" + line on `ShowRegistration` (BoolToVis). Add a `FocusOffsetText` label (violet) near the box, gated on `ShowFocusOffset`.
- **Hover graph (item 7):** a fixed-corner overlay `Border` (top-right of the image area, sibling of the image Border so it isn't clipped), visible on `ShowHover`, containing `<ContentControl Content="{Binding HoverCurve}"/>` (resolves the existing `OptimizationCurve` OxyPlot template) + TextBlocks for `HoverRSquaredText` and `HoverOptimalFocusText`. In code-behind `MouseMove` (currently pan-only): when not panning, map cursor via `Vm.Viewport.ScreenToImage`, hit-test `Vm.Markers` (smallest-area `Contains`, only `MatchedWithFit`); call `Vm.SetHover(id)` or `ClearHover()`. Wire the Canvas `MouseLeave` to `ClearHover()`.
- **Toggles (item 4, 8) + greyed legend (item 9):** under the legend add two checkboxes — "Show registration" and "Show optimal-focus offset" (both bound to the VM toggles). In the legend `ItemTemplate`, add an `Opacity` DataTrigger on `Enabled == false` → `0.4` (greys disabled rows).
- **OxyPlot availability:** add `xmlns:oxy="clr-namespace:OxyPlot.Wpf;assembly=OxyPlot.Contrib.Wpf"` if directly referencing `<oxy:Plot>`; preferred is the `ContentControl`+implicit-template route (no xmlns needed) — but the `OptimizationCurve` template lives in `StarDetection/Optimization/DataTemplates.xaml`; confirm it's in scope for `FrameReviewControl` (it is the same merged dictionary the control's own template is registered in; if resolution fails, add a `MergedDictionaries` ref or move the template).
- **Larger window (item 10):** in `StarDetection/Optimization/DataTemplates.xaml`, wrap the `FrameReviewVM` template content in a root with the wizard's `Grid.Style` size idiom — set ~`Width=1280`, `MinHeight=860` (resizable, `WindowStyle.SingleBorderWindow`).

## Critical files

| File | Change |
|---|---|
| `StarDetection/HocusFocusStarDetection.cs` | `OriginalBoundingBox` on `HocusFocusDetectedStar` |
| `Inspection/SensorModel.cs` | capture raw bbox (~407); widen + assign `RegisteredStar.Fitting` (~724); `EstimateHfrStdDev` internal |
| `Inspection/FrameReviewSnapshot.cs` | DTO extensions, registration-state + offset + focus-curve + `°` formatter |
| `StarDetection/Optimization/Review/FrameReviewVM.cs` | toggles, brushes, marker fields, hover (`OptimizationCurve`), legend `Enabled` |
| `StarDetection/Optimization/Review/FrameReviewControl.xaml(.cs)` | header, raw-coord overlays, plus/line, hover overlay, toggles, greyed legend, hover hit-test |
| `StarDetection/Optimization/DataTemplates.xaml` | larger default size on the `FrameReviewVM` template |
| `StarDetection/Optimization/Review/StarReviewVM.cs` | add `StarReviewLegendEntry.Enabled` (additive) |
| `Tests/Inspection/FrameReviewSnapshotBuilderTests.cs` | update 3, add ~9 tests |

## Sequencing

1. Step 1 (SensorModel/star) → build.
2. Step 2 (snapshot + tests) → **green before any UI**.
3. Step 3 (VM) → build.
4. Step 4 (XAML/code-behind) → full build + suite.

## Verification

- **Unit:** `dotnet.exe test … --filter FrameReviewSnapshotBuilderTests` green, then full suite (expect ~1410+ pass, 0 fail).
- **Manual in NINA:** enable Sensor Curve Model + "Keep frames for Review"; run a Detailed Analysis; open Review Frames and confirm:
  - Boxes sit *on* the stars on a RANSAC-aligned non-reference frame (raw-coord fix); box colors = green (fit) / red (registered, no fit) / grey (unregistered).
  - "Show registration" off by default; toggling on shows reg-id labels + cyan "+" at star centers + orange line/"+" to the aligned target. Reference frame: no lines.
  - Header above the image shows detected count + transform with a "°" symbol; no in-image HUD.
  - Hovering a green (matched-with-fit) star pops the corner focus-graph overlay with the hyperbola + points + R² + optimal focus; moving off / leaving clears it.
  - "Show optimal-focus offset" off by default; on → violet "+N/−N" labels per fitted star.
  - Legend rows for disabled toggles are greyed; window opens large and resizes.
  - Toggle RANSAC off + rerun → reg-id still computed, no lines/target; everything else intact.

## Risks

- **`FitImages` array identity** (Step 1) — verify the mutated `registeredStars` is the one surfaced in `SensorModelResult.RegisteredStars`.
- **OxyPlot template scope** in the separate UserControl — fall back to `MergedDictionaries` if the implicit `OptimizationCurve` template doesn't resolve.
- **Legend greying** needs `LegendEntries` to rebuild on toggle (no longer init-only).
- **Reference / non-RANSAC frames** (`Position == OriginalPosition`) must still render boxes with no registration line — existing builder tests guard this; keep them green.
- **`°` test** — update the equality assertion to the new formatter, not `ToFullString`.
- **Hover scatter points** reconstructed from `MatchedStars` (pre-regularization) — the fitted curve is exact; dot positions are diagnostic-acceptable.
