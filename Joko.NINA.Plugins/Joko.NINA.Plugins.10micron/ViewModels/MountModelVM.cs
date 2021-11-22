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
using Joko.NINA.Plugins.TenMicron.Model;
using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Timers;

namespace Joko.NINA.Plugins.TenMicron.ViewModels {

    [Export(typeof(IDockableVM))]
    public class MountModelVM : DockableVM, ITelescopeConsumer, IMountConsumer {
        private readonly IMountMediator mountMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IModelAccessor modelAccessor;
        private bool disposed = false;

        private Timer timer;

        [ImportingConstructor]
        public MountModelVM(IProfileService profileService, ITelescopeMediator telescopeMediator) :
            this(profileService, telescopeMediator, TenMicronPlugin.MountMediator, new ModelAccessor(telescopeMediator, TenMicronPlugin.MountMediator, new SystemDateTime())) {
        }

        public MountModelVM(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            IMountMediator mountMediator,
            IModelAccessor modelAccessor) : base(profileService) {
            this.Title = "10u Model";
            this.mountMediator = mountMediator;
            this.telescopeMediator = telescopeMediator;
            this.modelAccessor = modelAccessor;

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

        private readonly LoadedAlignmentModel loadedAlignmentModel = LoadedAlignmentModel.Empty();

        public LoadedAlignmentModel LoadedAlignmentModel => loadedAlignmentModel;

        private void Connect() {
            if (Connected) {
                return;
            }

            _ = LoadAlignmentModel();
            Connected = true;
        }

        private Task alignmentModelLoadTask;

        private async Task LoadAlignmentModel() {
            if (alignmentModelLoadTask != null) {
                await alignmentModelLoadTask;
            }

            this.alignmentModelLoadTask = Task.Run(() => {
                try {
                    LoadedAlignmentModel.CopyFrom(modelAccessor.LoadActiveModel());
                } catch (Exception ex) {
                    Notification.ShowError("Failed to get 10u alignment model");
                    Logger.Error("Failed to get alignment model", ex);
                    LoadedAlignmentModel.Clear();
                }
            });

            await this.alignmentModelLoadTask;
            this.alignmentModelLoadTask = null;

            this.timer = new Timer();
            this.timer.Elapsed += Timer_Elapsed;
            this.timer.Interval = TimeSpan.FromMilliseconds(100).TotalMilliseconds;
            // this.timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e) {
            this.LoadedAlignmentModel.ModelCreationTime -= TimeSpan.FromMinutes(6);
        }

        private void Disconnect() {
            if (!Connected) {
                return;
            }

            LoadedAlignmentModel.Clear();
            Connected = false;
            this.timer?.Stop();
            this.timer = null;
        }
    }
}