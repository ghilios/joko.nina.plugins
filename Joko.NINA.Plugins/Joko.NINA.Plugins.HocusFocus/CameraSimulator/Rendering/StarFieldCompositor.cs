#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Threading;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.Rendering {

    /// <summary>
    /// Phase 0 stub. The real render pipeline (ASTAP query → TAN projection → per-star local defocus → PSF
    /// kernel → stamp → sky/dark → Poisson/read noise → <c>ushort[]</c>) is filled in by Phase 4. This stub
    /// exists so the device skeleton, options, MEF registration, and the disconnected-guard error paths can
    /// be wired and tested now.
    /// </summary>
    public class StarFieldCompositor : IStarFieldCompositor {

        public ushort[] Render(RenderRequest request, CancellationToken token) {
            throw new NotImplementedException("Star-field rendering is implemented in Phase 4");
        }
    }
}
