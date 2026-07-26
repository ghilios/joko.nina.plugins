#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection {

    public class PSFModel {

        public PSFModel(
            StarDetectorPSFFitType psfType,
            double offsetX,
            double offsetY,
            double peak,
            double background,
            double sigmaX,
            double sigmaY,
            double fwhmX,
            double fwhmY,
            double thetaRadians,
            double rSquared,
            double pixelScale,
            double reducedChiSquared = double.NaN,
            double beta = double.NaN) {
            this.PSFType = psfType;
            this.OffsetX = offsetX;
            this.OffsetY = offsetY;
            this.Peak = peak;
            this.Background = background;
            this.SigmaX = sigmaX;
            this.SigmaY = sigmaY;
            this.Sigma = Math.Sqrt(sigmaX * sigmaY);
            this.FWHMx = fwhmX;
            this.FWHMy = fwhmY;
            this.ThetaRadians = thetaRadians;
            var a = Math.Max(fwhmX, fwhmY);
            var b = Math.Min(fwhmX, fwhmY);
            this.Eccentricity = Math.Sqrt(1 - b * b / (a * a));
            this.FWHMPixels = Math.Sqrt(fwhmX * fwhmY);
            this.FWHMArcsecs = this.FWHMPixels * pixelScale;
            this.RSquared = rSquared;
            this.ReducedChiSquared = reducedChiSquared;
            this.Beta = beta;
        }

        public StarDetectorPSFFitType PSFType { get; private set; }
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public double Peak { get; private set; }
        public double Background { get; private set; }
        public double SigmaX { get; private set; }
        public double SigmaY { get; private set; }
        public double Sigma { get; private set; }
        public double FWHMx { get; private set; }
        public double FWHMy { get; private set; }
        public double ThetaRadians { get; private set; }
        public double FWHMPixels { get; private set; }
        public double FWHMArcsecs { get; private set; }
        public double Eccentricity { get; private set; }
        public double RSquared { get; private set; }

        /// <summary>
        /// Reduced chi-squared goodness-of-fit: rss / (nPixels * noiseSigma²).
        /// Values near 1.0 indicate residuals consistent with the noise level.
        /// NaN when noiseSigma was not available at fit time.
        /// </summary>
        public double ReducedChiSquared { get; private set; }

        /// <summary>
        /// Moffat beta (power-law index). NaN for Gaussian PSF types.
        /// For fixed-beta Moffat types this is the fixed value (e.g. 4.0, 2.5, 1.5).
        /// For the fittable Moffat type this is the optimized value.
        /// </summary>
        public double Beta { get; private set; }

        /// <summary>
        /// This model re-expressed in SOURCE pixels after star detection ran on a software-binned frame: every
        /// length (offsets, sigmas, FWHM) grows by <paramref name="factor"/>, and the pixel scale shrinks by the
        /// same factor so <see cref="FWHMArcsecs"/> — which was already physical — is preserved exactly.
        /// Amplitudes, orientation, eccentricity and the fit statistics are scale-invariant and carry over.
        /// </summary>
        public PSFModel ScaledToSourcePixels(int factor) {
            if (factor <= 1) {
                return this;
            }
            var pixelScale = FWHMPixels > 0.0 ? FWHMArcsecs / FWHMPixels : 0.0;
            return new PSFModel(
                PSFType,
                OffsetX * factor,
                OffsetY * factor,
                Peak,
                Background,
                SigmaX * factor,
                SigmaY * factor,
                FWHMx * factor,
                FWHMy * factor,
                ThetaRadians,
                RSquared,
                pixelScale / factor,
                ReducedChiSquared,
                Beta);
        }

        public override string ToString() {
            return $"{{{nameof(PSFType)}={PSFType.ToString()}, {nameof(OffsetX)}={OffsetX.ToString()}, {nameof(OffsetY)}={OffsetY.ToString()}, {nameof(Background)}={Background.ToString()}, {nameof(SigmaX)}={SigmaX.ToString()}, {nameof(SigmaY)}={SigmaY.ToString()}, {nameof(Sigma)}={Sigma.ToString()}, {nameof(FWHMx)}={FWHMx.ToString()}, {nameof(FWHMy)}={FWHMy.ToString()}, {nameof(ThetaRadians)}={ThetaRadians.ToString()}, {nameof(FWHMPixels)}={FWHMPixels.ToString()}, {nameof(FWHMArcsecs)}={FWHMArcsecs.ToString()}, {nameof(Eccentricity)}={Eccentricity.ToString()}, {nameof(RSquared)}={RSquared.ToString()}, {nameof(ReducedChiSquared)}={ReducedChiSquared.ToString()}, {nameof(Beta)}={Beta.ToString()}}}";
        }
    }
}