using System.Globalization;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Modules.Review.ViewModels;

public sealed record ReviewScalarPoint(double TimeSeconds, double StartSeconds, double EndSeconds,
    double? Value, string State, string Quality, string Failure,
    System.Text.Json.JsonElement? Evidence, System.Text.Json.JsonElement? Provenance,
    string? EstimateSource = null, double? PeakHz = null, double? CogHz = null, bool SplitPeak = false);

/// <summary>Review-only chart projection; all values and validation come from the algorithm module.</summary>
public sealed class ReviewScalarAnalysisViewModel(string unit,
    Func<DynamicWindowResponse, string?, string?, IReadOnlyList<ReviewScalarPoint>> project,
    string algorithmId = "") : ObservableObject
{
    private IReadOnlyList<ReviewScalarPoint> points = [];
    private string statusText = "尚未准备。";
    private string currentWindowRangeText = "分析范围不可用";
    private string latestValueText = "--";
    private string detailText = "";
    private string iapfDisplayMode = ScalarTrendChartStyle.IapfDisplayModeCog;

    public IReadOnlyList<ReviewScalarPoint> Points { get => points; private set => SetProperty(ref points, value); }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public string CurrentWindowRangeText { get => currentWindowRangeText; private set => SetProperty(ref currentWindowRangeText, value); }
    public string LatestValueText { get => latestValueText; private set => SetProperty(ref latestValueText, value); }
    public string DetailText { get => detailText; private set => SetProperty(ref detailText, value); }
    public string Unit { get; } = unit;
    public bool IsIapf => string.Equals(algorithmId, "iapf", StringComparison.OrdinalIgnoreCase);

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
            RefreshSummary();
        }
    }

    public bool IsIapfCogMode => IsIapf && iapfDisplayMode == ScalarTrendChartStyle.IapfDisplayModeCog;
    public bool IsIapfCompareMode => IsIapf && iapfDisplayMode == ScalarTrendChartStyle.IapfDisplayModeCompare;
    public bool IsIapfPeakMode => IsIapf && iapfDisplayMode == ScalarTrendChartStyle.IapfDisplayModePeak;

    public void Apply(WindowPreviewResponse? response, string? primaryChannel, string? secondaryChannel)
    {
        if (response is null)
        {
            Points = [];
            StatusText = "尚未准备。";
            CurrentWindowRangeText = "分析范围不可用";
            LatestValueText = "--";
            DetailText = "";
            return;
        }

        var startSeconds = response.WindowStartSeconds ?? response.DynamicResponse?.Range?.StartSeconds;
        var endSeconds = response.WindowEndSeconds ?? response.DynamicResponse?.Range?.EndSeconds;
        CurrentWindowRangeText = startSeconds is double start && endSeconds is double end &&
            double.IsFinite(start) && double.IsFinite(end) && end >= start
                ? $"{start:0.###}–{end:0.###} s" : "分析范围不可用";

        if (response.DynamicResponse is not { } raw)
        {
            Points = [];
            StatusText = "缺少可验证的算法结果。";
            LatestValueText = "--";
            DetailText = "";
            return;
        }

        try
        {
            var incoming = project(raw, primaryChannel, secondaryChannel);
            Points = points.Where(existing => incoming.All(point => point.TimeSeconds != existing.TimeSeconds))
                .Concat(incoming).OrderBy(point => point.TimeSeconds).TakeLast(256).ToArray();
            RefreshSummary();
            var currentFailure = incoming.LastOrDefault()?.Failure;
            StatusText = response.Status == "available" ? "" :
                !string.IsNullOrWhiteSpace(currentFailure) ? currentFailure :
                response.ReasonCodes.Count == 0 ? $"窗口状态：{response.Status}" : string.Join("、", response.ReasonCodes);
        }
        catch (InvalidOperationException exception)
        {
            Points = [];
            StatusText = $"结果无法展示：{exception.Message}";
            LatestValueText = "--";
            DetailText = "";
        }
    }

    private void RefreshSummary()
    {
        var latestValid = Points.LastOrDefault(point =>
            point.State is "Complete" or "Partial" &&
            ((point.Value is { } v && double.IsFinite(v)) || (point.CogHz is { } c && double.IsFinite(c))));
        if (IsIapf)
        {
            LatestValueText = latestValid is not null
                ? ScalarTrendChartStyle.FormatIapfLatestValue(iapfDisplayMode, latestValid.Value, latestValid.CogHz, Unit)
                : "--";
            var latestPoint = Points.LastOrDefault();
            DetailText = latestPoint is not null
                ? ScalarTrendChartStyle.FormatIapfSummaryForMode(
                    iapfDisplayMode, latestPoint.EstimateSource, latestPoint.Value, latestPoint.PeakHz, latestPoint.CogHz, latestPoint.SplitPeak) ?? ""
                : "";
            return;
        }
        LatestValueText = latestValid?.Value is { } latestVal ? FormatValue(latestVal) : "--";
        DetailText = "";
    }

    private string FormatValue(double value)
    {
        var formatted = value.ToString("0.##", CultureInfo.InvariantCulture);
        var displayUnit = ScalarTrendChartStyle.FormatDisplayUnit(Unit);
        return string.IsNullOrEmpty(displayUnit) ? formatted : $"{formatted} {displayUnit}";
    }
}
