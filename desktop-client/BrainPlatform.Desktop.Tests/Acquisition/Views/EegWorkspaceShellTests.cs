using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Acquisition.Views;

namespace BrainPlatform.Desktop.Tests.Views;

public sealed class EegWorkspaceShellTests
{
    [Fact]
    public void WaveformSlotReceivesItsExplicitDataContextAndVisibleWorkspace()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var waveformContext = new object();
                var waveform = new Border();
                var shell = new EegWorkspaceShell
                {
                    TopBar = new Border { Height = 52 },
                    Waveform = waveform,
                    WaveformDataContext = waveformContext,
                    BottomBar = new Border { Height = 60 },
                };
                shell.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(
                        "/BrainPlatform.Desktop;component/Shared/Styles/WorkspaceStyles.xaml",
                        UriKind.RelativeOrAbsolute),
                });

                shell.Measure(new Size(1_200, 800));
                shell.Arrange(new Rect(0, 0, 1_200, 800));
                shell.UpdateLayout();

                Assert.Same(waveformContext, waveform.DataContext);
                Assert.True(waveform.ActualWidth > 1_000, $"Waveform width was {waveform.ActualWidth}.");
                Assert.True(waveform.ActualHeight > 500, $"Waveform height was {waveform.ActualHeight}.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
