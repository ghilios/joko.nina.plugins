#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Imaging.Filters;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Core.Interfaces;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageAnalysis;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.IO;
using NINA.Joko.Plugins.HocusFocus.Interfaces;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

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
        public HocusFocusStarAnnotator(IImageControlVM imageControlVM) : this(HocusFocusPlugin.StarAnnotatorOptions, imageControlVM) {
        }

        public HocusFocusStarAnnotator(IStarAnnotatorOptions starAnnotatorOptions, IImageControlVM imageControlVM) {
            StarAnnotatorOptions = starAnnotatorOptions;
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

        public IStarAnnotatorOptions StarAnnotatorOptions { get; set; }

        public string Name => "Hocus Focus";

        public string ContentId => GetType().FullName;

        private BitmapSource GenerateAnnotatedImage(StarDetectionParams p, StarDetectionResult result, BitmapSource imageToAnnotate, CancellationToken token) {
            token.ThrowIfCancellationRequested();
            if (!StarAnnotatorOptions.ShowAnnotations) {
                return imageToAnnotate;
            }

            if (imageToAnnotate.Format == System.Windows.Media.PixelFormats.Rgb48) {
                using (var source = ImageUtility.BitmapFromSource(imageToAnnotate, System.Drawing.Imaging.PixelFormat.Format48bppRgb)) {
                    using (var img = new Grayscale(0.2125, 0.7154, 0.0721).Apply(source)) {
                        imageToAnnotate = ImageUtility.ConvertBitmap(img, System.Windows.Media.PixelFormats.Gray16);
                        imageToAnnotate.Freeze();
                    }
                }
            }

            var hfResult = result as HocusFocusStarDetectionResult;
            using (var starBoundsBrush = new SolidBrush(StarAnnotatorOptions.StarBoundsColor.ToDrawingColor()))
            using (var starBoundsPen = new Pen(starBoundsBrush))
            using (var annotationBrush = new SolidBrush(StarAnnotatorOptions.AnnotationColor.ToDrawingColor()))
            using (var roiBrush = new SolidBrush(StarAnnotatorOptions.ROIColor.ToDrawingColor()))
            using (var roiPen = new Pen(roiBrush))
            using (var annotationFont = new Font(StarAnnotatorOptions.AnnotationFontFamily.ToDrawingFontFamily(), StarAnnotatorOptions.AnnotationFontSizePoints, FontStyle.Regular, GraphicsUnit.Point))
            using (var starCenterBrush = new SolidBrush(StarAnnotatorOptions.StarCenterColor.ToDrawingColor()))
            using (var starCenterPen = new Pen(starCenterBrush))
            using (MyStopWatch.Measure()) {
                using (var bmp = ImageUtility.Convert16BppTo8Bpp(imageToAnnotate)) {
                    using (var newBitmap = new Bitmap(bmp.Width, bmp.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb)) {
                        Graphics graphics = Graphics.FromImage(newBitmap);
                        graphics.DrawImage(bmp, 0, 0);
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        var starList = result.StarList;

                        if (starList.Count > 0) {
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

                                var hocusFocusStar = star as HocusFocusDetectedStar;
                                var psf = hocusFocusStar?.PSF;

                                if (StarAnnotatorOptions.ShowStarBounds) {
                                    if (StarAnnotatorOptions.StarBoundsType == Interfaces.StarBoundsTypeEnum.Box) {
                                        graphics.DrawRectangle(starBoundsPen, new Rectangle(star.BoundingBox.X, star.BoundingBox.Y, star.BoundingBox.Width, star.BoundingBox.Height));
                                    } else if (StarAnnotatorOptions.StarBoundsType == Interfaces.StarBoundsTypeEnum.PSF) {
                                        if (psf != null) {
                                            var thetaDegrees = MathUtility.RadiansToDegrees(psf.ThetaRadians);
                                            graphics.TranslateTransform(hocusFocusStar.Position.X, hocusFocusStar.Position.Y);

                                            // Rotation is clockwise, whereas PSF rotation is counter-clockwise
                                            graphics.RotateTransform(-(float)thetaDegrees);
                                            graphics.DrawEllipse(starBoundsPen, new RectangleF(
                                                (float)-psf.FWHMx,
                                                (float)-psf.FWHMy,
                                                (float)(psf.FWHMx * 2),
                                                (float)(psf.FWHMy * 2)));
                                            graphics.ResetTransform();
                                        }
                                    } else {
                                        graphics.DrawEllipse(starBoundsPen, new RectangleF(star.BoundingBox.X, star.BoundingBox.Y, star.BoundingBox.Width, star.BoundingBox.Height));
                                    }
                                }

                                if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.HFR) {
                                    graphics.DrawString(star.HFR.ToString("#0.00"), annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                } else if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.FWHM && psf != null) {
                                    graphics.DrawString(psf.FWHMArcsecs.ToString("#0.00"), annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                } else if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.FWHMPixels && psf != null) {
                                    graphics.DrawString(psf.FWHMPixels.ToString("#0.00"), annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                } else if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.FWHM_X && psf != null) {
                                    graphics.DrawString(psf.FWHMx.ToString("#0.00"), annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                } else if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.FWHM_Y && psf != null) {
                                    graphics.DrawString(psf.FWHMy.ToString("#0.00"), annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                } else if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.Eccentricity && psf != null) {
                                    graphics.DrawString(psf.Eccentricity.ToString("#.00"), annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                } else if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.PSFTheta && psf != null) {
                                    var thetaDegrees = MathUtility.RadiansToDegrees(psf.ThetaRadians);
                                    graphics.DrawString(Math.Round(thetaDegrees).ToString("##0") + "°", annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                } else if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.Background) {
                                    graphics.DrawString(star.Background.ToString("#.0000"), annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                } else if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.PSFBackground && psf != null) {
                                    graphics.DrawString(psf.Background.ToString("#.0000"), annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                } else if (StarAnnotatorOptions.ShowAnnotationType == ShowAnnotationTypeEnum.PSFPeak && psf != null) {
                                    graphics.DrawString(psf.Peak.ToString("#.0000"), annotationFont, annotationBrush, new PointF(Convert.ToSingle(textposx), Convert.ToSingle(textposy)));
                                }
                                if (StarAnnotatorOptions.ShowStarCenter) {
                                    float starX = star.Position.X, starY = star.Position.Y;
                                    if (psf != null && StarAnnotatorOptions.StarBoundsType == StarBoundsTypeEnum.PSF) {
                                        starX += (float)psf.OffsetX;
                                        starY += (float)psf.OffsetY;
                                    }

                                    var xLength = Math.Max(1.0f, Math.Min(starX - star.BoundingBox.Left, star.BoundingBox.Right - starX)) / 2.0f;
                                    var yLength = Math.Max(1.0f, Math.Min(starY - star.BoundingBox.Top, star.BoundingBox.Bottom - starY)) / 2.0f;
                                    graphics.DrawLine(
                                        starCenterPen, starX - xLength, starY, starX + xLength, starY);
                                    graphics.DrawLine(
                                        starCenterPen, starX, starY - yLength, starX, starY + yLength);
                                }
                            }
                        }

                        var metrics = (result as HocusFocusStarDetectionResult)?.Metrics;
                        if (StarAnnotatorOptions.ShowDegenerate && metrics?.TooDistortedBounds != null) {
                            using (var brush = new SolidBrush(StarAnnotatorOptions.TooDistortedColor.ToDrawingColor()))
                            using (var pen = new Pen(brush)) {
                                foreach (var rect in metrics.TooDistortedBounds) {
                                    graphics.DrawRectangle(pen, rect.ToDrawingRectangle());
                                }
                            }
                        }
                        if (StarAnnotatorOptions.ShowDegenerate && metrics?.DegenerateBounds != null) {
                            using (var brush = new SolidBrush(StarAnnotatorOptions.DegenerateColor.ToDrawingColor()))
                            using (var pen = new Pen(brush)) {
                                foreach (var rect in metrics.DegenerateBounds) {
                                    graphics.DrawRectangle(pen, rect.ToDrawingRectangle());
                                }
                            }
                        }
                        if (StarAnnotatorOptions.ShowSaturated && metrics?.SaturatedBounds != null) {
                            using (var brush = new SolidBrush(StarAnnotatorOptions.SaturatedColor.ToDrawingColor()))
                            using (var pen = new Pen(brush)) {
                                foreach (var rect in metrics.SaturatedBounds) {
                                    graphics.DrawRectangle(pen, rect.ToDrawingRectangle());
                                }
                            }
                        }
                        if (StarAnnotatorOptions.ShowLowSensitivity && metrics?.LowSensitivityBounds != null) {
                            using (var brush = new SolidBrush(StarAnnotatorOptions.LowSensitivityColor.ToDrawingColor()))
                            using (var pen = new Pen(brush)) {
                                foreach (var rect in metrics.LowSensitivityBounds) {
                                    graphics.DrawRectangle(pen, rect.ToDrawingRectangle());
                                }
                            }
                        }
                        if (StarAnnotatorOptions.ShowNotCentered && metrics?.NotCenteredBounds != null) {
                            using (var brush = new SolidBrush(StarAnnotatorOptions.NotCenteredColor.ToDrawingColor()))
                            using (var pen = new Pen(brush)) {
                                foreach (var rect in metrics.NotCenteredBounds) {
                                    graphics.DrawRectangle(pen, rect.ToDrawingRectangle());
                                }
                            }
                        }
                        if (StarAnnotatorOptions.ShowTooFlat && metrics?.TooFlatBounds != null) {
                            using (var brush = new SolidBrush(StarAnnotatorOptions.TooFlatColor.ToDrawingColor()))
                            using (var pen = new Pen(brush)) {
                                foreach (var rect in metrics.TooFlatBounds) {
                                    graphics.DrawRectangle(pen, rect.ToDrawingRectangle());
                                }
                            }
                        }

                        if (hfResult != null) {
                            if (!hfResult.DetectorParams.Region.IsFull()) {
                                var outerRegion = hfResult.DetectorParams.Region.OuterBoundary;
                                graphics.DrawRectangle(roiPen, (float)outerRegion.StartX * imageToAnnotate.PixelWidth, (float)outerRegion.StartY * imageToAnnotate.PixelHeight, (float)outerRegion.Width * imageToAnnotate.PixelWidth, (float)outerRegion.Height * imageToAnnotate.PixelHeight);
                                if (hfResult.DetectorParams.Region.InnerCropBoundary != null) {
                                    var innerRegion = hfResult.DetectorParams.Region.InnerCropBoundary;
                                    graphics.DrawRectangle(roiPen, (float)innerRegion.StartX * imageToAnnotate.PixelWidth, (float)innerRegion.StartY * imageToAnnotate.PixelHeight, (float)innerRegion.Width * imageToAnnotate.PixelWidth, (float)innerRegion.Height * imageToAnnotate.PixelHeight);
                                }
                            }
                        } else {
                            if (p.UseROI) {
                                graphics.DrawRectangle(roiPen, (float)(1 - p.InnerCropRatio) * imageToAnnotate.PixelWidth / 2, (float)(1 - p.InnerCropRatio) * imageToAnnotate.PixelHeight / 2, (float)p.InnerCropRatio * imageToAnnotate.PixelWidth, (float)p.InnerCropRatio * imageToAnnotate.PixelHeight);
                                if (p.OuterCropRatio < 1) {
                                    graphics.DrawRectangle(roiPen, (float)(1 - p.OuterCropRatio) * imageToAnnotate.PixelWidth / 2, (float)(1 - p.OuterCropRatio) * imageToAnnotate.PixelHeight / 2, (float)p.OuterCropRatio * imageToAnnotate.PixelWidth, (float)p.OuterCropRatio * imageToAnnotate.PixelHeight);
                                }
                            }
                        }

                        if (StarAnnotatorOptions.ShowStructureMap != Interfaces.ShowStructureMapEnum.None) {
                            var structureMapData = hfResult?.DebugData?.StructureMap;
                            if (structureMapData != null) {
                                var minStructureMapValue = StarAnnotatorOptions.ShowStructureMap == Interfaces.ShowStructureMapEnum.Dilated ? 1 : 2;
                                var structureMapColor = StarAnnotatorOptions.StructureMapColor.ToDrawingColor();
                                int i = 0;
                                for (int y = 0; y < hfResult.DebugData.DetectionROI.Height; ++y) {
                                    for (int x = 0; x < hfResult.DebugData.DetectionROI.Width; ++x) {
                                        var structureMapPixel = structureMapData[i++];
                                        if (structureMapPixel >= minStructureMapValue) {
                                            newBitmap.BlendPixel(x, y, structureMapColor);
                                        }
                                    }
                                }
                            }
                        }

                        var img = ImageUtility.ConvertBitmap(newBitmap, System.Windows.Media.PixelFormats.Bgr24);
                        img.Freeze();
                        var hocusFocusDetectorParams = (result as HocusFocusStarDetectionResult)?.DetectorParams;
                        if (hocusFocusDetectorParams != null) {
                            MaybeSaveIntermediateBitmapSource(img, hocusFocusDetectorParams, "annotated-result.png");
                        }

                        return img;
                    }
                }
            }
        }

        private static void MaybeSaveIntermediateBitmapSource(BitmapSource image, StarDetectorParams p, string filename) {
            var saveIntermediate = !string.IsNullOrEmpty(p.SaveIntermediateFilesPath) && Directory.Exists(p.SaveIntermediateFilesPath);
            if (!saveIntermediate) {
                return;
            }

            var targetPath = Path.Combine(p.SaveIntermediateFilesPath, filename);
            using (var fileStream = new FileStream(targetPath, FileMode.Create)) {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(fileStream);
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