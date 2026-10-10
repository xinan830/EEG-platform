using System.Windows;
using System.Windows.Threading;

namespace BrainPlatform.Desktop.Tests.Shared;

[CollectionDefinition("WPF application resources", DisableParallelization = true)]
public sealed class WpfApplicationCollection : ICollectionFixture<WpfApplicationFixture>;

/// <summary>One STA application/resource owner for editor integration tests.</summary>
public sealed class WpfApplicationFixture : IDisposable
{
    private readonly Thread thread;
    private readonly Application application;

    public WpfApplicationFixture()
    {
        var ready = new TaskCompletionSource<Application>(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/BrainPlatform.Desktop;component/Shared/Styles/ControlStyles.xaml",
                        UriKind.RelativeOrAbsolute),
                });
                ready.SetResult(app);
                Dispatcher.Run();
            }
            catch (Exception error) { ready.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        application = ready.Task.GetAwaiter().GetResult();
    }

    public void Run(Action action) => application.Dispatcher.Invoke(action);

    public void Dispose()
    {
        application.Dispatcher.Invoke(() =>
        {
            application.Shutdown();
            Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        });
        thread.Join();
    }
}
