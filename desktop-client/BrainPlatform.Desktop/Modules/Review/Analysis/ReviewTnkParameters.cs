using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Review.Analysis;

public sealed class ReviewTnkRoleSelection(string role) : ObservableObject
{
    private string? selectedChannel;
    public string Role { get; } = role;
    public string? SelectedChannel
    {
        get => selectedChannel;
        set => SetProperty(ref selectedChannel, value);
    }
    public IReadOnlyList<string> Channels { get; private set; } = [];

    internal void SetChannels(IReadOnlyList<string> channels, string? preferred)
    {
        Channels = channels;
        if (selectedChannel is null || !channels.Contains(selectedChannel, StringComparer.OrdinalIgnoreCase))
            selectedChannel = preferred is not null && channels.Contains(preferred, StringComparer.OrdinalIgnoreCase)
                ? preferred : null;
        RaisePropertyChanged(nameof(Channels));
        RaisePropertyChanged(nameof(SelectedChannel));
    }
}

/// <summary>Explicit, distinct source-channel mapping for TNK's five scientific roles.</summary>
public sealed class ReviewTnkParameters : ObservableObject
{
    private static readonly string[] RequiredRoles = ["Fz", "Pz", "Oz", "F3", "F4"];
    private AlgorithmCatalogItem? algorithm;
    public IReadOnlyList<ReviewTnkRoleSelection> Roles { get; } = RequiredRoles.Select(role =>
        new ReviewTnkRoleSelection(role)).ToArray();
    public HistoricalMontageDescriptor? Montage { get; private set; }
    public string? ScientificVersion { get; private set; }
    public double WindowSeconds { get; private set; }
    public double StepSeconds { get; private set; }
    public bool IsInitialized => algorithm is not null;

    public ReviewTnkParameters()
    {
        foreach (var role in Roles)
            role.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ReviewTnkRoleSelection.SelectedChannel))
                    RaisePropertyChanged(nameof(RoleMapping));
            };
    }

    public string RoleMapping => string.Join(" · ", Roles.Select(role =>
        $"{role.Role} → {role.SelectedChannel ?? "未选择"}"));

    public void Initialize(RegisteredRecording recording, AlgorithmCatalogItem algorithm,
        HistoricalMontageDescriptor? montage)
    {
        if (algorithm.Id != "tnk_biofeedback") throw new ArgumentException("TNK 目录项不匹配。", nameof(algorithm));
        this.algorithm = algorithm;
        Montage = montage;
        ScientificVersion = algorithm.Version;
        WindowSeconds = algorithm.DynamicPolicy.DefaultWindowSeconds;
        StepSeconds = algorithm.DynamicPolicy.RefreshStepSeconds;
        var channels = montage is null ? recording.Channels : montage.Outputs.Where(item => item.Available)
            .Select(item => item.Id).Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var role in Roles)
        {
            var preferred = montage?.Outputs.FirstOrDefault(item => item.Available &&
                string.Equals(item.Role, role.Role, StringComparison.OrdinalIgnoreCase))?.Id;
            preferred ??= channels.FirstOrDefault(item => string.Equals(item, role.Role, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item, $"{role.Role}-REF", StringComparison.OrdinalIgnoreCase));
            role.SetChannels(channels, preferred);
        }
        RaisePropertyChanged(nameof(Montage));
        RaisePropertyChanged(nameof(ScientificVersion));
        RaisePropertyChanged(nameof(WindowSeconds));
        RaisePropertyChanged(nameof(StepSeconds));
        RaisePropertyChanged(nameof(RoleMapping));
    }

    public void SelectMontage(RegisteredRecording recording, HistoricalMontageDescriptor? montage)
    {
        // Review can change its viewing montage before TNK is opened; Initialize receives the current montage when the panel is prepared.
        if (algorithm is null) return;
        if (Montage?.Id == montage?.Id && Montage?.Fingerprint == montage?.Fingerprint) return;
        Initialize(recording, algorithm, montage);
    }

    public JsonElement BuildConfig(double durationSeconds, double samplingRateHz)
    {
        var algorithm = this.algorithm ?? throw new InvalidOperationException("TNK 算法目录尚未初始化。");
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 ||
            !double.IsFinite(samplingRateHz) || samplingRateHz <= 100 ||
            string.IsNullOrWhiteSpace(ScientificVersion) || WindowSeconds <= 0 || StepSeconds <= 0)
            throw new InvalidOperationException("TNK 科学版本、采样率或窗口策略无效。");
        var roleChannels = Roles.ToDictionary(role => role.Role,
            role => role.SelectedChannel ?? "", StringComparer.Ordinal);
        if (roleChannels.Count != RequiredRoles.Length || RequiredRoles.Any(role =>
                !roleChannels.TryGetValue(role, out var channel) || string.IsNullOrWhiteSpace(channel)))
            throw new InvalidOperationException("TNK 需要为 Fz、Pz、Oz、F3、F4 分别选择来源通道。");
        if (roleChannels.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != RequiredRoles.Length)
            throw new InvalidOperationException("TNK 五个角色必须选择互不重复的来源通道。");
        var validChannels = Roles.SelectMany(role => role.Channels).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (roleChannels.Values.Any(channel => !validChannels.Contains(channel)))
            throw new InvalidOperationException("TNK 角色映射包含当前导联配置中不存在的来源通道。");
        return AlgorithmRunConfiguration.BuildRunConfig(algorithm, roleChannels["Fz"],
            new TimeRange(0, durationSeconds), "dynamic", windowSeconds: WindowSeconds,
            stepSeconds: StepSeconds, montage: Montage, roleChannels: roleChannels);
    }
}
