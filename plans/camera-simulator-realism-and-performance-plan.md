# Camera Simulator — Realism & Performance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the synthetic camera fast enough to autofocus without timing out, give every exposure its own noise realization, and resolve aperture/focal length from the NINA profile with the implied value shown as hint text.

**Architecture:** Five independent changes to the existing render pipeline and options layer. The render stays a *pure function of `RenderRequest`* — that invariant is what makes both the prefetch (byte-identical pixels) and the per-exposure seed (varies via the request, not via hidden state) safe. Optics resolution moves out of the camera and into `CameraSimulatorOptions`, so the UI hint and the render read the **same** property and cannot disagree.

**Tech Stack:** C# / .NET 8, NUnit 4.4.0, WPF (`ninactrl:HintTextBox`), NINA plugin options via `PluginOptionsAccessor`.

---

## Background — measured evidence

From the user's NINA log (`20260716-141624`) and direct benchmarks on a 24-core box (Debug):

| Fact | Value |
|---|---|
| Autofocus outcome | **timed out after 10:00**, never completed |
| Per AF point | move 3s + expose 5s + **download ~35s** + detect 10s ≈ 53s |
| Full render, 61.2 MP (IMX455) | **13.1 s** |
| Same render, 5k vs 50k stars | 12.88s vs 13.11s → **stamping is not the cost** |
| `DevelopToAdu`, single-threaded | **~90% of the render** |
| `DevelopToAdu` @ λ=14.6 (user's sky) | ~12 s |
| `DevelopToAdu` @ λ=39 | **37.25 s** (609 ns/px) ← cliff just under the threshold |
| `DevelopToAdu` @ λ=60 | 3.78 s (62 ns/px) — Gaussian branch |
| Poisson multiplicative vs log-space | **1.9× faster, 0/200k draw mismatches** |
| Parallel over 24 stripes | 7.94s → **0.79s (10×)** |
| `exp(-λ)` underflow | only at **λ > 745**; threshold is 40, `exp(-40)`≈4.2e-18 |
| NINA profile `FocalLength`/`FocalRatio` default | **`NaN`** (3 of the user's 5 profiles) |

Two facts drive the design:

1. **`NaN` slips past both `x > 0` and `x <= 0`.** Every optics guard in the codebase is written `if (x <= 0) throw`, which *passes* NaN straight through into `N = f/D` and poisons the frame silently. Guards must be written `if (!(x > 0)) throw`.
2. **The stripe partition is part of the frame's identity** once each stripe has its own RNG. It must be a fixed constant, never `Environment.ProcessorCount`.

---

## File Structure

| File | Responsibility |
|---|---|
| **Create** `CameraSimulator/Rendering/SeedMixer.cs` | Combine integers into a decorrelated RNG seed. Used by the frame developer (per-stripe) and the camera (per-exposure). |
| **Create** `CameraSimulator/Rendering/FrameDeveloper.cs` | Parallel orchestration of frame development: fixed stripe partition + per-stripe seed derivation. Owns the determinism guarantee. |
| **Modify** `CameraSimulator/Rendering/NoiseGenerator.cs` | Multiplicative Poisson; expose `DevelopRange` so a stripe can develop a slice with its own RNG. Stays single-threaded and non-thread-safe by design. |
| **Modify** `CameraSimulator/Rendering/StarFieldCompositor.cs` | Call `FrameDeveloper` instead of a lone `NoiseGenerator`; update the concurrency doc. |
| **Modify** `CameraSimulator/Rendering/DefocusModel.cs` | NaN-safe optics guards. |
| **Modify** `CameraSimulator/Rendering/RadiometryCalculator.cs` | NaN-safe optics guards (mirror of the above). |
| **Modify** `CameraSimulator/CameraSimulatorOptions.cs` | `-1` = unset sentinel; `EffectiveApertureMillimeters` / `EffectiveFocalLengthMillimeters` resolution against the profile. |
| **Modify** `Interfaces/ICameraSimulatorOptions.cs` | Declare the two effective properties. |
| **Modify** `CameraSimulator/HocusFocusSimulatorCamera.cs` | Per-exposure seed; prefetch render at `StartExposure`; consume the effective optics. |
| **Modify** `CameraSimulator/SetupDialog/SetupDataTemplates.xaml` | `HintTextBox` for aperture + focal length. |
| **Modify** `Resources/OptionsDataTemplates.xaml` | Read-only rows show the *effective* value, never a bare `-1`. |
| **Modify** `docs/synthetic-camera-manual-smoke-test.md` | New checks for the above. |

Tests mirror the source tree under `Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/`.

---

## Task 1: SeedMixer

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/SeedMixer.cs`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/SeedMixerTests.cs`

Both callers combine *adjacent* integers (stripe 3 vs 4; exposure 7 vs 8; focuser 24979 vs 25000). `new Random(seed)` seeded with adjacent integers does **not** guarantee decorrelated streams, so a plain `seed + stripe` would risk visibly correlated noise between neighbouring stripes. The MurmurHash3 finalizer avalanches every input bit.

- [ ] **Step 1: Write the failing test**

```csharp
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class SeedMixerTests {

    [Test]
    public void Combine_IsDeterministic() {
        Assert.That(SeedMixer.Combine(42, 7, 3), Is.EqualTo(SeedMixer.Combine(42, 7, 3)));
    }

    [Test]
    public void Combine_IsOrderSensitive() {
        Assert.That(SeedMixer.Combine(1, 2), Is.Not.EqualTo(SeedMixer.Combine(2, 1)));
    }

    [Test]
    public void Combine_HasNoCollisionsAcrossARealisticExposureGrid() {
        // Every (focuser position, exposure counter) an autofocus run can produce.
        var seeds = new HashSet<int>();
        for (var focuser = 24_900; focuser <= 25_100; ++focuser) {
            for (var exposure = 0; exposure < 50; ++exposure) {
                seeds.Add(SeedMixer.Combine(42, focuser, exposure));
            }
        }
        Assert.That(seeds.Count, Is.EqualTo(201 * 50), "distinct inputs must not collide");
    }

    [Test]
    public void Combine_AvalanchesAdjacentInputs() {
        // Adjacent inputs (stripe i vs i+1) must differ in ~half their bits, not one.
        // Without avalanche a +1 input yields a +1 output and neighbouring stripes correlate.
        var distances = new List<int>();
        for (var i = 0; i < 64; ++i) {
            var a = (uint)SeedMixer.Combine(42, i);
            var b = (uint)SeedMixer.Combine(42, i + 1);
            distances.Add(System.Numerics.BitOperations.PopCount(a ^ b));
        }
        Assert.That(distances.Average(), Is.EqualTo(16.0).Within(5.0),
            "adjacent seeds should differ in roughly half of their 32 bits");
        Assert.That(distances.Min(), Is.GreaterThan(4), "no adjacent pair may be nearly identical");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~SeedMixerTests"`
Expected: FAIL — `error CS0103: The name 'SeedMixer' does not exist`.

- [ ] **Step 3: Write the implementation**

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

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Combines several integers into one well-distributed RNG seed.
    ///
    /// <para>Both callers combine <b>adjacent</b> integers and both need avalanche, not merely uniqueness.
    /// <see cref="FrameDeveloper"/> derives a per-stripe seed from (frame seed, stripe index) — stripes 3 and 4
    /// abut in the image, so correlated streams would show as a visible seam. The camera derives a per-exposure
    /// seed from (base seed, focuser position, exposure counter) — consecutive exposures differ by 1 in the
    /// counter and consecutive autofocus points by ~20 in the focuser position. <c>new Random(seed + 1)</c> does
    /// not guarantee a stream decorrelated from <c>new Random(seed)</c>; the MurmurHash3 finalizer spreads a
    /// one-bit input change across the whole output.</para>
    /// </summary>
    public static class SeedMixer {

        /// <summary>Combines values in order (order-sensitive) into a seed. Allocates — call per exposure/stripe, never per pixel.</summary>
        public static int Combine(params int[] values) {
            if (values == null) throw new ArgumentNullException(nameof(values));
            unchecked {
                var hash = 2166136261u; // FNV-1a offset basis
                foreach (var value in values) {
                    hash = Fmix32((hash ^ (uint)value) * 16777619u); // FNV-1a step, then avalanche
                }
                return (int)hash;
            }
        }

        /// <summary>MurmurHash3's 32-bit finalizer: spreads every input bit across every output bit.</summary>
        private static uint Fmix32(uint hash) {
            unchecked {
                hash ^= hash >> 16;
                hash *= 0x85ebca6bu;
                hash ^= hash >> 13;
                hash *= 0xc2b2ae35u;
                hash ^= hash >> 16;
                return hash;
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~SeedMixerTests"`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/SeedMixer.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/SeedMixerTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): add SeedMixer for decorrelated per-stripe/per-exposure seeds"
```

---

## Task 2: Multiplicative Poisson draw

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/NoiseGenerator.cs:73-83` (`NextPoisson`)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/NoiseGeneratorTests.cs` (append)

`NextPoisson` works in log space "so `e^(−λ)` cannot underflow", paying a `Math.Log` **per iteration** (~λ per pixel — the dominant cost of a 61 MP frame). That underflow cannot occur: this branch only runs below `PoissonToGaussianThreshold` (40), and a double does not underflow until λ > 745. Because `log` is monotonic, `Σlog(uᵢ) > −λ` and `Πuᵢ > e^(−λ)` are the same predicate, so the rewrite is a pure speedup: **measured 1.9×, 0 mismatches in 200k draws** at λ ∈ {0.5, 5, 14.62, 39.9}.

- [ ] **Step 1: Write the failing test** (append to `NoiseGeneratorTests.cs`, inside the existing fixture)

```csharp
    // Reference log-space implementation — the form NoiseGenerator shipped before the multiplicative rewrite.
    // Kept here so the rewrite is pinned against the exact algorithm it replaced, not against a re-derivation.
    private static long ReferencePoissonLogSpace(Random rng, double lambda) {
        if (lambda <= 0.0) return 0L;
        var target = -lambda;
        var logProduct = 0.0;
        var k = 0L;
        do {
            ++k;
            logProduct += Math.Log(1.0 - rng.NextDouble());
        } while (logProduct > target);
        return k - 1;
    }

    [Test]
    [TestCase(0.5)]
    [TestCase(5.0)]
    [TestCase(14.62)]
    [TestCase(39.9)]
    public void PoissonDraw_MatchesLegacyLogSpaceFormExactly(double lambda) {
        // Same seed => same uniform sequence => the two forms must agree draw for draw, because
        // sum(log(u)) > -lambda and prod(u) > exp(-lambda) are the same predicate.
        const int seed = 20260716;
        const int draws = 50_000;

        var reference = new Random(seed);
        var expected = new long[draws];
        for (var i = 0; i < draws; ++i) {
            expected[i] = ReferencePoissonLogSpace(reference, lambda);
        }

        var generator = new NoiseGenerator(seed);
        var actual = new long[draws];
        for (var i = 0; i < draws; ++i) {
            actual[i] = generator.NextPoissonForTest(lambda);
        }

        Assert.That(actual, Is.EqualTo(expected), $"multiplicative form must reproduce the log-space draws at lambda={lambda}");
    }

    [Test]
    public void PoissonToGaussianThreshold_LeavesHugeUnderflowHeadroom() {
        // The multiplicative form is only safe because exp(-lambda) cannot reach zero on this branch.
        Assert.That(Math.Exp(-NoiseGenerator.PoissonToGaussianThreshold), Is.GreaterThan(0.0));
        Assert.That(Math.Exp(-NoiseGenerator.PoissonToGaussianThreshold), Is.EqualTo(4.24e-18).Within(1e-19));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~NoiseGeneratorTests"`
Expected: FAIL — `error CS1061: 'NoiseGenerator' does not contain a definition for 'NextPoissonForTest'`.

- [ ] **Step 3: Rewrite `NextPoisson` and add the test seam**

Replace `NoiseGenerator.cs:68-83` with:

```csharp
        /// <summary>
        /// One Poisson draw for mean <paramref name="lambda"/> using Knuth's multiplicative algorithm: one
        /// <see cref="Math.Exp"/> up front, then one multiply per iteration. Returns 0 for λ ≤ 0.
        ///
        /// <para>This deliberately does <b>not</b> work in log space. Log space costs a <see cref="Math.Log"/>
        /// per iteration — about λ of them per pixel, which at 61 MP is the single dominant cost of a frame — to
        /// guard against <c>e^(−λ)</c> underflowing to zero. That cannot happen here: this branch only runs below
        /// <see cref="PoissonToGaussianThreshold"/> (40), <c>e^(−40)</c> ≈ 4.2e−18, and a double does not
        /// underflow until λ > 745. The guard was buying nothing and costing ~2× (measured).</para>
        ///
        /// <para>The two forms are equivalent, not merely similar: <c>log</c> is monotonic, so
        /// <c>Σ log(uᵢ) &gt; −λ</c> and <c>Π uᵢ &gt; e^(−λ)</c> are the same predicate over the same uniforms.
        /// Pinned draw-for-draw against the previous implementation by
        /// <c>PoissonDraw_MatchesLegacyLogSpaceFormExactly</c>.</para>
        /// </summary>
        private long NextPoisson(double lambda) {
            if (lambda <= 0.0) return 0L;
            var threshold = Math.Exp(-lambda); // > 0 for every λ this branch sees; see remarks
            var product = 1.0;
            var k = 0L;
            do {
                ++k;
                product *= 1.0 - rng.NextDouble(); // uniform in (0,1]
            } while (product > threshold);
            return k - 1;
        }

        /// <summary>Test seam for <see cref="NextPoisson"/> — the shot-noise draw is otherwise only reachable
        /// through a whole frame, which cannot pin the draw sequence itself.</summary>
        internal long NextPoissonForTest(double lambda) => NextPoisson(lambda);
```

Also update the class-level `<summary>` code block: `ne  = λ_e < 1000 ? Poisson(λ_e) : ...` is stale (the constant has been 40 since Phase 3b). Change to `λ_e < 40`.

- [ ] **Step 4: Make the internal seam visible to the test project**

Verify `Joko.NINA.Plugins.HocusFocus/Joko.NINA.Plugins.HocusFocus.csproj` already has an `InternalsVisibleTo` for the test assembly (the camera's `internal` constructor is used by existing tests, so it must). If absent, add to the csproj:

```xml
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>NINA.Joko.Plugins.HocusFocus.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~NoiseGeneratorTests"`
Expected: PASS — all pre-existing tests plus 5 new ones. `MeanAndVariance_MatchPhysicalModel` and `PhotonTransfer_SlopeIsInverseGain_InterceptIsReadNoiseSquared` must still pass unchanged (the distribution is untouched).

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/NoiseGenerator.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/NoiseGeneratorTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "perf(camera-sim): drop per-iteration Math.Log from the Poisson draw (1.9x, bit-identical)"
```

---

## Task 3: Parallel frame development

**Files:**
- Create: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/FrameDeveloper.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/NoiseGenerator.cs` (add `DevelopRange`)
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/StarFieldCompositor.cs:163-166`
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/FrameDeveloperTests.cs`

`NoiseGenerator` stays exactly what it is — a single-threaded, non-thread-safe RNG wrapper. `FrameDeveloper` is the orchestrator that owns the partition and the seeding. Splitting them this way keeps every existing `NoiseGenerator` test valid (they still exercise the single-generator path) and puts the determinism guarantee in one place.

- [ ] **Step 1: Write the failing test**

```csharp
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Tests.CameraSimulator;

[TestFixture]
public class FrameDeveloperTests {

    private static SensorDefinition Sensor => SensorRegistry.Get(SonySensorModel.IMX533);

    private static float[] Flat(int n, float lambda) {
        var a = new float[n];
        Array.Fill(a, lambda);
        return a;
    }

    [Test]
    public void SameSeed_ProducesIdenticalFrame() {
        var acc = Flat(200_000, 14.62f);
        var a = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 4242, CancellationToken.None);
        var b = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 4242, CancellationToken.None);
        Assert.That(a, Is.EqualTo(b), "identical seed => identical frame");
    }

    [Test]
    public void DifferentSeed_ProducesDifferentFrame() {
        var acc = Flat(200_000, 14.62f);
        var a = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 1, CancellationToken.None);
        var b = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 2, CancellationToken.None);
        Assert.That(a, Is.Not.EqualTo(b));
    }

    // THE load-bearing test for this task. The stripe partition selects which RNG stream lands on which
    // pixel, so it is part of the frame's identity. If StripeCount ever becomes ProcessorCount-derived, the
    // same seed renders differently on different machines and the plugin's determinism claim silently breaks.
    [Test]
    public void StripePartition_DoesNotDependOnProcessorCount() {
        Assert.That(FrameDeveloper.StripeCount, Is.EqualTo(64),
            "the partition must be a fixed constant, never Environment.ProcessorCount");
    }

    [Test]
    public void Mean_MatchesPoissonModel_AcrossStripeBoundaries() {
        // Every stripe must actually develop its slice: an off-by-one in the partition would leave a band
        // of zeros, which a mean check over the whole frame catches.
        const float lambda = 14.62f;
        var acc = Flat(1_000_000, lambda);
        var frame = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 99, CancellationToken.None);
        var electronsPerAdu = Sensor.ElectronsPerAduAtGain(100);
        var mean = frame.Select(v => (v - 500) * electronsPerAdu).Average();
        Assert.That(mean, Is.EqualTo(lambda).Within(0.05 * lambda));
        Assert.That(frame.Count(v => v == 0), Is.Zero, "a zero band would mean a stripe was skipped");
    }

    [Test]
    public void ShorterThanStripeCount_DevelopsEveryPixel() {
        // len < StripeCount makes some stripes empty; they must be skipped, not throw or corrupt.
        var acc = Flat(7, 200.0f);
        var frame = FrameDeveloper.DevelopToAdu(acc, Sensor, 100, 500, 5, CancellationToken.None);
        Assert.That(frame.Length, Is.EqualTo(7));
        Assert.That(frame.All(v => v > 500), "every pixel should carry signal above the pedestal");
    }

    [Test]
    public void Cancellation_IsObserved() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            FrameDeveloper.DevelopToAdu(Flat(200_000, 14.62f), Sensor, 100, 500, 1, cts.Token));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~FrameDeveloperTests"`
Expected: FAIL — `error CS0103: The name 'FrameDeveloper' does not exist`.

- [ ] **Step 3: Extract `DevelopRange` on `NoiseGenerator`**

Replace the body of `DevelopToAdu(float[], ushort[], SensorDefinition, int, int)` (`NoiseGenerator.cs:101-126`) with a delegation, and add the range overload:

```csharp
        /// <summary>
        /// Develops the electron accumulator into a caller-provided ADU buffer (same length), avoiding an
        /// allocation. See <see cref="DevelopToAdu(float[], SensorDefinition, int, int)"/>.
        /// </summary>
        public void DevelopToAdu(float[] electronAccumulator, ushort[] output, SensorDefinition sensor, int gain, int biasPedestalAdu) {
            if (electronAccumulator == null) throw new ArgumentNullException(nameof(electronAccumulator));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (output.Length != electronAccumulator.Length) throw new ArgumentException("Output length must match the accumulator length.", nameof(output));
            DevelopRange(electronAccumulator, output, 0, electronAccumulator.Length, sensor, gain, biasPedestalAdu);
        }

        /// <summary>
        /// Develops the half-open index range <c>[from, to)</c> of the accumulator into the same range of
        /// <paramref name="output"/>, drawing from <b>this</b> generator's RNG.
        ///
        /// <para>This is the seam <see cref="FrameDeveloper"/> parallelizes over: one generator per stripe, each
        /// with its own seed, each writing a disjoint index range. This type remains non-thread-safe — a single
        /// instance must never be handed to two threads.</para>
        /// </summary>
        public void DevelopRange(float[] electronAccumulator, ushort[] output, int from, int to, SensorDefinition sensor, int gain, int biasPedestalAdu) {
            if (electronAccumulator == null) throw new ArgumentNullException(nameof(electronAccumulator));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (sensor == null) throw new ArgumentNullException(nameof(sensor));
            if (output.Length != electronAccumulator.Length) throw new ArgumentException("Output length must match the accumulator length.", nameof(output));
            if (from < 0 || to > electronAccumulator.Length || from > to) throw new ArgumentOutOfRangeException(nameof(from));

            var fullWell = sensor.FullWellElectrons;
            var readNoise = sensor.ReadNoiseElectronsAtGain(gain);
            var electronsPerAdu = sensor.ElectronsPerAduAtGain(gain);
            var maxAdu = sensor.MaxAdu;

            for (var i = from; i < to; ++i) {
                double lambda = electronAccumulator[i];
                double ne = lambda < PoissonToGaussianThreshold
                    ? NextPoisson(lambda)
                    : Math.Round(NextGaussian(lambda, Math.Sqrt(lambda)));
                if (ne < 0.0) ne = 0.0;
                if (ne > fullWell) ne = fullWell;

                var electrons = ne + NextGaussian(0.0, readNoise);
                var adu = (long)Math.Round(electrons / electronsPerAdu) + biasPedestalAdu;
                if (adu < 0) adu = 0;
                else if (adu > maxAdu) adu = maxAdu;
                output[i] = (ushort)adu;
            }
        }
```

Update the class `<summary>`'s thread-safety paragraph to point at `FrameDeveloper` as the sanctioned parallel path.

- [ ] **Step 4: Write `FrameDeveloper`**

```csharp
#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Develops a per-pixel electron accumulator into an ADU frame in parallel.
    ///
    /// <para>Development is the dominant cost of a frame — at 61 MP it is ~90% of the render (measured ~12 s of
    /// a ~13 s render), because every pixel draws shot noise and read noise. It used to run single-threaded so
    /// that one seeded <see cref="NoiseGenerator"/> produced a reproducible frame. This type keeps that
    /// reproducibility while using every core: the frame is cut into <see cref="StripeCount"/> disjoint index
    /// ranges, and each stripe develops with its <b>own</b> generator seeded from (frame seed, stripe index).
    /// Disjoint ranges mean no shared RNG and no shared writes, so the result is independent of the order the
    /// stripes happen to run in.</para>
    /// </summary>
    public static class FrameDeveloper {

        /// <summary>
        /// The number of disjoint index ranges the frame is cut into.
        ///
        /// <para><b>This is a fixed constant and must never become <see cref="Environment.ProcessorCount"/>.</b>
        /// Each stripe draws from its own RNG stream, so the partition decides which stream lands on which pixel
        /// — the partition is part of the frame's identity, not an execution detail. A core-count-derived
        /// partition would make the same seed render differently on different machines, silently breaking the
        /// "bit-deterministic for a fixed seed regardless of core count" guarantee this plugin already makes for
        /// the stamping stage. The <i>degree of parallelism</i> is free to vary with the machine; the
        /// <i>partition</i> is not. 64 sits above any core count we expect (so every core stays fed even when the
        /// stripes finish unevenly) and is small enough that per-stripe generator setup is noise.</para>
        /// </summary>
        public const int StripeCount = 64;

        /// <summary>Develops the accumulator into a freshly allocated ADU frame. See <see cref="DevelopToAdu(float[], ushort[], SensorDefinition, int, int, int, CancellationToken)"/>.</summary>
        public static ushort[] DevelopToAdu(float[] electronAccumulator, SensorDefinition sensor, int gain, int biasPedestalAdu, int seed, CancellationToken token) {
            if (electronAccumulator == null) throw new ArgumentNullException(nameof(electronAccumulator));
            var output = new ushort[electronAccumulator.Length];
            DevelopToAdu(electronAccumulator, output, sensor, gain, biasPedestalAdu, seed, token);
            return output;
        }

        /// <summary>
        /// Develops the accumulator into a caller-provided ADU buffer of the same length. Deterministic for a
        /// given (<paramref name="seed"/>, length) regardless of core count or scheduling.
        /// </summary>
        public static void DevelopToAdu(float[] electronAccumulator, ushort[] output, SensorDefinition sensor, int gain, int biasPedestalAdu, int seed, CancellationToken token) {
            if (electronAccumulator == null) throw new ArgumentNullException(nameof(electronAccumulator));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (sensor == null) throw new ArgumentNullException(nameof(sensor));
            if (output.Length != electronAccumulator.Length) throw new ArgumentException("Output length must match the accumulator length.", nameof(output));

            token.ThrowIfCancellationRequested();
            var length = electronAccumulator.Length;
            var options = new ParallelOptions { CancellationToken = token };

            Parallel.For(0, StripeCount, options, stripe => {
                // Boundaries come from (stripe, StripeCount, length) alone — never from the thread count.
                var from = (int)((long)stripe * length / StripeCount);
                var to = (int)((long)(stripe + 1) * length / StripeCount);
                if (from >= to) {
                    return; // frames shorter than StripeCount leave trailing stripes empty
                }
                options.CancellationToken.ThrowIfCancellationRequested();
                new NoiseGenerator(SeedMixer.Combine(seed, stripe))
                    .DevelopRange(electronAccumulator, output, from, to, sensor, gain, biasPedestalAdu);
            });
        }
    }
}
```

- [ ] **Step 5: Point the compositor at it**

In `StarFieldCompositor.Render`, replace lines 163-165:

```csharp
            // Single-threaded development so the seeded noise is deterministic (NoiseGenerator is not thread-safe).
            return new NoiseGenerator(request.NoiseSeed)
                .DevelopToAdu(accumulator, sensor, request.Gain, request.BiasPedestalAdu);
```

with:

```csharp
            // Development is ~90% of a 61 MP render, so it runs in parallel — deterministically, via a fixed
            // stripe partition with one seeded generator per stripe. See FrameDeveloper.StripeCount.
            return FrameDeveloper.DevelopToAdu(
                accumulator, sensor, request.Gain, request.BiasPedestalAdu, request.NoiseSeed, token);
```

Update the class `<summary>`'s concurrency paragraph — the sentence "Development then runs single-threaded over the whole accumulator with one `NoiseGenerator`, so the noise is deterministic" is now false. Replace with a pointer to `FrameDeveloper` and its fixed partition.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~FrameDeveloperTests|FullyQualifiedName~NoiseGeneratorTests|FullyQualifiedName~StarFieldCompositorTests"`
Expected: PASS. `StarFieldCompositorTests.Render_IsDeterministicForFixedSeed` must still pass — it is now testing the parallel path.

- [ ] **Step 7: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/FrameDeveloper.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/NoiseGenerator.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/StarFieldCompositor.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/FrameDeveloperTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "perf(camera-sim): develop frames in parallel over a fixed, deterministic stripe partition"
```

---

## Task 4: Per-exposure noise seed

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs` (`StartExposure`, `BuildRenderRequest`, `Connect`)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs` (append)

`NoiseSeed` is a constant 42, so every exposure at the same focuser position and pointing is byte-identical — the user's profile averages 2 frames per autofocus point (`MeasurementAverageCount = 2`), which currently averages a frame with *itself*. Mixing the focuser position alone would not fix that (repeat exposures at one point share a position); the exposure counter is what separates them.

The seed is mixed **into the request**, not read from hidden state inside `Render`. That keeps `Render` a pure function of `RenderRequest` — which every existing test relies on, and which Task 5's prefetch requires.

**Fixture facts (verified — do not re-derive):** `HocusFocusSimulatorCameraTests` already has `BuildOptions()` (returns a **real** `CameraSimulatorOptions` over an `InMemoryPluginOptionsAccessor` + substituted profile), `BuildCamera(options, focuser, telescope)`, and `BuildCameraWithCompositor(options, compositor, exposureDataFactory, focuser, telescope)`. Compositors are `Substitute.For<IStarFieldCompositor>()`. The fixture's existing test at `:339-358` already captures a `RenderRequest` off a substituted compositor — **copy that capture idiom rather than inventing a recorder.**

- [ ] **Step 1: Write the failing test** (append to the existing fixture)

> **Ordering matters here.** This task lands *before* the prefetch, so `compositor.Render` is still only reached
> via `DownloadExposure` — a test that calls `StartExposure` alone would capture nothing and fail for the wrong
> reason. Each test below therefore runs a full expose→download cycle. That keeps the test honest after Task 5
> moves the render earlier, since both paths still funnel through the same request.

```csharp
    /// <summary>Runs one full expose→download cycle and returns the RenderRequest the compositor was handed.</summary>
    private static async Task<List<RenderRequest>> CaptureRequests(
            CameraSimulatorOptions options, int focuserPosition, int exposures) {
        var captured = new List<RenderRequest>();
        options.SensorModel = SonySensorModel.IMX533; // smallest sensor: keeps the fake render array small
        var compositor = Substitute.For<IStarFieldCompositor>();
        compositor.Render(Arg.Any<RenderRequest>(), Arg.Any<CancellationToken>())
                  .Returns(call => {
                      captured.Add(call.Arg<RenderRequest>());
                      return new ushort[3008 * 3008];
                  });

        var camera = BuildCameraWithCompositor(
            options, compositor, Substitute.For<IExposureDataFactory>(),
            FocuserAt(focuserPosition), ConnectedTelescope());
        await camera.Connect(CancellationToken.None);

        for (var i = 0; i < exposures; ++i) {
            camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });
            await camera.WaitUntilExposureIsReady(CancellationToken.None);
            await camera.DownloadExposure(CancellationToken.None);
        }
        return captured;
    }

    [Test]
    public async Task RepeatedExposures_AtOneFocuserPosition_GetDifferentSeeds() {
        // MeasurementAverageCount > 1 averages several frames per autofocus point. With a constant seed those
        // frames were byte-identical, so the averaging was averaging a frame with itself. The focuser position
        // alone cannot fix this — repeat exposures share a position — so the counter is what separates them.
        var options = BuildOptions();
        options.NoiseSeed = 42;
        var captured = await CaptureRequests(options, focuserPosition: 25000, exposures: 2);

        Assert.That(captured, Has.Count.EqualTo(2));
        Assert.That(captured[1].NoiseSeed, Is.Not.EqualTo(captured[0].NoiseSeed),
            "the exposure counter must advance the seed between exposures at one position");
    }

    [Test]
    public async Task ExposuresAtDifferentFocuserPositions_GetDifferentSeeds() {
        var near = await CaptureRequests(BuildOptions(), focuserPosition: 24979, exposures: 1);
        var far = await CaptureRequests(BuildOptions(), focuserPosition: 25000, exposures: 1);
        Assert.That(near[0].NoiseSeed, Is.Not.EqualTo(far[0].NoiseSeed), "the focuser position must feed the seed");
    }

    [Test]
    public async Task NoiseSeedOption_StillChangesTheFrameSeed() {
        // The option must remain the BASE seed — otherwise a user setting it would have no effect at all.
        var a = BuildOptions(); a.NoiseSeed = 42;
        var b = BuildOptions(); b.NoiseSeed = 43;
        var first = await CaptureRequests(a, focuserPosition: 25000, exposures: 1);
        var second = await CaptureRequests(b, focuserPosition: 25000, exposures: 1);
        Assert.That(first[0].NoiseSeed, Is.Not.EqualTo(second[0].NoiseSeed));
    }

    [Test]
    public async Task ReconnectingResetsTheCounter_SoASessionReplaysIdentically() {
        // The counter resets on Connect, which is what keeps a run reproducible from the base seed.
        var options = BuildOptions();
        options.NoiseSeed = 42;
        var first = await CaptureRequests(options, focuserPosition: 25000, exposures: 2);
        var second = await CaptureRequests(options, focuserPosition: 25000, exposures: 2);
        Assert.That(second.ConvertAll(r => r.NoiseSeed), Is.EqualTo(first.ConvertAll(r => r.NoiseSeed)));
    }
```

> `FocuserAt(int position)` and `ConnectedTelescope()` are small helpers over `Substitute.For<IFocuserMediator>()` / `ITelescopeMediator` whose `GetInfo()` returns `new FocuserInfo { Connected = true, Position = position }` / `new TelescopeInfo { Connected = true }`. The fixture's connected-device tests already build these inline — extract them into private helpers as part of this step so Task 5 can reuse them.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~HocusFocusSimulatorCameraTests"`
Expected: FAIL — all three new tests, every captured `NoiseSeed` being the constant `42`.

- [ ] **Step 3: Add the counter and mix the seed**

Add the field next to the other exposure-state fields (`HocusFocusSimulatorCamera.cs:465-467`):

```csharp
        // Advanced once per StartExposure so repeated exposures at one focuser position get their own noise.
        // Reset on Connect, so replaying a session from the same base NoiseSeed reproduces it frame for frame.
        private int exposureCounter;
```

In `Connect` (`:171-175`), add `exposureCounter = 0;` beside `CameraState = CameraStates.Idle;`.

In `StartExposure`, advance it before building the request:

```csharp
            unchecked { ++exposureCounter; }
            pendingRender = BuildRenderRequest(exposureLengthSeconds);
```

In `BuildRenderRequest`, replace `NoiseSeed = options.NoiseSeed,` with:

```csharp
                // The option is the BASE seed, not the frame seed. Mixing in the focuser position and a
                // per-exposure counter gives every frame its own noise while keeping the whole session
                // reproducible from the base seed. It stays a property of the REQUEST rather than of the
                // compositor, so Render remains a pure function of its request — which is what lets the render
                // be started early (see StartExposure) without changing a single pixel.
                NoiseSeed = SeedMixer.Combine(options.NoiseSeed, focuserPosition, exposureCounter),
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~HocusFocusSimulatorCameraTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): give every exposure its own noise realization"
```

---

## Task 5: Prefetch the render at StartExposure

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs` (`StartExposure`, `DownloadExposure`, `StopExposure`, `AbortExposure`, `Disconnect`)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs` (append)

`BuildRenderRequest` already snapshots everything `Render` reads, and `Render` touches no ambient state, so rendering at `StartExposure` yields byte-identical pixels — this only moves the work under the exposure wait. The prefetch is **skipped when a required device is missing**, so `DownloadExposure`'s descriptive guards still fire with their exact messages and no cores are burned on a frame nobody can use.

- [ ] **Step 1: Write the failing test**

Add this fake to the fixture — a substituted compositor cannot express "started, now blocking":

```csharp
    /// <summary>Records when Render begins and blocks there until cancelled, so a test can observe that the
    /// render is already in flight while the camera is still exposing.</summary>
    private sealed class BlockingCompositor : IStarFieldCompositor {
        private readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim cancelled = new(false);

        public Task Started => started.Task;
        public int RenderCount;

        public bool WaitForCancellation(TimeSpan timeout) => cancelled.Wait(timeout);

        public ushort[] Render(RenderRequest request, CancellationToken token) {
            Interlocked.Increment(ref RenderCount);
            started.TrySetResult(true);
            using (token.Register(() => cancelled.Set())) {
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            }
            token.ThrowIfCancellationRequested();
            return new ushort[1];
        }
    }
```

```csharp
    [Test]
    public async Task Render_StartsDuringTheExposure_NotAtDownload() {
        var compositor = new BlockingCompositor();
        var camera = BuildCameraWithCompositor(
            BuildOptions(), compositor, Substitute.For<IExposureDataFactory>(),
            FocuserAt(25000), ConnectedTelescope());
        await camera.Connect(CancellationToken.None);

        camera.StartExposure(new CaptureSequence { ExposureTime = 30.0 });

        // The render must already be running while the camera is still nominally exposing. Before this change
        // Render was not called until DownloadExposure, so this waits out the timeout.
        var winner = await Task.WhenAny(compositor.Started, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.That(winner, Is.SameAs(compositor.Started), "the render must begin at StartExposure, not at DownloadExposure");
        camera.AbortExposure();
    }

    [Test]
    public async Task AbortExposure_CancelsTheInFlightRender() {
        var compositor = new BlockingCompositor();
        var camera = BuildCameraWithCompositor(
            BuildOptions(), compositor, Substitute.For<IExposureDataFactory>(),
            FocuserAt(25000), ConnectedTelescope());
        await camera.Connect(CancellationToken.None);

        camera.StartExposure(new CaptureSequence { ExposureTime = 30.0 });
        await compositor.Started;
        camera.AbortExposure();

        Assert.That(compositor.WaitForCancellation(TimeSpan.FromSeconds(5)), Is.True,
            "aborting must cancel the prefetched render rather than leave every core busy on a dead frame");
    }

    [Test]
    public async Task DownloadWithoutFocuser_StillThrowsTheDescriptiveError_AndNeverRenders() {
        var compositor = new BlockingCompositor();
        var focuser = Substitute.For<IFocuserMediator>();
        focuser.GetInfo().Returns(new FocuserInfo { Connected = false });
        var camera = BuildCameraWithCompositor(
            BuildOptions(), compositor, Substitute.For<IExposureDataFactory>(),
            focuser, ConnectedTelescope());
        await camera.Connect(CancellationToken.None);

        camera.StartExposure(new CaptureSequence { ExposureTime = 0.0 });

        var ex = Assert.ThrowsAsync<CameraExposureFailedException>(() => camera.DownloadExposure(CancellationToken.None));
        Assert.That(ex.Message, Does.Contain("no focuser is connected"), "the descriptive guard must survive prefetching");
        Assert.That(compositor.RenderCount, Is.Zero, "a frame nobody can use must never be rendered");
    }
```

The byte-identity argument is covered where it is cheapest to assert — the compositor, not the camera:

```csharp
    // In StarFieldCompositorTests. Prefetching is only safe because Render is a pure function of its request:
    // same request => same pixels, no matter when or on which thread it runs.
    [Test]
    public void Render_IsPure_SameRequestRendersIdenticallyOnAnyThread() {
        var request = SyntheticCameraTestScene.BuildRequest();
        var compositor = SyntheticCameraTestScene.BuildCompositor();
        var inline = compositor.Render(request, CancellationToken.None);
        var offThread = Task.Run(() => compositor.Render(request, CancellationToken.None)).GetAwaiter().GetResult();
        Assert.That(offThread, Is.EqualTo(inline));
    }
```

> `SyntheticCameraTestScene` already holds the fixed scene (`ApertureMillimeters = 130`, `FocalLengthMillimeters = 910`) and a request builder — reuse it; adjust the two member names to whatever it actually exposes.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~HocusFocusSimulatorCameraTests"`
Expected: FAIL — `Render_StartsDuringTheExposure_NotAtDownload` times out (the render only begins at download).

- [ ] **Step 3: Add the prefetch fields**

Beside `pendingRender` (`HocusFocusSimulatorCamera.cs:465`):

```csharp
        private Task<ushort[]> pendingRenderTask;
        private CancellationTokenSource renderCts;
```

- [ ] **Step 4: Start the render in `StartExposure`**

Replace `StartExposure` (`:469-478`) with:

```csharp
        public void StartExposure(CaptureSequence sequence) {
            if (!Connected) {
                throw new InvalidOperationException("Cannot start an exposure: the synthetic camera is not connected.");
            }

            CancelPendingRender();
            exposureStartTime = DateTime.UtcNow;
            exposureLengthSeconds = sequence?.ExposureTime ?? 0.0;
            unchecked { ++exposureCounter; }
            var request = BuildRenderRequest(exposureLengthSeconds);
            pendingRender = request;

            // Render NOW rather than in DownloadExposure. BuildRenderRequest has already snapshotted everything
            // Render reads, and Render touches no ambient state, so the pixels are byte-identical either way —
            // this only moves ~13 s of work (61 MP) out from after the exposure and under it. Skipped when a
            // required device is missing so DownloadExposure's descriptive guards still fire, unchanged, without
            // burning cores on a frame nobody can use.
            if (request.FocuserConnected && request.TelescopeConnected) {
                var cts = new CancellationTokenSource();
                var token = cts.Token;
                renderCts = cts;
                var task = Task.Run(() => compositor.Render(request, token), token);
                pendingRenderTask = task;
                // Observe the fault even if the exposure is aborted and nobody ever awaits this task, so a render
                // failure cannot resurface later as an unobserved TaskException.
                _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            }

            CameraState = CameraStates.Exposing;
        }

        /// <summary>
        /// Cancels any prefetched render and drops the references.
        ///
        /// <para>The CTS is deliberately not disposed: it carries no timer and no registered wait handle, so the
        /// GC reclaims it, whereas disposing here would race the render task that is still observing its token
        /// (<c>ThrowIfCancellationRequested</c> on a disposed source throws <see cref="ObjectDisposedException"/>,
        /// which would surface as a spurious exposure failure).</para>
        /// </summary>
        private void CancelPendingRender() {
            var cts = renderCts;
            renderCts = null;
            pendingRenderTask = null;
            cts?.Cancel();
        }
```

- [ ] **Step 5: Await the prefetched render in `DownloadExposure`**

Inside the `try` in `DownloadExposure`, replace the render line (`:549`):

```csharp
                var pixels = await Task.Run(() => compositor.Render(request, token), token).ConfigureAwait(false);
```

with:

```csharp
                // Normally already running since StartExposure; the inline fallback covers a caller that
                // downloads an exposure whose prefetch was cancelled out from under it.
                ushort[] pixels;
                var prefetched = pendingRenderTask;
                if (prefetched != null) {
                    // Route the download's cancellation into the render that is actually doing the work, rather
                    // than just abandoning the await and leaving 24 cores busy on a frame nobody wants.
                    var cts = renderCts;
                    using (token.Register(() => cts?.Cancel())) {
                        pixels = await prefetched.ConfigureAwait(false);
                    }
                } else {
                    pixels = await Task.Run(() => compositor.Render(request, token), token).ConfigureAwait(false);
                }
```

Leave the two disconnected guards and the `catch` block exactly as they are.

- [ ] **Step 6: Cancel on stop / abort / disconnect**

In `StopExposure` and `AbortExposure` (`:496-508`), add `CancelPendingRender();` beside the existing `pendingRender = null;`. In `Disconnect` (`:177-180`), add `CancelPendingRender();` beside `pendingRender = null;`.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~HocusFocusSimulatorCameraTests"`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/HocusFocusSimulatorCameraTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "perf(camera-sim): start the render at StartExposure so it overlaps the exposure"
```

---

## Task 6: Effective aperture / focal length

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/CameraSimulatorOptions.cs`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/ICameraSimulatorOptions.cs:89-90`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs:593-604`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/DefocusModel.cs:113-119`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/RadiometryCalculator.cs` (guards)
- Test: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/CameraSimulatorOptionsTests.cs` (append)

The resolution moves **into the options object** so the hint text and the render read the same property. That is the whole point: a hint computed separately from the render is a hint that can lie.

Resolution chain (`-1`, or any non-positive, means "unset" — the plugin's existing convention, per `DoubleNegativeToEmptyStringConverter`):

| | 1st | 2nd | 3rd |
|---|---|---|---|
| **Focal length** | the option, if > 0 | profile `TelescopeSettings.FocalLength`, if > 0 | **980 mm** |
| **Aperture** | the option, if > 0 | effective focal length ÷ profile `FocalRatio`, if ratio > 0 | effective focal length ÷ **7** |

The two fallbacks are consistent: 980 ÷ 7 = **140 mm**. Deriving the aperture from the *effective* focal length (rather than a flat 140) keeps the f-ratio sensible when only the focal length is known — the previous flat 100 mm default silently made a 2000 mm profile f/20. The default rig computes to 0.79″/px and **HFR_min ≈ 1.76 px**, safely above HocusFocus's own `MinHFR` gate of 1.2.

Every comparison is written `x > 0`, never `!(x <= 0)`: a fresh NINA profile stores **`NaN`**, and `NaN > 0` is correctly false while `NaN <= 0` is *also* false.

**Fixture facts (verified — do not re-derive):**
- `CameraSimulatorOptionsTests` already has `private static (CameraSimulatorOptions options, InMemoryPluginOptionsAccessor store, IProfileService profile) Build()`. It returns the profile substitute, so the new tests stub through it — **add the helper below, don't replace `Build()`.**
- The fixture uses **NSubstitute**. `IProfile` and `ITelescopeSettings` are interfaces, so `Substitute.For<IProfileService>().ActiveProfile.TelescopeSettings` auto-substitutes recursively and `FocalLength` returns `0.0` — not null, no NRE. This is already proven: `BuildRenderRequest` dereferences that exact chain in the passing camera tests today. A bare substitute therefore lands on the **default rig (980 / 140)**, which is the correct behaviour for the tests that don't care.

- [ ] **Step 1: Write the failing test**

```csharp
    private static CameraSimulatorOptions OptionsFor(double profileFocalLength, double profileFocalRatio) {
        var (options, _, profile) = Build();
        profile.ActiveProfile.TelescopeSettings.FocalLength.Returns(profileFocalLength);
        profile.ActiveProfile.TelescopeSettings.FocalRatio.Returns(profileFocalRatio);
        return options;
    }

    [Test]
    public void UnsetOptics_InferFromTheProfile() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        Assert.Multiple(() => {
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(-1.0), "unset sentinel");
            Assert.That(options.ApertureMillimeters, Is.EqualTo(-1.0), "unset sentinel");
            Assert.That(options.EffectiveFocalLengthMillimeters, Is.EqualTo(430.0));
            Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(86.0).Within(1e-9), "430 / f5");
        });
    }

    // A fresh NINA profile stores NaN — not 0, not -1. NaN passes `<= 0` guards, so it must be handled by
    // a positive test, not by the absence of one.
    [Test]
    public void ProfileWithNaNOptics_FallsBackToTheDefaultRig() {
        var options = OptionsFor(profileFocalLength: double.NaN, profileFocalRatio: double.NaN);
        Assert.Multiple(() => {
            Assert.That(options.EffectiveFocalLengthMillimeters, Is.EqualTo(980.0));
            Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(140.0).Within(1e-9), "980 / f7");
        });
    }

    [Test]
    public void ProfileWithFocalLengthButNoRatio_KeepsTheDefaultRatio() {
        var options = OptionsFor(profileFocalLength: 1400.0, profileFocalRatio: double.NaN);
        Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(200.0).Within(1e-9), "1400 / f7");
    }

    [Test]
    public void ExplicitOptics_WinOverTheProfile() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        options.FocalLengthMillimeters = 1000.0;
        options.ApertureMillimeters = 250.0;
        Assert.Multiple(() => {
            Assert.That(options.EffectiveFocalLengthMillimeters, Is.EqualTo(1000.0));
            Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(250.0));
        });
    }

    [Test]
    public void ExplicitFocalLength_WithUnsetAperture_KeepsTheProfileRatio() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        options.FocalLengthMillimeters = 1000.0;
        Assert.That(options.EffectiveApertureMillimeters, Is.EqualTo(200.0).Within(1e-9), "1000 / f5");
    }

    [Test]
    public void NonPositiveAssignment_HealsToTheUnsetSentinel() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        options.FocalLengthMillimeters = 0.0;   // the sentinel older builds stored
        options.ApertureMillimeters = -5.0;
        Assert.Multiple(() => {
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(-1.0));
            Assert.That(options.ApertureMillimeters, Is.EqualTo(-1.0));
            Assert.That(options.EffectiveFocalLengthMillimeters, Is.EqualTo(430.0), "falls back to the profile");
        });
    }

    [Test]
    public void ChangingFocalLength_RaisesTheInferredAperture() {
        var options = OptionsFor(profileFocalLength: 430.0, profileFocalRatio: 5.0);
        var raised = new List<string>();
        options.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        options.FocalLengthMillimeters = 1000.0;
        Assert.That(raised, Does.Contain(nameof(CameraSimulatorOptions.EffectiveApertureMillimeters)),
            "an inferred aperture depends on the focal length, so its hint must refresh too");
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~CameraSimulatorOptionsTests"`
Expected: FAIL — `EffectiveFocalLengthMillimeters` does not exist.

- [ ] **Step 3: Update the one existing test that pins the old defaults**

`CameraSimulatorOptionsTests.Defaults_MatchDesignConfigTable` (`CameraSimulatorOptionsTests.cs:29-30`) asserts the sentinels this task is deliberately changing. This is the **only** existing assertion in the suite that breaks — a full grep of `ApertureMillimeters|FocalLengthMillimeters` across the test project confirms every other use either sets an explicit positive value (which survives `NormalizeUnset` untouched) or builds a `RenderRequest` directly (unaffected). Update it to:

```csharp
            Assert.That(options.ApertureMillimeters, Is.EqualTo(-1.0), "unset: inferred from the profile");
            Assert.That(options.FocalLengthMillimeters, Is.EqualTo(-1.0), "unset: inferred from the profile");
```

Do **not** loosen or delete any other assertion in that test. If anything else in the suite goes red, stop — it means the resolution chain is wrong, not that the test is.

- [ ] **Step 4: Store the profile service and add the constants**

In `CameraSimulatorOptions.cs`, add the field and assign it in the internal constructor (`:37-41`):

```csharp
        private readonly IProfileService profileService;
```
```csharp
        internal CameraSimulatorOptions(IProfileService profileService, IPluginOptionsAccessor optionsAccessor) {
            this.profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            this.optionsAccessor = optionsAccessor ?? throw new ArgumentNullException(nameof(optionsAccessor));
            profileService.ProfileChanged += ProfileService_ProfileChanged;
            InitializeOptions();
        }
```

Add near `DefaultAstapCatalogPath` (`:56-58`):

```csharp
        /// <summary>
        /// The focal length (mm) used when neither the option nor the active profile supplies one. A fresh NINA
        /// profile stores NaN for both telescope values, so this is the out-of-the-box rig, not a rare edge case.
        /// Paired with <see cref="DefaultFocalRatio"/> it describes a 980 mm f/7 — which at 3.76 µm samples at
        /// 0.79″/px and puts HFR_min near 1.8 px, comfortably above HocusFocus's own minimum-HFR detection gate.
        /// </summary>
        public const double DefaultFocalLengthMillimeters = 980.0;

        /// <summary>
        /// The focal ratio used to infer an aperture when the profile has none. Applied to the <b>effective</b>
        /// focal length rather than yielding a flat aperture, so a long profile focal length cannot silently
        /// produce an absurd f-ratio (the previous flat 100 mm default made a 2000 mm profile an f/20).
        /// </summary>
        public const double DefaultFocalRatio = 7.0;

        /// <summary>The stored value meaning "unset — infer it". Matches the plugin's <c>DoubleNegativeToEmptyStringConverter</c> convention.</summary>
        private const double Unset = -1.0;

        /// <summary>Collapses anything non-positive (including NaN, which no ordinary comparison rejects) to <see cref="Unset"/>.</summary>
        private static double NormalizeUnset(double value) => value > 0.0 ? value : Unset;
```

- [ ] **Step 5: Switch the sentinels and add the effective properties**

In `InitializeOptions` (`:68-69`), replace the two loads:

```csharp
            // -1 means "unset — infer from the profile". NormalizeUnset also heals the sentinels older builds
            // wrote: focal length stored 0, and aperture stored a hard 100.0 default that ignored the profile's
            // focal ratio entirely. A value the user actually typed is > 0 and survives untouched.
            apertureMillimeters = NormalizeUnset(optionsAccessor.GetValueDouble(nameof(ApertureMillimeters), Unset));
            focalLengthMillimeters = NormalizeUnset(optionsAccessor.GetValueDouble(nameof(FocalLengthMillimeters), Unset));
```

In `ResetDefaults` (`:111-112`), replace with:

```csharp
            ApertureMillimeters = Unset;
            FocalLengthMillimeters = Unset;
```

Replace the two setters (`:174-196`) so they normalize and refresh the dependent hints:

```csharp
        public double ApertureMillimeters {
            get => apertureMillimeters;
            set {
                var normalized = NormalizeUnset(value);
                if (apertureMillimeters != normalized) {
                    apertureMillimeters = normalized;
                    optionsAccessor.SetValueDouble(nameof(ApertureMillimeters), apertureMillimeters);
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(EffectiveApertureMillimeters));
                }
            }
        }

        public double FocalLengthMillimeters {
            get => focalLengthMillimeters;
            set {
                var normalized = NormalizeUnset(value);
                if (focalLengthMillimeters != normalized) {
                    focalLengthMillimeters = normalized;
                    optionsAccessor.SetValueDouble(nameof(FocalLengthMillimeters), focalLengthMillimeters);
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(EffectiveFocalLengthMillimeters));
                    // An unset aperture is inferred FROM the focal length, so its hint moves with this.
                    RaisePropertyChanged(nameof(EffectiveApertureMillimeters));
                }
            }
        }

        /// <summary>
        /// The focal length (mm) the next exposure will actually use: the option when set, else the active
        /// profile's telescope focal length, else <see cref="DefaultFocalLengthMillimeters"/>.
        ///
        /// <para>This is the single resolution point — the render reads it and the setup dialog's hint text
        /// displays it, so the number shown to the user is by construction the number that will be used.</para>
        /// </summary>
        public double EffectiveFocalLengthMillimeters {
            get {
                if (focalLengthMillimeters > 0.0) {
                    return focalLengthMillimeters;
                }
                // `> 0` and not `!(<= 0)`: a fresh NINA profile stores NaN, which fails BOTH comparisons.
                var profileFocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength;
                return profileFocalLength > 0.0 ? profileFocalLength : DefaultFocalLengthMillimeters;
            }
        }

        /// <summary>
        /// The aperture (mm) the next exposure will actually use: the option when set, else
        /// <see cref="EffectiveFocalLengthMillimeters"/> divided by the profile's focal ratio (so leaving the
        /// aperture blank simply means "my profile's focal ratio"), else by <see cref="DefaultFocalRatio"/>.
        /// Never NaN and never non-positive, so the optics models always receive a usable diameter.
        /// </summary>
        public double EffectiveApertureMillimeters {
            get {
                if (apertureMillimeters > 0.0) {
                    return apertureMillimeters;
                }
                var profileFocalRatio = profileService.ActiveProfile.TelescopeSettings.FocalRatio;
                var focalRatio = profileFocalRatio > 0.0 ? profileFocalRatio : DefaultFocalRatio;
                return EffectiveFocalLengthMillimeters / focalRatio;
            }
        }
```

- [ ] **Step 6: Declare the properties on the interface**

In `ICameraSimulatorOptions.cs`, beneath the existing `ApertureMillimeters` / `FocalLengthMillimeters` (`:89-90`):

```csharp
        /// <summary>The aperture (mm) the next exposure will use, with the profile fallback already applied. Read-only: set <see cref="ApertureMillimeters"/> to override.</summary>
        double EffectiveApertureMillimeters { get; }

        /// <summary>The focal length (mm) the next exposure will use, with the profile fallback already applied. Read-only: set <see cref="FocalLengthMillimeters"/> to override.</summary>
        double EffectiveFocalLengthMillimeters { get; }
```

- [ ] **Step 7: Consume them in the camera**

In `BuildRenderRequest`, delete the inline fallback (`:593-595`):

```csharp
            var focalLength = options.FocalLengthMillimeters > 0.0
                ? options.FocalLengthMillimeters
                : profileService.ActiveProfile.TelescopeSettings.FocalLength;
```

and change the two request fields (`:603-604`) to:

```csharp
                // Resolved by the options object, which is also what the setup dialog's hint text displays —
                // so what the user is shown and what is rendered cannot drift apart.
                ApertureMillimeters = options.EffectiveApertureMillimeters,
                FocalLengthMillimeters = options.EffectiveFocalLengthMillimeters,
```

If `profileService` is now unused in the camera, the compiler will say so; leave the field only if another member still reads it.

- [ ] **Step 8: Make the optics guards NaN-safe**

In `DefocusModel.cs:113-119`, rewrite the guards so NaN is rejected rather than propagated:

```csharp
            // `!(x > 0)` and not `x <= 0`: NaN fails BOTH `>` and `<=`, so the old form waved NaN through into
            // N = f/D and produced an all-NaN frame with no error anywhere. A fresh NINA profile stores NaN for
            // the telescope focal length and ratio, so this is reachable, not theoretical.
            if (!(apertureMillimeters > 0)) throw new ArgumentOutOfRangeException(nameof(apertureMillimeters), apertureMillimeters, "must be a positive number");
            if (!(focalLengthMillimeters > 0)) throw new ArgumentOutOfRangeException(nameof(focalLengthMillimeters), focalLengthMillimeters, "must be a positive number");
            if (!(centralObstructionFraction >= 0 && centralObstructionFraction < 1)) throw new ArgumentOutOfRangeException(nameof(centralObstructionFraction), centralObstructionFraction, "must be in [0, 1)");
            if (!(pixelSizeMicrons > 0)) throw new ArgumentOutOfRangeException(nameof(pixelSizeMicrons), pixelSizeMicrons, "must be a positive number");
            if (!(seeingArcsec >= 0)) throw new ArgumentOutOfRangeException(nameof(seeingArcsec), seeingArcsec, "must be a non-negative number");
            if (!(wavelengthNm > 0)) throw new ArgumentOutOfRangeException(nameof(wavelengthNm), wavelengthNm, "must be a positive number");
            if (!(focuserStepSizeMicrons > 0)) throw new ArgumentOutOfRangeException(nameof(focuserStepSizeMicrons), focuserStepSizeMicrons, "must be a positive number");
```

Apply the identical treatment to any `<= 0` / `< 0` argument guard in `RadiometryCalculator`. Add to `DefocusModelTests`:

```csharp
    [Test]
    public void NaNOptics_AreRejected_NotPropagated() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefocusModel(double.NaN, 430.0, 0.3, 3.76, 2.5, 540.0, 2.0, 25000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefocusModel(100.0, double.NaN, 0.3, 3.76, 2.5, 540.0, 2.0, 25000));
    }
```

- [ ] **Step 9: Run tests to verify they pass**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo --filter "FullyQualifiedName~CameraSimulatorOptionsTests|FullyQualifiedName~DefocusModelTests|FullyQualifiedName~HocusFocusSimulatorCameraTests"`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/CameraSimulatorOptions.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Interfaces/ICameraSimulatorOptions.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/HocusFocusSimulatorCamera.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/DefocusModel.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/Rendering/RadiometryCalculator.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/CameraSimulatorOptionsTests.cs \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus.Tests/CameraSimulator/DefocusModelTests.cs
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): infer aperture and focal length from the profile, NaN-safe"
```

---

## Task 7: Hint text in the UI

**Files:**
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/SetupDialog/SetupDataTemplates.xaml:21-22, 65-89`
- Modify: `Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml:3012-3016`

`ninactrl:HintTextBox` (NINA.CustomControlLibrary) renders its `HintText` at **opacity 0.4** whenever the text box is empty — this is exactly NINA's defaulted-gain treatment, and the plugin already uses it in `AutoFocus/DataTemplates.xaml:1262`. It pairs with `DoubleNegativeToEmptyStringConverter`, which maps a negative value to `""` (so the hint shows) and `""` back to `-1.0` (the unset sentinel).

> **Two traps, both already paid for once in this codebase.**
> 1. **Declare the converter locally.** `SetupDataTemplates.xaml:12-14` documents why: `StaticResource` resolves at parse time and the merge order of separately-exported plugin `ResourceDictionary` instances is not guaranteed, so reaching into `OptionsDataTemplates.xaml` is a load-order race.
> 2. **A missing `StaticResource` key does not fail loudly.** It throws `XamlParseException` at parse time, and `WindowService.Show` swallows that — the setup dialog simply never opens. This is exactly how `NotificationSuccessBrush` shipped. `XamlBrushResourceTests` guards brushes; converters are not covered, so verify this dialog actually opens (Task 8).
> 3. **Drop the `FloatRangeRule`s.** WPF runs validation rules on the *raw* input before the converter, so an empty box ("unset") would fail a `Minimum="1"` rule. The established `HintTextBox` usage carries no validation rules; the `NormalizeUnset` clamp in the setter is what keeps the value sane.

- [ ] **Step 1: Declare the converter in the setup dialog**

After `SetupDataTemplates.xaml:16` (`<hfconverters:InverseBooleanConverter ... />`):

```xml
    <hfconverters:DoubleNegativeToEmptyStringConverter x:Key="HF_DoubleNegativeToEmptyStringConverter" />
```

- [ ] **Step 2: Update the two tooltips** (`:21-22`)

```xml
    <TextBlock x:Key="CamSimSetup_ApertureMillimeters_Tooltip" Text="Telescope aperture diameter D (mm). Drives the collecting area (star brightness) and the diffraction-limited in-focus star size. Leave blank to derive it from the focal length and the focal ratio in your NINA telescope settings; the greyed-out value is what will be used." />
    <TextBlock x:Key="CamSimSetup_FocalLengthMillimeters_Tooltip" Text="Focal length (mm) used for plate scale and defocus geometry. Leave blank to use the focal length from your NINA telescope settings; the greyed-out value is what will be used." />
```

- [ ] **Step 3: Replace the two rows** (`:65-89`)

```xml
                <TextBlock Grid.Row="1" Grid.Column="0" VerticalAlignment="Center" Text="Aperture" ToolTip="{StaticResource CamSimSetup_ApertureMillimeters_Tooltip}" />
                <StackPanel Grid.Row="1" Grid.Column="1" Margin="5,5,0,0" Orientation="Horizontal">
                    <!--  Blank = "infer it". The hint renders the inferred value at 40% opacity, so the box always
                          shows the number the next exposure will use — it is bound to the same property the
                          renderer reads. No ValidationRules: they run on the raw text before the converter, so
                          they would reject the empty (unset) box; CameraSimulatorOptions clamps on commit.  -->
                    <ninactrl:HintTextBox
                        MinWidth="80"
                        VerticalAlignment="Center"
                        HorizontalContentAlignment="Left"
                        VerticalContentAlignment="Center"
                        HintText="{Binding Options.EffectiveApertureMillimeters, StringFormat={}{0:F1}}"
                        TextAlignment="Left"
                        ToolTip="{StaticResource CamSimSetup_ApertureMillimeters_Tooltip}">
                        <ninactrl:HintTextBox.Text>
                            <Binding
                                Converter="{StaticResource HF_DoubleNegativeToEmptyStringConverter}"
                                Mode="TwoWay"
                                Path="Options.ApertureMillimeters"
                                UpdateSourceTrigger="LostFocus" />
                        </ninactrl:HintTextBox.Text>
                    </ninactrl:HintTextBox>
                    <TextBlock Margin="3,0,0,0" VerticalAlignment="Center" Text="mm" ToolTip="{StaticResource CamSimSetup_ApertureMillimeters_Tooltip}" />
                </StackPanel>

                <TextBlock Grid.Row="2" Grid.Column="0" VerticalAlignment="Center" Text="Focal Length" ToolTip="{StaticResource CamSimSetup_FocalLengthMillimeters_Tooltip}" />
                <StackPanel Grid.Row="2" Grid.Column="1" Margin="5,5,0,0" Orientation="Horizontal">
                    <ninactrl:HintTextBox
                        MinWidth="80"
                        VerticalAlignment="Center"
                        HorizontalContentAlignment="Left"
                        VerticalContentAlignment="Center"
                        HintText="{Binding Options.EffectiveFocalLengthMillimeters, StringFormat={}{0:F0}}"
                        TextAlignment="Left"
                        ToolTip="{StaticResource CamSimSetup_FocalLengthMillimeters_Tooltip}">
                        <ninactrl:HintTextBox.Text>
                            <Binding
                                Converter="{StaticResource HF_DoubleNegativeToEmptyStringConverter}"
                                Mode="TwoWay"
                                Path="Options.FocalLengthMillimeters"
                                UpdateSourceTrigger="LostFocus" />
                        </ninactrl:HintTextBox.Text>
                    </ninactrl:HintTextBox>
                    <TextBlock Margin="3,0,0,0" VerticalAlignment="Center" Text="mm" ToolTip="{StaticResource CamSimSetup_FocalLengthMillimeters_Tooltip}" />
                </StackPanel>
```

If `rules:` is now unreferenced in this file, remove the `xmlns:rules` declaration; if other rows still use it, leave it.

- [ ] **Step 4: Show the effective values in the read-only plugin options** (`OptionsDataTemplates.xaml:3012-3016`)

```xml
                <!--  Rig (read-only here; edited in the camera's setup dialog). Bound to the Effective* values, not
                      the raw options: the raw value is -1 when unset, and showing a bare "-1" as the aperture would
                      be actively misleading. These are the numbers the next exposure will use.  -->
                <TextBlock Grid.Row="2" Grid.Column="0" VerticalAlignment="Center" Text="Aperture" ToolTip="{StaticResource CamSim_RigReadOnly_Tooltip}" />
                <ninactrl:UnitTextBox Grid.Row="2" Grid.Column="1" MinWidth="80" Margin="5,5,0,0" HorizontalAlignment="Left" VerticalAlignment="Center" IsEnabled="False" Text="{Binding CameraSimulatorOptions.EffectiveApertureMillimeters, Mode=OneWay, StringFormat={}{0:F1}}" ToolTip="{StaticResource CamSim_RigReadOnly_Tooltip}" Unit="mm" />

                <TextBlock Grid.Row="3" Grid.Column="0" VerticalAlignment="Center" Text="Focal Length" ToolTip="{StaticResource CamSim_RigReadOnly_Tooltip}" />
                <ninactrl:UnitTextBox Grid.Row="3" Grid.Column="1" MinWidth="80" Margin="5,5,0,0" HorizontalAlignment="Left" VerticalAlignment="Center" IsEnabled="False" Text="{Binding CameraSimulatorOptions.EffectiveFocalLengthMillimeters, Mode=OneWay, StringFormat={}{0:F0}}" ToolTip="{StaticResource CamSim_RigReadOnly_Tooltip}" Unit="mm" />
```

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: PASS, 0 failed. `XamlBrushResourceTests` must stay green (it scans all plugin XAML).

- [ ] **Step 6: Commit**

```bash
git add Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/CameraSimulator/SetupDialog/SetupDataTemplates.xaml \
        Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus/Resources/OptionsDataTemplates.xaml
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "feat(camera-sim): show inferred aperture/focal length as hint text"
```

---

## Task 8: Verify and document

**Files:**
- Modify: `docs/synthetic-camera-manual-smoke-test.md`

- [ ] **Step 1: Run the full suite**

Run: `dotnet test Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug --nologo`
Expected: PASS, 0 failed, count ≥ 2053 + the new tests.

- [ ] **Step 2: Measure the win, don't assume it**

Add a temporary profiling test (delete before committing) that renders one IMX455 frame at the user's config (sky 14.62 e⁻/px) and prints the wall time. Baseline for comparison: **13.1 s**.
Expected: **≤ 2 s**. If it is not, stop and investigate before claiming the fix — do not report a speedup that was not measured.

- [ ] **Step 3: Add smoke-test checks**

Append to `docs/synthetic-camera-manual-smoke-test.md`:

```markdown
### (p) Optics default to the profile

With **Aperture** and **Focal Length** blank in the camera setup dialog, both boxes show a greyed-out
number (40% opacity). On a profile with 430 mm f/5 they read **430** and **86.0**. On a profile whose
telescope settings are unset (NINA stores `NaN`) they read **980** and **140.0**. Typing a value makes it
solid; clearing the box returns it to grey. The Hocus Focus plugin options show the same numbers,
read-only, and never show `-1`.

### (q) The setup dialog still opens

Click the camera gear button. The dialog must appear. If it does not, a `{StaticResource}` key in
`SetupDataTemplates.xaml` is unresolvable: `WindowService.Show` swallows the `XamlParseException`, so the
only symptom is nothing happening. Check the NINA log for `WindowService.cs|Show|41`.

### (r) Autofocus completes without timing out

Run an autofocus. Download should be roughly as fast as the exposure (it now overlaps it), each point
taking about as long as expose + star detection. The run must complete rather than hit the 10-minute
timeout. Frames at the same focuser position must differ — repeated exposures no longer produce identical
noise, so a >1 frames-per-point average is now doing real work.
```

- [ ] **Step 4: Commit**

```bash
git add docs/synthetic-camera-manual-smoke-test.md
GIT_COMMITTER_NAME="George Hilios" GIT_COMMITTER_EMAIL="322725+ghilios@users.noreply.github.com" \
  git commit --author="George Hilios <322725+ghilios@users.noreply.github.com>" \
  -m "docs(camera-sim): smoke-test checks for inferred optics, prefetch and per-exposure noise"
```

- [ ] **Step 5: Push**

```bash
git push
```

---

## Out of scope

- **The 1.8″/px sampling itself.** The user's HFR ≈ 1 px is correct physics for a 430 mm rig at 3.76 µm; it is a rig choice, not a defect. Task 6 changes only the *default* when nothing is configured. Getting a realistic HFR on the "Default" profile still means setting a longer focal length.
- **A constant-time Poisson sampler** (PTRS / transformed rejection). Even after Task 2 the Poisson branch stays ~5× the Gaussian branch per pixel, so the cost cliff below λ=40 is reduced but not removed. Tasks 2+3 together take the frame to ~1.5 s, which makes the cliff moot; revisit only if it resurfaces.
- **`TiltAdapterOptions.Screw4AngleDegrees`'s naive NaN guard** (`TiltAdapterWizard/TiltAdapterOptions.cs:141`) — a pre-existing bug, still unanswered, unrelated to this work.
