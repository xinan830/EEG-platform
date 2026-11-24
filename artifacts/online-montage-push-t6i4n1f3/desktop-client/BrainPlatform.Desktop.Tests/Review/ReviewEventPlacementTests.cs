namespace BrainPlatform.Desktop.Tests.Review;

public sealed partial class RecordingReviewViewModelTests
{
    [Fact]
    public async Task EventTimeInputCreatesAtEnteredSampleWithoutSeeking()
    {
        await using var context = await EventContext.CreateAsync();
        var model = context.Model;
        model.AddEventTimeText = "00:00:13.740";
        await model.AddEventAtEnteredTimeAsync();
        var item = Assert.Single(model.RecordingEvents);
        Assert.Equal(6870, item.StartSample);
        Assert.Equal(0, item.DurationSamples);
        Assert.Equal(0, model.PositionSeconds);
        Assert.Equal(EventSource.ManualButton, item.Source);
        model.AddEventTimeText = "00:00:20.000";
        await Assert.ThrowsAsync<EventValidationException>(model.AddEventAtEnteredTimeAsync);
        Assert.Single(model.RecordingEvents);
        model.AddEventTimeText = "bad";
        await Assert.ThrowsAsync<EventValidationException>(model.AddEventAtEnteredTimeAsync);
        Assert.Equal("bad", model.AddEventTimeText);
    }

    [Fact]
    public async Task WaveformPlacementPausesPreviewsAndCommitsExactlyOnce()
    {
        await using var context = await EventContext.CreateAsync();
        var model = context.Model;
        model.TogglePlayback();
        Assert.True(model.IsPlaying);
        model.BeginEventPlacement(false);
        Assert.False(model.IsPlaying);
        Assert.Equal("#C040A0", model.EventPlacementColor);
        Assert.True(model.PreviewEventAtSeconds(2.003));
        Assert.Equal(1002, model.EventPreviewSample);
        Assert.Empty(model.RecordingEvents);
        await Task.WhenAll(model.CommitEventPlacementAsync(), model.CommitEventPlacementAsync());
        Assert.Equal(1002, Assert.Single(model.RecordingEvents).StartSample);
        Assert.False(model.IsEventPlacementActive);
        Assert.Null(model.EventPreviewSample);
        await model.CommitEventPlacementAsync();
        Assert.Single(model.RecordingEvents);
        model.BeginEventPlacement(false);
        Assert.True(model.PreviewEventAtSeconds(4));
        model.CancelEventPlacement();
        await model.CommitEventPlacementAsync();
        Assert.Single(model.RecordingEvents);
    }

    [Fact]
    public async Task MovingIntervalPreservesExactDurationAndMetadata()
    {
        await using var context = await EventContext.CreateAsync();
        var model = context.Model;
        var item = await context.CreateIntervalAsync();
        await model.LoadEventsAsync();
        model.SelectedRecordingEvent = Assert.Single(model.RecordingEvents);
        model.EditEventTimeText = "00:00:04.000";
        model.EditEventNote = "changed";
        await model.SaveEditedEventAsync();
        var moved = Assert.Single(model.RecordingEvents);
        Assert.Equal(2000, moved.StartSample);
        Assert.Equal(item.DurationSamples, moved.DurationSamples);
        Assert.Equal(item.DefinitionSnapshot, moved.DefinitionSnapshot);
        Assert.Equal(item.SourceSampleCounter, moved.SourceSampleCounter);
        model.BeginEventPlacement(true);
        Assert.Equal(item.DefinitionSnapshot.Color, model.EventPlacementColor);
        Assert.True(model.PreviewEventAtSeconds(6.003));
        await model.CommitEventPlacementAsync();
        moved = Assert.Single(model.RecordingEvents);
        Assert.Equal(3002, moved.StartSample);
        Assert.Equal("changed", moved.Note);
        Assert.Equal(item.DurationSamples, moved.DurationSamples);
        Assert.False(model.IsEventPlacementActive);
        model.BeginEventPlacement(true);
        Assert.False(model.PreviewEventAtSeconds(19.9));
        await model.CommitEventPlacementAsync();
        Assert.Equal(moved, Assert.Single(model.RecordingEvents));
    }

    [Fact]
    public async Task EventPlacementKeepsEditedNoteAndCancelsWhenPlaybackResumes()
    {
        await using var context = await EventContext.CreateAsync();
        var model = context.Model;
        await context.CreateIntervalAsync();
        await model.LoadEventsAsync();
        model.SelectedRecordingEvent = Assert.Single(model.RecordingEvents);
        model.IsEventEditorOpen = true;
        model.BeginEventPlacement(true);
        model.EditEventNote = "编辑中的备注";
        model.PreviewEventAtSeconds(4);
        await model.CommitEventPlacementAsync();
        Assert.Equal("编辑中的备注", Assert.Single(model.RecordingEvents).Note);
        model.BeginEventPlacement(false);
        model.TogglePlayback();
        Assert.True(model.IsPlaying);
        Assert.False(model.IsEventPlacementActive);
    }

    [Fact]
    public async Task EventTimeInputNoteOnlyEditPreservesSubMillisecondSample()
    {
        await using var context = await EventContext.CreateAsync(2048);
        var item = await context.CreateIntervalAsync();
        await context.Model.LoadEventsAsync();
        context.Model.SelectedRecordingEvent = Assert.Single(context.Model.RecordingEvents);
        context.Model.EditEventNote = "note only";
        await context.Model.SaveEditedEventAsync();
        Assert.Equal(item.StartSample, Assert.Single(context.Model.RecordingEvents).StartSample);
        Assert.Equal(item.DurationSamples, Assert.Single(context.Model.RecordingEvents).DurationSamples);
    }

    [Fact]
    public async Task EventPlacementWriteFailureKeepsOriginalAndDraftUntilRetrySucceeds()
    {
        await using var context = await EventContext.CreateAsync();
        var original = await context.CreateIntervalAsync();
        var model = context.Model;
        await model.LoadEventsAsync();
        model.SelectedRecordingEvent = Assert.Single(model.RecordingEvents);
        model.IsEventEditorOpen = true;
        model.EditEventTimeText = "00:00:04.000";
        model.EditEventNote = "未保存备注";
        model.BeginEventPlacement(true);
        model.PreviewEventAtSeconds(4);
        using (File.Open(Path.Combine(context.DirectoryPath, "events.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAnyAsync<IOException>(model.CommitEventPlacementAsync);
        Assert.Equal(original, Assert.Single(model.RecordingEvents));
        Assert.Equal("00:00:04.000", model.EditEventTimeText);
        Assert.Equal("未保存备注", model.EditEventNote);
        Assert.True(model.IsEventPlacementActive);
        Assert.NotEmpty(model.EventErrorText);
        Assert.False(model.IsEventSaving);
        await model.CommitEventPlacementAsync();
        var saved = Assert.Single(model.RecordingEvents);
        Assert.Equal(2000, saved.StartSample);
        Assert.Equal(original.DurationSamples, saved.DurationSamples);
        Assert.Equal("未保存备注", saved.Note);
        Assert.False(model.IsEventPlacementActive);
    }

    [Fact]
    public async Task ReadonlyEventsAndFailuresDoNotChangeStorageOrDiscardDraft()
    {
        await using var context = await EventContext.CreateAsync();
        var model = context.Model;
        var item = await context.CreateIntervalAsync(EventSource.ImportedAnnotation);
        await model.LoadEventsAsync();
        model.SelectedRecordingEvent = item;
        Assert.Throws<EventValidationException>(() => model.BeginEventPlacement(true));
        model.EditEventTimeText = "00:00:04.000";
        await Assert.ThrowsAsync<EventValidationException>(model.SaveEditedEventAsync);
        Assert.Equal(item, Assert.Single(model.RecordingEvents));
        model.BeginEventPlacement(false);
        model.PreviewEventAtSeconds(2);
        await context.Definitions.SaveAsync(context.Definition with { IsEnabled = false }, CancellationToken.None);
        await Assert.ThrowsAsync<EventValidationException>(model.CommitEventPlacementAsync);
        Assert.True(model.IsEventPlacementActive);
        Assert.Equal(1000, model.EventPreviewSample);
        Assert.NotEmpty(model.EventErrorText);
        Assert.Equal(item, Assert.Single(model.RecordingEvents));
        model.CancelEventPlacement();
    }

    private sealed class EventContext : IAsyncDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "review-event-tests", Guid.NewGuid().ToString("N"));
        public RecordingReviewViewModel Model { get; private set; } = null!;
        public EventDefinitionService Definitions { get; private set; } = null!;
        public EventDefinition Definition { get; } = new("type", "EO", "睁眼", "", "#C040A0", true, null,
            ShortcutScope.Review, EventDefinitionSource.User, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        public static async Task<EventContext> CreateAsync(int samplingRate = 500)
        {
            var context = new EventContext();
            Directory.CreateDirectory(context.DirectoryPath);
            context.Definitions = new EventDefinitionService(new EventDefinitionStore(Path.Combine(context.DirectoryPath, "definitions.json")));
            await context.Definitions.SaveAsync(context.Definition, CancellationToken.None);
            var configuration = Configuration();
            var profile = Profile("采集导联", configuration);
            var catalog = new RecordingMontageCatalogResult(profile, AcquisitionMontageStatus.Available,
                new RawSignalViewDefinition(["F3"]), [new CompatibleRecordingMontage(profile)], []);
            context.Model = new RecordingReviewViewModel(new FakeReader(Manifest(configuration) with { SamplingRateHz = samplingRate }), catalog,
                recordingDirectory: context.DirectoryPath, eventDefinitionService: context.Definitions);
            context.Model.EnabledEventDefinitions.Add(context.Definition);
            context.Model.SelectedEventDefinitionId = context.Definition.Id;
            return context;
        }

        public Task<RecordingEvent> CreateIntervalAsync(EventSource source = EventSource.ManualButton) =>
            new RecordingEventService(Model.RecordingId, new RecordingEventStore(DirectoryPath), Definitions).CreateAsync(
                Definition.Id, 1001, 333, source, "original-source", "external-code", " original note ", CancellationToken.None,
                sourceSampleCounter: 8001);
        public async ValueTask DisposeAsync()
        {
            await Model.DisposeAsync();
            Directory.Delete(DirectoryPath, true);
        }
    }
}
