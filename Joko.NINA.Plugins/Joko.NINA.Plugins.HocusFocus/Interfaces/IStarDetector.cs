#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.StarDetection;
using NINA.Core.Model;
using NINA.Image.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Interfaces {

    public class Star {
        public Point2d Center { get; set; }
        public Rect StarBoundingBox { get; set; }
        public double Background { get; set; }
        public double MeanBrightness { get; set; }
        public double HFR { get; set; }

        public override string ToString() {
            return $"{{{nameof(Center)}={Center.ToString()}, {nameof(StarBoundingBox)}={StarBoundingBox.ToString()}, {nameof(Background)}={Background.ToString()}, {nameof(MeanBrightness)}={MeanBrightness.ToString()}, {nameof(HFR)}={HFR.ToString()}}}";
        }
    }

    public class StarDetectorMetrics {
        public int StructureCandidates { get; set; } = -1;
        public int TotalDetected { get; set; } = -1;
        public int TooSmall { get; set; } = -1;
        public int OnBorder { get; set; } = -1;
        public int TooDistorted { get; set; } = -1;
        public int Degenerate { get => DegenerateBounds.Count; set => throw new NotSupportedException("Can't set Degenerate directly"); }
        public List<Rect> DegenerateBounds { get; private set; } = new List<Rect>();
        public int Saturated { get => SaturatedBounds.Count; set => throw new NotSupportedException("Can't set Saturated directly"); }
        public List<Rect> SaturatedBounds { get; private set; } = new List<Rect>();
        public int LowSensitivity { get => LowSensitivityBounds.Count; set => throw new NotSupportedException("Can't set LowSensitivity directly"); }
        public List<Rect> LowSensitivityBounds { get; private set; } = new List<Rect>();
        public int NotCentered { get => NotCenteredBounds.Count; set => throw new NotSupportedException("Can't set NotCentered directly"); }
        public List<Rect> NotCenteredBounds { get; private set; } = new List<Rect>();
        public int TooFlat { get => TooFlatBounds.Count; set => throw new NotSupportedException("Can't set TooFlat directly"); }
        public List<Rect> TooFlatBounds { get; private set; } = new List<Rect>();
        public int TooLowHFR { get; set; } = -1;
        public int HFRAnalysisFailed { get; set; } = -1;
        public int OutsideROI { get; set; } = -1;
    }

    public class StarDetectorResult {
        public List<Star> DetectedStars { get; set; }
        public StarDetectorMetrics Metrics { get; set; }
        public DebugData DebugData { get; set; }
    }

    public interface IStarDetector {

        Task<StarDetectorResult> Detect(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token);
    }
}