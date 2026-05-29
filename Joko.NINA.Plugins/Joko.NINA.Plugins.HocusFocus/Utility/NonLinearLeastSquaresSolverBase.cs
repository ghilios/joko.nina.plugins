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
using System.Linq;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public interface INonLinearLeastSquaresDataPoint {

        double[] ToInput();

        double ToOutput();

        /// <summary>
        /// The 1-sigma measurement uncertainty of <see cref="ToOutput"/>, in the same units as the
        /// output. Used to weight the fit by 1/σ (χ²). The default of 1.0 reduces to an unweighted fit.
        /// </summary>
        double ToOutputStdDev() => 1.0;
    }

    public interface INonLinearLeastSquaresParameters {

        void FromArray(double[] parameters);

        double[] ToArray();
    }

    public abstract class NonLinearLeastSquaresSolverBase<T, U>
        where T : INonLinearLeastSquaresDataPoint
        where U : INonLinearLeastSquaresParameters, new() {

        protected NonLinearLeastSquaresSolverBase(List<T> dataPoints, int numParameters) {
            this.Inputs = dataPoints.Select(p => p.ToInput()).ToArray();
            this.Outputs = dataPoints.Select(p => p.ToOutput()).ToArray();
            this.OutputStdDevs = dataPoints.Select(p => p.ToOutputStdDev()).ToArray();
            this.NumParameters = numParameters;
        }

        public double[][] Inputs { get; private set; }
        public double[] Outputs { get; private set; }

        /// <summary>Per-observation 1-sigma uncertainties of <see cref="Outputs"/> (used for χ² weighting).</summary>
        public double[] OutputStdDevs { get; private set; }

        public int NumParameters { get; private set; }

        public virtual bool UseJacobian => false;

        public virtual double NumericIntegrationIntervalSize => 1E-6;

        public abstract double Value(double[] parameters, double[] input);

        public virtual void Gradient(double[] parameters, double[] input, double[] result) {
            throw new NotImplementedException();
        }

        public abstract void SetInitialGuess(double[] initialGuess);

        public abstract void SetBounds(double[] lowerBounds, double[] upperBounds);

        public abstract void SetScale(double[] scales);
    }
}