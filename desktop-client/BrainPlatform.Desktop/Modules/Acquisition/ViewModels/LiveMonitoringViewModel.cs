using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed class LiveMonitoringViewModel : ObservableObject, IDisposable
{
    private readonly Func<AcquisitionStateSnapshot> stateProvider;
    private readonly Func<AcquisitionStreamMetadata?> metadataProvider;
    private readonly Func<IReadOnlyList<AcquisitionBatch>> displaySnapshotProvider;
    private readonly Func<IReadOnlyList<long>> displayFilterBoundaryProvider;
    private readonly Func<long?> recordingFirstSampleCounterProvider;
    private readonly Func<DateTimeOffset?> recordingStartUtcProvider;
    private readonly Func<IReadOnlyList<RecordingEvent>> recordingEventsProvider;
    private readonly DispatcherTimer clock;
    private AcquisitionStreamMetadata? streamMetadata;
    private readonly WaveformDisplaySettings displaySettings = new();
    private bool isSettingsOpen;
    private string recordingElapsedText = "--:--:--";
    private string captureStatusText = "未开始采集";
    private long? displaySessionFirstSampleCounter;
    private DateTimeOffset? displaySessionStartUtc;
    private long? recordingPauseAnchorRawSampleCounter;
    private long pausedRecordingSampleCount;
    private bool awaitingFirstBatchAfterRecordingPause;
    private readonly List<LiveDisplayCounterAdjustment> displayCounterAdjustments = [];
    private IReadOnlyDictionary<int, ConfiguredDisplayChannel> configuredChannels = new Dictionary<int, ConfiguredDisplayChannel>();
    private MontageProfile? montageProfile;

    public LiveMonitoringViewModel(
        Func<AcquisitionStateSnapshot> stateProvider,
        Func<AcquisitionStreamMetadata?> metadataProvider,
        Func<IReadOnlyList<AcquisitionBatch>> displaySnapshotProvider,
        Dispatcher dispatcher,
        Func<IReadOnlyList<long>>? displayFilterBoundaryProvider = null,
        Func<long?>? recordingFirstSampleCounterProvider = null,
        Func<IReadOnlyList<RecordingEvent>>? recordingEventsProvider = null,
        Func<DateTimeOffset?>? recordingStartUtcProvider = null)
    {
        this.stateProvider = stateProvider;
        this.metadataProvider = metadataProvider;
        this.displaySnapshotProvider = displaySnapshotProvider;
        this.displayFilterBoundaryProvider = displayFilterBoundaryProvider ?? (() => []);
        this.recordingFirstSampleCounterProvider = recordingFirstSampleCounterProvider ?? (() => null);
        this.recordingStartUtcProvider = recordingStartUtcProvider ?? (() => null);
        this.recordingEventsProvider = recordingEventsProvider ?? (() => []);
        displaySettings.PropertyChanged += OnDisplaySettingsChanged;
        ToggleSettingsCommand = new AsyncRelayCommand(ToggleSettingsAsync, ReportCommandError);
        clock = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        clock.Tick += (_, _) => Refresh();
        clock.Start();
        Refresh();
    }

    public ObservableCollection<LiveDisplayChannel> Channels { get; } = [];

    /// <summary>Durably stored event markers for the active Recording only.</summary>
    public IReadOnlyList<RecordingEvent> LiveRecordingEvents => recordingEventsProvider();

    public string FormatEventMarker(RecordingEvent item)
    {
        var metadata = metadataProvider();
        var recordingStartUtc = recordingStartUtcProvider();
        return metadata is null || recordingStartUtc is null
            ? item.DefinitionSnapshot.Name
            : RecordingEventDisplayTime.FormatMarker(item, recordingStartUtc.Value, metadata.SamplingRateHz);
    }

    public string? FormatEventClockTime(RecordingEvent item)
    {
        var metadata = metadataProvider();
        var recordingStartUtc = recordingStartUtcProvider();
        return metadata is null || recordingStartUtc is null
            ? null
            : RecordingClockLabelFormatter.FormatSampleMilliseconds(
                recordingStartUtc.Value, item.StartSample, metadata.SamplingRateHz);
    }

    public DateTimeOffset? GetDisplayClockAnchorUtc()
    {
        var metadata = streamMetadata;
        if (metadata is null || displaySessionFirstSampleCounter is not { } sessionFirst)
        {
            return null;
        }

        if (stateProvider().State is AcquisitionState.Recording or AcquisitionState.Paused)
        {
            if (recordingStartUtcProvider() is not { } recordingStartUtc)
            {
                return null;
            }

            if (recordingFirstSampleCounterProvider() is not { } recordingFirst)
            {
                return displaySessionStartUtc;
            }

            var elapsedSamples = ToDisplayCounter(recordingFirst) - ToDisplayCounter(sessionFirst);
            return recordingStartUtc.AddSeconds(-elapsedSamples / (double)metadata.SamplingRateHz);
        }

        return displaySessionStartUtc;
    }

    /// <summary>Display-only visibility choices for the selected montage outputs.</summary>
    public ObservableCollection<LiveMontageDisplayChannel> MontageChannels { get; } = [];

    public WaveformDisplaySettings DisplaySettings => displaySettings;

    public ICommand ToggleSettingsCommand { get; }

    /// <summary>
    /// EEG paper speed. The canvas derives visible seconds from its width, so
    /// this remains independent of pixel resolution and window width.
    /// </summary>
    public double PaperSpeedMillimetersPerSecond
    {
        get => displaySettings.PaperSpeedMillimetersPerSecond;
        set => displaySettings.PaperSpeedMillimetersPerSecond = value;
    }

    /// <summary>
    /// The active user input for horizontal display scale. The scientific time
    /// axis remains sample-counter based in both modes.
    /// </summary>
    public HorizontalTimeScaleMode HorizontalTimeScaleMode
    {
        get => displaySettings.HorizontalTimeScaleMode;
        set => displaySettings.HorizontalTimeScaleMode = value;
    }

    public bool IsPaperSpeedMode
    {
        get => displaySettings.IsPaperSpeedMode;
        set
        {
            if (value)
            {
                HorizontalTimeScaleMode = HorizontalTimeScaleMode.PaperSpeed;
            }
        }
    }

    public bool IsTimebaseMode
    {
        get => displaySettings.IsTimebaseMode;
        set
        {
            if (value)
            {
                HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase;
            }
        }
    }

    /// <summary>Displayed seconds per screen when timebase input is active.</summary>
    public double TimebaseSecondsPerScreen
    {
        get => displaySettings.TimebaseSecondsPerScreen;
        set => displaySettings.TimebaseSecondsPerScreen = value;
    }

    /// <summary>The effective screen duration, independent of the chosen input mode.</summary>
    public double EffectiveTimebaseSeconds => GetDisplayWindowSeconds(Math.Max(1d, displaySettings.ViewportWidthDips));

    /// <summary>
    /// The nominal paper speed corresponding to a timebase selection. It is
    /// explicitly derived because Windows layout units are not a calibrated
    /// physical ruler on arbitrary monitors.
    /// </summary>
    public double DerivedPaperSpeedMillimetersPerSecond =>
        displaySettings.DerivedPaperSpeedMillimetersPerSecond;

    public void UpdateViewportWidth(double widthDips)
    {
        if (!double.IsFinite(widthDips) || widthDips <= 0 || Math.Abs(widthDips - displaySettings.ViewportWidthDips) < 0.5)
        {
            return;
        }

        displaySettings.ViewportWidthDips = widthDips;
    }

    public void UpdateScreenScale(ScreenScaleContext value)
    {
        displaySettings.ScreenScale = value;
    }

    public double GetDisplayWindowSeconds(double viewportWidthDips)
    {
        if (viewportWidthDips <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidthDips));
        }

        return displaySettings.GetVisibleSeconds(viewportWidthDips);
    }

    public double SensitivityMicrovoltsPerMillimeter
    {
        get => displaySettings.SensitivityMicrovoltsPerMillimeter;
        set => displaySettings.SensitivityMicrovoltsPerMillimeter = value;
    }

    private void RaiseHorizontalScalePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsPaperSpeedMode));
        RaisePropertyChanged(nameof(IsTimebaseMode));
        RaisePropertyChanged(nameof(EffectiveTimebaseSeconds));
        RaisePropertyChanged(nameof(DerivedPaperSpeedMillimetersPerSecond));
    }

    public bool IsSettingsOpen
    {
        get => isSettingsOpen;
        set => SetProperty(ref isSettingsOpen, value);
    }

    public string RecordingElapsedText
    {
        get => recordingElapsedText;
        private set => SetProperty(ref recordingElapsedText, value);
    }

    public string CaptureStatusText
    {
        get => captureStatusText;
        private set => SetProperty(ref captureStatusText, value);
    }

    public string WaveformStatusText
    {
        get
        {
            var state = stateProvider();
            return state.State == AcquisitionState.Faulted
                ? $"采集已停止：{state.Detail}"
                : "等待设备连接并开始采集";
        }
    }

    public bool IsRecording => stateProvider().State == AcquisitionState.Recording;

    public bool IsPreviewing => stateProvider().State == AcquisitionState.Previewing;

    public bool IsPaused => stateProvider().State == AcquisitionState.Paused;

    public bool HasOpenRecording => stateProvider().State is AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;

    public bool HasOpenStream => stateProvider().State is AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;

    public IReadOnlyList<string> VisibleChannelLabels => montageProfile is not null
        ? MontageChannels
            .Where(channel => channel.IsVisible)
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => channel.Name)
            .ToArray()
        : Channels
            .Where(channel => channel.IsVisible)
            .Select(channel => channel.Label)
            .ToArray();

    public bool HasSelectedMontage => montageProfile is not null;

    public string CurrentMontageName => montageProfile?.Name ?? "未选择导联配置";

    public int MontageOutputChannelCount => MontageChannels.Count;

    public int SelectedMontageDisplayChannelCount => MontageChannels.Count(channel => channel.IsVisible);

    public void ConfigureChannels(IEnumerable<ChannelLabelMappingRow> rows)
    {
        configuredChannels = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ElectrodeLabel))
            .ToDictionary(
                row => row.NativeChannelIndex,
                row => new ConfiguredDisplayChannel(row.ElectrodeLabel.Trim(), row.Role, row.IsSelectedForDisplay));
        ReplaceChannels(streamMetadata);
        RaisePropertyChanged(nameof(VisibleChannelLabels));
    }

    public void ConfigureMontage(MontageProfile? profile)
    {
        var previousVisibility = MontageChannels.ToDictionary(channel => channel.Name, channel => channel.IsVisible, StringComparer.OrdinalIgnoreCase);
        montageProfile = profile;
        MontageChannels.Clear();
        if (profile is not null)
        {
            foreach (var channel in profile.DerivedChannels.OrderBy(channel => channel.DisplayOrder))
            {
                var output = new LiveMontageDisplayChannel(
                    channel.Name,
                    channel.DisplayOrder,
                    previousVisibility.GetValueOrDefault(channel.Name, true));
                output.PropertyChanged += OnMontageChannelPropertyChanged;
                MontageChannels.Add(output);
            }
        }

        RaisePropertyChanged(nameof(HasSelectedMontage));
        RaisePropertyChanged(nameof(CurrentMontageName));
        RaisePropertyChanged(nameof(MontageOutputChannelCount));
        RaisePropertyChanged(nameof(SelectedMontageDisplayChannelCount));
        RaisePropertyChanged(nameof(VisibleChannelLabels));
    }

    public void Refresh()
    {
        var currentMetadata = metadataProvider();
        if (!ReferenceEquals(streamMetadata, currentMetadata))
        {
            if (streamMetadata is null || currentMetadata is null)
            {
                displaySessionStartUtc = currentMetadata?.RecordingStartUtc;
            }
            streamMetadata = currentMetadata;
            ResetDisplayTimeline();
            ReplaceChannels(currentMetadata);
        }

        var state = stateProvider();
        var batches = currentMetadata is null ? [] : displaySnapshotProvider();
        UpdateDisplayTimeline(state.State, batches);
        CaptureStatusText = state.State switch
        {
            AcquisitionState.Previewing => "实时预览中，尚未记录",
            AcquisitionState.Recording => "正在记录真实 EEG",
            AcquisitionState.Paused => "记录已暂停，实时预览继续",
            AcquisitionState.Starting => "正在打开 EEG 数据流",
            AcquisitionState.Stopping => "正在停止采集",
            AcquisitionState.Faulted => "采集故障",
            AcquisitionState.Stopped => "记录已停止",
            AcquisitionState.Ready => "设备已连接，等待开始",
            _ => "未开始采集",
        };
        RecordingElapsedText = currentMetadata is null || state.State is not (AcquisitionState.Recording or AcquisitionState.Paused)
            ? "--:--:--"
            : FormatDuration(GetEffectiveAcquisitionDuration(currentMetadata, batches, recordingFirstSampleCounterProvider()));
        RaisePropertyChanged(nameof(IsRecording));
        RaisePropertyChanged(nameof(IsPreviewing));
        RaisePropertyChanged(nameof(IsPaused));
        RaisePropertyChanged(nameof(HasOpenRecording));
        RaisePropertyChanged(nameof(HasOpenStream));
        RaisePropertyChanged(nameof(VisibleChannelLabels));
        RaisePropertyChanged(nameof(WaveformStatusText));
    }

    public LiveWaveformSource? GetWaveformSource()
    {
        var metadata = streamMetadata;
        if (metadata is null)
        {
            return null;
        }

        var batches = displaySnapshotProvider();
        UpdateDisplayTimeline(stateProvider().State, batches);
        RecordingElapsedText = stateProvider().State is AcquisitionState.Recording or AcquisitionState.Paused
            ? FormatDuration(GetEffectiveAcquisitionDuration(metadata, batches, recordingFirstSampleCounterProvider()))
            : "--:--:--";

        return new LiveWaveformSource(
            metadata,
            batches,
            Channels.Where(channel => channel.IsVisible).ToArray(),
            displaySessionFirstSampleCounter,
            displayCounterAdjustments.ToArray(),
            displayFilterBoundaryProvider(),
            montageProfile,
            Channels.ToArray(),
            montageProfile is null
                ? null
                : MontageChannels
                    .Where(channel => channel.IsVisible)
                    .Select(channel => channel.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Converts an event's Recording-relative sample coordinate back into the
    /// display counter used by the cyclic live page. A counter reset cannot be
    /// represented as a continuous display coordinate and returns null.
    /// </summary>
    public long? ToDisplayCounterFromRecordingSample(long recordingRelativeSample)
    {
        if (recordingRelativeSample < 0 || recordingFirstSampleCounterProvider() is not { } recordingFirst)
        {
            return null;
        }

        try
        {
            return ToDisplayCounter(checked(recordingFirst + recordingRelativeSample));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        clock.Stop();
        displaySettings.PropertyChanged -= OnDisplaySettingsChanged;
    }

    private Task ToggleSettingsAsync()
    {
        IsSettingsOpen = !IsSettingsOpen;
        return Task.CompletedTask;
    }

    private void OnDisplaySettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(e.PropertyName);
        RaiseHorizontalScalePropertiesChanged();
    }

    private void ReportCommandError(Exception exception) =>
        CaptureStatusText = $"操作失败：{exception.Message}";

    private void OnMontageChannelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(LiveMontageDisplayChannel.IsVisible))
        {
            RaisePropertyChanged(nameof(SelectedMontageDisplayChannelCount));
            RaisePropertyChanged(nameof(VisibleChannelLabels));
        }
    }

    private void UpdateDisplayTimeline(AcquisitionState state, IReadOnlyList<AcquisitionBatch> batches)
    {
        if (batches.Count > 0 && displaySessionFirstSampleCounter is null)
        {
            displaySessionFirstSampleCounter = batches[0].FirstSampleCounter;
        }

        // The waveform timeline is always raw sample-counter time. Recording
        // pause only excludes a range from raw persistence and its duration;
        // it must never freeze or rewrite the live paper trace.
        if (state == AcquisitionState.Paused)
        {
            if (batches.Count > 0)
            {
                recordingPauseAnchorRawSampleCounter = batches[^1].LastSampleCounter;
            }

            awaitingFirstBatchAfterRecordingPause = true;
            return;
        }

        if (state != AcquisitionState.Recording || !awaitingFirstBatchAfterRecordingPause ||
            recordingPauseAnchorRawSampleCounter is not { } pauseAnchor)
        {
            return;
        }

        var firstBatchAfterResume = batches
            .Where(batch => batch.FirstSampleCounter > pauseAnchor)
            .OrderBy(batch => batch.FirstSampleCounter)
            .FirstOrDefault();
        if (firstBatchAfterResume is null)
        {
            return;
        }

        pausedRecordingSampleCount = checked(pausedRecordingSampleCount +
            Math.Max(0L, firstBatchAfterResume.FirstSampleCounter - pauseAnchor - 1L));
        recordingPauseAnchorRawSampleCounter = null;
        awaitingFirstBatchAfterRecordingPause = false;
    }

    private TimeSpan GetEffectiveAcquisitionDuration(
        AcquisitionStreamMetadata metadata,
        IReadOnlyList<AcquisitionBatch> batches,
        long? recordingStartRawSampleCounter)
    {
        var recordingStart = recordingStartRawSampleCounter ?? displaySessionFirstSampleCounter;
        if (recordingStart is not { } startCounter || batches.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var lastRawCounter = batches[^1].LastSampleCounter;
        var sampleCount = Math.Max(0, lastRawCounter - startCounter + 1L - pausedRecordingSampleCount);
        return TimeSpan.FromSeconds(sampleCount / (double)metadata.SamplingRateHz);
    }

    private long ToDisplayCounter(long rawSampleCounter)
    {
        var skipped = 0L;
        foreach (var adjustment in displayCounterAdjustments)
        {
            if (rawSampleCounter < adjustment.EffectiveFromRawSampleCounter)
            {
                break;
            }

            skipped = adjustment.SkippedSampleCountBefore;
        }

        return checked(rawSampleCounter - skipped);
    }

    private void ResetDisplayTimeline()
    {
        displaySessionFirstSampleCounter = null;
        recordingPauseAnchorRawSampleCounter = null;
        pausedRecordingSampleCount = 0;
        awaitingFirstBatchAfterRecordingPause = false;
        displayCounterAdjustments.Clear();
    }

    private void ReplaceChannels(AcquisitionStreamMetadata? metadata)
    {
        var existingVisibility = Channels.ToDictionary(channel => channel.NativeChannelIndex, channel => channel.IsVisible);
        Channels.Clear();
        if (metadata is null)
        {
            foreach (var configured in configuredChannels.OrderBy(pair => pair.Key))
            {
                Channels.Add(new LiveDisplayChannel(
                    -1,
                    configured.Key,
                    configured.Value.Label,
                    configured.Value.Kind,
                    configured.Value.IsSelectedForDisplay));
            }
            return;
        }

        var candidates = metadata.Channels
            .Where(channel => channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar)
            .ToArray();
        var defaultVisibleCount = 0;
        foreach (var channel in candidates)
        {
            var isConfigured = configuredChannels.TryGetValue(channel.NativeChannelIndex, out var configured);
            var visible = isConfigured
                ? configured!.IsSelectedForDisplay
                : configuredChannels.Count > 0
                    ? false
                : existingVisibility.TryGetValue(channel.NativeChannelIndex, out var saved)
                    ? saved
                    : defaultVisibleCount++ < 8;
            Channels.Add(new LiveDisplayChannel(
                channel.StreamIndex,
                channel.NativeChannelIndex,
                isConfigured ? configured!.Label : channel.Label ?? $"CH {channel.NativeChannelIndex}",
                isConfigured ? configured!.Kind : channel.Kind.ToString(),
                visible));
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        $"{Math.Max(0, (int)duration.TotalHours):00}:{duration.Minutes:00}:{duration.Seconds:00}";
}

internal sealed record ConfiguredDisplayChannel(string Label, string Kind, bool IsSelectedForDisplay);

public sealed record LiveWaveformSource(
    AcquisitionStreamMetadata Metadata,
    IReadOnlyList<AcquisitionBatch> Batches,
    IReadOnlyList<LiveDisplayChannel> Channels,
    long? SessionFirstSampleCounter = null,
    IReadOnlyList<LiveDisplayCounterAdjustment>? DisplayCounterAdjustments = null,
    IReadOnlyList<long>? DisplayFilterBoundaries = null,
    MontageProfile? MontageProfile = null,
    IReadOnlyList<LiveDisplayChannel>? MontageSourceChannels = null,
    IReadOnlySet<string>? VisibleMontageChannelNames = null);

public sealed record LiveDisplayCounterAdjustment(
    long EffectiveFromRawSampleCounter,
    long SkippedSampleCountBefore);

public sealed class LiveDisplayChannel : ObservableObject
{
    private bool isVisible;

    public LiveDisplayChannel(int streamIndex, int nativeChannelIndex, string label, string kind, bool isVisible)
    {
        StreamIndex = streamIndex;
        NativeChannelIndex = nativeChannelIndex;
        Label = label;
        Kind = kind;
        this.isVisible = isVisible;
    }

    public int StreamIndex { get; }

    public int NativeChannelIndex { get; }

    public string Label { get; }

    public string Kind { get; }

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }
}

public sealed class LiveMontageDisplayChannel : ObservableObject
{
    private bool isVisible;

    public LiveMontageDisplayChannel(string name, int displayOrder, bool isVisible)
    {
        Name = name;
        DisplayOrder = displayOrder;
        this.isVisible = isVisible;
    }

    public string Name { get; }

    public int DisplayOrder { get; }

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }
}
