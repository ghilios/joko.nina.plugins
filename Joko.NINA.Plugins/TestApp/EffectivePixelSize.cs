#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Globalization;

namespace TestApp {

    /// <summary>
    /// Which pixel pitch a headless tilt validation should reason in, and why.
    ///
    /// <para>Everything downstream multiplies this by a frame DIMENSION to get the sensor's physical extent, so
    /// it has to describe the same frame those dimensions came from. Under NINA's Auto Focus Binning of N the
    /// captured frame is N× smaller per axis and each of its pixels is N× coarser; pairing a binned frame with a
    /// NATIVE pitch understates the sensor by N and inflates every recovered gradient — and with it the reported
    /// thread pitch / stepper step size — by exactly N. That is the bug the tilt wizard carried until
    /// <c>TiltPlaneModel</c> started carrying its own pitch.</para>
    ///
    /// <para>The frame header wins over <c>TiltCalibrationMetadata.PixelSizeMicrons</c> deliberately: that field
    /// is the EFFECTIVE pitch only from metadata schema 4 onward and the NATIVE pitch before it, so trusting it
    /// would silently reproduce the old inflation on every run an older wizard saved. A header is unambiguous at
    /// every schema — NINA's FITS and XISF readers both divide the stored (binned) XPIXSZ back out by XBINNING,
    /// so <c>Camera.PixelSize</c> is always native and <c>BinX</c> always carries the factor, which is exactly
    /// how <c>HocusFocusStarDetection.BuildResultHeader</c> derives it in the live app.</para>
    ///
    /// <para>Pure (no NINA or OpenCV coupling) so the decision is unit-testable; <c>TiltCalibrationRunner</c>
    /// does the file I/O and calls in here for every judgement.</para>
    /// </summary>
    public static class EffectivePixelSize {

        /// <summary>Below this the resolved pitch and the metadata's are the same number, not a disagreement.</summary>
        public const double DisagreementToleranceMicrons = 1e-6;

        public readonly struct Resolution {

            public Resolution(double microns, string provenance, bool fromFrameHeader) {
                Microns = microns;
                Provenance = provenance;
                FromFrameHeader = fromFrameHeader;
            }

            /// <summary>The pitch of one pixel OF THE SAVED FRAME, in microns.</summary>
            public double Microns { get; }

            /// <summary>Human-readable account of where <see cref="Microns"/> came from, for the console + report.</summary>
            public string Provenance { get; }

            /// <summary>False when this fell back to the metadata value (no header, or no pixel size in it).</summary>
            public bool FromFrameHeader { get; }
        }

        /// <summary>
        /// Resolve from a loaded frame's camera metadata. <paramref name="headerPixelSizeMicrons"/> is the NATIVE
        /// pitch (both NINA readers divide the binning back out) and <paramref name="binX"/> the factor it was
        /// captured at, so the effective pitch is their product. Falls back to
        /// <paramref name="metadataPixelSizeMicrons"/> when the header carries no usable pixel size — a frame
        /// written without XPIXSZ tells us nothing, and the metadata is still better than nothing.
        /// </summary>
        public static Resolution FromFrameHeader(
            double headerPixelSizeMicrons, int binX, double metadataPixelSizeMicrons, string frameName) {
            if (double.IsNaN(headerPixelSizeMicrons) || headerPixelSizeMicrons <= 0) {
                return FromMetadata(metadataPixelSizeMicrons, $"{frameName} carries no pixel size");
            }
            // Clamp rather than trust: a header that omits XBINNING leaves BinX at its 1 default, and a
            // nonsensical 0 or negative must never scale the pitch to zero and take the whole sensor extent
            // with it.
            int binning = Math.Max(binX, 1);
            double microns = headerPixelSizeMicrons * binning;
            string provenance = binning > 1
                ? $"frame header: {Format(headerPixelSizeMicrons)} um native x {binning}x{binning} binning"
                : $"frame header: {Format(headerPixelSizeMicrons)} um, unbinned";
            return new Resolution(microns, provenance, fromFrameHeader: true);
        }

        /// <summary>The fallback: no frame could answer, so the metadata's stored value stands — with the reason
        /// carried in the provenance, because on a pre-schema-4 binned run that value is the one that inflates
        /// the result and the reader deserves to know the harness had to trust it.</summary>
        public static Resolution FromMetadata(double metadataPixelSizeMicrons, string reason) =>
            new Resolution(metadataPixelSizeMicrons, $"metadata ({reason})", fromFrameHeader: false);

        /// <summary>True when the resolved pitch and the metadata's stored one are genuinely different numbers —
        /// the signal that this run predates metadata schema 4 and was captured binned.</summary>
        public static bool DisagreesWithMetadata(Resolution resolution, double metadataPixelSizeMicrons) =>
            metadataPixelSizeMicrons > 0
            && Math.Abs(resolution.Microns - metadataPixelSizeMicrons) > DisagreementToleranceMicrons;

        private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
