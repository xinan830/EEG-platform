using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Projects.ViewModels;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels;

public sealed partial class AlgorithmListViewModel : ObservableObject
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
    private string selectedF4Channel = string.Empty;
    private string startSecondsText = "0";
    private string endSecondsText = string.Empty;
    private string lowFrequencyText = "1";
    private string highFrequencyText = "30";
    private string numeratorLowFrequencyText = "4";
    private string numeratorHighFrequencyText = "8";
    private string denominatorLowFrequencyText = "8";
    private string denominatorHighFrequencyText = "13";
    private string selectedPeakBandPreset = "自定义";
    private string selectedRatioPreset = "自定义";
    private bool applyingBandPreset;
    private string psdFrequencyUnit = "Hz";
    private string psdValueUnit = "";
    private bool hasPsdPreview;
    private string psdFrequencyRangeText = "";
    private string psdValueRangeText = "";
    private StftPreview? stftPreview;
    private string rbpDeltaText = "不可用";
    private string rbpThetaText = "不可用";
    private string rbpAlphaText = "不可用";
    private string rbpBetaText = "不可用";

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
    public IReadOnlyList<FrequencyBandPreset> FrequencyBandPresets { get; } =
    [
        new("自定义", null, null),
        new("Delta（1–4 Hz）", 1, 4),
        new("Theta（4–8 Hz）", 4, 8),
        new("Alpha（8–13 Hz）", 8, 13),
        new("Beta（13–30 Hz）", 13, 30),
    ];
    public IReadOnlyList<BandRatioPreset> BandRatioPresets { get; } =
    [
        new("自定义", null, null, null, null),
        new("Theta / Beta", 4, 8, 13, 30),
        new("Alpha / Theta", 8, 13, 4, 8),
        new("Alpha / Beta", 8, 13, 13, 30),
    ];

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
            SelectedF4Channel = string.Empty;
            EndSecondsText = string.Empty;
            ClearPreviews();
        }
    }
    public string SelectedChannel { get => selectedChannel; set => SetProperty(ref selectedChannel, value); }
    public string SelectedF4Channel { get => selectedF4Channel; set => SetProperty(ref selectedF4Channel, value); }
    public string StartSecondsText { get => startSecondsText; set => SetProperty(ref startSecondsText, value); }
    public string EndSecondsText { get => endSecondsText; set => SetProperty(ref endSecondsText, value); }
    public RegisteredRecording? RegisteredRecording { get => registeredRecording; private set => SetProperty(ref registeredRecording, value); }
    public AlgorithmCatalogItem? SelectedAlgorithm
    {
        get => selectedAlgorithm;
        set
        {
            if (!SetProperty(ref selectedAlgorithm, value)) return;
            ClearPreviews();
            RaisePropertyChanged(nameof(IsPsdSelected));
            RaisePropertyChanged(nameof(IsStftSelected));
            RaisePropertyChanged(nameof(IsRbpSelected));
            RaisePropertyChanged(nameof(IsPeakFrequencySelected));
            RaisePropertyChanged(nameof(IsBandRatioSelected));
            RaisePropertyChanged(nameof(IsFaaSelected));
            RaisePropertyChanged(nameof(IsIapfSelected));
            RaisePropertyChanged(nameof(IsThetaBetaSelected));
            RaisePropertyChanged(nameof(ChannelLabel));
        }
    }
    public bool IsPsdSelected => SelectedAlgorithm?.Id == "psd";
    public bool IsStftSelected => SelectedAlgorithm?.Id == "stft";
    public bool IsRbpSelected => SelectedAlgorithm?.Id == "rbp";
    public bool IsPeakFrequencySelected => SelectedAlgorithm?.Id == "peak_frequency";
    public bool IsBandRatioSelected => SelectedAlgorithm?.Id == "band_ratio";
    public bool IsFaaSelected => SelectedAlgorithm?.Id == "faa";
    public bool IsIapfSelected => SelectedAlgorithm?.Id == "iapf";
    public bool IsThetaBetaSelected => SelectedAlgorithm?.Id == "theta_beta";
    public string ChannelLabel => IsFaaSelected ? "FAA F3 通道" : IsStftSelected ? "STFT 通道" : IsRbpSelected ? "RBP 通道" : IsPeakFrequencySelected ? "峰频率通道" : IsBandRatioSelected ? "频段比通道" : IsIapfSelected ? "IAPF 通道" : IsThetaBetaSelected ? "Theta/Beta 通道" : "PSD 通道";
    public string LowFrequencyText { get => lowFrequencyText; set { if (SetProperty(ref lowFrequencyText, value) && !applyingBandPreset) SelectedPeakBandPreset = "自定义"; } }
    public string HighFrequencyText { get => highFrequencyText; set { if (SetProperty(ref highFrequencyText, value) && !applyingBandPreset) SelectedPeakBandPreset = "自定义"; } }
    public string NumeratorLowFrequencyText { get => numeratorLowFrequencyText; set { if (SetProperty(ref numeratorLowFrequencyText, value) && !applyingBandPreset) SelectedRatioPreset = "自定义"; } }
    public string NumeratorHighFrequencyText { get => numeratorHighFrequencyText; set { if (SetProperty(ref numeratorHighFrequencyText, value) && !applyingBandPreset) SelectedRatioPreset = "自定义"; } }
    public string DenominatorLowFrequencyText { get => denominatorLowFrequencyText; set { if (SetProperty(ref denominatorLowFrequencyText, value) && !applyingBandPreset) SelectedRatioPreset = "自定义"; } }
    public string DenominatorHighFrequencyText { get => denominatorHighFrequencyText; set { if (SetProperty(ref denominatorHighFrequencyText, value) && !applyingBandPreset) SelectedRatioPreset = "自定义"; } }
    public string SelectedPeakBandPreset
    {
        get => selectedPeakBandPreset;
        set
        {
            if (!SetProperty(ref selectedPeakBandPreset, value)) return;
            var preset = FrequencyBandPresets.FirstOrDefault(item => item.Name == value);
            if (preset?.LowHz is double low && preset.HighHz is double high)
            {
                applyingBandPreset = true;
                try
                {
                    LowFrequencyText = low.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    HighFrequencyText = high.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                }
                finally { applyingBandPreset = false; }
            }
        }
    }
    public string SelectedRatioPreset
    {
        get => selectedRatioPreset;
        set
        {
            if (!SetProperty(ref selectedRatioPreset, value)) return;
            var preset = BandRatioPresets.FirstOrDefault(item => item.Name == value);
            if (preset?.NumeratorLowHz is double numeratorLow && preset.NumeratorHighHz is double numeratorHigh &&
                preset.DenominatorLowHz is double denominatorLow && preset.DenominatorHighHz is double denominatorHigh)
            {
                applyingBandPreset = true;
                try
                {
                    NumeratorLowFrequencyText = numeratorLow.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    NumeratorHighFrequencyText = numeratorHigh.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    DenominatorLowFrequencyText = denominatorLow.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    DenominatorHighFrequencyText = denominatorHigh.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                }
                finally { applyingBandPreset = false; }
            }
        }
    }
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
    public StftPreview? StftResult { get => stftPreview; private set => SetProperty(ref stftPreview, value); }
    public bool HasStftPreview => StftResult is not null;
    public string RbpDeltaText { get => rbpDeltaText; private set => SetProperty(ref rbpDeltaText, value); }
    public string RbpThetaText { get => rbpThetaText; private set => SetProperty(ref rbpThetaText, value); }
    public string RbpAlphaText { get => rbpAlphaText; private set => SetProperty(ref rbpAlphaText, value); }
    public string RbpBetaText { get => rbpBetaText; private set => SetProperty(ref rbpBetaText, value); }
    public string StftAxisText => StftResult is null ? "时间 (s)" :
        $"请求范围 {StftResult.RequestedRange.StartSeconds:0.###} - {StftResult.RequestedRange.EndSeconds:0.###} s  ·  时频中心 {StftResult.TimesSeconds[0]:0.###} - {StftResult.TimesSeconds[^1]:0.###} s  ·  频率 {StftResult.FrequenciesHz[0]:0.###} - {StftResult.FrequenciesHz[^1]:0.###} Hz  ·  {StftResult.PowerUnit}";

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
        ClearPreviews();
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
        SelectedF4Channel = RegisteredChannels.Count > 1 ? RegisteredChannels[1] : string.Empty;
        EndSecondsText = RegisteredRecording.DurationSeconds.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        StatusText = $"已注册记录：{RegisteredRecording.OriginalName}，{RegisteredRecording.DurationSeconds.Value:0.###} 秒。请选择分析通道和范围。";
    }

    private async Task RunAsync()
    {
        var algorithm = SelectedAlgorithm ?? throw new InvalidOperationException("请选择一个官方算法。");
        if (!IsSupportedStaticAlgorithm(algorithm))
        {
            throw new InvalidOperationException("当前配置页仅支持可运行的静态 PSD、STFT、RBP、峰频率、频段功率比、FAA、IAPF 和 Theta/Beta；其他算法需要各自的参数契约。");
        }
        if (RegisteredRecording is null)
        {
            throw new InvalidOperationException("请先注册一条已完成的 WPF 记录。");
        }
        if (string.IsNullOrWhiteSpace(SelectedChannel) || !RegisteredChannels.Contains(SelectedChannel))
            throw new InvalidOperationException("请选择注册记录返回的有效分析通道。");
        if (algorithm.Id == "faa" &&
            (string.IsNullOrWhiteSpace(SelectedF4Channel) || !RegisteredChannels.Contains(SelectedF4Channel)))
            throw new InvalidOperationException("请选择注册记录返回的有效 FAA F4 来源通道。");
        if (algorithm.Id == "faa" && string.Equals(SelectedChannel, SelectedF4Channel, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FAA 的 F3 与 F4 来源通道必须不同。");
        var durationSeconds = RegisteredRecording.DurationSeconds ?? throw new InvalidOperationException("注册记录没有可用的时长。");
        var requestedRange = ParseStaticRangeOrThrow(StartSecondsText, EndSecondsText, durationSeconds);
        if (algorithm.Id == "stft" && requestedRange.EndSeconds - requestedRange.StartSeconds < 4.0)
            throw new InvalidOperationException("STFT 分析区间至少需要 4 秒。");
        var frequencyBand = algorithm.Id == "peak_frequency"
            ? ParseFrequencyBandOrThrow(LowFrequencyText, HighFrequencyText, RegisteredRecording.SamplingRateHz)
            : null;
        var ratioBands = algorithm.Id == "band_ratio"
            ? ParseBandRatioBandsOrThrow(
                NumeratorLowFrequencyText, NumeratorHighFrequencyText,
                DenominatorLowFrequencyText, DenominatorHighFrequencyText,
                RegisteredRecording.SamplingRateHz)
            : null;
        ClearPreviews();
        var start = requestedRange.StartSeconds;
        var end = requestedRange.EndSeconds;
        var config = BuildStaticRunConfig(algorithm, SelectedChannel, requestedRange, frequencyBand, ratioBands, algorithm.Id == "faa" ? SelectedF4Channel : null);
        var run = await client.CreateRunAsync(new AnalysisRunRequest(RegisteredRecording.Id, "official_algorithm", config), CancellationToken.None);
        RunStatusText = $"{algorithm.DisplayNameZh}：{run.Status}（{run.RunId}）";
        run = await PollRunToTerminalAsync(run, algorithm.DisplayNameZh, CancellationToken.None);
        if (run.Status == "completed")
        {
            RunStatusText += "，结果已由后端保存。";
            ResultSummaryText = BuildResultSummary(run);
            ProvenanceText = BuildProvenance(run);
            if (algorithm.Id == "rbp")
            {
                UpdateRbpValues(run);
                StructuredPreviewText = "RBP 为四频段标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "peak_frequency")
            {
                StructuredPreviewText = "频段峰频率为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "band_ratio")
            {
                StructuredPreviewText = "频段功率比为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "faa")
            {
                StructuredPreviewText = "额叶 Alpha 不对称性为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "iapf")
            {
                StructuredPreviewText = "个体 Alpha 峰频率为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else if (algorithm.Id == "theta_beta")
            {
                StructuredPreviewText = "Theta/Beta 比值为后端标量结果，数值由后端直接返回，未在客户端重算。";
            }
            else
            {
                await LoadStructuredPreviewAsync(run.RunId, algorithm.Id);
            }
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

    internal sealed record FrequencyBand(double LowHz, double HighHz);
    internal sealed record BandRatioBands(FrequencyBand Numerator, FrequencyBand Denominator);
    public sealed record FrequencyBandPreset(string Name, double? LowHz, double? HighHz);
    public sealed record BandRatioPreset(string Name, double? NumeratorLowHz, double? NumeratorHighHz, double? DenominatorLowHz, double? DenominatorHighHz);

    internal static FrequencyBand ParseFrequencyBandOrThrow(string lowText, string highText, double? samplingRateHz)
    {
        if (!double.TryParse(lowText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var low) ||
            !double.TryParse(highText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var high) ||
            !double.IsFinite(low) || !double.IsFinite(high) || low < 0 || high <= low)
        {
            throw new InvalidOperationException("请输入有效的频段范围，下限必须小于上限。");
        }

        if (samplingRateHz is not > 0 || !double.IsFinite(samplingRateHz.Value) || high >= samplingRateHz.Value / 2.0)
        {
            throw new InvalidOperationException("频段上限必须低于记录采样率的 Nyquist 频率。");
        }

        return new FrequencyBand(low, high);
    }

    internal static BandRatioBands ParseBandRatioBandsOrThrow(
        string numeratorLowText, string numeratorHighText,
        string denominatorLowText, string denominatorHighText,
        double? samplingRateHz)
    {
        var numerator = ParseFrequencyBandOrThrow(numeratorLowText, numeratorHighText, samplingRateHz);
        var denominator = ParseFrequencyBandOrThrow(denominatorLowText, denominatorHighText, samplingRateHz);
        return new BandRatioBands(numerator, denominator);
    }

    internal static JsonElement BuildStaticRunConfig(
        AlgorithmCatalogItem algorithm,
        string channel,
        TimeRange range,
        FrequencyBand? frequencyBand = null,
        BandRatioBands? ratioBands = null,
        string? f4Channel = null) =>
        JsonSerializer.SerializeToElement(new
        {
            algorithm_id = algorithm.Id,
            scientific_version = algorithm.Version,
            time = new { start_s = range.StartSeconds, end_s = range.EndSeconds },
            channel,
            mode = "static",
            low_hz = frequencyBand?.LowHz,
            high_hz = frequencyBand?.HighHz,
            numerator_low_hz = ratioBands?.Numerator.LowHz,
            numerator_high_hz = ratioBands?.Numerator.HighHz,
            denominator_low_hz = ratioBands?.Denominator.LowHz,
            denominator_high_hz = ratioBands?.Denominator.HighHz,
            f4_channel = f4Channel,
        });

    internal static bool IsSupportedStaticAlgorithm(AlgorithmCatalogItem? algorithm) =>
        algorithm is { IsRunnable: true } && (algorithm.Id is "psd" or "stft" or "rbp" or "peak_frequency" or "band_ratio" or "faa" or "iapf" or "theta_beta") && algorithm.Modes.Contains("static");

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

    private async Task LoadStructuredPreviewAsync(string runId, string algorithmId)
    {
        try
        {
            var preview = await client.GetStructuredPreviewAsync(runId, algorithmId == "stft" ? 100_000 : 4_000, CancellationToken.None);
            StructuredPreviewText = BuildStructuredPreview(preview);
            if (algorithmId == "stft")
            {
                StftResult = StftPreview.Parse(preview);
                RaisePropertyChanged(nameof(HasStftPreview));
                RaisePropertyChanged(nameof(StftAxisText));
            }
            else BuildPsdPreview(preview);
        }
        catch (AlgorithmApiException exception) when (exception.Code == "REQUEST_INVALID" && algorithmId == "stft")
        {
            StructuredPreviewText = "STFT 结果已保存，但预览超过 100,000 个数值单元或无法读取；请缩短分析范围后重新运行。";
            ClearPreviews();
        }
        catch (AlgorithmApiException exception) when (exception.Code is "REQUEST_INVALID" or "RESOURCE_NOT_FOUND")
        {
            StructuredPreviewText = $"结构化预览不可用：{exception.Code}：{exception.Message}";
            ClearPreviews();
        }
        catch (Exception exception)
        {
            StructuredPreviewText = $"结构化预览不可用：{exception.Message}";
            ClearPreviews();
        }
    }

    private void ClearPreviews()
    {
        ClearPsdPreview();
        StftResult = null;
        RbpDeltaText = RbpThetaText = RbpAlphaText = RbpBetaText = "不可用";
        RaisePropertyChanged(nameof(HasStftPreview));
        RaisePropertyChanged(nameof(StftAxisText));
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

    internal static string BuildResultSummary(AnalysisRunResponse run)
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
            ? FormatQuality(qualityElement)
            : "未提供";
        var channel = metric.TryGetProperty("channel", out var channelElement)
            ? channelElement.GetString() ?? ""
            : "";
        if (metric.TryGetProperty("band_values", out var bands) && bands.ValueKind == JsonValueKind.Object)
        {
            var bandText = string.Join("；", bands.EnumerateObject().Select(item =>
                $"{FormatBandName(item.Name)}：{FormatJsonValue(item.Value)}"));
            return $"频段相对功率：{bandText}；单位：ratio；通道：{(string.IsNullOrWhiteSpace(channel) ? "未指定" : channel)}；质量：{quality}";
        }
        return $"指标：{value}{(string.IsNullOrWhiteSpace(unit) ? "" : $" {unit}")}；通道：{(string.IsNullOrWhiteSpace(channel) ? "未指定" : channel)}；质量：{quality}";
    }

    private static string FormatBandName(string name) => name.ToLowerInvariant() switch
    {
        "delta" => "Delta",
        "theta" => "Theta",
        "alpha" => "Alpha",
        "beta" => "Beta",
        _ => name,
    };

    private void UpdateRbpValues(AnalysisRunResponse run)
    {
        if (run.ResultSummary is not { } summary || !summary.TryGetProperty("metric", out var metric) ||
            !metric.TryGetProperty("band_values", out var values) || values.ValueKind != JsonValueKind.Object)
            return;
        RbpDeltaText = FormatBandValue(values, "delta");
        RbpThetaText = FormatBandValue(values, "theta");
        RbpAlphaText = FormatBandValue(values, "alpha");
        RbpBetaText = FormatBandValue(values, "beta");
    }

    private static string FormatBandValue(JsonElement values, string key) =>
        values.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
            : "不可用";

    private static string FormatQuality(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "未提供";
        if (value.ValueKind != JsonValueKind.Object)
            return FormatJsonValue(value);
        var status = value.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        var reasons = value.TryGetProperty("reasons", out var reasonsElement) && reasonsElement.ValueKind == JsonValueKind.Array
            ? string.Join("、", reasonsElement.EnumerateArray().Select(FormatJsonValue))
            : "";
        return string.IsNullOrWhiteSpace(reasons)
            ? status switch { "clean" => "良好（clean）", "gate_failed" => "未通过质量门", _ => status ?? "未提供" }
            : $"{status ?? "未提供"}：{reasons}";
    }

    internal static string BuildProvenance(AnalysisRunResponse run)
    {
        var provenance = run.AnalysisProvenance is { } value && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        // The Run-level version identifies the selected official algorithm.
        // The provenance payload may also contain the lower-level spectral
        // implementation version; it must not replace the algorithm identity
        // shown to the operator.
        var version = run.ScientificVersion ??
            (provenance.ValueKind == JsonValueKind.Object && provenance.TryGetProperty("scientific_algorithm_version", out var versionElement)
                ? versionElement.GetString() ?? "未知"
                : "未知");
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
