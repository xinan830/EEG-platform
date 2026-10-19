using BrainPlatform.Desktop.Modules.Acquisition.Contracts;
using BrainPlatform.Desktop.Modules.Review.Filtering;
using BrainPlatform.Desktop.Modules.Review.Playback;
using BrainPlatform.Desktop.Modules.Review.Waveform;

namespace BrainPlatform.Desktop.Tests.Review.Waveform;

public sealed class ReviewDisplaySourceIdentityTests
{
    [Fact]
    public void Raw_and_filtered_sources_never_share_identity()
    {
        var manifest = CreateManifest();
        var settings = new RecordingReviewFilterSettings(1, 30, 50);
        var contract = new ReviewFilterContract("filter-v1", "contract-a", "checkpoint-v1");

        var raw = ReviewDisplaySourceIdentity.CreateRaw(manifest, null);
        var filtered = ReviewDisplaySourceIdentity.CreateFiltered(manifest, null, settings, contract);

        Assert.NotEqual(raw, filtered);
    }

    [Fact]
    public void Same_scientific_display_inputs_have_a_stable_identity()
    {
        var manifest = CreateManifest();
        var settings = new RecordingReviewFilterSettings(1, 30, 50);
        var contract = new ReviewFilterContract("filter-v1", "contract-a", "checkpoint-v1");

        var first = ReviewDisplaySourceIdentity.CreateFiltered(manifest, null, settings, contract);
        var second = ReviewDisplaySourceIdentity.CreateFiltered(manifest, null, settings, contract);

        Assert.Equal(first, second);
    }

    private static LocalRawRecordingManifest CreateManifest() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        "binary-v1", "V", "ant", "ANT", 250, 0,
        DateTimeOffset.UnixEpoch,
        [new AcquisitionChannel(0, 0, "F3", AcquisitionChannelKind.Reference, "V")],
        new AcquisitionProjectContext("p", "p", "Project", "D:\\project", "{}"),
        new Dictionary<string, string>());
}
