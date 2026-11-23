using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Acquisition.Runtime;

public sealed record LiveMontageSnapshot(
    MontageProfile Profile,
    long Revision,
    long EffectiveFromSampleCounter,
    IReadOnlyList<string> OrderedChannelNames,
    string Fingerprint);

/// <summary>Owns the versioned montage view for one live acquisition session.</summary>
public sealed class LiveMontageCoordinator
{
    private readonly string channelConfigurationFingerprint;
    private readonly object gate = new();
    private readonly List<LiveMontageSnapshot> revisions = [];
    private LiveMontageSnapshot current;

    public LiveMontageCoordinator(MontageProfile initialProfile)
    {
        ArgumentNullException.ThrowIfNull(initialProfile);
        channelConfigurationFingerprint = initialProfile.ChannelConfigurationFingerprint;
        current = CreateSnapshot(initialProfile, revision: 1, effectiveFromSampleCounter: 0);
        revisions.Add(current);
    }

    public LiveMontageSnapshot Current => Volatile.Read(ref current);

    public LiveMontageSnapshot Capture(long firstSampleCounter)
    {
        if (firstSampleCounter < 0) throw new ArgumentOutOfRangeException(nameof(firstSampleCounter));
        lock (gate)
        {
            for (var index = revisions.Count - 1; index >= 0; index--)
            {
                if (revisions[index].EffectiveFromSampleCounter <= firstSampleCounter) return revisions[index];
            }
            throw new InvalidOperationException("采集批次早于实时导联配置的生效边界。");
        }
    }

    public LiveMontageSnapshot Select(MontageProfile profile, long effectiveFromSampleCounter)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (effectiveFromSampleCounter < 0)
            throw new ArgumentOutOfRangeException(nameof(effectiveFromSampleCounter));
        if (!string.Equals(profile.ChannelConfigurationFingerprint, channelConfigurationFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("实时导联必须使用当前采集通道配置。");

        var candidate = CreateSnapshot(profile, 0, effectiveFromSampleCounter);
        lock (gate)
        {
            var previous = current;
            if (candidate.Fingerprint == previous.Fingerprint) return previous;
            if (effectiveFromSampleCounter < previous.EffectiveFromSampleCounter)
                throw new InvalidOperationException("实时导联生效样本不能早于当前配置边界。");

            current = candidate with { Revision = checked(previous.Revision + 1) };
            revisions.Add(current);
            return current;
        }
    }

    private static LiveMontageSnapshot CreateSnapshot(
        MontageProfile profile,
        long revision,
        long effectiveFromSampleCounter)
    {
        var orderedChannels = profile.DerivedChannels
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => channel.Name.Trim())
            .ToArray();
        if (orderedChannels.Length == 0 || orderedChannels.Any(string.IsNullOrWhiteSpace) ||
            orderedChannels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != orderedChannels.Length)
            throw new InvalidOperationException("实时导联必须提供唯一且有序的输出通道。");

        var fingerprint = string.IsNullOrWhiteSpace(profile.Fingerprint)
            ? MontageProfileFingerprint.Create(
                profile.ChannelConfigurationFingerprint, profile.DerivedChannels, profile.AverageReferenceLabels)
            : profile.Fingerprint;
        var capturedProfile = profile with
        {
            DerivedChannels = profile.DerivedChannels.ToArray(),
            AverageReferenceLabels = profile.AverageReferenceLabels?.ToArray(),
            ChannelConfigurationSnapshot = profile.ChannelConfigurationSnapshot with
            {
                Channels = profile.ChannelConfigurationSnapshot.Channels.ToArray(),
            },
        };
        return new LiveMontageSnapshot(capturedProfile, revision, effectiveFromSampleCounter, orderedChannels, fingerprint);
    }
}
