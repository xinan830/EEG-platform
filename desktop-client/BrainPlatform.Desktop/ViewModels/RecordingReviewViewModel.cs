using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.Review;

namespace BrainPlatform.Desktop.ViewModels;

public sealed class RecordingReviewViewModel : ObservableObject, IAsyncDisposable
{
    private readonly RecordingReviewSession session;
    private readonly RecordingMontageCatalogResult catalog;

    public RecordingReviewViewModel(
        IRecordingReviewReader reader,
        RecordingMontageCatalogResult catalog,
        double visibleDurationSeconds = 10)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        session = new RecordingReviewSession(reader, catalog, visibleDurationSeconds);
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

    public double VisibleDurationSeconds => session.VisibleDurationSeconds;

    public int OutputChannelCount => session.CurrentFrame?.OutputChannelNames.Count ?? 0;

    public bool IsLoading => session.IsLoading;

    public string StatusText => session.StatusText;

    public Task InitializeAsync() => session.InitializeAsync();

    public Task SeekAsync(double positionSeconds) => session.SeekAsync(positionSeconds);

    public Task SetVisibleDurationAsync(double visibleDurationSeconds) =>
        session.SetVisibleDurationAsync(visibleDurationSeconds);

    public Task SelectViewingMontageAsync(MontageProfile? montage) =>
        session.SelectViewingMontageAsync(montage);

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
    }
}
