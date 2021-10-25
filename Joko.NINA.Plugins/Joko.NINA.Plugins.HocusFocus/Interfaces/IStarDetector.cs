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
        public int StructureCandidates;
        public int TotalDetected;
        public int TooSmall;
        public int OnBorder;
        public int TooDistorted;
        public int Degenerate;
        public int Saturated;
        public int LowSensitivity;
        public int Uneven;
        public int TooFlat;
        public int TooLowHFR;
        public int HFRAnalysisFailed;
    }

    public class StarDetectorResult {
        public List<Star> DetectedStars { get; set; }
        public StarDetectorMetrics Metrics { get; set; }
    }

    public interface IStarDetector {
        Task<StarDetectorResult> Detect(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token);
    }
}
