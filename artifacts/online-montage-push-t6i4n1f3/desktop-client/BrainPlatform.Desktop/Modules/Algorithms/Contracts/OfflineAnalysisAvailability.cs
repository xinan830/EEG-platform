namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

/// <summary>Module-approved availability with an optional maintenance override.</summary>
internal static class OfflineAnalysisAvailability
{
    internal const string FullOnly = "全量分析";
    internal const string OnDemandOnly = "按需分析";
    internal const string OnDemandWithBackgroundFull = "按需预览 + 后台全量";

    internal static bool IsOnDemandEnabled(string algorithmId, bool enabledByDefault = false)
    {
        var value = Environment.GetEnvironmentVariable(
            $"BRAIN_WPF_ON_DEMAND_{algorithmId.ToUpperInvariant()}");
        return string.IsNullOrEmpty(value) ? enabledByDefault : value == "1";
    }
}
