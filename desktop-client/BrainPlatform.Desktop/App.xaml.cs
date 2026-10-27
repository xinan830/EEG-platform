using System.Net.Http;
using System.Windows;
using BrainPlatform.Desktop.Infrastructure.Backend;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using SciChart.Charting.Visuals;

namespace BrainPlatform.Desktop;

public partial class App : Application
{
    private BackendProcessManager? _backendProcess;
    private DesktopWorkspaceViewModel? workspace;
    private Modules.Acquisition.Session.DeviceSessionAvailabilityMonitor? deviceAvailabilityMonitor;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DesktopLog.Info("desktop_started");

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        SciChartSurface.SetRuntimeLicenseKey("2RQWHmeDhvpKpDsa8pVN3szv5jTAI4KMQo+47DAGc79L9k/hVPHCmcyROfzXz7gCEAxHF219c3lxXov+FRG75oinudVQX+UDzuPREcO+SpV6etBpDLGMkqSrE/m4r0mddSbU8C7HbOF9R7oawIr/aNNel6r7vJMm52rajnZLyJgnOeWftKj8p6q9FCxK7or5KJ0sAEqmiL4HFrUIMqUpVehKgIKHRzIjVP1WWnIaHnv6cFHb/YVyrMg6rNPfxU+ec8dv6ssnYOnqWC+0qI5wuIhr48mop7j8HzZl4n/8iYYPvXsDfCoFi39/MiOfLmqF1360MvkYlcOJit9zIqZKjn9pdGHyel0eJI5ruPimNir83IR6O7MwB+fdo5D16pEh/S8I8R6q3JlQHsUxfIW/SA98x0Yjhp9wBBIBifDV3m+xFyb65ij/Sr+/EQLfJn53928780Qr9q4q6mBNULqnW0LodnX3tMg6D224ogPbPfJWgjw4B7gmBLKcHj5GV6tJQIl2k3qkmg2pGwMbI6WQ");

        // ── 第一步：启动打包后端（开发模式下后端不存在则跳过）──
        _backendProcess = new BackendProcessManager();
        var started = await _backendProcess.StartAndWaitAsync();

        if (!started)
        {
            // 打包后后端在 20s 内仍未就绪，提示用户
            var exePath = System.IO.Path.Combine(
                AppContext.BaseDirectory, @"backend\brain_backend.exe");
            if (System.IO.File.Exists(exePath))
            {
                MessageBox.Show(
                    "科学引擎启动超时（20 秒），请检查 backend\\brain_backend.exe 是否可以运行。",
                    "脑科研平台",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            // 开发模式（无打包后端）：正常继续，假设已手动启动
        }

        // ── 第二步：构建客户端 ──
        var backendEndpoint = new Uri("http://127.0.0.1:8000/");
        var healthClient = new BackendHealthClient(new HttpClient(new DiagnosticHttpMessageHandler(new HttpClientHandler()))
        {
            BaseAddress = backendEndpoint,
            Timeout = TimeSpan.FromSeconds(3),
        });

        var runtime = new Modules.Acquisition.Runtime.ConfiguredAcquisitionRuntime(new HttpClient(new DiagnosticHttpMessageHandler(new HttpClientHandler()))
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
        var electrodeLocationClient = new Modules.Channels.Api.HttpElectrodeLocationClient(new HttpClient
        {
            BaseAddress = backendEndpoint,
            Timeout = TimeSpan.FromSeconds(5),
        });
        var montageStore = new MontageProfileStore();
        workspace = new DesktopWorkspaceViewModel(
            healthClient,
            new AcquisitionWorkspaceViewModel(runtime: runtime, deviceSession: deviceSession, notifications: notifications, eventDefinitionService: eventDefinitionService, recordingRegistrationClient: algorithmClient, montageStore: montageStore),
            notifications,
            eventDefinitionService,
            algorithmClient,
            montageStore: montageStore,
            electrodeLocationClient: electrodeLocationClient);
        var reviewFilterClient = new HttpClient(new DiagnosticHttpMessageHandler(new HttpClientHandler()))
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
            // 关闭后端进程（打包模式下）
            _backendProcess?.Dispose();
            DesktopLog.Info("desktop_stopped");
            base.OnExit(e);
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        DesktopLog.Error("desktop_dispatcher_unhandled_exception", e.Exception);
        MessageBox.Show(
            e.Exception.ToString(),
            "脑科研平台桌面客户端启动错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
