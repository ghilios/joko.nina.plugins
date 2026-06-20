#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.TiltAdapterWizard {

    /// <summary>
    /// A known tilt-adapter device. A preset pre-fills the adapter-hardware fields (screw count,
    /// adjustment type, thread pitch / stepper step size, screw radius) and locks them in the wizard.
    /// "Manual" is the sentinel that leaves every field user-editable. To add a device, append one
    /// entry to <see cref="All"/> — no other code changes are required.
    /// </summary>
    public sealed class TiltAdapterDevicePreset {
        public const string ManualName = "Manual";

        private TiltAdapterDevicePreset(
            string name, bool isManual, int screwCount, TiltAdjustmentType adjustmentType,
            double threadPitchMicrons, double stepperStepSizeMicrons, double screwRadiusMillimeters) {
            Name = name;
            IsManual = isManual;
            ScrewCount = screwCount;
            AdjustmentType = adjustmentType;
            ThreadPitchMicrons = threadPitchMicrons;
            StepperStepSizeMicrons = stepperStepSizeMicrons;
            ScrewRadiusMillimeters = screwRadiusMillimeters;
        }

        public string Name { get; }
        public bool IsManual { get; }
        public int ScrewCount { get; }
        public TiltAdjustmentType AdjustmentType { get; }
        public double ThreadPitchMicrons { get; }
        public double StepperStepSizeMicrons { get; }
        public double ScrewRadiusMillimeters { get; }

        public static readonly TiltAdapterDevicePreset Manual = new TiltAdapterDevicePreset(
            ManualName, isManual: true, screwCount: 3, adjustmentType: TiltAdjustmentType.Screws,
            threadPitchMicrons: -1, stepperStepSizeMicrons: -1, screwRadiusMillimeters: -1);

        public static readonly IReadOnlyList<TiltAdapterDevicePreset> All = new[] {
            Manual,
            new TiltAdapterDevicePreset(
                "Neumann CTU XT48", isManual: false, screwCount: 3, adjustmentType: TiltAdjustmentType.Screws,
                threadPitchMicrons: 400, stepperStepSizeMicrons: -1, screwRadiusMillimeters: 44),
        };

        public static TiltAdapterDevicePreset ByName(string name) =>
            All.FirstOrDefault(p => p.Name == name) ?? Manual;
    }
}
