#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;

namespace Joko.NINA.Plugins.TenMicron.ModelBuilder {

    public class ModelPoint : BaseINPC {
        private readonly IProfileService profileService;

        public ModelPoint(IProfileService profileService, int index, Coordinates coordinates) {
            this.profileService = profileService;
            this.Index = index;
            this.Coordinates = coordinates;
        }

        private int index;

        public int Index {
            get => index;
            private set {
                if (index != value) {
                    this.index = value;
                    RaisePropertyChanged();
                }
            }
        }

        private Coordinates coordinates;

        public Coordinates Coordinates {
            get => coordinates;
            private set {
                if (value != coordinates) {
                    this.coordinates = value.Transform(Epoch.JNOW);
                    RaisePropertyChanged();
                    this.AltAz = this.coordinates.Transform(
                        latitude: Angle.ByDegree(this.profileService.ActiveProfile.AstrometrySettings.Latitude),
                        longitude: Angle.ByDegree(this.profileService.ActiveProfile.AstrometrySettings.Longitude));
                }
            }
        }

        private TopocentricCoordinates altAz;

        public TopocentricCoordinates AltAz {
            get => altAz;
            private set {
                if (value != altAz) {
                    this.altAz = value;
                    RaisePropertyChanged();
                }
            }
        }

        private CoordinateAngle mountReportedDeclination;

        public CoordinateAngle MountReportedDeclination {
            get => mountReportedDeclination;
            set {
                this.mountReportedDeclination = value;
                RaisePropertyChanged();
            }
        }

        private AstrometricTime mountReportedRightAscension;

        public AstrometricTime MountReportedRightAscension {
            get => mountReportedRightAscension;
            set {
                this.mountReportedRightAscension = value;
                RaisePropertyChanged();
            }
        }

        private double rmsError;

        public double RMSError {
            get => rmsError;
            set {
                if (rmsError != value) {
                    this.rmsError = value;
                    RaisePropertyChanged();
                }
            }
        }
    }
}