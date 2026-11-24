using BrainPlatform.Desktop.Infrastructure.Backend;

namespace BrainPlatform.Desktop.Tests;

public sealed class BackendHealthResponseParserTests
{
    [Fact]
    public void Parse_ReturnsAvailableOnlyForExpectedHealthStatus()
    {
        var result = BackendHealthResponseParser.Parse(
            "{\"status\": \"ok\", \"service\": \"brain-platform-backend\"}",
            DateTimeOffset.UnixEpoch);

        Assert.True(result.IsAvailable);
        Assert.Equal("已连接", result.StatusText);
        Assert.Equal("brain-platform-backend", result.Detail);
    }

    [Fact]
    public void Parse_ReturnsUnavailableForMalformedPayload()
    {
        var result = BackendHealthResponseParser.Parse("not-json", DateTimeOffset.UnixEpoch);

        Assert.False(result.IsAvailable);
        Assert.Equal("未连接", result.StatusText);
    }
}
