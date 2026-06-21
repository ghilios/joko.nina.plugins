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
