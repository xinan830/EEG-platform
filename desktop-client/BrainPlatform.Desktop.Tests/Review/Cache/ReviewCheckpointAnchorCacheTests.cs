
namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewCheckpointAnchorCacheTests
{
    [Fact]
    public void CompatibilityFingerprintCoversEveryScientificIdentityField()
    {
        var baseline = Group();
        var variants = new[]
        {
            Group(sessionId: Guid.Parse("22222222-2222-2222-2222-222222222222")),
            Group(manifest: "manifest-b"),
            Group(rate: 1000),
            Group(schema: "F4|F3|Counter"),
            Group(unit: "uV"),
            Group(settings: new RecordingReviewFilterSettings(0.5, 30, 50)),
            Group(settings: new RecordingReviewFilterSettings(1, 40, 50)),
            Group(settings: new RecordingReviewFilterSettings(1, 30, 60)),
            Group(contract: new ReviewFilterContract("algorithm-b", "contract-a", "checkpoint-a")),
            Group(contract: new ReviewFilterContract("algorithm-a", "contract-b", "checkpoint-a")),
            Group(contract: new ReviewFilterContract("algorithm-a", "contract-a", "checkpoint-b")),
        };

        Assert.All(variants, variant => Assert.NotEqual(baseline.Fingerprint, variant.Fingerprint));
    }

    [Fact]
    public async Task FindsNearestCompletedAnchorWithinTheRequestedSegment()
    {
        var root = TemporaryDirectory();
        try
        {
            var cache = new ReviewCheckpointAnchorCache(root);
            var group = Group();
            await cache.StoreAsync(group, 100, 200, "state-200", CancellationToken.None);
            await cache.StoreAsync(group, 100, 300, "state-300", CancellationToken.None);
            await cache.StoreAsync(group, 500, 600, "other-segment", CancellationToken.None);

            var hit = await cache.FindNearestAsync(group, 100, 350, CancellationToken.None);
            var miss = await cache.FindNearestAsync(group, 500, 550, CancellationToken.None);

            Assert.Equal(300, hit!.NextSampleCounter);
            Assert.Equal("state-300", hit.CheckpointB64);
            Assert.Null(miss);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CorruptAndPartialEntriesAreCacheMisses()
    {
        var root = TemporaryDirectory();
        try
        {
            var cache = new ReviewCheckpointAnchorCache(root);
            var group = Group();
            await cache.StoreAsync(group, 0, 100, "valid", CancellationToken.None);
            var path = Directory.GetFiles(root, "*.bpra", SearchOption.AllDirectories).Single();
            await File.WriteAllTextAsync(path, "{partial");

            Assert.Null(await cache.FindNearestAsync(group, 0, 100, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CountBoundEvictsTheLeastRecentlyUsedDerivedAnchorOnly()
    {
        var root = TemporaryDirectory();
        var raw = Path.Combine(root, "immutable-raw.bin");
        try
        {
            await File.WriteAllTextAsync(raw, "raw-source");
            var cache = new ReviewCheckpointAnchorCache(Path.Combine(root, "cache"), maximumAnchorCount: 2);
            var group = Group();
            await cache.StoreAsync(group, 0, 100, "one", CancellationToken.None);
            await cache.StoreAsync(group, 0, 200, "two", CancellationToken.None);
            await cache.StoreAsync(group, 0, 300, "three", CancellationToken.None);

            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "cache"), "*.bpra", SearchOption.AllDirectories).Length);
            Assert.Equal("raw-source", await File.ReadAllTextAsync(raw));
            Assert.NotNull(await cache.FindNearestAsync(group, 0, 300, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    private static ReviewCheckpointAnchorGroupKey Group(
        Guid? sessionId = null,
        string manifest = "manifest-a",
        int rate = 500,
        string schema = "F3|F4|Counter",
        string unit = "V",
        RecordingReviewFilterSettings? settings = null,
        ReviewFilterContract? contract = null) => new(
            sessionId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
            manifest,
            rate,
            schema,
            unit,
            settings ?? new RecordingReviewFilterSettings(1, 30, 50),
            contract ?? new ReviewFilterContract("algorithm-a", "contract-a", "checkpoint-a"));

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "brain-platform-anchor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
