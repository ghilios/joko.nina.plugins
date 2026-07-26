#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Tests.AutoFocus.Harness {

    /// <summary>
    /// A fully-scripted <see cref="IHocusFocusStarDetection"/> for the deterministic AutoFocus sweep harness. It never
    /// looks at pixels: it reads the CAPTURE-time focuser position for the frame out of the <see cref="SweepFrameLedger"/>
    /// (never the focuser's live position — see the ledger) and asks a <see cref="DegradationScenario"/> for the
    /// <c>(hfr, sigma, starCount)</c> that position should yield, then packs that into a <see cref="StarDetectionResult"/>.
    /// A zero-star result carries <c>AverageHFR = 0</c>, which is precisely the engine's "found no usable stars" failure
    /// signal. Only the full-frame <see cref="IStarDetection.Detect(IRenderedImage, PixelFormat, StarDetectionParams, IProgress{ApplicationStatus}, CancellationToken)"/>
    /// overloads are wired (the engine's <c>region == null</c> path); every region/PSF/context member throws, so a wiring
    /// mistake surfaces loudly instead of silently returning garbage.
    /// </summary>
    internal sealed class ScriptedStarDetection : IHocusFocusStarDetection {
        private readonly SweepFrameLedger ledger;
        private readonly DegradationScenario scenario;

        public ScriptedStarDetection(SweepFrameLedger ledger, DegradationScenario scenario) {
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        }

        public string Name => "Scripted (test)";

        public string ContentId => "Scripted_Test_StarDetection";

        public Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(EvaluateFrame(image));
        }

        // The AF engine routes here only when ModelPSF is set (the "Review Frames" path); detection HFR is identical, so
        // reuse the same scripted evaluation rather than throwing — keeps the harness robust if a scenario sets ModelPSF.
        public Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token, bool modelPSFForAutoFocus) {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(EvaluateFrame(image));
        }

        private StarDetectionResult EvaluateFrame(IRenderedImage image) {
            var position = ledger.PositionFor(image);
            var (hfr, sigma, starCount) = scenario.Evaluate(position);

            var starList = new List<DetectedStar>(Math.Max(0, starCount));
            for (var i = 0; i < starCount; i++) {
                starList.Add(new DetectedStar { HFR = hfr });
            }

            // AverageHFR/HFRStdDev must be set explicitly: StarDetectionResult defaults them to NaN, but the engine reads
            // Measure = AverageHFR and treats 0 as "no stars". starCount == 0 ⇒ hfr == 0 (the scenario guarantees this).
            return new StarDetectionResult {
                AverageHFR = hfr,
                HFRStdDev = sigma,
                DetectedStars = starCount,
                StarList = starList,
                Params = null
            };
        }

        // ---- Unreached on the region==null Run() path (region/PSF/context/optimizer surfaces). --------------------

        public IStarDetectionOptions StarDetectionOptions => null;

        public IStarDetectionAnalysis CreateAnalysis() => throw new NotSupportedException();

        public void UpdateAnalysis(IStarDetectionAnalysis analysis, StarDetectionParams p, StarDetectionResult result) => throw new NotSupportedException();

        public HocusFocusDetectionParams ToHocusFocusParams(StarDetectionParams p) => throw new NotSupportedException();

        public StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) => throw new NotSupportedException();

        public StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus, IStarDetectionOptions optionsOverride) => throw new NotSupportedException();

        public StarDetectorParams GetDefaultStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus) => throw new NotSupportedException();

        public Task<StarDetectionResult> Detect(IRenderedImage image, HocusFocusDetectionParams hocusFocusParams, StarDetectorParams detectorParams, IProgress<ApplicationStatus> progress, CancellationToken token) => throw new NotSupportedException();

        public Task<HocusFocusDetectionContext> BuildDetectionContext(IRenderedImage image, HocusFocusDetectionParams hocusFocusParams, StarDetectorParams detectorParams, IProgress<ApplicationStatus> progress, CancellationToken token) => throw new NotSupportedException();

        public StarDetectionResult GateAndMeasure(HocusFocusDetectionContext context, StarDetectorParams detectorParams, CancellationToken token) => throw new NotSupportedException();

        public string ComputeEarlyCacheKey(StarDetectorParams detectorParams) => throw new NotSupportedException();
    }
}
