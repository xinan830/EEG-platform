using System.Windows;
using System.Windows.Threading;
using System.Runtime.CompilerServices;

namespace BrainPlatform.Desktop.Tests.Shared;

[CollectionDefinition("WPF application resources", DisableParallelization = true)]
public sealed class WpfApplicationCollection : ICollectionFixture<WpfApplicationFixture>;

/// <summary>Adapter to the process-owned WPF host; a collection cannot shut it down.</summary>
public sealed class WpfApplicationFixture
{
    public void Run(Action action) => WpfTestApplication.Run(action);
}

internal static class WpfTestApplication
{
    private static Application application = null!;

    // Application/pack resources are process-wide, including tests outside this collection.
    // Keep the background dispatcher alive until the test host exits.
    [ModuleInitializer]
    internal static void Initialize()
    {
        var ready = new TaskCompletionSource<Application>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
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

    internal static void Run(Action action) => application.Dispatcher.Invoke(action);
}
