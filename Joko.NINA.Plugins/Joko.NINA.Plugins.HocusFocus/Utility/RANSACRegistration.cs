#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using MathNet.Numerics.LinearAlgebra;
using NINA.Core.Model;
using NINA.Core.Utility;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public class Point2D {
        public double X { get; private set; }
        public double Y { get; private set; }
        public double NormalisedBrightness { get; private set; } // Optional: for brightness filtering

        public Point2D(double x, double y, double normalisedBrightness = 0) {
            X = x;
            Y = y;
            NormalisedBrightness = normalisedBrightness;
        }

        public Point2D(Accord.Point point) {
            X = point.X;
            Y = point.Y;
            NormalisedBrightness = 0;
        }

        public Point2D(System.Drawing.Point point) {
            X = point.X;
            Y = point.Y;
            NormalisedBrightness = 0;
        }

        public PointF AsPointF() {
            return new PointF((float)X, (float)Y);
        }

        public System.Drawing.Point AsPoint() {
            return new System.Drawing.Point((int)X, (int)Y);
        }

        public override string ToString() {
            return $"{X}, {Y} ({NormalisedBrightness})";
        }
    }

    public class SimilarityTransform {
        public double Scale { get; private set; }
        public double Rotation { get; private set; } // In radians
        public double Tx { get; private set; } // Translation X
        public double Ty { get; private set; } // Translation Y

        private double cosTheta;
        private double sinTheta;

        public SimilarityTransform(double scale, double rotation, double tx, double ty) {
            Scale = scale;
            Rotation = rotation;
            cosTheta = Math.Cos(Rotation);
            sinTheta = Math.Sin(Rotation);
            Tx = tx;
            Ty = ty;
        }

        public Point2D Transform(Point2D point) {
            double x = Scale * (cosTheta * point.X - sinTheta * point.Y) + Tx;
            double y = Scale * (sinTheta * point.X + cosTheta * point.Y) + Ty;
            return new Point2D(x, y);
        }

        public override string ToString() {
            return $"S:{Scale}, R:{Rotation}, Tx:{Tx}, Ty:{Ty}";
        }
    }

    public class RANSACRegistration {
        private static Random RNG = new();

        // Generate putative matches using nearest neighbor
        public static (List<Point2D> srcPoints, List<Point2D> dstPoints) GeneratePutativeMatchesUsingNN(
            List<Point2D> imageStars,
            List<Point2D> referenceStars,
            double maxDistance,
            double relativeBrightnessDiff,
            ApplicationStatus status) {
            var srcPoints = new List<Point2D>();
            var dstPoints = new List<Point2D>();
            double maxDistance2 = maxDistance * maxDistance;    // distance stays as squared

            // For each star in this image, find closest match in the reference image
            foreach (var imageStar in imageStars) {
                Point2D bestMatch = null;
                double bestDistance = double.MaxValue;

                foreach (var referenceStar in referenceStars) {
                    if (Math.Abs(imageStar.NormalisedBrightness - referenceStar.NormalisedBrightness) <= relativeBrightnessDiff) {
                        double distance2 = MathUtility.CalcDistance(imageStar, referenceStar);

                        if (distance2 < bestDistance && distance2 < maxDistance2) {
                            bestDistance = distance2;
                            bestMatch = referenceStar;
                        }
                    }
                }

                if (bestMatch != null) {
                    srcPoints.Add(imageStar);
                    dstPoints.Add(bestMatch);
                }
            }

            return (srcPoints, dstPoints);
        }

        public class StarTriangle {
            private int referenceID;
            private List<double> normalizedLengths;
            private List<double> normalizedBrightnesses;
            private List<Point2D> normalizedPoints;
            private bool matched;
            private bool isReference;   // just used for annotating images with triangles
            private double matchScore;

            public List<Point2D> Points { get; private set; }
            public Point2D P1 { get => Points[0]; }
            public Point2D P2 { get => Points[1]; }
            public Point2D P3 { get => Points[2]; }

            public StarTriangle(System.Drawing.Size imageSize, double minBrightness, double maxBrightness, Point2D p1, Point2D p2, Point2D p3, bool isReference, int referenceID) {
                Points = new List<Point2D> { p1, p2, p3 }.OrderBy(p => (p == p1) ? 0 : AngleFromHorizontal(p1, p)).ToList();
                this.isReference = isReference;
                if (isReference) {
                    this.referenceID = referenceID;
                }

                normalizedLengths = normalizeLength();
                normalizedBrightnesses = normalizeBrightnesses(minBrightness, maxBrightness);
                normalizedPoints = normalizePositions(imageSize, minBrightness, maxBrightness);
            }

            public bool SameTriangle(StarTriangle other) {
                return ((this.P1 == other.P1) && (this.P2 == other.P2) && (this.P3 == other.P3));
            }

            private List<double> normalizeLength() {
                var squaredLengths = new List<double> {
                    lineLengthSquared(Points[0], Points[1]),
                    lineLengthSquared(Points[1], Points[2]),
                    lineLengthSquared(Points[2], Points[0]) };
                double shortestSquaredLength = squaredLengths.Min();
                for (int i = 0; i < squaredLengths.Count; i++) {
                    squaredLengths[i] /= shortestSquaredLength;
                }
                return squaredLengths;
            }

            private List<double> normalizeBrightnesses(double minBrightness, double maxBrightness) {
                var brightnesses = Points.Select(p => p.NormalisedBrightness).ToList();
                double range = maxBrightness - minBrightness;
                for (int i = 0; i < brightnesses.Count; i++) {
                    brightnesses[i] = (brightnesses[i] - minBrightness) / range;
                }
                return brightnesses;
            }

            private List<Point2D> normalizePositions(System.Drawing.Size imageSize, double minBrightness, double maxBrightness) {
                var normPos = new List<Point2D>();
                foreach (var p in Points) {
                    normPos.Add(new Point2D(
                        p.X / imageSize.Width,
                        p.Y / imageSize.Height,
                        (minBrightness == maxBrightness) ? p.NormalisedBrightness : (p.NormalisedBrightness - minBrightness) / (maxBrightness - minBrightness)));
                }
                return normPos;
            }

            private double lineLengthSquared(Point2D p1, Point2D p2) {
                double x = p2.X - p1.X;
                double y = p2.Y - p1.Y;
                return x * x + y * y;
            }

            public List<double> NormalizedLengths { get => normalizedLengths; }
            public List<double> NormalizedBrightnesses { get => normalizedBrightnesses; }

            public double[] AsPositionMatrix() {
                return new double[] {
                    normalizedPoints[0].X, normalizedPoints[0].Y,
                    normalizedPoints[1].X, normalizedPoints[1].Y,
                    normalizedPoints[2].X, normalizedPoints[2].Y,
                };
            }

            public double[] AsShapeMatrix() {
                return new double[] {
                    normalizedLengths[0],
                    normalizedLengths[1],
                    normalizedLengths[2],
                };
            }

            public double[] AsBrightnessMatrix() {
                return new double[] {
                    NormalizedBrightnesses[0],
                    NormalizedBrightnesses[1],
                    NormalizedBrightnesses[2],
                };
            }

            public double[] AsShapeAndBrightnessMatrix() {
                return new double[] {
                    normalizedLengths[0],
                    normalizedLengths[1],
                    normalizedLengths[2],
                    NormalizedBrightnesses[0],
                    NormalizedBrightnesses[1],
                    NormalizedBrightnesses[2],
                };
            }

            public bool Matched { get => matched; }
            public bool IsReference { get => isReference; }
            public int ReferenceID { get => referenceID; }

            public override string ToString() {
                return $"StarTriangle: ({Points[0].X}, {P1.Y}), ({P2.X}, {P2.Y}), ({P3.X}, {P3.Y})";
            }

            public void MarkAsMatched(int referenceID, double matchScore) {
                matched = true;
                this.referenceID = referenceID;
                this.matchScore = matchScore;
            }

            public string MatchString {
                get { return $"{ReferenceID} ({matchScore:0.#########})"; }
            }
        }

        public static double AngleBetweenPoints(Point2D a, Point2D b, Point2D c) {
            double BAx = a.X - b.X;
            double BAy = a.Y - b.Y;
            double BCx = c.X - b.X;
            double BCy = c.Y - b.Y;

            double dot = BAx * BCx + BAy * BCy;
            double magBA = BAx * BAx + BAy * BAy;
            double magBC = BCx * BCx + BCy * BCy;

            if (magBA == 0 || magBC == 0)
                throw new ArgumentException("Points must not be identical.");

            // Calculate angle (in radians)
            double cosAngle = dot / Math.Sqrt(magBA * magBC);
            cosAngle = Math.Clamp(cosAngle, -1.0, 1.0);

            double angleRad = Math.Acos(cosAngle);
            return angleRad;
        }

        public static double AngleFromHorizontal(Point2D a, Point2D b) {
            return Math.Atan2(b.Y - a.Y, b.X - a.X);
        }

        public static List<StarTriangle> BuildStarTriangles(System.Drawing.Size imageSize, List<Point2D> point2Ds, int searchSquareSide, bool onePerPoint, bool isReference) {
            // first pass - build list of triangles in reference image
            var triangles = new List<StarTriangle>();
            var pointsUsed = new HashSet<Point2D>();
            int id = 0;
            var brightnesses = point2Ds.Select(p => p.NormalisedBrightness);
            var minBrightness = brightnesses.Min();
            var maxBrightness = brightnesses.Max();

            for (int i = 0; i < point2Ds.Count; i++) {
                if (pointsUsed.Contains(point2Ds[i]))
                    continue;
                var pt = point2Ds[i];
                var searchArea = new Rect2d(pt.X - searchSquareSide, pt.Y - searchSquareSide, searchSquareSide * 2, searchSquareSide * 2);
                var nearbyPoints = point2Ds
                    .Where(p => !pointsUsed.Contains(p) && searchArea.Contains(p.X, p.Y) && (p != pt))
                    .OrderByDescending(p => MathUtility.CalcDistance(p, pt))
                    .ToList();
                if (onePerPoint) {
                    if (nearbyPoints.Count > 2) {
                        double widestAngle = 0;
                        int thirdPoint = 1;
                        for (int j = 1; j < nearbyPoints.Count; j++) {
                            double rad = AngleBetweenPoints(pt, nearbyPoints[0], nearbyPoints[j]);
                            if (rad > widestAngle) {
                                thirdPoint = j;
                                widestAngle = rad;
                            }
                        }
                        triangles.Add(new StarTriangle(imageSize, minBrightness, maxBrightness, pt, nearbyPoints[0], nearbyPoints[thirdPoint], isReference, id++));
                        pointsUsed.Add(pt);
                        pointsUsed.Add(nearbyPoints[0]);
                        pointsUsed.Add(nearbyPoints[thirdPoint]);
                    }
                } else {
                    for (int j = 0; j < nearbyPoints.Count; j++) {
                        var p2 = nearbyPoints[j];
                        for (int k = j + 1; k < nearbyPoints.Count; k++) {
                            var p3 = nearbyPoints[k];
                            var triangle = new StarTriangle(imageSize, minBrightness, maxBrightness, pt, p2, p3, isReference, id++);
                            if (triangles.Count(tri => tri.SameTriangle(triangle)) == 0)    // avoid duplicates
                                triangles.Add(triangle);
                        }
                    }
                }
            }
            return triangles;
        }

        // Generate putative matches using triangles
        public static (List<Point2D> srcPoints, List<Point2D> dstPoints) GeneratePutativeMatchesUsingSimilarTriangles(
            List<StarTriangle> imageTriangles,
            List<StarTriangle> referenceTriangles,
            ApplicationStatus status,
            double minCosSim) {
            var srcPoints = new List<Point2D>();
            var dstPoints = new List<Point2D>();

            // For each triangle in the reference image, find closest match in this image

            for (int i = 0; i < referenceTriangles.Count; i++) {
                var referenceTriangle = referenceTriangles[i];
                double bestCosSimLoc = minCosSim;
                StarTriangle bestMatch = null;
                List<(StarTriangle, double)> matches = new();
                foreach (var imageTriangle in imageTriangles.Where(t => !t.Matched)) {
                    var cosSim = CosineSimilarity(referenceTriangle.AsShapeMatrix(), imageTriangle.AsShapeMatrix());
                    if (cosSim > minCosSim) {
                        matches.Add((imageTriangle, cosSim));
                    }
                }

                if (matches.Count > 1) {
                    // examine matches and pick best on brightness - these matches are already pretty close matches based on location (>0.99999)
                    double bestCosSimBri = 0;

                    foreach (var (tri, cs) in matches) {
                        var cosSim = CosineSimilarity(referenceTriangle.AsBrightnessMatrix(), tri.AsBrightnessMatrix());

                        if (cosSim > bestCosSimBri) {
                            bestMatch = tri;
                            bestCosSimBri = cosSim;
                        }
                    }
                    //Logger.Debug($"Multiple ({matches.Count}) matches for ref triangle {i} - selected triangle with briCosSim of {bestCosSimBri}");
                } else {
                    if (matches.Count == 1) {
                        var best = matches.First();
                        bestMatch = best.Item1;
                        bestCosSimLoc = best.Item2;
                    }
                }

                if (bestMatch != null) {
                    srcPoints.AddRange(new List<Point2D> { bestMatch.P1, bestMatch.P2, bestMatch.P3 });
                    dstPoints.AddRange(new List<Point2D> { referenceTriangle.P1, referenceTriangle.P2, referenceTriangle.P3 });
                    bestMatch.MarkAsMatched(referenceTriangle.ReferenceID, bestCosSimLoc);
                }
            }

            return (srcPoints, dstPoints);
        }

        private static double CosineSimilarity(double[] vecA, double[] vecB) {
            double dotProduct = 0;
            double magnitudeA = 0;
            double magnitudeB = 0;
            for (int i = 0; i < vecA.Length; i++) {
                dotProduct += vecA[i] * vecB[i];
                magnitudeA += vecA[i] * vecA[i];
                magnitudeB += vecB[i] * vecB[i];
            }
            if (magnitudeA == 0 || magnitudeB == 0) {
                return 0;
            }
            return dotProduct / (Math.Sqrt(magnitudeA * magnitudeB));
        }

        public static SimilarityTransform EstimateSimilarityTransform(
            List<Point2D> srcPoints,
            List<Point2D> dstPoints,
            ApplicationStatus status,
            IProgress<ApplicationStatus> progress,
            int maxIterations = 10000,
            double inlierThreshold = 9,    // 3.0^2
            int minInliers = 4) {
            if (srcPoints.Count != dstPoints.Count || srcPoints.Count < 2) {
                throw new ArgumentException("At least 2 corresponding points are required.");
            }

            List<int> bestInlierIndices = new List<int>();
            double bestInlierAveDistance = double.MaxValue;
            SimilarityTransform bestTransform = null;

            // build randomized list of pairs of points
            List<(int, int)> randIndices = new List<(int, int)>();
            for (int i = 0; i < srcPoints.Count; i++) {
                for (int j = i + 1; j < srcPoints.Count; j++) {
                    randIndices.Add((i, j));
                }
            }
            randIndices = randIndices.OrderBy(x => RNG.Next()).Take(maxIterations).ToList();

            foreach ((int idx1, int idx2) in randIndices) {
                var sampleSrc = new List<Point2D>() { srcPoints[idx1], srcPoints[idx2] };
                var sampleDst = new List<Point2D>() { dstPoints[idx1], dstPoints[idx2] };

                SimilarityTransform transform;
                // Estimate transformation from sample
                try {
                    transform = FitSimilarityTransform(sampleSrc, sampleDst);
                } catch (Exception e) {
                    Logger.Error($"Failed to build affine transform: {e.Message}");
                    continue;
                }

                // Count inliers
                List<int> inlierIndices = new List<int>();
                double inlierDistanceTotal = 0;
                for (int j = 0; j < srcPoints.Count; j++) {
                    var transformedPoint = transform.Transform(srcPoints[j]);
                    double distance = MathUtility.CalcDistance(transformedPoint, dstPoints[j]);
                    if (distance < inlierThreshold) {
                        inlierIndices.Add(j);
                        inlierDistanceTotal += distance;
                    }
                }

                double aveInlierDistance = inlierDistanceTotal / inlierIndices.Count;

                //Trace.WriteLine($"{idx1},{idx2}: {sampleSrc[0]}, {sampleSrc[1]} -> {sampleDst[0]}, {sampleDst[1]} = {transform}, {inlierIndices.Count*100/srcPoints.Count:0.####}%");

                // Update best model if more inliers
                if ((inlierIndices.Count > bestInlierIndices.Count) || ((inlierIndices.Count == bestInlierIndices.Count) && (aveInlierDistance < bestInlierAveDistance))) {
                    bestInlierIndices = inlierIndices;
                    bestInlierAveDistance = aveInlierDistance;
                }
            }

            // Refine transformation using all inliers
            if (bestInlierIndices.Count >= minInliers) {
                var inlierSrc = bestInlierIndices.Select(idx => srcPoints[idx]).ToList();
                var inlierDst = bestInlierIndices.Select(idx => dstPoints[idx]).ToList();
                bestTransform = FitSimilarityTransform(inlierSrc, inlierDst);
            } else {
                throw new InvalidOperationException("Not enough inliers to compute transformation.");
            }

            //Trace.WriteLine($"Best: {bestTransform} {bestInlierIndices.Count * 100 / srcPoints.Count:0.####}% aveDist:{bestInlierAveDistance:0.###} iterations:{randIndices.Count}");

            return bestTransform;
        }

        public static Matrix3x2 EstimateAffineTransform(
            List<Point2D> srcPoints,
            List<Point2D> dstPoints,
            ApplicationStatus status,
            IProgress<ApplicationStatus> progress,
            int maxIterations = 10000,
            double inlierThreshold = 9,    // 3.0^2
            int minInliers = 4) {
            if (srcPoints.Count != dstPoints.Count || srcPoints.Count < 2) {
                throw new ArgumentException("At least 2 corresponding points are required.");
            }

            List<int> bestInlierIndices = new List<int>();
            double bestInlierAveDistance = double.MaxValue;
            Matrix3x2 bestTransform = null;

            // build randomized list of pairs of points
            List<(int, int, int)> randIndices = new List<(int, int, int)>();
            for (int i = 0; i < srcPoints.Count; i++) {
                for (int j = i + 1; j < srcPoints.Count; j++) {
                    for (int k = j + 1; k < srcPoints.Count; k++) {
                        randIndices.Add((i, j, k));
                    }
                }
            }
            randIndices = randIndices.OrderBy(x => RNG.Next()).Take(maxIterations).ToList();

            foreach ((int idx1, int idx2, int idx3) in randIndices) {
                var sampleSrc = new List<Point2D>() { srcPoints[idx1], srcPoints[idx2], srcPoints[idx3] };
                var sampleDst = new List<Point2D>() { dstPoints[idx1], dstPoints[idx2], dstPoints[idx3] };

                Matrix3x2 transform;
                // Estimate transformation from sample
                try {
                    transform = FitAffineTransform(sampleSrc, sampleDst);
                } catch (Exception e) {
                    if (e.Message != "Singular matrix.")
                        Logger.Error($"Failed to build affine transform: {e.Message}");
                    continue;
                }

                // Count inliers
                List<int> inlierIndices = new List<int>();
                double inlierDistanceTotal = 0;
                for (int j = 0; j < srcPoints.Count; j++) {
                    var transformedPoint = transform.Transform(srcPoints[j]);
                    double distance = MathUtility.CalcDistance(transformedPoint, dstPoints[j]);
                    if (distance < inlierThreshold) {
                        inlierIndices.Add(j);
                        inlierDistanceTotal += distance;
                    }
                }

                double aveInlierDistance = inlierDistanceTotal / inlierIndices.Count;

                //Trace.WriteLine($"{idx1},{idx2}: {sampleSrc[0]}, {sampleSrc[1]} -> {sampleDst[0]}, {sampleDst[1]} = {transform}, {inlierIndices.Count*100/srcPoints.Count:0.####}%");

                // Update best model if more inliers
                if ((inlierIndices.Count > bestInlierIndices.Count) || ((inlierIndices.Count == bestInlierIndices.Count) && (aveInlierDistance < bestInlierAveDistance))) {
                    bestInlierIndices = inlierIndices;
                    bestInlierAveDistance = aveInlierDistance;
                }
            }

            // Refine transformation using all inliers
            if (bestInlierIndices.Count >= minInliers) {
                var inlierSrc = bestInlierIndices.Select(idx => srcPoints[idx]).ToList();
                var inlierDst = bestInlierIndices.Select(idx => dstPoints[idx]).ToList();
                bestTransform = FitAffineTransform(inlierSrc, inlierDst);
            } else {
                throw new InvalidOperationException("Not enough inliers to compute transformation.");
            }

            //Trace.WriteLine($"Best: {bestTransform} {bestInlierIndices.Count * 100 / srcPoints.Count:0.####}% aveDist:{bestInlierAveDistance:0.###} iterations:{randIndices.Count}");

            return bestTransform;
        }

        private static SimilarityTransform FitSimilarityTransform(List<Point2D> srcPoints, List<Point2D> dstPoints) {
            int n = srcPoints.Count;
            if (n < 2) return null;

            // Build design matrix A and vector b for least-squares: Ax = b
            var A = Matrix<double>.Build.Dense(2 * n, 4);
            var b = Vector<double>.Build.Dense(2 * n);

            for (int i = 0; i < n; i++) {
                double srcX = srcPoints[i].X;
                double srcY = srcPoints[i].Y;
                A[2 * i, 0] = srcX;
                A[2 * i, 1] = -srcY;
                A[2 * i, 2] = 1;
                A[2 * i, 3] = 0;
                A[2 * i + 1, 0] = srcY;
                A[2 * i + 1, 1] = srcX;
                A[2 * i + 1, 2] = 0;
                A[2 * i + 1, 3] = 1;

                b[2 * i] = dstPoints[i].X;
                b[2 * i + 1] = dstPoints[i].Y;
            }

            // Solve least-squares problem
            var x = A.Solve(b);
            if (x == null) return null;

            // Extract parameters: [s*cos(theta), s*sin(theta), tx, ty]
            double sCosTheta = x[0];
            double sSinTheta = x[1];
            double scale = Math.Sqrt(sCosTheta * sCosTheta + sSinTheta * sSinTheta);
            double rotation = Math.Atan2(sSinTheta, sCosTheta);
            double tx = x[2];
            double ty = x[3];

            return new SimilarityTransform(scale, rotation, tx, ty);
        }

        /// <summary>
        /// Computes an affine transform that maps sourcePoints -> destPoints.
        /// Requires at least 3 non-collinear points.
        /// Returns Matrix3x2 containing the affine transform.
        /// </summary>
        public static Matrix3x2 FitAffineTransform(
            IList<Point2D> sourcePoints,
            IList<Point2D> destPoints) {
            if (sourcePoints.Count != destPoints.Count)
                throw new ArgumentException("Point lists must have the same length.");

            int n = sourcePoints.Count;
            if (n < 3)
                throw new ArgumentException("At least 3 point pairs are required.");

            // We solve for:
            // x' = a*x + b*y + c
            // y' = d*x + e*y + f
            // Unknowns: [a b c d e f]^T  (6 values)

            // Build normal equations: A^T * A * params = A^T * b

            double[,] ATA = new double[6, 6];
            double[] ATb = new double[6];

            for (int i = 0; i < n; i++) {
                double x = sourcePoints[i].X;
                double y = sourcePoints[i].Y;
                double xp = destPoints[i].X;
                double yp = destPoints[i].Y;

                double[] rowX = { x, y, 1, 0, 0, 0 }; // coefficients for x'
                double[] rowY = { 0, 0, 0, x, y, 1 }; // coefficients for y'

                // Accumulate for xp (x')
                AccumulateATA(ATA, rowX);
                AccumulateATb(ATb, rowX, xp);

                // Accumulate for yp (y')
                AccumulateATA(ATA, rowY);
                AccumulateATb(ATb, rowY, yp);
            }

            // Solve the 6x6 system
            double[] solution = SolveLinearSystem6x6(ATA, ATb);

            return new Matrix3x2(
                (double)solution[0], (double)solution[1],
                (double)solution[3], (double)solution[4],
                (double)solution[2], (double)solution[5]);
        }

        private static void AccumulateATA(double[,] ATA, double[] row) {
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 6; j++)
                    ATA[i, j] += row[i] * row[j];
        }

        private static void AccumulateATb(double[] ATb, double[] row, double value) {
            for (int i = 0; i < 6; i++)
                ATb[i] += row[i] * value;
        }

        /// <summary> Naive Gaussian elimination for a 6×6 system. </summary>
        private static double[] SolveLinearSystem6x6(double[,] A, double[] b) {
            int n = 6;
            double[,] M = new double[n, n + 1];

            // Build augmented matrix
            for (int i = 0; i < n; i++) {
                for (int j = 0; j < n; j++)
                    M[i, j] = A[i, j];
                M[i, n] = b[i];
            }

            // Gaussian elimination
            for (int i = 0; i < n; i++) {
                // Pivot
                double max = Math.Abs(M[i, i]);
                int pivot = i;
                for (int r = i + 1; r < n; r++) {
                    if (Math.Abs(M[r, i]) > max) {
                        max = Math.Abs(M[r, i]);
                        pivot = r;
                    }
                }
                if (pivot != i)
                    SwapRows(M, i, pivot);

                // Normalize pivot row
                double div = M[i, i];
                if (Math.Abs(div) < 1e-12)
                    throw new Exception("Singular matrix.");

                for (int j = i; j <= n; j++)
                    M[i, j] /= div;

                // Eliminate below
                for (int r = i + 1; r < n; r++) {
                    double factor = M[r, i];
                    for (int j = i; j <= n; j++)
                        M[r, j] -= factor * M[i, j];
                }
            }

            // Back-substitution
            double[] x = new double[n];
            for (int i = n - 1; i >= 0; i--) {
                x[i] = M[i, n];
                for (int j = i + 1; j < n; j++)
                    x[i] -= M[i, j] * x[j];
            }

            return x;
        }

        private static void SwapRows(double[,] M, int r1, int r2) {
            int cols = M.GetLength(1);
            for (int i = 0; i < cols; i++) {
                double tmp = M[r1, i];
                M[r1, i] = M[r2, i];
                M[r2, i] = tmp;
            }
        }
    }

    public class Matrix3x2 {
        public double M11, M12;
        public double M21, M22;
        public double M31, M32;

        public Matrix3x2(
            double m11, double m12,
            double m21, double m22,
            double m31, double m32) {
            M11 = m11; M12 = m12;
            M21 = m21; M22 = m22;
            M31 = m31; M32 = m32;
        }

        // Identity Matrix
        public static Matrix3x2 Identity =>
            new Matrix3x2(1, 0, 0, 1, 0, 0);

        // Apply transform to a point
        public (double X, double Y) TransformPoint(double x, double y) {
            double tx = M11 * x + M12 * y + M31;
            double ty = M21 * x + M22 * y + M32;
            return (tx, ty);
        }

        public Point2D Transform(Point2D v) {
            return new Point2D(
                M11 * v.X + M12 * v.Y + M31,
                M21 * v.X + M22 * v.Y + M32
            );
        }

        // Matrix multiplication: result = a * b
        public static Matrix3x2 operator *(Matrix3x2 a, Matrix3x2 b) {
            return new Matrix3x2(
                a.M11 * b.M11 + a.M12 * b.M21,
                a.M11 * b.M12 + a.M12 * b.M22,

                a.M21 * b.M11 + a.M22 * b.M21,
                a.M21 * b.M12 + a.M22 * b.M22,

                a.M31 * b.M11 + a.M32 * b.M21 + b.M31,
                a.M31 * b.M12 + a.M32 * b.M22 + b.M32
            );
        }

        // Determinant of the linear part
        public double GetDeterminant() {
            return M11 * M22 - M12 * M21;
        }

        // Inverse transform
        public bool Invert(out Matrix3x2 inv) {
            double det = GetDeterminant();
            if (Math.Abs(det) < 1e-12f) {
                inv = default;
                return false;
            }

            double invDet = 1.0f / det;

            double i11 = M22 * invDet;
            double i12 = -M12 * invDet;
            double i21 = -M21 * invDet;
            double i22 = M11 * invDet;

            double i31 = -(i11 * M31 + i12 * M32);
            double i32 = -(i21 * M31 + i22 * M32);

            inv = new Matrix3x2(i11, i12, i21, i22, i31, i32);
            return true;
        }

        public override string ToString() {
            return $"[{M11}, {M12}, {M31}]  [{M21}, {M22}, {M32}]";
        }

        public string ToFullString() {
            var scaleX = Math.Sqrt(M11 * M11 + M21 * M21);
            var scaleY = Math.Sqrt(M12 * M12 + M22 * M22);
            var rotation = Math.Atan2(M21, M11) * 180 / Math.PI;
            var translationX = M31;
            var translationY = M32;
            return $"Scale(x,y):({scaleX:0.###},{scaleY:0.###}), rotation:{rotation:0.###}deg, translation(x,y):{translationX:0.###},{translationY:0.###}";
        }
    }
}