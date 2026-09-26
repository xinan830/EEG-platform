using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Projects.ViewModels;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

public sealed class AlgorithmListViewModel : ObservableObject
{
    public sealed record PsdPreviewPoint(double Frequency, double Value);
    private readonly IAlgorithmClient client;
    private readonly OperationNotificationCenter notifications;
    private string statusText = "尚未加载算法目录。";
    private bool isLoading;
    private string sourceDirectory = string.Empty;
    private RegisteredRecording? registeredRecording;
    private AlgorithmCatalogItem? selectedAlgorithm;
    private ProjectRecordingRow? selectedProjectRecording;
    private string runStatusText = "尚未提交分析。";
    private string resultSummaryText = "暂无结果摘要。";
    private string provenanceText = "暂无溯源信息。";
    private string structuredPreviewText = "暂无结构化预览。";
    private string selectedChannel = string.Empty;
    private string startSecondsText = "0";
    private string endSecondsText = string.Empty;
    private string psdFrequencyUnit = "Hz";
    private string psdValueUnit = "";
    private bool hasPsdPreview;
    private string psdFrequencyRangeText = "";
    private string psdValueRangeText = "";

    public AlgorithmListViewModel(IAlgorithmClient client, OperationNotificationCenter notifications, ProjectWorkspaceViewModel? projects = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, ReportCatalogError);
        RegisterCommand = new AsyncRelayCommand(RegisterAsync, ReportRunError);
        RunCommand = new AsyncRelayCommand(RunAsync, ReportRunError);
        Projects = projects;
    }

    public ObservableCollection<AlgorithmCatalogItem> Items { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand RunCommand { get; }
    public ProjectWorkspaceViewModel? Projects { get; }
    public ObservableCollection<string> RegisteredChannels { get; } = [];

    public string SourceDirectory { get => sourceDirectory; private set => SetProperty(ref sourceDirectory, value); }
    public ProjectRecordingRow? SelectedProjectRecording
    {
        get => selectedProjectRecording;
        set
        {
            if (!SetProperty(ref selectedProjectRecording, value)) return;
            SourceDirectory = value?.RecordingDirectory ?? string.Empty;
            RegisteredRecording = null;
            RegisteredChannels.Clear();
            SelectedChannel = string.Empty;
            EndSecondsText = string.Empty;
        }
    }
    public string SelectedChannel { get => selectedChannel; set => SetProperty(ref selectedChannel, value); }
    public string StartSecondsText { get => startSecondsText; set => SetProperty(ref startSecondsText, value); }
    public string EndSecondsText { get => endSecondsText; set => SetProperty(ref endSecondsText, value); }
    public RegisteredRecording? RegisteredRecording { get => registeredRecording; private set => SetProperty(ref registeredRecording, value); }
    public AlgorithmCatalogItem? SelectedAlgorithm { get => selectedAlgorithm; set => SetProperty(ref selectedAlgorithm, value); }
    public string RunStatusText { get => runStatusText; private set => SetProperty(ref runStatusText, value); }
    public string ResultSummaryText { get => resultSummaryText; private set => SetProperty(ref resultSummaryText, value); }
    public string ProvenanceText { get => provenanceText; private set => SetProperty(ref provenanceText, value); }
    public string StructuredPreviewText { get => structuredPreviewText; private set => SetProperty(ref structuredPreviewText, value); }
    public ObservableCollection<PsdPreviewPoint?> PsdPreviewPoints { get; } = [];
    public string PsdFrequencyUnit { get => psdFrequencyUnit; private set => SetProperty(ref psdFrequencyUnit, value); }
    public string PsdValueUnit { get => psdValueUnit; private set => SetProperty(ref psdValueUnit, value); }
    public bool HasPsdPreview { get => hasPsdPreview; private set => SetProperty(ref hasPsdPreview, value); }
    public bool HasRegisteredChannels => RegisteredChannels.Count > 0;
    public string PsdFrequencyRangeText { get => psdFrequencyRangeText; private set => SetProperty(ref psdFrequencyRangeText, value); }
    public string PsdValueRangeText { get => psdValueRangeText; private set => SetProperty(ref psdValueRangeText, value); }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public bool IsLoading { get => isLoading; private set => SetProperty(ref isLoading, value); }

    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var items = await client.ListAlgorithmsAsync(CancellationToken.None);
            Items.Clear();
            foreach (var item in items) Items.Add(item);
            StatusText = $"已加载 {Items.Count} 个官方算法。";
        }
        catch (Exception exception)
        {
            ReportCatalogError(exception);
        }
        finally { IsLoading = false; }
    }

    private void ReportCatalogError(Exception exception)
    {
        StatusText = exception is AlgorithmApiException apiError
            ? $"算法目录加载失败：{apiError.Code}"
            : $"算法目录加载失败：{exception.Message}";
        notifications.PublishError(StatusText);
    }

    private void ReportRunError(Exception exception)
    {
        RunStatusText = exception is AlgorithmApiException apiError
            ? $"算法运行失败：{apiError.Code}：{apiError.Message}"
            : $"算法运行失败：{exception.Message}";
        notifications.PublishError(RunStatusText);
    }

    private async Task RegisterAsync()
    {
        ResultSummaryText = "暂无结果摘要。";
        ProvenanceText = "暂无溯源信息。";
        StructuredPreviewText = "暂无结构化预览。";
        ClearPsdPreview();
        if (SelectedProjectRecording is not { Status: "已完成" } || string.IsNullOrWhiteSpace(SourceDirectory))
        {
            throw new InvalidOperationException("请先从项目中选择一条已完成的记录。");
        }

        var registration = client as IRecordingRegistrationClient
            ?? throw new InvalidOperationException("当前算法客户端不支持 WPF 记录注册。");
        RegisteredRecording = await registration.RegisterWpfRecordingAsync(SourceDirectory.Trim(), CancellationToken.None);
        if (RegisteredRecording.Channels.Count == 0 || RegisteredRecording.DurationSeconds is not > 0)
        {
            throw new InvalidOperationException("注册记录没有有效的通道或时长。");
        }
        RegisteredChannels.Clear();
        RaisePropertyChanged(nameof(HasRegisteredChannels));
        foreach (var channel in RegisteredRecording.Channels
                     .Where(channel => !string.IsNullOrWhiteSpace(channel))
                     .Select(channel => channel.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RegisteredChannels.Add(channel);
        }
        if (RegisteredChannels.Count == 0)
            throw new InvalidOperationException("注册记录没有可分析的 EEG 通道。");
        RaisePropertyChanged(nameof(HasRegisteredChannels));
        SelectedChannel = RegisteredChannels[0];
        EndSecondsText = RegisteredRecording.DurationSeconds.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        StatusText = $"已注册记录：{RegisteredRecording.OriginalName}，{RegisteredRecording.DurationSeconds.Value:0.###} 秒。请选择 PSD 通道和分析范围。";
    }

    private async Task RunAsync()
    {
        var algorithm = SelectedAlgorithm;
        if (algorithm is null || !string.Equals(algorithm.Id, "psd", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("当前配置页仅支持功率谱密度（PSD）；其他算法需要各自的参数契约。 ");
        }
        if (RegisteredRecording is null)
        {
            throw new InvalidOperationException("请先注册一条已完成的 WPF 记录。");
        }
        if (string.IsNullOrWhiteSpace(SelectedChannel) || !RegisteredChannels.Contains(SelectedChannel))
            throw new InvalidOperationException("请选择注册记录返回的有效分析通道。");
        var durationSeconds = RegisteredRecording.DurationSeconds ?? throw new InvalidOperationException("注册记录没有可用的时长。");
        var requestedRange = ParseStaticRangeOrThrow(StartSecondsText, EndSecondsText, durationSeconds);
        var start = requestedRange.StartSeconds;
        var end = requestedRange.EndSeconds;
        var config = JsonSerializer.SerializeToElement(new
        {
            algorithm_id = algorithm.Id,
            scientific_version = algorithm.Version,
            time = new { start_s = start, end_s = end },
            channel = SelectedChannel,
            mode = "static",
        });
        var run = await client.CreateRunAsync(new AnalysisRunRequest(RegisteredRecording.Id, "official_algorithm", config), CancellationToken.None);
        RunStatusText = $"{algorithm.DisplayNameZh}：{run.Status}（{run.RunId}）";
        run = await PollRunToTerminalAsync(run, algorithm.DisplayNameZh, CancellationToken.None);
        if (run.Status == "completed")
        {
            RunStatusText += "，结果已由后端保存。";
            ResultSummaryText = BuildResultSummary(run);
            ProvenanceText = BuildProvenance(run);
            await LoadStructuredPreviewAsync(run.RunId);
        }
        else if (run.Error is not null)
        {
            RunStatusText += $"，{run.Error.Code}：{run.Error.Message}";
            ResultSummaryText = $"结果不可用：{run.Error.Code}。{run.Error.Message}";
            ProvenanceText = BuildProvenance(run);
            StructuredPreviewText = "结果未完成，无法加载结构化预览。";
        }
        else
        {
            ResultSummaryText = $"结果不可用：运行状态为 {run.Status}。";
            ProvenanceText = BuildProvenance(run);
            StructuredPreviewText = "结果未完成，无法加载结构化预览。";
        }
    }

    internal static TimeRange ParseStaticRangeOrThrow(string startText, string endText, double durationSeconds)
    {
        if (!double.TryParse(startText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var start) ||
            !double.TryParse(endText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var end) ||
            !double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(durationSeconds) ||
            start < 0 || end <= start || end > durationSeconds)
        {
            throw new InvalidOperationException("请输入有效的分析范围，结束时间必须大于开始时间且不超过记录时长。");
        }

        return new TimeRange(start, end);
    }

    internal async Task<AnalysisRunResponse> PollRunToTerminalAsync(
        AnalysisRunResponse run,
        string algorithmDisplayName,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 120 && (run.Status is "queued" or "running"); attempt++)
        {
            await Task.Delay(250, cancellationToken);
            run = await client.GetRunAsync(run.RunId, cancellationToken);
            RunStatusText = $"{algorithmDisplayName}：{run.Status}（{run.RunId}）";
        }

        return run;
    }

    private async Task LoadStructuredPreviewAsync(string runId)
    {
        try
        {
            var preview = await client.GetStructuredPreviewAsync(runId, 4_000, CancellationToken.None);
            StructuredPreviewText = BuildStructuredPreview(preview);
            BuildPsdPreview(preview);
        }
        catch (AlgorithmApiException exception) when (exception.Code is "REQUEST_INVALID" or "RESOURCE_NOT_FOUND")
        {
            // Scalar algorithms have no matrix artifact. This is an expected
            // absence, not a failed Run and not permission to invent values.
            StructuredPreviewText = "该算法没有可展示的结构化矩阵预览。";
            ClearPsdPreview();
        }
        catch (Exception exception)
        {
            StructuredPreviewText = $"结构化预览不可用：{exception.Message}";
            ClearPsdPreview();
        }
    }

    private void ClearPsdPreview()
    {
        PsdPreviewPoints.Clear();
        HasPsdPreview = false;
        PsdFrequencyUnit = "Hz";
        PsdValueUnit = "";
        PsdFrequencyRangeText = "";
        PsdValueRangeText = "";
    }

    private void BuildPsdPreview(StructuredPreviewResponse preview)
    {
        ClearPsdPreview();
        if (!preview.Axes.TryGetValue("frequency_hz", out var frequencies) ||
            !preview.Arrays.TryGetValue("psd", out var values) ||
            frequencies.ValueKind != JsonValueKind.Array || values.ValueKind != JsonValueKind.Array)
            return;

        PsdFrequencyUnit = MetadataUnit(preview.AxisMetadata, "frequency_hz");
        PsdValueUnit = MetadataUnit(preview.ArrayMetadata, "psd");
        var frequencyValues = frequencies.EnumerateArray().ToArray();
        var psdValues = values.EnumerateArray().ToArray();
        var count = Math.Min(frequencyValues.Length, psdValues.Length);
        for (var index = 0; index < count; index++)
        {
            if (frequencyValues[index].ValueKind != JsonValueKind.Number ||
                psdValues[index].ValueKind != JsonValueKind.Number ||
                !frequencyValues[index].TryGetDouble(out var frequency) ||
                !psdValues[index].TryGetDouble(out var value) ||
                !double.IsFinite(frequency) || !double.IsFinite(value))
            {
                PsdPreviewPoints.Add(null);
                continue;
            }
            PsdPreviewPoints.Add(new PsdPreviewPoint(frequency, value));
        }
        HasPsdPreview = PsdPreviewPoints.Any(point => point is not null);
        if (HasPsdPreview)
        {
            var available = PsdPreviewPoints.Where(point => point is not null).Select(point => point!).ToArray();
            PsdFrequencyRangeText = $"{available.Min(point => point.Frequency):0.###} - {available.Max(point => point.Frequency):0.###} {PsdFrequencyUnit}";
            PsdValueRangeText = $"{available.Min(point => point.Value):G3} - {available.Max(point => point.Value):G3} {PsdValueUnit}";
        }
    }

    private static string BuildResultSummary(AnalysisRunResponse run)
    {
        if (run.ResultSummary is not { } summary || summary.ValueKind != JsonValueKind.Object)
        {
            return "后端未返回结果摘要；结果保持不可用。";
        }

        if (!summary.TryGetProperty("metric", out var metric) || metric.ValueKind != JsonValueKind.Object)
        {
            return "后端已保存频谱结果；曲线与单位见结构化预览。";
        }

        var output = metric.TryGetProperty("output", out var outputElement) && outputElement.ValueKind == JsonValueKind.Object
            ? outputElement
            : default;
        var value = output.ValueKind == JsonValueKind.Object && output.TryGetProperty("value", out var valueElement)
            ? FormatJsonValue(valueElement)
            : "不可用";
        var unit = output.ValueKind == JsonValueKind.Object && output.TryGetProperty("unit", out var unitElement)
            ? unitElement.GetString() ?? ""
            : "";
        var quality = metric.TryGetProperty("quality", out var qualityElement)
            ? FormatJsonValue(qualityElement)
            : "未提供";
        var channel = metric.TryGetProperty("channel", out var channelElement)
            ? channelElement.GetString() ?? ""
            : "";
        return $"指标：{value}{(string.IsNullOrWhiteSpace(unit) ? "" : $" {unit}")}；通道：{(string.IsNullOrWhiteSpace(channel) ? "未指定" : channel)}；质量：{quality}";
    }

    private static string BuildProvenance(AnalysisRunResponse run)
    {
        var provenance = run.AnalysisProvenance is { } value && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        var version = provenance.ValueKind == JsonValueKind.Object && provenance.TryGetProperty("scientific_algorithm_version", out var versionElement)
            ? versionElement.GetString() ?? run.ScientificVersion ?? "未知"
            : run.ScientificVersion ?? "未知";
        var mode = provenance.ValueKind == JsonValueKind.Object && provenance.TryGetProperty("mode", out var modeElement)
            ? modeElement.GetString() ?? "未知"
            : "未知";
        var channel = provenance.ValueKind == JsonValueKind.Object && provenance.TryGetProperty("channel", out var channelElement)
            ? channelElement.GetString() ?? "未指定"
            : "未指定";
        return $"Run：{run.RunId}；科学版本：{version}；模式：{mode}；通道：{channel}；请求范围：{FormatRange(run.RequestedRange)}；实际范围：{FormatRange(run.ActualRange)}";
    }

    private static string BuildStructuredPreview(StructuredPreviewResponse preview)
    {
        var outputKind = preview.Output is { } output && output.ValueKind == JsonValueKind.Object && output.TryGetProperty("kind", out var kind)
            ? kind.GetString() ?? "结构化结果"
            : "结构化结果";
        var axes = preview.Axes.Count == 0
            ? "无轴信息"
            : string.Join("；", preview.Axes.Select(item =>
            {
                var unit = MetadataUnit(preview.AxisMetadata, item.Key);
                return $"{item.Key}[{unit}]={PreviewValues(item.Value)}";
            }));
        var arrays = preview.Arrays.Count == 0
            ? "无数值矩阵"
            : string.Join("；", preview.Arrays.Select(item =>
            {
                var unit = MetadataUnit(preview.ArrayMetadata, item.Key);
                return $"{item.Key}[{unit}]={PreviewValues(item.Value)}";
            }));
        var states = preview.WindowStateCounts.Count == 0
            ? ""
            : $"；窗口状态：{string.Join("、", preview.WindowStateCounts.Select(item => $"{item.Key}={item.Value}"))}";
        return $"类型：{outputKind}；通道：{string.Join("、", preview.ChannelOrder)}；轴：{axes}；数组：{arrays}{states}。数值与单位由后端返回，未在客户端重算。";
    }

    private static string MetadataUnit(IReadOnlyDictionary<string, JsonElement> metadata, string key)
    {
        if (metadata.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("unit", out var unit))
        {
            return unit.GetString() ?? "未声明单位";
        }

        return "未声明单位";
    }

    private static string PreviewValues(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return FormatJsonValue(value);
        }

        var values = value.EnumerateArray().Take(3).Select(FormatJsonValue).ToArray();
        return values.Length == 0 ? "[]" : $"[{string.Join(", ", values)}{(value.GetArrayLength() > values.Length ? ", …" : "")}]";
    }

    private static string FormatRange(TimeRange? range) => range is null
        ? "未提供"
        : $"{range.StartSeconds:0.###}–{range.EndSeconds:0.###} s";

    private static string FormatJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "不可用",
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.String => value.GetString() ?? "不可用",
        JsonValueKind.True => "是",
        JsonValueKind.False => "否",
        _ => value.ToString(),
    };
}
