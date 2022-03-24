#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using ILNumerics;
using ILNumerics.Toolboxes;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Utility;
using OpenCvSharp;
using System;
using System.Linq;
using System.Threading;
using Rect = OpenCvSharp.Rect;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class PSFModelSolution {
        public double A { get; set; }
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double SigmaX { get; set; }
        public double SigmaY { get; set; }
        public double Theta { get; set; }
    }

    public abstract class PSFModelTypeBase {

        protected PSFModelTypeBase(double centroidBrightness, double pixelScale, Rect starBoundingBox) {
            this.CentroidBrightness = centroidBrightness;
            this.PixelScale = pixelScale;
            this.StarBoundingBox = starBoundingBox;
        }

        public abstract StarDetectorPSFFitType PSFType { get; }
        public double CentroidBrightness { get; private set; }
        public double PixelScale { get; private set; }
        public Rect StarBoundingBox { get; private set; }

        public abstract double SigmaToFWHM(double sigma);

        public abstract double GoodnessOfFit(double A, double x0, double y0, double sigmaX, double sigmaY, double theta);

        public abstract PSFModelSolution Solve(int maxIterations, double tolerance, CancellationToken ct);

        public abstract PSFModelSolution SolveAbsoluteDeviation(int maxIterations, double tolerance, CancellationToken ct);
    }

    public abstract class PSFModelTypeILNBase : PSFModelTypeBase {

        protected PSFModelTypeILNBase(double centroidBrightness, double pixelScale, Rect starBoundingBox, double[,] inputs, double[] outputs)
            : base(centroidBrightness, pixelScale, starBoundingBox) {
            this.Inputs = inputs;
            this.Outputs = outputs;

            var sigmaUpperBound = Math.Sqrt(this.StarBoundingBox.Width * this.StarBoundingBox.Width + this.StarBoundingBox.Height * this.StarBoundingBox.Height) / 2;
            var dxLimit = this.StarBoundingBox.Width / 8.0d;
            var dyLimit = this.StarBoundingBox.Height / 8.0d;
            this.lowerBounds = new double[] { 0.0d, -dxLimit, -dyLimit, 0, 0, -Math.PI / 2.0d };
            this.upperBounds = new double[] { 2.0d * this.CentroidBrightness, dxLimit, dyLimit, sigmaUpperBound, sigmaUpperBound, Math.PI / 2.0d };
        }

        public double[,] Inputs { get; private set; }
        public double[] Outputs { get; private set; }

        private readonly double[] lowerBounds;
        private readonly double[] upperBounds;

        public abstract RetArray<double> Residuals(InArray<double> parameters);

        public override double GoodnessOfFit(double A, double x0, double y0, double sigmaX, double sigmaY, double theta) {
            using (Scope.Enter()) {
                var arrayParameters = new double[] { A, x0, y0, sigmaX, sigmaY, theta };
                for (int i = 0; i < arrayParameters.Length; ++i) {
                    arrayParameters[i] = Math.Min(upperBounds[i], arrayParameters[i]);
                    arrayParameters[i] = Math.Max(lowerBounds[i], arrayParameters[i]);
                }

                Array<double> parameters = arrayParameters;
                Array<double> observedValues = this.Outputs;
                observedValues = observedValues.T;
                Array<double> residuals = this.Residuals(parameters);
                double yBar = observedValues.mean<double>().GetValue(0);
                Array<double> observedDispersion = observedValues - yBar;
                double tss = ILMath.multiplyElem(observedDispersion, observedDispersion).sum().GetValue(0);
                double rss = ILMath.multiplyElem(residuals, residuals).sum().GetValue(0);
                return 1 - rss / tss;
            }
        }

        public override PSFModelSolution Solve(int maxIterations, double tolerance, CancellationToken ct) {
            var initialGuess = new double[] { this.CentroidBrightness, 0.0, 0.0, this.StarBoundingBox.Width / 3.0, this.StarBoundingBox.Height / 3.0, 0.0d };
            Array<double> solution = Optimization.leastsq_levm(this.Residuals, initialGuess, Optimization.jacobian_prec, maxIter: maxIterations, tol: tolerance);
            ct.ThrowIfCancellationRequested();

            return new PSFModelSolution() {
                A = solution.GetValue<double>(0),
                X0 = solution.GetValue<double>(1),
                Y0 = solution.GetValue<double>(2),
                SigmaX = solution.GetValue<double>(3),
                SigmaY = solution.GetValue<double>(4),
                Theta = solution.GetValue<double>(5)
            };
        }

        public override PSFModelSolution SolveAbsoluteDeviation(int maxIterations, double tolerance, CancellationToken ct) {
            throw new NotImplementedException();
        }
    }

    public abstract class PSFModelTypeAlglibBase : PSFModelTypeBase {

        protected PSFModelTypeAlglibBase(double centroidBrightness, double pixelScale, Rect starBoundingBox, double[][] inputs, double[] outputs)
            : base(centroidBrightness, pixelScale, starBoundingBox) {
            this.Inputs = inputs;
            this.Outputs = outputs;
        }

        public abstract bool UseJacobian { get; }
        public double[][] Inputs { get; private set; }
        public double[] Outputs { get; private set; }

        public abstract double Value(double[] parameters, double[] input);

        public abstract void Gradient(double[] parameters, double[] input, double[] result);

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

        public override double GoodnessOfFit(double A, double x0, double y0, double sigmaX, double sigmaY, double theta) {
            var parameters = new double[] { A, x0, y0, sigmaX, sigmaY, theta };

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
            return 1 - rss / tss;
        }

        public virtual void FitAbsoluteDeviation(double[] parameters, double[] fi, object obj) {
            var residuals = new double[this.Inputs.Length];
            FitResiduals(parameters, residuals, obj);
            fi[0] = 0.0d;
            for (int i = 0; i < this.Inputs.Length; ++i) {
                fi[0] += Math.Abs(residuals[i]);
            }
        }

        public virtual void FitAbsoluteDeviationJacobian(double[] parameters, double[] fi, double[,] jac, object obj) {
            var residuals = new double[this.Inputs.Length];
            FitResiduals(parameters, residuals, obj);
            var singleGradient = new double[parameters.Length];
            fi[0] = 0.0d;
            for (int j = 0; j < parameters.Length; ++j) {
                jac[0, j] = 0.0d;
            }

            for (int i = 0; i < this.Inputs.Length; ++i) {
                var sign = Math.Sign(residuals[i]);
                Gradient(parameters, this.Inputs[i], singleGradient);
                for (int j = 0; j < parameters.Length; ++j) {
                    jac[0, j] += sign * singleGradient[j];
                }
                fi[0] += Math.Abs(residuals[i]);
            }
        }

        public override PSFModelSolution Solve(int maxIterations, double tolerance, CancellationToken ct) {
            alglib.minlmstate state = null;
            alglib.minlmreport rep = null;
            try {
                var sigmaUpperBound = Math.Sqrt(this.StarBoundingBox.Width * this.StarBoundingBox.Width + this.StarBoundingBox.Height * this.StarBoundingBox.Height) / 2;
                var initialGuess = new double[] { this.CentroidBrightness, 0.0, 0.0, this.StarBoundingBox.Width / 3.0, this.StarBoundingBox.Height / 3.0, 0.0d };
                var dxLimit = this.StarBoundingBox.Width / 8.0d;
                var dyLimit = this.StarBoundingBox.Height / 8.0d;
                var lowerBounds = new double[] { 0.0d, -dxLimit, -dyLimit, 0, 0, -Math.PI / 2.0d };
                var upperBounds = new double[] { 2.0d * this.CentroidBrightness, dxLimit, dyLimit, sigmaUpperBound, sigmaUpperBound, Math.PI / 2.0d };
                var scale = new double[] { 0.001, 0.1, 0.1, 1, 1, 1 };
                var solution = new double[5];

                if (this.UseJacobian) {
                    alglib.minlmcreatevj(this.Inputs.Length, initialGuess, out state);
                    alglib.minlmsetacctype(state, 1);
                } else {
                    const double deltaForNumericIntegration = 1E-4;
                    alglib.minlmcreatev(this.Inputs.Length, initialGuess, deltaForNumericIntegration, out state);
                }
                alglib.minlmsetbc(state, lowerBounds, upperBounds);

                // Set the termination conditions
                alglib.minlmsetcond(state, tolerance, maxIterations);

                // Set all variables to the same scale, except for x0, y0. This feature is useful if the magnitude if some variables is dramatically different than others
                alglib.minlmsetscale(state, scale);

                // TODO: Remove optguard
                // alglib.minlmoptguardgradient(state, 1E-4);

                // Perform the optimization
                alglib.minlmoptimize(state, this.FitResiduals, this.FitResidualsJacobian, null, null);
                ct.ThrowIfCancellationRequested();

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

                /*
                alglib.optguardreport ogrep;
                alglib.minlmoptguardresults(state, out ogrep);
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
                    X0 = solution[1],
                    Y0 = solution[2],
                    SigmaX = solution[3],
                    SigmaY = solution[4],
                    Theta = solution[5]
                };
            } finally {
                if (state != null) {
                    alglib.deallocateimmediately(ref state);
                }
                if (rep != null) {
                    alglib.deallocateimmediately(ref rep);
                }
            }
        }

        public override PSFModelSolution SolveAbsoluteDeviation(int maxIterations, double tolerance, CancellationToken ct) {
            alglib.minnsstate state = null;
            alglib.minnsreport rep = null;
            try {
                var sigmaUpperBound = Math.Sqrt(this.StarBoundingBox.Width * this.StarBoundingBox.Width + this.StarBoundingBox.Height * this.StarBoundingBox.Height) / 2;
                var initialGuess = new double[] { this.CentroidBrightness, 0.0, 0.0, this.StarBoundingBox.Width / 2.0, this.StarBoundingBox.Height / 2.0, 0.0d };
                var dxLimit = 0.25;
                var dyLimit = 0.25;
                var lowerBounds = new double[] { 0.75d * this.CentroidBrightness, -dxLimit, -dyLimit, sigmaUpperBound / 2.0, sigmaUpperBound / 2.0, -Math.PI / 2.0d };
                var upperBounds = new double[] { 1.5d * this.CentroidBrightness, dxLimit, dyLimit, sigmaUpperBound, sigmaUpperBound, Math.PI / 2.0d };
                var scale = new double[] { 0.1, 0.1, 0.1, 1, 1, 1 };
                var solution = new double[5];

                // AGS settings
                // * radius=0.1   documentation states this is a good initial value. The algorithm will decrease it over time
                // * rho=50       penalty coefficient for nonlinear constraints. This needs to be large enough that it enforces constraints, but small enough to avoid extreme slowdown
                var radius = 1.0;
                var rho = 0.0;

                // TODO: REMOVE
                var diffstep = 0.1;

                // TODO: Tune this. 0 makes this unbounded?
                maxIterations = 0;

                alglib.minnscreate(initialGuess, out state);
                alglib.minnssetalgoags(state, radius, rho);

                // Set bounded constraints for input parameters
                alglib.minnssetbc(state, lowerBounds, upperBounds);

                // Set the termination conditions
                alglib.minnssetcond(state, tolerance, maxIterations);

                // Set all variables to the same scale, except for x0, y0. This feature is useful if the magnitude if some variables is dramatically different than others
                alglib.minnssetscale(state, scale);

                // Perform the optimization
                alglib.minnsoptimize(state, this.FitAbsoluteDeviationJacobian, null, null);
                ct.ThrowIfCancellationRequested();

                alglib.minnsresults(state, out solution, out rep);
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

                return new PSFModelSolution() {
                    A = solution[0],
                    X0 = solution[1],
                    Y0 = solution[2],
                    SigmaX = solution[3],
                    SigmaY = solution[4],
                    Theta = solution[5]
                };
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

    public class PSFModeler {

        public static PSFModelTypeBase Create(StarDetectorPSFFitType fitType, int psfResolution, Star detectedStar, Mat srcImage, double pixelScale, bool useILNumerics) {
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

            if (useILNumerics) {
                var inputs = new double[2, numPixels];
                var outputs = new double[numPixels];
                int pixelIndex = 0;
                for (var y = startY; y < endY; y += samplingSize) {
                    for (var x = startX; x < endX; x += samplingSize) {
                        var value = CvImageUtility.BilinearSamplePixelValue(srcImage, y: y, x: x) - background;
                        var dx = x - detectedStar.Center.X;
                        var dy = y - detectedStar.Center.Y;
                        inputs[0, pixelIndex] = dx;
                        inputs[1, pixelIndex] = dy;
                        outputs[pixelIndex++] = value;
                    }
                }

                if (fitType == StarDetectorPSFFitType.Gaussian) {
                    return new GaussianPSFILNumericsType(inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starBoundingBox: detectedStar.StarBoundingBox, pixelScale: pixelScale);
                } else if (fitType == StarDetectorPSFFitType.Moffat_40) {
                    return new MoffatPSFILNumericsType(beta: 4.0, inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starBoundingBox: detectedStar.StarBoundingBox, pixelScale: pixelScale);
                } else {
                    throw new ArgumentException($"Unknown PSF fit type {fitType}");
                }
            } else {
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
                    return new GaussianPSFAlglibType(inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starBoundingBox: detectedStar.StarBoundingBox, pixelScale: pixelScale);
                } else if (fitType == StarDetectorPSFFitType.Moffat_40) {
                    return new MoffatPSFAlglibType(beta: 4.0, inputs: inputs, outputs: outputs, centroidBrightness: centroidBrightness, starBoundingBox: detectedStar.StarBoundingBox, pixelScale: pixelScale);
                } else {
                    throw new ArgumentException($"Unknown PSF fit type {fitType}");
                }
            }
        }

        public static PSFModel Solve(PSFModelTypeBase modelType, int maxIterations = 0, double tolerance = 1E-8, CancellationToken ct = default) {
            var modelSolution = modelType.SolveAbsoluteDeviation(maxIterations, tolerance, ct);
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

            // Normalize rotation angles by ensuring the X axis is the elongated one
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
            var rSquared = modelType.GoodnessOfFit(modelSolution.A, modelSolution.X0, modelSolution.Y0, sigX, sigY, theta);
            return new PSFModel(psfType: modelType.PSFType, sigmaX: sigX, sigmaY: sigY, fwhmX: fwhmX, fwhmY: fwhmY, thetaRadians: theta, rSquared: rSquared, pixelScale: modelType.PixelScale);
        }
    }
}