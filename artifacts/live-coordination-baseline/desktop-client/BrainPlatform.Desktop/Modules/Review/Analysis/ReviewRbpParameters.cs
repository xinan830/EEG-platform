using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>RBP Review configuration; scientific values remain backend-owned.</summary>
public sealed class ReviewRbpParameters : ObservableObject
{
    private string? channel;

    public IReadOnlyList<string> Channels { get; private set; } = [];
    public string? Channel
    {
        get => channel;
        set
        {
            if (!SetProperty(ref channel, value)) return;
        }
    }
    public double WindowSeconds { get; private set; } = 5;
    public double StepSeconds { get; private set; } = 1;
    public string? ScientificVersion { get; private set; }

    public void Initialize(RegisteredRecording recording, AlgorithmCatalogItem algorithm,
        HistoricalMontageDescriptor? montage = null)
    {
        UpdateChannels(recording, montage);
        Channel = Channels.Contains(Channel ?? "", StringComparer.OrdinalIgnoreCase)
            ? Channel : Channels.FirstOrDefault();
        WindowSeconds = algorithm.DynamicPolicy.DefaultWindowSeconds;
        StepSeconds = algorithm.DynamicPolicy.RefreshStepSeconds;
        ScientificVersion = algorithm.Version;
        RaisePropertyChanged(nameof(Channels));
        RaisePropertyChanged(nameof(WindowSeconds));
        RaisePropertyChanged(nameof(StepSeconds));
    }

    public void RefreshInputOptions(RegisteredRecording recording, HistoricalMontageDescriptor? previous,
        HistoricalMontageDescriptor? montage)
    {
        var previousRole = previous?.Outputs.FirstOrDefault(output =>
            string.Equals(output.Id, Channel, StringComparison.OrdinalIgnoreCase))?.Role;
        UpdateChannels(recording, montage);
        var mapped = previousRole is null ? null : montage?.Outputs.FirstOrDefault(output =>
            output.Available && string.Equals(output.Role, previousRole, StringComparison.OrdinalIgnoreCase))?.Id;
        Channel = mapped is not null && Channels.Contains(mapped, StringComparer.OrdinalIgnoreCase)
            ? mapped : Channels.FirstOrDefault();
        RaisePropertyChanged(nameof(Channel));
    }

    public JsonElement BuildConfig(double durationSeconds, double samplingRateHz,
        HistoricalMontageDescriptor? montage)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new InvalidOperationException("记录时长必须是正数。");
        if (!double.IsFinite(samplingRateHz) || samplingRateHz <= 100)
            throw new InvalidOperationException("RBP 需要采样率高于 100 Hz。");
        if (Channel is null || !Channels.Contains(Channel, StringComparer.OrdinalIgnoreCase))
            throw new ReviewInputMappingException("analysis_channel_unavailable", "当前导联配置没有可用的 RBP 分析通道。", "channel");
        if (!double.IsFinite(WindowSeconds) || !double.IsFinite(StepSeconds) || WindowSeconds <= 0 || StepSeconds <= 0)
            throw new InvalidOperationException("RBP 动态窗口参数无效。");

        var channels = ResolveAnalysisChannels(montage);
        return JsonSerializer.SerializeToElement(new
        {
            algorithm_id = "rbp", scientific_version = ScientificVersion,
            channel = Channel, channels, mode = "dynamic",
            time = new { start_s = 0d, end_s = durationSeconds },
            dynamic_window_s = WindowSeconds, refresh_step_s = StepSeconds,
            analysis_montage_id = montage?.Id,
            analysis_montage_revision = montage?.Revision,
            analysis_montage_fingerprint = montage?.Fingerprint,
            analysis_montage_outputs = montage?.Outputs.Where(output => output.Available)
                .Select(output => new { role = output.Role, output = output.Id, unit = output.Unit }).ToArray(),
        });
    }

    private static IReadOnlyList<string> AvailableChannels(RegisteredRecording recording,
        HistoricalMontageDescriptor? montage) => montage is null
        ? recording.Channels
        : montage.Outputs.Where(output => output.Available).Select(output => output.Id).ToArray();

    private void UpdateChannels(RegisteredRecording recording, HistoricalMontageDescriptor? montage)
    {
        Channels = AvailableChannels(recording, montage);
        RaisePropertyChanged(nameof(Channels));
    }

    public IReadOnlyList<string> ResolveAnalysisChannels(HistoricalMontageDescriptor? montage)
    {
        var channels = montage?.Outputs.Where(output => output.Available).Select(output => output.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return channels is { Length: > 0 } ? channels : Channels;
    }
}
