using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Shared;

/// <summary>Catalog-driven TNK result boundary.</summary>
internal sealed class TnkShadowRunResultHandler : IAlgorithmRunResultHandler
{
    private static readonly string[] RequiredRoles = ["Fz", "Pz", "Oz", "F3", "F4"];

    public TnkShadowRunResultHandler(string algorithmId) => AlgorithmId = algorithmId;

    public string AlgorithmId { get; }
    public bool SupportsOnDemandWindows =>
        OfflineAnalysisAvailability.IsOnDemandEnabled(AlgorithmId, enabledByDefault: true);
    public AlgorithmRunParameters ParseParameters(AlgorithmRunInputs inputs)
    {
        var roles = inputs.RoleChannels ?? new Dictionary<string, string>();
        if (roles.Count != RequiredRoles.Length || RequiredRoles.Any(role => !roles.ContainsKey(role)) ||
            roles.Keys.Any(role => !RequiredRoles.Contains(role, StringComparer.Ordinal)))
            throw new InvalidOperationException("TNK 脑指标组需要为 Fz、Pz、Oz、F3、F4 五个角色分别选择来源通道。");
        foreach (var role in roles)
        {
            if (string.IsNullOrWhiteSpace(role.Value))
                throw new InvalidOperationException($"TNK 必需角色 {role.Key} 尚未选择来源通道。请在“分析参数”中选择一个有效通道后再运行。");
            if (!inputs.Channels.Contains(role.Value))
                throw new InvalidOperationException($"TNK 角色 {role.Key} 的来源通道 {role.Value} 已不属于当前导联配置，请在“分析参数”中重新选择。");
        }
        var selectedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            if (!selectedChannels.Add(role.Value))
                throw new InvalidOperationException($"TNK 角色 {role.Key} 必须选择不同于其他角色的来源通道，请在“分析参数”中调整重复选择。");
        }
        return new(RoleChannels: roles, PrimaryChannel: roles["Fz"]);
    }

    public void ApplyConfigFields(IDictionary<string, object?> payload, string channel, string? secondaryChannel)
    {
        payload["channel"] = channel;
    }

    public void ApplyConfigFields(IDictionary<string, object?> payload, string channel, string? secondaryChannel,
        IReadOnlyDictionary<string, string>? roleChannels)
    {
        if (roleChannels is null || !roleChannels.TryGetValue("Fz", out var primaryChannel) ||
            string.IsNullOrWhiteSpace(primaryChannel))
            throw new InvalidOperationException("TNK 脑指标组尚未选择 Fz 来源通道。");
        payload["channel"] = primaryChannel;
        payload["tnk_role_mapping"] = roleChannels
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    }

    public void ApplyWindowResult(AlgorithmListViewModel catalog, DynamicWindowResponse response)
    {
        var state = AlgorithmListViewModel.WindowStateLabel(response.State);
        var failure = response.Failure is { } error
            ? $"；{error.Code}：{error.Message}"
            : string.Empty;
        var metricCount = response.Result?.Arrays?.Count ?? 0;
        catalog.SetStructuredPreviewTextForModule(
            $"TNK 脑指标窗口：{state}；质量：{response.Quality ?? "unknown"}；" +
            $"已返回 {metricCount} 个指标字段{failure}");
    }

    public Task ApplyAsync(AlgorithmListViewModel catalog, AnalysisRunResponse run, bool dynamic,
        double? preservedDynamicCursorSeconds)
    {
        catalog.SetRunResultTextForModule(
            AlgorithmResultFormatter.BuildResultSummary(run),
            AlgorithmResultFormatter.BuildProvenance(run),
            dynamic
            ? AlgorithmResultFormatter.BuildDynamicSeriesSummary(run)
            : "暂无动态结果。");
        return Task.CompletedTask;
    }
}
