#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using NINA.Joko.Plugins.HocusFocus.Interfaces;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    public class TiltAdapterGuidanceVM {
        public int ScrewCount { get; set; }
        public bool HasFourScrews => ScrewCount == 4;
        public bool HasTiltGuidance { get; set; }
        public bool HasBackfocusRow { get; set; }

        // Gate for the 4th-screw backfocus arrow: needs both the 4th screw to exist and the backfocus
        // row to be active. Exposed as a single bool so the XAML can use one plain Visibility binding
        // (like every other cell) instead of a MultiDataTrigger, which did not re-apply when the whole
        // guidance object is swapped (this VM raises no per-property change notifications).
        public bool HasFourScrewBackfocus => HasFourScrews && HasBackfocusRow;

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

        // The Turns/Degrees dropdown is meaningful only for screw adapters with numeric guidance;
        // steppers always show whole steps. Single derived bool so the XAML uses one plain Visibility
        // binding (mirrors HasFourScrewBackfocus).
        public bool ShowAngleUnitSelector => HasNumericGuidance && !UnitsAreSteps;

        // Per-screw signed adjustments, all three rows carrying their own rotation direction
        // (⟳/⟲ glyphs for screws, +/− signs for steppers). The arrows grid above describes adapter
        // MOTION (⬆ = toward the objective), not rotation — the two answer different questions.
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

        // One-line legend defining the motion arrows and rotation glyphs; empty until guidance renders.
        public string DirectionLegend { get; set; } = string.Empty;
        public bool HasDirectionLegend => !string.IsNullOrEmpty(DirectionLegend);

        /// <summary>
        /// Fixed legend for the guidance table: ⬆/⬇ describe adapter-plate motion (toward the
        /// objective / toward the camera — pure physics, identical on every rig), and ⟳/⟲ (screws)
        /// or the +/− step sign (steppers) describe the rig-specific rotation that produces it.
        /// "(assumed)" flags a direction setting never verified by a wizard measurement.
        /// </summary>
        public static string BuildDirectionLegend(bool steps, bool signIsMeasured, TiltGuidanceAngleUnit angleUnit) {
            string unitWord = angleUnit == TiltGuidanceAngleUnit.Degrees ? "degrees" : "turns";
            string body = steps
                ? "⬆ = adapter moves toward the objective · steps are signed as in the wizard prompts"
                : $"⬆ = adapter moves toward the objective · ⟳ = clockwise (tighten) · amounts in {unitWord}";
            string assumed = signIsMeasured ? string.Empty : " (assumed — set or measure in the Tilt Adapter Wizard)";
            return body + assumed;
        }

        /// <summary>
        /// Format a signed per-screw adjustment. Positive = clockwise / the wizard-prompt "+" step
        /// direction. Screws render the magnitude with a rotation glyph — either turns ("1.25 ⟳",
        /// 2 decimals) or whole degrees ("45° ⟳", 1 turn = 360°) per <paramref name="angleUnit"/>;
        /// steppers render signed whole steps ("+35 steps") and ignore the unit. Values below the
        /// 0.005-turn noise floor render as an em dash with no direction mark (so the smallest shown
        /// degree value is ~2°).
        /// </summary>
        public static string FormatAmount(double signedAmount, bool steps, TiltGuidanceAngleUnit angleUnit) {
            if (steps) {
                long rounded = (long)Math.Round(Math.Abs(signedAmount), MidpointRounding.AwayFromZero);
                if (rounded == 0) return "—";
                return signedAmount >= 0 ? $"+{rounded} steps" : $"−{rounded} steps";
            }
            if (Math.Abs(signedAmount) < 0.005) return "—";
            string glyph = signedAmount >= 0 ? "⟳" : "⟲";
            if (angleUnit == TiltGuidanceAngleUnit.Degrees) {
                long degrees = (long)Math.Round(Math.Abs(signedAmount) * 360.0, MidpointRounding.AwayFromZero);
                return $"{degrees}° {glyph}";
            }
            return $"{Math.Abs(signedAmount):0.00} {glyph}";
        }
    }
}
