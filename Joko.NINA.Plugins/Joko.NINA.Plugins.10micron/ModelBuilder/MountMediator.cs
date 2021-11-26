#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.WPF.Base.Mediator;
using Joko.NINA.Plugins.TenMicron.Interfaces;
using Joko.NINA.Plugins.TenMicron.Equipment;

namespace Joko.NINA.Plugins.TenMicron.ModelBuilder {

    public class MountMediator : DeviceMediator<IMountVM, IMountConsumer, MountInfo>, IMountMediator {

        public void DeleteAlignment() {
            handler.DeleteAlignment();
        }

        public bool DeleteAlignmentStar(int alignmentStarIndex) {
            return handler.DeleteAlignmentStar(alignmentStarIndex);
        }

        public bool DeleteModel(string name) {
            return handler.DeleteModel(name);
        }

        public bool FinishAlignmentSpec() {
            return handler.FinishAlignmentSpec();
        }

        public AlignmentModelInfo GetAlignmentModelInfo() {
            return handler.GetAlignmentModelInfo();
        }

        public int GetAlignmentStarCount() {
            return handler.GetAlignmentStarCount();
        }

        public AlignmentStarInfo GetAlignmentStarInfo(int alignmentStarIndex) {
            return handler.GetAlignmentStarInfo(alignmentStarIndex);
        }

        public int GetModelCount() {
            return handler.GetModelCount();
        }

        public string GetModelName(int modelIndex) {
            return handler.GetModelName(modelIndex);
        }

        public CoordinateAngle GetMountReportedDeclination() {
            return handler.GetMountReportedDeclination();
        }

        public AstrometricTime GetMountReportedLocalSiderealTime() {
            return handler.GetMountReportedLocalSiderealTime();
        }

        public AstrometricTime GetMountReportedRightAscension() {
            return handler.GetMountReportedRightAscension();
        }

        public bool LoadModel(string name) {
            return handler.LoadModel(name);
        }

        public bool SaveModel(string name) {
            return handler.SaveModel(name);
        }

        public bool StartNewAlignmentSpec() {
            return handler.StartNewAlignmentSpec();
        }
    }
}