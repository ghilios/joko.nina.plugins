using Joko.NINA.Plugins.HocusFocus.Utility;
using NINA.Core.Interfaces;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageAnalysis;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Joko.NINA.Plugins.HocusFocus.StarDetection {

    [Export(typeof(IPluggableBehavior))]
    public class HocusFocusStarAnnotator : IStarAnnotator {
        private readonly IImageControlVM imageControlVM;
        private WeakReference<BitmapSource> previousAnnotatedImageRef;
        private object annotationPropertyChangeLock = new object();
        private StarDetectionParams previousParams;
        private StarDetectionResult previousResult;
        private Task refreshAnnotationTask;
        private CancellationTokenSource refreshAnnotationTaskCTS;

        [ImportingConstructor]
        public HocusFocusStarAnnotator(IProfileService profileService, IImageControlVM imageControlVM) {
            StarAnnotatorOptions = HocusFocusPlugin.StarAnnotatorOptions;
            StarAnnotatorOptions.PropertyChanged += StarAnnotatorOptions_PropertyChanged;
            this.imageControlVM = imageControlVM;
        }

        private void StarAnnotatorOptions_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            lock (annotationPropertyChangeLock) {
                refreshAnnotationTaskCTS?.Cancel();
                refreshAnnotationTaskCTS = null;
                refreshAnnotationTask = null;
            }

            // Only re-annotate if the previous annotated image still matches the rendered image being displayed
            if (this.previousAnnotatedImageRef == null || !this.previousAnnotatedImageRef.TryGetTarget(out var previousAnnotatedImage)
                || !object.ReferenceEquals(previousAnnotatedImage, this.imageControlVM.RenderedImage.OriginalImage)) {
                return;
            }

            refreshAnnotationTaskCTS = new CancellationTokenSource();
            refreshAnnotationTask = Task.Run(() => {
                var reAnnotatedImage = GenerateAnnotatedImage(previousParams, previousResult, previousAnnotatedImage, refreshAnnotationTaskCTS.Token);
                if (!refreshAnnotationTaskCTS.IsCancellationRequested) {
                    this.imageControlVM.Image = reAnnotatedImage;
                }
            }, refreshAnnotationTaskCTS.Token);
        }

        public StarAnnotatorOptions StarAnnotatorOptions { get; set; }

        public string Name => "Hocus Focus";

        public string ContentId => GetType().FullName;

        private BitmapSource GenerateAnnotatedImage(StarDetectionParams p, StarDetectionResult result, BitmapSource imageToAnnotate, CancellationToken token) {
            using (var starBoundsBrush = new SolidBrush(StarAnnotatorOptions.StarBoundsColor.ToDrawingColor()))
            using (var starBoundsPen = new Pen(starBoundsBrush))
            using (var hfrBrush = new SolidBrush(StarAnnotatorOptions.HFRColor.ToDrawingColor()))
            using (var roiBrush = new SolidBrush(StarAnnotatorOptions.ROIColor.ToDrawingColor()))
            using (var roiPen = new Pen(roiBrush))
            using (var hfrFont = new Font(StarAnnotatorOptions.TextFontFamily.ToDrawingFontFamily(), StarAnnotatorOptions.TextFontSizePoints, FontStyle.Regular, GraphicsUnit.Point))
            using (var starCenterBrush = new SolidBrush(StarAnnotatorOptions.StarCenterColor.ToDrawingColor()))
            using (var starCenterPen = new Pen(starCenterBrush))
            using (MyStopWatch.Measure()) {
                using (var bmp = ImageUtility.Convert16BppTo8Bpp(imageToAnnotate)) {
                    using (var newBitmap = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb)) {
                        Graphics graphics = Graphics.FromImage(newBitmap);
                        graphics.DrawImage(bmp, 0, 0);
                        var starList = result.StarList;

                        if (starList.Count > 0) {
                            int offset = 5;
                            float textposx, textposy;

                            var maxStars = StarAnnotatorOptions.MaxStars;
                            if (!StarAnnotatorOptions.ShowAllStars && maxStars > 0 && starList.Count > maxStars) {
                                starList = new List<DetectedStar>(starList);

                                starList.Sort((item1, item2) => item2.AverageBrightness.CompareTo(item1.AverageBrightness));
                                starList = starList.GetRange(0, maxStars);
                            }

                            foreach (var star in starList) {
                                token.ThrowIfCancellationRequested();
                                textposx = star.BoundingBox.Right;
                                textposy = star.BoundingBox.Top;
                                if (StarAnnotatorOptions.ShowStarBounds) {
                                    if (StarAnnotatorOptions.StarBoundsType == Interfaces.StarBoundsTypeEnum.Box) {
                                        graphics.DrawRectangle(starBoundsPen, new Rectangle(star.BoundingBox.X, star.BoundingBox.Y, star.BoundingBox.Width, star.BoundingBox.Height));
                                    } else {
                                        graphics.DrawEllipse(starBoundsPen, new RectangleF(star.BoundingBox.X, star.BoundingBox.Y, star.BoundingBox.Width, star.BoundingBox.Height));
                                    }
                                }
                                if (StarAnnotatorOptions.ShowHFR) {
                                    graphics.DrawString(star.HFR.ToString("##.##"), hfrFont, hfrBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                }
                                if (StarAnnotatorOptions.ShowStarCenter) {
                                    var xLength = Math.Max(1.0f, Math.Min(star.Position.X - star.BoundingBox.Left, star.BoundingBox.Right - star.Position.X)) / 2.0f;
                                    var yLength = Math.Max(1.0f, Math.Min(star.Position.Y - star.BoundingBox.Top, star.BoundingBox.Bottom - star.Position.Y)) / 2.0f;
                                    graphics.DrawLine(
                                        starCenterPen, star.Position.X - xLength, star.Position.Y, star.Position.X + xLength, star.Position.Y);
                                    graphics.DrawLine(
                                        starCenterPen, star.Position.X, star.Position.Y - yLength, star.Position.X, star.Position.Y + yLength);
                                }
                            }
                        }

                        if (p.UseROI) {
                            graphics.DrawRectangle(roiPen, (float)(1 - p.InnerCropRatio) * imageToAnnotate.PixelWidth / 2, (float)(1 - p.InnerCropRatio) * imageToAnnotate.PixelHeight / 2, (float)p.InnerCropRatio * imageToAnnotate.PixelWidth, (float)p.InnerCropRatio * imageToAnnotate.PixelHeight);
                            if (p.OuterCropRatio < 1) {
                                graphics.DrawRectangle(roiPen, (float)(1 - p.OuterCropRatio) * imageToAnnotate.PixelWidth / 2, (float)(1 - p.OuterCropRatio) * imageToAnnotate.PixelHeight / 2, (float)p.OuterCropRatio * imageToAnnotate.PixelWidth, (float)p.OuterCropRatio * imageToAnnotate.PixelHeight);
                            }
                        }

                        var img = ImageUtility.ConvertBitmap(newBitmap, System.Windows.Media.PixelFormats.Bgr24);
                        img.Freeze();
                        return img;
                    }
                }
            }
        }

        public Task<BitmapSource> GetAnnotatedImage(StarDetectionParams p, StarDetectionResult result, BitmapSource imageToAnnotate, int maxStars = 200, CancellationToken token = default) {
            previousAnnotatedImageRef = new WeakReference<BitmapSource>(imageToAnnotate);
            previousParams = p;
            previousResult = result;
            return Task.Run(() => GenerateAnnotatedImage(p, result, imageToAnnotate, token));
        }
    }
}
