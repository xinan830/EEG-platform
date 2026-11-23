using System.Text.Json;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveRbpResultReaderTests
{
    private static readonly Guid SessionId = Guid.Parse("91ca20ee-a76f-47cb-b9cc-3ef03e77f2d3");

    [Fact]
    public void ReadsSampleAlignedFiveBandResultAndPreservesQuality()
    {
        using var document = JsonDocument.Parse(ResultJson());

        var update = LiveRbpResultReader.Read(document.RootElement, SessionId, ["Fz"], 512, 7);

        Assert.Equal(SessionId, update.SessionId);
        Assert.Equal(7, update.Revision);
        Assert.Equal(10d, update.StartSeconds);
        Assert.Equal(11d, update.EndSeconds);
        var channel = Assert.Single(update.Channels!);
        Assert.Equal("warning", channel.Quality);
        Assert.Equal("artifact", channel.Warning);
        Assert.Equal(0.2, channel.Shares[0]);
    }

    [Theory]
    [InlineData("\"acquisition_session_id\":\"91ca20ee-a76f-47cb-b9cc-3ef03e77f2d3\"", "\"acquisition_session_id\":\"fed66bc5-4e59-4a2d-a50c-356dd0f57356\"")]
    [InlineData("\"delta\":\"ratio\"", "\"delta\":\"uV\"")]
    [InlineData("\"start_s\":10.0", "\"start_s\":9.0")]
    [InlineData("\"channels\":[\"Fz\"]", "\"channels\":[\"Cz\"]")]
    public void RejectsResultsThatDoNotMatchTheLiveContract(string expected, string replacement)
    {
        using var document = JsonDocument.Parse(ResultJson().Replace(expected, replacement, StringComparison.Ordinal));

        Assert.Throws<InvalidOperationException>(() =>
            LiveRbpResultReader.Read(document.RootElement, SessionId, ["Fz"], 512, 1));
    }

    [Fact]
    public void GenericEnvelopeStillRendersTheExistingRbpChartHistory()
    {
        using var document = JsonDocument.Parse(ResultJson());
        var envelope = new LiveAnalysisUpdate(SessionId, "rbp", 7, "partial", 512,
            document.RootElement.Clone(), 10, 11, "rbp-live-v1");
        var presenter = new BrainPlatform.Desktop.Modules.Acquisition.ViewModels.LiveRbpAnalysisViewModel();
        presenter.Begin(SessionId, 23);
        Assert.Equal(23, presenter.VisibleStartSeconds);
        Assert.True(presenter.Apply(LiveRbpPresentation.Read(envelope, ["Fz"])!));
        Assert.Single(presenter.ChannelHistories);
        Assert.Contains("质量警告", presenter.StatusText);
        presenter.Clear();
        Assert.Null(presenter.VisibleStartSeconds);
    }

    [Fact]
    public void UnknownAlgorithmAndMismatchedRbpEnvelopeCannotRenderAsRbp()
    {
        using var document = JsonDocument.Parse(ResultJson());
        var envelope = new LiveAnalysisUpdate(SessionId, "other", 7, "partial", 512,
            document.RootElement.Clone(), 10, 11, "rbp-live-v1");
        Assert.Null(LiveRbpPresentation.Read(envelope, ["Fz"]));
        var mismatched = envelope with { AlgorithmId = "rbp", EndSeconds = 12 };
        var unavailable = LiveRbpPresentation.Read(mismatched, ["Fz"])!;
        Assert.Equal("unavailable", unavailable.State);
        Assert.All(Assert.Single(unavailable.Channels!).Shares, value => Assert.Null(value));
    }

    [Fact]
    public void GenericGapOrOverloadCreatesAnUnavailableChartPoint()
    {
        var envelope = new LiveAnalysisUpdate(SessionId, "rbp", 8, "overloaded", 512,
            StartSeconds: 11, EndSeconds: 12, FailureCode: "LIVE_ANALYSIS_OVERLOADED", Message: "窗口已跳过");
        var result = LiveRbpPresentation.Read(envelope, ["Fz"])!;
        Assert.Equal("unavailable", result.State);
        Assert.Equal(12, result.EndSeconds);
        Assert.Equal("unavailable", Assert.Single(result.Channels!).Quality);
    }

    private static string ResultJson() => """
        {
          "type":"live_rbp_result","schema_version":"live-rbp-result-v1","algorithm_id":"rbp",
          "scientific_identity":"rbp-live-v1","acquisition_session_id":"91ca20ee-a76f-47cb-b9cc-3ef03e77f2d3",
          "state":"partial","channels":["Fz"],"start_sample":5120,"end_sample":5632,"start_s":10.0,"end_s":11.0,
          "values":{"delta":[0.2],"theta":[0.2],"alpha":[0.2],"beta":[0.2],"gamma":[0.2]},
          "units":{"delta":"ratio","theta":"ratio","alpha":"ratio","beta":"ratio","gamma":"ratio"},
          "quality":{"Fz":{"status":"warning","reasons":["artifact"]}},"failure":null
        }
        """;
}
