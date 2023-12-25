#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public class HocusFocusDetectionParams {
        public double HighSigmaOutlierRejection { get; set; } = 4.0d;
        public double LowSigmaOutlierRejection { get; set; } = 3.0d;
        public List<Accord.Point> MatchStarPositions { get; set; } = null;
        public int NumberOfAFStars { get; set; } = 0;
        public bool IsAutoFocus { get; set; } = false;
    }

    public interface IHocusFocusStarDetection : IStarDetection {

        HocusFocusDetectionParams ToHocusFocusParams(StarDetectionParams p);

        StarDetectorParams GetStarDetectorParams(IRenderedImage image, StarDetectionRegion starDetectionRegion, bool isAutoFocus);

        Task<StarDetectionResult> Detect(PreparedImage preparedImage, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token);

        Task<StarDetectionResult> Detect(PreparedImage image, HocusFocusDetectionParams hocusFocusParams, StarDetectorParams detectorParams, IProgress<ApplicationStatus> progress, CancellationToken token);
    }
}