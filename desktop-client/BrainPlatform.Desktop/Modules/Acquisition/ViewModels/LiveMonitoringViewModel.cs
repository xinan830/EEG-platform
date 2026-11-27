using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Channels.Domain;
using BrainPlatform.Desktop.Modules.Channels.Services;
using BrainPlatform.Desktop.Modules.Montages.Domain;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed partial class LiveMonitoringViewModel : ObservableObject, IDisposable
{
    private readonly Func<AcquisitionStateSnapshot> stateProvider;
    private readonly Func<AcquisitionStreamMetadata?> metadataProvider;
    private readonly Func<IReadOnlyList<AcquisitionBatch>> displaySnapshotProvider;
    private readonly Func<IReadOnlyList<long>> displayFilterBoundaryProvider;
    private readonly Func<long?> recordingFirstSampleCounterProvider;
    private readonly Func<DateTimeOffset?> recordingStartUtcProvider;
    private readonly Func<IReadOnlyList<RecordingEvent>> recordingEventsProvider;
    private readonly Func<IReadOnlyList<RecordingLifecycleBoundary>> lifecycleBoundariesProvider;
    private readonly DispatcherTimer clock;
    private readonly WaveformDisplaySettings displaySettings = new();
    private readonly LiveDisplayTimelineTracker timeline = new();
    private AcquisitionStreamMetadata? streamMetadata;
    private bool isSettingsOpen;
    private string recordingElapsedText = "--:--:--";
    private string captureStatusText = "未开始采集";
    private IReadOnlyDictionary<int, ConfiguredDisplayChannel> configuredChannels = new Dictionary<int, ConfiguredDisplayChannel>();
    private ChannelConfigurationProfile? channelConfigurationSnapshot;
    private MontageProfile? montageProfile;

    public LiveMonitoringViewModel(
        Func<AcquisitionStateSnapshot> stateProvider,
        Func<AcquisitionStreamMetadata?> metadataProvider,
        Func<IReadOnlyList<AcquisitionBatch>> displaySnapshotProvider,
        Dispatcher dispatcher,
        Func<IReadOnlyList<long>>? displayFilterBoundaryProvider = null,
        Func<long?>? recordingFirstSampleCounterProvider = null,
        Func<IReadOnlyList<RecordingEvent>>? recordingEventsProvider = null,
        Func<DateTimeOffset?>? recordingStartUtcProvider = null,
        Func<IReadOnlyList<RecordingLifecycleBoundary>>? lifecycleBoundariesProvider = null)
    {
        this.stateProvider = stateProvider;
        this.metadataProvider = metadataProvider;
        this.displaySnapshotProvider = displaySnapshotProvider;
        this.displayFilterBoundaryProvider = displayFilterBoundaryProvider ?? (() => []);
        this.recordingFirstSampleCounterProvider = recordingFirstSampleCounterProvider ?? (() => null);
        this.recordingStartUtcProvider = recordingStartUtcProvider ?? (() => null);
        this.recordingEventsProvider = recordingEventsProvider ?? (() => []);
        this.lifecycleBoundariesProvider = lifecycleBoundariesProvider ?? (() => []);
        displaySettings.PropertyChanged += OnDisplaySettingsChanged;
        ToggleSettingsCommand = new AsyncRelayCommand(ToggleSettingsAsync, ReportCommandError);
        clock = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => Refresh();
        clock.Start();
        Refresh();
    }

    public ObservableCollection<LiveDisplayChannel> Channels { get; } = [];

    /// <summary>Durably stored event markers for the active Recording only.</summary>
    public IReadOnlyList<RecordingEvent> LiveRecordingEvents => recordingEventsProvider();
    public IReadOnlyList<RecordingLifecycleBoundary> LiveLifecycleBoundaries => lifecycleBoundariesProvider();
    public long? RecordingFirstSampleCounter => recordingFirstSampleCounterProvider();
    public int SamplingRateHz => streamMetadata?.SamplingRateHz ?? 0;
    public DateTimeOffset? RecordingStartUtc => recordingStartUtcProvider();

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

    public DateTimeOffset? GetDisplayClockAnchorUtc() => timeline.GetDisplayClockAnchorUtc(
        streamMetadata,
        stateProvider().State,
        recordingStartUtcProvider(),
        recordingFirstSampleCounterProvider());

    /// <summary>Display-only visibility choices for the selected montage outputs.</summary>
    public ObservableCollection<LiveMontageDisplayChannel> MontageChannels { get; } = [];

    public WaveformDisplaySettings DisplaySettings => displaySettings;
    public ICommand ToggleSettingsCommand { get; }

    public double PaperSpeedMillimetersPerSecond
    {
        get => displaySettings.PaperSpeedMillimetersPerSecond;
        set => displaySettings.PaperSpeedMillimetersPerSecond = value;
    }

    public HorizontalTimeScaleMode HorizontalTimeScaleMode
    {
        get => displaySettings.HorizontalTimeScaleMode;
        set => displaySettings.HorizontalTimeScaleMode = value;
    }

    public bool IsPaperSpeedMode
    {
        get => displaySettings.IsPaperSpeedMode;
        set { if (value) HorizontalTimeScaleMode = HorizontalTimeScaleMode.PaperSpeed; }
    }

    public bool IsTimebaseMode
    {
        get => displaySettings.IsTimebaseMode;
        set { if (value) HorizontalTimeScaleMode = HorizontalTimeScaleMode.Timebase; }
    }

    public double TimebaseSecondsPerScreen
    {
        get => displaySettings.TimebaseSecondsPerScreen;
        set => displaySettings.TimebaseSecondsPerScreen = value;
    }

    public double EffectiveTimebaseSeconds => GetDisplayWindowSeconds(Math.Max(1d, displaySettings.ViewportWidthDips));
    public double DerivedPaperSpeedMillimetersPerSecond => displaySettings.DerivedPaperSpeedMillimetersPerSecond;

    public void UpdateViewportWidth(double widthDips)
    {
        if (!double.IsFinite(widthDips) || widthDips <= 0 || Math.Abs(widthDips - displaySettings.ViewportWidthDips) < 0.5) return;
        displaySettings.ViewportWidthDips = widthDips;
    }

    public void UpdateScreenScale(ScreenScaleContext value) => displaySettings.ScreenScale = value;

    public double GetDisplayWindowSeconds(double viewportWidthDips)
    {
        if (viewportWidthDips <= 0) throw new ArgumentOutOfRangeException(nameof(viewportWidthDips));
        return displaySettings.GetVisibleSeconds(viewportWidthDips);
    }

    public double SensitivityMicrovoltsPerMillimeter
    {
        get => displaySettings.SensitivityMicrovoltsPerMillimeter;
        set => displaySettings.SensitivityMicrovoltsPerMillimeter = value;
    }

    public bool IsSettingsOpen { get => isSettingsOpen; set => SetProperty(ref isSettingsOpen, value); }
    public string RecordingElapsedText { get => recordingElapsedText; private set => SetProperty(ref recordingElapsedText, value); }
    public string CaptureStatusText { get => captureStatusText; private set => SetProperty(ref captureStatusText, value); }

    public string WaveformStatusText
    {
        get
        {
            var state = stateProvider();
            return state.State == AcquisitionState.Faulted ? $"采集已停止：{state.Detail}" : "等待设备连接并开始采集";
        }
    }

    public bool IsRecording => stateProvider().State == AcquisitionState.Recording;
    public bool IsPreviewing => stateProvider().State == AcquisitionState.Previewing;
    public bool IsPaused => stateProvider().State == AcquisitionState.Paused;
    public bool HasOpenRecording => stateProvider().State is AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;
    public bool HasOpenStream => stateProvider().State is AcquisitionState.Previewing or AcquisitionState.Recording or AcquisitionState.Paused or AcquisitionState.Stopping;

    public IReadOnlyList<string> VisibleChannelLabels => montageProfile is not null
        ? MontageChannels.Where(channel => channel.IsVisible).OrderBy(channel => channel.DisplayOrder).Select(channel => channel.Name).ToArray()
        : Channels.Where(channel => channel.IsVisible).Select(channel => channel.Label).ToArray();

    public bool HasSelectedMontage => montageProfile is not null;
    public IReadOnlyList<string> VisibleChannelColors => MontageOutputAppearance.ResolveColors(montageProfile, VisibleChannelLabels);
    public string CurrentMontageName => montageProfile?.DisplayName ?? "未选择导联配置";
    public int MontageOutputChannelCount => MontageChannels.Count;
    public int SelectedMontageDisplayChannelCount => MontageChannels.Count(channel => channel.IsVisible);

    public void ConfigureChannels(IEnumerable<ChannelLabelMappingRow> rows)
    {
        var configuredRows = rows.ToArray();
        configuredChannels = configuredRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ElectrodeLabel))
            .ToDictionary(
                row => row.NativeChannelIndex,
                row => new ConfiguredDisplayChannel(row.ElectrodeLabel.Trim(), row.Role, row.IsSelectedForDisplay));
        channelConfigurationSnapshot = new ChannelConfigurationProfile(
            "live-monitor", "Live monitor", string.Empty, ChannelConfigurationSource.System, "live-monitor",
            configuredRows.Where(row => row.IsEegInput).Select(row => new ChannelConfigurationEntry(
                row.NativeChannelIndex,
                Enum.TryParse<AcquisitionChannelKind>(row.Role, true, out var kind) ? kind : AcquisitionChannelKind.Unknown,
                row.ElectrodeLabel.Trim(), row.IsSelectedForDisplay, row.DisplayOrder)).ToArray(),
            DateTimeOffset.MinValue, DateTimeOffset.MinValue);
        ReplaceChannels(streamMetadata);
        RaisePropertyChanged(nameof(VisibleChannelLabels));
    }

    public void ConfigureMontage(MontageProfile? profile)
    {
        var previousVisibility = MontageChannels.ToDictionary(channel => channel.Name, channel => channel.IsVisible, StringComparer.OrdinalIgnoreCase);
        montageProfile = profile;
        ClearMontageChannels();
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
        var currentMetadata = IsDisplayFrozen ? streamMetadata : metadataProvider();
        if (!IsDisplayFrozen && !ReferenceEquals(streamMetadata, currentMetadata))
        {
            var wasOrIsNull = streamMetadata is null || currentMetadata is null;
            streamMetadata = currentMetadata;
            timeline.OnMetadataChanged(wasOrIsNull, currentMetadata?.RecordingStartUtc);
            ReplaceChannels(currentMetadata);
        }

        var state = stateProvider();
        var batches = currentMetadata is null ? [] : displaySnapshotProvider();
        var recordingFirst = recordingFirstSampleCounterProvider();
        timeline.Update(state.State, batches, recordingFirst);
        CaptureStatusText = IsDisplayFrozen
            ? "阻抗检测中，EEG 已停止；波形已冻结"
            : state.State switch
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
            : FormatDuration(timeline.GetEffectiveAcquisitionDuration(
                currentMetadata, batches, recordingFirst, lifecycleBoundariesProvider()));
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
        if (IsDisplayFrozen) return frozenPreviewSource;
        var metadata = streamMetadata;
        if (metadata is null) return null;

        var batches = displaySnapshotProvider();
        var recordingFirst = recordingFirstSampleCounterProvider();
        timeline.Update(stateProvider().State, batches, recordingFirst);
        RecordingElapsedText = stateProvider().State is AcquisitionState.Recording or AcquisitionState.Paused
            ? FormatDuration(timeline.GetEffectiveAcquisitionDuration(
                metadata, batches, recordingFirst, lifecycleBoundariesProvider()))
            : "--:--:--";

        var source = new LiveWaveformSource(
            metadata,
            batches,
            Channels.Where(channel => channel.IsVisible).ToArray(),
            timeline.SessionFirstSampleCounter,
            timeline.DisplayCounterAdjustments.ToArray(),
            displayFilterBoundaryProvider(),
            montageProfile,
            Channels.ToArray(),
            montageProfile is null
                ? null
                : MontageChannels
                    .Where(channel => channel.IsVisible)
                    .Select(channel => channel.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
        return previewDisplayContinuity?.Compose(source) ?? source;
    }

    /// <summary>
    /// Converts an event's Recording-relative sample coordinate back into the
    /// display counter used by the cyclic live page. A counter reset cannot be
    /// represented as a continuous display coordinate and returns null.
    /// </summary>
    public long? ToDisplayCounterFromRecordingSample(long recordingRelativeSample) =>
        timeline.ToDisplayCounterFromRecordingSample(recordingRelativeSample, recordingFirstSampleCounterProvider());

    public void Dispose()
    {
        clock.Stop();
        displaySettings.PropertyChanged -= OnDisplaySettingsChanged;
        ClearMontageChannels();
    }

    private void ClearMontageChannels()
    {
        foreach (var channel in MontageChannels)
        {
            channel.PropertyChanged -= OnMontageChannelPropertyChanged;
        }

        MontageChannels.Clear();
    }

    private void RaiseHorizontalScalePropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsPaperSpeedMode));
        RaisePropertyChanged(nameof(IsTimebaseMode));
        RaisePropertyChanged(nameof(EffectiveTimebaseSeconds));
        RaisePropertyChanged(nameof(DerivedPaperSpeedMillimetersPerSecond));
    }

    private Task ToggleSettingsAsync() { IsSettingsOpen = !IsSettingsOpen; return Task.CompletedTask; }

    private void OnDisplaySettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        RaisePropertyChanged(e.PropertyName);
        RaiseHorizontalScalePropertiesChanged();
    }

    private void ReportCommandError(Exception exception) => CaptureStatusText = $"操作失败：{exception.Message}";

    private void OnMontageChannelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(LiveMontageDisplayChannel.IsVisible))
        {
            RaisePropertyChanged(nameof(SelectedMontageDisplayChannelCount));
            RaisePropertyChanged(nameof(VisibleChannelLabels));
        }
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

        var projection = EnabledEegChannelProjection.Project(channelConfigurationSnapshot, metadata.Channels);
        var defaultVisibleCount = 0;
        foreach (var channel in projection.Channels)
        {
            var visible = existingVisibility.TryGetValue(channel.NativeChannelIndex, out var saved)
                ? saved
                : defaultVisibleCount++ < 8;
            Channels.Add(new LiveDisplayChannel(
                channel.StreamIndex,
                channel.NativeChannelIndex,
                channel.Label,
                channel.Kind.ToString(),
                visible));
        }
    }

    private static string FormatDuration(TimeSpan duration) =>
        $"{Math.Max(0, (int)duration.TotalHours):00}:{duration.Minutes:00}:{duration.Seconds:00}";
}
