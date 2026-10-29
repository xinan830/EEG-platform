using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Tests.Algorithms;

public sealed class ReviewTargetContractsTests
{
    [Fact]
    public void Scientific_context_key_excludes_trace_and_generation()
    {
        var first = Target("trace-1", generation: 0);
        var second = Target("trace-2", generation: 4);

        Assert.Equal(first.ScientificContextKey, second.ScientificContextKey);
    }

    [Fact]
    public void Context_key_includes_channel_order_and_configuration()
    {
        var first = Target("trace-1", generation: 0);
        var differentChannels = first with { ChannelOrder = ["Pz-REF", "Fz-REF"] };
        var differentConfiguration = first with { ConfigurationFingerprint = "cfg-2" };

        Assert.NotEqual(first.ScientificContextKey, differentChannels.ScientificContextKey);
        Assert.NotEqual(first.ScientificContextKey, differentConfiguration.ScientificContextKey);
    }

    private static ReviewTargetEnvelope Target(string traceId, long generation) => new(
        traceId, generation, "recording-1", "rbp", "definition-1", "3.0.0", "cfg-1",
        500, 250, ["Fz-REF", "Pz-REF"], ReviewTaskLane.WindowIndependentAnalysis);
}
