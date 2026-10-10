namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Workspace-owned analysis window policy, supplied by the algorithm catalog.</summary>
public sealed class ReviewAnalysisSettings : ObservableObject
{
    private double windowSeconds = 5;
    public IReadOnlyList<double> WindowOptions { get; private set; } = [];
    public double WindowSeconds
    {
        get => windowSeconds;
        set
        {
            if (!WindowOptions.Contains(value)) return;
            SetProperty(ref windowSeconds, value);
        }
    }

    public void Initialize(DynamicAnalysisPolicy policy)
    {
        WindowOptions = policy.WindowOptionsSeconds;
        WindowSeconds = WindowOptions.Contains(windowSeconds) ? windowSeconds : policy.DefaultWindowSeconds;
        RaisePropertyChanged(nameof(WindowOptions));
        RaisePropertyChanged(nameof(WindowSeconds));
    }
}

public sealed record ReviewAnalysisSettingsSnapshot(
    double HighPassHz, double LowPassHz, double NotchHz, double WindowSeconds);
