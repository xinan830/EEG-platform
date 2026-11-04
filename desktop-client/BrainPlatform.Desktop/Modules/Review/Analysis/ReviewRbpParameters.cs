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
            RaisePropertyChanged(nameof(AnalysisChannels));
        }
    }
    public IReadOnlyList<string> AnalysisChannels
    {
        get
        {
            var channels = (Montage?.Outputs ?? [])
                .Where(output => output.Available)
                .Select(output => output.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return channels.Length > 0 ? channels : Channel is { Length: > 0 } selected ? [selected] : [];
        }
    }
    public double WindowSeconds { get; private set; } = 5;
    public double StepSeconds { get; private set; } = 1;
    public string? ScientificVersion { get; private set; }
    public HistoricalMontageDescriptor? Montage { get; private set; }

    public void Initialize(RegisteredRecording recording, AlgorithmCatalogItem algorithm,
        HistoricalMontageDescriptor? montage = null)
    {
        Montage = montage;
        Channels = AvailableChannels(recording, montage);
        Channel = Channels.Contains(Channel ?? "", StringComparer.OrdinalIgnoreCase)
            ? Channel : Channels.FirstOrDefault();
        WindowSeconds = algorithm.DynamicPolicy.DefaultWindowSeconds;
        StepSeconds = algorithm.DynamicPolicy.RefreshStepSeconds;
        ScientificVersion = algorithm.Version;
        RaisePropertyChanged(nameof(Channels));
        RaisePropertyChanged(nameof(WindowSeconds));
        RaisePropertyChanged(nameof(StepSeconds));
        RaisePropertyChanged(nameof(AnalysisChannels));
    }

    public void SelectMontage(RegisteredRecording recording, HistoricalMontageDescriptor? montage)
    {
        if (string.Equals(Montage?.Id, montage?.Id, StringComparison.Ordinal) &&
            string.Equals(Montage?.Fingerprint, montage?.Fingerprint, StringComparison.Ordinal)) return;
        var previousRole = Montage?.Outputs.FirstOrDefault(output =>
            string.Equals(output.Id, Channel, StringComparison.OrdinalIgnoreCase))?.Role;
        Montage = montage;
        Channels = AvailableChannels(recording, montage);
        var mapped = previousRole is null ? null : montage?.Outputs.FirstOrDefault(output =>
            output.Available && string.Equals(output.Role, previousRole, StringComparison.OrdinalIgnoreCase))?.Id;
        Channel = mapped is not null && Channels.Contains(mapped, StringComparer.OrdinalIgnoreCase)
            ? mapped : Channels.FirstOrDefault();
        RaisePropertyChanged(nameof(Montage));
        RaisePropertyChanged(nameof(Channels));
        RaisePropertyChanged(nameof(Channel));
        RaisePropertyChanged(nameof(AnalysisChannels));
    }

    public JsonElement BuildConfig(double durationSeconds, double samplingRateHz)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new InvalidOperationException("记录时长必须是正数。");
        if (!double.IsFinite(samplingRateHz) || samplingRateHz <= 100)
            throw new InvalidOperationException("RBP 需要采样率高于 100 Hz。");
        if (Channel is null || !Channels.Contains(Channel, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择有效 RBP 导联。");
        if (!double.IsFinite(WindowSeconds) || !double.IsFinite(StepSeconds) || WindowSeconds <= 0 || StepSeconds <= 0)
            throw new InvalidOperationException("RBP 动态窗口参数无效。");

        var channels = AnalysisChannels;
        return JsonSerializer.SerializeToElement(new
        {
            algorithm_id = "rbp", scientific_version = ScientificVersion,
            channel = Channel, channels, mode = "dynamic",
            time = new { start_s = 0d, end_s = durationSeconds },
            dynamic_window_s = WindowSeconds, refresh_step_s = StepSeconds,
            analysis_montage_id = Montage?.Id,
            analysis_montage_revision = Montage?.Revision,
            analysis_montage_fingerprint = Montage?.Fingerprint,
            analysis_montage_outputs = Montage?.Outputs.Where(output => output.Available)
                .Select(output => new { role = output.Role, output = output.Id, unit = output.Unit }).ToArray(),
        });
    }

    private static IReadOnlyList<string> AvailableChannels(RegisteredRecording recording,
        HistoricalMontageDescriptor? montage) => montage is null
        ? recording.Channels
        : montage.Outputs.Where(output => output.Available).Select(output => output.Id).ToArray();
}
