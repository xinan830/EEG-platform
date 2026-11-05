namespace BrainPlatform.Desktop.Modules.Montages.Domain;

public sealed class WorkspaceMontageSelection
{
    private readonly object gate = new();
    private readonly IReadOnlyList<MontageProfile> availableMontages;
    private WorkspaceMontageSelectionSnapshot current;

    public WorkspaceMontageSelection(
        string recordingId,
        MontageProfile? acquisitionMontage,
        IEnumerable<MontageProfile> availableMontages)
    {
        if (string.IsNullOrWhiteSpace(recordingId))
            throw new ArgumentException("工作区导联选择必须绑定录音身份。", nameof(recordingId));

        RecordingId = recordingId;
        AcquisitionMontage = acquisitionMontage;
        this.availableMontages = Array.AsReadOnly(availableMontages?.ToArray()
            ?? throw new ArgumentNullException(nameof(availableMontages)));
        if (acquisitionMontage is not null && !ContainsReference(acquisitionMontage))
            throw new ArgumentException("采集导联必须属于当前录音的兼容目录。", nameof(availableMontages));
        current = new WorkspaceMontageSelectionSnapshot(recordingId, acquisitionMontage, 0);
    }

    public event EventHandler<WorkspaceMontageSelectionChangedEventArgs>? SelectionChanged;

    public string RecordingId { get; }
    public MontageProfile? AcquisitionMontage { get; }
    public IReadOnlyList<MontageProfile> AvailableMontages => availableMontages;
    public MontageProfile? SelectedMontage => Capture().SelectedMontage;
    public long Revision => Capture().Revision;

    public WorkspaceMontageSelectionSnapshot Capture() => Volatile.Read(ref current);

    public bool Select(MontageProfile? montage)
    {
        if (montage is not null && !ContainsReference(montage))
            throw new InvalidOperationException("所选导联与此录音的已验证导联目录不匹配。");

        WorkspaceMontageSelectionSnapshot previous;
        WorkspaceMontageSelectionSnapshot selected;
        lock (gate)
        {
            previous = current;
            if (ReferenceEquals(previous.SelectedMontage, montage)) return false;
            selected = new WorkspaceMontageSelectionSnapshot(RecordingId, montage, previous.Revision + 1);
            Volatile.Write(ref current, selected);
        }
        SelectionChanged?.Invoke(this, new(previous, selected));
        return true;
    }

    private bool ContainsReference(MontageProfile montage) =>
        availableMontages.Any(item => ReferenceEquals(item, montage));
}

public sealed record WorkspaceMontageSelectionSnapshot(
    string RecordingId,
    MontageProfile? SelectedMontage,
    long Revision);

public sealed class WorkspaceMontageSelectionChangedEventArgs(
    WorkspaceMontageSelectionSnapshot previous,
    WorkspaceMontageSelectionSnapshot current) : EventArgs
{
    public WorkspaceMontageSelectionSnapshot Previous { get; } = previous;
    public WorkspaceMontageSelectionSnapshot Current { get; } = current;
}
