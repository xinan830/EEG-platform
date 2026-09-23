
namespace BrainPlatform.Desktop.Review;

/// <summary>
/// A bounded, in-memory cache for review windows. It deliberately stores only
/// a few nearby blocks; recordings remain the source of truth on disk.
/// </summary>
internal sealed class RecordingReviewWindowCache
{
    private const int MaximumRawWindows = 2;
    private const int MaximumFrames = 3;
    private readonly object gate = new();
    private readonly LinkedList<RawEntry> rawEntries = [];
    private readonly LinkedList<FrameEntry> frameEntries = [];

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
        lock (gate)
        {
            var existing = rawEntries.FirstOrDefault(item => SameRange(item.Window, window));
            if (existing is not null)
            {
                Touch(rawEntries, existing);
                return;
            }

            rawEntries.AddFirst(new RawEntry(window));
            Trim(rawEntries, MaximumRawWindows);
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
            frame = entry.Frame;
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
        lock (gate)
        {
            frameEntries.AddFirst(new FrameEntry(frame, settings, MontageFingerprint(montage), filterUnavailable));
            Trim(frameEntries, MaximumFrames);
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

    private static void Trim<T>(LinkedList<T> entries, int maximum)
    {
        while (entries.Count > maximum)
        {
            entries.RemoveLast();
        }
    }

    private sealed record RawEntry(RecordingReviewWindow Window);

    private sealed record FrameEntry(
        RecordingReviewFrame Frame,
        RecordingReviewFilterSettings Settings,
        string MontageFingerprint,
        bool FilterUnavailable);
}
