#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.TenMicron.ModelBuilder;
using NINA.Core.Enum;
using NINA.Equipment.Equipment;

namespace Joko.NINA.Plugins.TenMicron.Equipment {

    public class MountInfo : DeviceInfo {
        private string mountId;

        public string MountId {
            get => mountId;
            set {
                if (mountId != value) {
                    mountId = value;
                    RaisePropertyChanged();
                }
            }
        }

        private ProductFirmware productFirmware;

        public ProductFirmware ProductFirmware {
            get => productFirmware;
            set {
                if (productFirmware != value) {
                    productFirmware = value;
                    RaisePropertyChanged();
                }
            }
        }

        private bool unattendedFlipEnabled;

        public bool UnattendedFlipEnabled {
            get => unattendedFlipEnabled;
            set {
                if (unattendedFlipEnabled != value) {
                    unattendedFlipEnabled = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal trackingRateArcsecPerSec;

        public decimal TrackingRateArcsecPerSec {
            get => trackingRateArcsecPerSec;
            set {
                if (trackingRateArcsecPerSec != value) {
                    trackingRateArcsecPerSec = value;
                    RaisePropertyChanged();
                }
            }
        }

        private MountStatusEnum status;

        public MountStatusEnum Status {
            get => status;
            set {
                if (status != value) {
                    status = value;
                    RaisePropertyChanged();
                }
            }
        }

        private decimal slewSettleTimeSeconds;

        public decimal SlewSettleTimeSeconds {
            get => slewSettleTimeSeconds;
            set {
                if (slewSettleTimeSeconds != value) {
                    slewSettleTimeSeconds = value;
                    RaisePropertyChanged();
                }
            }
        }

        private int meridianLimitDegrees;

        public int MeridianLimitDegrees {
            get => meridianLimitDegrees;
            set {
                if (meridianLimitDegrees != value) {
                    meridianLimitDegrees = value;
                    RaisePropertyChanged();
                }
            }
        }

        private string refractionFilePathOverride;

        public string RefractionFilePathOverride {
            get => refractionFilePathOverride;
            set {
                if (refractionFilePathOverride != value) {
                    refractionFilePathOverride = value;
                    RaisePropertyChanged();
                }
            }
        }
    }
}