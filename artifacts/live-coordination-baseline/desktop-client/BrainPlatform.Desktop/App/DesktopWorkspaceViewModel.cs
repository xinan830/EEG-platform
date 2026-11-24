using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using BrainPlatform.Desktop.Infrastructure.Backend;
using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Modules.Recordings.Access;
using System.Net.Http;
using BrainPlatform.Desktop.Infrastructure.Diagnostics;
using BrainPlatform.Desktop.Modules.Settings.Api;
using BrainPlatform.Desktop.Modules.Settings.Services;
using BrainPlatform.Desktop.Modules.Settings.ViewModels;

namespace BrainPlatform.Desktop.Application;

public sealed class DesktopWorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IBackendHealthClient backendHealthClient;
    private readonly HttpClient? maintenanceTransport;
    private BackendConnectionState backendState = BackendConnectionState.Checking(DateTimeOffset.Now);

    public DesktopWorkspaceViewModel(
        IBackendHealthClient backendHealthClient,
        AcquisitionWorkspaceViewModel acquisition,
        OperationNotificationCenter? notifications = null,
        EventDefinitionService? eventDefinitionService = null,
        IAlgorithmClient? algorithmClient = null,
        ResearchProjectStore? projectStore = null,
        ChannelConfigurationProfileStore? channelConfigurationStore = null,
        MontageProfileStore? montageStore = null,
        IElectrodeLocationClient? electrodeLocationClient = null,
        RecordingSessionManager? recordingSessionManager = null,
        ICacheMaintenanceClient? cacheMaintenanceClient = null)
    {
        this.backendHealthClient = backendHealthClient;
        Notifications = notifications ?? new OperationNotificationCenter();
        Acquisition = acquisition;
        ElectrodeLocationClient = electrodeLocationClient;
        montageStore ??= new MontageProfileStore(projectStore: projectStore);
        Algorithms = algorithmClient ?? throw new ArgumentNullException(nameof(algorithmClient));
        RecordingSessions = recordingSessionManager ?? new RecordingSessionManager();
        if (cacheMaintenanceClient is null)
        {
            maintenanceTransport = new HttpClient(new DiagnosticHttpMessageHandler(new HttpClientHandler()))
            {
                BaseAddress = new Uri(backendHealthClient.Endpoint.GetLeftPart(UriPartial.Authority) + "/"),
                Timeout = TimeSpan.FromMinutes(5),
            };
            cacheMaintenanceClient = new HttpCacheMaintenanceClient(maintenanceTransport);
        }
        var cacheMaintenance = new CacheMaintenanceService(cacheMaintenanceClient, new ReviewCacheCleaner(), RecordingSessions);
        CacheMaintenance = new CacheMaintenanceViewModel(() => cacheMaintenance.ClearAsync());
        Projects = new ProjectWorkspaceViewModel(Notifications, store: projectStore);
        AlgorithmCatalog = new AlgorithmListViewModel(Algorithms, Notifications, Projects, TimeDisplaySettings, montageStore,
            recordingSessionManager: RecordingSessions);
        AlgorithmWorkspace = new AlgorithmWorkspaceViewModel(AlgorithmCatalog);
        Overview = new DeviceOverviewViewModel(acquisition.DeviceSession, acquisition);
        ChannelConfigurations = new ChannelConfigurationWorkspaceViewModel(
            acquisition.ChannelMapping,
            store: channelConfigurationStore,
            deviceSession: acquisition.DeviceSession,
            montageStore: montageStore,
            notifications: Notifications,
            electrodeLocationClient: electrodeLocationClient);
        MontageConfigurations = new MontageConfigurationWorkspaceViewModel(
            ChannelConfigurations,
            store: montageStore,
            notifications: Notifications);
        EventDefinitions = new EventDefinitionWorkspaceViewModel(
            eventDefinitionService ?? acquisition.EventDefinitionService,
            Notifications,
            IsEventDefinitionReferencedAsync,
            cancellationToken => Task.Run(
                () => LoadReferencedEventDefinitionIdsAsync(cancellationToken), cancellationToken));
        ScreenCalibration = new ScreenCalibrationViewModel(notifications: Notifications);
        RefreshBackendCommand = new AsyncRelayCommand(RefreshBackendAsync, ReportCommandError);
        DismissNotificationCommand = new AsyncRelayCommand(() =>
        {
            Notifications.Dismiss();
            return Task.CompletedTask;
        }, ReportCommandError);
    }

    public AcquisitionWorkspaceViewModel Acquisition { get; }
    public CacheMaintenanceViewModel CacheMaintenance { get; }

    public RecordingTimeDisplaySettings TimeDisplaySettings { get; } = new();

    public IAlgorithmClient Algorithms { get; }

    public IElectrodeLocationClient? ElectrodeLocationClient { get; }

    /// <summary>唯一的工作区录音访问所有者；Review 与算法工作台共享其 lease。</summary>
    public RecordingSessionManager RecordingSessions { get; }

    public AlgorithmListViewModel AlgorithmCatalog { get; }

    public AlgorithmWorkspaceViewModel AlgorithmWorkspace { get; }

    public ProjectWorkspaceViewModel Projects { get; }

    public OperationNotificationCenter Notifications { get; }

    public ICommand DismissNotificationCommand { get; }

    public DeviceOverviewViewModel Overview { get; }

    public ChannelConfigurationWorkspaceViewModel ChannelConfigurations { get; }

    public MontageConfigurationWorkspaceViewModel MontageConfigurations { get; }

    public EventDefinitionWorkspaceViewModel EventDefinitions { get; }

    public ScreenCalibrationViewModel ScreenCalibration { get; }

    public BackendConnectionState BackendState
    {
        get => backendState;
        private set
        {
            if (SetProperty(ref backendState, value))
            {
                RaisePropertyChanged(nameof(BackendIndicatorBrush));
            }
        }
    }

    public ICommand RefreshBackendCommand { get; }

    public string BackendEndpoint => backendHealthClient.Endpoint.AbsoluteUri;

    public Brush BackendIndicatorBrush => BackendState.IsAvailable
        ? new SolidColorBrush(Color.FromRgb(31, 136, 96))
        : new SolidColorBrush(Color.FromRgb(196, 69, 54));

    public async Task RefreshBackendAsync()
    {
        BackendState = BackendConnectionState.Checking(DateTimeOffset.Now);
        BackendState = await backendHealthClient.CheckAsync(CancellationToken.None);
    }

    private void ReportCommandError(Exception exception)
    {
        BackendState = BackendConnectionState.Unavailable(exception.Message, DateTimeOffset.Now);
        Notifications.PublishError(exception.Message);
    }

    private static async Task<bool> IsEventDefinitionReferencedAsync(string definitionId, CancellationToken cancellationToken)
    {
        var projects = await new ResearchProjectStore().LoadAsync(cancellationToken);
        foreach (var project in projects)
        {
            foreach (var recordingDirectory in EnumerateEventRecordingDirectories(project))
            {
                var events = await new RecordingEventStore(recordingDirectory).LoadAsync(cancellationToken);
                if (events.Any(item => item.DefinitionId == definitionId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Task<IReadOnlySet<string>> LoadReferencedEventDefinitionIdsAsync(CancellationToken cancellationToken) =>
        LoadReferencedEventDefinitionIdsAsync(new ResearchProjectStore(), cancellationToken);

    internal static async Task<IReadOnlySet<string>> LoadReferencedEventDefinitionIdsAsync(
        ResearchProjectStore projectStore, CancellationToken cancellationToken)
    {
        var referencedIds = new HashSet<string>(StringComparer.Ordinal);
        var projects = await projectStore.LoadAsync(cancellationToken);
        foreach (var project in projects)
        {
            foreach (var recordingDirectory in EnumerateEventRecordingDirectories(project))
            {
                var events = await new RecordingEventStore(recordingDirectory).LoadAsync(cancellationToken);
                foreach (var recordingEvent in events) referencedIds.Add(recordingEvent.DefinitionId);
            }
        }

        return referencedIds;
    }

    private static IEnumerable<string> EnumerateEventRecordingDirectories(ResearchProject project)
    {
        foreach (var root in new[]
                 {
                     project.RecordingsDirectory,
                     Path.Combine(project.DirectoryPath, ".trash", "recordings"),
                 })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var directory in Directory.EnumerateDirectories(root)) yield return directory;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await AlgorithmCatalog.DisposeAsync();
        await RecordingSessions.DisposeAsync();
        maintenanceTransport?.Dispose();
        Overview.Dispose();
        await Acquisition.DisposeAsync();
    }
}
