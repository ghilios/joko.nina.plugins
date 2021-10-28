﻿using Joko.NINA.Plugins.HocusFocus.StarDetection;
using NINA.Core.Model;
using NINA.Image.Interfaces;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.HocusFocus.Interfaces {

    public class Star {
        public Point2d Center;
        public Rect StarBoundingBox;
        public double Background;
        public double MeanBrightness;
        public double HFR;
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
    }

    public interface IStarDetector {
        Task<StarDetectorResult> Detect(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token);
    }
}