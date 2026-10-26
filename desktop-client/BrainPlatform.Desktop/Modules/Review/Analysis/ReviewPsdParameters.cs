using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>PSD-specific source selection; shared settings are captured when submitting a Run.</summary>
public sealed class ReviewPsdParameters : ObservableObject
{
    private string? channel;

    public IReadOnlyList<string> Channels { get; private set; } = [];
    public IReadOnlyList<double> WindowOptions { get; private set; } = [];
    public string? Channel { get => channel; set => SetProperty(ref channel, value); }
    public double StepSeconds { get; private set; } = 1;
    public string? ScientificVersion { get; private set; }
    public HistoricalMontageDescriptor? Montage { get; private set; }

    public void Initialize(RegisteredRecording recording, AlgorithmCatalogItem algorithm,
        HistoricalMontageDescriptor? montage = null)
    {
        Montage = montage;
        Channels = AvailableChannels(recording, montage);
        Channel = Channels.Contains(Channel ?? "") ? Channel : Channels.FirstOrDefault();
        WindowOptions = algorithm.DynamicPolicy.WindowOptionsSeconds;
        StepSeconds = algorithm.DynamicPolicy.RefreshStepSeconds;
        ScientificVersion = algorithm.Version;
        RaisePropertyChanged(nameof(Channels));
        RaisePropertyChanged(nameof(WindowOptions));
    }

    public void SelectMontage(RegisteredRecording recording, HistoricalMontageDescriptor? montage)
    {
        if (string.Equals(Montage?.Id, montage?.Id, StringComparison.Ordinal) &&
            string.Equals(Montage?.Fingerprint, montage?.Fingerprint, StringComparison.Ordinal))
            return;
        var previousRole = Montage?.Outputs.FirstOrDefault(output =>
            string.Equals(output.Id, Channel, StringComparison.OrdinalIgnoreCase))?.Role;
        Montage = montage;
        Channels = AvailableChannels(recording, montage);
        var mappedChannel = previousRole is null ? null : montage?.Outputs.FirstOrDefault(output =>
            output.Available && string.Equals(output.Role, previousRole, StringComparison.OrdinalIgnoreCase))?.Id;
        Channel = mappedChannel is not null && Channels.Contains(mappedChannel)
            ? mappedChannel
            : Channels.FirstOrDefault();
        RaisePropertyChanged(nameof(Montage));
        RaisePropertyChanged(nameof(Channels));
    }

    public JsonElement BuildConfig(double durationSeconds, double samplingRateHz,
        ReviewAnalysisSettingsSnapshot settings, HistoricalMontageDescriptor? montage = null)
    {
        var (HighPassHz, LowPassHz, NotchHz, WindowSeconds) = settings;
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new InvalidOperationException("记录时长必须是正数。");
        if (!double.IsFinite(samplingRateHz) || samplingRateHz <= 0)
            throw new InvalidOperationException("采样率无效。");
        if (Channel is null || !Channels.Contains(Channel)) throw new InvalidOperationException("请选择有效分析导联。");
        if (!double.IsFinite(HighPassHz) || !double.IsFinite(LowPassHz) || HighPassHz <= 0 ||
            LowPassHz <= HighPassHz || LowPassHz >= samplingRateHz / 2 || samplingRateHz <= 100)
            throw new InvalidOperationException("高低通必须满足 0 < 高通 < 低通 < 奈奎斯特频率；动态 PSD 要求采样率大于 100 Hz。");
        if (!WindowOptions.Contains(WindowSeconds)) throw new InvalidOperationException("请选择算法声明的动态窗口。");
        if (NotchHz is not (0 or 50 or 60)) throw new InvalidOperationException("陷波只能关闭、50 Hz 或 60 Hz。");
        var selectedMontage = montage ?? Montage;
        var outputs = selectedMontage?.Outputs.Where(output => output.Available &&
                string.Equals(output.Id, Channel, StringComparison.OrdinalIgnoreCase))
            .Select(output => new { role = output.Role, output = output.Id, unit = output.Unit }).ToArray();
        if (selectedMontage is not null && (outputs is null || outputs.Length != 1))
            throw new InvalidOperationException("分析导联未映射到当前历史导联快照。");
        return JsonSerializer.SerializeToElement(new
        {
            algorithm_id = "psd", scientific_version = ScientificVersion,
            channel = Channel, mode = "dynamic", time = new { start_s = 0d, end_s = durationSeconds },
            low_hz = HighPassHz, high_hz = LowPassHz, notch_hz = NotchHz == 0 ? (double?)null : NotchHz,
            dynamic_window_s = WindowSeconds, refresh_step_s = StepSeconds,
            analysis_montage_id = selectedMontage?.Id,
            analysis_montage_revision = selectedMontage?.Revision,
            analysis_montage_fingerprint = selectedMontage?.Fingerprint,
            analysis_montage_outputs = outputs,
        });
    }

    private static IReadOnlyList<string> AvailableChannels(RegisteredRecording recording,
        HistoricalMontageDescriptor? montage)
    {
        if (montage is null) return recording.Channels;
        return montage.Outputs.Where(output => output.Available)
            .Select(output => output.Id).ToArray();
    }
}
