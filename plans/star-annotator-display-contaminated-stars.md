# Star Annotator: Display Contaminated Stars

## Context

The star detector already cross-checks each star's background estimates (annulus median, per-pixel
threshold, and PSF-fitted background) and flags a star as *contamination-suspected* when the estimates
disagree by more than 2× the noise sigma (`StarDetector.CheckBackgroundContamination`, sets
`Star.StarContaminationSuspected`). Contaminated stars are **kept** in the output star list and are only
surfaced today as an aggregate count (`StarDetectorMetrics.ContaminationSuspected`). There is no way for a
user to *see which* stars were flagged.

This change adds a star-annotator option to draw a marker around each contaminated star, so users can
visually identify problem stars (neighbors, gradients, hot columns) on the annotated image.

Decisions (confirmed with user):
- **Marker shape**: reuse the existing `StarBoundsType` setting (Box / Ellipse / PSF) — contaminated
  markers use the same shape as normal star bounds, just in their own color.
- **Scope**: always highlight *every* contaminated star, independent of the "Show All Stars" / "Max Stars"
  limit (separate pass over the full star list).
- **Default color**: semi-transparent magenta `Color.FromArgb(128, 255, 0, 255)`; default off.

## Key finding / gap to close

`HocusFocusStarDetection.ToDetectedStar()` (HocusFocusStarDetection.cs:422) does **not** copy
`StarContaminationSuspected` from the internal `Star` onto the `HocusFocusDetectedStar` that reaches the
annotator. The flag is currently lost during conversion and must be wired through.

## Changes

### 1. Carry the contamination flag to the annotator
**File:** `StarDetection/HocusFocusStarDetection.cs`
- Add `public bool StarContaminationSuspected { get; set; }` to `HocusFocusDetectedStar` (class at line 196)
  and include it in its `ToString()`.
- In `ToDetectedStar(Star star)` (line 422), copy it:
  `StarContaminationSuspected = star.StarContaminationSuspected`.

### 2. New annotator options (interface + implementation)
**Files:** `Interfaces/IStarAnnotatorOptions.cs`, `StarDetection/StarAnnotatorOptions.cs`
Follow the existing `ShowX` + `XColor` pattern used by the rejection categories
(e.g. `ShowTooDistorted` / `TooDistortedColor`).
- Interface: add `bool ShowContaminated { get; set; }` and `Color ContaminatedColor { get; set; }`.
- Implementation: add backing fields + properties (persist via
  `optionsAccessor.SetValueBoolean("ShowContaminated", …)` and `SetValueColor("ContaminatedColor", …)`).
- `InitializeOptions()`: `showContaminated = optionsAccessor.GetValueBoolean("ShowContaminated", false);`
  and `contaminatedColor = optionsAccessor.GetValueColor("ContaminatedColor", Color.FromArgb(128, 255, 0, 255));`
- `ResetDefaults()`: `ShowContaminated = false;` and `ContaminatedColor = Color.FromArgb(128, 255, 0, 255);`

### 3. Render contaminated-star markers
**File:** `StarDetection/HocusFocusStarAnnotator.cs` (`GenerateAnnotatedImage`)
- **Extract a private helper** from the existing star-bounds drawing block (lines 130–150) to avoid
  duplication, e.g.:
  `private static void DrawStarBounds(Graphics g, DetectedStar star, HocusFocusDetectedStar hfStar, PSFModel psf, StarBoundsTypeEnum boundsType, Pen pen)`
  covering the Box / PSF (rotated FWHM ellipse) / Ellipse cases. Refactor the existing
  `if (StarAnnotatorOptions.ShowStarBounds)` block to call it, then reuse it for contamination.
- Add a **separate pass** (placed alongside the rejection-category blocks, ~line 195) that iterates the
  **full** `result.StarList` (not the Max-Stars-filtered local `starList`):
  ```
  if (StarAnnotatorOptions.ShowContaminated) {
      using (var brush = new SolidBrush(StarAnnotatorOptions.ContaminatedColor.ToDrawingColor()))
      using (var pen = new Pen(brush)) {
          foreach (var star in result.StarList) {
              var hfStar = star as HocusFocusDetectedStar;
              if (hfStar?.StarContaminationSuspected != true) continue;
              DrawStarBounds(graphics, star, hfStar, hfStar.PSF, StarAnnotatorOptions.StarBoundsType, pen);
          }
      }
  }
  ```
  This is independent of the `ShowAllStars` / `MaxStars` filtering, satisfying "always show all".

### 4. Options UI
**File:** `Resources/OptionsDataTemplates.xaml`
- Add two tooltip resources near the other annotator tooltips (~lines 447–472):
  `ShowContaminated_Tooltip` ("Whether to highlight stars flagged as possibly contaminated by a neighbor,
  background gradient, or hot column") and `ContaminatedColor_Tooltip` ("The color used to mark
  contaminated stars").
- In the `HocusFocus_StarAnnotator_Options` template (starts ~line 1682), append two new grid rows after
  the last existing category: a `TextBlock` + `CheckBox` bound to `ShowContaminated`, and a `TextBlock` +
  `xceed:ColorPicker` bound to `ContaminatedColor` (color row gated on `ShowContaminated` via
  `BooleanToVisibilityCollapsedConverter`), mirroring the "Show Distorted" / "Distorted Box Color" pair at
  lines ~1984–2015. Add two `<RowDefinition />` entries to the grid's `RowDefinitions` and use the next
  free `Grid.Row` indices.

No `StarDetectorMetrics` field is added, so the metrics-panel requirement does not apply. The new
annotator options do require UI (added above) per the project's Options-UI rule.

## Verification

1. **Build**: `rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (0 errors).
2. **Tests**: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"` — all pass.
   - Optionally add a unit test asserting `ToDetectedStar` copies `StarContaminationSuspected` (the
     test project links source files directly; see existing `StarDetection` tests).
3. **Manual (in NINA)**: open Star Annotation Options → enable "Show Contaminated", pick the magenta
   default, run star detection on an image known to contain blended/contaminated stars, and confirm
   magenta markers appear around the flagged stars, using the shape selected in "Star Bounds Type", and
   that they remain visible when "Show All Stars" is off with a small "Max Stars".
