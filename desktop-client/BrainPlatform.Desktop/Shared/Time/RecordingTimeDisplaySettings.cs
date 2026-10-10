namespace BrainPlatform.Desktop.Shared.Time;

public enum RecordingTimeDisplayMode { Relative, Clock }

/// <summary>Workspace display preference; never part of an analysis request.</summary>
public sealed class RecordingTimeDisplaySettings : ObservableObject
{
    private RecordingTimeDisplayMode mode;

    public RecordingTimeDisplayMode Mode
    {
        get => mode;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            SetProperty(ref mode, value);
        }
    }
}
