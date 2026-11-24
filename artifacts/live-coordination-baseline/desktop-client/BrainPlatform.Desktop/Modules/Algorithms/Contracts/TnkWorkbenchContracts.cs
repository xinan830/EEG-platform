using System.Text.Json.Serialization;

namespace BrainPlatform.Desktop.Modules.Algorithms.Contracts;

public sealed record TnkScoreState(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("duration_s")] double DurationSeconds,
    [property: JsonPropertyName("percent")] double Percent)
{
    public string DurationText => $"{(int)(DurationSeconds / 60)}:{(int)DurationSeconds % 60:00}  {Percent:0}%";
}
public sealed record TnkWorkbenchSummary(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("valid_duration_s")] double ValidDurationSeconds,
    [property: JsonPropertyName("states")] IReadOnlyList<TnkScoreState> States,
    [property: JsonPropertyName("timeline")] IReadOnlyList<TnkScoreFrame> Timeline);
public sealed record TnkScoreFrame(
    [property: JsonPropertyName("cursor_s")] double CursorSeconds,
    [property: JsonPropertyName("valid_duration_s")] double ValidDurationSeconds,
    [property: JsonPropertyName("states")] IReadOnlyList<TnkScoreState> States);
