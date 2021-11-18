#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.Equipment;
using Joko.NINA.Plugins.TenMicron.Interfaces;
using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using NINA.Core.Utility;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.TenMicron.ViewModels {

    [Export(typeof(IDockableVM))]
    public class MountModelVM : DockableVM, ITelescopeConsumer, IMountConsumer {
        private readonly IMountMediator mountMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private bool disposed = false;

        [ImportingConstructor]
        public MountModelVM(IProfileService profileService, ITelescopeMediator telescopeMediator) :
            this(profileService, telescopeMediator, TenMicronPlugin.MountMediator) {
        }

        public MountModelVM(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            IMountMediator mountMediator) : base(profileService) {
            this.Title = "10u Model";
            this.mountMediator = mountMediator;
            this.telescopeMediator = telescopeMediator;

            this.telescopeMediator.RegisterConsumer(this);
            this.mountMediator.RegisterConsumer(this);
        }

        public void Dispose() {
            if (!this.disposed) {
                this.telescopeMediator.RemoveConsumer(this);
                this.mountMediator.RemoveConsumer(this);
                this.disposed = true;
            }
        }

        public void UpdateDeviceInfo(TelescopeInfo deviceInfo) {
            this.TelescopeInfo = deviceInfo;
        }

        public void UpdateDeviceInfo(MountInfo deviceInfo) {
            this.MountInfo = deviceInfo;
            if (this.MountInfo.Connected) {
                Connect();
            } else {
                Disconnect();
            }
        }

        private MountInfo mountInfo = DeviceInfo.CreateDefaultInstance<MountInfo>();

        public MountInfo MountInfo {
            get => mountInfo;
            private set {
                mountInfo = value;
                RaisePropertyChanged();
            }
        }

        private TelescopeInfo telescopeInfo = DeviceInfo.CreateDefaultInstance<TelescopeInfo>();

        public TelescopeInfo TelescopeInfo {
            get => telescopeInfo;
            private set {
                telescopeInfo = value;
                RaisePropertyChanged();
            }
        }

        private bool connected;

        public bool Connected {
            get => connected;
            private set {
                if (connected != value) {
                    connected = value;
                    RaisePropertyChanged();
                }
            }
        }

        private AlignmentModelInfo alignmentModelInfo = AlignmentModelInfo.Default;

        public AlignmentModelInfo AlignmentModelInfo {
            get => alignmentModelInfo;
            private set {
                alignmentModelInfo = value;
                RaisePropertyChanged();
            }
        }

        private int alignmentStarCount = -1;

        public int AlignmentStarCount {
            get => alignmentStarCount;
            private set {
                if (alignmentStarCount != value) {
                    alignmentStarCount = value;
                    RaisePropertyChanged();
                }
            }
        }

        private void Connect() {
            if (Connected) {
                return;
            }

            try {
                AlignmentStarCount = mountMediator.GetAlignmentStarCount();
                AlignmentModelInfo = mountMediator.GetAlignmentModelInfo();
            } catch (Exception ex) {
                Logger.Error("Failed to get alignment model", ex);
                AlignmentModelInfo = AlignmentModelInfo.Default;
                AlignmentStarCount = -1;
            }
            Connected = true;
        }

        private void Disconnect() {
            if (!Connected) {
                return;
            }

            AlignmentModelInfo = AlignmentModelInfo.Default;
            AlignmentStarCount = -1;
            Connected = false;
        }
    }
}