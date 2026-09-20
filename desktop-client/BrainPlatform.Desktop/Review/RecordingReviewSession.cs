using BrainPlatform.Desktop.Configuration;

namespace BrainPlatform.Desktop.Review;

public interface IRecordingReviewReader
{
    LocalRawRecordingManifest Manifest { get; }

    double DurationSeconds { get; }

    Task<RecordingReviewWindow> ReadWindowAsync(
        double startSeconds,
        double durationSeconds,
        CancellationToken cancellationToken);
}

public sealed record RecordingReviewFrame(
    Guid RecordingSessionId,
    long Revision,
    double WindowStartSeconds,
    double WindowEndSeconds,
    string ViewingMontageName,
    IReadOnlyList<ProjectedMontageSegment> Segments,
    IReadOnlyList<string> OutputChannelNames);

public sealed class RecordingReviewSession : IAsyncDisposable
{
    private readonly IRecordingReviewReader reader;
    private readonly RecordingMontageCatalogResult catalog;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? currentLoad;
    private long revision;
    private int disposed;

    public RecordingReviewSession(
        IRecordingReviewReader reader,
        RecordingMontageCatalogResult catalog,
        double visibleDurationSeconds)
    {
        if (!double.IsFinite(visibleDurationSeconds) || visibleDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleDurationSeconds));
        }

        this.reader = reader;
        this.catalog = catalog;
        VisibleDurationSeconds = Math.Min(visibleDurationSeconds, reader.DurationSeconds);
        AcquisitionMontage = catalog.AcquisitionMontage;
        SelectedViewingMontage = catalog.AcquisitionMontage ?? catalog.CompatibleViewingMontages.FirstOrDefault()?.Profile;
    }

    public event EventHandler? Changed;

    public MontageProfile? AcquisitionMontage { get; }

    public MontageProfile? SelectedViewingMontage { get; private set; }

    public double PositionSeconds { get; private set; }

    public double DurationSeconds => reader.DurationSeconds;

    public double VisibleDurationSeconds { get; private set; }

    public RecordingReviewFrame? CurrentFrame { get; private set; }

    public bool IsLoading { get; private set; }

    public string StatusText { get; private set; } = "等待读取记录。";

    public Task InitializeAsync() => LoadCurrentWindowAsync();

    public Task SeekAsync(double positionSeconds)
    {
        ThrowIfDisposed();
        PositionSeconds = Math.Clamp(positionSeconds, 0, Math.Max(0, reader.DurationSeconds - VisibleDurationSeconds));
        return LoadCurrentWindowAsync();
    }

    public Task SetVisibleDurationAsync(double visibleDurationSeconds)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(visibleDurationSeconds) || visibleDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleDurationSeconds));
        }

        VisibleDurationSeconds = Math.Min(visibleDurationSeconds, reader.DurationSeconds);
        PositionSeconds = Math.Min(PositionSeconds, Math.Max(0, reader.DurationSeconds - VisibleDurationSeconds));
        return LoadCurrentWindowAsync();
    }

    public Task SelectViewingMontageAsync(MontageProfile? montage)
    {
        ThrowIfDisposed();
        if (montage is not null && catalog.CompatibleViewingMontages.All(
                item => !string.Equals(item.Profile.Id, montage.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("所选导联与此记录的原始通道不兼容。");
        }

        SelectedViewingMontage = montage;
        return LoadCurrentWindowAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        currentLoad?.Cancel();
        currentLoad?.Dispose();
        lifetime.Dispose();
        await Task.CompletedTask;
    }

    private async Task LoadCurrentWindowAsync()
    {
        ThrowIfDisposed();
        var requestRevision = Interlocked.Increment(ref revision);
        var previousLoad = Interlocked.Exchange(
            ref currentLoad,
            CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token));
        previousLoad?.Cancel();
        var load = currentLoad!;
        var token = load.Token;
        IsLoading = true;
        StatusText = "正在读取波形…";
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var window = await reader.ReadWindowAsync(
                PositionSeconds,
                VisibleDurationSeconds,
                token);
            token.ThrowIfCancellationRequested();
            if (requestRevision != Volatile.Read(ref revision))
            {
                return;
            }

            var projected = MontageDisplayProjector.Project(window, reader.Manifest, SelectedViewingMontage);
            var outputNames = projected.FirstOrDefault()?.Channels.Select(channel => channel.Label).ToArray()
                              ?? SelectedViewingMontage?.DerivedChannels.OrderBy(channel => channel.DisplayOrder).Select(channel => channel.Name).ToArray()
                              ?? catalog.RawSignalView.ChannelLabels.ToArray();
            CurrentFrame = new RecordingReviewFrame(
                reader.Manifest.SessionId,
                requestRevision,
                window.ActualStartSeconds,
                window.ActualEndSeconds,
                SelectedViewingMontage?.Name ?? "原始设备通道",
                projected,
                outputNames);
            StatusText = projected.Count == 0 ? "当前时间范围没有可显示的数据。" : "已加载";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (requestRevision == Volatile.Read(ref revision))
        {
            StatusText = $"回溯读取失败：{exception.Message}";
            CurrentFrame = null;
        }
        finally
        {
            if (requestRevision == Volatile.Read(ref revision))
            {
                IsLoading = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            Interlocked.CompareExchange(ref currentLoad, null, load);
            load.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(RecordingReviewSession));
        }
    }
}
