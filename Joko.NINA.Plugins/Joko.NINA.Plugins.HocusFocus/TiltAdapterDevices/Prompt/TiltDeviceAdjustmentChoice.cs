#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterDevices.Prompt {

    /// <summary>
    /// The immutable outcome of the tilt-device adjustment approval dialog. Not a simple enum because an
    /// approval must carry WHAT was approved: the group toggles the user left enabled and the exact
    /// <see cref="TiltAdapterMovePlan"/> that was on screen when Proceed was clicked — the executor must
    /// send precisely those moves, never a replanned variant the user did not see. Cancel (or closing the
    /// window) resolves to <see cref="Cancelled"/> with <see cref="Proceed"/> = false and no plan.
    /// </summary>
    public sealed class TiltDeviceAdjustmentChoice {

        /// <summary>The shared cancel/dismiss result: no moves were approved; nothing may be sent.</summary>
        public static readonly TiltDeviceAdjustmentChoice Cancelled =
            new TiltDeviceAdjustmentChoice(proceed: false, applyTilt: false, applyBackfocus: false, finalPlan: null);

        /// <summary>An approval carrying the toggles and the exact plan the user reviewed.</summary>
        public static TiltDeviceAdjustmentChoice Proceeded(bool applyTilt, bool applyBackfocus, TiltAdapterMovePlan finalPlan) {
            if (finalPlan == null) {
                throw new ArgumentNullException(nameof(finalPlan), "An approved choice must carry the reviewed plan.");
            }
            return new TiltDeviceAdjustmentChoice(proceed: true, applyTilt: applyTilt, applyBackfocus: applyBackfocus, finalPlan: finalPlan);
        }

        private TiltDeviceAdjustmentChoice(bool proceed, bool applyTilt, bool applyBackfocus, TiltAdapterMovePlan finalPlan) {
            Proceed = proceed;
            ApplyTilt = applyTilt;
            ApplyBackfocus = applyBackfocus;
            FinalPlan = finalPlan;
        }

        /// <summary>True only when the user explicitly clicked Proceed; false for Cancel or window close.</summary>
        public bool Proceed { get; }

        /// <summary>State of the Tilt group checkbox at the moment of approval.</summary>
        public bool ApplyTilt { get; }

        /// <summary>State of the Backfocus group checkbox at the moment of approval.</summary>
        public bool ApplyBackfocus { get; }

        /// <summary>
        /// The exact plan displayed when Proceed was clicked (the last replanner result). Null when
        /// <see cref="Proceed"/> is false.
        /// </summary>
        public TiltAdapterMovePlan FinalPlan { get; }
    }
}
