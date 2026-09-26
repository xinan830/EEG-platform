using System.Net.Http;
using System.Windows;
using SciChart.Charting.Visuals;

namespace BrainPlatform.Desktop;

public partial class App : Application
{
    private DesktopWorkspaceViewModel? workspace;
    private Modules.Acquisition.Session.DeviceSessionAvailabilityMonitor? deviceAvailabilityMonitor;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        SciChartSurface.SetRuntimeLicenseKey("7iF0IO0u4YmElZ5QNwdqPsiu+0CxVVDJ+A1X/bvIMPf6kI3qYb+a+o+6YaEnIfCPZkWuSAw3xMdQkm7oe8Tczb1mdOlyuTXW6bCVl+KtayjQnwyGl1Mts2tPlKk9CF0eVFsYF42hUGlSRvy3vplCDTEQsUxyHkpq8bVns2PdH8GzoTqZwCrhVpgVceNa3+mmxeRKEqOBGmfR0hHi0ynOAfqAiBUn9rb6dACEEXpiNT6JLyc8Vpx1eBDOYakPa3Gra4fJ2Wh34PjLcxDjfXhFM5CaO3wLMBgpH9MFjcXC3Ko330GpNOBcUhd7tWWHPg0yX+5AlA1SqNhuopnnUMmdVzZZZlTzudvtx+f6pVgDJlnY2gQhTDzRwaH3jkSnvERuYVKdTqoi0eraFEn7dNYGfjgC8q+dR9DjB12V1fP8l6Quq9h5AdD9i9t8Nyf+1DXnmohZke0vNOjSNUGAndnSlJVKAbeFTSNJcC4DADNzx02JSGKy08X7Gtl7eZuBYhTeG+O8JVF+8ZTTLm3EDluHDgPXStCRNvmidEFUjfIwUXKuJ1O2byl9aiw=");

        var backendEndpoint = new Uri("http://127.0.0.1:8000/");
        var healthClient = new Infrastructure.Backend.BackendHealthClient(new HttpClient
        {
            BaseAddress = backendEndpoint,
            Timeout = TimeSpan.FromSeconds(3),
        });

        var runtime = new Modules.Acquisition.Runtime.ConfiguredAcquisitionRuntime(new HttpClient
        {
            BaseAddress = backendEndpoint,
            Timeout = TimeSpan.FromSeconds(2),
        });
        var deviceSession = new Modules.Acquisition.Session.DeviceSessionManager(runtime);
        deviceAvailabilityMonitor = new Modules.Acquisition.Session.DeviceSessionAvailabilityMonitor(deviceSession, Dispatcher);
        deviceAvailabilityMonitor.Start();
        var notifications = new OperationNotificationCenter();
        var eventDefinitionService = new EventDefinitionService(new EventDefinitionStore());
        var algorithmClient = new HttpAlgorithmClient(new HttpClient
        {
            BaseAddress = backendEndpoint,
            Timeout = TimeSpan.FromSeconds(15),
        });
        workspace = new DesktopWorkspaceViewModel(
            healthClient,
            new AcquisitionWorkspaceViewModel(runtime: runtime, deviceSession: deviceSession, notifications: notifications, eventDefinitionService: eventDefinitionService, recordingRegistrationClient: algorithmClient),
            notifications,
            eventDefinitionService,
            algorithmClient);
        var reviewFilterClient = new HttpClient
        {
            BaseAddress = backendEndpoint,
            Timeout = TimeSpan.FromSeconds(15),
        };
        var window = new MainWindow(new Modules.Review.Filtering.HttpRecordingReviewFilter(reviewFilterClient))
        {
            DataContext = workspace,
        };

        MainWindow = window;
        deviceAvailabilityMonitor.Attach(window);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            deviceAvailabilityMonitor?.Dispose();
            workspace?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            base.OnExit(e);
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.ToString(),
            "脑科研平台桌面客户端启动错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
