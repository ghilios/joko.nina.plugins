# Small Fixes (F7–F14 + Weight-Chain Follow-ups) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Execution outcome (recorded post-run):** Tasks 1–5, 7, 8 (F9, F10, F13, W1, W2, F12, W3) and the
> Task 9 F14 doc landed as planned. **Task 6 (F11) was deferred**: its meanFlux fix is correct but
> Step 4's TestApp check measured an 8.1% accepted-star drop on the corpus image (1970→1810), so per
> this plan's own Task 6 decision rule it was pulled from the batch into a new roadmap step 6 (F11 +
> BrightnessSensitivity recalibration). W4 accordingly became roadmap step 7. The Task 9 roadmap edit
> below was written to reflect that split rather than the original in-batch F11 wording.

**Goal:** Land roadmap step 5 — eight small accuracy/correctness fixes (F9, F10, F11, F12, F13, W1, W2, W3), an F14 documentation note, and roadmap updates — in a single PR on branch `ghilios/step5-small-fixes`.

**Architecture:** Each fix is independent and localized (one production file + one test file per task). No new components except one small test double (`FailNthSolveAlglibAPI`). Design doc: `docs/small-fixes-f7-f14-design.md` (approved). F7/F8 are won't-fix decisions; W4 is deferred to roadmap step 6 — neither gets code.

**Tech Stack:** C# / .NET 8.0-windows, NUnit 4 + NSubstitute, OpenCvSharp, alglib. Windows toolchain from WSL: wrap `dotnet` in `cmd.exe /c "..."` or use `rtk dotnet ...` (see `~/.claude/CLAUDE.md`). Always pass `timeout: 600000` to Bash for build/test commands.

**Conventions used throughout:**

- Commit command (required author identity — substitute the message per task):

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "<message>

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- Filtered test run (fast, shows failure detail):

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~<TestName>"
```

- Full suite (token-cheap green check):

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```

  Note: `rtk dotnet` may print a `fail` header even on success — trust `errors=0` and the failed-test count, not the header word.

---

### Task 0: Setup and F11 baseline capture

The F11 fix (Task 6) changes the sensitivity gate slightly; we need a star-count baseline from the **unmodified** code before any edits.

**Files:** none modified.

- [ ] **Step 1: Confirm branch and clean state**

```bash
git status && git branch --show-current
```

Expected: branch `ghilios/step5-small-fixes`, clean tree (the design-doc commit is already on it).

- [ ] **Step 2: Confirm the suite is green before touching anything**

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```

(timeout: 600000) Expected: 0 failed.

- [ ] **Step 3: Build TestApp and confirm the corpus image exists**

```bash
ls "/mnt/c/Workshop Data/Data/autofocus/uneven/final/11_Frame00_BitDepth16_Bayered0_Focuser56457.xisf"
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
```

(timeout: 600000) If the image is missing, **stop and ask the user** for a representative `.xisf`/`.fits` path to use instead.

- [ ] **Step 4: Capture the F11 baseline star count**

```bash
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  contamination --image "C:\Workshop Data\Data\autofocus\uneven\final\11_Frame00_BitDepth16_Bayered0_Focuser56457.xisf" --out "C:\temp\hf-step5\baseline"
wc -l "/mnt/c/temp/hf-step5/baseline/contamination_stars.csv"
```

(timeout: 600000) Record the row count (= accepted stars + 1 header line) for Task 6's comparison.

---

### Task 1: F9 — `Star.AddOffset` field completeness

`AddOffset` (ROI→full-image translation, used on every AF inner-crop detection) silently drops `PeakBrightness`, `StarContaminationSuspected`, and `BackgroundPlane`.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/CvImageUtility.cs:561-570`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/CvImageUtilityTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `CvImageUtilityTests.cs` (add `using NINA.Joko.Plugins.HocusFocus.Interfaces;` to the file's usings if not present; `Star` and `LocalBackgroundPlane` live there):

```csharp
[Test]
public void AddOffset_CarriesAllFields_AndTranslatesBackgroundPlane() {
    var star = new Star() {
        Center = new Point2d(15, 27),
        StarBoundingBox = new Rect(10, 22, 11, 11),
        Background = 0.25,
        BackgroundPlane = new LocalBackgroundPlane(originX: 15, originY: 27, b0: 0.25, b1: 0.01, b2: -0.02, isFlat: false),
        MeanBrightness = 0.6,
        PeakBrightness = 0.9,
        HFR = 2.5,
        PSF = null,
        StarContaminationSuspected = true
    };

    var offset = star.AddOffset(xOffset: 100, yOffset: 200);

    Assert.Multiple(() => {
        Assert.That(offset.Center.X, Is.EqualTo(115));
        Assert.That(offset.Center.Y, Is.EqualTo(227));
        Assert.That(offset.StarBoundingBox, Is.EqualTo(new Rect(110, 222, 11, 11)));
        Assert.That(offset.Background, Is.EqualTo(0.25));
        Assert.That(offset.MeanBrightness, Is.EqualTo(0.6));
        Assert.That(offset.PeakBrightness, Is.EqualTo(0.9));
        Assert.That(offset.HFR, Is.EqualTo(2.5));
        Assert.That(offset.StarContaminationSuspected, Is.True);
        Assert.That(offset.BackgroundPlane, Is.Not.Null);
        Assert.That(offset.BackgroundPlane.IsFlat, Is.False);
        // The plane is anchored at the star center: translated plane at translated point == original at original point.
        Assert.That(offset.BackgroundPlane.ValueAt(115 + 3, 227 + 4),
            Is.EqualTo(star.BackgroundPlane.ValueAt(15 + 3, 27 + 4)).Within(1e-12));
    });
}

[Test]
public void AddOffset_NullBackgroundPlane_StaysNull() {
    var star = new Star() {
        Center = new Point2d(5, 6),
        StarBoundingBox = new Rect(1, 2, 8, 8),
        BackgroundPlane = null
    };

    var offset = star.AddOffset(xOffset: 10, yOffset: 20);

    Assert.That(offset.BackgroundPlane, Is.Null);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~AddOffset"
```

(timeout: 600000) Expected: `AddOffset_CarriesAllFields_AndTranslatesBackgroundPlane` FAILS (PeakBrightness 0, contamination flag false, plane null); `AddOffset_NullBackgroundPlane_StaysNull` passes (it pins the null guard for the new code).

- [ ] **Step 3: Implement**

Replace `CvImageUtility.AddOffset` (lines 561-570) with:

```csharp
public static Star AddOffset(this Star star, int xOffset, int yOffset) {
    return new Star() {
        Center = star.Center.Add(new Point2d(xOffset, yOffset)),
        StarBoundingBox = star.StarBoundingBox.Add(new Point(xOffset, yOffset)),
        Background = star.Background,
        // The plane is anchored at the star's ROI-space center — translate its origin so
        // ValueAt(translated point) matches the original plane at the original point.
        BackgroundPlane = star.BackgroundPlane == null
            ? null
            : new LocalBackgroundPlane(
                star.BackgroundPlane.OriginX + xOffset,
                star.BackgroundPlane.OriginY + yOffset,
                star.BackgroundPlane.B0,
                star.BackgroundPlane.B1,
                star.BackgroundPlane.B2,
                star.BackgroundPlane.IsFlat),
        MeanBrightness = star.MeanBrightness,
        PeakBrightness = star.PeakBrightness,
        HFR = star.HFR,
        PSF = star.PSF,
        StarContaminationSuspected = star.StarContaminationSuspected
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Same command as Step 2. Expected: both PASS.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/CvImageUtility.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/CvImageUtilityTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Carry PeakBrightness, contamination flag, and background plane through Star.AddOffset (F9)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: F10 — `ResetDefaults` consistency

`ResetDefaults` writes the `simple_FocusRange` backing field (no persist/notify/reconfigure) and sets `StarPeakResponse = 0.6` where the load default and `ConfigureSimpleSettings` both use 0.75.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs:203,215`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `StarDetectionOptionsTests.cs` (the fixture's `Build()` helper returns `(options, store, profile)`; `store` is the `InMemoryPluginOptionsAccessor`):

```csharp
[Test]
public void ResetDefaults_FocusRangePersistedAndNotified_PeakResponseMatchesSimpleMode() {
    var (options, store, _) = Build();
    options.Simple_FocusRange = FocusRangeEnum.WideRange;
    var raised = new List<string>();
    options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

    options.ResetDefaults();

    Assert.Multiple(() => {
        Assert.That(options.Simple_FocusRange, Is.EqualTo(FocusRangeEnum.Typical));
        Assert.That(raised, Does.Contain(nameof(StarDetectionOptions.Simple_FocusRange)));
        // Persisted value must match the in-memory value (the old code wrote the backing field only).
        Assert.That(store.GetValueEnum("Simple_FocusRange", FocusRangeEnum.WideRange), Is.EqualTo(FocusRangeEnum.Typical));
        // ResetDefaults and ConfigureSimpleSettings must agree on the canonical default.
        Assert.That(options.StarPeakResponse, Is.EqualTo(0.75));
    });
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~ResetDefaults_FocusRangePersistedAndNotified"
```

(timeout: 600000) Expected: FAIL — persisted value still `WideRange`, no `Simple_FocusRange` notification, `StarPeakResponse` 0.6.

- [ ] **Step 3: Implement**

In `StarDetectionOptions.ResetDefaults()`:

Line 203, change:

```csharp
            simple_FocusRange = FocusRangeEnum.Typical;
```

to:

```csharp
            Simple_FocusRange = FocusRangeEnum.Typical;
```

Line 215, change:

```csharp
            StarPeakResponse = 0.6;
```

to:

```csharp
            StarPeakResponse = 0.75;
```

- [ ] **Step 4: Run the test to verify it passes; run the options suite**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~StarDetectionOptionsTests"
```

(timeout: 600000) Expected: all PASS (including the pre-existing `ResetDefaults_RestoresDocumentedDefaults`).

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetectionOptions.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/StarDetectionOptionsTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Reset Simple_FocusRange through its property and align StarPeakResponse default to 0.75 (F10)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: F13 — kappa-sigma zero masking from iteration 0

`KappaSigmaNoiseEstimate` masks pixels outside `[ε, threshold)` from iteration 1 onward, but iteration 0 includes exact-zero pixels (calibrated/stacked frames' empty borders), biasing the initial mean/σ.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/CvImageUtility.cs:516-521`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/CvImageUtilityTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `CvImageUtilityTests.cs`:

```csharp
[Test]
public void KappaSigmaNoiseEstimate_ZeroBorder_MaskedFromFirstIteration() {
    const int width = 64, height = 64;
    const float mean = 0.5f, sigma = 0.05f;
    using var mat = new Mat(new Size(width, height), MatType.CV_32F);
    var rng = new Random(42);
    unsafe {
        var p = (float*)mat.DataPointer;
        for (var i = 0; i < width * height; ++i) {
            // Deterministic Gaussian noise via Box-Muller
            var u1 = 1.0 - rng.NextDouble();
            var u2 = rng.NextDouble();
            var n = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            p[i] = (float)Math.Max(0.01, mean + sigma * n);
        }
        // Zero the left quarter — a calibrated/stacked frame's empty border
        for (var y = 0; y < height; ++y) {
            for (var x = 0; x < width / 4; ++x) {
                p[y * width + x] = 0.0f;
            }
        }
    }

    // With maxIterations=1 the estimate IS the first-iteration statistic: unmasked zeros inflate
    // σ to ~0.22 (the 0 vs 0.5 split dominates); masked from iteration 0 it stays ≈ the noise σ.
    var firstIteration = CvImageUtility.KappaSigmaNoiseEstimate(mat, maxIterations: 1);
    Assert.Multiple(() => {
        Assert.That(firstIteration.Sigma, Is.EqualTo(sigma).Within(0.01));
        Assert.That(firstIteration.BackgroundMean, Is.EqualTo(mean).Within(0.01));
    });
}

[Test]
public void KappaSigmaNoiseEstimate_GaussianNoise_NoZeros_MatchesKnownSigma() {
    const int width = 64, height = 64;
    const float mean = 0.5f, sigma = 0.05f;
    using var mat = new Mat(new Size(width, height), MatType.CV_32F);
    var rng = new Random(1234);
    unsafe {
        var p = (float*)mat.DataPointer;
        for (var i = 0; i < width * height; ++i) {
            var u1 = 1.0 - rng.NextDouble();
            var u2 = rng.NextDouble();
            var n = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            p[i] = (float)Math.Max(0.01, mean + sigma * n);
        }
    }

    var result = CvImageUtility.KappaSigmaNoiseEstimate(mat);
    Assert.Multiple(() => {
        Assert.That(result.Sigma, Is.EqualTo(sigma).Within(0.01));
        Assert.That(result.BackgroundMean, Is.EqualTo(mean).Within(0.01));
    });
}
```

- [ ] **Step 2: Run the tests to verify the zero-border one fails**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~KappaSigmaNoiseEstimate"
```

(timeout: 600000) Expected: `ZeroBorder_MaskedFromFirstIteration` FAILS (σ ≈ 0.22); `GaussianNoise_NoZeros_MatchesKnownSigma` PASSES (pins the unchanged path); pre-existing flat-image tests PASS.

- [ ] **Step 3: Implement**

In `KappaSigmaNoiseEstimate` (CvImageUtility.cs:516-521), replace:

```csharp
                while (numIterations < maxIterations) {
                    if (numIterations > 0) {
                        Cv2.InRange(image, float.Epsilon, threshold - float.Epsilon, backgroundMaskMat);
                    }

                    Cv2.MeanStdDev(image, out var meanScalar, out var sigmaScalar, numIterations > 0 ? backgroundMaskMat : null);
```

with:

```csharp
                while (numIterations < maxIterations) {
                    // Exclude zero/negative pixels from the very first iteration too — calibrated/stacked
                    // frames can carry exact-zero borders that would otherwise bias the initial mean/σ
                    // (accuracy analysis F13). threshold starts at float.MaxValue, so iteration 0 keeps
                    // every positive finite pixel.
                    Cv2.InRange(image, float.Epsilon, threshold - float.Epsilon, backgroundMaskMat);

                    Cv2.MeanStdDev(image, out var meanScalar, out var sigmaScalar, backgroundMaskMat);
```

- [ ] **Step 4: Run the tests to verify they pass**

Same command as Step 2. Expected: all KappaSigma tests PASS.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Utility/CvImageUtility.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/Utility/CvImageUtilityTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Mask zero pixels from kappa-sigma's first iteration (F13)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: W1 — `SolveHuberIrls` keeps the last good solution

A mid-loop failed `SolveOnce` overwrites the `out solution` with the failed parameters (or null) yet returns true — the comment claims the previous good solution is kept, but it was just clobbered.

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TestDoubles/FailNthSolveAlglibAPI.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs:431-467`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/AlglibHyperbolicFittingHuberTests.cs`

- [ ] **Step 1: Create the test double**

Create `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TestDoubles/FailNthSolveAlglibAPI.cs`:

```csharp
using NINA.Joko.Plugins.HocusFocus.Utility;
using static alglib;

namespace NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles {

    /// <summary>
    /// Delegates to a real <see cref="AlglibAPI"/> but corrupts the Nth minlmresults call (1-based) —
    /// NaN parameters and a negative termination type — to simulate a mid-IRLS optimizer failure.
    /// </summary>
    internal sealed class FailNthSolveAlglibAPI : IAlglibAPI {
        private readonly IAlglibAPI inner = new AlglibAPI();
        private readonly int failOnCall;
        private int resultsCalls;

        public FailNthSolveAlglibAPI(int failOnCall) {
            this.failOnCall = failOnCall;
        }

        public void minlmresults(minlmstate state, out double[] x, out minlmreport rep) {
            inner.minlmresults(state, out x, out rep);
            if (++resultsCalls == failOnCall) {
                for (int i = 0; i < x.Length; ++i) {
                    x[i] = double.NaN;
                }
                rep.terminationtype = -3; // any negative type (except -5) makes SolveOnce return false
            }
        }

        public void minlmcreatevj(int m, double[] x, out minlmstate state) => inner.minlmcreatevj(m, x, out state);

        public void minlmsetacctype(minlmstate state, int acctype) => inner.minlmsetacctype(state, acctype);

        public void minlmcreatev(int m, double[] x, double diffstep, out minlmstate state) => inner.minlmcreatev(m, x, diffstep, out state);

        public void rbfcreate(int nx, int ny, out rbfmodel s) => inner.rbfcreate(nx, ny, out s);

        public void minlmsetbc(minlmstate state, double[] bndl, double[] bndu) => inner.minlmsetbc(state, bndl, bndu);

        public void minlmsetcond(minlmstate state, double epsx, int maxits) => inner.minlmsetcond(state, epsx, maxits);

        public void minlmsetscale(minlmstate state, double[] s) => inner.minlmsetscale(state, s);

        public void minlmoptguardgradient(minlmstate state, double teststep) => inner.minlmoptguardgradient(state, teststep);

        public void minlmoptimize(minlmstate state, ndimensional_fvec fvec, ndimensional_jac jac, ndimensional_rep rep, object obj) => inner.minlmoptimize(state, fvec, jac, rep, obj);

        public void minlmoptguardresults(minlmstate state, out optguardreport rep) => inner.minlmoptguardresults(state, out rep);

        public void deallocateimmediately<T>(ref T obj) where T : alglibobject => inner.deallocateimmediately(ref obj);
    }
}
```

(If `minlmreport.terminationtype` turns out not to be assignable — it is a public field in alglib.net, so it should be — replace the whole `rep` with a fresh `new minlmreport()` and set its fields.)

- [ ] **Step 2: Write the failing test**

Add to `AlglibHyperbolicFittingHuberTests.cs` (add `using NINA.Joko.Plugins.HocusFocus.Tests.TestDoubles;`):

```csharp
[Test]
public void SolveHuberIrls_MidLoopSolveFailure_ReturnsLastGoodSolution() {
    // The IRLS loop always attempts a second reweighted solve after a successful first one
    // (the convergence check cannot pass on iteration 0: prevSumAbsResiduals starts at +inf).
    // Failing solve #2 must leave the result exactly equal to the plain non-IRLS solution —
    // not the failed solve's parameters.
    var failing = new FailNthSolveAlglibAPI(failOnCall: 2);
    var robust = HyperbolicFittingAlglib.Create(failing, CleanCurveWithOutlier(), useWeights: false);
    robust.HuberIrlsEnabled = true;
    Assert.That(robust.Solve(), Is.True);

    var plain = HyperbolicFittingAlglib.Create(alglibAPI, CleanCurveWithOutlier(), useWeights: false);
    plain.HuberIrlsEnabled = false;
    Assert.That(plain.Solve(), Is.True);

    Assert.Multiple(() => {
        Assert.That(robust.Minimum.X, Is.Not.NaN);
        Assert.That(robust.Minimum.X, Is.EqualTo(plain.Minimum.X).Within(1e-6));
        Assert.That(robust.Minimum.Y, Is.EqualTo(plain.Minimum.Y).Within(1e-6));
    });
}
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~SolveHuberIrls_MidLoopSolveFailure"
```

(timeout: 600000) Expected: FAIL — `robust.Minimum.X` is NaN (the corrupted parameters were returned).

- [ ] **Step 4: Implement**

In `AlglibHyperbolicFitting.SolveHuberIrls` (lines 431-441), replace:

```csharp
        private bool SolveHuberIrls(double[] initialGuess, double[] lowerBounds, double[] upperBounds, double[] scale, out double[] solution) {
            const int maxIrlsIterations = 10;
            const double tolerance = 1e-6;
            solution = null;
            var prevSumAbsResiduals = double.PositiveInfinity;
            var guess = initialGuess;
            for (int iter = 0; iter < maxIrlsIterations; ++iter) {
                if (!SolveOnce(guess, lowerBounds, upperBounds, scale, out solution)) {
                    return iter > 0; // keep the previous good solution if a later reweighting fails
                }
                guess = solution; // warm-start the next reweighted solve
```

with:

```csharp
        private bool SolveHuberIrls(double[] initialGuess, double[] lowerBounds, double[] upperBounds, double[] scale, out double[] solution) {
            const int maxIrlsIterations = 10;
            const double tolerance = 1e-6;
            solution = null;
            double[] lastGoodSolution = null;
            var prevSumAbsResiduals = double.PositiveInfinity;
            var guess = initialGuess;
            for (int iter = 0; iter < maxIrlsIterations; ++iter) {
                if (!SolveOnce(guess, lowerBounds, upperBounds, scale, out solution)) {
                    // SolveOnce just overwrote the out param with the failed attempt (or null) —
                    // restore the previous good solution instead of returning the failed parameters.
                    solution = lastGoodSolution;
                    return solution != null;
                }
                lastGoodSolution = solution;
                guess = solution; // warm-start the next reweighted solve
```

- [ ] **Step 5: Run the test to verify it passes; run the Huber/fitting suites**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~Huber"
```

(timeout: 600000) Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/TestDoubles/FailNthSolveAlglibAPI.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/AlglibHyperbolicFittingHuberTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Keep the last good IRLS solution when a mid-loop solve fails (W1)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: W2 — median-centered Huber comparison

The Huber threshold δ is built from the median-centered MAD, but the comparison uses uncentered `|r|` — inconsistent when the residual distribution has a non-zero median.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs:425-459`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/AlglibHyperbolicFittingHuberTests.cs`

- [ ] **Step 1: Write the pinning test**

Add to `AlglibHyperbolicFittingHuberTests.cs`:

```csharp
private static List<ScatterErrorPoint> CleanCurveWithSameSideOutliers() {
    var pts = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
        x0: 5000, y0: 0.5, a: 2.0, b: 80.0,
        xStart: 4700, xStep: 25, count: 25);
    // Several outliers on the SAME side shift the residual median away from zero, which is
    // exactly where an uncentered |r| comparison and a median-centered MAD disagree.
    foreach (var i in new[] { 3, 9, 20 }) {
        var bad = pts[i];
        pts[i] = new ScatterErrorPoint(bad.X, bad.Y + 4.0, 0, 1.0);
    }
    return pts;
}

[Test]
public void HuberIrls_SameSideOutliers_RecoversMinimumWithCenteredThreshold() {
    var robust = HyperbolicFittingAlglib.Create(alglibAPI, CleanCurveWithSameSideOutliers(), useWeights: false);
    robust.HuberIrlsEnabled = true;
    Assert.That(robust.Solve(), Is.True);
    Assert.That(Math.Abs(robust.Minimum.X - 5000), Is.LessThan(10.0),
        "Huber IRLS should keep best-focus near truth despite same-side outliers");
}
```

This test pins the desired behavior; it may already pass pre-change (the existing Huber tests are the regression guard — they must stay green through the change).

- [ ] **Step 2: Run the new test (record pre-change result)**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~SameSideOutliers"
```

(timeout: 600000) Note the result either way; proceed.

- [ ] **Step 3: Implement**

In `SolveHuberIrls`, replace (lines 450-459 region):

```csharp
                var (_, mad) = residuals.MedianMAD();
                if (mad <= 0.0 || double.IsNaN(mad)) {
                    break; // residuals already tight; no robust reweighting needed
                }
                var delta = HuberSigmaMultiplier * mad;
                for (int i = 0; i < Inputs.Length; ++i) {
                    var absR = Math.Abs(residuals[i]);
                    var huber = absR <= delta ? 1.0 : delta / absR;
                    effectiveWeights[i] = Weights[i] * huber;
                }
```

with:

```csharp
                var (residualMedian, mad) = residuals.MedianMAD();
                if (mad <= 0.0 || double.IsNaN(mad)) {
                    break; // residuals already tight; no robust reweighting needed
                }
                var delta = HuberSigmaMultiplier * mad;
                for (int i = 0; i < Inputs.Length; ++i) {
                    // Center on the residual median so the comparison is consistent with the
                    // median-centered MAD that defines δ — an offset residual distribution would
                    // otherwise down-weight the bulk and under-penalize one-sided outliers.
                    var absR = Math.Abs(residuals[i] - residualMedian);
                    var huber = absR <= delta ? 1.0 : delta / absR;
                    effectiveWeights[i] = Weights[i] * huber;
                }
```

Also update the method's XML doc (lines 425-430): change `inliers (|r| ≤ δ) keep weight 1, outliers are down-weighted by δ/|r|` to `inliers (|r − median(r)| ≤ δ) keep weight 1, outliers are down-weighted by δ/|r − median(r)|`.

- [ ] **Step 4: Run all Huber/fitting tests**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~Fitting"
```

(timeout: 600000) Expected: all PASS, including the existing `HuberIrls_GrossOutlier_RecoversMinimumBetterThanPlainFit` and the new same-side test.

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/AlglibHyperbolicFittingHuberTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Center the Huber residual comparison on the residual median (W2)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: F11 — `meanFlux` denominator + TestApp validation

`meanFlux = totalFlux / starPoints.Count` divides clip-survivor flux by the all-structure-pixel count, inflating `NormalizedBrightness` (and loosening the sensitivity gate) for faint stars with clipped skirt pixels.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs:1199`

- [ ] **Step 1: Implement**

In `ComputeStarParameters` (StarDetector.cs:1199), replace:

```csharp
            var meanFlux = totalFlux / starPoints.Count;
```

with:

```csharp
            // totalFlux sums clip-survivors only, so the mean must divide by the survivor count
            // (analysis F11) — dividing by all structure pixels inflated NormalizedBrightness for
            // faint stars with clipped skirt pixels, loosening the sensitivity gate inconsistently.
            var meanFlux = totalFlux / numUnclippedPixels;
```

(`numUnclippedPixels` ≥ 2 is guaranteed here: the degenerate check at line 1175 already returned null otherwise.)

- [ ] **Step 2: Run the detector test suites**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~StarDetector"
```

(timeout: 600000) Expected: all PASS. If a synthetic-detection test fails because a marginal star now drops below the sensitivity gate, examine whether the test pinned the buggy value — adjust only with that justification written in the test.

- [ ] **Step 3: Rebuild TestApp and compare star counts against the Task 0 baseline**

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe \
  contamination --image "C:\Workshop Data\Data\autofocus\uneven\final\11_Frame00_BitDepth16_Bayered0_Focuser56457.xisf" --out "C:\temp\hf-step5\after-f11"
wc -l "/mnt/c/temp/hf-step5/baseline/contamination_stars.csv" "/mnt/c/temp/hf-step5/after-f11/contamination_stars.csv"
```

(timeout: 600000) Expected: accepted-star count within ~2% of baseline. **If the drop exceeds 2%, STOP** — report the numbers to the user and reassess before continuing (per the design's risk note; the fix may then need its own calibration pass).

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Divide meanFlux by the clip-survivor count, not all structure pixels (F11)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: F12 — weighted parabolic Grubbs

The parabolic rejection test passes no weights while the hyperbolic one does — yet NINA core's `QuadraticFitting` always weights by 1/ErrorY², so its Grubbs test should judge residuals the same way.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs:127`

- [ ] **Step 1: Implement**

In `CurveFittingResult.Calculate` (AutoFocusEngine.cs:127), replace:

```csharp
                                fittings.QuadraticFitting = new QuadraticFitting().Calculate(validFocusPoints);
                                rejectedPoint = MathUtility.RejectionTest(points: validFocusPoints, fitting: fittings.QuadraticFitting.Fitting, confidence: rejectionConfidence);
```

with:

```csharp
                                fittings.QuadraticFitting = new QuadraticFitting().Calculate(validFocusPoints);
                                // NINA core's QuadraticFitting always weights by 1/ErrorY² (it has no unweighted
                                // mode), so Grubbs must judge residuals the same way — unconditionally weighted,
                                // unlike the hyperbolic site below, where WeightedHyperbolicFitEnabled gates the
                                // fit itself (analysis F12).
                                rejectedPoint = MathUtility.RejectionTest(points: validFocusPoints, fitting: fittings.QuadraticFitting.Fitting, confidence: rejectionConfidence, weights: AlglibHyperbolicFitting.BuildResidualWeights(validFocusPoints, useWeights: true));
```

- [ ] **Step 2: Run the engine test suite**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~AutoFocusEngine"
```

(timeout: 600000) Expected: all PASS.

- [ ] **Step 3: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/AutoFocus/AutoFocusEngine.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Pass residual weights to the parabolic Grubbs rejection test (F12)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: W3 — SensorModel per-star weight regularization

The Inspection module's per-star hyperbolic fits build σ from `EstimateHfrStdDev` (floored at 1e-3) without `WeightRegularization` — a high-SNR frame can carry ~1000× weight inside one star's sweep fit.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Inspection/SensorModel.cs:664`

- [ ] **Step 1: Implement**

In `FitImages` (SensorModel.cs:664), replace:

```csharp
                    var points = registeredStar.MatchedStars.Select(s => new ScatterErrorPoint(s.FocuserPosition, s.Star.HFR, 0.0d, EstimateHfrStdDev(s.Star))).ToList();
```

with:

```csharp
                    // Same 5× weight cap as the AF fit path: EstimateHfrStdDev floors σ at 1e-3, so a
                    // high-SNR frame could otherwise carry ~1000× weight inside this star's sweep fit
                    // (the weight-chain F5a hazard's sibling — see WeightRegularization).
                    var points = WeightRegularization.Regularize(
                        registeredStar.MatchedStars.Select(s => new ScatterErrorPoint(s.FocuserPosition, s.Star.HFR, 0.0d, EstimateHfrStdDev(s.Star))).ToList());
```

(`using NINA.Joko.Plugins.HocusFocus.StarDetection;` is already present in SensorModel.cs. `Regularize` returns a new list of copies, and the downstream rejection loop and `SelectBestModel` operate on the list they are handed — no further changes.)

- [ ] **Step 2: Run the Inspection test suites**

```bash
cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.HocusFocus.Tests\Joko.NINA.Plugins.HocusFocus.Tests.csproj -c Debug --nologo --filter FullyQualifiedName~SensorModel"
```

(timeout: 600000) Expected: all PASS (including the repeatability tests — `Regularize` is deterministic, so the build stays deterministic).

- [ ] **Step 3: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Inspection/SensorModel.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Regularize per-star fit weights in the sensor model (W3)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: F14 documentation + roadmap updates

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs:631` (XML doc only)
- Modify: `docs/star-detection-hfr-autofocus-accuracy-analysis.md` (§10 table)

- [ ] **Step 1: Add the F14 XML doc on `MeasureStar`**

Above `internal bool MeasureStar(...)` (StarDetector.cs:631), add:

```csharp
        /// <summary>
        /// Computes the star's HFR — the flux-weighted mean radius over a circular aperture, sampled
        /// on an AnalysisSamplingSize grid through the centroid with per-pixel background-plane
        /// subtraction. Saturated pixels are NOT masked here (unlike the PSF fit, which is off during
        /// AF): a flat saturated core under-weights the center, biasing HFR high — most likely near
        /// focus. No correction is attempted because HFR is an empirical flux sum (masking core pixels
        /// would bias it further); median aggregation across stars limits the damage, and the
        /// Saturated metric tracks exposure (accuracy analysis F14, decided document-only).
        /// </summary>
```

- [ ] **Step 2: Update the roadmap table**

In `docs/star-detection-hfr-autofocus-accuracy-analysis.md` §10, replace the row:

```markdown
| 5. Small fixes (F7–F14) | ⬜ Not started | Independent one-liner-to-small patches. |
```

with:

```markdown
| 5. Small fixes (F7–F14) | 🟡 In progress | One PR: F9 AddOffset fields, F10 ResetDefaults, F12 weighted parabolic Grubbs, F13 kappa-sigma zero mask, plus weight-chain follow-ups W1 (SolveHuberIrls last-good), W2 (centered Huber), W3 (SensorModel regularization). F14 document-only; F7/F8 won't-fix; **F11 pulled to step 6 after an 8.1% star-count drop**. Plans: `small-fixes-f7-f14-{design,plan}.md`. |
| 6. F11 meanFlux + sensitivity recalibration | ⬜ Not started | meanFlux denominator fix (÷ clip-survivor count) + BrightnessSensitivity recalibration per preset (the knob was tuned against the inflated NormalizedBrightness). Measured −8.1% accepted stars before recalibration. |
| 7. Structural IRLS robustness (W4) | ⬜ Not started | Judge Huber residuals against an unweighted reference fit so a high-weight displaced point cannot self-mask (recovery cliff at ≥ ~1.25× capped weight ratio — see `weight-chain-hygiene-design.md` §1 implementation finding). Needs FitQualityRunner corpus validation before any change. |
```

(The actual landed roadmap edit matches the split above — F11 → step 6, W4 → step 7. The original single-PR wording that listed F11 in row 5 and W4 as step 6 was superseded by the Task 6 deferral.)

- [ ] **Step 3: Build to confirm the doc-only code change compiles**

```bash
rtk dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```

(timeout: 600000) Expected: errors=0.

- [ ] **Step 4: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/StarDetector.cs docs/star-detection-hfr-autofocus-accuracy-analysis.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" -m "Document F14 saturation limitation; update roadmap for step 5 and add step 6 (W4)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 10: Final verification and PR

- [ ] **Step 1: Full suite green**

```bash
rtk dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo
```

(timeout: 600000) Expected: 0 failed. If anything fails, fix the cause before proceeding (never skip/ignore tests).

- [ ] **Step 2: Review the diff once end-to-end**

```bash
git log --oneline develop..HEAD && git diff develop --stat
```

Expected: ~10 commits (design + plan docs + 8 fix/doc commits), touching the 6 production files, 4 test files (1 new), and 3 plans docs listed in this plan — nothing else.

- [ ] **Step 3: Push and open the PR**

```bash
git push -u origin ghilios/step5-small-fixes
gh pr create --base develop --title "Step 5 small fixes: F9-F13 + weight-chain follow-ups (W1-W3)" --body "$(cat <<'EOF'
Roadmap step 5 of docs/star-detection-hfr-autofocus-accuracy-analysis.md (design: docs/small-fixes-f7-f14-design.md).

## Code fixes
- **F9**: Star.AddOffset now carries PeakBrightness, StarContaminationSuspected, and a translated BackgroundPlane (ROI/AF-crop detections no longer lose them)
- **F10**: ResetDefaults sets Simple_FocusRange through its property (persist+notify) and aligns StarPeakResponse to the canonical 0.75
- **F12**: parabolic Grubbs rejection now weighted, matching NINA core's always-weighted QuadraticFitting
- **F13**: kappa-sigma noise estimate masks zero pixels from the first iteration
- **W1**: SolveHuberIrls returns the last good solution when a mid-loop solve fails (was: the failed solve's parameters)
- **W2**: Huber comparison centered on the residual median, consistent with the median-centered MAD threshold
- **W3**: SensorModel per-star fits routed through WeightRegularization (same 5× cap as the AF path)

## Decisions (no code)
- F14 saturation-in-HFR: document-only (XML doc on MeasureStar)
- F7 TRENDHYPERBOLIC averaging, F8 brightest-N selection: won't-fix (rarely-used paths)
- F11 meanFlux: deferred to roadmap step 6 (8.1% star-count drop needs a BrightnessSensitivity recalibration)
- W4 structural IRLS reference-fit: deferred to roadmap step 7

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR URL printed.

---

## Self-review checklist (run after writing, before execution)

- Spec coverage: every design section (F9, F10, F11, F12, F13, W1, W2, W3, F14 doc, F7/F8/W4 decisions, roadmap) maps to a task — verified.
- No placeholders; all code blocks are complete and paths exact.
- Type consistency: `BuildResidualWeights(ICollection<ScatterErrorPoint>, bool)` returns `Func<double, double>` (matches `RejectionTest`'s `weights:` parameter); `WeightRegularization.Regularize(IReadOnlyList<ScatterErrorPoint>) → List<ScatterErrorPoint>`; `LocalBackgroundPlane(originX, originY, b0, b1, b2, isFlat)`; `KappaSigmaNoiseEstimate(Mat, double clippingMultipler = 3.0, double allowedError = 0.00001, int maxIterations = 5)` — `maxIterations: 1` is a valid named argument.
