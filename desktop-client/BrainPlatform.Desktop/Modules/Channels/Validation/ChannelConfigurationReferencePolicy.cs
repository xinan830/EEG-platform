namespace BrainPlatform.Desktop.Configuration;

/// <summary>
/// Enforces referential immutability between reusable channel profiles and
/// montage snapshots. Catalogue stores remain persistence-only components.
/// </summary>
public sealed class ChannelConfigurationReferencePolicy
{
    private readonly MontageProfileStore montageStore;

    public ChannelConfigurationReferencePolicy(MontageProfileStore? montageStore = null)
    {
        this.montageStore = montageStore ?? new MontageProfileStore();
    }

    public async Task<IReadOnlyDictionary<string, int>> GetReferenceCountsAsync(
        CancellationToken cancellationToken)
    {
        var montages = await montageStore.LoadAsync(cancellationToken);
        return montages
            .GroupBy(profile => profile.ChannelConfigurationSnapshot.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    public async Task EnsureSignalChangeAllowedAsync(
        ChannelConfigurationProfile original,
        ChannelConfigurationProfile candidate,
        CancellationToken cancellationToken)
    {
        var count = await GetReferenceCountAsync(original.Id, cancellationToken);
        if (count > 0 && !string.Equals(
                ChannelConfigurationFingerprint.Create(original),
                ChannelConfigurationFingerprint.Create(candidate),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"该通道配置已被 {count} 个导联配置引用，不能修改信号映射；请创建新版本。");
        }
    }

    public async Task EnsureDeleteAllowedAsync(
        ChannelConfigurationProfile profile,
        CancellationToken cancellationToken)
    {
        var count = await GetReferenceCountAsync(profile.Id, cancellationToken);
        if (count > 0)
        {
            throw new InvalidOperationException(
                $"该通道配置已被 {count} 个导联配置引用，不能删除；请先删除引用它的导联配置。");
        }
    }

    private async Task<int> GetReferenceCountAsync(string profileId, CancellationToken cancellationToken)
    {
        var counts = await GetReferenceCountsAsync(cancellationToken);
        return counts.GetValueOrDefault(profileId);
    }
}
