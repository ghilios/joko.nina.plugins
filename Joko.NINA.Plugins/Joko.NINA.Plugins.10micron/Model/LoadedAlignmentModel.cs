#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using NINA.Astrometry;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Joko.NINA.Plugins.TenMicron.Model {

    public class LoadedAlignmentModel : BaseINPC {

        public void SynchronizePoints() {
            var maxError = (double)this.OriginalAlignmentStars.Select(s => s.ErrorArcseconds).DefaultIfEmpty(decimal.Zero).Max();
            this.AlignmentStars = new AsyncObservableCollection<AlignmentStarPoint>(
                this.OriginalAlignmentStars.Select(s => AlignmentStarPoint.FromAlignmentStarInfo(
                    alignmentStarInfo: s, modelMaxErrorArcsec: maxError, latitude: latitude, longitude: longitude, siteElevation: siteElevation)));
        }

        private Angle latitude = Angle.Zero;

        public Angle Latitude {
            get => latitude;
            set {
                if (latitude != value) {
                    latitude = value;
                    RaisePropertyChanged();
                }
            }
        }

        private Angle longitude = Angle.Zero;

        public Angle Longitude {
            get => longitude;
            set {
                if (longitude != value) {
                    longitude = value;
                    RaisePropertyChanged();
                }
            }
        }

        private double siteElevation = 0.0d;

        public double SiteElevation {
            get => siteElevation;
            set {
                if (siteElevation != value) {
                    siteElevation = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal rightAscensionAzimuth = decimal.Zero;

        public decimal RightAscensionAzimuth {
            get => rightAscensionAzimuth;
            set {
                if (rightAscensionAzimuth != value) {
                    rightAscensionAzimuth = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal rightAscensionAltitude = decimal.Zero;

        public decimal RightAscensionAltitude {
            get => rightAscensionAltitude;
            set {
                if (rightAscensionAltitude != value) {
                    rightAscensionAltitude = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal polarAlignErrorDegrees = decimal.Zero;

        public decimal PolarAlignErrorDegrees {
            get => polarAlignErrorDegrees;
            set {
                if (polarAlignErrorDegrees != value) {
                    polarAlignErrorDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal rightAscensionPolarPositionAngleDegrees = decimal.Zero;

        public decimal RightAscensionPolarPositionAngleDegrees {
            get => rightAscensionPolarPositionAngleDegrees;
            set {
                if (rightAscensionPolarPositionAngleDegrees != value) {
                    rightAscensionPolarPositionAngleDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal orthogonalityErrorDegrees = decimal.Zero;

        public decimal OrthogonalityErrorDegrees {
            get => orthogonalityErrorDegrees;
            set {
                if (orthogonalityErrorDegrees != value) {
                    orthogonalityErrorDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal azimuthAdjustmentTurns = decimal.Zero;

        public decimal AzimuthAdjustmentTurns {
            get => azimuthAdjustmentTurns;
            set {
                if (azimuthAdjustmentTurns != value) {
                    azimuthAdjustmentTurns = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal altitudeAdjustmentTurns = decimal.Zero;

        public decimal AltitudeAdjustmentTurns {
            get => altitudeAdjustmentTurns;
            set {
                if (altitudeAdjustmentTurns != value) {
                    altitudeAdjustmentTurns = value;
                    RaisePropertyChanged();
                }
            }
        }

        private int modelTerms = -1;

        public int ModelTerms {
            get => modelTerms;
            set {
                if (modelTerms != value) {
                    modelTerms = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal rmsError = decimal.Zero;

        public decimal RMSError {
            get => rmsError;
            set {
                if (rmsError != value) {
                    rmsError = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal paErrorAltitudeDegrees = decimal.Zero;

        public decimal PAErrorAltitudeDegrees {
            get => paErrorAltitudeDegrees;
            set {
                if (paErrorAltitudeDegrees != value) {
                    paErrorAltitudeDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal paErrorAzimuthDegrees = decimal.Zero;

        public decimal PAErrorAzimuthDegrees {
            get => paErrorAzimuthDegrees;
            set {
                if (paErrorAzimuthDegrees != value) {
                    paErrorAzimuthDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private ImmutableList<AlignmentStarInfo> originalAlignmentStars = ImmutableList.Create<AlignmentStarInfo>();

        public ImmutableList<AlignmentStarInfo> OriginalAlignmentStars {
            get => originalAlignmentStars;
            set {
                this.originalAlignmentStars = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AlignmentStarCount));
                RaisePropertyChanged(nameof(MaxRMSError));
            }
        }

        private string modelName = "";

        public string ModelName {
            get => modelName;
            set {
                if (modelName != value) {
                    modelName = value;
                    RaisePropertyChanged();
                }
            }
        }

        private int alignmentStarCount = -1;

        public int AlignmentStarCount {
            get => alignmentStarCount;
            set {
                if (alignmentStarCount != value) {
                    alignmentStarCount = value;
                    RaisePropertyChanged();
                }
            }
        }

        public double MaxRMSError {
            get => (double)OriginalAlignmentStars.Select(s => s.ErrorArcseconds).DefaultIfEmpty(decimal.Zero).Max();
        }

        private AsyncObservableCollection<AlignmentStarPoint> alignmentStars = new AsyncObservableCollection<AlignmentStarPoint>();

        public AsyncObservableCollection<AlignmentStarPoint> AlignmentStars {
            get => alignmentStars;
            private set {
                alignmentStars = value;
                RaisePropertyChanged();
            }
        }

        private void AlignmentStars_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            RaisePropertyChanged(nameof(AlignmentStarCount));
            RaisePropertyChanged(nameof(MaxRMSError));
        }

        public void Clear() {
            this.ModelName = "";
            this.RightAscensionAzimuth = decimal.MinValue;
            this.RightAscensionAltitude = decimal.MinValue;
            this.PolarAlignErrorDegrees = decimal.MinValue;
            this.PAErrorAltitudeDegrees = decimal.MinValue;
            this.PAErrorAzimuthDegrees = decimal.MinValue;
            this.RightAscensionPolarPositionAngleDegrees = decimal.MinValue;
            this.OrthogonalityErrorDegrees = decimal.MinValue;
            this.AzimuthAdjustmentTurns = decimal.MinValue;
            this.AltitudeAdjustmentTurns = decimal.MinValue;
            this.ModelTerms = -1;
            this.RMSError = decimal.MinValue;
            this.AlignmentStarCount = -1;
            this.OriginalAlignmentStars = ImmutableList.Create<AlignmentStarInfo>();
            SynchronizePoints();
        }
    }
}