#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Linq;
using System.Threading;
using Rect = OpenCvSharp.Rect;
using PSFMoffatBeta = NINA.Joko.Plugins.HocusFocus.Interfaces.PSFMoffatBetaEnum;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class PSFModelSolution {
        public double A { get; set; }
        public double B { get; set; }
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double SigmaX { get; set; }
        public double SigmaY { get; set; }
        public double Theta { get; set; }
    }

    public abstract class PSFModelTypeBase {

        protected PSFModelTypeBase(double centroidBrightness, double starDetectionBackground, double pixelScale, Rect starBoundingBox) {
            this.CentroidBrightness = centroidBrightness;
            this.StarDetectionBackground = starDetectionBackground;
            this.PixelScale = pixelScale;
            this.StarBoundingBox = starBoundingBox;
        }

        public abstract StarDetectorPSFFitType PSFType { get; }
        public double StarDetectionBackground { get; private set; }
        public double CentroidBrightness { get; private set; }
        public double PixelScale { get; private set; }
        public Rect StarBoundingBox { get; private set; }

        public abstract double SigmaToFWHM(double sigma);

        public abstract double GoodnessOfFit(double A, double B, double x0, double y0, double sigmaX, double sigmaY, double theta);

        public abstract PSFModelSolution Solve(int maxIterations, double tolerance, CancellationToken ct);

        public abstract PSFModelSolution SolveIRLS(int maxIterationsIRLS, double toleranceIRLS, int maxIterationsLM, double toleranceLM, double noiseSigma, CancellationToken ct);
    }

    public abstract class PSFModelTypeAlglibBase : PSFModelTypeBase {
        /// <summary>
        /// Multiplier applied to noiseSigma to compute the Huber IRLS threshold δ = HuberThresholdMultiplier * noiseSigma.
        /// Residuals with |r| ≤ δ are treated as inliers (weight = 1); residuals with |r| > δ are down-weighted as δ/|r|.
        /// </summary>
        public const double HuberThresholdMultiplier = 1.5;

        protected readonly IAlglibAPI alglibAPI;

        protected PSFModelTypeAlglibBase(IAlglibAPI alglibAPI, double centroidBrightness, double starDetectionBackground, double pixelScale, Rect starBoundingBox, double[][] inputs, double[] outputs)
            : base(centroidBrightness, starDetectionBackground, pixelScale, starBoundingBox) {
            this.Inputs = inputs;
            this.Outputs = outputs;
            this.weights = new double[inputs.Length];
            for (int i = 0; i < this.weights.Length; ++i) {
                this.weights[i] = 1.0d;
            }
            this.alglibAPI = alglibAPI;
        }

        public abstract bool UseJacobian { get; }
        public double[][] Inputs { get; private set; }
        public double[] Outputs { get; private set; }

        protected readonly double[] weights;

        public abstract double Value(double[] parameters, double[] input);

        public abstract void Gradient(double[] parameters, double[] input, double[] result);

        public virtual void FitResiduals(double[] parameters, double[] fi, object obj) {
            // x contains the parameters
            // fi will store the residualized result for each observation
            for (int i = 0; i < this.Inputs.Length; ++i) {
                var input = this.Inputs[i];
                var observedValue = this.Outputs[i];
                var estimatedValue = Value(parameters, input);
                fi[i] = estimatedValue - observedValue;
            }
        }

        public virtual void FitResidualsWeighted(double[] parameters, double[] fi, object obj) {
            FitResiduals(parameters, fi, obj);
            for (int i = 0; i < this.Inputs.Length; ++i) {
                fi[i] *= Math.Sqrt(this.weights[i]);
            }
        }

        public virtual void FitResidualsJacobian(double[] parameters, double[] fi, double[,] jac, object obj) {
            // Every observation is a function that returns a value.
            // The optimizer minimizes the sum of the squares of the values, so we use a residual for the value function
            // The Jacobian matrix has 1 row per function/observation, and each column is a partial derivative with respect to each parameter
            // We use 3 parameters: sigmaX, sigmaY, and theta

            FitResiduals(parameters, fi, obj);
            var singleGradient = new double[parameters.Length];
            for (int i = 0; i < this.Inputs.Length; ++i) {
                Gradient(parameters, this.Inputs[i], singleGradient);
                for (int j = 0; j < parameters.Length; ++j) {
                    jac[i, j] = singleGradient[j];
                }
            }
        }

        public virtual void FitResidualsJacobianWeighted(double[] parameters, double[] fi, double[,] jac, object obj) {
            FitResidualsJacobian(parameters, fi, jac, obj);
            for (int i = 0; i < this.Inputs.Length; ++i) {
                var weight = Math.Sqrt(this.weights[i]);
                fi[i] *= weight;
                for (int j = 0; j < parameters.Length; ++j) {
                    jac[i, j] *= weight;
                }
            }
        }

        /// <summary>
        /// Computes initial sigma guesses from raw image second moments.
        /// σx² = Σ (pixel - background) · dx² / Σ (pixel - background)
        /// σy² = Σ (pixel - background) · dy² / Σ (pixel - background)
        /// where the sum is over pixels above the background and (dx, dy) are already
        /// centroid-relative offsets stored in Inputs.
        /// Falls back to box/3 for degenerate cases, and clamps to [0.5, max(w,h)].
        /// </summary>
        protected (double sigmaX, double sigmaY) ComputeSecondMomentSigmas() {
            var background = this.StarDetectionBackground;
            var maxSigma = Math.Max(this.StarBoundingBox.Width, this.StarBoundingBox.Height);
            var fallbackX = this.StarBoundingBox.Width / 3.0;
            var fallbackY = this.StarBoundingBox.Height / 3.0;

            double sumWeight = 0.0;
            double sumX2 = 0.0;
            double sumY2 = 0.0;

            for (int i = 0; i < this.Inputs.Length; ++i) {
                var weight = this.Outputs[i] - background;
                if (weight <= 0.0) continue;
                var dx = this.Inputs[i][0];
                var dy = this.Inputs[i][1];
                sumWeight += weight;
                sumX2 += weight * dx * dx;
                sumY2 += weight * dy * dy;
            }

            double sigX, sigY;
            if (sumWeight <= 0.0) {
                sigX = fallbackX;
                sigY = fallbackY;
            } else {
                var varX = sumX2 / sumWeight;
                var varY = sumY2 / sumWeight;
                if (varX <= 0.0 || varY <= 0.0) {
                    sigX = fallbackX;
                    sigY = fallbackY;
                } else {
                    sigX = Math.Sqrt(varX);
                    sigY = Math.Sqrt(varY);
                }
            }

            sigX = Math.Max(0.5, Math.Min(sigX, maxSigma));
            sigY = Math.Max(0.5, Math.Min(sigY, maxSigma));
            return (sigX, sigY);
        }

        /// <summary>
        /// Computes residual sum of squares (RSS) and total sum of squares (TSS) for the given parameter set.
        /// R² = 1 - rss/tss.  Both values are returned so the caller can also derive reduced χ².
        /// </summary>
        public (double rss, double tss) ComputeRSS(double A, double B, double x0, double y0, double sigmaX, double sigmaY, double theta) {
            var parameters = new double[] { A, B, x0, y0, sigmaX, sigmaY, theta };

            var rss = 0.0d;
            var tss = 0.0d;
            int pixelCount = this.Inputs.Length;
            var yBar = this.Outputs.Average();
            for (int i = 0; i < pixelCount; ++i) {
                var input = this.Inputs[i];
                var observedValue = this.Outputs[i];
                var estimatedValue = Value(parameters, input);
                var residual = estimatedValue - observedValue;
                var observedDispersion = observedValue - yBar;
                tss += observedDispersion * observedDispersion;
                rss += residual * residual;
            }
            return (rss, tss);
        }

        public override double GoodnessOfFit(double A, double B, double x0, double y0, double sigmaX, double sigmaY, double theta) {
            var (rss, tss) = ComputeRSS(A, B, x0, y0, sigmaX, sigmaY, theta);
            return 1 - rss / tss;
        }

        public override PSFModelSolution SolveIRLS(
            int maxIterationsIRLS,
            double toleranceIRLS,
            int maxIterationsLM,
            double toleranceLM,
            double noiseSigma,
            CancellationToken ct) {
            alglib.minlmstate state = null;
            alglib.minlmreport rep = null;
            var sigmaUpperBound = Math.Sqrt(this.StarBoundingBox.Width * this.StarBoundingBox.Width + this.StarBoundingBox.Height * this.StarBoundingBox.Height) / 2;
            var (initSigmaX, initSigmaY) = ComputeSecondMomentSigmas();
            // Canonical form: sigmaX (parameter index 4) is the major axis (larger σ).
            // Enforce sigmaX ≥ sigmaY in the initial seed so LM starts in the correct basin.
            // For near-circular stars (initSigmaX ≈ initSigmaY), a tiny asymmetric nudge prevents
            // the optimizer from sitting on the symmetry boundary and flipping to the sigmaY > sigmaX
            // solution on different frames, which would cause a ±π/2 discontinuity in the reported θ.
            if (initSigmaX < initSigmaY) {
                (initSigmaX, initSigmaY) = (initSigmaY, initSigmaX);
            }
            // Nudge sigmaX slightly above sigmaY so the seed is unambiguously in the sigmaX > sigmaY
            // basin; 0.1% is negligible for the optimizer but prevents exact ties.
            initSigmaX *= 1.001;
            var initialGuess = new double[] { Math.Max(0.0d, this.CentroidBrightness - this.StarDetectionBackground), this.StarDetectionBackground, 0.0, 0.0, initSigmaX, initSigmaY, 0.0d };
            var dxLimit = this.StarBoundingBox.Width / 2.0d;
            var dyLimit = this.StarBoundingBox.Height / 2.0d;
            var lowerBounds = new double[] { 0.0d, 0.0d, -dxLimit, -dyLimit, 0, 0, -Math.PI / 2.0d };
            var upperBounds = new double[] { 2.0d, 1.0d, dxLimit, dyLimit, sigmaUpperBound, sigmaUpperBound, Math.PI / 2.0d };
            var scale = new double[] { 0.01, 0.01, 0.1, 0.1, 1, 1, 1 };
            try {
                var solution = new double[6];

                maxIterationsLM = maxIterationsLM > 0 ? Math.Min(maxIterationsLM, 20) : 20;
                var iterations = 0;
                var sumOfResidualsDelta = double.PositiveInfinity;
                var prevSumOfResiduals = double.PositiveInfinity;
                while (sumOfResidualsDelta > toleranceIRLS && iterations++ < maxIterationsLM) {
                    if (this.UseJacobian) {
                        this.alglibAPI.minlmcreatevj(this.Inputs.Length, initialGuess, out state);
                        this.alglibAPI.minlmsetacctype(state, 1);
                    } else {
                        const double deltaForNumericIntegration = 1E-4;
                        this.alglibAPI.minlmcreatev(this.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);
                    }
                    this.alglibAPI.minlmsetbc(state, lowerBounds, upperBounds);

                    // Set the termination conditions
                    this.alglibAPI.minlmsetcond(state, toleranceLM, maxIterationsLM);

                    // Set all variables to the same scale, except for x0, y0. This feature is useful if the magnitude if some variables is dramatically different than others
                    this.alglibAPI.minlmsetscale(state, scale);

                    // TODO: Remove optguard
                    // alglib.minlmoptguardgradient(state, 1E-4);

                    // Perform the optimization
                    this.alglibAPI.minlmoptimize(state, this.FitResidualsWeighted, this.FitResidualsJacobianWeighted, null, null);
                    ct.ThrowIfCancellationRequested();

                    this.alglibAPI.minlmresults(state, out solution, out rep);
                    if (rep.terminationtype < 0) {
                        string reason;
                        if (rep.terminationtype == -8) {
                            reason = "optimizer detected NAN/INF values either in the function itself, or in its Jacobian";
                        } else if (rep.terminationtype == -3) {
                            reason = "constraints are inconsistent";
                        } else {
                            reason = "unknown";
                        }
                        throw new Exception($"PSF modeling failed with type {rep.terminationtype} and reason: {reason}");
                    }

                    var sumOfResiduals = 0.0d;
                    var huberDelta = HuberThresholdMultiplier * noiseSigma;
                    for (int i = 0; i < this.weights.Length; ++i) {
                        var observedValue = this.Outputs[i];
                        var estimatedValue = Value(solution, this.Inputs[i]);
                        var absResidual = Math.Abs(estimatedValue - observedValue);
                        sumOfResiduals += absResidual;
                        // Huber IRLS weights: inliers (|r| ≤ δ) keep weight 1; outliers (|r| > δ) get weight δ/|r|
                        var newWeight = absResidual <= huberDelta ? 1.0 : huberDelta / absResidual;
                        this.weights[i] = newWeight;
                    }

                    sumOfResidualsDelta = Math.Abs(sumOfResiduals - prevSumOfResiduals);
                    prevSumOfResiduals = sumOfResiduals;
                    initialGuess = solution;
                }

                /*
                alglib.optguardreport ogrep;
                this.alglibAPI.minlmoptguardresults(state, out ogrep);
                if (ogrep.badgradsuspected) {
                    var differences = new double[ogrep.badgraduser.GetLength(0), ogrep.badgraduser.GetLength(1)];
                    for (int i = 0; i < ogrep.badgraduser.GetLength(0); ++i) {
                        for (int j = 0; j < ogrep.badgraduser.GetLength(1); ++j) {
                            differences[i, j] = ogrep.badgradnum[i, j] - ogrep.badgraduser[i, j];
                        }
                    }

                    Console.WriteLine();
                }
                */

                return new PSFModelSolution() {
                    A = solution[0],
                    B = solution[1],
                    X0 = solution[2],
                    Y0 = solution[3],
                    SigmaX = solution[4],
                    SigmaY = solution[5],
                    Theta = solution[6]
                };
            } finally {
                if (state != null) {
                    this.alglibAPI.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    this.alglibAPI.deallocateimmediately(ref rep);
                }
            }
        }

        public override PSFModelSolution Solve(int maxIterations, double tolerance, CancellationToken ct) {
            alglib.minlmstate state = null;
            alglib.minlmreport rep = null;
            try {
                var sigmaUpperBound = Math.Sqrt(this.StarBoundingBox.Width * this.StarBoundingBox.Width + this.StarBoundingBox.Height * this.StarBoundingBox.Height) / 2;
                var centroidBrightnessAboveBackground = Math.Max(0.0d, this.CentroidBrightness - this.StarDetectionBackground);
                var (initSigmaX, initSigmaY) = ComputeSecondMomentSigmas();
                // Canonical form: sigmaX (parameter index 4) is the major axis (larger σ).
                // Enforce sigmaX ≥ sigmaY in the initial seed so LM starts in the correct basin.
                // For near-circular stars (initSigmaX ≈ initSigmaY), a tiny asymmetric nudge prevents
                // the optimizer from sitting on the symmetry boundary and flipping to the sigmaY > sigmaX
                // solution on different frames, which would cause a ±π/2 discontinuity in the reported θ.
                if (initSigmaX < initSigmaY) {
                    (initSigmaX, initSigmaY) = (initSigmaY, initSigmaX);
                }
                // Nudge sigmaX slightly above sigmaY so the seed is unambiguously in the sigmaX > sigmaY
                // basin; 0.1% is negligible for the optimizer but prevents exact ties.
                initSigmaX *= 1.001;
                var initialGuess = new double[] { centroidBrightnessAboveBackground, this.StarDetectionBackground, 0.0, 0.0, initSigmaX, initSigmaY, 0.0d };
                var dxLimit = this.StarBoundingBox.Width / 2.0d;
                var dyLimit = this.StarBoundingBox.Height / 2.0d;
                var lowerBounds = new double[] { 0.0d, 0.0d, -dxLimit, -dyLimit, 0, 0, -Math.PI / 2.0d };
                var upperBounds = new double[] { 2.0d, 1.0d, dxLimit, dyLimit, sigmaUpperBound, sigmaUpperBound, Math.PI / 2.0d };
                var scale = new double[] { 0.01, 0.01, 0.1, 0.1, 1, 1, 1 };
                var solution = new double[6];

                if (this.UseJacobian) {
                    this.alglibAPI.minlmcreatevj(this.Inputs.Length, initialGuess, out state);
                    this.alglibAPI.minlmsetacctype(state, 1);
                } else {
                    const double deltaForNumericIntegration = 1E-4;
                    this.alglibAPI.minlmcreatev(this.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);
                }
                this.alglibAPI.minlmsetbc(state, lowerBounds, upperBounds);

                // Set the termination conditions
                this.alglibAPI.minlmsetcond(state, tolerance, maxIterations);

                // Set all variables to the same scale, except for x0, y0. This feature is useful if the magnitude if some variables is dramatically different than others
                this.alglibAPI.minlmsetscale(state, scale);

                // TODO: Remove optguard
                // alglib.minlmoptguardgradient(state, 1E-4);

                // Perform the optimization
                this.alglibAPI.minlmoptimize(state, this.FitResiduals, this.FitResidualsJacobian, (a, f, o) => ct.ThrowIfCancellationRequested(), null);
                ct.ThrowIfCancellationRequested();

                this.alglibAPI.minlmresults(state, out solution, out rep);
                if (rep.terminationtype < 0) {
                    string reason;
                    if (rep.terminationtype == -8) {
                        reason = "optimizer detected NAN/INF values either in the function itself, or in its Jacobian";
                    } else if (rep.terminationtype == -3) {
                        reason = "constraints are inconsistent";
                    } else {
                        reason = "unknown";
                    }
                    throw new Exception($"PSF modeling failed with type {rep.terminationtype} and reason: {reason}");
                }

                /*
                alglib.optguardreport ogrep;
                this.alglibAPI.minlmoptguardresults(state, out ogrep);
                if (ogrep.badgradsuspected) {
                    var differences = new double[ogrep.badgraduser.GetLength(0), ogrep.badgraduser.GetLength(1)];
                    for (int i = 0; i < ogrep.badgraduser.GetLength(0); ++i) {
                        for (int j = 0; j < ogrep.badgraduser.GetLength(1); ++j) {
                            differences[i, j] = ogrep.badgradnum[i, j] - ogrep.badgraduser[i, j];
                        }
                    }

                    Console.WriteLine();
                }
                */

                return new PSFModelSolution() {
                    A = solution[0],
                    B = solution[1],
                    X0 = solution[2],
                    Y0 = solution[3],
                    SigmaX = solution[4],
                    SigmaY = solution[5],
                    Theta = solution[6]
                };
            } finally {
                if (state != null) {
                    this.alglibAPI.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    this.alglibAPI.deallocateimmediately(ref rep);
                }
            }
        }
    }

    public class PSFModeler {

        /// <summary>
        /// Minimum number of unsaturated pixels required to attempt a PSF fit.
        /// If fewer than this many pixels survive the saturation mask, <see cref="Create"/> returns null.
        /// </summary>
        public const int MinUnsaturatedPixels = 10;

        public static PSFModelTypeBase Create(
            StarDetectorPSFFitType fitType,
            int psfResolution,
            Star detectedStar,
            Mat srcImage,
            double pixelScale,
            IAlglibAPI alglibAPI,
            double saturationThreshold = double.MaxValue,
            bool pixelIntegration = false,
            PSFMoffatBeta moffatBeta = PSFMoffatBeta.Fixed_4_0) {
            var background = detectedStar.Background;
            var nominalBoundingBoxWidth = Math.Sqrt(detectedStar.StarBoundingBox.Width * detectedStar.StarBoundingBox.Height);
            var samplingSize = nominalBoundingBoxWidth / psfResolution;
            var startX = detectedStar.Center.X - samplingSize * Math.Floor((detectedStar.Center.X - detectedStar.StarBoundingBox.Left) / samplingSize);
            var startY = detectedStar.Center.Y - samplingSize * Math.Floor((detectedStar.Center.Y - detectedStar.StarBoundingBox.Top) / samplingSize);
            var endX = detectedStar.StarBoundingBox.Right;
            var endY = detectedStar.StarBoundingBox.Bottom;
            var widthPixels = (int)Math.Floor((endX - startX - 1E-12) / samplingSize) + 1;
            var heightPixels = (int)Math.Floor((endY - startY - 1E-12) / samplingSize) + 1;
            var numPixels = widthPixels * heightPixels;
            var centroidBrightness = CvImageUtility.BilinearSamplePixelValue(srcImage, y: detectedStar.Center.Y, x: detectedStar.Center.X);

            // Collect only unsaturated pixels (raw value < saturationThreshold)
            var inputsList = new System.Collections.Generic.List<double[]>(numPixels);
            var outputsList = new System.Collections.Generic.List<double>(numPixels);
            for (var y = startY; y < endY; y += samplingSize) {
                for (var x = startX; x < endX; x += samplingSize) {
                    var value = CvImageUtility.BilinearSamplePixelValue(srcImage, y: y, x: x);
                    // Skip saturated pixels — they don't carry valid profile information
                    if (value >= saturationThreshold) {
                        continue;
                    }
                    var dx = x - detectedStar.Center.X;
                    var dy = y - detectedStar.Center.Y;
                    inputsList.Add(new double[2] { dx, dy });
                    outputsList.Add(value);
                }
            }

            // Require a minimum number of unsaturated pixels to attempt a reliable fit
            if (inputsList.Count < MinUnsaturatedPixels) {
                return null;
            }

            var inputs = inputsList.ToArray();
            var outputs = outputsList.ToArray();

            if (fitType == StarDetectorPSFFitType.Gaussian) {
                return new GaussianPSFAlglibType(alglibAPI: alglibAPI, inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starDetectionBackground: background, starBoundingBox: detectedStar.StarBoundingBox, pixelScale: pixelScale, pixelIntegration: pixelIntegration);
            } else if (fitType == StarDetectorPSFFitType.Moffat_40) {
                if (moffatBeta == PSFMoffatBeta.Fittable) {
                    return new FittableMoffatPSFAlglibType(alglibAPI: alglibAPI, inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starDetectionBackground: background, starBoundingBox: detectedStar.StarBoundingBox, pixelScale: pixelScale);
                }
                var fixedBeta = moffatBeta switch {
                    PSFMoffatBeta.Fixed_1_5 => 1.5,
                    PSFMoffatBeta.Fixed_2_5 => 2.5,
                    PSFMoffatBeta.Fixed_4_0 => 4.0,
                    _ => 4.0
                };
                return new MoffatPSFAlglibType(alglibAPI: alglibAPI, beta: fixedBeta, inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starDetectionBackground: background, starBoundingBox: detectedStar.StarBoundingBox, pixelScale: pixelScale, pixelIntegration: pixelIntegration);
            } else {
                throw new ArgumentException($"Unknown PSF fit type {fitType}");
            }
        }

        public static PSFModel Solve(
            PSFModelTypeBase modelType,
            double noiseSigma,
            bool useAbsoluteResiduals,
            int maxIterations = 0,
            double tolerance = 1E-8,
            CancellationToken ct = default) {
            PSFModelSolution modelSolution;
            if (useAbsoluteResiduals) {
                modelSolution = modelType.SolveIRLS(
                    maxIterationsIRLS: 10,
                    toleranceIRLS: 1E-6,
                    maxIterationsLM: maxIterations,
                    toleranceLM: tolerance,
                    noiseSigma: noiseSigma,
                    ct: ct);
            } else {
                modelSolution = modelType.Solve(
                    maxIterations: maxIterations,
                    tolerance: tolerance,
                    ct: ct);
            }

            double sigX = modelSolution.SigmaX;
            double sigY = modelSolution.SigmaY;
            if (double.IsNaN(sigX) || double.IsNaN(sigY)) {
                return null;
            }

            var PI_2 = Math.PI / 2;
            double theta = Astrometry.AstroUtil.EuclidianModulus(modelSolution.Theta, Math.PI);
            if (theta > PI_2) {
                theta = theta - Math.PI;
            }

            // theta is a negative angle, solved to rotate the star back to the X-Y axes
            theta = -theta;

            // Canonical form: sigX is always the major axis (sigX ≥ sigY).
            // When the optimizer returns sigY > sigX (which can happen for near-circular stars due to
            // numerical noise, despite seeding with sigX ≥ sigY), we unconditionally swap and rotate
            // theta by ±π/2 so that θ always describes the orientation of the major axis.
            // Seeding with sigX ≥ sigY (in Solve/SolveIRLS) keeps the optimizer in the correct half
            // of solution space most of the time, so this swap fires only when genuinely needed.
            if (sigY > sigX) {
                if (theta < 0) {
                    theta += PI_2;
                } else {
                    theta -= PI_2;
                }

                var temp = sigY;
                sigY = sigX;
                sigX = temp;
            }

            var fwhmX = modelType.SigmaToFWHM(sigX);
            var fwhmY = modelType.SigmaToFWHM(sigY);

            double rSquared;
            double reducedChiSquared = double.NaN;
            if (modelType is FittableMoffatPSFAlglibType fittableMoffat) {
                // FittableMoffatPSFAlglibType requires 8 parameters (including β); use its specialized RSS method.
                var (rss, tss) = fittableMoffat.ComputeRSS8(modelSolution.A, modelSolution.B, modelSolution.X0, modelSolution.Y0, sigX, sigY, theta, fittableMoffat.Beta);
                rSquared = 1 - rss / tss;
                var noiseSigmaSq = noiseSigma * noiseSigma;
                if (noiseSigmaSq > 0 && fittableMoffat.Inputs.Length > 0) {
                    reducedChiSquared = rss / (fittableMoffat.Inputs.Length * noiseSigmaSq);
                }
            } else if (modelType is PSFModelTypeAlglibBase alglibBase) {
                var (rss, tss) = alglibBase.ComputeRSS(modelSolution.A, modelSolution.B, modelSolution.X0, modelSolution.Y0, sigX, sigY, theta);
                rSquared = 1 - rss / tss;
                // Reduced chi-squared: rss / (nPixels * noiseSigma²).  Only meaningful when noiseSigma > 0.
                var noiseSigmaSq = noiseSigma * noiseSigma;
                if (noiseSigmaSq > 0 && alglibBase.Inputs.Length > 0) {
                    reducedChiSquared = rss / (alglibBase.Inputs.Length * noiseSigmaSq);
                }
            } else {
                rSquared = modelType.GoodnessOfFit(modelSolution.A, modelSolution.B, modelSolution.X0, modelSolution.Y0, sigX, sigY, theta);
            }

            return new PSFModel(psfType: modelType.PSFType,
                offsetX: modelSolution.X0, offsetY: modelSolution.Y0,
                peak: modelSolution.A, background: modelSolution.B,
                sigmaX: sigX, sigmaY: sigY,
                fwhmX: fwhmX, fwhmY: fwhmY,
                thetaRadians: theta,
                rSquared: rSquared,
                pixelScale: modelType.PixelScale,
                reducedChiSquared: reducedChiSquared);
        }
    }
}
