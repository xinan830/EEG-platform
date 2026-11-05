namespace BrainPlatform.Desktop.Modules.Montages.Domain;

public sealed class WorkspaceMontageSelection<TMontage> where TMontage : class
{
    private readonly object gate = new();
    private readonly IReadOnlyList<TMontage> availableMontages;
    private WorkspaceMontageSelectionSnapshot<TMontage> current;

    public WorkspaceMontageSelection(
        string recordingId,
        TMontage? acquisitionMontage,
        IEnumerable<TMontage> availableMontages)
    {
        if (string.IsNullOrWhiteSpace(recordingId))
            throw new ArgumentException("工作区导联选择必须绑定录音身份。", nameof(recordingId));

        RecordingId = recordingId;
        AcquisitionMontage = acquisitionMontage;
        this.availableMontages = Array.AsReadOnly(availableMontages?.ToArray()
            ?? throw new ArgumentNullException(nameof(availableMontages)));
        if (acquisitionMontage is not null && !ContainsReference(acquisitionMontage))
            throw new ArgumentException("采集导联必须属于当前录音的兼容目录。", nameof(availableMontages));
        current = new WorkspaceMontageSelectionSnapshot<TMontage>(recordingId, acquisitionMontage, 0);
    }

    public event EventHandler<WorkspaceMontageSelectionChangedEventArgs<TMontage>>? SelectionChanged;

    public string RecordingId { get; }
    public TMontage? AcquisitionMontage { get; }
    public IReadOnlyList<TMontage> AvailableMontages => availableMontages;
    public TMontage? SelectedMontage => Capture().SelectedMontage;
    public long Revision => Capture().Revision;

    public WorkspaceMontageSelectionSnapshot<TMontage> Capture() => Volatile.Read(ref current);

    public bool Select(TMontage? montage)
    {
        if (montage is not null && !ContainsReference(montage))
            throw new InvalidOperationException("所选导联与此录音的已验证导联目录不匹配。");

        WorkspaceMontageSelectionSnapshot<TMontage> previous;
        WorkspaceMontageSelectionSnapshot<TMontage> selected;
        lock (gate)
        {
            previous = current;
            if (ReferenceEquals(previous.SelectedMontage, montage)) return false;
            selected = new WorkspaceMontageSelectionSnapshot<TMontage>(RecordingId, montage, previous.Revision + 1);
            Volatile.Write(ref current, selected);
        }
        SelectionChanged?.Invoke(this, new(previous, selected));
        return true;
    }

    private bool ContainsReference(TMontage montage) =>
        availableMontages.Any(item => ReferenceEquals(item, montage));
}

public sealed record WorkspaceMontageSelectionSnapshot<TMontage>(
    string RecordingId,
    TMontage? SelectedMontage,
    long Revision);

public sealed class WorkspaceMontageSelectionChangedEventArgs<TMontage>(
    WorkspaceMontageSelectionSnapshot<TMontage> previous,
    WorkspaceMontageSelectionSnapshot<TMontage> current) : EventArgs
{
    public WorkspaceMontageSelectionSnapshot<TMontage> Previous { get; } = previous;
    public WorkspaceMontageSelectionSnapshot<TMontage> Current { get; } = current;
}
