# Tilt Calibration Accuracy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the §7 recommendations of `docs/tilt-calibration-pitch-nonlinearity-design.md`: make the tilt wizard's calibration metrics honest (physical-space math, drift-cancelling deltas, corner-AF cross-check, piston-implied pitch) and fix the 4-corner plane's lever-arm bug, so unequal-screw warnings and pitch readouts reflect the hardware instead of estimator artifacts.

**Architecture:** All calibration math stays centralized in `TiltCalibrationCalculator` + `TiltScrewGeometry` (pure, shared by wizard VM and TestApp). The wizard VM gains a second per-step reading (the corner-region plane the inspector already computes) and new display rows. `TiltPlaneModel.Create` learns its actual sample positions. Metadata schema goes 2→3 with additive fields only.

**Tech Stack:** C# / .NET 8 (WPF plugin), NUnit 4.4 (`dotnet.exe test` via WSL interop), Newtonsoft JSON metadata, Accord OLS (existing).

**Read first (execution session):** `docs/tilt-calibration-pitch-nonlinearity-design.md` (the spec), `.claude/docs/mvvm-patterns.md` (VM work), `.claude/docs/options-system.md` (Task 6 option), `.claude/docs/documentation-style.md` (Task 8).

**Locked decisions (from the investigation, do not relitigate mid-execution):**
- The corner cross-check **flags and displays** disagreement; it does NOT silently replace the paraboloid-measured pitch. Threshold: relative per-move magnitude difference > 0.15.
- Piston-vs-measured pitch warning threshold: 0.20 (both are focuser-frame, so honest agreement is expected).
- Symmetric screw-1 delta (`Screw1 − mid(ReBaseline1, ReBaseline2)`) becomes the default unconditionally (it cancels linear drift and needs no new data). Screw 2 gets symmetry only when the new optional measured final re-baseline ran.
- New enum member `ReBaseline3 = 7` (AFTER `Complete = 6` numerically — never renumber `Complete`); its step folder is special-cased to `07_ReBaseline3`.
- `TiltCalibrationMetadata.CurrentSchemaVersion` 2→3, all new fields additive with NaN defaults; `Validate()` untouched (verified: it checks no step names or versions).
- Test cadence per user preference: run the focused test project filter after each task, full suite at Task 10 (final gate).

**Branch:** all work on `ghilios/tilt-calibration-accuracy` off `develop`. Never push `develop`. Commits use:
```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>"
```
Test command template (WSL → Windows dotnet; ~40-60 s per run, timeout 600000):
```bash
dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~<FixtureName>"
```

---

### Task 0: Branch setup

**Files:** none (git only)

- [x] **Step 0.1:** `git checkout develop && git pull && git checkout -b ghilios/tilt-calibration-accuracy`
- [x] **Step 0.2:** Confirm clean: `git status` → "nothing to commit".

---

### Task 1: Fix `TiltPlaneModel.Create` corner lever arms (×2/3 attenuation)

The `Create(AutoFocusResult, …)` overload feeds corner-REGION AF vertices (region centers at normalized ±1/3 with default `SensorROI=CornersROI=1.0`) into an OLS whose design points are hard-coded to (±0.5, ±0.5) (`TiltModel.cs:145-151`). Result: A/B attenuated by 2/3, which propagates to the inspector's per-corner "adjustment required" values and TestApp's region-path pitch. The paraboloid path (`SensorModelAberrationResult.CreateTiltPlaneModel`) evaluates the surface AT the true corners and passes those values to the 8-arg overload — for it, ±0.5 is correct and must not change.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/TiltModel.cs:113-165`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/TiltPlaneModelTests.cs`

- [x] **Step 1.1: Read the existing test file and region types** to confirm construction patterns before writing the test:
  - `Joko.NINA.Plugins.HocusFocus.Tests/AutoFocus/TiltPlaneModelTests.cs` (how tests build `TiltPlaneModel` today)
  - The `StarDetectionRegion` + `RatioRect` types (grep: `grep -rn "class StarDetectionRegion\|class RatioRect" Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/ --include="*.cs"`), specifically the constructor and the `OuterBoundary.StartX/StartY/Width/Height` property names (these are the names serialized into the region reports, so they will match).
  Adapt the ctor calls in Step 1.2's test to what you find; the assertions and values stay as written.

- [x] **Step 1.2: Write the failing test** — a synthetic plane sampled at the standard corner-region centers must round-trip A/B exactly:

```csharp
[Test]
public void Create_FromAutoFocusResult_UsesActualCornerRegionCenters() {
    // Plane z = C + A*xn + B*yn over normalized [-0.5, 0.5]; corner regions centered at ±1/3.
    const double A = 300.0, B = -120.0, C = 11200.0;
    double At(double xn, double yn) => C + A * xn + B * yn;

    var imageSize = new System.Drawing.Size(9576, 6388);
    var result = new AutoFocusResult() {
        Succeeded = true,
        ImageSize = imageSize,
        RegionResults = new[] {
            RegionResult(0, new RatioRect(0.0, 0.0, 1.0, 1.0), At(0, 0)),
            RegionResult(1, new RatioRect(1/3d, 1/3d, 1/3d, 1/3d), At(0, 0)),
            RegionResult(2, new RatioRect(0.0, 0.0, 1/3d, 1/3d), At(-1/3d, -1/3d)),   // TL
            RegionResult(3, new RatioRect(2/3d, 0.0, 1/3d, 1/3d), At(+1/3d, -1/3d)),  // TR
            RegionResult(4, new RatioRect(0.0, 2/3d, 1/3d, 1/3d), At(-1/3d, +1/3d)),  // BL
            RegionResult(5, new RatioRect(2/3d, 2/3d, 1/3d, 1/3d), At(+1/3d, +1/3d)), // BR
        }
    };

    var model = TiltPlaneModel.Create(result, fRatio: 6.3, focuserStepSizeMicrons: 0.269);

    Assert.Multiple(() => {
        Assert.That(model.A, Is.EqualTo(A).Within(1e-9));  // old code returns 200.0 (A * 2/3)
        Assert.That(model.B, Is.EqualTo(B).Within(1e-9));
        Assert.That(model.C, Is.EqualTo(C).Within(1e-9));
    });
}

private static AutoFocusRegionResult RegionResult(int index, RatioRect boundary, double focusPosition) {
    return new AutoFocusRegionResult() {
        RegionIndex = index,
        Region = new StarDetectionRegion(boundary),   // adapt to actual ctor found in Step 1.1
        EstimatedFinalFocuserPosition = focusPosition,
        EstimatedFinalHFR = 2.0,
        Fittings = new AutoFocusFitting()             // GetRSquared() must not throw on defaults; verify in Step 1.1
    };
}
```

- [x] **Step 1.3: Run it, verify it fails with A = 200** (the 2/3 attenuation):
  `dotnet.exe test ... --filter "FullyQualifiedName~TiltPlaneModelTests"` → new test FAILS, expected 300 actual 200.

- [x] **Step 1.4: Implement.** In `TiltModel.cs`, generalize the 8-arg overload with corner design-point parameters (defaults preserve every existing caller), and make the `AutoFocusResult` overload pass the actual region centers:

```csharp
public static TiltPlaneModel Create(AutoFocusResult result, double fRatio, double focuserStepSizeMicrons) {
    var centerFocuser = result.RegionResults[1].EstimatedFinalFocuserPosition;
    var topLeftFocuser = result.RegionResults[2].EstimatedFinalFocuserPosition;
    var topRightFocuser = result.RegionResults[3].EstimatedFinalFocuserPosition;
    var bottomLeftFocuser = result.RegionResults[4].EstimatedFinalFocuserPosition;
    var bottomRightFocuser = result.RegionResults[5].EstimatedFinalFocuserPosition;
    // The corner-region samples sit at the REGION CENTERS (±1/3 normalized with default ROI), not at the
    // frame corners. Regress against where the samples actually are, or A/B are attenuated by 2·|center|.
    var tlBoundary = result.RegionResults[2].Region.OuterBoundary;
    double cornerXNorm = Math.Abs(tlBoundary.StartX + tlBoundary.Width / 2.0 - 0.5);
    double cornerYNorm = Math.Abs(tlBoundary.StartY + tlBoundary.Height / 2.0 - 0.5);
    var tiltPlaneModel = Create(
        imageSize: result.ImageSize, fRatio: fRatio,
        focuserStepSizeMicrons: focuserStepSizeMicrons, centerFocuser: centerFocuser, topLeftFocuser: topLeftFocuser,
        topRightFocuser: topRightFocuser, bottomLeftFocuser: bottomLeftFocuser, bottomRightFocuser: bottomRightFocuser,
        cornerXNorm: cornerXNorm, cornerYNorm: cornerYNorm);
    // ... RSquared assignments unchanged ...
    return tiltPlaneModel;
}

public static TiltPlaneModel Create(
    System.Drawing.Size imageSize, double fRatio, double focuserStepSizeMicrons,
    double centerFocuser, double topLeftFocuser, double topRightFocuser,
    double bottomLeftFocuser, double bottomRightFocuser,
    double cornerXNorm = 0.5, double cornerYNorm = 0.5) {
    var ols = new OrdinaryLeastSquares() { UseIntercept = true };
    double[][] inputs = {
        new double[] { -cornerXNorm, -cornerYNorm },
        new double[] {  cornerXNorm, -cornerYNorm },
        new double[] { -cornerXNorm,  cornerYNorm },
        new double[] {  cornerXNorm,  cornerYNorm },
    };
    // ... rest unchanged ...
}
```

- [x] **Step 1.5: Run the fixture again** → PASS; also run `--filter "FullyQualifiedName~TiltModelTests|FullyQualifiedName~TiltPlaneModelTests"` → all green (existing 8-arg-overload tests are unaffected by the defaulted params).
- [x] **Step 1.6:** Note in the commit body that inspector per-corner "adjustment required" values grow ×1.5 with default ROI (they were understated).
- [x] **Step 1.7: Commit** — `fix(tilt): regress 4-corner plane against actual region centers, not frame corners`

---

### Task 2: Physical-gradient-space calibration metrics (F2 fix)

`ComputeScrewAngles`, `MoveMagnitudeRatio`, and `ComputeConfidence` currently work in anisotropic (A,B) space; on a 3:2 sensor two equal physical moves 90° apart read as a ~75° gap and a 1.35× magnitude ratio. Pitch math (`RecoverHardwareDetailed`) is already physical and stays untouched.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationCalculator.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltCalibrationCalculatorTests.cs`

- [x] **Step 2.1: Write the failing test** (values precomputed; f=0.5, 6000×4000 px, pixel 3.76 → two moves of EQUAL physical magnitude at physical directions 200° and 290°):

```csharp
[Test]
public void Calibrate_AnisotropicSensor_ReadsPhysicalGapAndEqualMagnitudes() {
    // On a 3:2 sensor, equal-magnitude physical moves at 200° and 290° produce these (A,B) deltas.
    // In raw (A,B) space they would read: gap 75.01°, magnitude ratio 1.354 — the F2 bug.
    var inputs = new TiltCalibrationInputs {
        ScrewCount = 4,
        ReBaseline1 = new TiltGradient(0, 0, 0),
        Screw1 = new TiltGradient(-77.1597, 141.3298, 0),
        ReBaseline2 = new TiltGradient(0, 0, 0),
        Screw2 = new TiltGradient(-211.9947, -51.4398, 0),
        ImageWidthPixels = 6000, ImageHeightPixels = 4000,
        PixelSizeMicrons = 3.76, FocuserStepMicrons = 0.5,
        ScrewRadiusMillimeters = 44.0, CalibrationAppliedAmount = 1.0,
        IsStepperAdjustment = false, HasCurvatureMeasurement = false, FallbackCurvatureSign = 1,
    };
    var result = TiltCalibrationCalculator.Calibrate(inputs);
    Assert.Multiple(() => {
        Assert.That(result.RawAngleDiffDegrees, Is.EqualTo(90.0).Within(0.01));
        Assert.That(result.MoveMagnitudeRatio, Is.EqualTo(1.0).Within(0.001));
        Assert.That(result.Screw1DirectionDegrees, Is.EqualTo(200.0).Within(0.01));
        Assert.That(result.Screw2DirectionDegrees, Is.EqualTo(290.0).Within(0.01));
        Assert.That(result.Screw1AngleDegrees, Is.EqualTo(200.0).Within(0.01));
    });
}
```

- [x] **Step 2.2: Run** `--filter "FullyQualifiedName~TiltCalibrationCalculatorTests"` → new test FAILS (rawDiff ≈ 255.0 → folded 105.0... assert reports 255.0 vs 90.0; ratio 1.354). Existing tests still pass (they use a square sensor where (A,B) space is isotropic).

- [x] **Step 2.3: Implement.** In `TiltCalibrationCalculator`:

```csharp
/// <summary>Screw-move delta in physical gradient space (µm focus travel per µm of sensor
/// displacement). Angles and magnitude ratios MUST be computed here, not in (A,B) space —
/// A and B are per-normalized-coordinate and distort directions on non-square sensors.</summary>
internal static (double gx, double gy) PhysicalDelta(TiltGradient to, TiltGradient from, TiltCalibrationInputs inputs) {
    double sensorW = inputs.ImageWidthPixels * inputs.PixelSizeMicrons;
    double sensorH = inputs.ImageHeightPixels * inputs.PixelSizeMicrons;
    return TiltScrewGeometry.PlaneGradientToPhysical(to.A - from.A, to.B - from.B,
        inputs.FocuserStepMicrons, sensorW, sensorH);
}
```

  - `ComputeScrewAngles(double g1x, double g1y, double g2x, double g2y, int screwCount)` — rename parameters only; body unchanged (`atan2(g1x, -g1y)` etc.).
  - `MoveMagnitudeRatio(double g1x, double g1y, double g2x, double g2y)` — rename parameters; body unchanged.
  - `ComputeConfidence(TiltCalibrationInputs inputs)` — every `Magnitude(a-b, ...)` over raw A/B becomes a magnitude of `PhysicalDelta(...)`. Signal/noise stay dimensionless ratios; `SignalToNoise` and `PredictedAngleUncertaintyDeg` semantics unchanged.
  - `Calibrate` — compute `var (d1x, d1y) = PhysicalDelta(inputs.Screw1, inputs.ReBaseline1, inputs);` (Task 3 will change the reference point) and same for screw 2; feed those to `ComputeScrewAngles`, `MoveMagnitudeRatio`, and the two `Screw*DirectionDegrees`.
  - `ScrewMoveSignal`/`NoiseEstimate` in `TiltCalibrationConfidence`: update the doc comments to say "physical gradient units (µm/µm)" — displayed only via the dimensionless SNR.

- [x] **Step 2.4: Run the calculator fixture** → all PASS. The existing square-sensor tests must pass without edits except signature-site updates if any test calls `ComputeScrewAngles`/`MoveMagnitudeRatio` directly with (A,B) values — convert those call sites with `a * FStep / SensorW` (constants already exist at the fixture top). `ComputeConfidence_RealAstrodet6Run_IsNoiseDominated` pins SNR 0.81/50.9° from literal TiltGradients: SNR is scale-invariant but NOT aniso-invariant — recompute the pinned values by running the test, and update the literals with a comment `// physical-space values (F2 fix)`.
- [x] **Step 2.5:** In `TiltAdapterWizardVM.RunAveragedMeasurement` (:2374) and `ReplayAsync` (:3080), the per-state `DirectionDeg` is `NormalizeAngle(Math.Atan2(avgA, -avgB) * 180.0 / Math.PI)` — change both to physical: reuse the gx/gy conversion already present in `ComputeTiltAngleDeg` (extract a small private helper `(double gx, double gy) StateGradient(double a, double b, TiltPlaneModel model)` from its body, then `DirectionDeg = NormalizeAngle(Math.Atan2(gx, -gy) * 180.0 / Math.PI)`).
- [x] **Step 2.6: Run** `--filter "FullyQualifiedName~TiltAdapterWizard"` → green.
- [x] **Step 2.7: Commit** — `fix(tilt): compute calibration angles, ratios, and SNR in physical gradient space (F2)`

---

### Task 3: Drift-cancelling symmetric screw deltas

`Screw1 − mid(ReBaseline1, ReBaseline2)` cancels a linear tilt drift exactly (readings bracket the move symmetrically); this needs no new measurements. Screw 2 gains the same property in Task 6 when the measured final re-baseline is present.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationCalculator.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltCalibrationCalculatorTests.cs`

- [x] **Step 3.1: Write the failing test:**

```csharp
[Test]
public void Calibrate_LinearTiltDrift_CancelsExactlyForScrew1() {
    // A constant drift vector v is added per measurement interval. The screw-1 move D is bracketed
    // by ReBaseline1 (2 intervals in) and ReBaseline2 (4 intervals in), with Screw1 at 3 intervals:
    // Screw1 − mid(RB1, RB2) recovers D exactly. The old delta (Screw1 − RB1) is off by |v|.
    var (vA, vB) = (9.0, -5.0);
    var move1 = SingleScrewReading(0, 400.0, 4);      // D
    var move2 = SingleScrewReading(90, 400.0, 4);     // E
    TiltGradient Drift(TiltGradient g, int k) => new TiltGradient(g.A + k * vA, g.B + k * vB, g.MeanFocuserPosition);

    var inputs = new TiltCalibrationInputs {
        ScrewCount = 4,
        Baseline = Drift(new TiltGradient(0, 0, 0), 0),
        AllInward = Drift(new TiltGradient(0, 0, 100), 1),
        ReBaseline1 = Drift(new TiltGradient(0, 0, 0), 2),
        Screw1 = Drift(move1, 3),
        ReBaseline2 = Drift(new TiltGradient(0, 0, 0), 4),
        Screw2 = Drift(move2, 5),
        ImageWidthPixels = ImgW, ImageHeightPixels = ImgH,
        PixelSizeMicrons = PixelSize, FocuserStepMicrons = FStep,
        ScrewRadiusMillimeters = RadiusMm, CalibrationAppliedAmount = 1.0,
        IsStepperAdjustment = false,
    };
    var clean = TiltCalibrationCalculator.Calibrate(inputs with-no-drift-equivalent);  // build a second inputs without Drift()
    var drifted = TiltCalibrationCalculator.Calibrate(inputs);
    // screw-1 recovery is drift-immune; screw-2 (no final re-baseline yet) is allowed to differ.
    Assert.That(drifted.Screw1DirectionDegrees, Is.EqualTo(clean.Screw1DirectionDegrees).Within(1e-9));
}
```
  (Build the "clean" inputs by calling the same initializer with `Drift(g, 0)` — write it out; `TiltCalibrationInputs` is a class, no `with` expression.)

- [x] **Step 3.2: Run** → FAILS (old delta uses RB1 only; drift shifts direction).
- [x] **Step 3.3: Implement.** Central delta helpers used by `Calibrate`, `RecoverHardwareDetailed`, and `ComputeConfidence` (replacing their four inline `Screw1.A - ReBaseline1.A` computations):

```csharp
/// <summary>Screw-1 move referenced to the MIDPOINT of the re-baselines that bracket it
/// (c and e). For a linear tilt drift the midpoint is exactly the drift-free reference, so the
/// recovered move is drift-immune. Works identically in the 4-step flow (Baseline sits in the
/// ReBaseline1 slot).</summary>
internal static (double dA, double dB) Screw1Delta(TiltCalibrationInputs inputs) {
    double midA = (inputs.ReBaseline1.A + inputs.ReBaseline2.A) / 2.0;
    double midB = (inputs.ReBaseline1.B + inputs.ReBaseline2.B) / 2.0;
    return (inputs.Screw1.A - midA, inputs.Screw1.B - midB);
}

/// <summary>Screw-2 move. Symmetric (drift-immune) only when the optional measured final
/// re-baseline ran; otherwise referenced to ReBaseline2 as before.</summary>
internal static (double dA, double dB) Screw2Delta(TiltCalibrationInputs inputs) {
    if (inputs.HasFinalRebaseline) {
        double midA = (inputs.ReBaseline2.A + inputs.ReBaseline3.A) / 2.0;
        double midB = (inputs.ReBaseline2.B + inputs.ReBaseline3.B) / 2.0;
        return (inputs.Screw2.A - midA, inputs.Screw2.B - midB);
    }
    return (inputs.Screw2.A - inputs.ReBaseline2.A, inputs.Screw2.B - inputs.ReBaseline2.B);
}
```
  Add to `TiltCalibrationInputs`: `public TiltGradient ReBaseline3 { get; set; }` and `public bool HasFinalRebaseline { get; set; }` (default false — Task 6 wires it). `PhysicalDelta` from Task 2 gains an overload taking `(dA, dB)` directly.

- [x] **Step 3.4:** Update `Calibrate_DerivesScrewDeltasFromReBaselineNotBaseline` (:235) — its intent ("not Baseline") still holds; its expected values shift to the midpoint reference. Recompute expectations from the helper semantics (the test constructs known moves; the midpoint of two identical re-baselines equals the old reference, so prefer constructing RB1 == RB2 there to keep it exact and obviously correct).
- [x] **Step 3.5: Run** the calculator fixture → all PASS.
- [x] **Step 3.6: Commit** — `feat(tilt): drift-cancelling symmetric screw-move deltas`

---

### Task 4: Piston-implied pitch (frame-factor probe)

The AllInward piston contains a free, tilt-fit-independent, focuser-frame pitch estimate. Drift-corrected using the Baseline/ReBaseline1 means that bracket it. On the analyzed run: 2.2333 µm/step vs tilt-derived 1.864 — a >20% gap that would have exposed the estimator problem immediately.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationCalculator.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationMetadata.cs` (schema 2→3)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml` (~:1580 UniformGrid)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltCalibrationCalculatorTests.cs`

- [x] **Step 4.1: Write the failing tests** (fixture numbers are the real ghilios_corrected means):

```csharp
[Test]
public void PistonImpliedMicronsPerStep_DriftCorrectedFromBracketingBaselines() {
    var inputs = new TiltCalibrationInputs {
        ScrewCount = 4, HasCurvatureMeasurement = true,
        Baseline = new TiltGradient(0, 0, 11283.868653107538),
        AllInward = new TiltGradient(0, 0, 9995.476157148303),
        ReBaseline1 = new TiltGradient(0, 0, 11197.706101225354),
        FocuserStepMicrons = 0.269, CalibrationAppliedAmount = 150.0,
        ImageWidthPixels = 9576, ImageHeightPixels = 6388, PixelSizeMicrons = 3.76,
        ScrewRadiusMillimeters = 55.0,
    };
    Assert.That(TiltCalibrationCalculator.PistonImpliedMicronsPerStep(inputs),
        Is.EqualTo(2.233258).Within(1e-5));
}

[Test]
public void PistonImpliedMicronsPerStep_NaNWithoutCurvatureSteps() {
    var inputs = new TiltCalibrationInputs { HasCurvatureMeasurement = false,
        FocuserStepMicrons = 0.269, CalibrationAppliedAmount = 150.0 };
    Assert.That(TiltCalibrationCalculator.PistonImpliedMicronsPerStep(inputs), Is.NaN);
}
```

- [x] **Step 4.2: Run** → FAIL (method missing).
- [x] **Step 4.3: Implement:**

```csharp
/// <summary>
/// Focuser-frame µm-per-unit implied by the AllInward piston: all screws moved by the applied
/// amount, so mean best-focus shifts by (applied × unit) / frame-factor. Drift-corrected by
/// interpolating the baseline mean to AllInward's time as mid(Baseline, ReBaseline1) — the two
/// nominally-identical states that bracket it. This is measured in the SAME (focuser) frame as
/// the tilt-derived hardware, so honest values agree; a large gap means the tilt estimator (or
/// the mechanics on pull-side moves) is off. NaN for 4-step runs (no piston measured).
/// </summary>
public static double PistonImpliedMicronsPerStep(TiltCalibrationInputs inputs) {
    if (!inputs.HasCurvatureMeasurement || inputs.CalibrationAppliedAmount <= 0 || inputs.FocuserStepMicrons <= 0) {
        return double.NaN;
    }
    double baselineAtAllInward = (inputs.Baseline.MeanFocuserPosition + inputs.ReBaseline1.MeanFocuserPosition) / 2.0;
    double deltaSteps = inputs.AllInward.MeanFocuserPosition - baselineAtAllInward;
    return Math.Abs(deltaSteps) * inputs.FocuserStepMicrons / inputs.CalibrationAppliedAmount;
}
```
  Wire into `Calibrate`: new result field `public double PistonImpliedMicronsPerStep { get; set; }`.

- [x] **Step 4.4: Metadata:** `CurrentSchemaVersion = 3`; `TiltCalibrationResultRecord` gains `public double PistonImpliedMicronsPerStep { get; set; } = double.NaN;` — populated in `TiltAdapterWizardVM.FinalizeMetadata()` (:3243). Confirm `TiltCalibrationMetadataTests` round-trip test covers new fields (add the field to its fixture object).
- [x] **Step 4.5: VM display + warning.** In WizardVM: store the value in a new field `pistonImpliedMicronsPerStep` when `RunCalibrationMath` completes; add:

```csharp
public string PistonPitchDisplay =>
    double.IsNaN(pistonImpliedMicronsPerStep) ? string.Empty
    : $"Piston-implied: {pistonImpliedMicronsPerStep:0.###} µm/{(IsStepperAdjustment ? "step" : "turn")}";
```
  Raise it inside `RaiseHardwareSummaryChanged()` (:2753). Extend `ValidateCalibrationQuality` with a piston check: when both values are positive and `Math.Abs(piston - measured) / measured > 0.20`, append part:
  `$"piston-implied hardware ({piston:0.##} µm) and tilt-derived ({measured:0.##} µm) disagree by more than 20% — the tilt estimate may be unreliable"`.
  (Extend the method signature with `double measuredHardware, double pistonImplied`; both callers are in `RunCalibrationMath`.)
- [x] **Step 4.6: XAML.** In `DataTemplates.xaml` "Measured Adapter Hardware" `UniformGrid Columns="2"` (~:1580), after the `SavedHardwareDisplay` row add:
```xml
<TextBlock Text="{Binding PistonPitchDisplay}" Margin="0,2,8,0"
           Visibility="{Binding PistonPitchDisplay, Converter={StaticResource StringToVisibilityConverter}}" />
```
  (Use whatever empty-string-collapse pattern the sibling rows use — check :1583-1588; if they rely on empty TextBlocks rather than a converter, do the same and skip the Visibility binding.)
- [x] **Step 4.7: Run** calculator + wizard filters → green. **Commit** — `feat(tilt): piston-implied pitch estimate + disagreement warning`

---

### Task 5: Corner-region cross-check in the wizard

The inspector already computes the 4-corner region plane every wizard AF (`inspector.TiltModel.TiltPlaneModel`, honest after Task 1). Capture it per step alongside the paraboloid reading, run the same calibration math on it, and flag when the two estimators' move magnitudes disagree by >15%. On the analyzed run this reads pitch ≈ 2.0 µm/step vs paraboloid 1.86 and ratio 1.14 vs 1.52.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationMetadata.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs`

- [x] **Step 5.1: Extend `StepReading`** (WizardVM:246-255) with `public double CornerA; public double CornerB; public double CornerMean;` (NaN when unavailable). Extend `SeedStepReading` (:2850) with optional `double cornerA = double.NaN, double cornerB = double.NaN, double cornerMean = double.NaN`.
- [x] **Step 5.2: Capture.** In `RunAveragedMeasurement` (:2306), next to the `CalibrationTiltPlane` read (:2354), read `var corner = cornerTiltPlaneOverrideForTest ?? inspector.TiltModel?.TiltPlaneModel;` and accumulate `(corner.A, corner.B, corner.MeanFocuserPosition)` into a parallel list; average like the paraboloid readings. Add the test seam next to `CalibrationTiltPlaneOverrideForTest` (:2286-2301):
```csharp
private TiltPlaneModel cornerTiltPlaneOverrideForTest;
internal TiltPlaneModel CornerTiltPlaneOverrideForTest {
    get => cornerTiltPlaneOverrideForTest;
    set => cornerTiltPlaneOverrideForTest = value;
}
```
  Do the same capture in `ReplayAsync` (:3070-3084).
- [x] **Step 5.3: Metadata.** `TiltPerStepResult` gains `public double CornerTiltPlaneA { get; set; } = double.NaN;`, `CornerTiltPlaneB`, `CornerMeanFocuserPosition` (NaN defaults). `RecordStepIntoMetadata` (:3216) fills them. `TiltCalibrationResultRecord` gains `public double CornerMeasuredHardwareMicrons { get; set; } = double.NaN;` and `public double EstimatorRelativeDifference { get; set; } = double.NaN;`.
- [x] **Step 5.4: Cross-check math.** In `RunCalibrationMath` (:2631), after the paraboloid `Calibrate` call: when every reading in `activeMeasurementSteps` has non-NaN corner values, build a second `TiltCalibrationInputs` from the corner readings (identical geometry fields) and run `TiltCalibrationCalculator.Calibrate`. Compute, using the Task-2/3 helpers:

```csharp
var (p1x, p1y) = TiltCalibrationCalculator.PhysicalDelta(TiltCalibrationCalculator.Screw1Delta(inputs), inputs);
// ... p2, c1, c2 likewise for paraboloid (p) and corner (c) inputs ...
double rel1 = Math.Abs(Mag(p1x, p1y) - Mag(c1x, c1y)) / Mag(c1x, c1y);
double rel2 = Math.Abs(Mag(p2x, p2y) - Mag(c2x, c2y)) / Mag(c2x, c2y);
double estimatorRelDiff = Math.Max(rel1, rel2);
```
  Store `cornerCalibration.MeasuredHardwareMicrons` + `estimatorRelDiff` in fields + metadata. Extend `ValidateCalibrationQuality` with: when `estimatorRelDiff > 0.15`, append part
  `$"the per-star model and the corner-region AF disagree on the screw moves by {estimatorRelDiff:P0} — the measured hardware may be unreliable (corner-AF estimate: {cornerMeasuredHardwareMicrons:0.###} µm)"`.
- [x] **Step 5.5: Display.** New VM property, raised in `RaiseHardwareSummaryChanged`:
```csharp
public string CornerCrossCheckDisplay =>
    double.IsNaN(cornerMeasuredHardwareMicrons) ? string.Empty
    : $"Corner-AF cross-check: {cornerMeasuredHardwareMicrons:0.###} µm/{(IsStepperAdjustment ? "step" : "turn")}";
```
  XAML row next to Task 4's, same pattern.
- [x] **Step 5.6: Write VM tests** in `TiltAdapterWizardVMTests` (follow existing patterns found there; use `SeedStepReading` + `RunCalibrationMath` via whatever internal invocation the existing calibration tests use — read the test file first). Two cases: (a) corner readings agreeing within 15% → no new warning part; (b) corner magnitudes 25% higher → warning contains "corner-region AF disagree".
- [x] **Step 5.7: Run** wizard + metadata filters → green. **Commit** — `feat(tilt): corner-region AF cross-check of the paraboloid calibration`

---

### Task 6: Optional measured final re-baseline (ReBaseline3)

Gives screw 2 the same drift-cancelling symmetry as screw 1 and adds a third re-baseline noise probe. One extra AF (~4 min) per calibration; default ON.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltAdapterWizardVM.cs` (enum, sequence, folder name, instructions, replay)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterDevices/AsgEat/EatWizardMapping.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/ITiltAdapterOptions.cs` + `TiltAdapterWizard/TiltAdapterOptions.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/TiltCalibrationCalculator.cs` (confidence probe)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/TiltAdapterWizard/DataTemplates.xaml` (Panel A checkbox)
- Test: `Joko.NINA.Plugins.HocusFocus.Tests/TiltAdapterDevices/AsgEat/EatWizardMappingTests.cs`, `Tests/TiltAdapterWizard/TiltCalibrationCalculatorTests.cs`, `Tests/TiltAdapterWizard/TiltAdapterWizardVMTests.cs`

- [x] **Step 6.1: Pre-check (read-only).** `grep -rn "(int)WizardStep\|(int)step\|(int)CurrentStep" Joko.NINA.Plugins/ --include="*.cs"` — confirm the only integer use is `StepFolderName` (WizardVM:2281). If any other numeric persistence of `WizardStep` exists, STOP and re-plan the enum placement with the user.
- [x] **Step 6.2: Failing tests first — mapping.** In `EatWizardMappingTests`:

```csharp
[TestCase(150)]
public void FullSequence_WithFinalRebaseline_SumsToZeroPerCorner(int n) {
    var executedSteps = new[] { WizardStep.AllInward, WizardStep.ReBaseline1, WizardStep.Screw1,
                                WizardStep.ReBaseline2, WizardStep.Screw2, WizardStep.ReBaseline3,
                                WizardStep.Complete };
    var total = new double[] { 0, 0, 0, 0 };
    foreach (var step in executedSteps) {
        var move = EatWizardMapping.MoveForStep(step, n, measuredFinalRebaseline: true);
        if (move == null) continue;   // Complete is a no-move when ReBaseline3 already restored
        for (int i = 0; i < 4; i++) { total[i] += move.PerCornerSteps[i]; }
    }
    Assert.That(total, Is.EqualTo(new double[] { 0, 0, 0, 0 }));
}

[Test]
public void ReBaseline3_IsDiagonalBRestore_AndCompleteBecomesNoMove() {
    var rb3 = EatWizardMapping.MoveForStep(WizardStep.ReBaseline3, 150, measuredFinalRebaseline: true);
    Assert.Multiple(() => {
        Assert.That(rb3.Axis, Is.EqualTo(TiltMoveAxis.DiagonalB));
        Assert.That(rb3.Steps, Is.EqualTo(-150));
        Assert.That(EatWizardMapping.MoveForStep(WizardStep.Complete, 150, measuredFinalRebaseline: true), Is.Null);
        Assert.That(EatWizardMapping.MoveForStep(WizardStep.Complete, 150, measuredFinalRebaseline: false).Steps, Is.EqualTo(-150));
    });
}
```
- [x] **Step 6.3: Run** `--filter "FullyQualifiedName~EatWizardMappingTests"` → FAIL (no enum member / overload).
- [x] **Step 6.4: Implement enum + mapping.**
  - `WizardStep`: add `ReBaseline3 = 7,  // g (optional): undo the screw-2 move, measured — screw 2's drift-symmetric reference` (AFTER `Complete = 6`; do not renumber).
  - `StepFolderName` (:2281): `if (step == WizardStep.ReBaseline3) return "07_ReBaseline3";` before the generic format.
  - `EatWizardMapping.MoveForStep(WizardStep step, int appliedSteps, bool measuredFinalRebaseline = false)`: `ReBaseline3` → `new TiltAdapterMove(TiltMoveAxis.DiagonalB, -appliedSteps, TiltMoveGroup.Tilt, $"Wizard Re-Baseline 3: {FormatSigned(-appliedSteps)} diagonal-B (restore, measured)")`; `Complete` → `measuredFinalRebaseline ? null : <existing DiagonalB(-N)>`. Update the class doc comment ("six executed moves" → describes both variants). Keep the existing two-arg call sites compiling via the default parameter, then update the wizard's call site to pass the real flag.
- [x] **Step 6.5: Option.** `ITiltAdapterOptions`: `bool MeasureFinalRebaseline { get; set; }` with doc comment `/// <summary>Measure one extra re-baseline after the final restore move so screw 2's move is referenced symmetrically (drift-cancelling), at the cost of one more AF run. Default true.</summary>`. `TiltAdapterOptions`: standard accessor pattern (`optionsAccessor.GetValueBoolean(nameof(MeasureFinalRebaseline), true)` in `InitializeOptions`, `SetValueBoolean` + `RaisePropertyChanged` in the setter — copy the `MeasureCurvatureDuringCalibration` implementation verbatim with the new name). UI: checkbox in wizard Panel A next to the `MeasureCurvatureDuringCalibration` binding (DataTemplates.xaml ~:716/:755), same visual pattern, label "Measure final re-baseline (recommended)". (Tilt-adapter options live in the wizard pane, not `Resources/OptionsDataTemplates.xaml` — established pattern for this options family.)
- [x] **Step 6.6: Sequence + flow.** `GetMeasurementSteps(bool measureCurvature, bool measureFinalRebaseline)` appends `WizardStep.ReBaseline3` to either array when true. Update both callers (`StartAsync` :2047, `ReplayAsync` :2952 — replay passes `byStep.ContainsKey(WizardStep.ReBaseline3.ToString())`). `MeasureStep`/`NextStep` need no structural change (RB3 is an ordinary measured step; `Complete` remains the terminal state that triggers `RunCalibrationMath`). `StepDescription`/`StepInstructionsText`: add ReBaseline3 wording — stepper: `"Apply -N steps to motor 2 and +N steps to motor 4, returning to the baseline position, then click Run Measurement."`; the device-driven path sends the RB3 move via the updated `MoveForStep`.
- [x] **Step 6.7: Calculator.** Wire `ReBaseline3`/`HasFinalRebaseline` (added in Task 3) from the wizard's readings in `RunCalibrationMath` (:2700-2705 and :2731-2736). `ComputeConfidence`: when `HasFinalRebaseline`, add probe `drift3 = |PhysicalDelta(ReBaseline3, ReBaseline2)|` into the RMS (divide by 4 instead of 3) and surface `public double Rebaseline3Drift { get; set; } = double.NaN;` on `TiltCalibrationConfidence`. Calculator test:

```csharp
[Test]
public void Calibrate_WithFinalRebaseline_Screw2DeltaIsDriftImmune() {
    // mirror of Calibrate_LinearTiltDrift_CancelsExactlyForScrew1 with ReBaseline3 = Drift(zero, 6)
    // and HasFinalRebaseline = true; assert Screw2DirectionDegrees matches the drift-free run.
}
```
  (Write it out fully by copying Task 3's test shape.)
- [x] **Step 6.8: Metadata/replay.** `RecordStepIntoMetadata` already keys by step name — "ReBaseline3" rows serialize with no schema change beyond v3. Verify `MapRunsToSteps`-equivalent replay path (WizardVM `ReplayAsync` :2947-2953) tolerates the extra step (it iterates `activeMeasurementSteps`, which now includes RB3 when present).
- [x] **Step 6.9: Run** EatWizardMapping + calculator + wizard filters → green. **Commit** — `feat(tilt): optional measured final re-baseline for drift-symmetric screw-2 delta`

---

### Task 7: TestApp — estimator comparison + fixed region path

**Files:**
- Modify: `Joko.NINA.Plugins/TestApp/TiltCalibrationRunner.cs`

- [x] **Step 7.1:** Region path fix: at :583-586 the runner calls the 8-arg `TiltPlaneModel.Create(...)` with its own region fits (regions from `BuildTiltRegions`, corner centers at ±1/3 with default ROI). Pass the new corner design points: compute `cornerXNorm`/`cornerYNorm` from the `BuildTiltRegions` rects exactly as Task 1 does (`Math.Abs(tlRect.StartX + tlRect.Width / 2.0 - 0.5)`) and pass them. Its recovered step size and SNR become honest (the ×0.62 scale disappears).
- [x] **Step 7.2:** Add to `WriteReport` (:780), after the paraboloid section:
  - a per-move comparison block: physical magnitudes of Screw1/Screw2 deltas for BOTH estimators (via `TiltCalibrationCalculator.Screw1Delta`/`Screw2Delta` + `PhysicalDelta`), their relative differences, and both `MeasuredHardwareMicrons`;
  - `Line($"Piston-implied hardware: {TiltCalibrationCalculator.PistonImpliedMicronsPerStep(inputs):0.###} µm/step");`
  - a curvature cross-check: per-step `cornerSagSteps = (TL+TR+BL+BR)/4 − center` from `RegionPositions` vs the paraboloid K's predicted sag at the corner-region radius (predicted = `K × rEff²` with `rEff²` computed from the region rects and pixel size — the design doc §4 shows the arithmetic; on ghilios_corrected this prints measured ≈ 2× predicted).
  - json: extend the anonymous object with `estimatorComparison = new { paraboloidMove1, paraboloidMove2, cornerMove1, cornerMove2, relDiff1, relDiff2, cornerHardwareMicrons, paraboloidHardwareMicrons, pistonImpliedMicronsPerStep }`.
- [x] **Step 7.3:** Update the "NOTE: the two screw turns produced very unequal tilt changes" text (:893) to add: `"If the corner-AF cross-check above disagrees with the paraboloid magnitudes, suspect the per-star fit before suspecting the hardware."` Keep verdicts and exit codes unchanged (the comparison is informational).
- [x] **Step 7.4:** Rebuild and re-run the replay against the bank run; verify the new sections appear and the corner path's recovered step size lands near 2.0–2.2 µm/step (was 1.376 with the lever-arm bug):
```bash
dotnet.exe build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
cd Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0 && \
  TILT_FIT_TIMEOUT_SEC=600 WSLENV=TILT_FIT_TIMEOUT_SEC dotnet.exe TestApp.dll tilt \
  --dataset 'D:\Tilt Calibration Bank\ghilios_corrected' --out <scratch>/tilt_replay_v2
```
  Expected: paraboloid section unchanged (ratio ~1.52); 4-corner recovered step size ≈ 2.0–2.2; estimator comparison prints relDiff ≈ 0.20 on move 2; piston-implied ≈ 2.23.
- [x] **Step 7.5: Commit** — `feat(testapp): estimator comparison, honest region lever arms, piston pitch in tilt validator`

---

### Task 8: Documentation

**Files:**
- Modify: `documentation/docs/overview/tilt-adapter-wizard.md` (hardware table :131-132, measured-pitch note :160-165)
- Modify: `docs/tilt-calibration-pitch-nonlinearity-design.md` (status)

- [x] **Step 8.1:** Read `.claude/docs/documentation-style.md` (house voice) before editing the manual.
- [x] **Step 8.2:** In `tilt-adapter-wizard.md`, rewrite the measured-pitch note to explain the frame factor in the house voice. Content requirements (style per the doc guide, not verbatim):
  - The wizard measures pitch through the optics and focuser — an *effective* µm/step in focus-shift terms. On rigs where focuser travel and sensor travel are not 1:1 (moving optics, reducer on the drawtube), the measured value can sit well above or below the mechanical spec **and that is not an error**.
  - Corrections are computed in the same effective frame, so applying "Use measured value" makes them self-consistent; keeping the mechanical spec on such a rig over- or under-corrects by the frame ratio.
  - The new "Piston-implied" line is an independent estimate of the same effective value; the wizard warns when the two disagree by more than 20%.
  - Document the new "Measure final re-baseline" setting (one extra AF; makes the second screw measurement immune to steady drift).
- [x] **Step 8.3:** In the design doc, mark §7 items 1, 2, 5, 6 implemented (with commit refs), item 3 in progress (Task 9), item 7 pending Task 9's outcome.
- [x] **Step 8.4: Commit** — `docs(tilt): frame-factor explanation and new wizard fields in the manual`

---

### Task 9: Paraboloid shrinkage investigation (analysis, timeboxed)

The replay already wrote per-state solver diagnostics to `<scratch>/tilt_replay/diag/` (`<Step>_iterations.csv`: per-iteration `Gx,Gy,K,GoF,residMedian,residMAD,lowerBound,upperBound,enabledCountAfter`; `<Step>_points.csv`: per-star position, sigma, residual, `disabledAtIteration`; `<Step>_stars.csv`: per-star sweep-fit quality and sigma). The question: what mechanism shrinks the Screw2-state gradient ×0.80 and K ×0.5 (vs region AF) while Screw1's state fits honestly?

**Files:**
- Modify: `docs/tilt-calibration-pitch-nonlinearity-design.md` (findings section)

- [x] **Step 9.1:** From `Screw2_iterations.csv` vs `Screw1_iterations.csv`: does Gx start honest at iteration 0 and shrink across winsorized iterations (→ pruning/clipping mechanism), or start low (→ weighting/seed mechanism)? Plot/tabulate Gx, K, enabledCountAfter per iteration.
- [x] **Step 9.2:** From `Screw2_points.csv`: spatial pattern of `disabledAtIteration >= 0` stars — are pruned stars concentrated on the far-defocus side of the tilt (which would drag the gradient down)? Compute mean x,y of pruned vs kept stars, and the residual sign of pruned stars.
- [x] **Step 9.3:** From `*_stars.csv`: compare `sigma_used_um` distributions Screw1 vs Screw2 states; check whether the effective sample concentrates (ESS = (Σw)²/Σw² with w = 1/σ²) — a repeat of the pre-46778b9 weight-monopoly in milder form.
- [x] **Step 9.4:** Write the findings into the design doc (new section "§8 Shrinkage mechanism") with the concrete next fix (e.g., symmetric residual budget per tilt side, or gradient-preserving clip), sized as its own follow-up plan. **Do not** modify `SensorModel.cs` in this plan — the fix needs its own TDD cycle against sim + real regressions.
- [x] **Step 9.5: Commit** — `docs(tilt): paraboloid shrinkage mechanism findings`

---

### Task 10: Final gate

- [x] **Step 10.1:** Full suite: `dotnet.exe test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` (timeout 600000). All green — except `SendAsync_WritesOnABackgroundThread` may flake (known, unrelated; re-run it in isolation before dismissing, and do not pipe through `tail`, which masks the exit code).
- [ ] **Step 10.2:** Re-run the Task 7 replay command once more from the final tree; eyeball the summary against the design doc's numbers (paraboloid ratio ~1.52 reported WITH the cross-check warning present; corner hardware ≈ 2.0; piston ≈ 2.23).
- [ ] **Step 10.3:** Push branch, open PR to `develop` titled "Tilt calibration accuracy: physical-space metrics, drift-symmetric deltas, estimator cross-checks". PR body summarizes the four root causes from the design doc and maps each commit to a §7 item. End the body with the standard generated-with footer.

---

## Self-review notes (kept for the executor)

- Spec coverage: §7.1 → Task 2; §7.2 → Tasks 1+5; §7.3 → Task 9 (analysis only, fix deliberately out of scope); §7.4 → Tasks 4+8; §7.5 → Task 4; §7.6 → Tasks 3+6; §7.7 → Task 9 outcome + Task 7's curvature cross-check line.
- Type consistency: `PhysicalDelta` (Task 2) is used by Tasks 3, 5, 7; `Screw1Delta`/`Screw2Delta` (Task 3) by Tasks 5, 6, 7; `PistonImpliedMicronsPerStep` (Task 4) by Task 7. `HasFinalRebaseline`/`ReBaseline3` are introduced in Task 3 (inputs) and wired in Task 6.
- Deliberate deviations an executor must not "fix": `ReBaseline3 = 7` sits numerically after `Complete = 6`; the corner cross-check warns but never replaces the measured pitch; TestApp verdicts/exit codes unchanged.
- Steps 1.1, 5.6, and 6.1 are read-before-write checks because test-helper ctor shapes (`RatioRect`, `StarDetectionRegion`, VM test invocation) were confirmed to exist but not read line-by-line during planning. The assertions and math in those tests are fixed; only construction syntax may need adapting.
