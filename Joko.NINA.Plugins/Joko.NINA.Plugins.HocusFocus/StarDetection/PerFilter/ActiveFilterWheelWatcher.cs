#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Interfaces.Mediator;
using System;

namespace NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter {

    /// <summary>
    /// Bridges filter-wheel device-info broadcasts to a callback, so <see cref="PerFilterEditBinder"/> can re-raise
    /// its active-filter warning when the wheel connects, disconnects, or changes filter.
    ///
    /// This exists as a separate adapter rather than being folded into the binder on purpose: the binder takes plain
    /// delegates instead of NINA mediator types precisely so it stays constructible with no equipment and remains
    /// unit-testable. The mediator dependency lives here, at the composition root, and the binder only ever sees the
    /// callback. This is the codebase's usual <c>RegisterConsumer(this)</c> pattern (cf. InspectorVM,
    /// TiltAdapterWizardVM), just narrowed to a single notification.
    ///
    /// <see cref="UpdateDeviceInfo"/> is invoked from wherever the mediator broadcasts, which is not necessarily the
    /// UI thread — the callback is responsible for marshaling (<see cref="PerFilterEditBinder.RefreshActiveFilter"/>
    /// posts, non-blocking, for exactly this reason).
    /// </summary>
    public class ActiveFilterWheelWatcher : IFilterWheelConsumer {
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly Action onFilterWheelChanged;
        private bool disposed;

        public ActiveFilterWheelWatcher(IFilterWheelMediator filterWheelMediator, Action onFilterWheelChanged) {
            this.filterWheelMediator = filterWheelMediator ?? throw new ArgumentNullException(nameof(filterWheelMediator));
            this.onFilterWheelChanged = onFilterWheelChanged ?? throw new ArgumentNullException(nameof(onFilterWheelChanged));
            this.filterWheelMediator.RegisterConsumer(this);
        }

        public FilterWheelInfo DeviceInfo { get; private set; }

        public void UpdateDeviceInfo(FilterWheelInfo deviceInfo) {
            DeviceInfo = deviceInfo;
            onFilterWheelChanged();
        }

        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            filterWheelMediator.RemoveConsumer(this);
        }
    }
}
