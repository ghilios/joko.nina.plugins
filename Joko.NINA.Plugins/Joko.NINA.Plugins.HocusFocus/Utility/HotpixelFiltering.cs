#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Imaging;
using NINA.Core.Enum;
using NINA.Core.Locale;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public class RawImageData {

        public RawImageData(ushort[] imageData, int width, int height) {
            if (imageData.Length != (width * height)) {
                throw new ArgumentException($"Image Data array length {imageData.Length} does not equal expected length for {width} x {height}");
            }

            this.Data = imageData;
            this.Width = width;
            this.Height = height;
        }

        public ushort[] Data { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public ushort GetPixel(int x, int y) {
            return Data[y * Width + x];
        }

        public void SetPixel(int x, int y, ushort value) {
            Data[y * Width + x] = value;
        }
    }

    public static class HotpixelFiltering {

        public static void HotpixelFilter(Mat m) {
            Cv2.MedianBlur(m, m, 3);
        }

        public static void HotpixelFilterWithThresholding(Mat m, double threshold) {
            using (var blurred = new Mat())
            using (var diff = new Mat()) {
                Cv2.MedianBlur(m, blurred, 3);
                Cv2.Absdiff(m, blurred, diff);
                using (var mask = new Mat()) {
                    Cv2.Threshold(diff, mask, threshold, 1.0, ThresholdTypes.Binary);
                    Cv2.CopyTo(blurred, m, ~mask);
                }
            }
        }

        public static void CFAHotpixelFilter(RawImageData imageData, SensorType bayerPattern, ushort threshold) {
            int firstRowG, secondRowG;
            if (bayerPattern == SensorType.BGGR) {
                firstRowG = 1;
                secondRowG = 0;
            } else if (bayerPattern == SensorType.BGRG) {
                firstRowG = 1;
                secondRowG = 1;
            } else if (bayerPattern == SensorType.GBGR) {
                firstRowG = 0;
                secondRowG = 0;
            } else if (bayerPattern == SensorType.GBRG) {
                firstRowG = 0;
                secondRowG = 1;
            } else if (bayerPattern == SensorType.GRBG) {
                firstRowG = 0;
                secondRowG = 1;
            } else if (bayerPattern == SensorType.GRGB) {
                firstRowG = 0;
                secondRowG = 0;
            } else if (bayerPattern == SensorType.RGBG) {
                firstRowG = 1;
                secondRowG = 1;
            } else if (bayerPattern == SensorType.RGGB) {
                firstRowG = 1;
                secondRowG = 0;
            } else {
                throw new InvalidImagePropertiesException(string.Format(Loc.Instance["LblUnsupportedCfaPattern"], bayerPattern));
            }

            int intThreshold = threshold;
            for (int y = 0; y < imageData.Height; ++y) {
                bool useAdjacent;
                if (y % 2 == 0) {
                    useAdjacent = firstRowG == 0;
                } else {
                    useAdjacent = secondRowG == 0;
                }

                for (int x = 0; x < imageData.Width; ++x) {
                    ApplyHotpixelFilter(imageData, x: x, y: y, useAdjacent: useAdjacent, threshold: intThreshold);
                    useAdjacent = !useAdjacent;
                }
            }
        }

        private static ushort CalculateMedianPixel(RawImageData imageData, int x, int y, bool useAdjacent, ushort centerPixel) {
            var distance = useAdjacent ? 1 : 2;
            if (x < distance) {
                if (y < distance) {
                    // In the top-left corner
                    return Math.Min(centerPixel, imageData.GetPixel(x: x + distance, y: y + distance));
                } else if (y > (imageData.Height - distance)) {
                    // In the bottom-left corner
                    return Math.Min(centerPixel, imageData.GetPixel(x: x + distance, y: y - distance));
                } else {
                    // Along left-edge
                    return Median_3(
                        centerPixel,
                        imageData.GetPixel(x: x + distance, y: y - distance),
                        imageData.GetPixel(x: x + distance, y: y + distance));
                }
            } else if (x > (imageData.Width - distance)) {
                if (y < distance) {
                    // In the top-right corner
                    return Math.Min(centerPixel, imageData.GetPixel(x: x - distance, y: y + distance));
                } else if (y > (imageData.Height - distance)) {
                    // In the bottom-right corner
                    return Math.Min(centerPixel, imageData.GetPixel(x: x - distance, y: y - distance));
                } else {
                    // Along right-edge
                    return Median_3(
                        centerPixel,
                        imageData.GetPixel(x: x - distance, y: y - distance),
                        imageData.GetPixel(x: x - distance, y: y + distance));
                }
            } else if (y < distance) {
                // Along top-edge
                return Median_3(
                    centerPixel,
                    imageData.GetPixel(x: x - distance, y: y + distance),
                    imageData.GetPixel(x: x + distance, y: y + distance));
            } else if (y > (imageData.Height - distance)) {
                // Along bottom-edge
                return Median_3(
                    centerPixel,
                    imageData.GetPixel(x: x - distance, y: y - distance),
                    imageData.GetPixel(x: x + distance, y: y - distance));
            }

            // Edge and corner cases all accounted for. Now we can do a regular 5-way median
            return Median_5(
                centerPixel,
                imageData.GetPixel(x: x - distance, y: y - distance),
                imageData.GetPixel(x: x + distance, y: y - distance),
                imageData.GetPixel(x: x - distance, y: y + distance),
                imageData.GetPixel(x: x + distance, y: y + distance));
        }

        private static void ApplyHotpixelFilter(RawImageData imageData, int x, int y, bool useAdjacent, int threshold) {
            var centerPixel = imageData.GetPixel(x: x, y: y);
            var medianPixelValue = CalculateMedianPixel(imageData, x: x, y: y, useAdjacent: useAdjacent, centerPixel: centerPixel);
            if (Math.Abs(medianPixelValue - centerPixel) >= threshold) {
                imageData.SetPixel(x: x, y: y, medianPixelValue);
            }
        }

        private static ushort Median_3(ushort a, ushort b, ushort c) {
            return Math.Max(Math.Min(a, b), Math.Min(c, Math.Max(a, b)));
        }

        private static ushort Median_5(ushort a, ushort b, ushort c, ushort d, ushort e) {
            ushort f = Math.Max(Math.Min(a, b), Math.Min(c, d)); // discards lowest from first 4
            ushort g = Math.Min(Math.Max(a, b), Math.Max(c, d)); // discards biggest from first 4
            return Median_3(e, f, g);
        }
    }
}