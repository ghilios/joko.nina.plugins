#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.Optimization {

    /// <summary>
    /// F35 — seeds <see cref="StarDetectorParams.MinHFR"/> beneath the gate on rigs whose stars are smaller than
    /// it, so the optimizer starts somewhere with a gradient instead of on a plateau where <c>J</c> is identically
    /// zero (F20).
    ///
    /// <para><b>The fit TRIGGERS the seed; it never SIZES it.</b> That split is the whole finding. Every HFR
    /// statistic available before the search reads high, and both do so in the direction that would make a
    /// fit-derived seed useless:</para>
    /// <list type="bullet">
    /// <item>the wing-only hyperbola vertex over-predicts by <b>2.3x</b> on D01 (0.548 px read vs 0.238 px truth) —
    /// a hyperbola's vertex is the parameter its wings constrain worst; and</item>
    /// <item>the median measured HFR of surviving stars reads <b>2.2–2.8x</b> high because it is
    /// <b>left-censored at <see cref="StarDetectorParams.MinHFR"/> itself</b> — only stars measuring above the
    /// gate can be seen, so the sample's median is bounded below by the very knob being tuned. Structurally the
    /// same trap as tuning Sensitivity from the marginal-SNR distribution it truncated (F23).</item>
    /// </list>
    /// <para>Both biases UNDERSTATE how far the gate must come down, so a plausible rule like
    /// <c>0.8 x predicted</c> yields 0.44 on D01 and still gates its true 0.238 px vertex completely — the fix
    /// would have looked applied and changed nothing.</para>
    ///
    /// <para><b>Why a constant is the geometric answer.</b> <see cref="StarDetectorParams.MinHFR"/> is already
    /// dimensionally self-contained: from the software-binning stage onward the whole pipeline runs in binned
    /// pixels, so every pixel-unit param "stays in the range it was calibrated for regardless of the rig"
    /// (StarDetector.cs:483-486). There is no arcsec/px conversion to apply. What bounds the seed is sampling:
    /// HFR cannot fall far below pixel quantization, because a star whose flux lands in essentially one pixel
    /// still measures a few tenths of a pixel.</para>
    ///
    /// <para><b>That argument covers the seeded VALUE only — not the trigger (F38).</b> The same sentence in
    /// StarDetector.cs:483-486 says the other half too: "GateAndMeasureInternal scales every pixel-space output
    /// back to source pixels before returning, so callers never see binned units." This class is exactly such a
    /// caller. <paramref name="Resolve.currentMinHfr"/> is an un-rescaled PARAM (binned) while
    /// <paramref name="Resolve.fitVertexHfr"/> is a rescaled OUTPUT (captured), so the trigger is a gate
    /// comparison pair that has to be put back into one space by hand — which is what
    /// <c>detectionBinning</c> is for. Elsewhere the detector is careful about precisely this:
    /// <c>RejectedCandidateRecord.MeasuredValue</c>/<c>ThresholdValue</c> are deliberately NOT rescaled because
    /// they are "the gate's own comparison pair" and "must stay in the same space" (StarDetector.cs:847-857).</para>
    ///
    /// <para><b>The value is measured, not argued.</b> <c>golden eval --min-hfr</c> over 8 values from 1.2 to 0.1
    /// on D01/D02/D03 plus the D05 control scores <b>zero false positives in all 32 configurations</b>, with the
    /// recall gain saturating by 0.5. <c>D05_tec140_1000mm</c> (truth vertex 1.80 px) is <b>flat at 0.985 across
    /// the entire range</b> — a rig that does not need the seed is provably unperturbed, which is what lets the
    /// rule be evaluated without first detecting the wide-field case the gate has already hidden.</para>
    ///
    /// <para><b>The success criterion is the hard floor, not recall.</b> Lowering D01's gate moves recall only
    /// 0.129 → 0.165 (<c>TooLowHFR</c> is 1877 of ~38500 missed stars; the structure map never proposes 18801 of
    /// them). What it does is put <b>5 stars on the vertex frame</b>, clearing the objective's <c>NHard</c> = 3
    /// per-frame requirement — which is the entire cause of <c>FinalJ = 0.00000</c>.</para>
    /// </summary>
    public static class MinHfrSeed {

        /// <summary>
        /// The seeded gate, in BINNED detection pixels. Mid-band of the measured-safe 0.25–0.35 px window, and
        /// comfortably above <c>OptimizerVariable</c>'s <c>MinHFRLower</c> (0.1) so it is never silently raised
        /// when the search reads it into theta0 (StarDetectionOptimizer.cs:115).
        /// </summary>
        public const double SeedFloor = 0.30;

        /// <summary>
        /// The MinHFR to seed the search with, or <c>null</c> to leave the seed untouched.
        /// </summary>
        /// <param name="fitVertexHfr">
        /// <c>BestFit.Minimum.Y</c> in CAPTURED (source) pixels — see <paramref name="detectionBinning"/>, which
        /// converts it. From a hyperbola fit taken BEFORE the search — the wing frames are far from
        /// focus, so their PSF is large and the gate does not touch them (D01's four outer frames carry 816–2002
        /// accepted stars each at MinHFR 1.2, and the fit succeeds at R² = 0.911). Pass <see cref="double.NaN"/>
        /// when no fit is available.
        /// </param>
        /// <param name="currentMinHfr">The gate the search would otherwise start from, in BINNED pixels.</param>
        /// <param name="detectionBinning">
        /// <c>StarDetectorParams.DetectionBinning</c> for the same params bundle <paramref name="currentMinHfr"/>
        /// came from. REQUIRED because the two inputs live in different pixel spaces (F38): the gate fires inside
        /// the binned raster (<c>StarDetector.cs:1894</c>) while every HFR the detector REPORTS has already been
        /// scaled back to source pixels (<c>StarDetector.cs:805-808</c>, <c>ScaleToSourcePixels</c> multiplies HFR
        /// by the factor). Comparing them directly made the trigger conservative by exactly the binning factor —
        /// strictly missed seeds, never spurious, and invisible because the wing frames still fit, so
        /// <c>BestFit.Minimum.Y</c> was finite and the rule took the "clears the gate" branch on a rig that
        /// emphatically did not. Latent headless (TestApp never leaves the default of 1, so no F35 bank result was
        /// affected); ACTIVE in the wizard, which stamps the user's real profile/per-filter factor.
        /// </param>
        /// <remarks>
        /// The rule only ever LOWERS. The objective rewards star count, so nothing pulls a seeded MinHFR back up —
        /// it is a knob the search cannot climb out of, which is why it comes from geometry rather than from a
        /// measurement the gate itself shaped, and why it must never raise a gate a caller deliberately set lower.
        /// </remarks>
        /// <summary>
        /// Whether this rig's in-focus stars measure at or below the gate — i.e. whether the gate is emptying the
        /// V-curve's core (F20).
        ///
        /// <para>Shared deliberately with <see cref="Resolve"/> so the SEED and the user-facing message that
        /// reports it cannot disagree. F20 part 1 is "say this happened" and part 2 is "do something about it";
        /// two copies of the comparison would eventually drift into a wizard that seeds silently while telling the
        /// user nothing, or warns about a gate it did not actually move.</para>
        /// </summary>
        /// <param name="fitVertexHfr">The fit vertex in CAPTURED pixels; see <see cref="Resolve"/>.</param>
        /// <param name="currentMinHfr">The gate, in BINNED pixels.</param>
        /// <param name="detectionBinning">The factor that relates the two (F38).</param>
        public static bool IsBelowGate(double fitVertexHfr, double currentMinHfr, int detectionBinning) {
            if (!IsUsable(fitVertexHfr) || !IsUsable(currentMinHfr)) {
                return false;
            }
            // Into the gate's own space before comparing. SeedFloor needs no such conversion: it is already binned,
            // and it is written to MinHFR, which is binned.
            return fitVertexHfr / Math.Max(1, detectionBinning) <= currentMinHfr;
        }

        public static double? Resolve(double fitVertexHfr, double currentMinHfr, int detectionBinning) {
            if (!IsUsable(fitVertexHfr)) {
                return null;                      // no fit => no trigger; never re-gate off a statistic that does not exist
            }
            if (!IsBelowGate(fitVertexHfr, currentMinHfr, detectionBinning)) {
                return null;                      // the rig's stars clear the gate; leave it alone (the D05 case)
            }
            if (!(SeedFloor < currentMinHfr)) {
                return null;                      // already at or beneath the floor; only ever lower
            }
            return SeedFloor;
        }

        private static bool IsUsable(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
