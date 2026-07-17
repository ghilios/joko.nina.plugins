#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Sensors {

    /// <summary>
    /// Piecewise-linear quantum-efficiency curve defined by a small set of (wavelength, QE) anchors.
    /// Between anchors the QE is interpolated linearly; outside the anchor range it clamps to the
    /// nearest endpoint value.
    ///
    /// All four Sony BSI sensors modeled by the camera simulator share the single visible-light curve
    /// exposed as <see cref="SonyBsiVisible"/>. Peak absolute QE is modeled at ~80% (not the vendor's
    /// optimistic ~90% relative-response figure); IMX455 was measured at ~80% in arXiv:2302.03700.
    /// </summary>
    public sealed class QeCurve {
        private readonly double[] wavelengthsNm;
        private readonly double[] qe;
        private readonly ReadOnlyCollection<double> wavelengthsView;
        private readonly ReadOnlyCollection<double> qeView;

        public QeCurve(IReadOnlyList<double> wavelengthsNm, IReadOnlyList<double> qeValues) {
            if (wavelengthsNm == null) throw new ArgumentNullException(nameof(wavelengthsNm));
            if (qeValues == null) throw new ArgumentNullException(nameof(qeValues));
            if (wavelengthsNm.Count != qeValues.Count) {
                throw new ArgumentException("Wavelength and QE anchor arrays must have the same length.");
            }
            if (wavelengthsNm.Count < 2) {
                throw new ArgumentException("At least two anchors are required to define a QE curve.");
            }
            for (int i = 1; i < wavelengthsNm.Count; i++) {
                if (wavelengthsNm[i] <= wavelengthsNm[i - 1]) {
                    throw new ArgumentException("Wavelength anchors must be strictly ascending.");
                }
            }

            this.wavelengthsNm = wavelengthsNm.ToArray();
            this.qe = qeValues.ToArray();
            this.wavelengthsView = Array.AsReadOnly(this.wavelengthsNm);
            this.qeView = Array.AsReadOnly(this.qe);
        }

        /// <summary>The wavelength anchors (nm), ascending. Read-only view over the backing array.</summary>
        public IReadOnlyList<double> WavelengthsNm => wavelengthsView;

        /// <summary>The QE value at each anchor. Read-only view over the backing array.</summary>
        public IReadOnlyList<double> QeValues => qeView;

        /// <summary>
        /// Quantum efficiency (0..1) at the given wavelength (nm). Linear interpolation between anchors;
        /// clamped to the endpoint value outside the anchor range.
        /// </summary>
        public double EvaluateAt(double wavelengthNm) {
            if (wavelengthNm <= wavelengthsNm[0]) {
                return qe[0];
            }
            var last = wavelengthsNm.Length - 1;
            if (wavelengthNm >= wavelengthsNm[last]) {
                return qe[last];
            }

            // Locate the bracketing interval [i, i+1].
            for (int i = 0; i < last; i++) {
                var x0 = wavelengthsNm[i];
                var x1 = wavelengthsNm[i + 1];
                if (wavelengthNm >= x0 && wavelengthNm <= x1) {
                    var frac = (wavelengthNm - x0) / (x1 - x0);
                    return qe[i] + (qe[i + 1] - qe[i]) * frac;
                }
            }

            // Unreachable given the guards above, but keep the compiler happy.
            return qe[last];
        }

        /// <summary>
        /// The shared Sony BSI visible-light QE curve used by all four simulated sensors (v1).
        /// Anchors from the design's sensor reference table.
        /// </summary>
        public static QeCurve SonyBsiVisible { get; } = new QeCurve(
            new double[] { 450, 475, 500, 530, 656, 672 },
            new double[] { 0.82, 0.80, 0.78, 0.75, 0.50, 0.46 });
    }
}
