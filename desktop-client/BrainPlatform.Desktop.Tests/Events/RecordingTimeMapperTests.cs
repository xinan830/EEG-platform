using BrainPlatform.Desktop.Shared.Time;
using BrainPlatform.Desktop.Shared.Waveform;
using System.Text.Json;

namespace BrainPlatform.Desktop.Tests.Events;

public sealed class RecordingTimeMapperTests
{
    [Fact]
    public void Sample_coordinate_preserves_submillisecond_precision_before_display()
    {
        var startUtc = new DateTimeOffset(2026, 9, 24, 13, 21, 30, 375, TimeSpan.Zero);

        var local = RecordingTimeMapper.ToLocalTime(startUtc, 5_121, 4_096);

        Assert.Equal(startUtc.AddTicks(12_502_441).ToLocalTime(), local);
        Assert.Equal(1.250244140625, RecordingTimeMapper.ToElapsedSeconds(5_121, 4_096), 12);
        Assert.Equal(local.ToString("HH:mm:ss.fff"),
            RecordingClockLabelFormatter.FormatSampleMilliseconds(startUtc, 5_121, 4_096));
    }

    [Fact]
    public void Invalid_sample_coordinates_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingTimeMapper.ToLocalTime(DateTimeOffset.UtcNow, -1, 4_096));
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingTimeMapper.ToElapsedSeconds(0, 0));
    }

    [Fact]
    public void Cursor_sample_uses_floor_and_does_not_round_into_future()
    {
        Assert.Equal(3999, RecordingTimeMapper.ToCursorSample(3.999999, 1000, 48216));
        Assert.Equal(4000, RecordingTimeMapper.ToCursorSample(4, 1000, 48216));
        Assert.Equal(4000, RecordingTimeMapper.ToCursorSample(4.000001, 1000, 48216));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecordingTimeMapper.ToCursorSample(double.NaN, 1000, 48216));
    }

    [Fact]
    public void Relative_sample_preserves_counter_origin_and_rejects_reverse_time()
    {
        Assert.Equal(4000, RecordingTimeMapper.ToRelativeSample(8_004_000, 8_000_000));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecordingTimeMapper.ToRelativeSample(7_999_999, 8_000_000));
    }

    [Fact]
    public void Shared_cursor_fixture_matches_the_desktop_mapper()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "review_cursor_samples.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var seconds = item.GetProperty("seconds").GetDouble();
            var rate = item.GetProperty("sampling_rate_hz").GetInt32();
            var duration = item.GetProperty("duration_samples").GetInt64();
            var expected = item.GetProperty("expected_sample").GetInt64();
            Assert.Equal(expected, RecordingTimeMapper.ToCursorSample(seconds, rate, duration));
        }
    }
}
