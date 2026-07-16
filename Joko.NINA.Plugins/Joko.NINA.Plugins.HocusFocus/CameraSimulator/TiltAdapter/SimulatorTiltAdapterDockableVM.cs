#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NINA.Joko.Plugins.HocusFocus.CameraSimulator.TiltAdapter {

    /// <summary>
    /// The Imaging-tab host for the virtual tilt-adapter panel, so the adapter can be operated beside the
    /// Aberration Inspector during a calibration run rather than through the camera's setup dialog. It hosts the
    /// same control the setup dialog does (<c>HocusFocus_SimTiltAdapter_Panel</c>), backed by its own
    /// <see cref="SimulatedTiltAdapterVM"/>: the two hosts share the state that matters — the injected plane and
    /// the adapter geometry — because both VMs read and write the one options singleton.
    /// </summary>
    /// <remarks>
    /// This panel is off by default and gated on <see cref="ICameraSimulatorOptions.ShowSimulatorTiltAdapterPanel"/>,
    /// which NINA only lets us honour halfway. Verified against NINA's assemblies, not assumed:
    ///
    /// <para>The export MUST be unconditional. MEF metadata is static, so there is no conditional export; and
    /// throwing from the constructor to refuse the part would fail the whole plugin's composition, not just this
    /// dockable.</para>
    ///
    /// <para>The 30x30 sidebar button therefore always appears, even with the option off, and it cannot be
    /// removed — not even by restarting. <c>DockManagerVM</c> builds the dockable set once at startup into a plain
    /// <c>List&lt;IDockableVM&gt;</c> (not observable) with private setters on an <c>internal</c> class, and
    /// <c>IDockManagerVM</c> is not composed into the plugin container; the button itself has no
    /// <c>Visibility</c> binding, only <c>IsChecked</c>. What we CAN do is make the button inert: closing the
    /// panel is honest (<c>IsVisible</c> is two-way bound to the anchorable), and <see cref="Hide"/> is the only
    /// route the button has to reopen it. See docs/camera-simulator-interactive-tilt-design.md, "Feasibility".</para>
    /// </remarks>
    [Export(typeof(IDockableVM))]
    public class SimulatorTiltAdapterDockableVM : DockableVM {
        private readonly ICameraSimulatorOptions options;

        [ImportingConstructor]
        public SimulatorTiltAdapterDockableVM(IProfileService profileService) : base(profileService) {
            Title = "Simulator Tilt Adapter";

            // Mirrors HocusFocusSimulatorCameraProvider: the plugin's singleton in production, a throwaway when
            // no plugin instance has been constructed. Never null — the panel VM rejects that, and an exception
            // here would take the whole plugin down with it.
            options = HocusFocusPlugin.CameraSimulatorOptions ?? new CameraSimulatorOptions(profileService);

            // The wizard's icon: this panel and the Tilt Adapter Wizard are the same subject, one measuring a real
            // adapter and one driving a synthetic one.
            var dict = new ResourceDictionary {
                Source = new Uri("NINA.Joko.Plugins.HocusFocus;component/TiltAdapterWizard/DataTemplates.xaml",
                                 UriKind.RelativeOrAbsolute)
            };
            ImageGeometry = (GeometryGroup)dict["TiltAdapterWizardSVG"];
            ImageGeometry.Freeze();

            TiltAdapterVM = new SimulatedTiltAdapterVM(options);
            options.PropertyChanged += OptionsChanged;

            // The gate CANNOT run here. NINA's InitializeAvalonDockLayout sets every anchorable IsVisible=false and
            // then restores from the saved <profileId>.dock.config, so a constructor-time value is overwritten and
            // a panel left open in a previous session would reappear. Deferring to the first idle puts us after
            // that restore. Application.Current is null outside NINA (unit tests), where there is nothing to gate.
            Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(() => {
                    if (!Enabled) {
                        IsVisible = false;
                    }
                }));
        }

        /// <summary>The panel's VM. Its own instance — see the type remarks on what the two hosts do and do not share.</summary>
        public SimulatedTiltAdapterVM TiltAdapterVM { get; }

        private bool Enabled => options.ShowSimulatorTiltAdapterPanel;

        /// <summary>Turning the option off closes the panel immediately, rather than waiting for a restart.</summary>
        private void OptionsChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(ICameraSimulatorOptions.ShowSimulatorTiltAdapterPanel) && !Enabled) {
                IsVisible = false;
            }
        }

        /// <summary>
        /// NINA's sidebar button toggles the panel through here (the base implementation flips
        /// <c>IsVisible</c>). While the option is off we swallow the open, which is what makes the button we
        /// cannot remove into a button that does nothing.
        /// </summary>
        public override void Hide(object o) {
            if (!Enabled) {
                IsVisible = false;
                return;
            }
            base.Hide(o);
        }
    }
}
