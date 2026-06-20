#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class TiltAdapterGuidanceVM {
        public int ScrewCount { get; set; }
        public bool HasFourScrews => ScrewCount == 4;
        public bool HasTiltGuidance { get; set; }
        public bool HasBackfocusRow { get; set; }

        public string Screw1TiltArrow { get; set; } = "—";
        public string Screw2TiltArrow { get; set; } = "—";
        public string Screw3TiltArrow { get; set; } = "—";
        public string Screw4TiltArrow { get; set; } = "—";

        public string Screw1BackfocusArrow { get; set; } = "—";
        public string Screw2BackfocusArrow { get; set; } = "—";
        public string Screw3BackfocusArrow { get; set; } = "—";
        public string Screw4BackfocusArrow { get; set; } = "—";

        // Precise numeric adjustments (turns for screws, whole steps for steppers). Populated only
        // when the adapter hardware (thread pitch / step size + screw radius) is configured.
        public bool HasNumericGuidance { get; set; }
        public bool UnitsAreSteps { get; set; }

        // Per-screw tilt and backfocus magnitudes (direction is shown by the arrows above), and the
        // signed total with an explicit IN/OUT direction word.
        public string Screw1TiltAmount { get; set; } = "—";
        public string Screw2TiltAmount { get; set; } = "—";
        public string Screw3TiltAmount { get; set; } = "—";
        public string Screw4TiltAmount { get; set; } = "—";

        public string Screw1BackfocusAmount { get; set; } = "—";
        public string Screw2BackfocusAmount { get; set; } = "—";
        public string Screw3BackfocusAmount { get; set; } = "—";
        public string Screw4BackfocusAmount { get; set; } = "—";

        public string Screw1TotalAmount { get; set; } = "—";
        public string Screw2TotalAmount { get; set; } = "—";
        public string Screw3TotalAmount { get; set; } = "—";
        public string Screw4TotalAmount { get; set; } = "—";

        // Set when the saved pitch/step size diverges from the value the wizard last measured.
        public string PitchMismatchWarning { get; set; } = string.Empty;
        public bool HasPitchMismatch => !string.IsNullOrEmpty(PitchMismatchWarning);

        /// <summary>
        /// Format an unsigned per-screw magnitude. Steppers round to whole steps (the user's choice);
        /// screws show two decimals of a turn. Values that round to nothing render as an em dash.
        /// </summary>
        public static string FormatMagnitude(double amount, bool steps) {
            double a = Math.Abs(amount);
            if (steps) {
                long rounded = (long)Math.Round(a, MidpointRounding.AwayFromZero);
                return rounded == 0 ? "—" : $"{rounded} steps";
            }
            return a < 0.005 ? "—" : $"{a:0.00} turns";
        }

        /// <summary>
        /// Format the signed total adjustment with an explicit IN/OUT direction (positive = inward).
        /// When the inward direction is not calibrated (<paramref name="hasDirection"/> false) only the
        /// magnitude is shown.
        /// </summary>
        public static string FormatTotal(double inwardAmount, bool steps, bool hasDirection) {
            string magnitude;
            if (steps) {
                long rounded = (long)Math.Round(Math.Abs(inwardAmount), MidpointRounding.AwayFromZero);
                if (rounded == 0) return "—";
                magnitude = $"{rounded} steps";
            } else {
                if (Math.Abs(inwardAmount) < 0.005) return "—";
                magnitude = $"{Math.Abs(inwardAmount):0.00} turns";
            }
            if (!hasDirection) return magnitude;
            return $"{magnitude} {(inwardAmount >= 0 ? "IN" : "OUT")}";
        }
    }
}
