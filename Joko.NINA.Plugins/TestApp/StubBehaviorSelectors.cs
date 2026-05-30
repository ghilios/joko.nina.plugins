#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TestApp {

    /// <summary>
    /// Minimal <see cref="IPluggableBehaviorSelector{T}"/> returning a fixed behavior (or null), used only to
    /// satisfy the <c>ImageDataFactory</c> constructor when loading XISF/FITS files headlessly.
    /// </summary>
    internal class StubBehaviorSelector<T> : IPluggableBehaviorSelector<T> where T : class {
        private readonly T behavior;

        public StubBehaviorSelector(T behavior = null) {
            this.behavior = behavior;
        }

        public AsyncObservableCollection<T> Behaviors { get; set; } = new AsyncObservableCollection<T>();

        public T SelectedBehavior { get; set; }

#pragma warning disable CS0067 // event is part of the interface contract but unused in this stub
        public event EventHandler SelectedBehaviorChanged;
#pragma warning restore CS0067

        public T GetBehavior() => behavior;

        public Type GetInterfaceType() => typeof(T);

        public void AddBehavior(object b) {
            if (b is T typed) {
                Behaviors.Add(typed);
            }
        }
    }

    /// <summary>
    /// No-op <see cref="IStarDetection"/> whose only meaningful member is <see cref="CreateAnalysis"/> —
    /// the single behavior <c>BaseImageData</c>'s constructor invokes during image decode. Star detection is
    /// driven separately by the HocusFocus <c>StarDetector</c>, so detection/analysis methods are never used.
    /// </summary>
    internal class StubStarDetection : IStarDetection {
        public string Name => "TestApp.StubStarDetection";
        public string ContentId => GetType().FullName;

        public IStarDetectionAnalysis CreateAnalysis() => new StarDetectionAnalysis();

        public Task<StarDetectionResult> Detect(IRenderedImage image, System.Windows.Media.PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token)
            => throw new NotSupportedException("StubStarDetection does not perform detection");

        public void UpdateAnalysis(IStarDetectionAnalysis analysis, StarDetectionParams p, StarDetectionResult result)
            => throw new NotSupportedException("StubStarDetection does not perform analysis");
    }
}
