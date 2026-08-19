#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard;
using System;
using System.Globalization;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual {

    /// <summary>
    /// Supplies the name a tilt surface should print for a screw. Every surface that used to hard-code
    /// "Screw 1" resolves through this instead, so one user edit renames the guidance table, the wizard
    /// prompts, the approval dialog, and the diagram together.
    /// </summary>
    public interface IScrewLabelProvider {

        /// <summary>
        /// Display name for a 1-based wizard screw number: the user's label if they set one for the
        /// selected device, otherwise that device's default name.
        /// </summary>
        string Label(int wizardScrewNumber);
    }

    /// <summary>
    /// Resolves screw display names, and formats the phrasings that pair a name with the identity
    /// underneath it.
    ///
    /// Lives beside <see cref="TiltAdapterCorner"/> on purpose. Labels are indexed by WIZARD SCREW number,
    /// and the default names for a motorized adapter are its DEVICE MOTOR numbers -- exactly the
    /// conversion that table exists to own, and that the table's own remarks call the most repeated bug in
    /// this feature. Resolve through it; never write out the 3-to-4 swap by hand.
    /// </summary>
    public static class TiltScrewLabels {

        /// <summary>
        /// The names in effect for <paramref name="options"/>'s currently selected device. The provider
        /// reads the options live, so it stays correct across device changes and label edits -- hold it
        /// for the life of a view model rather than re-resolving per call.
        /// </summary>
        public static IScrewLabelProvider For(ITiltAdapterOptions options) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }
            return new OptionsScrewLabelProvider(options);
        }

        /// <summary>
        /// Names for a scheme with no user labels applied -- the wizard's own numbering, or a device's
        /// motor names. Used where no options object is in reach (pure formatters, tests).
        /// </summary>
        public static IScrewLabelProvider ForScheme(ScrewLabelScheme scheme) {
            if (scheme == null) {
                throw new ArgumentNullException(nameof(scheme));
            }
            return new SchemeScrewLabelProvider(scheme);
        }

        /// <summary>Names as they were before this feature existed: "Screw 1".."Screw 4".</summary>
        public static IScrewLabelProvider Default { get; } = ForScheme(ScrewLabelScheme.Generic);

        /// <summary>
        /// "M4 (screw 3)" -- the label with the wizard number that identifies it, or just "Screw 3" when
        /// the label IS the wizard number and repeating it would read as a stutter. For the calibration
        /// surfaces where a reader has to map a name back to a position.
        /// </summary>
        public static string WithScrewNumber(IScrewLabelProvider labels, int wizardScrewNumber) {
            var label = Resolve(labels, wizardScrewNumber);
            var plain = ScrewLabelScheme.Generic.DefaultLabel(wizardScrewNumber);
            return string.Equals(label, plain, StringComparison.Ordinal)
                ? label
                : string.Format(CultureInfo.CurrentCulture, "{0} (screw {1})", label, wizardScrewNumber);
        }

        /// <summary>
        /// The full identity behind a label, for the tooltip on a surface too narrow to show it:
        /// "Screw 3 · BL · Motor 4" on a 4-corner adapter, "Screw 3" on anything else.
        /// </summary>
        public static string DescribeScrew(int wizardScrewNumber, int screwCount) {
            var plain = ScrewLabelScheme.Generic.DefaultLabel(wizardScrewNumber);
            if (screwCount != 4) {
                return plain;
            }
            var corner = TiltAdapterCorner.ForWizardScrew(wizardScrewNumber);
            return string.Format(CultureInfo.CurrentCulture, "{0} · {1} · Motor {2}", plain, corner.Label, corner.DeviceMotorNumber);
        }

        /// <summary>
        /// True when the user (or the selected device) supplies names that are not simply the wizard's own
        /// numbering -- the test for whether prose should introduce the names or teach the numbering.
        /// </summary>
        public static bool AnyNamed(IScrewLabelProvider labels, int screwCount) {
            for (int screw = 1; screw <= Math.Min(screwCount, 4); ++screw) {
                if (!string.Equals(Resolve(labels, screw), ScrewLabelScheme.Generic.DefaultLabel(screw), StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Null-tolerant lookup, so a surface with no provider wired yet still renders.</summary>
        internal static string Resolve(IScrewLabelProvider labels, int wizardScrewNumber) =>
            (labels ?? Default).Label(wizardScrewNumber);

        private sealed class OptionsScrewLabelProvider : IScrewLabelProvider {
            private readonly ITiltAdapterOptions options;

            public OptionsScrewLabelProvider(ITiltAdapterOptions options) {
                this.options = options;
            }

            public string Label(int wizardScrewNumber) {
                var scheme = TiltAdapterDevicePreset.ByName(options.DeviceName).ScrewLabels;
                var custom = options.GetScrewLabelOverride(wizardScrewNumber);
                return string.IsNullOrEmpty(custom) ? scheme.DefaultLabel(wizardScrewNumber) : custom;
            }
        }

        private sealed class SchemeScrewLabelProvider : IScrewLabelProvider {
            private readonly ScrewLabelScheme scheme;

            public SchemeScrewLabelProvider(ScrewLabelScheme scheme) {
                this.scheme = scheme;
            }

            public string Label(int wizardScrewNumber) => scheme.DefaultLabel(wizardScrewNumber);
        }
    }
}
