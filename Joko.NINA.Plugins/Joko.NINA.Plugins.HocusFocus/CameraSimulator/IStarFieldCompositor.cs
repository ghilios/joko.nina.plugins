#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator {

    /// <summary>
    /// Renders a synthetic star field into a row-major 16-bit frame from an immutable <see cref="RenderRequest"/>.
    /// The real physics pipeline (ASTAP query → projection → PSF → noise) is implemented in Phase 4.
    /// </summary>
    public interface IStarFieldCompositor {

        /// <summary>
        /// Render the exposure described by <paramref name="request"/> into a row-major
        /// <c>ushort[width * height]</c> frame. Sensor geometry is derived from <see cref="RenderRequest.SensorModel"/>.
        /// </summary>
        ushort[] Render(RenderRequest request, CancellationToken token);
    }
}
