using System.ComponentModel;
using BrainPlatform.Desktop.Acquisition.Analysis;
using BrainPlatform.Desktop.Acquisition.Contracts;
using BrainPlatform.Desktop.Acquisition.Runtime;

namespace BrainPlatform.Desktop.ViewModels;

public sealed record LiveDisplayPreferences(
    double HighPassHz,
    double LowPassHz,
    double NotchHz,
    double PaperSpeedMillimetersPerSecond);

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
        monitor.PaperSpeedMillimetersPerSecond);

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
        if (eventArgs.PropertyName == nameof(LiveMonitoringViewModel.PaperSpeedMillimetersPerSecond))
        {
            SavePreferences();
        }
    }

    private void ApplyFilter()
    {
        try
        {
            var update = runtime.ConfigureDisplayFilter(FilterSettings);
            if (update.AppliedDuringRecording)
            {
                MessageRaised?.Invoke(this,
                    $"实时显示滤波将在样本 {update.EffectiveFromRawSampleCounter} 后生效；"
                    + $"已用此前 {update.WarmupSampleCount} 个连续原始样本预热新配置。"
                    + "A 点之前的显示波形不变，原始记录未改写。");
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
