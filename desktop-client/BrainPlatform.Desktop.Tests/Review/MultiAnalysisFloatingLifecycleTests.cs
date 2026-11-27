using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using BrainPlatform.Desktop.Shared.Controls.Analysis;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Review;

[Collection("WPF application resources")]
public sealed class MultiAnalysisFloatingLifecycleTests
{
    [Fact]
    public void Floating_panel_returns_after_dock_host_is_reloaded_without_duplicates() =>
        WpfTestApplication.Run(() =>
        {
            var host = new MultiAnalysisDockHost { PanelTemplate = new DataTemplate() };
            var owner = new Window
            {
                Content = host, Width = 900, Height = 600, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
            };
            var slots = new ObservableCollection<object> { new FakeSlot("RBP") };

            try
            {
                owner.Show();
                host.FloatingSlots = slots;
                PumpDispatcher();
                var first = Assert.Single(owner.OwnedWindows.OfType<Window>());

                owner.Content = null;
                PumpDispatcher();
                Assert.Empty(owner.OwnedWindows.OfType<Window>());
                Assert.Single(slots);

                owner.Content = host;
                PumpDispatcher();
                var restored = Assert.Single(owner.OwnedWindows.OfType<Window>());
                Assert.NotSame(first, restored);
                Assert.True(restored.IsVisible);

                host.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.Same(restored, Assert.Single(owner.OwnedWindows.OfType<Window>()));
            }
            finally { owner.Close(); }
        });

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed record FakeSlot(string DisplayName);
}
