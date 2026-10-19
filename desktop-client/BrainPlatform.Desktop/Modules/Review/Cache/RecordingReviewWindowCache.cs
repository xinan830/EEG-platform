
namespace BrainPlatform.Desktop.Modules.Review.Cache;

/// <summary>
/// A bounded, in-memory cache for review windows. It deliberately stores only
/// a few nearby blocks; recordings remain the source of truth on disk.
/// </summary>
internal sealed class RecordingReviewWindowCache
{
    private const int MaximumRawWindows = 2;
    private const int MaximumFrames = 3;
    internal const long DefaultPayloadBudgetBytes = 64L * 1024 * 1024;
    private readonly long maximumRawPayloadBytes;
    private readonly long maximumFramePayloadBytes;
    private readonly object gate = new();
    private readonly LinkedList<RawEntry> rawEntries = [];
    private readonly LinkedList<FrameEntry> frameEntries = [];

    public RecordingReviewWindowCache(long maximumRawPayloadBytes = DefaultPayloadBudgetBytes,
        long maximumFramePayloadBytes = DefaultPayloadBudgetBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRawPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFramePayloadBytes);
        this.maximumRawPayloadBytes = maximumRawPayloadBytes;
        this.maximumFramePayloadBytes = maximumFramePayloadBytes;
    }

    public bool TryGetRaw(double startSeconds, double endSeconds, out RecordingReviewWindow window)
    {
        lock (gate)
        {
            var entry = rawEntries.FirstOrDefault(item => Covers(item.Window, startSeconds, endSeconds));
            if (entry is null)
            {
                window = default!;
                return false;
            }

            Touch(rawEntries, entry);
            window = entry.Window;
            return true;
        }
    }

    public void StoreRaw(RecordingReviewWindow window)
    {
        var bytes = window.Segments.Sum(segment => segment.SampleMajorValues.LongLength * sizeof(double));
        if (bytes > maximumRawPayloadBytes) return;
        lock (gate)
        {
            var existing = rawEntries.FirstOrDefault(item => SameRange(item.Window, window));
            if (existing is not null)
            {
                Touch(rawEntries, existing);
                return;
            }

            rawEntries.AddFirst(new RawEntry(window, bytes));
            Trim(rawEntries, MaximumRawWindows, maximumRawPayloadBytes, entry => entry.PayloadBytes);
        }
    }

    public bool TryGetFrame(
        double viewportStartSeconds,
        double viewportEndSeconds,
        RecordingReviewFilterSettings settings,
        MontageProfile? montage,
        out RecordingReviewFrame frame,
        out bool filterUnavailable)
    {
        lock (gate)
        {
            var entry = frameEntries.FirstOrDefault(item =>
                item.Settings == settings &&
                string.Equals(item.MontageFingerprint, MontageFingerprint(montage), StringComparison.Ordinal) &&
                Covers(item.Frame, viewportStartSeconds, viewportEndSeconds));
            if (entry is null)
            {
                frame = default!;
                filterUnavailable = false;
                return false;
            }

            Touch(frameEntries, entry);
            // Equivalent signal projections may belong to differently named presets.
            var montageName = montage?.Name ?? "原始设备通道";
            frame = entry.Frame.ViewingMontageName == montageName
                ? entry.Frame
                : entry.Frame with { ViewingMontageName = montageName };
            filterUnavailable = entry.FilterUnavailable;
            return true;
        }
    }

    public void StoreFrame(
        RecordingReviewFrame frame,
        RecordingReviewFilterSettings settings,
        MontageProfile? montage,
        bool filterUnavailable)
    {
        var bytes = frame.Segments.Sum(segment => segment.Channels.Sum(channel => channel.Values.LongLength * sizeof(double)));
        if (bytes > maximumFramePayloadBytes) return;
        lock (gate)
        {
            var existing = frameEntries.FirstOrDefault(entry => ReferenceEquals(entry.Frame, frame) &&
                entry.Settings == settings && entry.MontageFingerprint == MontageFingerprint(montage) &&
                entry.FilterUnavailable == filterUnavailable);
            if (existing is not null)
            {
                Touch(frameEntries, existing);
                return;
            }
            frameEntries.AddFirst(new FrameEntry(frame, settings, MontageFingerprint(montage), filterUnavailable, bytes));
            Trim(frameEntries, MaximumFrames, maximumFramePayloadBytes, entry => entry.PayloadBytes);
        }
    }


    private static bool Covers(RecordingReviewWindow window, double startSeconds, double endSeconds) =>
        window.ActualStartSeconds <= startSeconds + 0.000_001 &&
        window.ActualEndSeconds + 0.000_001 >= endSeconds;

    private static bool Covers(RecordingReviewFrame frame, double startSeconds, double endSeconds) =>
        frame.WindowStartSeconds <= startSeconds + 0.000_001 &&
        frame.WindowEndSeconds + 0.000_001 >= endSeconds;

    private static bool SameRange(RecordingReviewWindow left, RecordingReviewWindow right) =>
        Math.Abs(left.ActualStartSeconds - right.ActualStartSeconds) < 0.000_001 &&
        Math.Abs(left.ActualEndSeconds - right.ActualEndSeconds) < 0.000_001;

    private static string MontageFingerprint(MontageProfile? montage) => montage?.Fingerprint ?? "raw";

    private static void Touch<T>(LinkedList<T> entries, T entry)
    {
        var node = entries.Find(entry);
        if (node is not null && node != entries.First)
        {
            entries.Remove(node);
            entries.AddFirst(node);
        }
    }

    private static void Trim<T>(LinkedList<T> entries, int maximum, long maximumBytes, Func<T, long> size)
    {
        var bytes = entries.Sum(size);
        while (entries.Count > maximum || bytes > maximumBytes)
        {
            bytes -= size(entries.Last!.Value);
            entries.RemoveLast();
        }
    }

    private sealed record RawEntry(RecordingReviewWindow Window, long PayloadBytes);

    private sealed record FrameEntry(
        RecordingReviewFrame Frame,
        RecordingReviewFilterSettings Settings,
        string MontageFingerprint,
        bool FilterUnavailable,
        long PayloadBytes);
}
