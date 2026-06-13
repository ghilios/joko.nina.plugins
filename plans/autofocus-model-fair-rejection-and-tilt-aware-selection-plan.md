# Autofocus Model-Fair Rejection + Tilt-Aware Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Hybrid auto-focus model selection reject only *true* outliers (points no candidate model can fit) and report an asymmetric ("tilted") model whenever the asymmetry is statistically justified — so a genuinely tilted V-curve is never collapsed to symmetric by inappropriate point removal or by the variance-only ranking.

**Architecture:** Two coordinated changes inside `AlglibHyperbolicFitting.SelectBestModel` (the Hybrid finalization path). (1) **Consensus rejection**: compute each candidate model's would-be Grubbs outliers, remove only the *intersection* (points flagged by every solved model), and fit/compare every model on that one common cleaned set. (2) **Significance-gated selection**: a nested F-test (hardcoded confidence 0.95) gates the 5-parameter asymmetric models against the 4-parameter Symmetric baseline — asymmetric models can win only when they significantly improve the fit; otherwise Symmetric wins (parsimony). No new options, UI, interfaces, or public signature changes.

**Tech Stack:** C# / .NET 8, MathNet.Numerics (`FisherSnedecor`, `StudentT`), NUnit 4 + NSubstitute, alglib.net (Levenberg–Marquardt). Source-linked test project.

**Design spec:** `docs/autofocus-model-fair-rejection-and-tilt-aware-selection-design.md`

---

## Pre-requisite — already committed

This session's prep is committed on branch **`ghilios/af-model-fair-rejection`** (branched from `develop`) as two commits:
1. the `docs/`↔`plans/` reorg + the `CLAUDE.md` convention + the repointed `plans/→docs/` references (source comments + plan cross-links);
2. the `af-fit` diagnostic (`TestApp/AfFitDiagnosticRunner.cs` + `Program.cs` dispatch) + this design spec (`docs/…-design.md`) + this plan.

Before starting, confirm the branch and a clean tree:

```bash
cd /mnt/c/Users/ghili/src/joko.nina.plugins
git rev-parse --abbrev-ref HEAD   # expect: ghilios/af-model-fair-rejection
git status                        # expect: clean
```

Then begin at Task 1. (If you'd rather ship the reorg as its own PR, cherry-pick commit 1 onto a separate branch later — the two commits are independent.)

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs` | Hyperbolic fits + Hybrid selection | Add `IsAsymmetryJustified` helper + `TiltSignificanceConfidence` const; rewrite the `SelectBestModel(... maxOutlierRejections ...)` body to consensus rejection + gated selection; extract a `RankBest` helper from the existing tier logic. |
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/AsymmetryFTestTests.cs` | Unit tests for the F-test gate (pure math) | Create. |
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/ModelFairRejectionTests.cs` | Consensus rejection + gated selection behavior | Create. |
| `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/HybridModelSelectionTests.cs` | Existing SelectBestModel tests | Reconcile two tests that encode the old per-model/variance-only semantics. |

The Hybrid candidate set (`HybridCandidateModels`), `FitWithOutlierRejection`, `Create`, `BuildResidualWeights`, the viability filter, and the Tier-1/Tier-2 ranking already exist in `AlglibHyperbolicFitting.cs` and are reused.

---

## Task 1: F-test asymmetry-significance helper (pure, hardcoded confidence)

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs` (add `using MathNet.Numerics.Distributions;` if absent; add a `const` near `HybridCandidateModels` ~line 252 and a private static helper near `CompareAscNaNLast` ~line 478)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/AsymmetryFTestTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `AsymmetryFTestTests.cs`:

```csharp
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Tests the nested F-test that decides whether a 5-parameter asymmetric hyperbola significantly improves
    /// on the 4-parameter Symmetric baseline. The statistic is scale-invariant in σ (numerator and denominator
    /// both scale by 1/σ²), so it is valid even though weighted reduced χ² runs ≪ 1 for star-rich fields.
    /// </summary>
    [TestFixture]
    public class AsymmetryFTestTests {

        // Reproduces the faithful "uneven" run numbers (n=10): Symmetric reduced χ²=0.0259607 (dof 6) vs
        // Tilted reduced χ²=0.00707306 (dof 5) ⇒ F≈17 ⇒ significant at 0.95 AND 0.99.
        [Test]
        public void Justified_WhenAsymmetricFitsSignificantlyBetter() {
            double chiSym = 0.0259607 * 6;   // χ² = reducedχ² × dof, p_sym = 4 ⇒ dof = 10-4 = 6
            double chiTilt = 0.00707306 * 5; // p_asym = 5 ⇒ dof = 10-5 = 5
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(chiSym, chiTilt, n: 10, confidence: 0.95), Is.True);
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(chiSym, chiTilt, n: 10, confidence: 0.99), Is.True);
        }

        [Test]
        public void NotJustified_WhenImprovementIsTiny() {
            // Asymmetric barely improves χ² (1%): F is small ⇒ not significant.
            double chiSym = 0.020 * 6;
            double chiAsym = 0.0198 * 5;
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(chiSym, chiAsym, n: 10, confidence: 0.95), Is.False);
        }

        [Test]
        public void NotJustified_WhenAsymmetricFitsWorseOrEqual() {
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.10, 0.12, n: 10, confidence: 0.95), Is.False);
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.10, 0.10, n: 10, confidence: 0.95), Is.False);
        }

        [Test]
        public void NotJustified_WhenTooFewPointsToTest() {
            // n - p_asym < 1 ⇒ no denominator degrees of freedom ⇒ cannot test ⇒ false.
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.50, 0.10, n: 5, confidence: 0.95), Is.False);
        }

        [Test]
        public void NotJustified_OnNonFiniteInputs() {
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(double.NaN, 0.1, n: 10, confidence: 0.95), Is.False);
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.5, double.NaN, n: 10, confidence: 0.95), Is.False);
            Assert.That(AlglibHyperbolicFitting.IsAsymmetryJustified(0.5, 0.0, n: 10, confidence: 0.95), Is.False);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~AsymmetryFTestTests"`
Expected: FAIL — `AlglibHyperbolicFitting` does not contain a definition for `IsAsymmetryJustified` (compile error).

- [ ] **Step 3: Add the constant and the helper**

In `AlglibHyperbolicFitting.cs`, ensure the using block has `using MathNet.Numerics.Distributions;`. Immediately after the `HybridCandidateModels` array (~line 257) add:

```csharp
        /// <summary>
        /// Confidence for the nested F-test that gates the asymmetric (5-parameter) models against the
        /// Symmetric (4-parameter) baseline in <see cref="SelectBestModel"/>. Hardcoded — an internal
        /// statistical threshold, not a user tuning knob (design §6). Higher ⇒ stricter ⇒ more reluctant to
        /// report tilt.
        /// </summary>
        private const double TiltSignificanceConfidence = 0.95;
```

Then add this `internal static` helper near `CompareAscNaNLast` (~line 484) — `internal` so the test project (which links the source) can call it:

```csharp
        /// <summary>
        /// Nested F-test: is the 5-parameter asymmetric fit a statistically significant improvement over the
        /// 4-parameter Symmetric baseline, at <paramref name="confidence"/>? Both χ² are the weighted χ² of the
        /// two fits on the SAME point set (same regularized 1/σ weights), so the ratio is scale-invariant.
        /// Returns false when there are too few points to test (n − 5 &lt; 1), when inputs are non-finite, or when
        /// the asymmetric fit does not actually reduce χ².
        /// </summary>
        internal static bool IsAsymmetryJustified(double chiSquaredSymmetric, double chiSquaredAsymmetric, int n, double confidence) {
            const int pSym = 4, pAsym = 5;
            int dofDenom = n - pAsym;
            if (dofDenom < 1) return false;
            if (double.IsNaN(chiSquaredSymmetric) || double.IsNaN(chiSquaredAsymmetric) || chiSquaredAsymmetric <= 0.0) return false;
            double improvement = chiSquaredSymmetric - chiSquaredAsymmetric;
            if (improvement <= 0.0) return false;
            double f = (improvement / (pAsym - pSym)) / (chiSquaredAsymmetric / dofDenom);
            double fCritical = new FisherSnedecor(pAsym - pSym, dofDenom).InverseCumulativeDistribution(confidence);
            return f > fCritical;
        }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~AsymmetryFTestTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/AsymmetryFTestTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "AF: add nested F-test asymmetry-significance helper (hardcoded 0.95)"
```

---

## Task 2: Consensus rejection in SelectBestModel

Replace per-model rejection (each model cleans its own data) with consensus rejection (remove only points flagged by *every* solved model), and fit/compare all models on one common cleaned set. **No gate yet** — ranking still picks the lowest σ(focus) across all viable survivors, so this task is behavior-preserving for symmetric data and stops the spurious removals on tilted data.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs` (`SelectBestModel` overload at ~lines 291–431; add a `RankBest` helper)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/ModelFairRejectionTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `ModelFairRejectionTests.cs`:

```csharp
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Joko.Plugins.HocusFocus.Tests.Synthetic;
using NUnit.Framework;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.StarDetection {

    /// <summary>
    /// Model-fair rejection: only points flagged as outliers by EVERY solved candidate model (the consensus /
    /// intersection) are removed, so a tilt-revealing wing point (an outlier only under Symmetric) is kept while
    /// a gross bad frame (an outlier under every model) is removed.
    /// </summary>
    [TestFixture]
    public class ModelFairRejectionTests {
        private IAlglibAPI alglibAPI;

        [SetUp]
        public void SetUp() => alglibAPI = new AlglibAPI();

        // A clean, strongly tilted curve with tight error bars (mirrors the "uneven" run): the Symmetric model
        // would flag wing points, but the asymmetric models flag none → consensus is empty → nothing removed.
        [Test]
        public void Consensus_KeepsTiltRevealingPoints_NoSpuriousRemoval() {
            var points = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.02, xStart: 760, xStep: 32, count: 15, errorY: 0.04);
            var stepSize = InferStep(points);

            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, stepSize, useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.90,
                out var bestFit, out var rejects);

            Assert.That(rejects, Is.Empty, "consensus must remove no genuine points from a clean tilted curve");
            Assert.That(bestFit, Is.Not.Null);
        }

        // A gross bad frame is an outlier under every model → consensus removes it (capped by maxOutlierRejections).
        [Test]
        public void Consensus_RemovesGrossOutlier_FlaggedByAllModels() {
            var clean = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.02, xStart: 760, xStep: 32, count: 15, errorY: 0.04);
            var stepSize = InferStep(clean);
            var badX = clean[5].X;
            var withOutlier = clean.Select(p => p.X == badX
                ? new ScatterErrorPoint(p.X, p.Y + 4.0, 0, p.ErrorY) : p).ToList();

            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, withOutlier, stepSize, useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.90,
                out _, out var rejects);

            Assert.That(rejects.Select(p => p.X), Does.Contain(badX), "the gross bad frame must be the consensus outlier");
            Assert.That(rejects.Count, Is.EqualTo(1), "only the one bad frame is unanimous; tilt wings are kept");
        }

        // maxOutlierRejections = 0 ⇒ no rejection regardless of data.
        [Test]
        public void Consensus_ZeroBudget_RemovesNothing() {
            var clean = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, xStart: 880, xStep: 12, count: 21, errorY: 0.1);
            var withOutlier = clean.Select((p, i) => i == 8 ? new ScatterErrorPoint(p.X, p.Y + 4.0, 0, p.ErrorY) : p).ToList();
            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, withOutlier, InferStep(clean), useWeights: true, maxOutlierRejections: 0, rejectionConfidence: 0.90,
                out _, out var rejects);
            Assert.That(rejects, Is.Empty);
        }

        private static int InferStep(List<ScatterErrorPoint> points) {
            var xs = points.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
            var diffs = new List<double>();
            for (int i = 1; i < xs.Count; ++i) diffs.Add(xs[i] - xs[i - 1]);
            diffs.Sort();
            return diffs.Count == 0 ? 25 : Math.Max(1, (int)Math.Round(diffs[diffs.Count / 2]));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~ModelFairRejectionTests"`
Expected: FAIL — `Consensus_KeepsTiltRevealingPoints_NoSpuriousRemoval` fails because the current per-model code lets Symmetric reject wing points (`rejects` not empty).

- [ ] **Step 3: Rewrite the SelectBestModel body to consensus rejection**

In `AlglibHyperbolicFitting.cs`, replace the body of the rejection-budget `SelectBestModel` overload **from the first `Parallel.For` (~line 323) through the final `return survivorModels[best];` (~line 430)** with the following. Keep the method signature, the `minX`/`maxX` computation above it, and `FitWithOutlierRejection` unchanged.

```csharp
            // PASS 1: each candidate proposes its own Grubbs outliers (against its own residuals), but does NOT
            // commit them. modelRejects[k] = the points model k WOULD reject; solved[k] = whether it fit at all.
            var modelRejects = new List<ScatterErrorPoint>[HybridCandidateModels.Length];
            var solved = new bool[HybridCandidateModels.Length];
            var fitOptions = new ParallelOptions {
                MaxDegreeOfParallelism = Math.Min(ParallelExecution.ResolveDegreeOfParallelism(maxDegreeOfParallelism), HybridCandidateModels.Length)
            };
            Parallel.For(0, HybridCandidateModels.Length, fitOptions, k => {
                try {
                    var fit = FitWithOutlierRejection(alglibAPI, HybridCandidateModels[k], points, stepSize, useWeights,
                        maxOutlierRejections, rejectionConfidence, out var rejects, out _);
                    if (fit != null) {
                        solved[k] = true;
                        modelRejects[k] = rejects;
                    }
                } catch (Exception ex) {
                    Logger.Trace($"Hybrid selection (pass 1): model {HybridCandidateModels[k]} failed ({ex.Message})");
                }
            });

            // CONSENSUS: a point is a true outlier only if EVERY solved model flags it. Fewer than 2 solved models
            // cannot distinguish tilt from a bad frame, so reject nothing. Key points by rounded focuser position.
            var solvedCount = solved.Count(s => s);
            var consensus = new List<ScatterErrorPoint>();
            if (solvedCount >= 2 && maxOutlierRejections > 0) {
                IEnumerable<int> common = null;
                for (int k = 0; k < HybridCandidateModels.Length; ++k) {
                    if (!solved[k]) continue;
                    var keys = (modelRejects[k] ?? new List<ScatterErrorPoint>()).Select(p => (int)Math.Round(p.X)).ToHashSet();
                    common = common == null ? keys : common.Intersect(keys);
                }
                var commonKeys = (common ?? Enumerable.Empty<int>()).ToHashSet();
                if (commonKeys.Count > 0) {
                    // Materialize consensus points from the first solved model's proposed set (identical X across
                    // models), preserving that model's rejection order, then cap at maxOutlierRejections.
                    var firstSolved = Array.FindIndex(solved, s => s);
                    consensus = modelRejects[firstSolved]
                        .Where(p => commonKeys.Contains((int)Math.Round(p.X)))
                        .Take(maxOutlierRejections)
                        .ToList();
                }
            }

            // Build the single common cleaned set every model is judged on (fair comparison; no model cleans its
            // own data differently).
            var consensusKeysFinal = consensus.Select(p => (int)Math.Round(p.X)).ToHashSet();
            var cleaned = points.Where(p => !consensusKeysFinal.Contains((int)Math.Round(p.X))).ToList();

            // PASS 2: fit every model on the common cleaned set — these fits drive viability, the F-test gate, and ranking.
            var modelFits = new AlglibHyperbolicFitting[HybridCandidateModels.Length];
            Parallel.For(0, HybridCandidateModels.Length, fitOptions, k => {
                try {
                    var fit = Create(alglibAPI, HybridCandidateModels[k], cleaned, stepSize, useWeights);
                    if (fit.Solve()) {
                        modelFits[k] = fit;
                    }
                } catch (Exception ex) {
                    Logger.Trace($"Hybrid selection (pass 2): model {HybridCandidateModels[k]} failed ({ex.Message})");
                }
            });

            // Survivor selection — SEQUENTIAL, fixed HybridCandidateModels index order (preserves deterministic tie-break).
            var survivors = new List<AlglibHyperbolicFitting>();
            var survivorModels = new List<HyperbolicFitModel>();
            var survivorClean = new List<List<ScatterErrorPoint>>();
            AlglibHyperbolicFitting tiltedFit = null;
            for (int k = 0; k < HybridCandidateModels.Length; ++k) {
                var fit = modelFits[k];
                if (fit == null) continue;
                if (HybridCandidateModels[k] == HyperbolicFitModel.TiltedHyperbola) tiltedFit = fit;
                var x = fit.Minimum.X;
                if (double.IsNaN(x) || double.IsInfinity(x)) continue;
                if (maxX >= minX && (x < minX || x > maxX)) continue;
                survivors.Add(fit);
                survivorModels.Add(HybridCandidateModels[k]);
                survivorClean.Add(cleaned);
            }

            rejectedPoints = consensus;

            if (survivors.Count == 0) {
                bestFit = tiltedFit;
                return HyperbolicFitModel.TiltedHyperbola;
            }

            // Rank ALL viable survivors (no gate yet — Task 3 adds it).
            var allIndices = Enumerable.Range(0, survivors.Count).ToList();
            int winner = RankBest(alglibAPI, survivors, survivorModels, survivorClean, allIndices, stepSize, useWeights, maxDegreeOfParallelism);
            bestFit = survivors[winner];
            return survivorModels[winner];
```

Then add this `RankBest` helper (extracted Tier-1/Tier-2 logic, operating on a supplied contender index list) immediately after the `SelectBestModel` overload, before `FitWithOutlierRejection`:

```csharp
        /// <summary>
        /// Ranks the given <paramref name="allowed"/> survivor indices by the standard tiering: Tier 1 = ascending
        /// finite σ(focus) (<see cref="MinimumStdError"/>); Tier 2 = ascending leave-one-out best-focus std (lazy,
        /// only when no allowed candidate has a finite σ(focus)); final tiebreak ascending <see cref="ReducedChiSquared"/>
        /// then descending <see cref="RSquared"/>. Returns the winning index into <paramref name="survivors"/>.
        /// <paramref name="allowed"/> must be non-empty.
        /// </summary>
        private static int RankBest(
                IAlglibAPI alglibAPI, List<AlglibHyperbolicFitting> survivors, List<HyperbolicFitModel> survivorModels,
                List<List<ScatterErrorPoint>> survivorClean, List<int> allowed, int stepSize, bool useWeights, int maxDegreeOfParallelism) {
            var tier1 = allowed.Where(i => {
                var se = survivors[i].MinimumStdError;
                return !double.IsNaN(se) && !double.IsInfinity(se);
            }).ToList();

            var primary = new Dictionary<int, double>();
            List<int> contenders;
            if (tier1.Count > 0) {
                foreach (var i in allowed) primary[i] = survivors[i].MinimumStdError;
                contenders = tier1;
            } else {
                contenders = new List<int>();
                foreach (var i in allowed) {
                    var loo = ComputeLeaveOneOutBestFocusStdError(alglibAPI, survivorModels[i], survivorClean[i], stepSize, useWeights, maxDegreeOfParallelism);
                    primary[i] = loo;
                    if (!double.IsNaN(loo) && !double.IsInfinity(loo)) contenders.Add(i);
                }
                if (contenders.Count == 0) {
                    foreach (var i in allowed) primary[i] = 0.0;
                    contenders = new List<int>(allowed);
                }
            }

            contenders.Sort((i, j) => {
                int c = primary[i].CompareTo(primary[j]);
                if (c != 0) return c;
                c = CompareAscNaNLast(survivors[i].ReducedChiSquared, survivors[j].ReducedChiSquared);
                if (c != 0) return c;
                return CompareAscNaNLast(-survivors[i].RSquared, -survivors[j].RSquared);
            });
            return contenders[0];
        }
```

(Confirm `using System.Collections.Generic;`, `using System.Linq;`, and `using System;` are present — they are.)

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~ModelFairRejectionTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/ModelFairRejectionTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "AF: consensus (model-fair) outlier rejection in SelectBestModel"
```

---

## Task 3: Significance-gated selection

Restrict the winning candidates to asymmetric models that pass the F-test against Symmetric; otherwise Symmetric wins.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs` (the ranking block at the end of `SelectBestModel`)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/ModelFairRejectionTests.cs` (add gate tests)

- [ ] **Step 1: Write the failing tests**

Append to `ModelFairRejectionTests.cs` (inside the class):

```csharp
        private static readonly HyperbolicFitModel[] Asymmetric = {
            HyperbolicFitModel.UnevenBlend, HyperbolicFitModel.TiltedHyperbola, HyperbolicFitModel.SmoothBlend,
        };

        // Genuinely tilted curve, tight errors: the asymmetric fit is significantly better ⇒ gate unlocks the
        // asymmetric models and one of them wins (Symmetric is excluded even if it has lower σ(focus)).
        [Test]
        public void Gate_ReportsAsymmetric_WhenAsymmetryIsSignificant() {
            var points = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.02, xStart: 760, xStep: 32, count: 15, errorY: 0.04);
            var chosen = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, InferStep(points), useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.90,
                out _, out _);
            Assert.That(Asymmetric, Does.Contain(chosen), "a significant tilt must be reported as an asymmetric model");
        }

        // Clean symmetric curve: no significant asymmetry ⇒ gate stays closed ⇒ Symmetric wins (parsimony).
        [Test]
        public void Gate_ReportsSymmetric_WhenNoSignificantAsymmetry() {
            var points = SyntheticFocusCurveSamples.SymmetricHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, xStart: 760, xStep: 32, count: 15, errorY: 0.05);
            var chosen = AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, points, InferStep(points), useWeights: true, maxOutlierRejections: 2, rejectionConfidence: 0.90,
                out _, out _);
            Assert.That(chosen, Is.EqualTo(HyperbolicFitModel.Symmetric));
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~ModelFairRejectionTests"`
Expected: FAIL — `Gate_ReportsAsymmetric_WhenAsymmetryIsSignificant` fails (without the gate, Symmetric's lower σ(focus) wins on the tilted curve).

- [ ] **Step 3: Insert the gate into the ranking block**

In `SelectBestModel`, replace the final ranking block added in Task 2:

```csharp
            // Rank ALL viable survivors (no gate yet — Task 3 adds it).
            var allIndices = Enumerable.Range(0, survivors.Count).ToList();
            int winner = RankBest(alglibAPI, survivors, survivorModels, survivorClean, allIndices, stepSize, useWeights, maxDegreeOfParallelism);
            bestFit = survivors[winner];
            return survivorModels[winner];
```

with:

```csharp
            // Significance gate: unlock the asymmetric (5-parameter) models only when at least one significantly
            // improves on the Symmetric (4-parameter) baseline (nested F-test at TiltSignificanceConfidence, on the
            // common cleaned set). Otherwise prefer parsimony (Symmetric). Requires a viable Symmetric baseline.
            int symIndex = survivorModels.IndexOf(HyperbolicFitModel.Symmetric);
            var allIndices = Enumerable.Range(0, survivors.Count).ToList();
            List<int> allowed = allIndices;
            if (symIndex >= 0) {
                double chiSym = survivors[symIndex].ChiSquared;
                int n = cleaned.Count;
                var justified = new List<int>();
                for (int i = 0; i < survivors.Count; ++i) {
                    if (survivorModels[i] == HyperbolicFitModel.Symmetric) continue;
                    if (IsAsymmetryJustified(chiSym, survivors[i].ChiSquared, n, TiltSignificanceConfidence)) {
                        justified.Add(i);
                    }
                }
                allowed = justified.Count > 0 ? justified : new List<int> { symIndex };
            }

            int winner = RankBest(alglibAPI, survivors, survivorModels, survivorClean, allowed, stepSize, useWeights, maxDegreeOfParallelism);
            bestFit = survivors[winner];
            return survivorModels[winner];
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~ModelFairRejectionTests"`
Expected: PASS (5 tests in this fixture).

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/StarDetection/AlglibHyperbolicFitting.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/ModelFairRejectionTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "AF: significance-gated model selection (F-test unlocks asymmetric models)"
```

---

## Task 4: Reconcile existing SelectBestModel tests with the new semantics

Two tests in `HybridModelSelectionTests.cs` encode the *old* behavior (per-model rejection; variance-only ranking that lets Symmetric win on a tilted curve). Update them to the consensus + gated semantics. The gross-outlier and cap tests (`SelectBestModel_RejectsGrossOutlier_AndExcludesItFromWinningFit`, `SelectBestModel_RespectsMaxOutlierRejectionCap`, `SelectBestModel_MaxRejectionsZero_MatchesLegacyOverload`) remain valid (a gross outlier is unanimous; max=0 rejects nothing) and must keep passing unchanged.

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/HybridModelSelectionTests.cs`

- [ ] **Step 1: Run the existing fixture to see what breaks**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~HybridModelSelectionTests"`
Expected: `SelectBestModel_PicksLowestExpectedError` FAILS (winner is now gated to the asymmetric set, so its σ(focus) need not equal the global-min σ(focus)); `SelectBestModel_RejectionMatchesWinningModelResiduals` may still pass but now mis-describes the behavior.

- [ ] **Step 2: Update `SelectBestModel_PicksLowestExpectedError` to expect the gated winner**

Replace that test method body's assertion section so it computes the minimum finite σ(focus) **among the justified asymmetric models on the common cleaned set** rather than across all candidates. Replace the whole method with:

```csharp
        // On a clean tilted curve where the asymmetry is significant, the gate restricts winners to the asymmetric
        // models; the winner has the lowest finite σ(focus) AMONG those (not necessarily the global minimum, which
        // may be Symmetric — deliberately excluded by the gate).
        [Test]
        public void SelectBestModel_PicksLowestExpectedError_AmongJustifiedAsymmetricModels() {
            var points = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.012, xStart: 880, xStep: 15, count: 17, errorY: 0.05);
            var stepSize = InferStep(points);

            var asymmetric = new[] { HyperbolicFitModel.UnevenBlend, HyperbolicFitModel.TiltedHyperbola, HyperbolicFitModel.SmoothBlend };
            var asymSigmas = new List<double>();
            foreach (var model in asymmetric) {
                var f = AlglibHyperbolicFitting.Create(alglibAPI, model, points, stepSize, useWeights: true);
                if (f.Solve() && !double.IsNaN(f.MinimumStdError) && !double.IsInfinity(f.MinimumStdError)) {
                    asymSigmas.Add(f.MinimumStdError);
                }
            }
            Assert.That(asymSigmas, Is.Not.Empty);
            var minAsymSigma = asymSigmas.Min();

            var chosen = AlglibHyperbolicFitting.SelectBestModel(alglibAPI, points, stepSize, useWeights: true, out var bestFit);

            Assert.Multiple(() => {
                Assert.That(asymmetric, Does.Contain(chosen), "a significant tilt must yield an asymmetric winner");
                Assert.That(bestFit, Is.Not.Null);
                Assert.That(bestFit.MinimumStdError, Is.EqualTo(minAsymSigma).Within(1e-6),
                    "winner has the lowest σ(focus) among the justified asymmetric models");
            });
        }
```

Note: the no-argument `SelectBestModel(... out bestFit)` overload (max=0) still runs consensus (removes nothing at max=0) and the gate. This test exercises the gate via that overload.

- [ ] **Step 3: Repurpose `SelectBestModel_RejectionMatchesWinningModelResiduals` to assert consensus semantics**

Replace that method with a test that proves the rejected set is the cross-model consensus (the point every model flags), not one model's private set:

```csharp
        // Rejection is the CONSENSUS across models: a gross outlier flagged by every candidate is removed; a point
        // flagged only by some models (e.g. a tilt wing) is not. Proven by checking the rejected point is flagged
        // by the Symmetric model AND by an asymmetric model on the full set.
        [Test]
        public void SelectBestModel_RejectionIsCrossModelConsensus() {
            var clean = SyntheticFocusCurveSamples.TiltedHyperbolaPoints(
                x0: 1000, y0: 2.0, a: 6.0, b: 12.0, s: 0.012, xStart: 880, xStep: 15, count: 17, errorY: 0.1);
            var stepSize = InferStep(clean);
            var outlierX = clean[5].X;
            var withOutlier = clean.Select(p => p.X == outlierX
                ? new ScatterErrorPoint(p.X, p.Y + 3.0, 0, p.ErrorY) : p).ToList();

            AlglibHyperbolicFitting.SelectBestModel(
                alglibAPI, withOutlier, stepSize, useWeights: true, maxOutlierRejections: 1, rejectionConfidence: 0.95,
                out _, out var rejects);

            Assert.That(rejects.Count, Is.EqualTo(1));
            Assert.That(rejects[0].X, Is.EqualTo(outlierX), "the unanimous gross outlier is the consensus rejection");

            // It is flagged independently by both a symmetric and an asymmetric fit on the full set (unanimous).
            foreach (var model in new[] { HyperbolicFitModel.Symmetric, HyperbolicFitModel.TiltedHyperbola }) {
                var f = AlglibHyperbolicFitting.Create(alglibAPI, model, withOutlier, stepSize, useWeights: true);
                Assume.That(f.Solve(), Is.True);
                var flagged = MathUtility.RejectionTest(withOutlier, f.Fitting, 0.95,
                    AlglibHyperbolicFitting.BuildResidualWeights(withOutlier, true));
                Assert.That(flagged?.X, Is.EqualTo(outlierX), $"{model} also flags the gross outlier");
            }
        }
```

- [ ] **Step 4: Run the fixture to verify it passes**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo --filter FullyQualifiedName~HybridModelSelectionTests"`
Expected: PASS (all methods, including the unchanged gross-outlier/cap/legacy tests).

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/StarDetection/HybridModelSelectionTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "AF tests: reconcile SelectBestModel tests with consensus + gated semantics"
```

---

## Task 5: Full regression + real-data verification

**Files:** none modified (verification only).

- [ ] **Step 1: Run the full unit suite**

Run: `cmd.exe /c "dotnet test Joko.NINA.Plugins\Joko.NINA.Plugins.sln -c Debug --nologo"`
Expected: PASS, count ≥ the prior 954 + the new tests (≈ 964). No failures. If `AsymmetricFitRobustnessTests` or `WeightChainIntegrationTests` regress, inspect — they may also assume per-model rejection; reconcile the same way as Task 4 (update the test to consensus semantics; do not weaken production logic to satisfy a stale test).

- [ ] **Step 2: Rebuild TestApp and reproduce the "uneven" run through the real (now patched) selection**

```bash
cmd.exe /c "dotnet build Joko.NINA.Plugins\TestApp\TestApp.csproj -c Debug --nologo"
./Joko.NINA.Plugins/TestApp/bin/Debug/net8.0-windows7.0/TestApp.exe af-fit \
  --af-run "C:\Workshop Data\Data\autofocus\uneven" --params-from initial --max-rejections 2 --out "C:\temp\hf-afdiag-verify"
```

Expected (from design §1 / §9): the per-model section still shows consensus outliers = **none**; F = 17–22 for the asymmetric models. The `af-fit` diagnostic prototypes the algorithm independently, so use it as the cross-check that production `SelectBestModel` now (a) returns an **empty** rejected set on this run and (b) returns an **asymmetric** winner (TiltedHyperbola). If you want a direct production-path assertion, add a one-off test that loads the 10 frames is out of scope (no XISF in the test project) — the diagnostic is the real-data oracle.

- [ ] **Step 3: Commit (if Step 2 produced any doc/diagnostic tweaks; otherwise skip)**

```bash
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --allow-empty --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "AF: verify model-fair rejection + tilt-aware selection on the uneven run"
```

---

## Self-Review

**1. Spec coverage:**
- §4 consensus rejection → Task 2. ✓
- §5 significance-gated selection (F-test, hardcoded 0.95, parsimony fallback, Symmetric-baseline + n−5≥1 guards) → Task 1 (helper + guards) and Task 3 (integration). ✓
- §6 no new option/UI/interface/signature → confirmed: only `AlglibHyperbolicFitting.cs` production change; `IsAsymmetryJustified` is `internal` (test access), `SelectBestModel` signature unchanged. ✓
- §4 guards (<2 models solve → no rejection; ≤3 points; max=0) → Task 2 body (`solvedCount >= 2`, `maxOutlierRejections > 0`; `FitWithOutlierRejection`/`RejectionTest` already no-op at ≤3 points). ✓ Covered by `Consensus_ZeroBudget_RemovesNothing` and the guard logic.
- §9 test plan (symmetric/no-outlier, symmetric+bad-frame, tilted/no-outlier, tilted+bad-frame, below-significance, F-test math, guards) → Tasks 1–4 cover all seven. ✓
- §9 diagnostic reproduction → Task 5 Step 2. ✓

**2. Placeholder scan:** No TBD/TODO; every code step shows complete code; every run step gives an exact command and expected outcome. ✓

**3. Type consistency:** `IsAsymmetryJustified(double, double, int, double)` defined in Task 1, called identically in Task 3 and tested in Task 1. `RankBest(IAlglibAPI, List<AlglibHyperbolicFitting>, List<HyperbolicFitModel>, List<List<ScatterErrorPoint>>, List<int>, int, bool, int)` defined and called in Task 2, reused in Task 3. `ChiSquared`, `MinimumStdError`, `ReducedChiSquared`, `RSquared`, `Minimum.X`, `Create`, `Solve`, `BuildResidualWeights`, `FitWithOutlierRejection`, `ComputeLeaveOneOutBestFocusStdError`, `HybridCandidateModels`, `ParallelExecution.ResolveDegreeOfParallelism` all exist on the current `AlglibHyperbolicFitting`. `MathUtility.RejectionTest` and `SyntheticFocusCurveSamples.{TiltedHyperbolaPoints,SymmetricHyperbolaPoints}` match existing test usage. ✓

---

## Execution Handoff

Plan complete and saved to `plans/autofocus-model-fair-rejection-and-tilt-aware-selection-plan.md`. Two execution options:

**1. Subagent-Driven (recommended)** — a fresh subagent per task, with review between tasks; fast iteration.

**2. Inline Execution** — execute tasks in this session via the executing-plans skill, batched with review checkpoints.

Which approach?
