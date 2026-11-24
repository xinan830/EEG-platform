using System.ComponentModel;

namespace BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

public sealed record LiveDisplayPreferences(
    double HighPassHz,
    double LowPassHz,
    double NotchHz,
    double PaperSpeedMillimetersPerSecond,
    HorizontalTimeScaleMode HorizontalTimeScaleMode = HorizontalTimeScaleMode.PaperSpeed,
    double TimebaseSecondsPerScreen = 10);

/// <summary>Owns local waveform-display preferences; raw acquisition is never changed.</summary>
public sealed class LiveDisplayPreferencesViewModel : ObservableObject, IDisposable
{
    private readonly ConfiguredAcquisitionRuntime runtime;
    private readonly LiveMonitoringViewModel monitor;
    private readonly Func<LiveDisplayPreferences, Task> persistAsync;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private double highPassHz;
    private double lowPassHz;
    private double notchHz;
    private long revision;
    private long filterApplyRevision;
    private bool disposed;

    public LiveDisplayPreferencesViewModel(
        ConfiguredAcquisitionRuntime runtime,
        LiveMonitoringViewModel monitor,
        LiveDisplayPreferences initialPreferences,
        Func<LiveDisplayPreferences, Task> persistAsync)
    {
        this.runtime = runtime;
        this.monitor = monitor;
        this.persistAsync = persistAsync;
        highPassHz = initialPreferences.HighPassHz;
        lowPassHz = initialPreferences.LowPassHz;
        notchHz = initialPreferences.NotchHz;
        monitor.PaperSpeedMillimetersPerSecond = initialPreferences.PaperSpeedMillimetersPerSecond;
        monitor.HorizontalTimeScaleMode = initialPreferences.HorizontalTimeScaleMode;
        monitor.TimebaseSecondsPerScreen = initialPreferences.TimebaseSecondsPerScreen;
        monitor.PropertyChanged += OnMonitorPropertyChanged;
    }

    public event EventHandler<string>? MessageRaised;

    public double LiveHighPassHz
    {
        get => highPassHz;
        set
        {
            if (SetProperty(ref highPassHz, value))
            {
                ApplyFilter();
                SavePreferences();
            }
        }
    }

    public double LiveLowPassHz
    {
        get => lowPassHz;
        set
        {
            if (SetProperty(ref lowPassHz, value))
            {
                ApplyFilter();
                SavePreferences();
            }
        }
    }

    /// <summary>Zero means no mains-frequency notch for display.</summary>
    public double LiveNotchHz
    {
        get => notchHz;
        set
        {
            if (SetProperty(ref notchHz, value))
            {
                ApplyFilter();
                SavePreferences();
            }
        }
    }

    public bool CanConfigureFilters => runtime.State.State is not AcquisitionState.Starting and not AcquisitionState.Stopping;

    public LiveDisplayFilterSettings FilterSettings => new(
        LiveHighPassHz,
        LiveLowPassHz,
        LiveNotchHz <= 0 ? null : LiveNotchHz);

    public LiveDisplayPreferences Snapshot => new(
        LiveHighPassHz,
        LiveLowPassHz,
        LiveNotchHz,
        monitor.PaperSpeedMillimetersPerSecond,
        monitor.HorizontalTimeScaleMode,
        monitor.TimebaseSecondsPerScreen);

    public void RefreshAvailability() => RaisePropertyChanged(nameof(CanConfigureFilters));

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        monitor.PropertyChanged -= OnMonitorPropertyChanged;
        saveGate.Dispose();
    }

    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(LiveMonitoringViewModel.PaperSpeedMillimetersPerSecond)
            or nameof(LiveMonitoringViewModel.HorizontalTimeScaleMode)
            or nameof(LiveMonitoringViewModel.TimebaseSecondsPerScreen))
        {
            SavePreferences();
        }
    }

    private void ApplyFilter() =>
        _ = ApplyLatestFilterAsync(Interlocked.Increment(ref filterApplyRevision), FilterSettings);

    private async Task ApplyLatestFilterAsync(
        long expectedRevision,
        LiveDisplayFilterSettings settings)
    {
        try
        {
            // A selection can raise several property notifications in quick
            // succession. Build the large raw warm-up snapshot away from the
            // dispatcher and apply only the latest user choice.
            await Task.Delay(150);
            if (disposed || expectedRevision != Volatile.Read(ref filterApplyRevision))
            {
                return;
            }

            var update = await Task.Run(() => runtime.ConfigureDisplayFilter(settings));
            if (disposed || expectedRevision != Volatile.Read(ref filterApplyRevision))
            {
                return;
            }

            if (update.AppliedDuringRecording)
            {
                MessageRaised?.Invoke(this,
                    "正在后台准备新的实时显示滤波；"
                    + "准备期间旧滤波继续显示，追上实时流后无缝切换。"
                    + "切换点之前的波形和原始记录均不改写。");
            }
        }
        catch (Exception exception)
        {
            MessageRaised?.Invoke(this, exception.Message);
        }
    }

    private void SavePreferences() => _ = PersistLatestAsync(Interlocked.Increment(ref revision));

    private async Task PersistLatestAsync(long expectedRevision)
    {
        var entered = false;
        try
        {
            await saveGate.WaitAsync();
            entered = true;
            if (disposed || expectedRevision != Volatile.Read(ref revision))
            {
                return;
            }

            await persistAsync(Snapshot);
        }
        catch
        {
            // A display-preference write must never interrupt device capture.
        }
        finally
        {
            if (entered)
            {
                saveGate.Release();
            }
        }
    }
}
