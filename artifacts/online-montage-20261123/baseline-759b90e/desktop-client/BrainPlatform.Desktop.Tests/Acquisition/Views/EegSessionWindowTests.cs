using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
using BrainPlatform.Desktop.Modules.Acquisition.Views;
using BrainPlatform.Desktop.Shared.Dialogs;
using BrainPlatform.Desktop.Tests.Shared;

namespace BrainPlatform.Desktop.Tests.Views;

[Collection("WPF application resources")]
public sealed class EegSessionWindowTests(WpfApplicationFixture fixture)
{
    [Fact]
    public void Native_frame_keeps_rounding_preference_and_resizing_after_restore()
    {
        fixture.Run(() =>
        {
            var resources = new ResourceDictionary
            {
                Source = new Uri("/BrainPlatform.Desktop;component/Shared/Styles/InstrumentWorkspaceStyles.xaml",
                    UriKind.RelativeOrAbsolute),
            };
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(resources);
            var owner = new Window();
            EegSessionWindow? window = null;
            try
            {
                _ = new WindowInteropHelper(owner).EnsureHandle();
                window = new EegSessionWindow(owner, "数据回溯", new Border(),
                    SessionCloseRequest.Review, () => Task.CompletedTask);
                var chrome = WindowChrome.GetWindowChrome(window);
                Assert.False(window.AllowsTransparency);
                Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
                Assert.Equal(new CornerRadius(8), chrome.CornerRadius);
                Assert.Equal(new Thickness(1), chrome.GlassFrameThickness);
                Assert.Equal(new Thickness(6), chrome.ResizeBorderThickness);

                // Create an invisible HWND, without opening a session on the user's desktop.
                var handle = new WindowInteropHelper(window).EnsureHandle();
                foreach (var state in new[] { WindowState.Normal, WindowState.Maximized, WindowState.Normal })
                {
                    window.WindowState = state;
                    Assert.Equal(state, window.WindowState);
                    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                    {
                        Assert.Equal(0, DwmGetWindowAttribute(handle, 33, out var preference, sizeof(int)));
                        Assert.Equal(2, preference);
                    }
                }
            }
            finally
            {
                window?.CloseCompletedSession();
                owner.Close();
                System.Windows.Application.Current.Resources.MergedDictionaries.Remove(resources);
            }
        });
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
