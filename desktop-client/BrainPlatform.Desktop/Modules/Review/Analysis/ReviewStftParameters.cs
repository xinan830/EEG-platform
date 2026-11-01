using System.Text.Json;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Single-channel STFT configuration, independent of waveform display filters.</summary>
public sealed class ReviewStftParameters : ObservableObject
{
    private string? channel;
    public IReadOnlyList<string> Channels { get; private set; } = [];
    public string? Channel { get => channel; set => SetProperty(ref channel, value); }
    public HistoricalMontageDescriptor? Montage { get; private set; }
    public double WindowSeconds { get; private set; }
    public double StepSeconds { get; private set; }
    public string? ScientificVersion { get; private set; }

    public void Initialize(RegisteredRecording recording, AlgorithmCatalogItem algorithm,
        HistoricalMontageDescriptor? montage)
    {
        if (algorithm.Id != "stft") throw new ArgumentException("需要 STFT 算法声明。", nameof(algorithm));
        ScientificVersion = algorithm.Version;
        WindowSeconds = algorithm.DynamicPolicy.DefaultWindowSeconds;
        StepSeconds = algorithm.DynamicPolicy.RefreshStepSeconds;
        UpdateChannels(recording, montage);
    }

    public void SelectMontage(RegisteredRecording recording, HistoricalMontageDescriptor? montage)
    {
        if (Montage?.Id == montage?.Id && Montage?.Fingerprint == montage?.Fingerprint) return;
        UpdateChannels(recording, montage);
        RaisePropertyChanged(nameof(Montage));
    }

    private void UpdateChannels(RegisteredRecording recording, HistoricalMontageDescriptor? montage)
    {
        var previousRole = Montage?.Outputs.FirstOrDefault(output => output.Id == Channel)?.Role;
        Montage = montage;
        Channels = montage is null ? recording.Channels : montage.Outputs
            .Where(output => output.Available).Select(output => output.Id).ToArray();
        var mapped = previousRole is null ? Channel : montage?.Outputs.FirstOrDefault(output =>
            output.Available && string.Equals(output.Role, previousRole, StringComparison.OrdinalIgnoreCase))?.Id;
        Channel = Channels.FirstOrDefault(value => string.Equals(value, mapped, StringComparison.OrdinalIgnoreCase))
            ?? Channels.FirstOrDefault();
        RaisePropertyChanged(nameof(Channels));
    }

    public JsonElement BuildConfig(double durationSeconds, double samplingRateHz)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 ||
            !double.IsFinite(samplingRateHz) || samplingRateHz <= 100)
            throw new InvalidOperationException("STFT 需要有效录音时长与高于 100 Hz 的采样率。");
        if (Channel is null || !Channels.Contains(Channel, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择有效 STFT 导联。");
        if (string.IsNullOrWhiteSpace(ScientificVersion) || !double.IsFinite(WindowSeconds) ||
            !double.IsFinite(StepSeconds) || WindowSeconds <= 0 || StepSeconds <= 0)
            throw new InvalidOperationException("STFT 科学版本或动态窗口声明无效。");
        return JsonSerializer.SerializeToElement(new
        {
            algorithm_id = "stft", scientific_version = ScientificVersion,
            channel = Channel, mode = "dynamic", low_hz = 1d, high_hz = 50d,
            time = new { start_s = 0d, end_s = durationSeconds },
            dynamic_window_s = WindowSeconds, refresh_step_s = StepSeconds,
            analysis_montage_id = Montage?.Id, analysis_montage_revision = Montage?.Revision,
            analysis_montage_fingerprint = Montage?.Fingerprint,
            analysis_montage_outputs = Montage?.Outputs.Where(output => output.Available &&
                string.Equals(output.Id, Channel, StringComparison.OrdinalIgnoreCase))
                .Select(output => new { role = output.Role, output = output.Id, unit = output.Unit }).ToArray(),
        });
    }
}
