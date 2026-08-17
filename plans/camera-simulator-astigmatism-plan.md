# Camera Simulator — Astigmatic Tilt & Backfocus — Implementation Plan

Spec: [`docs/camera-simulator-astigmatism-design.md`](../docs/camera-simulator-astigmatism-design.md).
Physics reference: [`docs/backfocus-eccentricity-modeling.md`](../docs/backfocus-eccentricity-modeling.md).
The model, its derivations, the sign convention, and the rejected alternatives live in the spec; this
plan is the execution order and the acceptance criteria.

## Ordering constraint

**Land the timing seam and record the `off` performance baseline before the elliptical PSF change goes
in.** Once the PSF is elliptical there is no way to re-measure today's cost.

Steps 2-4 are pure math with no rendering dependency and can land independently of step 6.

---

## 1. Timing seam and baseline

**`CameraSimulator/Rendering/RenderPhaseTimings.cs`** (new, `internal`): `CatalogQueryMs`,
`StampJobBuildMs`, `KernelGenerateMs`, `StampMs`, `DevelopMs`, `StarsQueried`, `StampJobs`,
`DistinctKernels`, `KernelCacheBytes`, `MaxKernelRadius`.

**`StarFieldCompositor`**: an `internal ushort[] Render(RenderRequest, ICollection<StarTruth>,
RenderPhaseTimings, CancellationToken)` overload; the two public overloads chain into it with nulls. This
mirrors the existing `truthSink` overload precedent exactly, including its contract — **passing null must
render byte-identically**, so every timing call sits behind `if (timings != null)`. `TestApp` already has
`InternalsVisibleTo` (`Properties/AssemblyInfo.cs:22`), so this never becomes public API.

Why a seam rather than differencing two renders: `NoiseGenerator.DevelopRange` branches at
`PoissonToGaussianThreshold = 40`, and the measured table in
`plans/camera-simulator-realism-and-performance-plan.md` shows development swinging ~10× with λ (12 s at
λ=14.6, 37 s at λ=39, 3.8 s at λ=60). Stars change λ, so "dense minus starless" books a development delta
as PSF cost.

**`TestApp/BenchSimRenderRunner.cs`** + a `bench-simrender` verb in `TestApp/Program.cs`, modeled on
`TestApp/BenchWaveletRunner.cs` (warmup, `GC.Collect()` between iterations, median + min/max, banner with
build configuration / `ProcessorCount` / GC mode). Sub-modes: `--census` (star and kernel counts only,
seconds) and `--kernel-ladder` (times `PsfKernelGenerator.Generate` alone across R = 8…240 px and fits the
log-log scaling exponent; needs no catalog).

Build requests through the single existing owner `SynthRenderRequestFactory.Build(...)` and override the
aberration block with a `with`-expression — `RenderRequest` is a record and the factory hard-codes
`AberrationsEnabled = false`.

**Then**: run `--census` on the three pointings (below), confirm `dense` clears 25 000 on-frame stars
(raise `--limit-mag` toward 17.5 if short), pin the values in the spec, and record the full matrix on
today's code as the `off` arm.

---

## 2. `CameraSimulator/Rendering/AberrationSurface.cs`

Do **not** fold astigmatism into `LocalDefocusMicrons` — that method's contract ("the inspector's
algebraic inverse") is load-bearing and must stay literally true.

```csharp
public const double NominalCurvaturePerSpacingPerAreaMicrons = 1.0684e-10;  // c_m0; derivation in the spec
public double EffectiveSpacingErrorMicrons { get; }   // e_c, inferred as K / c_m0 when the option is unset
public double CurvaturePerSpacing { get; }            // c_m
public double AstigmatismCoefficient { get; }         // rho * c_m; literally 0.0 when disabled
public double PredictedAstigmatismEffectMicrons { get; }

public double LocalSpacingErrorMicrons(int px, int py);   // e(x,y)
public double AstigmatismSplitMicrons(int px, int py);    // A(x,y)
public double FieldAngleRadians(int px, int py);          // theta about the optical axis; 0 at r'=0
public void AstigmaticDefocusMicrons(int px, int py, int steps,
        out double tangentialMicrons, out double sagittalMicrons, out double thetaRadians);
```

New ctor params (`astigmatismEnabled`, `backfocusSpacingErrorMicrons`, `astigmatismRatio`) default to the
**off** state on the plain-number constructor, so every existing caller and test compiles and behaves
identically; `FromRequest` passes the real values. Reject a negative or NaN ratio with the file's existing
`!(x >= 0)` idiom. Factor the inline pixel→centered-micron conversion into a shared private helper. Update
the class doc: it is no longer a pure local-defocus surface, it is a *pair* of surfaces whose **mean** is
that surface.

---

## 3. `CameraSimulator/Rendering/PsfKernelGenerator.cs`

```csharp
public static PsfKernel GenerateAstigmatic(DefocusModel model,
        double tangentialDefocusMicrons, double sagittalDefocusMicrons,
        double positionAngleRadians, PsfKernelMethod method = PsfKernelMethod.Analytic);
```

with the hard requirement that `aRad == aTan` returns `Generate(model, tangentialDefocusMicrons)` — the
existing exact path, untouched.

`GenerateElliptical` (private, with an `internal` entry so tests can force it at equal axes):

```
h = 1/S ; pad = 1
hx = sqrt((aRad*cos)^2 + (aTan*sin)^2) + 5*sigma
hy = sqrt((aRad*sin)^2 + (aTan*cos)^2) + 5*sigma
radius = max(1, ceil(max(hx, hy)))            ; keep the MaxKernelRadius = 512 throw
aR = max(aRad, 1e-3*sigma) ; aT = max(aTan, 1e-3*sigma)      ; a line focus has a literally-zero semi-axis

; pass 1 - antialiased elliptical-annulus mask in the rotated frame
u =  cx*cos + cy*sin ;  v = -cx*sin + cy*cos
q = sqrt((u/aR)^2 + (v/aT)^2) ;  g = sqrt((u/aR^2)^2 + (v/aT^2)^2)
dOut = (q-1)*q/max(g,tiny) ;  dIn = (eps-q)*q/max(g,tiny)
cover = clamp(0.5 - dOut/h, 0, 1) * (eps > 0 ? clamp(0.5 - dIn/h, 0, 1) : 1)

; pass 2 - separable Gaussian, taps precomputed once per render (sigma is frame-global)
sTaps = sqrt(max(sigma^2 - h^2/12, (sigma/2)^2)) * S          ; double-box correction
fine = convolve rows then cols, zero border

; pass 3 - verbatim existing code: measuredHfr, box-bin into S^2 phases, normalize each to sum 1
```

Managed float convolution, **not** `Cv2.SepFilter2D` — OpenCV dispatches SIMD by runtime CPU feature and
`Render` is contractually pure with byte-for-byte determinism asserted. Note the escape hatch in a comment
as an all-or-nothing choice. Keep the elliptical path's fine raster `float[]`; leave the isotropic path's
`double[]` untouched so it cannot shift by a ULP.

Add `EllipticalRiceHfr` (2-D Simpson over the pupil annulus, reusing `RiceMean`), reducing algebraically to
`RiceHfr` at equal axes.

---

## 4. `CameraSimulator/Rendering/PsfKernel.cs`

Add `OuterRadiusRadialPixels`, `OuterRadiusTangentialPixels`, the two inner radii,
`PositionAngleRadians`, `IsAstigmatic`, and `PredictedEccentricity` from the closed form in the spec.

`OuterRadiusPixels` **keeps its name and becomes `max(a_rad, a_tan)`** (and `InnerRadiusPixels = ε·max`).
Every consumer treats it as the enclosing extent — `GoldenFromTruth.BoxHalfWidthPixels` uses
`outerRadius + 3σ` — so `max` is the only value that keeps golden boxes conservative. Had it been the mean
or the radial axis, elliptical stars would spill outside their boxes and register as recall failures. Put
that reasoning in a comment.

`RadialIntensity` is meaningless for an ellipse and has only two consumers, both in
`PsfKernelGeneratorTests` (lines 89, 93). Add `AxisIntensity(r, PsfPrincipalAxis)` backed by two
principal-axis LUTs and keep `RadialIntensity` as the radial-axis alias — identical to today for circular
kernels, failing loudly on an elliptical one rather than silently returning a mean profile.

---

## 5. Options and plumbing

| Option | Type | Default |
|---|---|---|
| `EnableFieldAstigmatism` | bool | `true` |
| `BackfocusSpacingErrorMicrons` | double | `-1` (unset ⇒ infer) |
| `AstigmatismRatio` | double | `0.7` |

Plus `BackfocusErrorMicrons` default **0.0 → 50.0** in both `InitializeOptions` and `ResetDefaults`.

- `Interfaces/ICameraSimulatorOptions.cs` + `CameraSimulator/CameraSimulatorOptions.cs` — the five edit
  sites per option, plus a read-only `EffectiveBackfocusSpacingErrorMicrons` for the hint text.
- `CameraSimulator/RenderRequest.cs` — three fields in the `// --- Aberrations ---` group (the
  enabled+value pair mirrors the existing `CentralObstructionEnabled` + `CentralObstructionFraction`).
- `CameraSimulator/HocusFocusSimulatorCamera.cs` `BuildRenderRequest` — copy all three. An option not
  copied here is invisible to exposures.
- `Resources/OptionsDataTemplates.xaml` — three controls in the Field Aberrations group (~3597-3690),
  three `CamSim_<Prop>_Tooltip` resources (~3261-3282), and a `<RowDefinition />` per new row
  (`Tests/Resources/OptionsDataTemplatesLayoutTests.cs` fails the build if two column-0 children share a
  row). The spacing field copies the blank-means-infer pattern from the Focuser Step Size row (`:3330-3349`):
  `ninactrl:HintTextBox` + `HF_DoubleNegativeToEmptyStringConverter`, `HintText` bound to the Effective
  property, and **no** `ValidationRules` (they run on raw text and reject an empty box). `AstigmatismRatio`
  copies the Tilt Amount row (`:3639-3650`) with `Minimum="0" Maximum="3"`.
- `CameraSimulator/TiltAdapter/SimulatedTiltInjection.cs` — `Fold` also adds `pistonMicrons` to
  `BackfocusSpacingErrorMicrons` **only when that option is explicitly set**. Never silently convert blank
  to explicit.
- `CameraSimulator/Rendering/StarTruth.cs` — `OuterRadiusRadialPixels`, `OuterRadiusTangentialPixels`,
  `PositionAngleRadians`, `PredictedEccentricity`, `AstigmatismSplitMicrons`, and the quantized triple key
  (the benchmark's census cross-check reads it). `OuterRadiusPixels` keeps its name and now carries the
  max, so `GoldenFromTruth:588` and `SynthBankDerivations` need no change.

---

## 6. `CameraSimulator/Rendering/StarFieldCompositor.cs`

```csharp
private readonly record struct PsfKernelKey(long LevelT, long LevelS, int OrientationBin);
```

- **Isotropy decided from the quantized levels, never the raw Δ** — see the spec's lemma. `levelT ==
  levelS` ⇒ `OrientationBin = -1` and the existing isotropic `Generate` call.
- **Adaptive orientation bins**, `n_θ = clamp(ceil(π·s_max/(√2·0.25)), 1, 64)` with
  `s_max = max min(|Δ|,|A|)/(N·p)` over the field sample set, computed once per render. Kernels are
  generated at the **bin centre**; orientation is mod π.
- **Cache byte budget**: estimate `n_T·n_S·n_θ · S²(2R_worst+1)²·4` up front; over 128 MB, coarsen the
  defocus quantum and orientation bins by a common factor `g = min(16, ceil((est/budget)^(1/3)))` and log
  it; still over at `g = 16`, disable astigmatism for that render with a warning. Uniform across the
  frame, decided before the loop, never an OOM or a throw. Plus a `HardKernelCount = 4096` assert on
  insert.
- **Two-pass parallel kernel build**: (1) loop stars → `(x, y, key, flux)` collecting distinct keys;
  (2) `Parallel.ForEach` over the distinct keys through `ParallelExecution.CreateOptions` writing into a
  pre-sized array indexed by list position, then populate the dictionary serially; (3) attach kernels to
  jobs and emit truth. Deterministic because the key list order is the deterministic star order and each
  kernel is a pure function of its key. This parallelizes today's isotropic path for free. At ~1 ms/kernel
  near focus and ~8 ms at R = 35, this is not optional.
- **`WorstCaseKernelRadius`** maximizes `|Δ| + |A|` (since `max(|Δ_T|,|Δ_S|) = |Δ| + |A|`). While there,
  fix a **pre-existing bug**: the sample set is 4 corners + centre, but for a plane-plus-paraboloid the
  extremum over the rectangle is at a corner or at the interior stationary point
  `(X0 − Gx/(2K), Y0 − Gy/(2K))`, which equals the sensor centre only when `Gx = Gy = X0 = Y0 = 0`. With a
  large optical-axis offset the true worst case is missed, under-sizing the projection margin and silently
  dropping wing-spill stars. Add the clamped stationary point.

`MaxAbsDefocusMicrons` is unchanged and simply applies per axis.

---

## 7. Performance run

**Fields** (all IMX455 = QHY600, 9576×6388, 3.76 µm), with **measured** on-frame star counts at mag 17:

| name | pointing | optics | on-frame stars | purpose |
|---|---|---|---|---|
| `dense-wide` | γ Cygni, RA 305.5583°, Dec +40.2567° | 530 mm f/5 | **34 193** | the headline dense field. |
| `dense` | γ Cygni | 1000 mm f/7.1 | 8 377 | same pointing/optics as the checked-in `D19_cygnus_deep_shed` bank row. |
| `sparse` | North Galactic Pole, RA 192.8595°, Dec +27.1283° | 1000 mm f/7.1 | 980 | 8.5× density contrast against `dense` with all else fixed. |
| `dense-oversampled` | γ Cygni | 2000 mm f/8 | 1 651 | σ_min 2.78 px vs 1.44 — the R² time/memory stress. |

The plan originally targeted ≥ 25 000 on-frame stars at 1000 mm and mag 16. **That is not reachable**:
at 1000 mm the frame covers only 2.8 sq deg and the installed G18 database bottoms out near mag 18, so
even at the catalog's faint limit the 1000 mm field yields 14 237 on-frame stars. `dense-wide` at 530 mm
f/5 — an FSQ-106-class widefield astrograph, a common QHY600 pairing — covers 3.6× the sky and clears the
target comfortably. Mag 17 is the chosen cut (mag 18 gives 61 214 and is available as a stress).

**Matrix**: 3 aberration configs (`A0` clean / `A1` backfocus only / `A2` tilt + backfocus) × 3 defocus
points (0, 150, 350 steps ≈ 1×, 3.6×, 8× HFR_min) × 4 arms (`off`, `on-zero`, `on`, `on-strong`), plus the
sparse and oversampled controls and one `--with-detection` contention cell. 54 cells, ~15-30 min at
`--iters 3`. The `on-zero` arm matters: "elliptical code path with zero ellipticity" is a different cost
from "no elliptical code path", and conflating them hides fixed overhead.

**Run in Release** — the historical 13.1 s figure was Debug; label every table with the configuration.
Run `--kernel-ladder` **first**: seconds, no catalog, and a bad convolution shows immediately as a scaling
exponent near 4 instead of ~2.

**Gates**:

| | gate | bar |
|---|---|---|
| G1 | `on/off` total, clean `A0` cells | ≤ 1.10× |
| G2 | `on/off` total, all other cells | ≤ 1.25× |
| G3 | worst absolute cell, Release | ≤ 3.0 s and ≤ the configured AF exposure |
| G4 | `KernelGenerateMs` share of total | ≤ 10 % |
| G5 | `DistinctKernels` per frame | ≤ 512 |
| G6 | peak `KernelCacheBytes` | ≤ 256 MB |
| G7 | `on/off` under `--with-detection` | ≤ 1.40× |

G2's bar comes from the render being prefetched at `StartExposure`: the cost is invisible while
render ≤ exposure, and beyond that it resurfaces as camera download time. If G2 fails, ranked levers to
benchmark as extra arms: (1) the parallel kernel build; (2) coarser orientation bins; (3) a process-level
cross-frame kernel memo keyed on the **full** model identity (σ, N, p, ε, S, key) — a 9-point AF sweep
rebuilds the same kernels nine times, and this changes timing only, not pixels; (4) a better convolution.

Results → `docs/camera-simulator-astigmatism-results.md`, shaped like
`docs/star-detection-optimizer-speedup-results.md`. Add a `bench-simrender` section to
`.claude/docs/testapp-cli.md` (which does not currently document `bench-wavelet`, so there is no existing
bench section to append to).

Environment: ASTAP **G18 (.290)** database at `C:\Program Files\astap` (limiting mag ~18). No `dotnet` in
WSL — build and run via Windows `dotnet.exe` through WSL interop (`wslpath -w`).

---

## 8. Tests

**`PsfKernelGeneratorTests`**
- `Astigmatic_EqualAxes_ReturnsTheAnalyticKernelUnchanged` — element-wise exact; the hard-requirement guard.
- `EllipticalRasterizer_ForcedWithEqualAxes_MatchesAnalyticPath` — via the `internal` entry bypassing the
  short-circuit: ≤ 1e-3·MaxPeak, HFR within 0.5 %.
- `Astigmatic_MeasuredHfr_MatchesEllipticalRiceClosedForm` — 1 %, matching the isotropic tolerance.
- `EllipticalRiceHfr_ReducesToRiceHfr_WhenAxesEqual` — 1e-9.
- `Astigmatic_SecondMoments_MatchPredictedEccentricity`.
- `Astigmatic_LineFocus_HasFiniteMinorWidth` — minor width = σ within 5 %, eccentricity < 0.99. **The test
  the rejected warp approach fails.**
- 90°-transpose and π-identity; phase normalization; donut hole on both principal axes; the cap throw; Fft
  still not implemented.

**`AberrationSurfaceTests`**
- `InjectRecover_IsIdentity` unchanged and still passing, plus cases with astigmatism enabled proving
  `Gx/Gy/K/Phi` are untouched.
- `MeanOfTangentialAndSagittal == LocalDefocusMicrons` to 1e-12.
- `A == 0.0` exactly when the toggle or aberrations are off or ρ = 0.
- `A != 0` for pure tilt with zero backfocus — the reason this model was chosen.
- Inference consistency: `c_m == c_m0` when the spacing field is blank.
- `A` scales as r'² about the *optical axis* with nonzero `X0/Y0`, and is exactly 0 there.
- Sign flips with the spacing sign; negative ρ throws.
- The orientation rule (`sign(Δ·A) < 0 ⇒ a_rad > a_tan`) on a field grid, no rendering.

**`StarFieldCompositorTests`**
- Astigmatism **disabled** renders byte-identically to today (and with ρ = 0).
- `Render_NullTimings_IsByteIdenticalToPublicOverload`.
- Determinism and purity with astigmatism on — proves the parallel kernel build.
- `Render_KernelCacheCardinality_StaysBounded` — `DistinctKernels` and `KernelCacheBytes` against the caps,
  and the coarsening log fires. Pure counting, sub-second; fails the day someone halves a quantum.
- Truth sink carries the new fields with `OuterRadiusPixels == max(...)`.
- A wing-spill star at an astigmatic corner is still stamped — guards the `|Δ| + |A|` change.

**Capstone** — `AstigmatismRendersRadialAndTangentialCornersThroughFullPipeline` on
`SyntheticCameraTestScene` (IMX533 3008², N = 7, σ_min = 1.324 px, 2Np = 52.64 µm/px). Inject tilt 120 µm
at 0°, backfocus 40 µm, ρ = 0.7, focuser **at** `OptimalFocuserPosition`:

| region | Δ (µm) | A (µm) | a_rad, a_tan (px) | orientation | predicted e |
|---|---|---|---|---|---|
| left, x ≈ 300 | +83.3 | +8.97 | 1.41, 1.75 | tangential | 0.33 |
| right, x ≈ 2700 | −108.1 | +8.86 | 2.22, 1.89 | radial | 0.34 |
| centre | 0 | 0 | equal | round | ≈ 0 |

Assert `S = median cos(2·Δθ)` < −0.5 left, > +0.5 right; median eccentricity > 0.20 at the edges and
< 0.15 at centre. Then re-render with backfocus −40 µm and assert both signs flip — that pins the sign
convention against a whole-model inversion, which a relative-only test cannot catch.

**The y-flip, by derivation not trial**: `PSFModeler.Solve` returns θ for the major axis in [−π/2, π/2]
and `InspectorVM` draws it as `(cos θ, −sin θ)` "since y is inverted to render top-down", so
`θ_radial = atan2(−(py − cy), px − cx)`. Put the derivation in a comment.

Keep the capstone **near focus** (`a ≲ 2–3 px`): `PSFGoodnessOfFitThreshold = 0.9` means a strongly
elliptical *donut* fits a Moffat poorly and can lose its `PSF` object entirely, taking `Eccentricity` with
it. Note that in the test so nobody "improves" it by defocusing harder.

**Regression** — `AberrationSurfaceRecoveredThroughFullPipeline_TiltAndBackfocus` must pass **with
astigmatism enabled** at its existing tolerances (φ ±6°, tilt ±20 %, curvature ±25 %), by the 90°-rotation
property in the spec. If it does not, the model or the rasterizer is wrong — investigate, do not widen.
Parameterize `[TestCase(astigmatism: false/true)]`.

**Suite** — `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo` via Windows
`dotnet.exe`. `CameraSimulatorOptionsTests` needs updating for the three new options and the changed
`BackfocusErrorMicrons` default.

**Bank** — `SynthBankSpec.cs:346` hard-codes `AberrationsEnabled = false`, so every synthetic-bank star
collapses to `levelT == levelS` and hits the identical `Generate` call; existing `*.golden.json` stay
valid. **Verify, don't assume**: re-run `bank-verify` on one dataset before and after and diff.

**Live** — render in NINA against the simulator camera with aberrations on and confirm the Aberration
Inspector's eccentricity vector field shows the radial/tangential corner flip.

---

## 9. Documentation

- `documentation/docs/overview/camera-simulator-rendering.md` — §"The point-spread function" gains the
  elliptical-annulus form; §"Tilt and field curvature" loses the now-false *"no coma or astigmatism
  stretching is modeled"* sentence and gains the two-surface model, the `Δ·A < 0 ⇒ radial` rule, the finite
  circle of least confusion, and the new options. The `adversarial-doc-review` skill flags raw pipes inside
  table-cell math — write `|Δ|` as `\lvert \Delta \rvert`.
- `docs/synthetic-camera-design.md` §"Field-aberration surface (tilt / backfocus)" — point at the new spec
  and note the surface is now a pair.

---

## 10. Risks

- **Render time** is the largest regression risk; without the parallel kernel build an aggressive
  tilt+backfocus scene can add seconds per exposure, and the camera prefetches renders at `StartExposure`.
  The `off` case must be within noise of today.
- **Memory** — the 3-D key is the first unbounded allocation in this pipeline. The byte budget is not
  optional.
- **Determinism** — `Render_IsDeterministicForFixedSeed` / `Render_IsPure` are the contract that lets the
  camera prefetch. Parallel kernel building must write to disjoint pre-indexed slots, and OpenCV must stay
  out of the kernel path.
- **Detection-side heuristics** — `StarDetector`'s donut-aware paths assume circular donuts. Elliptical
  donuts are new input for them. Not a correctness bug (stressing the detector is the point), but expect
  the AF-degradation and detection-binning capstones to be where a surprise first shows; both currently
  render with `BackfocusErrorMicrons = 0` and are inert until someone enables aberrations.
- **The changed `BackfocusErrorMicrons` default** is the one shipped-behaviour change. It only bites when
  a user turns `EnableAberrations` on, but it does change what they see versus today, and
  `SyntheticCameraTestScene` defaults should be set deliberately rather than inherited.
