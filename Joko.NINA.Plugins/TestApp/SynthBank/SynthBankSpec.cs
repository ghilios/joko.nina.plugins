#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Joko.Plugins.HocusFocus.CameraSimulator;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;

namespace TestApp.SynthBank {

    /// <summary>
    /// Root of the checked-in synthetic-bank spec (<c>TestApp/SynthBank/synthetic-bank-spec.json</c>, written by
    /// workstream G7 — not this file). Deserialized with Newtonsoft.Json, matching the
    /// <c>[JsonProperty("...")]</c> convention <c>GoldenStarSet.cs</c> already uses in this project. Plain
    /// settable properties throughout (not records) so <c>JsonConvert.DeserializeObject</c> can populate them
    /// without a matching constructor.
    /// </summary>
    public sealed class SynthBankSpec {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;

        /// <summary>Bank-wide seed. Per-frame noise seeds are <c>SeedMixer.Combine(bankSeed, datasetIndex, frameIndex)</c> (G6/G7 — not derived here).</summary>
        [JsonProperty("bankSeed")] public int BankSeed { get; set; }

        [JsonProperty("defaults")] public SynthBankDefaults Defaults { get; set; } = new SynthBankDefaults();

        [JsonProperty("datasets")] public List<SynthDatasetSpec> Datasets { get; set; } = new List<SynthDatasetSpec>();
    }

    /// <summary>
    /// Bank-wide defaults a <see cref="SynthDatasetSpec"/> may override (filter, gain, exposure — see those
    /// fields' doc comments) or simply inherit (bias, sensor temperature, throughput, sweep shape, catalog
    /// path, golden tier thresholds). Values match the design's stated defaults: L filter, gain 100, bias 500,
    /// −10 °C, throughput 0.85, offset 4, tier thresholds {20, 10, 5, 3.5}.
    /// </summary>
    public sealed class SynthBankDefaults {

        /// <summary><see cref="SimulatorFilter"/> member name (e.g. "L", "Ha3"). Parsed by <see cref="SynthRenderRequestFactory.ParseFilter"/>.</summary>
        [JsonProperty("filter")] public string Filter { get; set; } = "L";

        [JsonProperty("gain")] public int Gain { get; set; } = 100;
        [JsonProperty("biasPedestalAdu")] public int BiasPedestalAdu { get; set; } = 500;
        [JsonProperty("sensorTemperatureCelsius")] public double SensorTemperatureCelsius { get; set; } = -10.0;
        [JsonProperty("opticalThroughput")] public double OpticalThroughput { get; set; } = 0.85;

        /// <summary>Offset steps per side of focus for the generated sweep — <see cref="NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization.StepSizeRecommender"/>'s own default (4), reused here so the truth model's frame count matches what the recommender would itself propose.</summary>
        [JsonProperty("offsetSteps")] public int OffsetSteps { get; set; } = 4;

        /// <summary>Frames per sweep, <c>2·offsetSteps + 1</c> at the default offset (9).</summary>
        [JsonProperty("framesPerSweep")] public int FramesPerSweep { get; set; } = 9;

        /// <summary>Folder containing the ASTAP/HNSKY star-database cell files (the real Gaia G18 catalog installed at this path).</summary>
        [JsonProperty("astapCatalogPath")] public string AstapCatalogPath { get; set; } = @"C:\Program Files\astap";

        // Golden peak-SNR tier thresholds (G5's tiering; kept here as plain doubles per the task boundary —
        // G5 owns the actual golden-policy TYPE, this file only carries the checked-in numbers it will read).
        [JsonProperty("goldenHighSnr")] public double GoldenHighSnr { get; set; } = 20.0;
        [JsonProperty("goldenMediumSnr")] public double GoldenMediumSnr { get; set; } = 10.0;
        [JsonProperty("goldenLowSnr")] public double GoldenLowSnr { get; set; } = 5.0;
        [JsonProperty("goldenUnresolvedSnr")] public double GoldenUnresolvedSnr { get; set; } = 3.5;
    }

    /// <summary>
    /// One synthetic dataset's optical train, pointing, and sensor/exposure configuration — everything
    /// <see cref="SynthRenderRequestFactory.Build"/> and <see cref="SynthBankDerivations"/> need to build a
    /// <see cref="RenderRequest"/> and derive its expected-optimal bootstrap parameters. Matches the 17-row
    /// dataset matrix in <c>docs/synthetic-af-bank-design.md</c>.
    /// </summary>
    public sealed class SynthDatasetSpec {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("description")] public string Description { get; set; }

        /// <summary>
        /// Non-null when this row's rendered frames are KNOWN BAD and must be re-rendered before their numbers are
        /// used again; the text says why. Purely declarative — nothing branches on it — but it is a real property
        /// rather than a stray JSON key so a spec round-trip cannot silently drop the warning, and
        /// <c>synth-bank</c> prints it (see <see cref="SuspectBanner"/>).
        ///
        /// <para>Currently set on the two rows whose diagonal FOV exceeds the catalog query's one-cell cap
        /// (F44): their star fields are spatially truncated, so counts, recall, precision and positions from them
        /// are not trustworthy. Gate-threshold results are.</para>
        /// </summary>
        [JsonProperty("suspect", NullValueHandling = NullValueHandling.Ignore)] public string Suspect { get; set; }

        [JsonProperty("focalLengthMm")] public double FocalLengthMm { get; set; }

        /// <summary>Focal ratio N = f/D. Aperture is DERIVED as <see cref="FocalLengthMm"/> / <see cref="FocalRatio"/> (<see cref="ApertureMillimeters"/>) rather than stored redundantly.</summary>
        [JsonProperty("focalRatio")] public double FocalRatio { get; set; }

        /// <summary>Central obstruction fraction ε. 0 ⇒ obstruction disabled (an unobstructed refractor/apo).</summary>
        [JsonProperty("centralObstructionFraction")] public double CentralObstructionFraction { get; set; }

        /// <summary><see cref="SonySensorModel"/> member name (e.g. "IMX571"). Parsed by <see cref="SynthRenderRequestFactory.ParseSensorModel"/>.</summary>
        [JsonProperty("sensorModel")] public string SensorModel { get; set; }

        /// <summary>Physical (AF) capture binning, 1 or 2. Distinct from DETECTION binning (<see cref="SynthExpectedOptimal.DetectionBinning"/>), which is a separate, software-only downsample applied on top of this.</summary>
        [JsonProperty("captureBinning")] public int CaptureBinning { get; set; } = 1;

        [JsonProperty("raDegrees")] public double RaDegrees { get; set; }
        [JsonProperty("decDegrees")] public double DecDegrees { get; set; }
        [JsonProperty("rotationDegrees")] public double RotationDegrees { get; set; }

        [JsonProperty("limitingMagnitude")] public double LimitingMagnitude { get; set; }
        [JsonProperty("skyBrightnessMagPerArcsec2")] public double SkyBrightnessMagPerArcsec2 { get; set; }
        [JsonProperty("seeingArcsec")] public double SeeingArcsec { get; set; }

        /// <summary>Per-dataset filter override (<see cref="SimulatorFilter"/> member name). Null ⇒ inherit <see cref="SynthBankDefaults.Filter"/> (used by the narrowband D16/D17 rows).</summary>
        [JsonProperty("filter", NullValueHandling = NullValueHandling.Ignore)] public string Filter { get; set; }

        /// <summary>Per-dataset gain override. Null ⇒ inherit <see cref="SynthBankDefaults.Gain"/>.</summary>
        [JsonProperty("gain", NullValueHandling = NullValueHandling.Ignore)] public int? Gain { get; set; }

        /// <summary>
        /// Per-dataset exposure override, seconds. Nullable specifically so the checked-in spec can leave this
        /// unset and let <see cref="SynthBankDerivations.DeriveExposureBand"/> supply it from the radiometry —
        /// the exposure column in the design's dataset matrix is the DERIVED value, not an independent input.
        /// </summary>
        [JsonProperty("exposureSeconds", NullValueHandling = NullValueHandling.Ignore)] public double? ExposureSeconds { get; set; }

        [JsonProperty("optimalFocuserPosition")] public int OptimalFocuserPosition { get; set; }

        /// <summary>Focuser step size k, µm of sensor-plane defocus per focuser step.</summary>
        [JsonProperty("focuserStepSizeMicrons")] public double FocuserStepSizeMicrons { get; set; }

        /// <summary>Per-dataset seed feeding <c>SeedMixer.Combine(datasetSeed, scenarioId, round)</c> (V1 — not this file).</summary>
        [JsonProperty("datasetSeed")] public int DatasetSeed { get; set; }

        // Optional pins: let the checked-in spec override a value SynthBankDerivations would otherwise compute
        // (e.g. because validation calibrated the R2 pixelization floor, or hand-tuned a borderline donut call).
        // SynthBankDerivations itself never reads these — see that class's remarks on staying override-free so
        // a physics-vs-pinned-value disagreement stays visible instead of being silently absorbed.
        [JsonProperty("stepSizeOverride", NullValueHandling = NullValueHandling.Ignore)] public int? StepSizeOverride { get; set; }
        [JsonProperty("detectionBinningOverride", NullValueHandling = NullValueHandling.Ignore)] public int? DetectionBinningOverride { get; set; }
        [JsonProperty("donutOverride", NullValueHandling = NullValueHandling.Ignore)] public bool? DonutOverride { get; set; }

        /// <summary>Clear aperture diameter D (mm), derived as f/N — never stored independently, so it cannot drift from <see cref="FocalLengthMm"/>/<see cref="FocalRatio"/>.</summary>
        [JsonIgnore] public double ApertureMillimeters => FocalLengthMm / FocalRatio;
    }

    /// <summary>
    /// The V1 convergence driver's bootstrap input (design §G6/G7's "Library API used by V1" — the type name is
    /// prescribed by the plan verbatim). A scenario perturbs one or more of these away from
    /// <see cref="SynthExpectedOptimal"/> and the driver measures whether the optimizer's recommendations walk
    /// them back.
    /// </summary>
    public sealed class SynthBootstrapParams {
        [JsonProperty("centerPosition")] public int CenterPosition { get; set; }
        [JsonProperty("stepSize")] public int StepSize { get; set; }
        [JsonProperty("offsetSteps")] public int OffsetSteps { get; set; }
        [JsonProperty("exposureSeconds")] public double ExposureSeconds { get; set; }
        [JsonProperty("afBinning")] public int AfBinning { get; set; }
        [JsonProperty("emitGoldens")] public bool EmitGoldens { get; set; }
    }

    /// <summary>
    /// A dataset's expected-optimal bootstrap parameters — the answer <see cref="SynthBankDerivations"/> computes
    /// from physics (<c>synthetic_meta.json</c>'s <c>expectedOptimal</c> block). See
    /// <see cref="SynthBankDerivations.DeriveExpectedOptimal"/> for how each field is derived.
    /// </summary>
    public sealed class SynthExpectedOptimal {

        /// <summary>The exposure (s) whose gate SNR lands nearest <c>ExposureRecommender.TargetSensitivity</c>, clamped and ladder-rounded.</summary>
        [JsonProperty("exposureSeconds")] public double ExposureSeconds { get; set; }

        /// <summary>Exposure (s) at the low end of the acceptable gate-SNR band (7).</summary>
        [JsonProperty("exposureBandLowSeconds")] public double ExposureBandLowSeconds { get; set; }

        /// <summary>Exposure (s) at the high end of the acceptable gate-SNR band (20).</summary>
        [JsonProperty("exposureBandHighSeconds")] public double ExposureBandHighSeconds { get; set; }

        /// <summary>Human-readable statement of exactly what was solved for and against which star/frame — see <see cref="SynthBankDerivations.DeriveExposureBand"/>.</summary>
        [JsonProperty("exposureDefinition")] public string ExposureDefinition { get; set; }

        /// <summary>Physics step* (focuser steps), <see cref="SynthBankDerivations.DeriveStepSize"/>.</summary>
        [JsonProperty("stepSizeSteps")] public int StepSizeSteps { get; set; }

        /// <summary>Fractional tolerance for comparing a measured step size against <see cref="StepSizeSteps"/>.</summary>
        [JsonProperty("stepSizeTolerance")] public double StepSizeTolerance { get; set; } = 0.4;

        [JsonProperty("offsetSteps")] public int OffsetSteps { get; set; }

        /// <summary>The dataset's physical (AF/capture) binning — echoed from <see cref="SynthDatasetSpec.CaptureBinning"/>, since that is a design choice, not a derived quantity.</summary>
        [JsonProperty("autofocusBinning")] public int AutofocusBinning { get; set; }

        /// <summary>Recommended DETECTION binning, <see cref="SynthBankDerivations.DeriveDetectionBinning"/>.</summary>
        [JsonProperty("detectionBinning")] public int DetectionBinning { get; set; }

        /// <summary>Whether donut-aware detection is expected to be warranted, <see cref="SynthBankDerivations.DeriveDonutExpectation"/>.</summary>
        [JsonProperty("donutDetection")] public bool DonutDetection { get; set; }

        /// <summary>Which term decided <see cref="DonutDetection"/> — see <see cref="SynthBankDerivations.DeriveDonutExpectation"/>.</summary>
        [JsonProperty("donutRationale")] public string DonutRationale { get; set; }
    }

    /// <summary>One sweep frame's ground-truth focus geometry — <c>truthModel.perFrame[]</c> in <c>synthetic_meta.json</c>.</summary>
    public sealed class SynthPerFrameTruth {
        [JsonProperty("focuserPosition")] public int FocuserPosition { get; set; }
        [JsonProperty("defocusMicrons")] public double DefocusMicrons { get; set; }

        /// <summary>Rice closed-form HFR (px, native/pre-binning) of this frame's kernel — <c>PsfKernel.AnalyticHfrPixels</c>.</summary>
        [JsonProperty("analyticKernelHfrPixels")] public double AnalyticKernelHfrPixels { get; set; }

        /// <summary>Flux-weighted HFR (px, native/pre-binning) measured on this frame's kernel raster — <c>PsfKernel.MeasuredHfrPixels</c>.</summary>
        [JsonProperty("measuredKernelHfrPixels")] public double MeasuredKernelHfrPixels { get; set; }

        [JsonProperty("outerRadiusPixels")] public double OuterRadiusPixels { get; set; }
        [JsonProperty("innerRadiusPixels")] public double InnerRadiusPixels { get; set; }
    }

    /// <summary>The dataset's geometric truth model — <c>synthetic_meta.json</c>'s <c>truthModel</c> block, built by <see cref="SynthBankDerivations.BuildTruthModel"/>.</summary>
    public sealed class SynthTruthModel {
        [JsonProperty("hfrMin")] public double HfrMin { get; set; }

        /// <summary>max(<see cref="HfrMin"/>, <see cref="SynthBankDerivations.PixelizationFloorPixels"/>) — the floored value <see cref="SynthBankDerivations.DeriveStepSize"/> actually used. R2: the floor is an estimate; carrying both lets validation calibrate it.</summary>
        [JsonProperty("hfrMinEffective")] public double HfrMinEffective { get; set; }

        [JsonProperty("kappa")] public double Kappa { get; set; }
        [JsonProperty("arcsecPerPixel")] public double ArcsecPerPixel { get; set; }
        [JsonProperty("captureBinning")] public int CaptureBinning { get; set; }

        /// <summary>Free-text note on where read noise enters (it does not enter this geometric model — see <see cref="SynthBankDerivations.DeriveExposureBand"/>).</summary>
        [JsonProperty("readNoiseNote")] public string ReadNoiseNote { get; set; }

        [JsonProperty("perFrame")] public List<SynthPerFrameTruth> PerFrame { get; set; } = new List<SynthPerFrameTruth>();
    }

    /// <summary>The kernel-cap guard table row for one dataset — <see cref="SynthBankDerivations.DeriveKernelCapGuard"/>. Never thrown for; the runner's <c>--dry-run</c> prints the table and decides.</summary>
    public sealed class SynthKernelCapGuard {

        /// <summary>Largest |Δ| (µm) whose kernel support stays safely renderable — <c>StarFieldCompositor.MaxAbsDefocusMicrons</c>.</summary>
        [JsonProperty("maxAbsDefocusMicrons")] public double MaxAbsDefocusMicrons { get; set; }

        /// <summary>The sweep's most-defocused frame's |Δ| (µm): <c>offsetSteps · step* · focuserStepSizeMicrons</c>.</summary>
        [JsonProperty("sweepExtremeDefocusMicrons")] public double SweepExtremeDefocusMicrons { get; set; }

        /// <summary><see cref="SweepExtremeDefocusMicrons"/> / <see cref="MaxAbsDefocusMicrons"/>.</summary>
        [JsonProperty("utilization")] public double Utilization { get; set; }

        /// <summary>True when <see cref="Utilization"/> ≤ <see cref="SynthBankDerivations.KernelCapGuardUtilizationLimit"/> (0.9).</summary>
        [JsonProperty("withinCap")] public bool WithinCap { get; set; }
    }

    /// <summary>
    /// The single owner of the spec+defaults+(focuser position, exposure) → <see cref="RenderRequest"/> mapping,
    /// so the (future) runner and <see cref="SynthBankDerivations"/> build byte-identical requests for the same
    /// inputs — required because the exposure-band derivation renders through the real compositor and that
    /// render must match what the runner will later actually write to disk.
    /// </summary>
    public static class SynthRenderRequestFactory {

        /// <summary>
        /// Builds the immutable <see cref="RenderRequest"/> for one exposure of <paramref name="dataset"/>.
        /// Aberrations (tilt/curvature/optical-axis offset) are always off — the design's 17-dataset matrix has
        /// no tilt axis, so every frame's defocus is the isotropic <c>DefocusModel</c> curve with no field
        /// dependence beyond what the aberration surface's flat (zero-tilt) default already gives.
        /// </summary>
        /// <param name="dataset">The dataset spec.</param>
        /// <param name="defaults">Bank-wide defaults, for any field <paramref name="dataset"/> does not override.</param>
        /// <param name="focuserPosition">The focuser step position for this exposure.</param>
        /// <param name="exposureSeconds">The exposure length for this render (seconds).</param>
        /// <param name="noiseSeed">The frame's noise seed. Irrelevant to anything <see cref="SynthBankDerivations"/> reads from <c>StarTruth</c> (truth is a pure function of position/catalog/flux, not of the noise draw), so callers deriving truth-only quantities may pass any fixed value.</param>
        public static RenderRequest Build(SynthDatasetSpec dataset, SynthBankDefaults defaults, int focuserPosition, double exposureSeconds, int noiseSeed = 0) {
            if (dataset == null) throw new ArgumentNullException(nameof(dataset));
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));

            var filterName = dataset.Filter ?? defaults.Filter;
            var gain = dataset.Gain ?? defaults.Gain;

            return new RenderRequest {
                FocuserConnected = true,
                FocuserPosition = focuserPosition,
                TelescopeConnected = true,
                RaDegreesJ2000 = dataset.RaDegrees,
                DecDegreesJ2000 = dataset.DecDegrees,
                ApertureMillimeters = dataset.ApertureMillimeters,
                FocalLengthMillimeters = dataset.FocalLengthMm,
                CentralObstructionEnabled = dataset.CentralObstructionFraction > 0.0,
                CentralObstructionFraction = dataset.CentralObstructionFraction,
                OpticalThroughput = defaults.OpticalThroughput,
                SensorModel = ParseSensorModel(dataset.SensorModel),
                Gain = gain,
                BiasPedestalAdu = defaults.BiasPedestalAdu,
                SensorTemperatureCelsius = defaults.SensorTemperatureCelsius,
                Filter = ParseFilter(filterName),
                SkyBrightnessMagPerArcsec2 = dataset.SkyBrightnessMagPerArcsec2,
                SeeingArcsec = dataset.SeeingArcsec,
                OptimalFocuserPosition = dataset.OptimalFocuserPosition,
                FocuserStepSizeMicrons = dataset.FocuserStepSizeMicrons,
                AstapCatalogPath = defaults.AstapCatalogPath,
                LimitingMagnitude = dataset.LimitingMagnitude,
                RotationDegrees = dataset.RotationDegrees,
                NoiseSeed = noiseSeed,
                AberrationsEnabled = false,
                TiltAngleDegrees = 0.0,
                TiltAmountMicrons = 0.0,
                BackfocusErrorMicrons = 0.0,
                OpticalAxisOffsetXMicrons = 0.0,
                OpticalAxisOffsetYMicrons = 0.0,
                ExposureSeconds = exposureSeconds
            };
        }

        /// <summary>Parses a <see cref="SonySensorModel"/> member name (e.g. "IMX571"), case-insensitive.</summary>
        public static SonySensorModel ParseSensorModel(string name) {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Sensor model name must not be empty.", nameof(name));
            if (!Enum.TryParse<SonySensorModel>(name, ignoreCase: true, out var model)) {
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown sensor model name.");
            }
            return model;
        }

        /// <summary>Parses a <see cref="SimulatorFilter"/> member name (e.g. "L", "Ha3"), case-insensitive.</summary>
        public static SimulatorFilter ParseFilter(string name) {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Filter name must not be empty.", nameof(name));
            if (!Enum.TryParse<SimulatorFilter>(name, ignoreCase: true, out var filter)) {
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown filter name.");
            }
            return filter;
        }
    }
}
