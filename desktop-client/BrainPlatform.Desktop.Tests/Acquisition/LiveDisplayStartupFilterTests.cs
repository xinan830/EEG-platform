using System.Reflection;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Acquisition.Analysis;
using BrainPlatform.Desktop.Modules.Acquisition.ViewModels;

namespace BrainPlatform.Desktop.Tests.Acquisition;

public sealed class LiveDisplayStartupFilterTests
{
    [Fact]
    public async Task StartupWaitsForOlderWorkerBeforeApplyingItsFilterSnapshot()
    {
        using var client = new HttpClient();
        await using var runtime = new ConfiguredAcquisitionRuntime(backendHttpClient: client);
        using var monitor = new LiveMonitoringViewModel(() => runtime.State, () => null, () => [], Dispatcher.CurrentDispatcher);
        var preparing = false;
        using var preferences = new LiveDisplayPreferencesViewModel(runtime, monitor,
            new LiveDisplayPreferences(1, 70, 0, 30), _ => Task.CompletedTask, () => preparing);
        var earlierWorker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Hold an already-running older update; a later update must not lose ownership of it.
        typeof(LiveDisplayPreferencesViewModel).GetField("pendingFilterUpdate",
            BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(preferences, earlierWorker.Task);
        preferences.LiveLowPassHz = 80;
        preparing = true;
        var drain = preferences.CompletePendingFilterUpdatesAsync();
        await Task.Delay(200);
        Assert.False(drain.IsCompleted);
        runtime.ConfigureDisplayFilter(new LiveDisplayFilterSettings(1, 45, null));
        earlierWorker.SetResult();
        await drain.WaitAsync(TimeSpan.FromSeconds(5));

        var bridge = typeof(ConfiguredAcquisitionRuntime).GetField("liveFilterBridge",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)!;
        var settings = (LiveDisplayFilterSettings)bridge.GetType().GetField("activeSettings",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bridge)!;
        Assert.Equal(45, settings.HighCutHz);
    }
}
