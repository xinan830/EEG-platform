using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using BrainPlatform.Desktop.Acquisition.Storage;
using BrainPlatform.Desktop.Review;
using BrainPlatform.Desktop.ViewModels;
using BrainPlatform.Desktop.Views;

namespace BrainPlatform.Desktop;

public partial class MainWindow : Window
{
    private AcquisitionPreparationView? acquisitionPreparationView;
    private DeviceOverviewView? overviewView;
    private ProjectListView? projectListView;
    private ProjectDetailView? projectDetailView;
    private SettingsView? settingsView;
    private ScreenCalibrationView? screenCalibrationView;
    private ChannelListView? channelListView;
    private ChannelDetailView? channelDetailView;
    private MontageListView? montageListView;
    private MontageDetailView? montageDetailView;
    private EventListView? eventListView;
    private RecordingReviewViewModel? recordingReviewViewModel;
    private LocalRawRecording? recordingReviewRecording;
    private EegSessionWindow? acquisitionSessionWindow;
    private EegSessionWindow? reviewSessionWindow;
    private readonly IRecordingReviewFilter? recordingReviewFilter;

    public MainWindow(IRecordingReviewFilter? recordingReviewFilter = null)
    {
        this.recordingReviewFilter = recordingReviewFilter;
        InitializeComponent();

        // Initialize view instances immediately for instant UI display
        projectListView = new ProjectListView();
        projectDetailView = new ProjectDetailView();
        settingsView = new SettingsView();
        screenCalibrationView = new ScreenCalibrationView();
        channelListView = new ChannelListView();
        channelDetailView = new ChannelDetailView();
        montageListView = new MontageListView();
        montageDetailView = new MontageDetailView();
        eventListView = new EventListView();
        overviewView = new DeviceOverviewView();
        MainContentHost.Content = overviewView;
    }

    public void ShowChannelListView()
    {
        SetImmersiveChrome(false);
        channelListView ??= new ChannelListView();
        MainContentHost.Content = channelListView;
        SelectNavigation(NavSettingsBtn);
    }

    public void ShowChannelDetailView(bool newProfile = false, bool preserveDraft = false)
    {
        SetImmersiveChrome(false);
        channelDetailView ??= new ChannelDetailView();
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            if (newProfile)
            {
                viewModel.ChannelConfigurations.BeginNewProfile();
            }
            else if (!preserveDraft)
            {
                viewModel.ChannelConfigurations.BeginEditSelected();
            }
        }
        MainContentHost.Content = channelDetailView;
        SelectNavigation(NavSettingsBtn);
    }

    public void ShowMontageListView()
    {
        SetImmersiveChrome(false);
        montageListView ??= new MontageListView();
        MainContentHost.Content = montageListView;
        SelectNavigation(NavSettingsBtn);
    }

    public void ShowMontageDetailView(bool newProfile = false, bool preserveDraft = false)
    {
        SetImmersiveChrome(false);
        montageDetailView ??= new MontageDetailView();
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            if (newProfile)
            {
                viewModel.MontageConfigurations.BeginNewProfile();
            }
            else if (!preserveDraft)
            {
                viewModel.MontageConfigurations.BeginEditSelected();
            }
        }
        MainContentHost.Content = montageDetailView;
        SelectNavigation(NavSettingsBtn);
    }

    public void ShowSettingsView()
    {
        SetImmersiveChrome(false);
        settingsView ??= new SettingsView();
        MainContentHost.Content = settingsView;
        SelectNavigation(NavSettingsBtn);
    }

    public void ShowScreenCalibrationView()
    {
        SetImmersiveChrome(false);
        screenCalibrationView ??= new ScreenCalibrationView();
        MainContentHost.Content = screenCalibrationView;
        SelectNavigation(NavSettingsBtn);
    }

    public async void ShowEventListView()
    {
        SetImmersiveChrome(false);
        eventListView ??= new EventListView();
        if (DataContext is DesktopWorkspaceViewModel workspace)
        {
            await workspace.EventDefinitions.RefreshAsync();
        }
        MainContentHost.Content = eventListView;
        SelectNavigation(NavSettingsBtn);
    }

    public void ShowAcquisitionView()
    {
        if (acquisitionSessionWindow is { IsLoaded: true })
        {
            acquisitionSessionWindow.Activate();
            return;
        }

        if (DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }

        var view = new AcquisitionWorkspaceView { DataContext = workspace };
        acquisitionSessionWindow = new EegSessionWindow(
            this,
            "实时采集",
            view,
            SessionCloseRequest.Acquisition,
            async () =>
            {
                if (workspace.Acquisition.CanStop)
                {
                    await workspace.Acquisition.FinishAcquisitionAsync();
                }
            },
            () =>
            {
                acquisitionSessionWindow = null;
                workspace.Projects.RefreshRecordings();
                ShowProjectDetailView();
            });
        acquisitionSessionWindow.Show();
    }

    public async void ShowAcquisitionPreparationView()
    {
        SetImmersiveChrome(false);
        acquisitionPreparationView ??= new AcquisitionPreparationView();
        MainContentHost.Content = acquisitionPreparationView;
        SelectNavigation(NavProjectsBtn);

        if (DataContext is not DesktopWorkspaceViewModel workspace)
        {
            return;
        }

        try
        {
            // Re-evaluate the device-compatible montage list at the boundary
            // where an operator is about to open a stream. No configuration is
            // silently selected or mutated here.
            await workspace.ChannelConfigurations.RefreshAsync();
            await workspace.MontageConfigurations.RefreshAsync();

            // A device change or a channel/montage edit can make the previous
            // preparation selection unavailable. Never retain an invisible,
            // stale montage as though it were still valid for this session.
            if (workspace.Acquisition.SelectedMontageProfile is { } selectedMontage &&
                !workspace.MontageConfigurations.AvailableForAcquisition.Any(profile =>
                    string.Equals(profile.Id, selectedMontage.Id, StringComparison.Ordinal) &&
                    string.Equals(profile.Fingerprint, selectedMontage.Fingerprint, StringComparison.Ordinal)))
            {
                workspace.Acquisition.SelectedMontageProfile = null;
                workspace.Notifications.PublishWarning("先前选择的导联配置已不再兼容当前设备或通道配置，请重新选择。");
            }
        }
        catch (Exception exception)
        {
            workspace.Notifications.PublishError($"无法刷新采集准备配置：{exception.Message}");
        }
    }

    public void ShowProjectListView()
    {
        SetImmersiveChrome(false);
        projectListView ??= new ProjectListView();
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.Projects.RefreshRecordings();
        }
        MainContentHost.Content = projectListView;
        SelectNavigation(NavProjectsBtn);
    }

    public void ShowProjectDetailView()
    {
        SetImmersiveChrome(false);
        projectDetailView ??= new ProjectDetailView();
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            if (viewModel.Projects.SelectedProject is null)
            {
                ShowProjectListView();
                return;
            }

            viewModel.Projects.RefreshRecordings();
        }

        MainContentHost.Content = projectDetailView;
        SelectNavigation(NavProjectsBtn);
    }

    public async Task ShowRecordingReviewViewAsync(
        ProjectRecordingRow recording,
        IEnumerable<MontageProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(recording);
        if (reviewSessionWindow is { IsLoaded: true })
        {
            reviewSessionWindow.Activate();
            if (DataContext is DesktopWorkspaceViewModel activeWorkspace)
            {
                activeWorkspace.Notifications.PublishWarning("已有一个数据回溯窗口，请先关闭后再打开另一条记录。");
            }
            return;
        }

        var loadedRecording = await LocalRawRecordingReader.OpenAsync(
            recording.RecordingDirectory,
            CancellationToken.None);
        var catalog = RecordingMontageCatalog.Build(loadedRecording.Manifest, profiles);
        var viewModel = new RecordingReviewViewModel(
            loadedRecording.Reader,
            catalog,
            recordingName: recording.Name,
            projectName: loadedRecording.Manifest.Project.Name,
            filter: recordingReviewFilter,
            recordingDirectory: recording.RecordingDirectory,
            eventDefinitionService: (DataContext as DesktopWorkspaceViewModel)?.Acquisition.EventDefinitionService,
            notifications: (DataContext as DesktopWorkspaceViewModel)?.Notifications);
        try
        {
            await viewModel.InitializeAsync();
        }
        catch
        {
            await viewModel.DisposeAsync();
            await loadedRecording.DisposeAsync();
            throw;
        }

        recordingReviewRecording = loadedRecording;
        recordingReviewViewModel = viewModel;
        var view = new RecordingReviewView { DataContext = viewModel };
        reviewSessionWindow = new EegSessionWindow(
            this,
            "数据回溯",
            view,
            SessionCloseRequest.Review,
            DisposeRecordingReviewAsync,
            () =>
            {
                reviewSessionWindow = null;
                ShowProjectDetailView();
            });
        reviewSessionWindow.Show();
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            await viewModel.RefreshBackendAsync();
            await viewModel.Projects.RefreshAsync();
            await viewModel.ChannelConfigurations.RefreshAsync();
            await viewModel.MontageConfigurations.RefreshAsync();
            await viewModel.EventDefinitions.RefreshAsync();
        }
    }

    private void OnNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel activeWorkspace && activeWorkspace.Acquisition.CanStop)
        {
            ShowAcquisitionView();
            activeWorkspace.Notifications.PublishWarning("采集会话运行期间请留在实时采集页；终止采集后可切换页面。");
            return;
        }

        if (sender == NavOverviewBtn)
        {
            SetImmersiveChrome(false);
            overviewView ??= new DeviceOverviewView();
            MainContentHost.Content = overviewView;
        }
        else if (sender == NavProjectsBtn)
        {
            ShowProjectListView();
        }
        else if (sender == NavSettingsBtn)
        {
            ShowSettingsView();
        }
        else
        {
            ShowUnavailableWorkspace((sender as RadioButton)?.ToolTip?.ToString() ?? "此功能");
        }
    }

    private void SelectNavigation(RadioButton navigationItem)
    {
        navigationItem.IsChecked = true;
    }

    public void ShowUnavailableWorkspace(string title)
    {
        SetImmersiveChrome(false);
        MainContentHost.Content = new WorkspaceUnavailableView(title);
    }

    private void SetImmersiveChrome(bool immersive)
    {
        NavigationColumn.Width = immersive ? new GridLength(0) : new GridLength(72);
        GlobalNavigationHost.Visibility = immersive ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(MainContentHost, immersive ? 0 : 1);
        Grid.SetColumnSpan(MainContentHost, immersive ? 2 : 1);
        Grid.SetColumn(NotificationHost, immersive ? 0 : 1);
        Grid.SetColumnSpan(NotificationHost, immersive ? 2 : 1);
    }

    private async Task DisposeRecordingReviewAsync()
    {
        if (recordingReviewViewModel is { } viewModel)
        {
            recordingReviewViewModel = null;
            await viewModel.DisposeAsync();
        }

        if (recordingReviewRecording is { } loadedRecording)
        {
            recordingReviewRecording = null;
            await loadedRecording.DisposeAsync();
        }
    }
}
