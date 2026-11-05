using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>PSD-owned scientific inputs for the shared Review window channel.</summary>
public sealed class ReviewPsdParameters : ObservableObject
{
    private double lowHz;
    private double highHz;
    private double notchHz;

    public IReadOnlyList<string> Channels { get; private set; } = [];
    public double WindowSeconds { get; private set; }
    public double StepSeconds { get; private set; }
    public string? ScientificVersion { get; private set; }

    public void Initialize(RegisteredRecording recording, AlgorithmCatalogItem algorithm,
        HistoricalMontageDescriptor? montage)
    {
        if (algorithm.Id != "psd") throw new ArgumentException("需要 PSD 算法声明。", nameof(algorithm));
        ScientificVersion = algorithm.Version;
        WindowSeconds = algorithm.DynamicPolicy.DefaultWindowSeconds;
        StepSeconds = algorithm.DynamicPolicy.RefreshStepSeconds;
        LowHz = ParameterDefault(algorithm, "low_hz");
        HighHz = ParameterDefault(algorithm, "high_hz");
        NotchHz = ParameterDefault(algorithm, "notch_hz");
        if (LowHz <= 0 || HighHz <= LowHz || NotchHz is not (0 or 50 or 60))
            throw new InvalidOperationException("PSD catalog 默认滤波参数不符合当前算法契约。");
        UpdateChannels(recording, montage);
    }

    public void RefreshInputOptions(RegisteredRecording recording, HistoricalMontageDescriptor? montage) =>
        UpdateChannels(recording, montage);

    public JsonElement BuildConfig(double durationSeconds, double samplingRateHz,
        HistoricalMontageDescriptor? montage)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 ||
            !double.IsFinite(samplingRateHz) || samplingRateHz <= 100)
            throw new InvalidOperationException("PSD 需要有效录音时长与高于 100 Hz 的采样率。");
        if (Channels.Count == 0) throw new InvalidOperationException("PSD 分析通道集为空。");
        if (string.IsNullOrWhiteSpace(ScientificVersion) ||
            !double.IsFinite(WindowSeconds) || !double.IsFinite(StepSeconds) || WindowSeconds <= 0 || StepSeconds <= 0)
            throw new InvalidOperationException("PSD 科学版本或动态窗口声明无效。");

        return JsonSerializer.SerializeToElement(new
        {
            algorithm_id = "psd", scientific_version = ScientificVersion,
            channels = Channels, mode = "dynamic", low_hz = LowHz, high_hz = HighHz,
            notch_hz = NotchHz == 0 ? (double?)null : NotchHz,
            time = new { start_s = 0d, end_s = durationSeconds },
            dynamic_window_s = WindowSeconds, refresh_step_s = StepSeconds,
            analysis_montage_id = montage?.Id, analysis_montage_revision = montage?.Revision,
            analysis_montage_fingerprint = montage?.Fingerprint,
            analysis_montage_outputs = montage?.Outputs.Where(output => output.Available &&
                Channels.Contains(output.Id, StringComparer.OrdinalIgnoreCase))
                .Select(output => new { role = output.Role, output = output.Id, unit = output.Unit }).ToArray(),
        });
    }

    private void UpdateChannels(RegisteredRecording recording, HistoricalMontageDescriptor? montage)
    {
        Channels = montage is null ? recording.Channels : montage.Outputs
            .Where(output => output.Available).Select(output => output.Id).ToArray();
        RaisePropertyChanged(nameof(Channels));
    }

    private double LowHz { get => lowHz; set => SetProperty(ref lowHz, value); }
    private double HighHz { get => highHz; set => SetProperty(ref highHz, value); }
    private double NotchHz { get => notchHz; set => SetProperty(ref notchHz, value); }

    private static double ParameterDefault(AlgorithmCatalogItem algorithm, string key)
    {
        var parameter = algorithm.Parameters.SingleOrDefault(item => item.Key == key);
        if (parameter?.Default is not { ValueKind: JsonValueKind.Number } value ||
            !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new InvalidOperationException($"PSD catalog 未声明有效的 {key} 默认值。");
        return number;
    }
}
