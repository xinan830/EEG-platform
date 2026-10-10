using System.Text.Json;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;
using BrainPlatform.Desktop.Modules.Review.Analysis;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed class ReviewPsdProjectionTests
{
    [Fact]
    public void Official_voltage_density_is_projected_to_microvolt_density()
    {
        var response = Response("V^2/Hz", [0.5, 1.0], [2e-12, 4e-12]);

        var projection = ReviewPsdProjection.From(response);

        Assert.Equal([0.5, 1.0], projection.Frequencies);
        Assert.Equal([2d, 4d], projection.Values);
    }

    [Fact]
    public void Already_converted_density_is_not_scaled_twice()
    {
        var projection = ReviewPsdProjection.From(Response("uV^2/Hz", [1, 2], [3, 4]));

        Assert.Equal([3d, 4d], projection.Values);
    }

    [Fact]
    public void Missing_unit_is_rejected_instead_of_guessing()
    {
        var response = Response(null, [1, 2], [3, 4]);

        var exception = Assert.Throws<InvalidOperationException>(() => ReviewPsdProjection.From(response));

        Assert.Contains("单位", exception.Message);
    }

    [Fact]
    public void Non_monotonic_axis_is_rejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReviewPsdProjection.From(Response("V^2/Hz", [1, 1], [3, 4])));

        Assert.Contains("顺序", exception.Message);
    }

    private static WindowPreviewResponse Response(string? unit, double[] frequencies, double[] values)
    {
        var metadata = unit is null
            ? JsonSerializer.SerializeToElement(new { })
            : JsonSerializer.SerializeToElement(new { unit });
        return new WindowPreviewResponse(
            "run", "recording", 10, 1, "available", [], null, false, null, null,
            new Dictionary<string, JsonElement> { ["axis_frequency_hz"] = JsonSerializer.SerializeToElement(frequencies) },
            new Dictionary<string, JsonElement>(),
            new Dictionary<string, JsonElement> { ["psd"] = JsonSerializer.SerializeToElement(values) },
            new Dictionary<string, JsonElement> { ["psd"] = metadata },
            "v1", "impl");
    }
}
