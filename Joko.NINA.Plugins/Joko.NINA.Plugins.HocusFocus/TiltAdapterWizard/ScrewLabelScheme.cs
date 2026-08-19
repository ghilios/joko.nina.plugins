#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Manual;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// The naming vocabulary a family of tilt adapters uses for its adjustment points, and the key its
    /// user-entered labels are stored under.
    ///
    /// A scheme exists because a label belongs to the DEVICE, not to the profile: a user who names the
    /// screws of a manual adapter and then switches to an EAT must get the EAT's names back, and their
    /// manual names back again on the way home. Each scheme therefore owns an independent slot in
    /// <c>ITiltAdapterOptions.ScrewLabelsJson</c>, and <see cref="Id"/> is a PERSISTED KEY — renaming one
    /// silently orphans every label a user stored under it.
    ///
    /// To give a new pre-labeled device its own vocabulary: add a scheme here and name it in that
    /// device's <see cref="TiltAdapterDevicePreset"/>. Nothing else needs to change.
    /// </summary>
    public sealed class ScrewLabelScheme {

        /// <summary>Persisted key for <see cref="Generic"/>.</summary>
        public const string GenericId = "Generic";

        /// <summary>Persisted key for <see cref="AsgEat"/>.</summary>
        public const string AsgEatId = "AsgEat";

        /// <summary>
        /// Longest label the editor accepts. The width-constrained surfaces (guidance table headers, the
        /// 2×2 position cells, the screw diagram) size themselves for this, so raising it means re-checking
        /// those three layouts, not just the TextBox.
        /// </summary>
        public const int MaxLabelLength = 12;

        private readonly Func<int, string> defaultLabel;

        private ScrewLabelScheme(string id, Func<int, string> defaultLabel) {
            Id = id;
            this.defaultLabel = defaultLabel;
        }

        /// <summary>Stable persistence key. Never change these — see the class remarks.</summary>
        public string Id { get; }

        /// <summary>
        /// The name this scheme gives a 1-based wizard screw when the user has not entered one of their own.
        /// </summary>
        public string DefaultLabel(int wizardScrewNumber) {
            if (wizardScrewNumber < 1 || wizardScrewNumber > 4) {
                throw new ArgumentOutOfRangeException(nameof(wizardScrewNumber), wizardScrewNumber, "Wizard screw index must be 1-4.");
            }
            return defaultLabel(wizardScrewNumber);
        }

        /// <summary>
        /// "Screw 1".."Screw 4" — the wizard's own index, and the wording every tilt surface used before
        /// labels existed. An adapter on this scheme with no labels entered therefore reads exactly as it
        /// always has.
        /// </summary>
        public static readonly ScrewLabelScheme Generic = new ScrewLabelScheme(
            GenericId,
            n => string.Format(CultureInfo.CurrentCulture, "Screw {0}", n));

        /// <summary>
        /// The ASG Electronic EAT's motor names: M1, M2, M4, M3 for wizard screws 1, 2, 3, 4.
        ///
        /// That is not a typo — wizard screws 3 and 4 map to motors 4 and 3 (the wizard numbers clockwise,
        /// the device numbers its motors TR/TL/BR/BL). The permutation is resolved through
        /// <see cref="TiltAdapterCorner"/> rather than written out here, because that table is the one
        /// tested reconciliation of the three index spaces and hand-copying it is the most repeated bug in
        /// this feature.
        /// </summary>
        public static readonly ScrewLabelScheme AsgEat = new ScrewLabelScheme(
            AsgEatId,
            n => string.Format(CultureInfo.CurrentCulture, "M{0}", TiltAdapterCorner.ForWizardScrew(n).DeviceMotorNumber));

        public static readonly IReadOnlyList<ScrewLabelScheme> All =
            Array.AsReadOnly(new[] { Generic, AsgEat });

        /// <summary>Scheme for a persisted id, falling back to <see cref="Generic"/> for anything unknown.</summary>
        public static ScrewLabelScheme ById(string id) =>
            All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal)) ?? Generic;
    }
}
