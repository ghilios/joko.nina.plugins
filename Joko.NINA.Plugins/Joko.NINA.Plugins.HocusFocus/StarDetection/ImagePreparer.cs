#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Enum;
using NINA.Core.Interfaces;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Mediator;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class ImagePreparer : IImagePreparer {
        private readonly IProfileService profileService;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectorSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        private readonly ICameraMediator cameraMediator;
        private readonly IImagingMediator imagingMediator;

        public ImagePreparer(
            IProfileService profileService,
            IImagingMediator imagingMediator,
            ICameraMediator cameraMediator,
            IPluggableBehaviorSelector<IStarDetection> starDetectorSelector,
            IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector) {
            this.profileService = profileService;
            this.imagingMediator = imagingMediator;
            this.cameraMediator = cameraMediator;
            this.starDetectorSelector = starDetectorSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
        }

        public async Task<PreparedImage> PrepareImage(IRenderedImage image, StarDetectorParams p) {
            var imageData = image.RawImageData;
            var isDebayered = image is IDebayeredImage;
            var shouldBeDebayered = imageData.Properties.IsBayered && profileService.ActiveProfile.ImageSettings.DebayerImage;
            var needsDebayering = shouldBeDebayered && !isDebayered;
            if (needsDebayering || p.HotpixelFiltering) {
                return await PrepareImage(imageData, p);
            } else {
                var resourceTracker = new ResourcesTracker();
                try {
                    var rawImageDataCopy = new ushort[imageData.Data.FlatArray.Length];
                    Buffer.BlockCopy(imageData.Data.FlatArray, 0, rawImageDataCopy, 0, imageData.Data.FlatArray.Length * sizeof(ushort));
                    Mat srcImage = resourceTracker.T(CvImageUtility.ToOpenCVMat(imageData));
                    return new PreparedImage() {
                        HotpixelCount = 0L,
                        HotpixelFilteringApplied = false,
                        Image = image,
                        SrcImage = srcImage
                    };
                } catch (TypeInitializationException e) {
                    Logger.Error(e, "TypeInitialization exception while performing star detection. This indicates the OpenCV library couldn't be loaded. If you have a Windows N SKU, install the Media Pack");
                    Notification.ShowError("Could not load the OpenCV library. If you have a Windows N SKU, install the Media Pack");
                    throw;
                } finally {
                    // Cleanup
                    resourceTracker.Dispose();
                }
            }
        }

        public async Task<PreparedImage> PrepareImage(IImageData imageData, StarDetectorParams p) {
            var resourceTracker = new ResourcesTracker();
            if (p.HotpixelFiltering && p.HotpixelFilterRadius != 1) {
                throw new NotImplementedException("Only hotpixel filter radius of 1 currently supported");
            }

            try {
                Mat srcImage;
                var hotpixelFilteringApplied = false;
                long numHotpixels = 0L;
                IStarDetection starDetector = starDetectorSelector.GetBehavior();
                IStarAnnotator starAnnotator = starAnnotatorSelector.GetBehavior();
                IRenderedImage renderedImage;

                var rawImageDataCopy = new ushort[imageData.Data.FlatArray.Length];
                Buffer.BlockCopy(imageData.Data.FlatArray, 0, rawImageDataCopy, 0, imageData.Data.FlatArray.Length * sizeof(ushort));
                var props = imageData.Properties;
                bool autoStretch = profileService.ActiveProfile.ImageSettings.AutoStretch;
                bool unlinkedStretch = false;

                if (imageData.Properties.IsBayered && profileService.ActiveProfile.ImageSettings.DebayerImage) {
                    unlinkedStretch = profileService.ActiveProfile.ImageSettings.UnlinkedStretch;
                    var cameraInfo = cameraMediator.GetInfo();
                    var bayerPattern = cameraInfo.SensorType;
                    if (profileService.ActiveProfile.CameraSettings.BayerPattern != BayerPatternEnum.Auto) {
                        bayerPattern = (SensorType)profileService.ActiveProfile.CameraSettings.BayerPattern;
                    } else if (!cameraInfo.Connected) {
                        var imageSensorType = imageData.MetaData?.Camera?.SensorType;
                        if (imageSensorType.HasValue) {
                            bayerPattern = imageSensorType.Value;
                        }
                    }

                    var rawImageData = new RawImageData(rawImageDataCopy, width: props.Width, height: props.Height);
                    if (p.HotpixelFiltering) {
                        ushort threshold = 0;
                        if (p.HotpixelThresholdingEnabled) {
                            threshold = (ushort)(p.HotpixelThreshold * (1 << props.BitDepth));
                        }

                        numHotpixels = HotpixelFiltering.CFAHotpixelFilter(rawImageData, bayerPattern, threshold);
                        hotpixelFilteringApplied = true;
                    }

                    var bitmapSource = ImageUtility.CreateSourceFromArray(new ImageArray(rawImageDataCopy), props, PixelFormats.Gray16);

                    var tempRenderedImage = new RenderedImage(
                        image: bitmapSource,
                        rawImageData: imageData,
                        profileService: profileService,
                        starDetection: starDetector,
                        starAnnotator: starAnnotator);
                    IDebayeredImage debayeredImage = tempRenderedImage.Debayer(true, true, bayerPattern);
                    srcImage = resourceTracker.T(CvImageUtility.ToOpenCVMat(debayeredImage.DebayeredData.Lum, bpp: props.BitDepth, width: props.Width, height: props.Height));
                    renderedImage = debayeredImage;
                } else {
                    srcImage = resourceTracker.T(CvImageUtility.ToOpenCVMat(imageData));
                    if (p.HotpixelFiltering) {
                        numHotpixels = ApplyHotpixelFilter(srcImage, p);
                        hotpixelFilteringApplied = true;
                    }

                    var bitmapSource = ImageUtility.CreateSourceFromArray(new ImageArray(rawImageDataCopy), props, PixelFormats.Gray16);
                    renderedImage = new RenderedImage(
                        image: bitmapSource,
                    rawImageData: imageData,
                        profileService: profileService,
                    starDetection: starDetector,
                        starAnnotator: starAnnotator);
                }

                if (autoStretch) {
                    renderedImage = await renderedImage.Stretch(
                        factor: profileService.ActiveProfile.ImageSettings.AutoStretchFactor,
                        blackClipping: profileService.ActiveProfile.ImageSettings.BlackClipping,
                        unlinked: unlinkedStretch);
                }
                imagingMediator.SetImage(renderedImage.Image);

                return new PreparedImage {
                    SrcImage = srcImage,
                    Image = renderedImage,
                    HotpixelFilteringApplied = hotpixelFilteringApplied,
                    HotpixelCount = numHotpixels
                };
            } catch (TypeInitializationException e) {
                Logger.Error(e, "TypeInitialization exception while performing star detection. This indicates the OpenCV library couldn't be loaded. If you have a Windows N SKU, install the Media Pack");
                Notification.ShowError("Could not load the OpenCV library. If you have a Windows N SKU, install the Media Pack");
                throw;
            } finally {
                // Cleanup
                resourceTracker.Dispose();
            }
        }

        private long ApplyHotpixelFilter(Mat img, StarDetectorParams p) {
            if (p.HotpixelThresholdingEnabled) {
                return HotpixelFiltering.HotpixelFilterWithThresholding(img, p.HotpixelThreshold);
            } else {
                HotpixelFiltering.HotpixelFilter(img);
                return 0L;
            }
        }
    }
}