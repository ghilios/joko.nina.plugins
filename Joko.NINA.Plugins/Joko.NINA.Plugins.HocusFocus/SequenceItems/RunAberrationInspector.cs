#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Validations;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Core.Utility.WindowService;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Locale;
using NINA.Joko.Plugins.HocusFocus.AutoFocus;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus;

namespace NINA.Sequencer.SequenceItem.Autofocus {

    [ExportMetadata("Name", "Run Aberration Inspector")]
    [ExportMetadata("Description", "Runs Aberration Inspector with options chosen in the Aberration Inspector panel")]
    [ExportMetadata("Icon", "InspectorSVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Focuser")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class RunAberrationInspector : SequenceItem, IValidatable {
        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IInspectorVMFactory inspectorVMFactory;
        private readonly IPerFilterStarDetectionStore perFilterStarDetectionStore;

        [ImportingConstructor]
        public RunAberrationInspector(
            IProfileService profileService, ICameraMediator cameraMediator, IFocuserMediator focuserMediator, IFilterWheelMediator filterWheelMediator, IInspectorVMFactory inspectorVMFactory)
            : this(profileService, cameraMediator, focuserMediator, filterWheelMediator, inspectorVMFactory, HocusFocusPlugin.PerFilterStarDetection) {
        }

        public RunAberrationInspector(
            IProfileService profileService, ICameraMediator cameraMediator, IFocuserMediator focuserMediator, IFilterWheelMediator filterWheelMediator, IInspectorVMFactory inspectorVMFactory, IPerFilterStarDetectionStore perFilterStarDetectionStore) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.focuserMediator = focuserMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.inspectorVMFactory = inspectorVMFactory;
            this.perFilterStarDetectionStore = perFilterStarDetectionStore;
        }

        private RunAberrationInspector(RunAberrationInspector cloneMe) : this(cloneMe.profileService, cloneMe.cameraMediator, cloneMe.focuserMediator, cloneMe.filterWheelMediator, cloneMe.inspectorVMFactory, cloneMe.perFilterStarDetectionStore) {
            CopyMetaData(cloneMe);
        }

        public override object Clone() {
            return new RunAberrationInspector(this);
        }

        private IList<string> issues = new List<string>();

        public IList<string> Issues {
            get => issues;
            set {
                issues = value;
                RaisePropertyChanged();
            }
        }

        public IWindowServiceFactory WindowServiceFactory { get; set; } = new WindowServiceFactory();

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var inspector = this.inspectorVMFactory.Create();
            token.ThrowIfCancellationRequested();
            var result = await inspector.AnalyzeAutoFocus(token);
            if (!result) {
                throw new SequenceEntityFailedException("Aberration inspector failed to run");
            }
        }

        public bool Validate() {
            var i = new List<string>();
            if (!cameraMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblCameraNotConnected"]);
            }
            if (!focuserMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblFocuserNotConnected"]);
            }
            if (perFilterStarDetectionStore?.Enabled == true && filterWheelMediator.GetInfo()?.Connected != true) {
                i.Add("Per-filter star detection requires a connected filter wheel");
            }

            Issues = i;
            return issues.Count == 0;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public override TimeSpan GetEstimatedDuration() {
            var filter = filterWheelMediator.GetInfo()?.SelectedFilter;

            var focuserSettings = profileService.ActiveProfile.FocuserSettings;

            var exposureTime = focuserSettings.AutoFocusExposureTime;
            if (filter != null) {
                var filterTime = profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters[filter.Position].AutoFocusExposureTime;
                exposureTime = filterTime > 0 ? filterTime : exposureTime;
            }

            // + 2 because the autofocus will take an initial exposure and a final exposure to evaluate the run
            var steps = focuserSettings.AutoFocusInitialOffsetSteps * 2 * focuserSettings.AutoFocusNumberOfFramesPerPoint + 2;

            // Assume for focuser settle time an additional 2 seconds for focuser movement itself
            var settleTime = focuserSettings.FocuserSettleTime + 2;

            var instructionAttempts = Math.Max(1, Attempts);
            var afAttemptsSetting = Math.Max(1, focuserSettings.AutoFocusTotalNumberOfAttempts);

            // More than 10 attempts will be highly unlikely. Safeguard against unreasonable high reattampt values for the estimation
            var time = Math.Min(10, afAttemptsSetting * instructionAttempts) * steps * (exposureTime + settleTime);

            return TimeSpan.FromSeconds(time);
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(RunAberrationInspector)}";
        }
    }
}