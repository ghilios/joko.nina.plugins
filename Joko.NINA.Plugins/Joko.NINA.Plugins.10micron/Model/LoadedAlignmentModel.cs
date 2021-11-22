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
        private Angle latitude;
        private Angle longitude;
        private double siteElevation;

        public LoadedAlignmentModel(
            AlignmentModelInfo alignmentModelInfo,
            IEnumerable<AlignmentStarInfo> alignmentStars,
            Angle latitude,
            Angle longitude,
            double siteElevation,
            DateTime modelCreationTime,
            string modelName = null) {
            this.alignmentModelInfo = alignmentModelInfo;
            this.latitude = latitude;
            this.longitude = longitude;
            this.siteElevation = siteElevation;
            // TODO: FIXME
            // this.modelCreationTime = modelCreationTime;
            this.modelCreationTime = new DateTime(2021, 11, 20, 5, 37, 16, DateTimeKind.Utc);
            this.modelName = modelName;
            this.originalAlignmentStars = alignmentStars.ToImmutableList();
            SynchronizePoints();
        }

        public static LoadedAlignmentModel Empty() {
            return new LoadedAlignmentModel(AlignmentModelInfo.Empty(), Enumerable.Empty<AlignmentStarInfo>(), Angle.Zero, Angle.Zero, 0.0, DateTime.MinValue);
        }

        private void SynchronizePoints() {
            var maxError = (double)this.OriginalAlignmentStars.Select(s => s.ErrorArcseconds).DefaultIfEmpty(decimal.Zero).Max();
            this.AlignmentStars = new AsyncObservableCollection<AlignmentStarPoint>(
                this.OriginalAlignmentStars.Select(s => AlignmentStarPoint.FromAlignmentStarInfo(
                    alignmentStarInfo: s, modelMaxErrorArcsec: maxError, latitude: latitude, longitude: longitude, siteElevation: siteElevation, modelCreationTime: ModelCreationTime)));
        }

        private ImmutableList<AlignmentStarInfo> originalAlignmentStars;

        public ImmutableList<AlignmentStarInfo> OriginalAlignmentStars {
            get => originalAlignmentStars;
            private set {
                this.originalAlignmentStars = value;
                SynchronizePoints();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(AlignmentStarCount));
                RaisePropertyChanged(nameof(MaxRMSError));
            }
        }

        private AlignmentModelInfo alignmentModelInfo;
        public AlignmentModelInfo AlignmentModelInfo => alignmentModelInfo;

        private DateTime modelCreationTime;

        public DateTime ModelCreationTime {
            get => modelCreationTime;
            set {
                if (modelCreationTime != value) {
                    modelCreationTime = value;
                    SynchronizePoints();
                    RaisePropertyChanged();
                }
            }
        }

        private string modelName;

        public string ModelName {
            get => modelName;
            set {
                if (modelName != value) {
                    modelName = value;
                    RaisePropertyChanged();
                }
            }
        }

        public int AlignmentStarCount {
            get => OriginalAlignmentStars.Count;
        }

        public double MaxRMSError {
            get => (double)OriginalAlignmentStars.Select(s => s.ErrorArcseconds).DefaultIfEmpty(decimal.Zero).Max();
        }

        private AsyncObservableCollection<AlignmentStarPoint> alignmentStars;

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

        public void CopyFrom(LoadedAlignmentModel other) {
            this.AlignmentModelInfo.CopyFrom(other.AlignmentModelInfo);
            this.latitude = other.latitude;
            this.longitude = other.longitude;
            this.siteElevation = other.siteElevation;
            this.ModelName = other.ModelName;
            this.modelCreationTime = other.ModelCreationTime;
            this.OriginalAlignmentStars = other.OriginalAlignmentStars;
        }

        public void Clear() {
            CopyFrom(Empty());
        }
    }
}