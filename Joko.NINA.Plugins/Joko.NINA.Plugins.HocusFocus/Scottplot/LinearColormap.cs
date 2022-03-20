#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ScottPlot.Drawing;
using System;
using System.Drawing;

namespace NINA.Joko.Plugins.HocusFocus.Scottplot {

    public class LinearColormap : IColormap {
        private readonly Color[] rgbColors;

        public LinearColormap(string name, Color lowColor, Color midColor, Color highColor) {
            this.Name = name;
            this.rgbColors = new Color[byte.MaxValue + 1];
            for (int i = 0; i <= byte.MaxValue; ++i) {
                var ratio = (double)i / (double)byte.MaxValue;
                if (ratio < 0.5) {
                    var colorRatio = ratio * 2;
                    var r = (byte)Math.Round((midColor.R - lowColor.R) * colorRatio + lowColor.R);
                    var g = (byte)Math.Round((midColor.G - lowColor.G) * colorRatio + lowColor.G);
                    var b = (byte)Math.Round((midColor.B - lowColor.B) * colorRatio + lowColor.B);
                    this.rgbColors[i] = Color.FromArgb(r, g, b);
                } else {
                    var colorRatio = (ratio - 0.5) * 2;
                    var r = (byte)Math.Round((highColor.R - midColor.R) * colorRatio + midColor.R);
                    var g = (byte)Math.Round((highColor.G - midColor.G) * colorRatio + midColor.G);
                    var b = (byte)Math.Round((highColor.B - midColor.B) * colorRatio + midColor.B);
                    this.rgbColors[i] = Color.FromArgb(r, g, b);
                }
            }
        }

        public string Name { get; private set; }

        public (byte r, byte g, byte b) GetRGB(byte value) {
            var color = this.rgbColors[value];
            return (color.R, color.G, color.B);
        }
    }
}