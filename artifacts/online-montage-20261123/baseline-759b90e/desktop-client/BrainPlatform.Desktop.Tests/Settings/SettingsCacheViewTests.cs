using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BrainPlatform.Desktop.Modules.Settings.Services;
using BrainPlatform.Desktop.Modules.Settings.ViewModels;
using BrainPlatform.Desktop.Modules.Settings.Views;

namespace BrainPlatform.Desktop.Tests.Settings;

public sealed class SettingsCacheViewTests
{
    [Fact]
    public async Task SettingsBindsActionBusyStateAndCompletionText()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                var pending = new TaskCompletionSource<CacheClearResult>();
                var model = new CacheMaintenanceViewModel(() => pending.Task);
                var view = new SettingsView { DataContext = new { CacheMaintenance = model }, Width = 1100, Height = 760 };
                view.Measure(new Size(1100, 760));
                view.Arrange(new Rect(0, 0, 1100, 760));
                view.UpdateLayout();
                Pump();
                var button = Descendants<Button>(view).Single(item => Equals(item.Content, "清除缓存"));
                Assert.Same(model.ClearCommand, button.Command);
                Assert.True(button.IsEnabled);
                button.Command.Execute(null);
                Pump();
                Assert.False(button.IsEnabled);
                Assert.Equal("正在清理…", button.Content);
                pending.SetResult(new CacheClearResult(2, 4, 1024 * 1024));
                Pump();
                Assert.True(button.IsEnabled);
                Assert.Equal("清除缓存", button.Content);
                Assert.Contains(Descendants<TextBlock>(view), item => item.Text.Contains("已清除 2 个动态窗口"));
                var preview = Environment.GetEnvironmentVariable("BRAIN_SETTINGS_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(preview))
                {
                    view.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(1100, 760, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(preview);
                    png.Save(file);
                }
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
