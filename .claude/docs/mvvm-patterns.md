# MVVM, Async, Error Handling & Logging

Read this when writing or editing a ViewModel, command, observable property, mediator consumer, async work, custom exception, or log statement.

## MVVM Patterns

### Base Classes

| Base | Use When |
|---|---|
| `BaseINPC` | Options objects, plain observable models |
| `DockableVM` | Dockable panel ViewModels |
| `SequenceItem` | NINA sequence instructions |

### Commands

```csharp
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;
// AsyncRelayCommand also from CommunityToolkit.Mvvm.Input

// Async command
LoadSavedAutoFocusRunCommand = new AsyncRelayCommand(() => Task.Run(() => LoadData()));

// Sync command with canExecute
ClearAnalysesCommand = new RelayCommand(ClearAnalyses, canExecute: (o) => !AnalysisRunning());

// Cancel button for an async command
CancelCommand = new RelayCommand(Cancel);
```

Use `AsyncCommand<T>` from `NINA.Core.Utility` only when the NINA-native type is required. Default to `AsyncRelayCommand` from CommunityToolkit.Mvvm.

### Observable Properties

```csharp
private int stepCount;
public int StepCount {
    get => stepCount;
    set {
        if (stepCount != value) {
            stepCount = value;
            optionsAccessor.SetValueInt32(nameof(StepCount), stepCount);
            RaisePropertyChanged();
        }
    }
}

// Computed/dependent property — no backing field, no persistence
public string StepCountHint => $"(auto: {StepCount})";
// Notify it from the property it depends on:
// RaisePropertyChanged(nameof(StepCountHint));
```

### Mediator Consumer Registration

```csharp
public class MyVM : DockableVM, ICameraConsumer, IFocuserConsumer {
    public MyVM(ICameraMediator cameraMediator, IFocuserMediator focuserMediator, ...) {
        cameraMediator.RegisterConsumer(this);
        focuserMediator.RegisterConsumer(this);
    }
}
```

## Async Patterns

```csharp
// Threadpool work from a command
LoadCommand = new AsyncRelayCommand(() => Task.Run(() => LoadData(path)));

// Sequence Execute always takes progress + token
public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
    token.ThrowIfCancellationRequested();
    await DoWork(token);
}

// Progress reporting
private readonly IProgress<ApplicationStatus> progress;
// Created via: ProgressFactory.Create(applicationStatusMediator, "Aberration Inspector")
```

## UI Threading & Dispatcher Marshaling

VMs are driven from **three** kinds of threads: the UI thread (user actions, ctor), awaited task
continuations (AF/analysis/calibration runs), and **background timers/broadcasts** (NINA's DeviceUpdateTimer
device-info push; the tilt-device connection service's `cp` poll). Getting the marshaling wrong causes either a
cross-thread **crash** or a **deadlock/hang** — both have bitten this codebase. Rules:

- **`RaisePropertyChanged` is auto-marshaled by WPF data binding; `ICommand` requery is NOT.**
  `IRelayCommand.NotifyCanExecuteChanged()` raises through `CanExecuteChangedEventManager`, which **throws
  `InvalidOperationException` ("The calling thread cannot access this object…") off the UI thread.** Any
  `NotifyCanExecuteChanged()` reachable from a non-UI thread must be marshaled.

- **Marshal via `IApplicationDispatcher` — and default to the NON-blocking `PostSynchronizationContext`
  (`BeginInvoke`).** Reach for the blocking `DispatchSynchronizationContext` (`Invoke`) **only** when the action
  must complete before the method returns (e.g. the next step reads the value) **and** the caller is a task the
  UI awaits — never a background timer/poll. A blocking `Invoke` from a background thread onto a busy or
  tearing-down UI thread **deadlocks** (the UI thread may be laying out a tab, or, at shutdown, awaiting the very
  timer that is trying to Invoke). Both dispatch methods run inline when already on the UI thread (and in tests,
  where the dispatcher is null) — so UI-thread callers and unit tests are unaffected either way.
  - Precedents: `InspectorVM.RefreshCommandStates` / `RebuildTiltGuidance` **Post** (they are reachable from the
    `cp`-poll thread via `tiltAdapterOptions.PropertyChanged`); `NotifyReviewFramesAvailabilityChanged` **Dispatch**
    (only ever called from an awaited analysis task).

- **Watch what fires a subscribed `PropertyChanged` on a background thread.** An options setter written from a
  poll/broadcast thread (e.g. `EatTiltMotionController` persists `TiltDeviceShadowPositions` during a `cp` poll)
  runs every subscriber — including a `… .PropertyChanged += (s,e) => Rebuild()` handler — on **that** thread. So
  `Rebuild()` must be background-safe (no blocking Invoke).

- **Shared device / simulator state belongs in the shared options, not a per-VM field.** State that both a
  background/automation writer and the UI mutate (e.g. per-screw net counters driven by *both* manual clicks and
  the `SimulatedTiltActuator`) must live on the shared `*Options` object and be observed via INPC, so every VM
  instance and every writer stay in lockstep. A per-VM field silently desyncs when a second VM or an automated
  path bypasses it. (See `ICameraSimulatorOptions.SimNetAxialMicrons`.)

- **Status-bar lines don't clear themselves.** `IProgress<ApplicationStatus>` shows the *last* reported status
  until something reports an empty one. After a transient operation (a device move, a recovery sweep), clear it in
  a `finally`: `progress.Report(new ApplicationStatus { Status = string.Empty })`, or the last message lingers in
  NINA's bottom-left corner after the command finishes.

## Error Handling

Custom domain exceptions extend `Exception` directly:
```csharp
public class TooManyFailedMeasurementsException : Exception {
    public int NumFailures { get; }
    public TooManyFailedMeasurementsException(int numFailures)
        : base("Too many failed measurements") { NumFailures = numFailures; }
}
```

Sequence failures: throw `SequenceEntityFailedException`.
Validation errors: populate `Issues` list and return `false` from `Validate()`.

## Logging

```csharp
using Logger = NINA.Core.Utility.Logger;

Logger.Trace($"Frame {frame}: measured {hfr:F3}");
Logger.Debug($"State transition: {oldState} -> {newState}");
Logger.Info("Auto-focus run completed");
Logger.Warning("Retry attempt due to measurement failure");
Logger.Error("Focus failed");
Logger.Error(ex, "Unhandled exception in focus engine");
```

| Level | When |
|---|---|
| Trace | Frame-by-frame measurements, inner loops |
| Debug | State transitions, intermediate results |
| Info | Major milestones (run start/complete) |
| Warning | Retries, fallbacks, non-fatal conditions |
| Error | Failures and exceptions |
