using Joko.NINA.Plugins.JokoFocus.StarDetection;
using NINA.Core.Model;
using NINA.Image.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.JokoFocus.Interfaces {
    public interface IStarDetector {
        Task<List<Star>> Detect(IRenderedImage image, StarDetectorParams p, IProgress<ApplicationStatus> progress, CancellationToken token);
    }
}
