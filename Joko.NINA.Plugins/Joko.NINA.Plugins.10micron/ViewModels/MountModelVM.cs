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
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Joko.NINA.Plugins.TenMicron.ViewModels {

    [Export(typeof(IDockableVM))]
    public class MountModelVM : DockableVM, ITelescopeConsumer, IMountConsumer {
        private readonly IMountMediator mountMediator;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IModelAccessor modelAccessor;
        private IProgress<ApplicationStatus> progress;
        private bool disposed = false;
        private CancellationTokenSource disconnectCts;

        [ImportingConstructor]
        public MountModelVM(IProfileService profileService, IApplicationStatusMediator applicationStatusMediator, ITelescopeMediator telescopeMediator) :
            this(profileService,
                telescopeMediator,
                applicationStatusMediator,
                TenMicronPlugin.MountMediator,
                new ModelAccessor(telescopeMediator, TenMicronPlugin.MountMediator, new SystemDateTime())) {
        }

        public MountModelVM(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            IApplicationStatusMediator applicationStatusMediator,
            IMountMediator mountMediator,
            IModelAccessor modelAccessor) : base(profileService) {
            this.Title = "10u Model";
            this.applicationStatusMediator = applicationStatusMediator;
            this.mountMediator = mountMediator;
            this.telescopeMediator = telescopeMediator;
            this.modelAccessor = modelAccessor;
            this.disconnectCts = new CancellationTokenSource();

            this.telescopeMediator.RegisterConsumer(this);
            this.mountMediator.RegisterConsumer(this);

            this.ModelNames = new AsyncObservableCollection<string>() { GetUnselectedModelName() };
            this.SelectedModelName = GetUnselectedModelName();

            this.LoadModelNamesCommand = new AsyncCommand<bool>(async o => { await LoadModelNames(this.disconnectCts.Token); return true; });
            this.DeleteSelectedModelCommand = new AsyncCommand<bool>(DeleteSelectedModel);
            this.LoadSelectedModelCommand = new AsyncCommand<bool>(LoadSelectedModel);
            this.SaveSelectedModelCommand = new AsyncCommand<bool>(SaveSelectedModel);
            this.SaveAsModelCommand = new AsyncCommand<bool>(SaveAsModel);
            this.DeleteWorstStarCommand = new AsyncCommand<bool>(DeleteWorstStar);
        }

        private async Task<bool> DeleteWorstStar(object o) {
            try {
                var alignmentStars = new AlignmentStarPoint[this.LoadedAlignmentModel.AlignmentStarCount];
                this.LoadedAlignmentModel.AlignmentStars.CopyTo(alignmentStars, 0);

                var largestError = double.MinValue;
                var worstStarIndex = -1;
                for (int i = 0; i < alignmentStars.Length; ++i) {
                    if (alignmentStars[i].ErrorArcsec > largestError) {
                        largestError = alignmentStars[i].ErrorArcsec;
                        worstStarIndex = i;
                    }
                }

                if (worstStarIndex >= 0) {
                    var toDelete = alignmentStars[worstStarIndex];
                    Logger.Info($"Deleting alignment star {worstStarIndex + 1}. Alt={toDelete.Altitude:.00}, Az={toDelete.Azimuth:.00}, RMS={toDelete.ErrorArcsec:.00} arcsec");
                    if (!this.mountMediator.DeleteAlignmentStar(worstStarIndex + 1)) {
                        Notification.ShowError("Failed to delete worst alignment star");
                        Logger.Error("Failed to delete worst alignment star");
                        return false;
                    }

                    ModelLoaded = false;
                    _ = LoadAlignmentModel(this.disconnectCts.Token);
                    return true;
                } else {
                    return false;
                }
            } catch (Exception e) {
                Notification.ShowError($"Failed to delete worst alignment star. {e.Message}");
                Logger.Error($"Failed to delete worst alignment star", e);
                return false;
            }
        }

        private async Task<bool> DeleteSelectedModel(object o) {
            try {
                var selectedModelName = this.SelectedModelName;
                Logger.Info($"Deleting model {selectedModelName}");
                this.mountMediator.DeleteModel(selectedModelName);
                ModelNamesLoaded = false;
                _ = LoadModelNames(this.disconnectCts.Token);
                return true;
            } catch (Exception e) {
                Notification.ShowError($"Failed to delete {selectedModelName}. {e.Message}");
                Logger.Error($"Failed to delete {selectedModelName}", e);
                return false;
            }
        }

        private async Task<bool> LoadSelectedModel(object o) {
            try {
                var selectedModelName = this.SelectedModelName;
                Logger.Info($"Loading model {selectedModelName}");
                ModelLoaded = false;
                this.mountMediator.LoadModel(selectedModelName);
                _ = LoadAlignmentModel(this.disconnectCts.Token);
                ModelLoaded = true;
                return true;
            } catch (Exception e) {
                Notification.ShowError($"Failed to load {selectedModelName}. {e.Message}");
                Logger.Error($"Failed to load {selectedModelName}", e);
                return false;
            }
        }

        private async Task<bool> SaveSelectedModel(object o) {
            try {
                var selectedModelName = this.SelectedModelName;
                Logger.Info($"Saving model as {selectedModelName}");
                this.mountMediator.SaveModel(selectedModelName);
                return true;
            } catch (Exception e) {
                Notification.ShowError($"Failed to save {selectedModelName}. {e.Message}");
                Logger.Error($"Failed to save {selectedModelName}", e);
                return false;
            }
        }

        private async Task<bool> SaveAsModel(object o) {
            try {
                var result = MyInputBox.Show("Save Model As...", "Model Name", "What name to save the active model with");
                if (result.MessageBoxResult == System.Windows.MessageBoxResult.OK) {
                    var inputModelName = result.InputText;
                    Logger.Info($"Saving model as {inputModelName}");
                    this.mountMediator.SaveModel(result.InputText);
                    ModelNamesLoaded = false;
                    _ = LoadModelNames(this.disconnectCts.Token);
                    return true;
                } else {
                    Logger.Info("Save cancelled by user");
                    return false;
                }
            } catch (Exception e) {
                Notification.ShowError($"Failed to save {selectedModelName}. {e.Message}");
                Logger.Error($"Failed to save {selectedModelName}", e);
                return false;
            }
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

        private readonly LoadedAlignmentModel loadedAlignmentModel = new LoadedAlignmentModel();

        public LoadedAlignmentModel LoadedAlignmentModel => loadedAlignmentModel;

        private void Connect() {
            if (Connected) {
                return;
            }

            if (this.progress == null) {
                this.progress = new Progress<ApplicationStatus>(p => {
                    p.Source = this.Title;
                    this.applicationStatusMediator.StatusUpdate(p);
                });
            }

            this.disconnectCts?.Cancel();
            this.disconnectCts = new CancellationTokenSource();
            _ = Task.Run(async () => {
                await LoadModelNames(this.disconnectCts.Token);
                await LoadAlignmentModel(this.disconnectCts.Token);
            }, this.disconnectCts.Token);
            Connected = true;
        }

        private Task alignmentModelLoadTask;

        private async Task LoadAlignmentModel(CancellationToken ct) {
            if (alignmentModelLoadTask != null) {
                await alignmentModelLoadTask;
            }

            this.alignmentModelLoadTask = Task.Run(() => {
                try {
                    ModelLoaded = false;
                    modelAccessor.LoadActiveModelInto(LoadedAlignmentModel, progress: this.progress, ct: ct);
                    if (LoadedAlignmentModel.AlignmentStarCount <= 0) {
                        Notification.ShowWarning("No alignment stars in loaded model");
                        Logger.Warning("No alignment stars in loaded model");
                    }

                    ModelLoaded = true;
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    Notification.ShowError("Failed to get 10u alignment model");
                    Logger.Error("Failed to get alignment model", ex);
                }
            }, ct);

            await this.alignmentModelLoadTask;
            this.alignmentModelLoadTask = null;
        }

        private Task alignmentModelNameLoadTask;

        private async Task LoadModelNames(CancellationToken ct) {
            if (alignmentModelNameLoadTask != null) {
                await alignmentModelNameLoadTask;
            }

            this.alignmentModelNameLoadTask = Task.Run(() => {
                bool succeeded = false;
                try {
                    ModelNamesLoaded = false;
                    var modelCount = this.mountMediator.GetModelCount();
                    ct.ThrowIfCancellationRequested();
                    this.ModelNames.Clear();
                    this.ModelNames.Add(GetUnselectedModelName());
                    for (int i = 1; i <= modelCount; i++) {
                        ct.ThrowIfCancellationRequested();
                        this.ModelNames.Add(this.mountMediator.GetModelName(i));
                    }
                    succeeded = true;
                } catch (OperationCanceledException) {
                } catch (Exception e) {
                    Notification.ShowError($"Failed to load 10u models. {e.Message}");
                } finally {
                    if (!succeeded) {
                        this.ModelNames.Clear();
                        this.ModelNames.Add(GetUnselectedModelName());
                    }
                    this.SelectedModelName = GetUnselectedModelName();
                    ModelNamesLoaded = true;
                }
            }, ct);

            await this.alignmentModelNameLoadTask;
            this.alignmentModelNameLoadTask = null;
        }

        private void Disconnect() {
            if (!Connected) {
                return;
            }

            this.disconnectCts?.Cancel();
            LoadedAlignmentModel.Clear();
            Connected = false;
        }

        private static string GetUnselectedModelName() {
            return "- Select Model -";
        }

        private AsyncObservableCollection<string> modelNames;

        public AsyncObservableCollection<string> ModelNames {
            get => modelNames;
            set {
                modelNames = value;
                RaisePropertyChanged();
                SelectedModelIndex = 0;
            }
        }

        private int selectedModelIndex;

        public int SelectedModelIndex {
            get => selectedModelIndex;
            set {
                if (selectedModelIndex != value) {
                    selectedModelIndex = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string selectedModelName;

        public string SelectedModelName {
            get => selectedModelName;
            set {
                if (selectedModelName != value) {
                    selectedModelName = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool modelLoaded = false;

        public bool ModelLoaded {
            get => modelLoaded;
            private set {
                if (modelLoaded != value) {
                    modelLoaded = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool modelNamesLoaded = false;

        public bool ModelNamesLoaded {
            get => modelNamesLoaded;
            private set {
                if (modelNamesLoaded != value) {
                    modelNamesLoaded = value;
                    RaisePropertyChanged();
                }
            }
        }

        public ICommand LoadModelNamesCommand { get; private set; }
        public ICommand DeleteSelectedModelCommand { get; private set; }
        public ICommand LoadSelectedModelCommand { get; private set; }
        public ICommand SaveSelectedModelCommand { get; private set; }
        public ICommand SaveAsModelCommand { get; private set; }
        public ICommand DeleteWorstStarCommand { get; private set; }
    }
}