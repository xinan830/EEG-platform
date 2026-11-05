using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Review inputs for scalar metrics; channel roles stay explicit in the request.</summary>
public sealed class ReviewScalarParameters(string algorithmId, string? primaryRole = null,
    string? secondaryRole = null, string? secondaryConfigField = null,
    IReadOnlyDictionary<string, object?>? additionalFields = null) : ObservableObject
{
    private string? channel;
    private string? secondaryChannel;
    private string frequencyMode = "standard";

    public string AlgorithmId { get; } = algorithmId;
    public IReadOnlyList<string> Channels { get; private set; } = [];
    public IReadOnlyList<string> SecondaryChannels { get; private set; } = [];
    public string? Channel { get => channel; set => SetProperty(ref channel, value); }
    public string? SecondaryChannel { get => secondaryChannel; set => SetProperty(ref secondaryChannel, value); }
    public string? ScientificVersion { get; private set; }
    public double WindowSeconds { get; private set; }
    public double StepSeconds { get; private set; }
    public bool IsThetaBeta => AlgorithmId == "theta_beta";
    public string FrequencyMode { get => frequencyMode; set => SetProperty(ref frequencyMode, value); }

    public void Initialize(RegisteredRecording recording, AlgorithmCatalogItem algorithm,
        HistoricalMontageDescriptor? montage)
    {
        if (algorithm.Id != AlgorithmId) throw new ArgumentException("算法目录项与回溯参数不匹配。", nameof(algorithm));
        ScientificVersion = algorithm.Version;
        WindowSeconds = algorithm.DynamicPolicy.DefaultWindowSeconds;
        StepSeconds = algorithm.DynamicPolicy.RefreshStepSeconds;
        UpdateSources(recording, null, montage);
    }

    public void RefreshInputOptions(RegisteredRecording recording, HistoricalMontageDescriptor? previous,
        HistoricalMontageDescriptor? montage) => UpdateSources(recording, previous, montage);

    public JsonElement BuildConfig(double durationSeconds, double samplingRateHz,
        HistoricalMontageDescriptor? montage)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 ||
            !double.IsFinite(samplingRateHz) || samplingRateHz <= 100 ||
            string.IsNullOrWhiteSpace(ScientificVersion) || WindowSeconds <= 0 || StepSeconds <= 0)
            throw new InvalidOperationException($"{AlgorithmId} 的科学版本、采样率或窗口声明无效。");
        if (Channel is null || !Channels.Contains(Channel, StringComparer.OrdinalIgnoreCase))
            throw new ReviewInputMappingException("required_role_unavailable",
                $"{AlgorithmId} 无法从当前导联配置映射角色 {primaryRole ?? "channel"}。", primaryRole ?? "channel");

        var paired = secondaryRole is not null;
        if (paired && (SecondaryChannel is null ||
            !SecondaryChannels.Contains(SecondaryChannel, StringComparer.OrdinalIgnoreCase) ||
            string.Equals(Channel, SecondaryChannel, StringComparison.OrdinalIgnoreCase)))
            throw new ReviewInputMappingException("required_role_unavailable",
                $"{AlgorithmId} 无法从当前导联配置映射角色 {secondaryRole}。", secondaryRole);

        var sources = paired ? new[] { Channel, SecondaryChannel! } : [Channel];
        var config = new Dictionary<string, object?>
        {
            ["algorithm_id"] = AlgorithmId,
            ["scientific_version"] = ScientificVersion,
            ["channel"] = Channel,
            ["channels"] = sources,
            ["mode"] = "dynamic",
            ["time"] = new { start_s = 0d, end_s = durationSeconds },
            ["dynamic_window_s"] = WindowSeconds,
            ["refresh_step_s"] = StepSeconds,
        };
        if (IsThetaBeta) config["frequency_mode"] = FrequencyMode;
        if (secondaryConfigField is not null) config[secondaryConfigField] = SecondaryChannel;
        if (additionalFields is not null)
            foreach (var (name, value) in additionalFields) config[name] = value;
        if (montage is not null)
        {
            config["analysis_montage_id"] = montage.Id;
            config["analysis_montage_revision"] = montage.Revision;
            config["analysis_montage_fingerprint"] = montage.Fingerprint;
            config["analysis_montage_outputs"] = montage.Outputs.Where(output => output.Available &&
                    sources.Contains(output.Id, StringComparer.OrdinalIgnoreCase))
                .Select(output => new { role = output.Role, output = output.Id, unit = output.Unit }).ToArray();
        }
        return JsonSerializer.SerializeToElement(config);
    }

    private void UpdateSources(RegisteredRecording recording, HistoricalMontageDescriptor? previous,
        HistoricalMontageDescriptor? montage)
    {
        var previousRole = previous?.Outputs.FirstOrDefault(output => output.Id == Channel)?.Role;
        var allChannels = montage is null ? recording.Channels : montage.Outputs.Where(output => output.Available)
            .Select(output => output.Id).ToArray();
        Channels = primaryRole is null ? allChannels : ChannelsForRole(recording, montage, primaryRole);
        SecondaryChannels = secondaryRole is null ? allChannels : ChannelsForRole(recording, montage, secondaryRole);
        var preferred = previousRole ?? primaryRole;
        var first = montage?.Outputs.FirstOrDefault(output => output.Available &&
            string.Equals(output.Role, preferred, StringComparison.OrdinalIgnoreCase))?.Id;
        first ??= Channels.FirstOrDefault(value => string.Equals(value, preferred, StringComparison.OrdinalIgnoreCase));
        Channel = Channels.FirstOrDefault(value => string.Equals(value, Channel, StringComparison.OrdinalIgnoreCase))
            ?? first ?? (primaryRole is null ? Channels.FirstOrDefault() : null);
        var second = secondaryRole is null ? null : montage?.Outputs.FirstOrDefault(output => output.Available &&
            string.Equals(output.Role, secondaryRole, StringComparison.OrdinalIgnoreCase))?.Id;
        second ??= secondaryRole is null ? null : SecondaryChannels.FirstOrDefault(value =>
            string.Equals(value, secondaryRole, StringComparison.OrdinalIgnoreCase));
        SecondaryChannel = SecondaryChannels.FirstOrDefault(value =>
            string.Equals(value, SecondaryChannel, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, Channel, StringComparison.OrdinalIgnoreCase))
            ?? SecondaryChannels.FirstOrDefault(value => !string.Equals(value, Channel, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value, second, StringComparison.OrdinalIgnoreCase));
        RaisePropertyChanged(nameof(Channels));
        RaisePropertyChanged(nameof(SecondaryChannels));
    }

    private static IReadOnlyList<string> ChannelsForRole(RegisteredRecording recording,
        HistoricalMontageDescriptor? montage, string role) => montage is null
        ? recording.Channels.Where(channel => string.Equals(channel, role, StringComparison.OrdinalIgnoreCase)).ToArray()
        : montage.Outputs.Where(output => output.Available &&
            string.Equals(output.Role, role, StringComparison.OrdinalIgnoreCase)).Select(output => output.Id).ToArray();
}
