#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Joko.NINA.Plugins.Common.Utility;
using Joko.NINA.Plugins.TenMicron.Interfaces;
using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joko.NINA.Plugins.TenMicron.ModelBuilder {

    public class ModelBuilderOptions : BaseINPC, IModelBuilderOptions {
        private readonly PluginOptionsAccessor optionsAccessor;

        public ModelBuilderOptions(IProfileService profileService) {
            var guid = PluginOptionsAccessor.GetAssemblyGuid(typeof(ModelBuilderOptions));
            if (guid == null) {
                throw new Exception($"Guid not found in assembly metadata");
            }

            this.optionsAccessor = new PluginOptionsAccessor(profileService, guid.Value);
            InitializeOptions();
        }

        private void InitializeOptions() {
            placeholder = optionsAccessor.GetValueDouble("Placeholder", 0.0);
        }

        public void ResetDefaults() {
            Placeholder = 0.0;
        }

        private double placeholder;

        public double Placeholder {
            get => placeholder;
            set {
                if (placeholder != value) {
                    if (value < 0) {
                        throw new ArgumentException("Placeholder must be non-negative", "Placeholder");
                    }
                    placeholder = value;
                    optionsAccessor.SetValueDouble("Placeholder", placeholder);
                    RaisePropertyChanged();
                }
            }
        }
    }
}