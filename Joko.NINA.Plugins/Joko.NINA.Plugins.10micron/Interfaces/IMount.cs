#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using NINA.Core.Enum;

namespace Joko.NINA.Plugins.TenMicron.Interfaces {

    public interface IMount {

        Response<CoordinateAngle> GetDeclination();

        Response<AstrometricTime> GetRightAscension();

        Response<AstrometricTime> GetLocalSiderealTime();

        Response<int> GetModelCount();

        Response<string> GetModelName(int modelIndex);

        Response<bool> LoadModel(string name);

        Response<bool> SaveModel(string name);

        Response<bool> DeleteModel(string name);

        void DeleteAlignment();

        Response<bool> DeleteAlignmentStar(int alignmentStarIndex);

        Response<int> GetAlignmentStarCount();

        Response<AlignmentStarInfo> GetAlignmentStarInfo(int alignmentStarIndex);

        Response<AlignmentModelInfo> GetAlignmentModelInfo();

        Response<bool> StartNewAlignmentSpec();

        Response<bool> FinishAlignmentSpec();

        Response<PierSide> GetSideOfPier();

        Response<int> AddAlignmentPointToSpec(
            AstrometricTime mountRightAscension,
            CoordinateAngle mountDeclination,
            PierSide sideOfPier,
            AstrometricTime plateSolvedRightAscension,
            CoordinateAngle plateSolvedDeclination,
            AstrometricTime localSiderealTime);

        Response<string> GetId();

        void SetUltraPrecisionMode();

        Response<ProductFirmware> GetProductFirmware();

        Response<int> GetMeridianSlewLimitDegrees();

        Response<decimal> GetSlewSettleTimeSeconds();

        Response<bool> SetSlewSettleTime(decimal seconds);

        Response<MountStatusEnum> GetStatus();

        Response<bool> GetUnattendedFlipEnabled();

        Response<decimal> GetTrackingRateArcsecsPerSec();

        void SetUnattendedFlip(bool enabled);

        void SetMaximumPrecision(ProductFirmware productFirmware);

        Response<bool> SetMeridianSlewLimit(int degrees);
    }
}