#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Interfaces;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageAnalysis;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NINA.Joko.Plugins.HocusFocus.AutoFocus {

    // [Export(typeof(IDockableVM))]
    public class InspectorVM : DockableVM {
        private readonly IStarDetectionOptions starDetectionOptions;
        private readonly IStarAnnotatorOptions starAnnotatorOptions;
        private readonly IAutoFocusEngineFactory autoFocusEngineFactory;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;

        [ImportingConstructor]
        public InspectorVM(
            IProfileService profileService,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector)
            : this(profileService, HocusFocusPlugin.StarDetectionOptions, HocusFocusPlugin.StarAnnotatorOptions, HocusFocusPlugin.AutoFocusEngineFactory, starDetectionSelector, starAnnotatorSelector) {
        }

        public InspectorVM(
            IProfileService profileService,
            IStarDetectionOptions starDetectionOptions,
            IStarAnnotatorOptions starAnnotatorOptions,
            IAutoFocusEngineFactory autoFocusEngineFactory,
            IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector) : base(profileService) {
            this.starDetectionOptions = starDetectionOptions;
            this.starAnnotatorOptions = starAnnotatorOptions;
            this.autoFocusEngineFactory = autoFocusEngineFactory;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;

            this.Title = "Aberration Inspector";

            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/StarDetection/DataTemplates.xaml", UriKind.RelativeOrAbsolute);

            // TODO: Change logo
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["HocusFocusAnnotateStarsSVG"];
            ImageGeometry.Freeze();

            AnalyzeCommand = new AsyncCommand<bool>(() => {
                analyzeCts?.Cancel();
                analyzeCts = new CancellationTokenSource();
                analyzeTask = Task.Run(Analyze);
                return analyzeTask;
            });
            CancelAnalyzeCommand = new AsyncCommand<bool>(() => Task.Run(CancelAnalyze));
        }

        public override bool IsTool { get; } = true;

        private CancellationTokenSource analyzeCts;
        private Task<bool> analyzeTask;

        private async Task<bool> Analyze() {
            var one_third = 1.0d / 3.0d;
            var two_thirds = 2.0d / 3.0d;
            var regions = new List<StarDetectionRegion>() {
                new StarDetectionRegion(new RatioRect(one_third, one_third, one_third, one_third)),
                new StarDetectionRegion(new RatioRect(0, 0, one_third, one_third)),
                new StarDetectionRegion(new RatioRect(two_thirds, one_third, one_third, one_third)),
                new StarDetectionRegion(new RatioRect(0, two_thirds, one_third, one_third)),
                new StarDetectionRegion(new RatioRect(two_thirds, two_thirds, one_third, one_third))
            };
            var autoFocusEngine = autoFocusEngineFactory.Create();
            autoFocusEngine.IterationFailed += AutoFocusEngine_IterationFailed;
            autoFocusEngine.Completed += AutoFocusEngine_Completed;
            autoFocusEngine.MeasurementPointCompleted += AutoFocusEngine_MeasurementPointCompleted;

            // var result = await autoFocusEngine.RunWithRegions(imagingFilter, regions, token, progress);

            return true;
        }

        private void AutoFocusEngine_MeasurementPointCompleted(object sender, AutoFocusMeasurementPointCompletedEventArgs e) {
            throw new NotImplementedException();
        }

        private void AutoFocusEngine_Completed(object sender, AutoFocusCompletedEventArgs e) {
            throw new NotImplementedException();
        }

        private void AutoFocusEngine_IterationFailed(object sender, AutoFocusIterationFailedEventArgs e) {
            throw new NotImplementedException();
        }

        private async Task<bool> CancelAnalyze() {
            var localAnalyzeTask = analyzeTask;
            analyzeCts?.Cancel();
            if (localAnalyzeTask != null) {
                try {
                    await localAnalyzeTask;
                } catch (Exception) { }
            }
            return true;
        }

        public ICommand AnalyzeCommand { get; private set; }

        public ICommand CancelAnalyzeCommand { get; private set; }
    }
}