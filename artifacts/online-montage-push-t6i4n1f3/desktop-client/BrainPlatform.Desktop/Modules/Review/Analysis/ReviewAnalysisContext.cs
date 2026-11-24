namespace BrainPlatform.Desktop.Modules.Review.Analysis;

/// <summary>Identity and cursor owned by review; no algorithm timer or formula lives here.</summary>
public sealed record ReviewAnalysisContext(
    string RecordingId,
    string RunId,
    long CursorSample,
    double CursorSeconds);

/// <summary>Latest-only result coordinator for review observers.</summary>
