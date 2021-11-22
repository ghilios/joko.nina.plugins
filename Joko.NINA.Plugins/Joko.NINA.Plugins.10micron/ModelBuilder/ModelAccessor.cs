#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.Interfaces;
using Joko.NINA.Plugins.TenMicron.Model;
using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Joko.NINA.Plugins.TenMicron.ModelBuilder {

    public class ModelAccessor : IModelAccessor {
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IMountMediator mountMediator;
        private readonly ICustomDateTime dateTime;

        public ModelAccessor(ITelescopeMediator telescopeMediator, IMountMediator mountMediator, ICustomDateTime dateTime) {
            this.telescopeMediator = telescopeMediator;
            this.mountMediator = mountMediator;
            this.dateTime = dateTime;
        }

        public LoadedAlignmentModel LoadActiveModel(string modelName = null, DateTime? modelCreationTime = null, CancellationToken ct = default) {
            var alignmentStarCount = mountMediator.GetAlignmentStarCount();
            ct.ThrowIfCancellationRequested();

            if (alignmentStarCount > 0) {
                var telescopeInfo = telescopeMediator.GetInfo();
                var alignmentModelInfo = mountMediator.GetAlignmentModelInfo();
                ct.ThrowIfCancellationRequested();

                var alignmentStars = new List<AlignmentStarInfo>(alignmentStarCount);
                for (int i = 1; i <= alignmentStarCount; ++i) {
                    alignmentStars.Add(mountMediator.GetAlignmentStarInfo(i));
                    ct.ThrowIfCancellationRequested();
                }

                return new LoadedAlignmentModel(
                    alignmentModelInfo: alignmentModelInfo,
                    alignmentStars: alignmentStars,
                    latitude: Angle.ByDegree(telescopeInfo.SiteLatitude),
                    longitude: Angle.ByDegree(telescopeInfo.SiteLongitude),
                    // TODO: FIXME
                    siteElevation: 0, // telescopeInfo.SiteElevation,
                    modelCreationTime: modelCreationTime ?? this.dateTime.Now,
                    modelName: modelName);
            }
            return LoadedAlignmentModel.Empty();
        }
    }
}