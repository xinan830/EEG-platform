using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Acquisition.Storage;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

/// <summary>
/// Periodically samples active recording size and free disk space off the UI
/// thread so the acquisition header can keep storage telemetry visible during
/// Recording and Paused states without blocking WPF rendering.
/// </summary>
public sealed class AcquisitionStorageStatusViewModel : ObservableObject, IDisposable
{
    private readonly Func<AcquisitionState> stateProvider;
    private readonly Func<string?> recordingDirectoryProvider;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;
    private int sampling;
    private bool disposed;
    private bool hasActiveRecordingMetrics;
    private string recordedFileSizeText = "--";
    private string availableDiskSpaceText = "--";
    private bool isLowDiskSpace;

    public AcquisitionStorageStatusViewModel(
        Func<AcquisitionState> stateProvider,
        Func<string?> recordingDirectoryProvider,
        Dispatcher dispatcher)
    {
        this.stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        this.recordingDirectoryProvider = recordingDirectoryProvider ?? throw new ArgumentNullException(nameof(recordingDirectoryProvider));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        timer.Tick += OnTimerTick;
    }

    public bool HasActiveRecordingMetrics
    {
        get => hasActiveRecordingMetrics;
        private set => SetProperty(ref hasActiveRecordingMetrics, value);
    }

    public string RecordedFileSizeText
    {
        get => recordedFileSizeText;
        private set => SetProperty(ref recordedFileSizeText, value);
    }

    public string AvailableDiskSpaceText
    {
        get => availableDiskSpaceText;
        private set => SetProperty(ref availableDiskSpaceText, value);
    }

    public bool IsLowDiskSpace
    {
        get => isLowDiskSpace;
        private set => SetProperty(ref isLowDiskSpace, value);
    }

    public void RefreshState()
    {
        if (disposed)
        {
            return;
        }

        var isActiveRecording = stateProvider() is AcquisitionState.Recording or AcquisitionState.Paused;
        HasActiveRecordingMetrics = isActiveRecording;
        if (!isActiveRecording)
        {
            timer.Stop();
            RecordedFileSizeText = "--";
            AvailableDiskSpaceText = "--";
            IsLowDiskSpace = false;
            return;
        }

        if (!timer.IsEnabled)
        {
            timer.Start();
        }

        TriggerSample();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= OnTimerTick;
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs) => TriggerSample();

    private void TriggerSample()
    {
        if (disposed || Interlocked.CompareExchange(ref sampling, 1, 0) != 0)
        {
            return;
        }

        var directory = recordingDirectoryProvider();
        _ = Task.Run(() => RecordingStorageTelemetry.Sample(directory))
            .ContinueWith(task =>
            {
                Interlocked.Exchange(ref sampling, 0);
                if (disposed || !task.IsCompletedSuccessfully)
                {
                    return;
                }

                var snapshot = task.Result;
                _ = dispatcher.InvokeAsync(() =>
                {
                    if (disposed || !HasActiveRecordingMetrics)
                    {
                        return;
                    }

                    RecordedFileSizeText = RecordingStorageTelemetry.FormatBytes(snapshot.RecordedBytes);
                    AvailableDiskSpaceText = snapshot.AvailableFreeBytes is not null
                        ? $"剩余 {RecordingStorageTelemetry.FormatBytes(snapshot.AvailableFreeBytes)}"
                        : "磁盘不可用";
                    IsLowDiskSpace = snapshot.IsLowDiskSpace;
                });
            }, TaskScheduler.Default);
    }
}
