using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;

namespace BrainPlatform.Desktop.ViewModels;

public sealed class LiveMonitoringViewModel : ObservableObject, IDisposable
{
    private readonly Func<AcquisitionStateSnapshot> stateProvider;
    private readonly Func<AcquisitionStreamMetadata?> metadataProvider;
    private readonly Func<IReadOnlyList<AcquisitionBatch>> displaySnapshotProvider;
    private readonly Func<IReadOnlyList<long>> displayFilterBoundaryProvider;
    private readonly Func<long?> recordingFirstSampleCounterProvider;
    private readonly DispatcherTimer clock;
    private AcquisitionStreamMetadata? streamMetadata;
    private double paperSpeedMillimetersPerSecond = 30;
    private double sensitivityMicrovoltsPerMillimeter = 10;
    private bool isSettingsOpen;
    private string recordingElapsedText = "--:--:--";
    private string captureStatusText = "未开始采集";
    private long? displaySessionFirstSampleCounter;
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
        Func<long?>? recordingFirstSampleCounterProvider = null)
    {
        this.stateProvider = stateProvider;
        this.metadataProvider = metadataProvider;
        this.displaySnapshotProvider = displaySnapshotProvider;
        this.displayFilterBoundaryProvider = displayFilterBoundaryProvider ?? (() => []);
        this.recordingFirstSampleCounterProvider = recordingFirstSampleCounterProvider ?? (() => null);
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

    public ICommand ToggleSettingsCommand { get; }

    /// <summary>
    /// EEG paper speed. The canvas derives visible seconds from its width, so
    /// this remains independent of pixel resolution and window width.
    /// </summary>
    public double PaperSpeedMillimetersPerSecond
    {
        get => paperSpeedMillimetersPerSecond;
        set
        {
            if (value is not (5d or 10d or 15d or 30d or 60d))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetProperty(ref paperSpeedMillimetersPerSecond, value);
        }
    }

    public double GetDisplayWindowSeconds(double viewportWidthDips)
    {
        if (viewportWidthDips <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidthDips));
        }

        // A WPF DIP is 1/96 inch. This establishes paper-speed geometry in
        // resolution-independent layout units; monitor ruler calibration is a
        // separate workstation concern.
        var viewportMillimeters = viewportWidthDips * 25.4d / 96d;
        return viewportMillimeters / PaperSpeedMillimetersPerSecond;
    }

    public double SensitivityMicrovoltsPerMillimeter
    {
        get => sensitivityMicrovoltsPerMillimeter;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetProperty(ref sensitivityMicrovoltsPerMillimeter, value);
        }
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

    public bool IsRecording => stateProvider().State == AcquisitionState.Recording;

    public bool IsPreviewing => stateProvider().State == AcquisitionState.Previewing;

    public bool IsPaused => stateProvider().State == AcquisitionState.Paused;

    public bool HasOpenRecording => stateProvider().State is AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;

    public bool HasOpenStream => stateProvider().State is AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;

    public IReadOnlyList<string> VisibleChannelLabels => montageProfile is { } montage
        ? montage.DerivedChannels
            .OrderBy(channel => channel.DisplayOrder)
            .Select(channel => channel.Name)
            .ToArray()
        : Channels
            .Where(channel => channel.IsVisible)
            .Select(channel => channel.Label)
            .ToArray();

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
        montageProfile = profile;
        RaisePropertyChanged(nameof(VisibleChannelLabels));
    }

    public void Refresh()
    {
        var currentMetadata = metadataProvider();
        if (!ReferenceEquals(streamMetadata, currentMetadata))
        {
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
            Channels.ToArray());
    }

    public void Dispose()
    {
        clock.Stop();
    }

    private Task ToggleSettingsAsync()
    {
        IsSettingsOpen = !IsSettingsOpen;
        return Task.CompletedTask;
    }

    private void ReportCommandError(Exception exception) =>
        CaptureStatusText = $"操作失败：{exception.Message}";

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
    IReadOnlyList<LiveDisplayChannel>? MontageSourceChannels = null);

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
