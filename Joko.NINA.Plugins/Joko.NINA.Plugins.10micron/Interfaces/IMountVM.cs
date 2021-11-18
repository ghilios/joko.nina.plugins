#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.Equipment;
using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using NINA.Equipment.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.TenMicron.Interfaces {

    public interface IMountVM : IDeviceVM<MountInfo>, IDockableVM {

        string GetModelName(int modelIndex);

        int GetModelCount();

        bool LoadModel(string name);

        bool SaveModel(string name);

        bool DeleteModel(string name);

        void DeleteAlignment();

        int GetAlignmentStarCount();

        AlignmentStarInfo GetAlignmentStarInfo(int alignmentStarIndex);

        AlignmentModelInfo GetAlignmentModelInfo();

        bool StartNewAlignmentSpec();

        bool FinishAlignmentSpec();

        CoordinateAngle GetMountReportedDeclination();

        AstrometricTime GetMountReportedRightAscension();

        AstrometricTime GetMountReportedLocalSiderealTime();
    }
}