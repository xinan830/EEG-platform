using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Algorithms.Views.Shared;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Algorithms;

[Collection("WPF application resources")]
public sealed class TnkPlotRefreshSchedulerTests(WpfApplicationFixture fixture)
{
    [Fact]
    public void Coalesces_callbacks_and_deduplicates_the_same_chart_refresh()
        => fixture.Run(() =>
        {
            var scheduler = new TnkPlotRefreshScheduler(Dispatcher.CurrentDispatcher);
            var posted = 0;
            void OnOperationPosted(object? sender, DispatcherHookEventArgs args) => posted++;
            Dispatcher.CurrentDispatcher.Hooks.OperationPosted += OnOperationPosted;
            var callbacks = new List<int>();
            Action first = () => callbacks.Add(1);
            Action second = () => callbacks.Add(2);
            try
            {
                scheduler.Request(first);
                scheduler.Request(second);
                scheduler.Request(first);

                Assert.Equal(1, posted);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Assert.Equal([1, 2], callbacks);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.Hooks.OperationPosted -= OnOperationPosted;
            }
        });
}
