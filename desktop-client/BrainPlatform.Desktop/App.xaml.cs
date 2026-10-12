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
        SciChartSurface.SetRuntimeLicenseKey("7iF0IO0u4YmElZ5QNwdqPsiu+0CxVVDJ+A1X/bvIMPf6kI3qYb+a+o+6YaEnIfCPZkWuSAw3xMdQkm7oe8Tczb1mdOlyuTXW6bCVl+KtayjQnwyGl1Mts2tPlKk9CF0eVFsYF42hUGlSRvy3vplCDTEQsUxyHkpq8bVns2PdH8GzoTqZwCrhVpgVceNa3+mmxeRKEqOBGmfR0hHi0ynOAfqAiBUn9rb6dACEEXpiNT6JLyc8Vpx1eBDOYakPa3Gra4fJ2Wh34PjLcxDjfXhFM5CaO3wLMBgpH9MFjcXC3Ko330GpNOBcUhd7tWWHPg0yX+5AlA1SqNhuopnnUMmdVzZZZlTzudvtx+f6pVgDJlnY2gQhTDzRwaH3jkSnvERuYVKdTqoi0eraFEn7dNYGfjgC8q+dR9DjB12V1fP8l6Quq9h5AdD9i9t8Nyf+1DXnmohZke0vNOjSNUGAndnSlJVKAbeFTSNJcC4DADNzx02JSGKy08X7Gtl7eZuBYhTeG+O8JVF+8ZTTLm3EDluHDgPXStCRNvmidEFUjfIwUXKuJ1O2byl9aiw=");

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
        workspace = new DesktopWorkspaceViewModel(
            healthClient,
            new AcquisitionWorkspaceViewModel(runtime: runtime, deviceSession: deviceSession, notifications: notifications, eventDefinitionService: eventDefinitionService, recordingRegistrationClient: algorithmClient),
            notifications,
            eventDefinitionService,
            algorithmClient);
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
