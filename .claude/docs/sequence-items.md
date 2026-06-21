# Sequence Instructions

Read this when adding or editing a NINA `SequenceItem` instruction.

Full implementation of a `SequenceItem`:

```csharp
[ExportMetadata("Name", "My Action")]
[ExportMetadata("Description", "...")]
[ExportMetadata("Icon", "MySVG")]
[ExportMetadata("Category", "Lbl_SequenceCategory_Focuser")]
[Export(typeof(ISequenceItem))]
[JsonObject(MemberSerialization.OptIn)]
public class MyAction : SequenceItem, IValidatable {

    [ImportingConstructor]
    public MyAction(ICameraMediator cameraMediator, IFocuserMediator focuserMediator, ...)
    { ... }

    // Clone constructor (required)
    private MyAction(MyAction cloneMe) : this(...) { CopyMetaData(cloneMe); }
    public override object Clone() => new MyAction(this);

    // Validation
    private IList<string> issues = new List<string>();
    public IList<string> Issues {
        get => issues;
        set { issues = value; RaisePropertyChanged(); }
    }

    public bool Validate() {
        var i = new List<string>();
        if (!cameraMediator.GetInfo().Connected)
            i.Add(Loc.Instance["LblCameraNotConnected"]);
        Issues = i;
        return i.Count == 0;
    }

    // Execution
    public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        var result = await DoWork(token);
        if (!result) throw new SequenceEntityFailedException("My action failed");
    }

    public override TimeSpan GetEstimatedDuration() => TimeSpan.FromSeconds(estimatedSeconds);

    public override string ToString() =>
        $"Category: {Category}, Item: {nameof(MyAction)}";
}
```
