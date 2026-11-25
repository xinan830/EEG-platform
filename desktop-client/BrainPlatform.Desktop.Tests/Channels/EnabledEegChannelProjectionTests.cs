using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Services;

namespace BrainPlatform.Desktop.Tests.Channels;

public sealed class EnabledEegChannelProjectionTests
{
    [Fact]
    public void ProjectFiltersDisabledChannelsOrdersByProfileAndKeepsSourceIndexes()
    {
        var profile = Profile(
            Entry(8, "F8", true, 0),
            Entry(3, "F3", false, 1),
            Entry(5, "Cz", true, 2));
        var source = new[]
        {
            Source(4, 5, "Cz"),
            Source(1, 8, "F8"),
            Source(2, 3, "F3"),
            new AcquisitionChannel(3, 99, "Counter", AcquisitionChannelKind.SampleCounter, "count"),
        };

        var result = EnabledEegChannelProjection.Project(profile, source);

        Assert.Equal(EegChannelProjectionStatus.Applied, result.Status);
        Assert.Equal(["F8", "Cz"], result.Channels.Select(channel => channel.Label));
        Assert.Equal([1, 4], result.Channels.Select(channel => channel.StreamIndex));
        Assert.Equal([8, 5], result.Channels.Select(channel => channel.NativeChannelIndex));
    }

    [Fact]
    public void ProjectWithoutSnapshotPreservesSourceEegOrder()
    {
        var result = EnabledEegChannelProjection.Project(null,
            [Source(2, 0, "Cz"), Source(0, 1, "F3"), new(1, 90, "Trigger", AcquisitionChannelKind.Trigger, "V")]);

        Assert.Equal(EegChannelProjectionStatus.MissingSnapshot, result.Status);
        Assert.Equal(["F3", "Cz"], result.Channels.Select(channel => channel.Label));
        Assert.Equal([0, 2], result.Channels.Select(channel => channel.StreamIndex));
    }

    [Fact]
    public void ProjectWithMalformedSnapshotMarksInvalidWithoutReexposingDisabledChannels()
    {
        var profile = Profile(Entry(0, "F3", true, 0), Entry(1, "Cz", false, 0));

        var result = EnabledEegChannelProjection.Project(profile, [Source(0, 0, "F3"), Source(1, 1, "Cz")]);

        Assert.Equal(EegChannelProjectionStatus.InvalidSnapshot, result.Status);
        Assert.Empty(result.Channels);
    }

    [Fact]
    public void ProjectWithDuplicateNativeMappingMarksInvalidWithoutReexposingDisabledChannels()
    {
        var profile = Profile(Entry(0, "F3", true, 0), Entry(1, "Cz", false, 1));

        var result = EnabledEegChannelProjection.Project(profile,
            [Source(0, 0, "F3"), Source(1, 0, "F3")]);

        Assert.Equal(EegChannelProjectionStatus.InvalidSnapshot, result.Status);
        Assert.Empty(result.Channels);
    }

    [Fact]
    public void ProjectAcceptsAnImportedSubsetOfTheBoundConfiguration()
    {
        var profile = Profile(Entry(8, "F8", true, 0), Entry(3, "F3", false, 1), Entry(5, "Cz", true, 2));

        var result = EnabledEegChannelProjection.Project(profile, [Source(1, 8, "F8"), Source(4, 5, "Cz")]);

        Assert.Equal(EegChannelProjectionStatus.Applied, result.Status);
        Assert.Equal(["F8", "Cz"], result.Channels.Select(channel => channel.Label));
        Assert.Equal([1, 4], result.Channels.Select(channel => channel.StreamIndex));
    }

    [Fact]
    public void ProjectWithNoEnabledChannelsReturnsAnEmptyVerifiedProjection()
    {
        var profile = Profile(Entry(0, "F3", false, 0), Entry(1, "Cz", false, 1));

        var result = EnabledEegChannelProjection.Project(profile,
            [Source(0, 0, "F3"), Source(1, 1, "Cz")]);

        Assert.Equal(EegChannelProjectionStatus.Applied, result.Status);
        Assert.Empty(result.Channels);
    }

    private static ChannelConfigurationProfile Profile(params ChannelConfigurationEntry[] channels) => new(
        "test", "test", string.Empty, ChannelConfigurationSource.User, "device", channels,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static ChannelConfigurationEntry Entry(int nativeIndex, string label, bool enabled, int order) =>
        new(nativeIndex, AcquisitionChannelKind.Reference, label, enabled, order);

    private static AcquisitionChannel Source(int streamIndex, int nativeIndex, string label) =>
        new(streamIndex, nativeIndex, label, AcquisitionChannelKind.Reference, "V");
}
