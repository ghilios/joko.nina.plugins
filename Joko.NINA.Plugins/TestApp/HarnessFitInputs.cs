#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System.Globalization;

namespace TestApp {

    /// <summary>
    /// The four values that reach the AF <b>fit</b> — read once, used to build the fit AND to render the
    /// <c>FitInputs</c> provenance field, so the values that drive a landing and the values recorded beside it
    /// <b>cannot diverge</b>.
    ///
    /// <para><b>Why this type exists (F58).</b> <c>optimize</c> pinned the DETECTOR knobs with
    /// <c>--settings</c> and read these four straight off <c>AutoFocusOptions</c>, which binds a
    /// <c>PluginOptionsAccessor</c> to whichever NINA profile is ACTIVE. So the detector was pinned and the fit
    /// was not, and six waves read <c>--settings</c> as pinning the arm. Wave 11 measured the consequence: the
    /// machine's nine profiles partition <b>2 / 7</b> on <c>MaxOutlierRejections</c> alone, concurrent
    /// <c>optimize</c> processes each silently acquire a DIFFERENT profile (NINA holds the <c>.profile</c> file
    /// open, and <c>TryLoad</c> skips a locked one and takes the next by <c>LastUsed</c>), and the resulting two
    /// discrete values of <c>J</c> were chased as a floating-point race across two waves.</para>
    ///
    /// <para>A guard test would keep the printed list in sync with the read list only until someone forgot to
    /// update it. Routing both through one type removes the failure mode instead of watching for it — the same
    /// move as <c>ConcurrencyCheck</c> replacing "say so in the run instructions".</para>
    /// </summary>
    internal sealed class HarnessFitInputs {

        private HarnessFitInputs(bool useWeights, int maxOutlierRejections, double rejectionConfidence, HyperbolicFitModel preferredModel) {
            UseWeights = useWeights;
            MaxOutlierRejections = maxOutlierRejections;
            RejectionConfidence = rejectionConfidence;
            PreferredModel = preferredModel;
        }

        public bool UseWeights { get; }

        public int MaxOutlierRejections { get; }

        public double RejectionConfidence { get; }

        public HyperbolicFitModel PreferredModel { get; }

        public static HarnessFitInputs From(IAutoFocusOptions options) => new HarnessFitInputs(
            options.WeightedHyperbolicFitEnabled,
            options.MaxOutlierRejections,
            options.OutlierRejectionConfidence,
            options.HyperbolicFitModel);

        /// <summary>
        /// <c>key=value;…</c>, invariant-culture, stable ordering. This string goes into every landing's
        /// <c>OptimizerProvenance.FitInputs</c> — values rather than a hash, because a hash tells a reader that
        /// something moved and these tell them which one.
        /// </summary>
        public override string ToString() =>
            $"MaxOutlierRejections={MaxOutlierRejections.ToString(CultureInfo.InvariantCulture)};" +
            $"OutlierRejectionConfidence={RejectionConfidence.ToString("R", CultureInfo.InvariantCulture)};" +
            $"WeightedHyperbolicFitEnabled={UseWeights};" +
            $"HyperbolicFitModel={PreferredModel}";
    }
}
