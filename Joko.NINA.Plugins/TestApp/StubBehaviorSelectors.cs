#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Image.Interfaces;
using NINA.Joko.Plugins.HocusFocus.Interfaces;
using NINA.Joko.Plugins.HocusFocus.AutoFocus.Replay;
using NINA.Joko.Plugins.HocusFocus.StarDetection.PerFilter;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataPoint = OxyPlot.DataPoint;

namespace TestApp {

    /// <summary>
    /// Minimal <see cref="IPluggableBehaviorSelector{T}"/> returning a fixed behavior (or null), used only to
    /// satisfy the <c>ImageDataFactory</c> constructor when loading XISF/FITS files headlessly.
    /// </summary>
    internal class StubBehaviorSelector<T> : IPluggableBehaviorSelector<T> where T : class {
        private readonly T behavior;

        public StubBehaviorSelector(T behavior = null) {
            this.behavior = behavior;
        }

        public AsyncObservableCollection<T> Behaviors { get; set; } = new AsyncObservableCollection<T>();

        public T SelectedBehavior { get; set; }

#pragma warning disable CS0067 // event is part of the interface contract but unused in this stub
        public event EventHandler SelectedBehaviorChanged;
#pragma warning restore CS0067

        public T GetBehavior() => behavior;

        public Type GetInterfaceType() => typeof(T);

        public void AddBehavior(object b) {
            if (b is T typed) {
                Behaviors.Add(typed);
            }
        }
    }

    /// <summary>
    /// No-op <see cref="IStarDetection"/> whose only meaningful member is <see cref="CreateAnalysis"/> —
    /// the single behavior <c>BaseImageData</c>'s constructor invokes during image decode. Star detection is
    /// driven separately by the HocusFocus <c>StarDetector</c>, so detection/analysis methods are never used.
    /// </summary>
    internal class StubStarDetection : IStarDetection {
        public string Name => "TestApp.StubStarDetection";
        public string ContentId => GetType().FullName;

        public IStarDetectionAnalysis CreateAnalysis() => new StarDetectionAnalysis();

        public Task<StarDetectionResult> Detect(IRenderedImage image, System.Windows.Media.PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token)
            => throw new NotSupportedException("StubStarDetection does not perform detection");

        public void UpdateAnalysis(IStarDetectionAnalysis analysis, StarDetectionParams p, StarDetectionResult result)
            => throw new NotSupportedException("StubStarDetection does not perform analysis");
    }

    /// <summary>
    /// Headless <see cref="IFocuserMediator"/> whose only meaningful member is <see cref="GetInfo"/> — the single
    /// call <c>HocusFocusStarDetection.BuildResultHeader</c> makes, stamping the result's informational
    /// <c>FocuserPosition</c>. The harness carries each frame's real focuser position in <c>RunFrame</c> and never
    /// reads the result's copy, so a default (disconnected) <see cref="FocuserInfo"/> is correct rather than merely
    /// convenient. Everything else throws: a headless run has no focuser and must never pretend otherwise.
    /// </summary>
    internal sealed class StubFocuserMediator : IFocuserMediator {
        private static NotSupportedException Unsupported([CallerMemberName] string member = null)
            => new NotSupportedException($"StubFocuserMediator has no focuser; {member} is not available headlessly.");

        public FocuserInfo GetInfo() => new FocuserInfo();

        public void RegisterHandler(IFocuserVM handler) => throw Unsupported();

        public void RegisterConsumer(IFocuserConsumer consumer) => throw Unsupported();

        public void RemoveConsumer(IFocuserConsumer consumer) => throw Unsupported();

        public Task<IList<string>> Rescan() => throw Unsupported();

        public Task<bool> Connect() => throw Unsupported();

        public Task Disconnect() => throw Unsupported();

        public void Broadcast(FocuserInfo deviceInfo) => throw Unsupported();

        public string Action(string actionName, string actionParameters) => throw Unsupported();

        public string SendCommandString(string command, bool raw = true) => throw Unsupported();

        public bool SendCommandBool(string command, bool raw = true) => throw Unsupported();

        public void SendCommandBlind(string command, bool raw = true) => throw Unsupported();

        public IDevice GetDevice() => throw Unsupported();

        public void ToggleTempComp(bool tempComp) => throw Unsupported();

        public Task<int> MoveFocuser(int position, CancellationToken ct) => throw Unsupported();

        public Task<int> MoveFocuserRelative(int position, CancellationToken ct) => throw Unsupported();

        public Task<int> MoveFocuserByTemperatureRelative(double temperature, double Slope, CancellationToken ct) => throw Unsupported();

        public void BroadcastSuccessfulAutoFocusRun(AutoFocusInfo info) => throw Unsupported();

        public void BroadcastNewAutoFocusPoint(DataPoint dataPoint) => throw Unsupported();

        public void BroadcastUserFocused(FocuserInfo info) => throw Unsupported();

        public void BroadcastAutoFocusRunStarting() => throw Unsupported();

#pragma warning disable CS0067 // events are part of the interface contract but never raised in this stub
        public event Func<object, EventArgs, Task> Connected;

        public event Func<object, EventArgs, Task> Disconnected;
#pragma warning restore CS0067
    }

    /// <summary>
    /// Headless <see cref="IPerFilterStarDetectionStore"/> that is always DISABLED. Per-filter star detection
    /// resolves the TARGET filter's settings snapshot from the live filter wheel, which a headless run has no access
    /// to — so the harness runs the profile-level settings and says so, rather than inventing a filter.
    ///
    /// <para><b>That is a real divergence when the user has the feature ON</b>: live would resolve the captured
    /// filter's snapshot (<c>HocusFocusStarDetection.ResolveEffectiveOptions</c>) while the harness silently uses
    /// profile-level settings, so a headless "baseline" would be measured with settings the app would not have
    /// used. Pass the plugin's options accessor and this warns at construction — the flag is read by
    /// <see cref="PerFilterStarDetectionStore.IsEnabledForActiveProfile"/>, i.e. from the class that owns the
    /// persisted key, not from a copy of the key here.</para>
    /// </summary>
    internal sealed class StubPerFilterStarDetectionStore : IPerFilterStarDetectionStore {

        public StubPerFilterStarDetectionStore(IPluginOptionsAccessor optionsAccessor = null) {
            if (optionsAccessor == null || !PerFilterStarDetectionStore.IsEnabledForActiveProfile(optionsAccessor)) {
                return;
            }
            var message =
                "WARNING: per-filter star detection is ENABLED in this profile, but a headless run has no filter wheel. " +
                "The live app would resolve the captured filter's settings snapshot for these frames; this run uses the " +
                "PROFILE-LEVEL star detection settings instead, so its numbers may not match what the app would produce. " +
                "Compare against the target filter's settings, or disable the feature for the comparison.";
            Console.Error.WriteLine(message);
            Logger.Warning(message);
        }

        public bool Enabled {
            get => false;
            set { /* headless never enables per-filter resolution */ }
        }

#pragma warning disable CS0067 // events are part of the interface contract but never raised in this stub
        public event EventHandler EnabledChanged;

        public event EventHandler<PerFilterSnapshotChangedEventArgs> SnapshotChanged;
#pragma warning restore CS0067

        public StarDetectionSettingsSnapshot TryGetSnapshot(string filterName) => null;

        public StarDetectionSettingsSnapshot GetOrSeedSnapshot(string filterName)
            => throw new NotSupportedException("Per-filter star detection is unavailable headlessly (no filter wheel).");

        public void UpsertSnapshot(string filterName, StarDetectionSettingsSnapshot snapshot)
            => throw new NotSupportedException("Per-filter star detection is unavailable headlessly (no filter wheel).");

        public IReadOnlyList<string> GetKnownFilterNames() => Array.Empty<string>();
    }
}
