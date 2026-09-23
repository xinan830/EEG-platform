using System.Windows.Input;
using System.Windows.Media;
using BrainPlatform.Desktop.Domain;
using BrainPlatform.Desktop.Events;
using BrainPlatform.Desktop.Services;

namespace BrainPlatform.Desktop.ViewModels;

public sealed class DesktopWorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IBackendHealthClient backendHealthClient;
    private BackendConnectionState backendState = BackendConnectionState.Checking(DateTimeOffset.Now);

    public DesktopWorkspaceViewModel(
        IBackendHealthClient backendHealthClient,
        AcquisitionWorkspaceViewModel acquisition,
        OperationNotificationCenter? notifications = null)
    {
        this.backendHealthClient = backendHealthClient;
        Notifications = notifications ?? new OperationNotificationCenter();
        Acquisition = acquisition;
        Projects = new ProjectWorkspaceViewModel(Notifications);
        Overview = new DeviceOverviewViewModel(acquisition.DeviceSession, acquisition);
        ChannelConfigurations = new ChannelConfigurationWorkspaceViewModel(
            acquisition.ChannelMapping,
            deviceSession: acquisition.DeviceSession,
            notifications: Notifications);
        MontageConfigurations = new MontageConfigurationWorkspaceViewModel(
            ChannelConfigurations,
            notifications: Notifications);
        EventDefinitions = new EventDefinitionWorkspaceViewModel(
            new EventDefinitionService(new EventDefinitionStore()),
            Notifications);
        ScreenCalibration = new ScreenCalibrationViewModel(notifications: Notifications);
        RefreshBackendCommand = new AsyncRelayCommand(RefreshBackendAsync, ReportCommandError);
        DismissNotificationCommand = new AsyncRelayCommand(() =>
        {
            Notifications.Dismiss();
            return Task.CompletedTask;
        }, ReportCommandError);
    }

    public AcquisitionWorkspaceViewModel Acquisition { get; }

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

    public async ValueTask DisposeAsync()
    {
        Overview.Dispose();
        await Acquisition.DisposeAsync();
    }
}
