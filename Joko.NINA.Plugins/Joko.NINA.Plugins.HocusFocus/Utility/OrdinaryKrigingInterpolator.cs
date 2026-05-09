#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public readonly struct KrigingSample {

        public KrigingSample(double x, double y, double value) {
            X = x;
            Y = y;
            Value = value;
        }

        public double X { get; }
        public double Y { get; }
        public double Value { get; }
    }

    public sealed class OrdinaryKrigingInterpolator {
        private const double ExactPointTolerance = 1e-10;
        private readonly LU<double> lu;

        private OrdinaryKrigingInterpolator(
            IReadOnlyList<KrigingSample> samples,
            double alpha,
            double beta,
            bool constantField,
            double constantValue,
            LU<double> lu) {
            Samples = samples;
            Alpha = alpha;
            Beta = beta;
            ConstantField = constantField;
            ConstantValue = constantValue;
            this.lu = lu;
        }

        public IReadOnlyList<KrigingSample> Samples { get; }
        public double Alpha { get; }
        public double Beta { get; }
        public bool ConstantField { get; }
        public double ConstantValue { get; }

        public static OrdinaryKrigingInterpolator Create(IEnumerable<KrigingSample> samples, double duplicateTolerance = ExactPointTolerance) {
            if (samples == null) {
                throw new ArgumentNullException(nameof(samples));
            }

            var distinctSamples = CoalesceSamples(samples, duplicateTolerance);
            if (distinctSamples.Count == 0) {
                throw new ArgumentException("At least one finite sample is required", nameof(samples));
            }

            var constantValue = distinctSamples[0].Value;
            var constantField = distinctSamples.All(s => Math.Abs(s.Value - constantValue) <= 1e-12);
            if (constantField || distinctSamples.Count == 1) {
                return new OrdinaryKrigingInterpolator(distinctSamples, alpha: 1.0, beta: 1.0, constantField: true, constantValue: constantValue, lu: null);
            }

            var (alpha, beta) = FitPowerLawVariogram(distinctSamples);
            var matrix = Matrix<double>.Build.Dense(distinctSamples.Count + 1, distinctSamples.Count + 1);
            for (int row = 0; row < distinctSamples.Count; ++row) {
                for (int col = 0; col < distinctSamples.Count; ++col) {
                    matrix[row, col] = Variogram(Distance(distinctSamples[row], distinctSamples[col]), alpha, beta);
                }
                matrix[row, distinctSamples.Count] = 1.0;
                matrix[distinctSamples.Count, row] = 1.0;
            }

            var lu = FactorizeKrigingMatrix(matrix, distinctSamples.Count, nameof(samples));
            return new OrdinaryKrigingInterpolator(distinctSamples, alpha, beta, constantField: false, constantValue: double.NaN, lu);
        }

        public double Interpolate(double x, double y) {
            if (ConstantField) {
                return ConstantValue;
            }

            for (int i = 0; i < Samples.Count; ++i) {
                if (Distance(Samples[i].X, Samples[i].Y, x, y) <= ExactPointTolerance) {
                    return Samples[i].Value;
                }
            }

            var rhs = Vector<double>.Build.Dense(Samples.Count + 1);
            for (int i = 0; i < Samples.Count; ++i) {
                rhs[i] = Variogram(Distance(Samples[i].X, Samples[i].Y, x, y), Alpha, Beta);
            }
            rhs[Samples.Count] = 1.0;

            var solution = lu.Solve(rhs);
            var value = 0.0d;
            for (int i = 0; i < Samples.Count; ++i) {
                value += solution[i] * Samples[i].Value;
            }
            return value;
        }

        public double[,] InterpolateGrid(int width, int height, double xMin, double xMax, double yMin, double yMax) {
            if (width <= 0) {
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
            }
            if (height <= 0) {
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive");
            }

            var grid = new double[height, width];
            var dx = width == 1 ? 0.0d : (xMax - xMin) / (width - 1);
            var dy = height == 1 ? 0.0d : (yMax - yMin) / (height - 1);
            for (int row = 0; row < height; ++row) {
                var y = yMin + row * dy;
                for (int col = 0; col < width; ++col) {
                    var x = xMin + col * dx;
                    grid[row, col] = Interpolate(x, y);
                }
            }
            return grid;
        }

        private static List<KrigingSample> CoalesceSamples(IEnumerable<KrigingSample> samples, double duplicateTolerance) {
            var groups = new List<List<KrigingSample>>();
            foreach (var sample in samples) {
                if (!double.IsFinite(sample.X) || !double.IsFinite(sample.Y) || !double.IsFinite(sample.Value)) {
                    continue;
                }

                var group = groups.FirstOrDefault(g => Distance(g[0].X, g[0].Y, sample.X, sample.Y) <= duplicateTolerance);
                if (group == null) {
                    groups.Add(new List<KrigingSample>() { sample });
                } else {
                    group.Add(sample);
                }
            }

            return groups.Select(g => new KrigingSample(
                x: g.Average(s => s.X),
                y: g.Average(s => s.Y),
                value: g.Average(s => s.Value))).ToList();
        }

        private static LU<double> FactorizeKrigingMatrix(Matrix<double> matrix, int sampleCount, string paramName) {
            var lu = matrix.LU();
            if (CanSolve(lu, sampleCount)) {
                return lu;
            }

            for (int i = 0; i < sampleCount; ++i) {
                matrix[i, i] = 1e-12;
            }
            lu = matrix.LU();
            if (CanSolve(lu, sampleCount)) {
                return lu;
            }

            throw new ArgumentException("Kriging system is singular for the supplied samples", paramName);
        }

        private static bool CanSolve(LU<double> lu, int sampleCount) {
            try {
                var rhs = Vector<double>.Build.Dense(sampleCount + 1);
                rhs[sampleCount] = 1.0d;
                var solution = lu.Solve(rhs);
                return solution.All(double.IsFinite);
            } catch (ArgumentException) {
                return false;
            } catch (InvalidOperationException) {
                return false;
            }
        }

        private static (double alpha, double beta) FitPowerLawVariogram(IReadOnlyList<KrigingSample> samples) {
            var pairs = new List<(double logDistance, double logSemiVariance)>();
            for (int i = 0; i < samples.Count; ++i) {
                for (int j = i + 1; j < samples.Count; ++j) {
                    var h = Distance(samples[i], samples[j]);
                    var diff = samples[i].Value - samples[j].Value;
                    var semiVariance = 0.5d * diff * diff;
                    if (h > ExactPointTolerance && semiVariance > 0.0d) {
                        pairs.Add((Math.Log(h), Math.Log(semiVariance)));
                    }
                }
            }

            if (pairs.Count < 2) {
                return (1.0d, 1.0d);
            }

            var meanLogDistance = pairs.Average(p => p.logDistance);
            var meanLogSemiVariance = pairs.Average(p => p.logSemiVariance);
            var numerator = 0.0d;
            var denominator = 0.0d;
            foreach (var pair in pairs) {
                var distanceDelta = pair.logDistance - meanLogDistance;
                numerator += distanceDelta * (pair.logSemiVariance - meanLogSemiVariance);
                denominator += distanceDelta * distanceDelta;
            }

            var beta = denominator <= 0.0d ? 1.0d : numerator / denominator;
            beta = Math.Clamp(beta, 0.1d, 2.0d);
            var alpha = Math.Exp(meanLogSemiVariance - beta * meanLogDistance);
            if (!double.IsFinite(alpha) || alpha <= 0.0d) {
                alpha = 1.0d;
            }
            return (alpha, beta);
        }

        private static double Variogram(double distance, double alpha, double beta) {
            if (distance <= ExactPointTolerance) {
                return 0.0d;
            }
            return alpha * Math.Pow(distance, beta);
        }

        private static double Distance(KrigingSample left, KrigingSample right) {
            return Distance(left.X, left.Y, right.X, right.Y);
        }

        private static double Distance(double x1, double y1, double x2, double y2) {
            var dx = x2 - x1;
            var dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
