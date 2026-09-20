using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.Review;

namespace BrainPlatform.Desktop.ViewModels;

public sealed class RecordingReviewViewModel : ObservableObject, IAsyncDisposable
{
    private readonly RecordingReviewSession session;
    private readonly RecordingMontageCatalogResult catalog;
    private readonly RecordingPlaybackController playback;
    private readonly int samplingRateHz;
    private readonly int sourceChannelCount;
    private readonly string projectName;
    private readonly string recordingName;

    public RecordingReviewViewModel(
        IRecordingReviewReader reader,
        RecordingMontageCatalogResult catalog,
        double visibleDurationSeconds = 10,
        string? recordingName = null,
        string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        session = new RecordingReviewSession(reader, catalog, visibleDurationSeconds);
        playback = new RecordingPlaybackController(reader.DurationSeconds);
        samplingRateHz = reader.Manifest.SamplingRateHz;
        sourceChannelCount = reader.Manifest.Channels.Count(channel =>
            channel.Kind is AcquisitionChannelKind.Reference or AcquisitionChannelKind.Bipolar);
        this.projectName = string.IsNullOrWhiteSpace(projectName)
            ? reader.Manifest.Project.Name
            : projectName;
        this.recordingName = string.IsNullOrWhiteSpace(recordingName)
            ? "采集记录"
            : recordingName;
        session.Changed += OnSessionChanged;
        foreach (var item in catalog.CompatibleViewingMontages)
        {
            CompatibleViewingMontages.Add(item.Profile);
        }
    }

    public ObservableCollection<MontageProfile> CompatibleViewingMontages { get; } = [];

    public string AcquisitionMontageText => catalog.AcquisitionMontageStatusText;

    public MontageProfile? AcquisitionMontage => session.AcquisitionMontage;

    public MontageProfile? CurrentViewingMontage => session.SelectedViewingMontage;

    public RecordingReviewFrame? CurrentFrame => session.CurrentFrame;

    public double PositionSeconds => session.PositionSeconds;

    public double DurationSeconds => session.DurationSeconds;

    public int SamplingRateHz => samplingRateHz;

    public int SourceChannelCount => sourceChannelCount;

    public string ProjectName => projectName;

    public string RecordingName => recordingName;

    public double VisibleDurationSeconds => session.VisibleDurationSeconds;

    public int OutputChannelCount => session.CurrentFrame?.OutputChannelNames.Count ?? 0;

    public bool IsLoading => session.IsLoading;

    public bool IsPlaying => playback.IsPlaying;

    public bool IsCompleted => playback.IsCompleted;

    public string StatusText => session.StatusText;

    public Task InitializeAsync() => session.InitializeAsync();

    public Task SeekAsync(double positionSeconds)
    {
        playback.Seek(positionSeconds);
        RaisePlaybackPropertiesChanged();
        return session.SeekAsync(playback.PositionSeconds);
    }

    public Task SetVisibleDurationAsync(double visibleDurationSeconds) =>
        session.SetVisibleDurationAsync(visibleDurationSeconds);

    public Task SelectViewingMontageAsync(MontageProfile? montage) =>
        session.SelectViewingMontageAsync(montage);

    public void TogglePlayback()
    {
        if (playback.IsPlaying)
        {
            playback.Pause();
        }
        else
        {
            playback.Play();
        }

        RaisePlaybackPropertiesChanged();
    }

    public async Task TickPlaybackAsync()
    {
        if (!playback.Update())
        {
            return;
        }

        RaisePlaybackPropertiesChanged();
        await session.SeekAsync(playback.PositionSeconds);
    }

    public async ValueTask DisposeAsync()
    {
        session.Changed -= OnSessionChanged;
        await session.DisposeAsync();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        RaisePropertyChanged(nameof(AcquisitionMontageText));
        RaisePropertyChanged(nameof(CurrentViewingMontage));
        RaisePropertyChanged(nameof(CurrentFrame));
        RaisePropertyChanged(nameof(PositionSeconds));
        RaisePropertyChanged(nameof(VisibleDurationSeconds));
        RaisePropertyChanged(nameof(OutputChannelCount));
        RaisePropertyChanged(nameof(IsLoading));
        RaisePropertyChanged(nameof(StatusText));
        RaisePlaybackPropertiesChanged();
    }

    private void RaisePlaybackPropertiesChanged()
    {
        RaisePropertyChanged(nameof(IsPlaying));
        RaisePropertyChanged(nameof(IsCompleted));
    }
}
