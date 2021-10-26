using Joko.NINA.Plugins.HocusFocus.StarDetection;
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
        public int Degenerate { get; set; } = -1;
        public int Saturated { get; set; } = -1;
        public int LowSensitivity { get; set; } = -1;
        public int Uneven { get; set; } = -1;
        public int TooFlat { get; set; } = -1;
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
