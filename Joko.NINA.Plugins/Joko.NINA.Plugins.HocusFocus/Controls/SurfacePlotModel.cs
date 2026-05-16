#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Logger = NINA.Core.Utility.Logger;

namespace NINA.Joko.Plugins.HocusFocus.Controls {

    public enum ProjectionType {
        RotationBased,
        Oblique
    }

    public readonly struct PlotPoint3D {

        public PlotPoint3D(double x, double y, double z, Color color, float size = 5.0f, string label = null) {
            X = x;
            Y = y;
            Z = z;
            Color = color;
            Size = size;
            Label = label;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public Color Color { get; }
        public float Size { get; }
        public string Label { get; }
    }

    public readonly struct PlotTick {

        public PlotTick(double value, string label) {
            Value = value;
            Label = label;
            LabelColor = null;
        }

        public PlotTick(double value, string label, Color labelColor) {
            Value = value;
            Label = label;
            LabelColor = labelColor;
        }

        public double Value { get; }
        public string Label { get; }
        public Color? LabelColor { get; }
    }

    public readonly struct ScreenLabel {

        public ScreenLabel(string text, float xRatio, float yRatio, Color color) {
            Text = text;
            XRatio = xRatio;
            YRatio = yRatio;
            Color = color;
        }

        public string Text { get; }
        public float XRatio { get; }
        public float YRatio { get; }
        public Color Color { get; }
    }

    public sealed class SurfaceColorMap {
        private readonly (double position, Color color)[] stops;

        public SurfaceColorMap(params (double position, Color color)[] stops) {
            if (stops == null || stops.Length == 0) {
                throw new ArgumentException("At least one color stop is required", nameof(stops));
            }
            this.stops = stops.OrderBy(s => s.position).ToArray();
        }

        public Color GetColor(double value, double min, double max) {
            if (stops.Length == 1) {
                return stops[0].color;
            }

            var position = max <= min ? 0.5d : (value - min) / (max - min);
            position = Math.Clamp(position, 0.0d, 1.0d);
            for (int i = 1; i < stops.Length; ++i) {
                if (position <= stops[i].position) {
                    var left = stops[i - 1];
                    var right = stops[i];
                    var span = right.position - left.position;
                    var local = span <= 0.0d ? 0.0d : (position - left.position) / span;
                    return Blend(left.color, right.color, local);
                }
            }
            return stops[^1].color;
        }

        public static Color Blend(Color left, Color right, double ratio) {
            ratio = Math.Clamp(ratio, 0.0d, 1.0d);
            var inverse = 1.0d - ratio;
            return Color.FromArgb(
                (int)Math.Round(left.A * inverse + right.A * ratio),
                (int)Math.Round(left.R * inverse + right.R * ratio),
                (int)Math.Round(left.G * inverse + right.G * ratio),
                (int)Math.Round(left.B * inverse + right.B * ratio));
        }
    }

    public sealed class SurfacePlotModel {
        public double[,] X { get; set; }
        public double[,] Y { get; set; }
        public double[,] Z { get; set; }
        public double? ColorRangeMin { get; set; }
        public double? ColorRangeMax { get; set; }
        public SurfaceColorMap ColorMap { get; set; }
        public double RotationXDegrees { get; set; } = 45.0d;
        public double RotationYDegrees { get; set; } = 0.0d;
        public double RotationZDegrees { get; set; } = 25.0d;
        public double VerticalScale { get; set; } = 0.8d;
        public bool ShowSurface { get; set; } = true;
        public bool ShowContours { get; set; }
        public bool ShowContourLabels { get; set; }
        public bool ShowColorBar { get; set; }
        public int ContourCount { get; set; } = 10;
        public string ContourLabelFormat { get; set; } = "0.0";
        public string XAxisLabel { get; set; }
        public string YAxisLabel { get; set; }
        public string ZAxisLabel { get; set; }
        public List<PlotTick> XTicks { get; } = new List<PlotTick>();
        public List<PlotTick> YTicks { get; } = new List<PlotTick>();
        public List<PlotTick> ZTicks { get; } = new List<PlotTick>();
        public List<PlotPoint3D> Points { get; } = new List<PlotPoint3D>();
        public List<ScreenLabel> ScreenLabels { get; } = new List<ScreenLabel>();
        public Color BackgroundColor { get; set; } = Color.Black;
        public Color TextColor { get; set; } = Color.White;
        public Color AxisColor { get; set; } = Color.Gray;
        public Color ContourColor { get; set; } = Color.FromArgb(210, Color.Black);
        public Color? ReferencePlaneColor { get; set; }
        public double? ReferencePlaneZ { get; set; }
        public double ReferencePlaneScale { get; set; } = 1.0d;
        public bool ShowAxes { get; set; } = true;
        public int AutoZTickCount { get; set; } = 0;
        public string AutoZTickFormat { get; set; } = "0.0";
        public ProjectionType Projection { get; set; } = ProjectionType.RotationBased;
        public double ObliqueYAngleDegrees { get; set; } = 30.0d;
        public double ObliqueYScale { get; set; } = 0.5d;
    }

    public sealed class SurfacePlotRenderer {
        private readonly SurfacePlotModel model;
        private readonly int width;
        private readonly int height;
        private readonly float pixelScale;
        private readonly bool logDiagnostics;
        private Bounds bounds;
        private double xyScale;
        private double zScale;
        private double drawScale;
        private double offsetX;
        private double offsetY;

        public SurfacePlotRenderer(SurfacePlotModel model, int width, int height, float pixelScale = 1.0f, bool logDiagnostics = false) {
            this.model = model ?? throw new ArgumentNullException(nameof(model));
            this.width = Math.Max(1, width);
            this.height = Math.Max(1, height);
            this.pixelScale = pixelScale > 0.0f ? pixelScale : 1.0f;
            this.logDiagnostics = logDiagnostics;
        }

        private float S(float v) => v * pixelScale;

        private double S(double v) => v * pixelScale;

        private Font CreateFont(float pointSize, FontStyle style = FontStyle.Regular)
            => new Font(FontFamily.GenericSansSerif, pointSize * pixelScale, style);

        private void LogRenderDiagnostics(Bitmap bitmap, Graphics graphics) {
            try {
                var inv = CultureInfo.InvariantCulture;
                Logger.Info($"[SurfacePlotDiag] renderer: width={width} height={height} pixelScale={pixelScale.ToString("0.0000", inv)} " +
                    $"bitmapRes={bitmap.HorizontalResolution.ToString("0.##", inv)}x{bitmap.VerticalResolution.ToString("0.##", inv)} " +
                    $"graphicsDpi={graphics.DpiX.ToString("0.##", inv)}x{graphics.DpiY.ToString("0.##", inv)} " +
                    $"pageUnit={graphics.PageUnit} pageScale={graphics.PageScale.ToString("0.####", inv)}");

                // Design point sizes used by the renderer: contour labels, axes, point/colorbar labels, screen labels.
                foreach (var pointSize in new[] { 9.34f, 10.06f, 8.625f, 11.5f }) {
                    using (var font = CreateFont(pointSize)) {
                        var measured = graphics.MeasureString("Sample Mg", font);
                        Logger.Info($"[SurfacePlotDiag] renderer font: requestedPt={pointSize.ToString("0.###", inv)} " +
                            $"scaledPt={(pointSize * pixelScale).ToString("0.###", inv)} font.Size={font.Size.ToString("0.###", inv)} " +
                            $"font.SizeInPoints={font.SizeInPoints.ToString("0.###", inv)} font.Unit={font.Unit} " +
                            $"glyphHeightPx={font.GetHeight(graphics).ToString("0.###", inv)} " +
                            $"measure=\"Sample Mg\"={measured.Width.ToString("0.##", inv)}x{measured.Height.ToString("0.##", inv)}");
                    }
                }
            } catch (Exception e) {
                Logger.Error(e, "[SurfacePlotDiag] Failed logging renderer diagnostics");
            }
        }

        public Bitmap Render() {
            if (model.Z == null) {
                throw new ArgumentException("Surface Z data is required", nameof(model));
            }

            bounds = Bounds.FromModel(model);
            xyScale = Math.Max(bounds.XRange, bounds.YRange);
            if (xyScale <= 0.0d) {
                xyScale = 1.0d;
            }
            zScale = bounds.ZRange <= 0.0d ? 1.0d : bounds.ZRange;
            ConfigureView();

            var zBuffer = Enumerable.Repeat(double.NegativeInfinity, width * height).ToArray();
            var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap)) {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (logDiagnostics) {
                    LogRenderDiagnostics(bitmap, graphics);
                }
                graphics.Clear(model.BackgroundColor);
                if (model.ShowAxes) {
                    DrawBackBoxEdges(graphics);
                }
            }

            if (model.ShowSurface) {
                DrawSurface(bitmap, zBuffer);
            }

            if (model.ReferencePlaneColor.HasValue && model.ReferencePlaneZ.HasValue) {
                DrawReferencePlane(bitmap, zBuffer);
            }

            using (var graphics = Graphics.FromImage(bitmap)) {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                if (model.ShowContours) {
                    DrawContours(graphics);
                }
                if (model.ShowAxes) {
                    DrawAxes(graphics);
                }
                DrawPoints(graphics);
                if (model.ShowColorBar) {
                    DrawColorBar(graphics);
                }
                DrawScreenLabels(graphics);
            }
            return bitmap;
        }

        private void DrawSurface(Bitmap bitmap, double[] zBuffer) {
            var rows = model.Z.GetLength(0);
            var cols = model.Z.GetLength(1);
            var colorMin = model.ColorRangeMin ?? bounds.ZMin;
            var colorMax = model.ColorRangeMax ?? bounds.ZMax;
            var colorMap = model.ColorMap ?? new SurfaceColorMap((0.0d, Color.Gray), (1.0d, Color.White));
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);

            try {
                var stride = data.Stride;
                var absStride = Math.Abs(stride);
                var pixels = new byte[absStride * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

                for (int row = 0; row < rows - 1; ++row) {
                    for (int col = 0; col < cols - 1; ++col) {
                        var values = new[] {
                            GetPoint(row, col),
                            GetPoint(row, col + 1),
                            GetPoint(row + 1, col + 1),
                            GetPoint(row + 1, col)
                        };
                        if (values.Any(v => !double.IsFinite(v.Z))) {
                            continue;
                        }

                        var projected = values.Select(Project).ToArray();
                        var avgZ = values.Average(v => v.Z);
                        var avgDepth = projected.Average(p => p.depth);
                        var color = ApplyDepthShade(colorMap.GetColor(avgZ, colorMin, colorMax), avgDepth);

                        RasterizeTriangle(pixels, stride, zBuffer, projected[0], projected[1], projected[2], color);
                        RasterizeTriangle(pixels, stride, zBuffer, projected[0], projected[2], projected[3], color);
                    }
                }

                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            } finally {
                bitmap.UnlockBits(data);
            }
        }

        private void RasterizeTriangle(
            byte[] pixels,
            int stride,
            double[] zBuffer,
            (PointF point, double depth) v0,
            (PointF point, double depth) v1,
            (PointF point, double depth) v2,
            Color color) {
            var minX = Math.Max(0, (int)Math.Floor(Math.Min(v0.point.X, Math.Min(v1.point.X, v2.point.X))));
            var maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(v0.point.X, Math.Max(v1.point.X, v2.point.X))));
            var minY = Math.Max(0, (int)Math.Floor(Math.Min(v0.point.Y, Math.Min(v1.point.Y, v2.point.Y))));
            var maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(v0.point.Y, Math.Max(v1.point.Y, v2.point.Y))));
            if (maxX < minX || maxY < minY) {
                return;
            }

            var denominator =
                (v1.point.Y - v2.point.Y) * (v0.point.X - v2.point.X) +
                (v2.point.X - v1.point.X) * (v0.point.Y - v2.point.Y);
            if (Math.Abs(denominator) < 1e-6d) {
                return;
            }

            var absStride = Math.Abs(stride);
            for (int y = minY; y <= maxY; ++y) {
                var py = y + 0.5d;
                for (int x = minX; x <= maxX; ++x) {
                    var px = x + 0.5d;
                    var w0 =
                        ((v1.point.Y - v2.point.Y) * (px - v2.point.X) +
                         (v2.point.X - v1.point.X) * (py - v2.point.Y)) / denominator;
                    var w1 =
                        ((v2.point.Y - v0.point.Y) * (px - v2.point.X) +
                         (v0.point.X - v2.point.X) * (py - v2.point.Y)) / denominator;
                    var w2 = 1.0d - w0 - w1;
                    if (w0 < -1e-6d || w1 < -1e-6d || w2 < -1e-6d) {
                        continue;
                    }

                    var depth = w0 * v0.depth + w1 * v1.depth + w2 * v2.depth;
                    var zIndex = y * width + x;
                    if (depth <= zBuffer[zIndex]) {
                        continue;
                    }

                    zBuffer[zIndex] = depth;
                    var rowOffset = stride >= 0 ? y * stride : (height - 1 - y) * absStride;
                    BlendPixel(pixels, rowOffset + x * 4, color);
                }
            }
        }

        private static void BlendPixel(byte[] pixels, int offset, Color color) {
            if (color.A == byte.MaxValue) {
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = byte.MaxValue;
                return;
            }

            var inverseAlpha = byte.MaxValue - color.A;
            var sourceB = (color.B * color.A + 127) / byte.MaxValue;
            var sourceG = (color.G * color.A + 127) / byte.MaxValue;
            var sourceR = (color.R * color.A + 127) / byte.MaxValue;
            pixels[offset] = (byte)Math.Clamp(sourceB + (pixels[offset] * inverseAlpha + 127) / byte.MaxValue, 0, byte.MaxValue);
            pixels[offset + 1] = (byte)Math.Clamp(sourceG + (pixels[offset + 1] * inverseAlpha + 127) / byte.MaxValue, 0, byte.MaxValue);
            pixels[offset + 2] = (byte)Math.Clamp(sourceR + (pixels[offset + 2] * inverseAlpha + 127) / byte.MaxValue, 0, byte.MaxValue);
            pixels[offset + 3] = (byte)Math.Clamp(color.A + (pixels[offset + 3] * inverseAlpha + 127) / byte.MaxValue, 0, byte.MaxValue);
        }

        private void DrawReferencePlane(Bitmap bitmap, double[] zBuffer) {
            var color = model.ReferencePlaneColor.Value;
            var z = model.ReferencePlaneZ.Value;
            var centerX = (bounds.XMin + bounds.XMax) / 2.0d;
            var centerY = (bounds.YMin + bounds.YMax) / 2.0d;
            var halfX = bounds.XRange * model.ReferencePlaneScale / 2.0d;
            var halfY = bounds.YRange * model.ReferencePlaneScale / 2.0d;
            var corners = new[] {
                new Vertex3D(centerX - halfX, centerY - halfY, z),
                new Vertex3D(centerX + halfX, centerY - halfY, z),
                new Vertex3D(centerX + halfX, centerY + halfY, z),
                new Vertex3D(centerX - halfX, centerY + halfY, z)
            }.Select(Project).ToArray();

            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try {
                var stride = data.Stride;
                var pixels = new byte[Math.Abs(stride) * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                RasterizeTriangle(pixels, stride, zBuffer, corners[0], corners[1], corners[2], color);
                RasterizeTriangle(pixels, stride, zBuffer, corners[0], corners[2], corners[3], color);
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            } finally {
                bitmap.UnlockBits(data);
            }
        }

        private void DrawContours(Graphics graphics) {
            var levels = GetContourLevels().ToArray();
            if (levels.Length == 0) {
                return;
            }

            var labelCandidates = new List<(double level, PointF p1, PointF p2, double length)>();
            using (var pen = new Pen(model.ContourColor, S(1.0f))) {
                foreach (var segment in GenerateContourSegments(levels)) {
                    var p1 = Project(segment.start).point;
                    var p2 = Project(segment.end).point;
                    graphics.DrawLine(pen, p1, p2);
                    if (model.ShowContourLabels) {
                        var dx = p2.X - p1.X;
                        var dy = p2.Y - p1.Y;
                        labelCandidates.Add((segment.level, p1, p2, Math.Sqrt(dx * dx + dy * dy)));
                    }
                }
            }

            if (model.ShowContourLabels && labelCandidates.Count > 0) {
                DrawContourLabels(graphics, labelCandidates);
            }
        }

        private void DrawContourLabels(Graphics graphics, List<(double level, PointF p1, PointF p2, double length)> labelCandidates) {
            using (var font = CreateFont(9.34f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(model.TextColor))
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(220, model.BackgroundColor)))
            using (var borderPen = new Pen(Color.FromArgb(180, model.ContourColor))) {
                foreach (var candidate in labelCandidates
                    .GroupBy(c => c.level)
                    .Select(g => g.OrderBy(c => DistanceToPlotCenter(c)).First())) {
                    var label = candidate.level.ToString(model.ContourLabelFormat);
                    var midpoint = new PointF(
                        (candidate.p1.X + candidate.p2.X) / 2.0f,
                        (candidate.p1.Y + candidate.p2.Y) / 2.0f);
                    DrawLabelBox(graphics, label, font, textBrush, backgroundBrush, borderPen, midpoint);
                }
            }
        }

        private double DistanceToPlotCenter((double level, PointF p1, PointF p2, double length) candidate) {
            var x = (candidate.p1.X + candidate.p2.X) / 2.0d - width / 2.0d;
            var y = (candidate.p1.Y + candidate.p2.Y) / 2.0d - height / 2.0d;
            return x * x + y * y;
        }

        private IEnumerable<double> GetContourLevels() {
            var min = model.ColorRangeMin ?? bounds.ZMin;
            var max = model.ColorRangeMax ?? bounds.ZMax;
            if (model.ContourCount <= 0 || max <= min) {
                yield break;
            }

            var interval = (max - min) / (model.ContourCount + 1);
            for (int i = 1; i <= model.ContourCount; ++i) {
                yield return min + i * interval;
            }
        }

        private IEnumerable<(double level, Vertex3D start, Vertex3D end)> GenerateContourSegments(IEnumerable<double> levels) {
            var rows = model.Z.GetLength(0);
            var cols = model.Z.GetLength(1);
            foreach (var level in levels) {
                for (int row = 0; row < rows - 1; ++row) {
                    for (int col = 0; col < cols - 1; ++col) {
                        var corners = new[] {
                            GetPoint(row, col),
                            GetPoint(row, col + 1),
                            GetPoint(row + 1, col + 1),
                            GetPoint(row + 1, col)
                        };
                        if (corners.Any(c => !double.IsFinite(c.Z))) {
                            continue;
                        }

                        var intersections = new List<Vertex3D>(4);
                        AddContourIntersection(intersections, corners[0], corners[1], level);
                        AddContourIntersection(intersections, corners[1], corners[2], level);
                        AddContourIntersection(intersections, corners[2], corners[3], level);
                        AddContourIntersection(intersections, corners[3], corners[0], level);
                        if (intersections.Count == 2) {
                            yield return (level, intersections[0], intersections[1]);
                        } else if (intersections.Count == 4) {
                            yield return (level, intersections[0], intersections[1]);
                            yield return (level, intersections[2], intersections[3]);
                        }
                    }
                }
            }
        }

        private static void AddContourIntersection(List<Vertex3D> intersections, Vertex3D a, Vertex3D b, double level) {
            var da = a.Z - level;
            var db = b.Z - level;
            if (da == 0.0d && db == 0.0d) {
                return;
            }
            if ((da < 0.0d && db < 0.0d) || (da > 0.0d && db > 0.0d)) {
                return;
            }

            var denominator = b.Z - a.Z;
            if (Math.Abs(denominator) < 1e-12) {
                return;
            }

            var t = (level - a.Z) / denominator;
            if (t < 0.0d || t > 1.0d) {
                return;
            }

            intersections.Add(new Vertex3D(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                level));
        }

        private (Vertex3D, Vertex3D)[] GetBoxEdges() => new[] {
            // Bottom face
            (new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMin), new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMin)),
            (new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMin), new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMin)),
            (new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMin), new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMin)),
            (new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMin), new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMin)),
            // Top face
            (new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMax), new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMax)),
            (new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMax), new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMax)),
            (new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMax), new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMax)),
            (new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMax), new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMax)),
            // Vertical edges
            (new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMin), new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMax)),
            (new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMin), new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMax)),
            (new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMin), new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMax)),
            (new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMin), new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMax))
        };

        private double ComputeAvgBoxCornerDepth() {
            return new[] {
                new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMin),
                new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMax),
                new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMin),
                new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMax),
                new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMin),
                new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMax),
                new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMin),
                new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMax),
            }.Average(c => Project(c).depth);
        }

        private void DrawBackBoxEdges(Graphics graphics) {
            var avgDepth = ComputeAvgBoxCornerDepth();
            using (var pen = new Pen(model.AxisColor, S(1.0f))) {
                foreach (var edge in GetBoxEdges()) {
                    var projA = Project(edge.Item1);
                    var projB = Project(edge.Item2);
                    if (projA.depth < avgDepth && projB.depth < avgDepth) {
                        graphics.DrawLine(pen, projA.point, projB.point);
                    }
                }
            }
        }

        private void DrawAxes(Graphics graphics) {
            var avgDepth = ComputeAvgBoxCornerDepth();
            using (var pen = new Pen(model.AxisColor, S(1.0f)))
            using (var brush = new SolidBrush(model.TextColor))
            using (var font = CreateFont(10.06f)) {
                foreach (var edge in GetBoxEdges()) {
                    var projA = Project(edge.Item1);
                    var projB = Project(edge.Item2);
                    if (!(projA.depth < avgDepth && projB.depth < avgDepth)) {
                        graphics.DrawLine(pen, projA.point, projB.point);
                    }
                }

                DrawTicks(graphics, pen, font);
                DrawAxisLabels(graphics, brush, font);
            }
        }

        private void DrawTicks(Graphics graphics, Pen pen, Font font) {
            foreach (var tick in model.XTicks) {
                var projected = Project(new Vertex3D(tick.Value, bounds.YMin, bounds.ZMin)).point;
                graphics.DrawLine(pen, projected.X, projected.Y, projected.X, projected.Y + S(5f));
                using (var brush = new SolidBrush(tick.LabelColor ?? model.TextColor)) {
                    graphics.DrawString(tick.Label, font, brush, projected.X - S(12f), projected.Y + S(7f));
                }
            }
            foreach (var tick in model.YTicks) {
                var projected = Project(new Vertex3D(bounds.XMin, tick.Value, bounds.ZMax)).point;
                using (var brush = new SolidBrush(tick.LabelColor ?? model.TextColor)) {
                    graphics.DrawString(tick.Label, font, brush, projected.X + S(3f), projected.Y - S(20f));
                }
            }
            IEnumerable<PlotTick> zTicks;
            if (model.ZTicks.Count > 0) {
                zTicks = model.ZTicks;
            } else if (model.AutoZTickCount > 0 && model.VerticalScale > 0) {
                zTicks = GenerateAutoZTicks();
            } else {
                zTicks = Enumerable.Empty<PlotTick>();
            }
            foreach (var tick in zTicks) {
                var projected = Project(new Vertex3D(bounds.XMin, bounds.YMin, tick.Value)).point;
                graphics.DrawLine(pen, projected.X, projected.Y, projected.X - S(5f), projected.Y);
                using (var brush = new SolidBrush(tick.LabelColor ?? model.TextColor)) {
                    var labelSize = graphics.MeasureString(tick.Label, font);
                    graphics.DrawString(tick.Label, font, brush, projected.X - S(8f) - labelSize.Width, projected.Y - labelSize.Height / 2.0f);
                }
            }
        }

        private IEnumerable<PlotTick> GenerateAutoZTicks() {
            var min = bounds.ZMin;
            var max = bounds.ZMax;
            var range = max - min;
            if (range <= 0 || model.AutoZTickCount <= 0) {
                yield break;
            }

            var rawStep = range / (model.AutoZTickCount + 1);
            var magnitude = Math.Pow(10.0d, Math.Floor(Math.Log10(rawStep)));
            var normalized = rawStep / magnitude;
            double niceStep;
            if (normalized < 1.5d) niceStep = magnitude;
            else if (normalized < 3.5d) niceStep = 2.0d * magnitude;
            else if (normalized < 7.5d) niceStep = 5.0d * magnitude;
            else niceStep = 10.0d * magnitude;

            var firstTick = Math.Ceiling(min / niceStep) * niceStep;
            var value = firstTick;
            while (value <= max + niceStep * 1e-9) {
                if (value >= min - niceStep * 1e-9) {
                    yield return new PlotTick(value, value.ToString(model.AutoZTickFormat));
                }
                value += niceStep;
            }
        }

        private void DrawAxisLabels(Graphics graphics, Brush brush, Font font) {
            if (!string.IsNullOrWhiteSpace(model.ZAxisLabel)) {
                var zRef = Math.Clamp(0.0d, bounds.ZMin, bounds.ZMax);
                var projected = Project(new Vertex3D(bounds.XMin, bounds.YMin, zRef)).point;
                var size = graphics.MeasureString(model.ZAxisLabel, font);
                var state = graphics.Save();
                graphics.TranslateTransform(projected.X - S(68.0f), height / 2.0f);
                graphics.RotateTransform(-90.0f);
                graphics.DrawString(model.ZAxisLabel, font, brush, -size.Width / 2.0f, -size.Height / 2.0f);
                graphics.Restore(state);
            }
            if (!string.IsNullOrWhiteSpace(model.XAxisLabel)) {
                var projected = Project(new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMin)).point;
                graphics.DrawString(model.XAxisLabel, font, brush, projected.X + S(4f), projected.Y + S(4f));
            }
            if (!string.IsNullOrWhiteSpace(model.YAxisLabel)) {
                var projected = Project(new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMin)).point;
                graphics.DrawString(model.YAxisLabel, font, brush, projected.X - S(20f), projected.Y + S(4f));
            }
        }

        private void DrawPoints(Graphics graphics) {
            var points = model.Points
                .Select(p => (point: p, projection: Project(new Vertex3D(p.X, p.Y, p.Z))))
                .OrderBy(p => p.projection.depth)
                .ToArray();

            foreach (var item in points) {
                var point = item.point;
                var projected = item.projection.point;
                using (var brush = new SolidBrush(point.Color))
                using (var pen = new Pen(Color.FromArgb(220, model.BackgroundColor), S(1.0f))) {
                    var diameter = S(point.Size);
                    var radius = diameter / 2.0f;
                    graphics.FillEllipse(brush, projected.X - radius, projected.Y - radius, diameter, diameter);
                    graphics.DrawEllipse(pen, projected.X - radius, projected.Y - radius, diameter, diameter);
                }
            }

            using (var font = CreateFont(8.625f, FontStyle.Bold))
            using (var textBrush = new SolidBrush(model.TextColor))
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(220, model.BackgroundColor)))
            using (var borderPen = new Pen(Color.FromArgb(150, model.AxisColor))) {
                foreach (var item in points.Where(p => !string.IsNullOrWhiteSpace(p.point.Label))) {
                    var labelPoint = new PointF(
                        item.projection.point.X,
                        item.projection.point.Y - S(item.point.Size) / 2.0f - S(8.0f));
                    DrawLabelBox(graphics, item.point.Label, font, textBrush, backgroundBrush, borderPen, labelPoint, S(2.0f), S(0.5f));
                }
            }
        }

        private void DrawColorBar(Graphics graphics) {
            var colorMap = model.ColorMap ?? new SurfaceColorMap((0.0d, Color.Black), (1.0d, Color.White));
            var min = model.ColorRangeMin ?? bounds.ZMin;
            var max = model.ColorRangeMax ?? bounds.ZMax;
            var barWidth = (int)Math.Max(1, S(14.0));
            var barHeight = Math.Max((int)S(80.0), height / 3);
            var x = width - (int)S(34.0);
            var y = Math.Max((int)S(14.0), height / 2 - barHeight / 2);

            for (int i = 0; i < barHeight; ++i) {
                var ratio = 1.0d - i / (double)Math.Max(1, barHeight - 1);
                var value = min + ratio * (max - min);
                using (var pen = new Pen(colorMap.GetColor(value, min, max))) {
                    graphics.DrawLine(pen, x, y + i, x + barWidth, y + i);
                }
            }

            using (var pen = new Pen(model.AxisColor, S(1.0f)))
            using (var brush = new SolidBrush(model.TextColor))
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(220, model.BackgroundColor)))
            using (var font = CreateFont(8.625f, FontStyle.Bold)) {
                graphics.DrawRectangle(pen, x, y, barWidth, barHeight);
                DrawLabelBox(graphics, max.ToString("0.0"), font, brush, backgroundBrush, pen, new PointF(x - S(10.0f), y));
                DrawLabelBox(graphics, min.ToString("0.0"), font, brush, backgroundBrush, pen, new PointF(x - S(10.0f), y + barHeight));
            }
        }

        private void DrawLabelBox(
            Graphics graphics,
            string text,
            Font font,
            Brush textBrush,
            Brush backgroundBrush,
            Pen borderPen,
            PointF center,
            float? horizontalPadding = null,
            float? verticalPadding = null) {
            if (string.IsNullOrWhiteSpace(text)) {
                return;
            }

            var hPad = horizontalPadding ?? S(3.0f);
            var vPad = verticalPadding ?? S(1.5f);
            var edgePad = S(2.0f);
            var size = graphics.MeasureString(text, font);
            var x = Math.Clamp(center.X - size.Width / 2.0f, edgePad, Math.Max(edgePad, width - size.Width - hPad * 2.0f - edgePad));
            var y = Math.Clamp(center.Y - size.Height / 2.0f, edgePad, Math.Max(edgePad, height - size.Height - vPad * 2.0f - edgePad));
            var rect = new RectangleF(
                x - hPad,
                y - vPad,
                size.Width + hPad * 2.0f,
                size.Height + vPad * 2.0f);
            graphics.FillRectangle(backgroundBrush, rect);
            graphics.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);
            graphics.DrawString(text, font, textBrush, x, y);
        }

        private void DrawScreenLabels(Graphics graphics) {
            using (var font = CreateFont(11.5f)) {
                foreach (var label in model.ScreenLabels) {
                    using (var brush = new SolidBrush(label.Color)) {
                        var size = graphics.MeasureString(label.Text, font);
                        graphics.DrawString(label.Text, font, brush, label.XRatio * width - size.Width / 2.0f, label.YRatio * height - size.Height / 2.0f);
                    }
                }
            }
        }

        private Vertex3D GetPoint(int row, int col) {
            var x = model.X == null ? col : model.X[row, col];
            var y = model.Y == null ? row : model.Y[row, col];
            return new Vertex3D(x, y, model.Z[row, col]);
        }

        private void ConfigureView() {
            var projected = GetFitVertices()
                .Where(v => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z))
                .Select(ProjectNormalized)
                .ToArray();
            if (projected.Length == 0) {
                drawScale = Math.Min(width, height) * 0.34d;
                offsetX = width * 0.50d;
                offsetY = height * 0.54d;
                return;
            }

            var minX = projected.Min(p => p.x);
            var maxX = projected.Max(p => p.x);
            var minY = projected.Min(p => p.y);
            var maxY = projected.Max(p => p.y);
            var rangeX = Math.Max(1e-6d, maxX - minX);
            var rangeY = Math.Max(1e-6d, maxY - minY);
            var leftMargin = model.ShowAxes ? S(90.0) : S(8.0);
            var rightMargin = model.ShowColorBar ? S(78.0) : (model.ShowAxes ? S(28.0) : S(8.0));
            var topMargin = model.ScreenLabels.Count > 0 ? Math.Max(S(18.0), height * 0.1d) : (model.ShowAxes ? S(24.0) : S(8.0));
            var bottomMargin = model.ScreenLabels.Count > 0 ? Math.Max(S(18.0), height * 0.1d) : (model.ShowAxes ? S(28.0) : S(8.0));
            var availableWidth = Math.Max(1.0d, width - leftMargin - rightMargin);
            var availableHeight = Math.Max(1.0d, height - topMargin - bottomMargin);
            drawScale = 0.92d * Math.Min(availableWidth / rangeX, availableHeight / rangeY);

            var projectedCenterX = (minX + maxX) / 2.0d;
            var projectedCenterY = (minY + maxY) / 2.0d;
            offsetX = leftMargin + availableWidth / 2.0d - projectedCenterX * drawScale;
            offsetY = topMargin + availableHeight / 2.0d + projectedCenterY * drawScale;
        }

        private IEnumerable<Vertex3D> GetFitVertices() {
            var rows = model.Z.GetLength(0);
            var cols = model.Z.GetLength(1);
            for (int row = 0; row < rows; ++row) {
                for (int col = 0; col < cols; ++col) {
                    yield return GetPoint(row, col);
                }
            }

            foreach (var point in model.Points) {
                yield return new Vertex3D(point.X, point.Y, point.Z);
            }

            yield return new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMin);
            yield return new Vertex3D(bounds.XMin, bounds.YMin, bounds.ZMax);
            yield return new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMin);
            yield return new Vertex3D(bounds.XMin, bounds.YMax, bounds.ZMax);
            yield return new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMin);
            yield return new Vertex3D(bounds.XMax, bounds.YMin, bounds.ZMax);
            yield return new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMin);
            yield return new Vertex3D(bounds.XMax, bounds.YMax, bounds.ZMax);

            if (model.ReferencePlaneZ.HasValue) {
                var z = model.ReferencePlaneZ.Value;
                var centerX = (bounds.XMin + bounds.XMax) / 2.0d;
                var centerY = (bounds.YMin + bounds.YMax) / 2.0d;
                var halfX = bounds.XRange * model.ReferencePlaneScale / 2.0d;
                var halfY = bounds.YRange * model.ReferencePlaneScale / 2.0d;
                yield return new Vertex3D(centerX - halfX, centerY - halfY, z);
                yield return new Vertex3D(centerX + halfX, centerY - halfY, z);
                yield return new Vertex3D(centerX + halfX, centerY + halfY, z);
                yield return new Vertex3D(centerX - halfX, centerY + halfY, z);
            }
        }

        private (PointF point, double depth) Project(Vertex3D vertex) {
            var projected = ProjectNormalized(vertex);
            return (new PointF((float)(offsetX + projected.x * drawScale), (float)(offsetY - projected.y * drawScale)), projected.depth);
        }

        private (double x, double y, double depth) ProjectNormalized(Vertex3D vertex) {
            var x = (vertex.X - bounds.XCenter) / xyScale;
            var y = (vertex.Y - bounds.YCenter) / xyScale;
            var z = (vertex.Z - bounds.ZCenter) / zScale * model.VerticalScale;

            if (model.Projection == ProjectionType.Oblique) {
                var angle = model.ObliqueYAngleDegrees * Math.PI / 180.0d;
                var screenX = x + y * Math.Cos(angle) * model.ObliqueYScale;
                var screenY = z + y * Math.Sin(angle) * model.ObliqueYScale;
                return (screenX, screenY, -y + z * 1e-3);
            }

            var rx = model.RotationXDegrees * Math.PI / 180.0d;
            var ry = model.RotationYDegrees * Math.PI / 180.0d;
            var rz = model.RotationZDegrees * Math.PI / 180.0d;

            var cosX = Math.Cos(rx);
            var sinX = Math.Sin(rx);
            var y1 = y * cosX - z * sinX;
            var z1 = y * sinX + z * cosX;

            var cosY = Math.Cos(ry);
            var sinY = Math.Sin(ry);
            var x1 = x * cosY + z1 * sinY;
            var z2 = -x * sinY + z1 * cosY;

            var cosZ = Math.Cos(rz);
            var sinZ = Math.Sin(rz);
            var x2 = x1 * cosZ - y1 * sinZ;
            var y2 = x1 * sinZ + y1 * cosZ;

            return (x2, y2, z2);
        }

        private static Color ApplyDepthShade(Color color, double depth) {
            var shade = Math.Clamp(0.88d + depth * 0.18d, 0.62d, 1.12d);
            return Color.FromArgb(
                color.A,
                Math.Clamp((int)Math.Round(color.R * shade), 0, 255),
                Math.Clamp((int)Math.Round(color.G * shade), 0, 255),
                Math.Clamp((int)Math.Round(color.B * shade), 0, 255));
        }

        private readonly struct SurfaceCell {
            public SurfaceCell(PointF[] points, double depth, Color color) {
                Points = points;
                Depth = depth;
                Color = color;
            }

            public PointF[] Points { get; }
            public double Depth { get; }
            public Color Color { get; }
        }

        private readonly struct Vertex3D {
            public Vertex3D(double x, double y, double z) {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }
        }

        private readonly struct Bounds {
            public Bounds(double xMin, double xMax, double yMin, double yMax, double zMin, double zMax) {
                XMin = xMin;
                XMax = xMax;
                YMin = yMin;
                YMax = yMax;
                ZMin = zMin;
                ZMax = zMax;
            }

            public double XMin { get; }
            public double XMax { get; }
            public double YMin { get; }
            public double YMax { get; }
            public double ZMin { get; }
            public double ZMax { get; }
            public double XRange => XMax - XMin;
            public double YRange => YMax - YMin;
            public double ZRange => ZMax - ZMin;
            public double XCenter => (XMin + XMax) / 2.0d;
            public double YCenter => (YMin + YMax) / 2.0d;
            public double ZCenter => (ZMin + ZMax) / 2.0d;

            public static Bounds FromModel(SurfacePlotModel model) {
                var rows = model.Z.GetLength(0);
                var cols = model.Z.GetLength(1);
                var xMin = double.PositiveInfinity;
                var xMax = double.NegativeInfinity;
                var yMin = double.PositiveInfinity;
                var yMax = double.NegativeInfinity;
                var zMin = double.PositiveInfinity;
                var zMax = double.NegativeInfinity;

                for (int row = 0; row < rows; ++row) {
                    for (int col = 0; col < cols; ++col) {
                        var x = model.X == null ? col : model.X[row, col];
                        var y = model.Y == null ? row : model.Y[row, col];
                        var z = model.Z[row, col];
                        if (double.IsFinite(x)) {
                            xMin = Math.Min(xMin, x);
                            xMax = Math.Max(xMax, x);
                        }
                        if (double.IsFinite(y)) {
                            yMin = Math.Min(yMin, y);
                            yMax = Math.Max(yMax, y);
                        }
                        if (double.IsFinite(z)) {
                            zMin = Math.Min(zMin, z);
                            zMax = Math.Max(zMax, z);
                        }
                    }
                }

                foreach (var point in model.Points) {
                    xMin = Math.Min(xMin, point.X);
                    xMax = Math.Max(xMax, point.X);
                    yMin = Math.Min(yMin, point.Y);
                    yMax = Math.Max(yMax, point.Y);
                    zMin = Math.Min(zMin, point.Z);
                    zMax = Math.Max(zMax, point.Z);
                }

                if (model.ReferencePlaneZ.HasValue) {
                    zMin = Math.Min(zMin, model.ReferencePlaneZ.Value);
                    zMax = Math.Max(zMax, model.ReferencePlaneZ.Value);
                }

                if (!double.IsFinite(xMin) || !double.IsFinite(yMin) || !double.IsFinite(zMin)) {
                    xMin = yMin = zMin = 0.0d;
                    xMax = yMax = zMax = 1.0d;
                }
                if (xMax <= xMin) {
                    xMax = xMin + 1.0d;
                }
                if (yMax <= yMin) {
                    yMax = yMin + 1.0d;
                }
                if (zMax <= zMin) {
                    zMax = zMin + 1.0d;
                }

                return new Bounds(xMin, xMax, yMin, yMax, zMin, zMax);
            }
        }
    }
}
