using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class TnkPlaybackEventContractTests
{
    [Fact]
    public void EventBatchDeserializesResumeAndGapMetadata()
    {
        const string json = """
            {
              "delivery_id": "delivery-1",
              "events": [],
              "next_sequence": 4,
              "latest_sequence": 5,
              "has_more": true,
              "coverage": "incomplete",
              "discontinuity": {
                "first_missing_sequence": 2,
                "last_missing_sequence": 3,
                "affected_start_sample": 1000,
                "affected_end_sample": 6000,
                "sample_range_exact": true
              },
              "delivery": { "state": "enabled" }
            }
            """;

        var batch = JsonSerializer.Deserialize<TnkPlaybackEventBatchResponse>(json);

        Assert.NotNull(batch);
        Assert.Equal("delivery-1", batch.DeliveryId);
        Assert.Equal(4, batch.NextSequence);
        Assert.Equal("incomplete", batch.Coverage);
        Assert.Equal(2, batch.Discontinuity?.FirstMissingSequence);
        Assert.Equal(6000, batch.Discontinuity?.AffectedEndSample);
    }
}
