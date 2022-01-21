#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Utility;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public abstract class PSFModelTypeBase {

        protected PSFModelTypeBase(double centroidBrightness, double pixelScale, Rect starBoundingBox, double[][] inputs, double[] outputs) {
            this.CentroidBrightness = centroidBrightness;
            this.PixelScale = pixelScale;
            this.Inputs = inputs;
            this.Outputs = outputs;
            this.StarBoundingBox = starBoundingBox;
        }

        public abstract bool UseJacobian { get; }

        public abstract double Value(double[] parameters, double[] input);

        public abstract void Gradient(double[] parameters, double[] input, double[] result);

        public double CentroidBrightness { get; private set; }
        public double PixelScale { get; private set; }
        public Rect StarBoundingBox { get; private set; }
        public double[][] Inputs { get; private set; }
        public double[] Outputs { get; private set; }

        public virtual void FitResiduals(double[] parameters, double[] fi, object obj) {
            // x contains the 3 parameters
            // fi will store the result - 1 for each observation
            int pixelCount = this.Inputs.Length;
            for (int i = 0; i < pixelCount; ++i) {
                var input = this.Inputs[i];
                var observedValue = this.Outputs[i];
                var estimatedValue = Value(parameters, input);
                fi[i] = estimatedValue - observedValue;
            }
        }

        public virtual void FitResidualsJacobian(double[] parameters, double[] fi, double[,] jac, object obj) {
            // Every observation is a function that returns a value.
            // The optimizer minimizes the sum of the squares of the values, so we use a residual for the value function
            // The Jacobian matrix has 1 row per function/observation, and each column is a partial derivative with respect to each parameter
            // We use 3 parameters: sigmaX, sigmaY, and theta

            FitResiduals(parameters, fi, obj);
            int pixelCount = this.Inputs.Length;
            var singleGradient = new double[parameters.Length];
            for (int i = 0; i < pixelCount; ++i) {
                Gradient(parameters, this.Inputs[i], singleGradient);
                for (int j = 0; j < parameters.Length; ++j) {
                    jac[i, j] = singleGradient[j];
                }
            }
        }

        public double GoodnessOfFit(double sigmaX, double sigmaY, double theta) {
            var parameters = new double[] { sigmaX, sigmaY, theta };
            var rss = 0.0d;
            var tss = 0.0d;
            var estimatedSum = 0.0d;

            int pixelCount = this.Inputs.Length;
            var yBar = this.Outputs.Average();
            for (int i = 0; i < pixelCount; ++i) {
                var input = this.Inputs[i];
                var observedValue = this.Outputs[i];
                var estimatedValue = Value(parameters, input);
                estimatedSum += estimatedValue;
                var residual = estimatedValue - observedValue;
                var observedDispersion = observedValue - yBar;
                tss += observedDispersion * observedDispersion;
                rss += residual * residual;
            }
            return 1 - rss / tss;
        }
    }

    public class PSFModeler {
        private static readonly double SIGMA_TO_FWHM_FACTOR = 2.0d * Math.Sqrt(2.0d * Math.Log(2.0d));

        public static PSFModelTypeBase Create(StarDetectorPSFFitType fitType, int psfResolution, Star detectedStar, Mat srcImage, double pixelScale) {
            var background = detectedStar.Background;
            var nominalBoundingBoxWidth = Math.Sqrt(detectedStar.StarBoundingBox.Width * detectedStar.StarBoundingBox.Height);
            var samplingSize = nominalBoundingBoxWidth / psfResolution;
            var startX = detectedStar.Center.X - samplingSize * Math.Floor((detectedStar.Center.X - detectedStar.StarBoundingBox.Left) / samplingSize);
            var startY = detectedStar.Center.Y - samplingSize * Math.Floor((detectedStar.Center.Y - detectedStar.StarBoundingBox.Top) / samplingSize);
            var endX = detectedStar.StarBoundingBox.Right;
            var endY = detectedStar.StarBoundingBox.Bottom;
            var widthPixels = (int)Math.Floor((endX - startX) / samplingSize) + 1;
            var heightPixels = (int)Math.Floor((endY - startY) / samplingSize) + 1;
            var numPixels = widthPixels * heightPixels;
            var centroidBrightness = CvImageUtility.BilinearSamplePixelValue(srcImage, y: detectedStar.Center.Y, x: detectedStar.Center.X) - background;

            var inputs = new double[numPixels][];
            var outputs = new double[numPixels];
            int pixelIndex = 0;
            for (var y = startY; y < endY; y += samplingSize) {
                for (var x = startX; x < endX; x += samplingSize) {
                    var value = CvImageUtility.BilinearSamplePixelValue(srcImage, y: y, x: x) - background;
                    var dx = x - detectedStar.Center.X;
                    var dy = y - detectedStar.Center.Y;
                    var input = new double[2] { dx, dy };
                    inputs[pixelIndex] = input;
                    outputs[pixelIndex++] = value;
                }
            }

            if (fitType == StarDetectorPSFFitType.Gaussian) {
                return new GaussianPSFType(inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starBoundingBox: detectedStar.StarBoundingBox, pixelScale: pixelScale);
            } else if (fitType == StarDetectorPSFFitType.Moffat_40) {
                return new MoffatPSFType(beta: 0.4, inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starBoundingBox: detectedStar.StarBoundingBox, pixelScale: pixelScale);
            } else {
                throw new ArgumentException($"Unknown PSF fit type {fitType}");
            }
        }

        public static PSFModel Solve(PSFModelTypeBase modelType, int maxIterations = 0, double tolerance = 1E-8, CancellationToken ct = default) {
            alglib.minlmstate state = null;
            alglib.minlmreport rep = null;
            try {
                var initialGuess = new double[] { modelType.StarBoundingBox.Width / 3.0, modelType.StarBoundingBox.Height / 3.0, 0.0d };
                if (modelType.UseJacobian) {
                    alglib.minlmcreatevj(modelType.Inputs.Length, initialGuess, out state);
                    alglib.minlmsetacctype(state, 1);
                } else {
                    const double deltaForNumericIntegration = 1E-4;
                    alglib.minlmcreatev(modelType.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);
                }

                // Set the box constraints on the parameters
                var lowerBounds = new double[] { 0, 0, -Math.PI / 2.0d };
                var sigmaUpperBound = Math.Sqrt(modelType.StarBoundingBox.Width * modelType.StarBoundingBox.Width + modelType.StarBoundingBox.Height * modelType.StarBoundingBox.Height) / 2;
                var upperBounds = new double[] { sigmaUpperBound, sigmaUpperBound, Math.PI / 2.0d };
                alglib.minlmsetbc(state, lowerBounds, upperBounds);

                // Set the termination conditions
                alglib.minlmsetcond(state, tolerance, maxIterations);

                // Set all variables to the same scale. This feature is useful if the magnitude if some variables is dramatically different than others
                alglib.minlmsetscale(state, new double[] { 1, 1, 1 });

                // Perform the optimization
                alglib.minlmoptimize(state, modelType.FitResiduals, modelType.FitResidualsJacobian, null, null);
                ct.ThrowIfCancellationRequested();

                var solution = new double[3];
                alglib.minlmresults(state, out solution, out rep);
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

                var sigX = solution[0];
                var sigY = solution[1];
                var theta = solution[2];

                var fwhmX = sigX * SIGMA_TO_FWHM_FACTOR;
                var fwhmY = sigY * SIGMA_TO_FWHM_FACTOR;
                var rSquared = modelType.GoodnessOfFit(sigX, sigY, theta);
                return new PSFModel(fwhmX: fwhmX, fwhmY: fwhmY, thetaRadians: theta, rSquared: rSquared, pixelScale: modelType.PixelScale);
            } finally {
                if (state != null) {
                    alglib.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    alglib.deallocateimmediately(ref rep);
                }
            }
        }
    }
}