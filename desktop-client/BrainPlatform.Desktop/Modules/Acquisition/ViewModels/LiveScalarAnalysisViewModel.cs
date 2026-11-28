using System.Collections.ObjectModel;
using System.Globalization;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>Unique state owner for one live scalar trend card (IAPF, Theta/Beta, or Classic BrainBeat).</summary>
public sealed class LiveScalarAnalysisViewModel : ObservableObject
{
    private const int MaxRetainedPoints = 600;
    private readonly Action<LiveScalarAnalysisViewModel>? onParametersChanged;
    private double startSeconds;
    private double endSeconds = 1d;
    private string latestValueText = "--";
    private string windowText = "";
    private string statusText;
    private string detailText = "";
    private string frequencyMode = "standard";
    private string iapfDisplayMode = ScalarTrendChartStyle.IapfDisplayModeCog;
    private LiveScalarUpdate? latestUpdate;
    private bool hasResult;

    public LiveScalarAnalysisViewModel(
        string algorithmId,
        string unit,
        double windowSeconds = 20d,
        Action<LiveScalarAnalysisViewModel>? onParametersChanged = null)
    {
        AlgorithmId = algorithmId;
        Unit = unit;
        WindowSeconds = windowSeconds;
        this.onParametersChanged = onParametersChanged;
        statusText = DefaultWarmupMessage();
    }

    public string AlgorithmId { get; }
    public string Unit { get; }
    public double WindowSeconds { get; }
    public bool IsIapf => string.Equals(AlgorithmId, "iapf", StringComparison.OrdinalIgnoreCase);
    public bool IsThetaBeta => string.Equals(AlgorithmId, "theta_beta", StringComparison.OrdinalIgnoreCase);
    public bool IsBrainbeat => string.Equals(AlgorithmId, "brainbeat", StringComparison.OrdinalIgnoreCase);
    public ObservableCollection<LiveScalarPoint> Points { get; } = [];

    public double StartSeconds
    {
        get => startSeconds;
        private set => SetProperty(ref startSeconds, value);
    }

    public double EndSeconds
    {
        get => endSeconds;
        private set => SetProperty(ref endSeconds, value);
    }

    public string LatestValueText
    {
        get => latestValueText;
        private set => SetProperty(ref latestValueText, value);
    }

    public string WindowText
    {
        get => windowText;
        private set => SetProperty(ref windowText, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public string DetailText
    {
        get => detailText;
        private set => SetProperty(ref detailText, value);
    }

    public bool HasResult
    {
        get => hasResult;
        private set => SetProperty(ref hasResult, value);
    }

    public string FrequencyMode
    {
        get => frequencyMode;
        set
        {
            var normalized = string.Equals(value, "individualized", StringComparison.OrdinalIgnoreCase)
                ? "individualized"
                : "standard";
            if (!SetProperty(ref frequencyMode, normalized))
                return;
            onParametersChanged?.Invoke(this);
        }
    }

    public string IapfDisplayMode
    {
        get => iapfDisplayMode;
        set
        {
            var normalized = ScalarTrendChartStyle.NormalizeIapfDisplayMode(value);
            if (!SetProperty(ref iapfDisplayMode, normalized))
                return;
            RaisePropertyChanged(nameof(IsIapfCogMode));
            RaisePropertyChanged(nameof(IsIapfCompareMode));
            RaisePropertyChanged(nameof(IsIapfPeakMode));
            RefreshLatestDisplay();
        }
    }

    public bool IsIapfCogMode => IsIapf && iapfDisplayMode == ScalarTrendChartStyle.IapfDisplayModeCog;
    public bool IsIapfCompareMode => IsIapf && iapfDisplayMode == ScalarTrendChartStyle.IapfDisplayModeCompare;
    public bool IsIapfPeakMode => IsIapf && iapfDisplayMode == ScalarTrendChartStyle.IapfDisplayModePeak;

    public void Begin(double? analysisStartSeconds, string? message = null)
    {
        Points.Clear();
        latestUpdate = null;
        StartSeconds = analysisStartSeconds is { } s && double.IsFinite(s) ? Math.Max(0d, s) : 0d;
        EndSeconds = StartSeconds + 1d;
        LatestValueText = "--";
        WindowText = "";
        DetailText = "";
        HasResult = false;
        StatusText = message ?? DefaultWarmupMessage();
    }

    internal void Apply(LiveScalarUpdate update)
    {
        if (update.State == "warming_up")
        {
            StatusText = DefaultWarmupMessage();
            return;
        }

        WindowText = update.StartSeconds is { } start && update.EndSeconds is { } end
            ? $"{start:0.###}–{end:0.###} s"
            : "";

        if (update.TimeSeconds is { } time && double.IsFinite(time))
        {
            var isReady = update.State == "ready" && update.Value is { } val && double.IsFinite(val);
            var point = new LiveScalarPoint(
                time,
                isReady ? update.Value : null,
                isReady ? "Complete" : update.Quality == "gate_failed" ? "Rejected" : "Unavailable",
                update.Quality,
                update.StartSeconds,
                update.EndSeconds,
                update.FailureCode,
                update.Message,
                update.EstimateSource,
                update.PeakHz,
                update.CogHz,
                update.SplitPeak);
            Points.Add(point);
            while (Points.Count > MaxRetainedPoints)
                Points.RemoveAt(0);

            if (Points.Count == 1)
                StartSeconds = Math.Min(StartSeconds, update.StartSeconds ?? time);
            else if (Points.Count > 0 && Points[0].TimeSeconds > StartSeconds)
                StartSeconds = Points[0].WindowStartSeconds ?? Points[0].TimeSeconds;
            EndSeconds = Math.Max(StartSeconds + 1d, time);
        }

        latestUpdate = update;
        RefreshLatestDisplay();
    }

    internal void SetOverloaded(string message) => StatusText = message;

    internal void SetUnavailableConfiguration(string message)
    {
        Points.Clear();
        latestUpdate = null;
        LatestValueText = "--";
        WindowText = "";
        DetailText = "";
        HasResult = false;
        StatusText = message;
    }

    internal void SetFault(string message)
    {
        Points.Clear();
        latestUpdate = null;
        LatestValueText = "--";
        WindowText = "";
        DetailText = "";
        HasResult = false;
        StatusText = message;
    }

    private void RefreshLatestDisplay()
    {
        if (latestUpdate is not { } update)
            return;

        var isReady = update.State == "ready" && update.Value is { } readyValue && double.IsFinite(readyValue);
        if (isReady)
        {
            HasResult = true;
            LatestValueText = IsIapf
                ? ScalarTrendChartStyle.FormatIapfLatestValue(iapfDisplayMode, update.Value, update.CogHz, Unit)
                : FormatValue(update.Value!.Value);
            StatusText = "";
        }
        else
        {
            LatestValueText = "--";
            StatusText = FormatUnavailableStatus(update);
        }

        if (IsIapf)
        {
            var modeSummary = ScalarTrendChartStyle.FormatIapfSummaryForMode(
                iapfDisplayMode, update.EstimateSource, update.Value, update.PeakHz, update.CogHz, update.SplitPeak);
            DetailText = string.IsNullOrWhiteSpace(update.ProcessingSummary)
                ? modeSummary ?? ""
                : string.IsNullOrWhiteSpace(modeSummary)
                    ? update.ProcessingSummary
                    : $"{modeSummary} · {update.ProcessingSummary}";
        }
        else
        {
            DetailText = update.DetailSummary ?? "";
        }
    }

    private string DefaultWarmupMessage() => $"等待可用的 {WindowSeconds:0} 秒窗口";

    private string FormatValue(double value)
    {
        var formatted = value.ToString("0.##", CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(Unit) || string.Equals(Unit, "dimensionless", StringComparison.OrdinalIgnoreCase)
            ? formatted
            : $"{formatted} {Unit}";
    }

    private static string FormatUnavailableStatus(LiveScalarUpdate update)
    {
        if (!string.IsNullOrWhiteSpace(update.FailureCode) && !string.IsNullOrWhiteSpace(update.Message))
            return $"{update.FailureCode}：{update.Message}";
        return update.Message ?? "当前窗口不可用";
    }
}
