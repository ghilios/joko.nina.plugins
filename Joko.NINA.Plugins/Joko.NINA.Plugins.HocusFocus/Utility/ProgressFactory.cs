#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Model;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    public static class ProgressFactory {

        public static IProgress<ApplicationStatus> Create(IApplicationStatusMediator applicationStatusMediator, string sourceName) {
            var synchronizationContext = Application.Current?.Dispatcher != null
                ? new DispatcherSynchronizationContext(Application.Current.Dispatcher)
                : null;

            if (SynchronizationContext.Current == synchronizationContext) {
                return new Progress<ApplicationStatus>(p => {
                    p.Source = sourceName;
                    applicationStatusMediator.StatusUpdate(p);
                });
            } else {
                IProgress<ApplicationStatus> progressTemp = null;
                synchronizationContext.Send(_ => {
                    progressTemp = new Progress<ApplicationStatus>(p => {
                        p.Source = sourceName;
                        applicationStatusMediator.StatusUpdate(p);
                    });
                }, null);
                return progressTemp;
            }
        }
    }
}