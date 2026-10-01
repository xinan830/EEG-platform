using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Psd;

public enum PsdChartKind { Spectrum, BandShare }

public sealed record PsdChartOption(PsdChartKind Kind, string Label);

/// <summary>
/// PSD-only detail state. Shared recording selection and execution remain in
/// the catalog/context so other algorithms can reuse the same workflow.
/// </summary>
public sealed class PsdDetailViewModel : ObservableObject, IAlgorithmQualitySource
{
    private readonly AlgorithmListViewModel catalog;
    private bool isFailureDetailsExpanded;
    private PsdChartOption selectedChart;

    internal PsdDetailViewModel(AlgorithmListViewModel catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        selectedChart = ChartOptions[0];
        catalog.PropertyChanged += OnCatalogPropertyChanged;
        catalog.PsdPreviewState.PropertyChanged += OnPsdPreviewStateChanged;
        if (catalog.Projects is not null)
            catalog.Projects.PropertyChanged += OnProjectsPropertyChanged;
    }

    public AlgorithmListViewModel Catalog => catalog;
    public string AlgorithmId => "psd";
    public IReadOnlyList<PsdChartOption> ChartOptions { get; } =
    [
        new(PsdChartKind.Spectrum, "功率谱密度"),
        new(PsdChartKind.BandShare, "频段功率占比"),
    ];
    public PsdChartOption SelectedChart
    {
        get => selectedChart;
        set
        {
            if (value is null || !ChartOptions.Contains(value) || !SetProperty(ref selectedChart, value)) return;
            RaisePropertyChanged(nameof(IsSpectrumSelected));
            RaisePropertyChanged(nameof(IsBandShareSelected));
        }
    }
    public bool IsSpectrumSelected => SelectedChart.Kind == PsdChartKind.Spectrum;
    public bool IsBandShareSelected => SelectedChart.Kind == PsdChartKind.BandShare;
    public ProjectWorkspaceViewModel? Projects => catalog.Projects;
    public ResearchProject? SelectedProject
    {
        get => catalog.Projects?.SelectedProject;
        set { if (catalog.Projects is not null) catalog.Projects.SelectedProject = value; }
    }
    public AlgorithmCatalogItem? Algorithm => catalog.SelectedAlgorithm;
    public bool IsAvailable => string.Equals(Algorithm?.Id, "psd", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<AlgorithmParameter> Parameters => Algorithm?.Parameters ?? [];
    public bool HasStructuredResult => catalog.PsdPreviewState.HasPreview;
    public ObservableCollection<AlgorithmListViewModel.PsdPreviewPoint?> PreviewPoints => catalog.PsdPreviewState.Points;
    public ObservableCollection<AlgorithmListViewModel.PsdBandSharePoint> BandSharePoints => catalog.PsdPreviewState.BandShares;
    public string ValueUnit => catalog.PsdPreviewState.ValueUnit;
    public string FrequencyRange => catalog.PsdPreviewState.FrequencyRangeText;
    public string ValueRange => catalog.PsdPreviewState.ValueRangeText;
    public IReadOnlyList<string> RegisteredChannels => catalog.RegisteredChannels;
    public IReadOnlyList<string> AnalysisModes => catalog.AnalysisModes;
    public IReadOnlyList<double> DynamicWindowOptions => catalog.DynamicWindowOptions;
    public ProjectRecordingRow? SelectedProjectRecording
    {
        get => catalog.SelectedProjectRecording;
        set => catalog.SelectedProjectRecording = value;
    }
    public string SelectedChannel
    {
        get => catalog.SelectedChannel;
        set => catalog.SelectedChannel = value;
    }
    public string StartSecondsText
    {
        get => catalog.StartSecondsText;
        set => catalog.StartSecondsText = value;
    }
    public string EndSecondsText
    {
        get => catalog.EndSecondsText;
        set => catalog.EndSecondsText = value;
    }
    public string SelectedAnalysisMode
    {
        get => catalog.SelectedAnalysisMode;
        set => catalog.SelectedAnalysisMode = value;
    }
    public double SelectedDynamicWindowSeconds
    {
        get => catalog.SelectedDynamicWindowSeconds;
        set => catalog.SelectedDynamicWindowSeconds = value;
    }
    public string DynamicStepText
    {
        get => catalog.DynamicStepText;
        set => catalog.DynamicStepText = value;
    }
    public RegisteredRecording? RegisteredRecording => catalog.RegisteredRecording;
    public string RunStatus => catalog.RunStatusText;
    public string ResultSummary => catalog.ResultSummaryText;
    public string Provenance => catalog.ProvenanceText;
    public string StructuredPreview => catalog.StructuredPreviewText;
    public ObservableCollection<DynamicWindowRow> DynamicWindowRows => catalog.DynamicWindowRows;
    public StructuredPreviewResponse? StructuredPreviewResult => catalog.PsdPreviewState.StructuredPreview;
    public string QualityText => catalog.PsdPreviewState.StructuredPreview?.Quality is { } quality
        ? AlgorithmResultFormatter.FormatQualityForDisplay(quality)
        : catalog.LastRun?.Error is { } error ? $"运行失败：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}" : "尚未运行分析";
    public string WindowStateText => catalog.PsdPreviewState.StructuredPreview is not { } preview || preview.WindowStateCounts.Count == 0
        ? "暂无窗口状态"
        : string.Join("、", preview.WindowStateCounts.Select(item => $"{AlgorithmResultFormatter.FormatWindowStateForDisplay(item.Key)}：{item.Value}"));
    public AnalysisRunResponse? LastRun => catalog.LastRun;
    public bool IsRunActive => catalog.IsRunActive;
    public string RunIdText => catalog.LastRun?.RunId ?? "未运行";
    public string RequestedRangeText => FormatRange(catalog.LastRun?.RequestedRange);
    public string ActualRangeText => FormatRange(catalog.LastRun?.ActualRange);
    public string ResultTimeText => catalog.LastRun?.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "未运行";
    public string RunStartedAtText => catalog.LastRun?.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "未运行";
    public string RunEndedAtText => catalog.LastRun is { Status: "completed" or "failed" }
        ? catalog.LastRun.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")
        : "未结束";
    public string RunDurationText => catalog.LastRun is null
        ? "未运行"
        : $"{Math.Max(0, (catalog.LastRun.UpdatedAt - catalog.LastRun.CreatedAt).TotalSeconds):0.###} s";
    public double RunProgressPercent => catalog.LastRun?.Status switch
    {
        "completed" => 100.0,
        "queued" or "running" => 0.0,
        _ => 0.0,
    };
    public string RunProgressText => catalog.LastRun?.Status switch
    {
        "completed" => "100%",
        "queued" => "等待中",
        "running" => "运行中",
        "failed" => "失败",
        _ => "未运行",
    };
    public string FailureReasonText => catalog.LastRun?.Error is { } error
        ? $"{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}：{error.Message}"
        : StructuredFailureSummary(catalog.PsdPreviewState.StructuredPreview?.Failure) ?? GetWindowReasonSummary(catalog.PsdPreviewState.StructuredPreview);
    public bool IsFailureDetailsExpanded
    {
        get => isFailureDetailsExpanded;
        private set => SetProperty(ref isFailureDetailsExpanded, value);
    }
    public bool HasFailureDetails => catalog.LastRun?.Error is not null
        || IsFailureObject(catalog.PsdPreviewState.StructuredPreview?.Failure)
        || catalog.PsdPreviewState.StructuredPreview?.Windows.Any(window => WindowState(window) is not "Complete") == true;
    public string FailureDetailsText => BuildFailureDetails(catalog.PsdPreviewState.StructuredPreview, catalog.LastRun?.Error);
    public int UnavailableWindowCount => catalog.PsdPreviewState.StructuredPreview?.WindowStateCounts.TryGetValue("Unavailable", out var count) == true ? count : 0;
    public int RejectedWindowCount => catalog.PsdPreviewState.StructuredPreview?.WindowStateCounts.TryGetValue("Rejected", out var count) == true ? count : 0;
    public string WindowStateSummary => catalog.PsdPreviewState.StructuredPreview is null ? "未产生窗口结果" : WindowStateText;
    public ICommand RegisterCommand => catalog.RegisterCommand;
    public ICommand RunCommand => catalog.RunCommand;
    public ICommand SelectStaticModeCommand => catalog.SelectStaticModeCommand;
    public ICommand SelectDynamicModeCommand => catalog.SelectDynamicModeCommand;
    public bool IsDynamicMode => catalog.IsDynamicMode;
    public bool IsDynamicModeAvailable => catalog.IsDynamicModeAvailable;
    public double DynamicTimeProgressPercent
    {
        get => catalog.DynamicTimeProgressPercent;
        set => catalog.DynamicTimeProgressPercent = value;
    }
    public string DynamicTimeProgressText => catalog.DynamicTimeProgressText;
    public string DynamicCurrentWindowText => catalog.DynamicCurrentWindowText;
    public ICommand ToggleFailureDetailsCommand => new RelayCommand(ToggleFailureDetails);

    private void ToggleFailureDetails() => IsFailureDetailsExpanded = !IsFailureDetailsExpanded;

    private void OnCatalogPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedAlgorithm)
            or nameof(AlgorithmListViewModel.SelectedProjectRecording)
            or nameof(ProjectWorkspaceViewModel.SelectedProject)
            or nameof(AlgorithmListViewModel.RegisteredRecording)
            or nameof(AlgorithmListViewModel.RegisteredChannels)
            or nameof(AlgorithmListViewModel.SelectedChannel)
            or nameof(AlgorithmListViewModel.StartSecondsText)
            or nameof(AlgorithmListViewModel.EndSecondsText)
            or nameof(AlgorithmListViewModel.SelectedAnalysisMode)
            or nameof(AlgorithmListViewModel.SelectedDynamicWindowSeconds)
            or nameof(AlgorithmListViewModel.DynamicStepText)
            or nameof(AlgorithmListViewModel.RunStatusText)
            or nameof(AlgorithmListViewModel.ResultSummaryText)
            or nameof(AlgorithmListViewModel.ProvenanceText)
            or nameof(AlgorithmListViewModel.StructuredPreviewText)
            or nameof(AlgorithmListViewModel.DynamicWindowRows)
            or nameof(AlgorithmListViewModel.IsDynamicMode)
            or nameof(AlgorithmListViewModel.IsDynamicModeAvailable)
            or nameof(AlgorithmListViewModel.DynamicTimeProgressPercent)
            or nameof(AlgorithmListViewModel.DynamicTimeProgressText)
            or nameof(AlgorithmListViewModel.DynamicCurrentWindowText)
            or nameof(AlgorithmListViewModel.PsdPreviewPoints)
            or nameof(AlgorithmListViewModel.HasPsdPreview)
            or nameof(AlgorithmListViewModel.PsdValueUnit)
            or nameof(AlgorithmListViewModel.PsdFrequencyRangeText)
            or nameof(AlgorithmListViewModel.PsdValueRangeText)
            or nameof(AlgorithmListViewModel.PsdStructuredPreview)
            or nameof(AlgorithmListViewModel.LastRun)
            or nameof(AlgorithmListViewModel.IsRunActive))
        {
            if (args.PropertyName is nameof(AlgorithmListViewModel.SelectedAlgorithm)
                or nameof(AlgorithmListViewModel.SelectedProjectRecording)
                or nameof(AlgorithmListViewModel.PsdStructuredPreview)
                or nameof(AlgorithmListViewModel.LastRun))
                IsFailureDetailsExpanded = false;
            RaisePropertyChanged(nameof(Algorithm));
            RaisePropertyChanged(nameof(IsAvailable));
            RaisePropertyChanged(nameof(Parameters));
            RaisePropertyChanged(nameof(HasStructuredResult));
            RaisePropertyChanged(nameof(PreviewPoints));
            RaisePropertyChanged(nameof(BandSharePoints));
            RaisePropertyChanged(nameof(ValueUnit));
            RaisePropertyChanged(nameof(FrequencyRange));
            RaisePropertyChanged(nameof(ValueRange));
            RaisePropertyChanged(nameof(SelectedProjectRecording));
            RaisePropertyChanged(nameof(SelectedProject));
            RaisePropertyChanged(nameof(RegisteredRecording));
            RaisePropertyChanged(nameof(RegisteredChannels));
            RaisePropertyChanged(nameof(SelectedChannel));
            RaisePropertyChanged(nameof(StartSecondsText));
            RaisePropertyChanged(nameof(EndSecondsText));
            RaisePropertyChanged(nameof(SelectedAnalysisMode));
            RaisePropertyChanged(nameof(SelectedDynamicWindowSeconds));
            RaisePropertyChanged(nameof(DynamicStepText));
            RaisePropertyChanged(nameof(RunStatus));
            RaisePropertyChanged(nameof(ResultSummary));
            RaisePropertyChanged(nameof(Provenance));
            RaisePropertyChanged(nameof(StructuredPreview));
            RaisePropertyChanged(nameof(DynamicWindowRows));
            RaisePropertyChanged(nameof(IsDynamicMode));
            RaisePropertyChanged(nameof(IsDynamicModeAvailable));
            RaisePropertyChanged(nameof(DynamicTimeProgressPercent));
            RaisePropertyChanged(nameof(DynamicTimeProgressText));
            RaisePropertyChanged(nameof(DynamicCurrentWindowText));
            RaisePropertyChanged(nameof(StructuredPreviewResult));
            RaisePropertyChanged(nameof(QualityText));
            RaisePropertyChanged(nameof(WindowStateText));
            RaisePropertyChanged(nameof(LastRun));
            RaisePropertyChanged(nameof(IsRunActive));
            RaisePropertyChanged(nameof(RunIdText));
            RaisePropertyChanged(nameof(RequestedRangeText));
            RaisePropertyChanged(nameof(ActualRangeText));
            RaisePropertyChanged(nameof(ResultTimeText));
            RaisePropertyChanged(nameof(RunStartedAtText));
            RaisePropertyChanged(nameof(RunEndedAtText));
            RaisePropertyChanged(nameof(RunDurationText));
            RaisePropertyChanged(nameof(RunProgressPercent));
            RaisePropertyChanged(nameof(RunProgressText));
            RaisePropertyChanged(nameof(FailureReasonText));
            RaisePropertyChanged(nameof(HasFailureDetails));
            RaisePropertyChanged(nameof(FailureDetailsText));
            RaisePropertyChanged(nameof(UnavailableWindowCount));
            RaisePropertyChanged(nameof(RejectedWindowCount));
            RaisePropertyChanged(nameof(WindowStateSummary));
        }
    }

    private void OnPsdPreviewStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        RaisePropertyChanged(nameof(HasStructuredResult));
        RaisePropertyChanged(nameof(PreviewPoints));
        RaisePropertyChanged(nameof(BandSharePoints));
        RaisePropertyChanged(nameof(ValueUnit));
        RaisePropertyChanged(nameof(FrequencyRange));
        RaisePropertyChanged(nameof(ValueRange));
        RaisePropertyChanged(nameof(StructuredPreviewResult));
        RaisePropertyChanged(nameof(QualityText));
        RaisePropertyChanged(nameof(WindowStateText));
        RaisePropertyChanged(nameof(FailureReasonText));
        RaisePropertyChanged(nameof(HasFailureDetails));
        RaisePropertyChanged(nameof(FailureDetailsText));
        RaisePropertyChanged(nameof(UnavailableWindowCount));
        RaisePropertyChanged(nameof(RejectedWindowCount));
        RaisePropertyChanged(nameof(WindowStateSummary));
    }

    private void OnProjectsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ProjectWorkspaceViewModel.SelectedProject)
            or nameof(ProjectWorkspaceViewModel.CompletedRecordings))
        {
            RaisePropertyChanged(nameof(SelectedProject));
            RaisePropertyChanged(nameof(SelectedProjectRecording));
            RaisePropertyChanged(nameof(Projects));
        }
    }

    private static string FormatRange(TimeRange? range) => range is null
        ? "未运行"
        : $"{range.StartSeconds:0.###}–{range.EndSeconds:0.###} s";

    private static string BuildFailureDetails(StructuredPreviewResponse? preview, StructuredRunError? runError)
    {
        if (preview is null || preview.Windows.Count == 0)
            return runError is not null
                ? BuildRunErrorDetails(runError)
                : preview?.Failure is { } failure && IsFailureObject(failure)
                    ? BuildStructuredFailureDetails(failure)
                    : "暂无失败或不可用窗口。";

        var failed = preview.Windows
            .Where(window => WindowState(window) is not "Complete")
            .ToArray();
        if (failed.Length == 0)
            return runError is not null
                ? BuildRunErrorDetails(runError)
                : preview.Failure is { } failure && IsFailureObject(failure)
                    ? BuildStructuredFailureDetails(failure)
                    : "暂无失败或不可用窗口。";

        var lines = new List<string>
        {
            $"问题窗口：{failed.Length} 个（共 {preview.Windows.Count} 个）",
            ""
        };

        var reasonCounts = failed
            .Select(ReasonCode)
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (reasonCounts.Length > 0)
        {
            lines.Add("原因统计：");
            lines.AddRange(reasonCounts.Select(group => $"• {AlgorithmResultFormatter.FormatFailureCodeForDisplay(group.Key)}：{group.Count()} 个"));
            lines.Add("");
        }

        var qualityReasons = failed
            .SelectMany(QualityReasons)
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (qualityReasons.Length > 0)
        {
            lines.Add("质量门原因：");
            lines.AddRange(qualityReasons.Select(group => $"• {AlgorithmResultFormatter.FormatQualityReasonForDisplay(group.Key)}：{group.Count()} 个"));
            lines.Add("");
        }

        lines.Add("窗口明细：");
        foreach (var window in failed.Take(100))
        {
            var reason = ReasonCode(window);
            var quality = string.Join("、", QualityReasons(window));
            var detail = string.Join("；", new[]
            {
                AlgorithmResultFormatter.FormatWindowStateForDisplay(WindowState(window)),
                string.IsNullOrWhiteSpace(reason) ? null : $"原因 {AlgorithmResultFormatter.FormatFailureCodeForDisplay(reason)}",
                string.IsNullOrWhiteSpace(quality) ? null : $"质量门 {AlgorithmResultFormatter.FormatQualityReasonForDisplay(quality)}"
            }.Where(value => value is not null));
            lines.Add($"• {Number(window, "start_s"):0.###}–{Number(window, "end_s"):0.###} 秒：{detail}");
        }
        if (failed.Length > 100)
            lines.Add($"• 其余 {failed.Length - 100} 个窗口未展开。");
        return string.Join(Environment.NewLine, lines);
    }

    private static string? StructuredFailureSummary(JsonElement? failure)
    {
        if (failure is not { } value || !IsFailureObject(value))
            return null;
        var code = value.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        var message = value.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
        return $"{AlgorithmResultFormatter.FormatFailureCodeForDisplay(code ?? "")}{(string.IsNullOrWhiteSpace(message) ? "" : $"：{message}")}";
    }

    private static string BuildStructuredFailureDetails(JsonElement failure)
    {
        var lines = new List<string>();
        var code = failure.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        var message = failure.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
        if (!string.IsNullOrWhiteSpace(code))
            lines.Add($"错误代码：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(code)}（{code}）");
        if (!string.IsNullOrWhiteSpace(message))
            lines.Add($"错误信息：{message}");
        if (failure.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
        {
            lines.Add("");
            lines.Add("质量证据：");
            AddNumber(detail, lines, "clean_segments", "有效片段数");
            AddNumber(detail, lines, "total_segments", "候选片段数");
            if (detail.TryGetProperty("clean_ratio", out var ratio) && ratio.TryGetDouble(out var cleanRatio))
                lines.Add($"• 有效比例：{cleanRatio:P1}");
            AddAmplitudeEvidence(lines, detail);
            if (detail.TryGetProperty("gate_failed", out var gate) && gate.ValueKind == JsonValueKind.String)
                lines.Add($"• 质量门判定：{AlgorithmResultFormatter.FormatQualityReasonForDisplay(gate.GetString() ?? "")}");
        }
        return lines.Count == 0 ? "结构化结果未提供失败详情。" : string.Join(Environment.NewLine, lines);
    }

    private static bool IsFailureObject(JsonElement? value) => value is { ValueKind: JsonValueKind.Object };

    private static string BuildRunErrorDetails(StructuredRunError error)
    {
        var lines = new List<string>
        {
            $"错误代码：{AlgorithmResultFormatter.FormatFailureCodeForDisplay(error.Code)}（{error.Code}）",
            $"错误信息：{error.Message}"
        };
        if (!string.IsNullOrWhiteSpace(error.Stage))
            lines.Add($"失败阶段：{error.Stage}");

        if (error.Details is not { } details || details.ValueKind != JsonValueKind.Object)
            return string.Join(Environment.NewLine, lines);

        if (details.TryGetProperty("quality", out var quality) && quality.ValueKind == JsonValueKind.Object)
        {
            lines.Add("");
            lines.Add("质量证据：");
            AddNumber(quality, lines, "clean_segments", "有效片段数");
            AddNumber(quality, lines, "total_segments", "候选片段数");
            if (quality.TryGetProperty("clean_ratio", out var ratio) && ratio.TryGetDouble(out var cleanRatio))
                lines.Add($"• 有效比例：{cleanRatio:P1}");
            AddAmplitudeEvidence(lines, quality);
            if (quality.TryGetProperty("gate_failed", out var gate) && gate.ValueKind == JsonValueKind.String)
                lines.Add($"• 质量门判定：{AlgorithmResultFormatter.FormatQualityReasonForDisplay(gate.GetString() ?? "")}");
            if (quality.TryGetProperty("rejected_reasons", out var reasons) && reasons.ValueKind == JsonValueKind.Array)
            {
                var labels = reasons.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => AlgorithmResultFormatter.FormatQualityReasonForDisplay(item.GetString() ?? ""))
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .ToArray();
                if (labels.Length > 0)
                    lines.Add($"• 拒绝原因：{string.Join("、", labels)}");
            }
        }
        else
        {
            lines.Add($"错误详情：{details}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void AddAmplitudeEvidence(List<string> lines, JsonElement quality)
    {
        var amplitude = quality.TryGetProperty("amplitude", out var direct) && direct.ValueKind == JsonValueKind.Object
            ? direct
            : quality.TryGetProperty("evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Object
                && evidence.TryGetProperty("amplitude", out var evidenceAmplitude) && evidenceAmplitude.ValueKind == JsonValueKind.Object
                    ? evidenceAmplitude
                    : quality.TryGetProperty("evidence", out evidence) && evidence.ValueKind == JsonValueKind.Object
                        && evidence.TryGetProperty("spectral_evidence", out var spectral) && spectral.ValueKind == JsonValueKind.Object
                        && spectral.TryGetProperty("amplitude", out var nested) && nested.ValueKind == JsonValueKind.Object
                            ? nested
                            : default;
        if (amplitude.ValueKind != JsonValueKind.Object)
            return;

        if (!TryGetDouble(amplitude, "peak_uv", out var peak) || !TryGetDouble(amplitude, "threshold_uv", out var threshold))
            return;
        var exceeded = TryGetDouble(amplitude, "exceeded_uv", out var amount)
            ? amount
            : Math.Max(0, peak - threshold);
        lines.Add($"• 峰值振幅：{peak:0.###} μV");
        lines.Add($"• 质量阈值：{threshold:0.###} μV");
        if (exceeded > 0)
            lines.Add($"• 超出阈值：{exceeded:0.###} μV");
    }

    private static bool TryGetDouble(JsonElement value, string propertyName, out double number)
    {
        number = 0;
        return value.TryGetProperty(propertyName, out var element)
            && element.TryGetDouble(out number)
            && double.IsFinite(number);
    }

    private static void AddNumber(JsonElement value, List<string> lines, string propertyName, string label)
    {
        if (value.TryGetProperty(propertyName, out var element) && element.TryGetInt32(out var number))
            lines.Add($"• {label}：{number}");
    }

    private static string GetWindowReasonSummary(StructuredPreviewResponse? preview)
    {
        if (preview is null)
            return "无";

        var problemWindows = preview.Windows.Where(window => WindowState(window) is not "Complete").ToArray();
        if (problemWindows.Length == 0)
            return "无";

        var reasons = problemWindows
            .SelectMany(window => new[] { ReasonCode(window) }.Concat(QualityReasons(window)))
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{AlgorithmResultFormatter.FormatFailureCodeForDisplay(group.Key)}（{group.Count()} 个）")
            .ToArray();

        return reasons.Length > 0
            ? string.Join("、", reasons)
            : $"有 {problemWindows.Length} 个窗口未完成，请查看窗口失败原因";
    }

    private static string WindowState(JsonElement window) =>
        window.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String
            ? state.GetString() ?? "未知"
            : "未知";

    private static string ReasonCode(JsonElement window)
    {
        if (window.TryGetProperty("failure", out var failure) &&
            failure.ValueKind == JsonValueKind.Object &&
            failure.TryGetProperty("code", out var code) &&
            code.ValueKind == JsonValueKind.String)
            return code.GetString() ?? "";
        return "";
    }

    private static IEnumerable<string> QualityReasons(JsonElement window)
    {
        if (!window.TryGetProperty("evidence", out var evidence) ||
            evidence.ValueKind != JsonValueKind.Object ||
            !evidence.TryGetProperty("source_quality", out var quality) ||
            quality.ValueKind != JsonValueKind.Object ||
            !quality.TryGetProperty("rejected_reasons", out var reasons) ||
            reasons.ValueKind != JsonValueKind.Array)
            return [];
        return reasons.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? "")
            .Where(item => item.Length > 0);
    }

    private static double Number(JsonElement value, string name) =>
        value.TryGetProperty(name, out var element) && element.TryGetDouble(out var number) ? number : double.NaN;
}
