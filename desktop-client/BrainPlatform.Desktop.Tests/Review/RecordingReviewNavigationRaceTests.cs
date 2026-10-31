using System.Reflection;

namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Theory]
    [InlineData(55)]
    [InlineData(35)]
    public async Task Preparation_notifications_cannot_restore_an_older_drag_viewport(double target)
    {
        var configuration = Configuration();
        var profile = Profile("REF", configuration);
        var catalog = NavigationCatalog(profile);
        var reader = new DeferredNavigationReader(Manifest(configuration));
        await using var vm = new RecordingReviewViewModel(reader, catalog);
        await vm.InitializeAsync();
        var oldLoad = vm.SeekAsync(50);
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var original = vm.CurrentFrame;

        typeof(RecordingReviewViewModel).GetMethod("SetPreviewPosition", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, [target]);
        var session = (RecordingReviewSession)typeof(RecordingReviewViewModel)
            .GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        typeof(RecordingReviewSession).GetMethod("OnPreparationProgressChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(session, [null, ReviewPreparationState.Idle]);

        Assert.Equal(target, vm.PositionSeconds);
        Assert.Equal(target - 5, vm.ViewportStartSeconds);
        Assert.Same(original, vm.CurrentFrame);
        reader.Release.TrySetResult();
        await oldLoad;
        Assert.Equal(target - 5, vm.ViewportStartSeconds);
    }

    [Fact]
    public async Task Cached_target_supersedes_a_pending_cold_frame()
    {
        var configuration = Configuration();
        var reader = new DeferredNavigationReader(Manifest(configuration));
        await using var session = new RecordingReviewSession(reader, NavigationCatalog(Profile("REF", configuration)), 10);
        await session.InitializeAsync();
        var original = session.CurrentFrame;
        var cold = session.LoadViewportAsync(100, 105);
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await session.LoadViewportAsync(0, 5);
        Assert.Same(original, session.CurrentFrame);
        reader.Release.TrySetResult();
        await cold;

        Assert.Same(original, session.CurrentFrame);
        Assert.Equal(5, session.PositionSeconds);
        Assert.Equal(0, session.ViewportStartSeconds);
        Assert.False(session.IsLoading);
    }

    private static RecordingMontageCatalogResult NavigationCatalog(MontageProfile profile) => new(
        profile, AcquisitionMontageStatus.Available, new RawSignalViewDefinition(["F3"]),
        [new CompatibleRecordingMontage(profile)], []);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dragging_exclusively_owns_position_then_restores_previous_playback_state(bool wasPlaying)
    {
        var configuration = Configuration();
        var now = 0d;
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100),
            NavigationCatalog(Profile("REF", configuration)), playbackClockSeconds: () => now);
        await vm.InitializeAsync();
        await vm.SeekAsync(50);
        if (wasPlaying) vm.TogglePlayback();
        vm.BeginTimelineDrag();
        Assert.False(vm.IsPlaying);
        vm.PreviewSeek(55);
        now = 10;
        vm.TickPlayback();
        Assert.Equal(55, vm.PositionSeconds);
        vm.PreviewSeek(35);
        vm.EndTimelineDrag();
        vm.EndTimelineDrag();
        Assert.Equal(wasPlaying, vm.IsPlaying);
        now = 11;
        vm.TickPlayback();
        Assert.Equal(wasPlaying ? 36 : 35, vm.PositionSeconds);
    }

    [Fact]
    public async Task Preparation_notices_do_not_repeat_visible_duration_notifications()
    {
        var configuration = Configuration();
        await using var vm = new RecordingReviewViewModel(new FakeReader(Manifest(configuration), 100),
            NavigationCatalog(Profile("REF", configuration)));
        await vm.InitializeAsync();
        var notifications = 0;
        vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.VisibleDurationSeconds)) notifications++; };
        var session = (RecordingReviewSession)typeof(RecordingReviewViewModel)
            .GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        var progress = typeof(RecordingReviewSession).GetMethod("OnPreparationProgressChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var index = 0; index < 30; index++) progress.Invoke(session, [null, ReviewPreparationState.Idle]);
        Assert.Equal(0, notifications);
    }

    private sealed class DeferredNavigationReader(LocalRawRecordingManifest manifest) : IRecordingAccessSession
    {
        private int reads;
        public LocalRawRecordingManifest Manifest { get; } = manifest;
        public double DurationSeconds => 150;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RecordingReviewWindow> ReadWindowAsync(double start, double duration, CancellationToken token)
        {
            if (Interlocked.Increment(ref reads) > 1)
            {
                Started.TrySetResult();
                await Release.Task;
            }
            var samples = (int)Math.Ceiling(duration * Manifest.SamplingRateHz);
            return new RecordingReviewWindow(start, start, start + duration,
                [new RecordingReviewSegment((long)Math.Round(start * Manifest.SamplingRateHz), samples, 2, new double[samples * 2])]);
        }

        public Task<RecordingAccessReadResult> ReadSamplesAsync(
            RecordingSampleRange range,
            IReadOnlyList<int>? channelIndexes,
            CancellationToken cancellationToken) =>
            RecordingAccessTestHelpers.ReadAsync(
                Manifest, 0, ReadWindowAsync, range, channelIndexes, cancellationToken);
    }
}
