using OxyPlot.Series;
using System;
using System.Collections.Generic;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Synthetic {

    internal static class SyntheticFocusCurveSamples {

        // y = a / b * sqrt((x - x0)^2 + b^2) + y0
        public static double Hyperbola(double x, double x0, double y0, double a, double b) {
            var dx = x - x0;
            return a / b * Math.Sqrt(dx * dx + b * b) + y0;
        }

        public static List<ScatterErrorPoint> SymmetricHyperbolaPoints(
            double x0,
            double y0,
            double a,
            double b,
            double xStart,
            double xStep,
            int count,
            double errorY = 1.0) {
            var points = new List<ScatterErrorPoint>(count);
            for (var i = 0; i < count; ++i) {
                var x = xStart + i * xStep;
                var y = Hyperbola(x, x0, y0, a, b);
                points.Add(new ScatterErrorPoint(x, y, 0, errorY));
            }
            return points;
        }
    }
}
