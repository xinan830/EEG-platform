using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Tests.Waveform;

public sealed class WaveformDisplayFramePublisherTests
{
    [Fact]
    public void Publishes_payload_only_for_the_exact_current_request()
    {
        var request = Request();
        var published = 0;

        var accepted = WaveformDisplayFramePublisher.TryPublish(
            new WaveformDisplayFrame<string>(request, "complete-frame"),
            request,
            CancellationToken.None,
            payload => published++);

        Assert.True(accepted);
        Assert.Equal(1, published);
    }

    [Theory]
    [InlineData("recording")]
    [InlineData("source")]
    [InlineData("generation")]
    [InlineData("request")]
    public void Rejects_a_frame_from_an_obsolete_context(string changedIdentity)
    {
        var request = Request();
        var current = changedIdentity switch
        {
            "recording" => request with { RecordingId = Guid.NewGuid() },
            "source" => request with { SourceRevision = "new-source" },
            "generation" => request with { ViewGeneration = request.ViewGeneration + 1 },
            _ => request with { RequestId = Guid.NewGuid() },
        };
        var published = false;

        var accepted = WaveformDisplayFramePublisher.TryPublish(
            new WaveformDisplayFrame<string>(request, "stale-frame"),
            current,
            CancellationToken.None,
            _ => published = true);

        Assert.False(accepted);
        Assert.False(published);
    }

    [Fact]
    public void Does_not_publish_after_request_cancellation()
    {
        var request = Request();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var published = false;

        var accepted = WaveformDisplayFramePublisher.TryPublish(
            new WaveformDisplayFrame<string>(request, "obsolete-frame"),
            request,
            cancellation.Token,
            _ => published = true);

        Assert.False(accepted);
        Assert.False(published);
    }

    [Theory]
    [InlineData("recording")]
    [InlineData("source")]
    [InlineData("generation")]
    [InlineData("request")]
    public void Rejects_incomplete_request_identity(string missingIdentity)
    {
        var request = Request();
        var invalid = missingIdentity switch
        {
            "recording" => request with { RecordingId = Guid.Empty },
            "source" => request with { SourceRevision = " " },
            "generation" => request with { ViewGeneration = 0 },
            _ => request with { RequestId = Guid.Empty },
        };
        var published = false;

        var accepted = WaveformDisplayFramePublisher.TryPublish(
            new WaveformDisplayFrame<string>(invalid, "frame"),
            invalid,
            CancellationToken.None,
            _ => published = true);

        Assert.False(accepted);
        Assert.False(published);
    }

    private static WaveformDisplayRequestContext Request() => new(
        Guid.NewGuid(), "source-v1", 3, Guid.NewGuid());
}
