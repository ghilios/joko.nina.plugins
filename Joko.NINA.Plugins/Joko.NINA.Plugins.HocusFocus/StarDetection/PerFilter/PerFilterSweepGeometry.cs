#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

    /// <summary>
    /// One filter's override of the auto-focus SWEEP GEOMETRY — the focuser steps between points, and the number
    /// of points per side of the curve minimum.
    ///
    /// <para>Deliberately NOT part of <see cref="AutoFocus.Replay.StarDetectionSettingsSnapshot"/>. That type is
    /// also the replay metadata payload, the replay options override handed to the detector, and the flat
    /// <see cref="Interfaces.IStarDetectionOptions"/> surface the import diff reflects over — and sweep geometry is
    /// none of those things. Putting it there would put focuser settings inside a JSON node named
    /// <c>starDetection</c>, and would force the import/export drift guard to classify it as either "importable"
    /// (untrue: it is not an interface property at all) or "machine-local" (also untrue — it is per-filter and meant
    /// to persist).</para>
    ///
    /// <para>It does travel between machines: <see cref="StarDetectionSettingsExport.SweepGeometry"/> carries it as
    /// a SIBLING node of the detection snapshot, and <c>StarDetectionSettingsDiff.BuildSweepGeometryDiff</c> puts it
    /// in the same import confirmation. A step size describes a particular focuser, so it is never applied without
    /// those rows on screen first.</para>
    ///
    /// <para><see cref="Inherit"/> on a field means "use the profile's FocuserSettings value". The sentinel mirrors
    /// NINA core's own per-filter auto-focus idiom (<c>FilterInfo.AutoFocusExposureTime &gt; -1</c>, honored in
    /// <c>AutoFocusEngine.TakeExposure</c>). Core owns <c>FilterInfo</c>, so the plugin cannot add these two
    /// properties there; this is the closest faithful copy.</para>
    ///
    /// <para>The two fields are stored together because the Optimization Wizard recommends and applies them as a
    /// pair, but they resolve <b>independently</b>: a filter may override the step size and inherit the offset
    /// steps. Do not "fix" this into an all-or-nothing struct.</para>
    /// </summary>
    public sealed class PerFilterSweepGeometry {

        /// <summary>Sentinel meaning "no override — use the profile value".</summary>
        public const int Inherit = -1;

        public int StepSize { get; set; } = Inherit;

        public int InitialOffsetSteps { get; set; } = Inherit;

        // These predicates are the exact complement of the engine's own rejection in CaptureFixedSweepImpl
        // (`AutoFocusInitialOffsetSteps < 1 || AutoFocusStepSize <= 0`), so a stored override can never be the
        // thing that makes a run fail validation.
        [JsonIgnore]
        public bool HasStepSize => StepSize > 0;

        [JsonIgnore]
        public bool HasOffsetSteps => InitialOffsetSteps >= 1;

        [JsonIgnore]
        public bool IsUnset => !HasStepSize && !HasOffsetSteps;

        public PerFilterSweepGeometry Clone() => (PerFilterSweepGeometry)MemberwiseClone();

        /// <summary>
        /// A copy with anything unusable mapped back to <see cref="Inherit"/>. Applied on both write and read, so
        /// a hand-edited profile — or a future bug — cannot put a 0 or a negative step size in front of the engine.
        /// </summary>
        public PerFilterSweepGeometry Normalized() => new PerFilterSweepGeometry {
            StepSize = HasStepSize ? StepSize : Inherit,
            InitialOffsetSteps = HasOffsetSteps ? InitialOffsetSteps : Inherit
        };

        public static PerFilterSweepGeometry Unset() => new PerFilterSweepGeometry();
    }
}
