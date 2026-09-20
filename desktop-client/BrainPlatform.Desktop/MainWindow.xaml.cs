using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Acquisition.Storage;
using BrainPlatform.Desktop.Configuration;
using BrainPlatform.Desktop.Review;
using BrainPlatform.Desktop.ViewModels;
using BrainPlatform.Desktop.Views;

namespace BrainPlatform.Desktop;

public partial class MainWindow : Window
{
    private AcquisitionWorkspaceView? acquisitionView;
    private AcquisitionPreparationView? acquisitionPreparationView;
    private DeviceOverviewView? overviewView;
    private ProjectListView? projectListView;
    private SettingsView? settingsView;
    private ChannelListView? channelListView;
    private ChannelDetailView? channelDetailView;
    private MontageListView? montageListView;
    private MontageDetailView? montageDetailView;
    private RecordingReviewView? recordingReviewView;
    private RecordingReviewViewModel? recordingReviewViewModel;
    private LocalRawRecording? recordingReviewRecording;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize view instances immediately for instant UI display
        acquisitionView = new AcquisitionWorkspaceView();
        projectListView = new ProjectListView();
        settingsView = new SettingsView();
        channelListView = new ChannelListView();
        channelDetailView = new ChannelDetailView();
        montageListView = new MontageListView();
        montageDetailView = new MontageDetailView();
        recordingReviewView = new RecordingReviewView();

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

    public void ShowAcquisitionView()
    {
        SetImmersiveChrome(true);
        acquisitionView ??= new AcquisitionWorkspaceView();
        MainContentHost.Content = acquisitionView;
        SelectNavigation(NavProjectsBtn);
    }

    public void ShowAcquisitionPreparationView()
    {
        SetImmersiveChrome(false);
        acquisitionPreparationView ??= new AcquisitionPreparationView();
        MainContentHost.Content = acquisitionPreparationView;
        SelectNavigation(NavProjectsBtn);
    }

    public void ShowProjectListView()
    {
        DisposeRecordingReview();
        SetImmersiveChrome(false);
        projectListView ??= new ProjectListView();
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.Projects.RefreshRecordings();
        }
        MainContentHost.Content = projectListView;
        SelectNavigation(NavProjectsBtn);
    }

    public async Task ShowRecordingReviewViewAsync(
        ProjectRecordingRow recording,
        IEnumerable<MontageProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(recording);
        DisposeRecordingReview();
        var loadedRecording = await LocalRawRecordingReader.OpenAsync(
            recording.RecordingDirectory,
            CancellationToken.None);
        var catalog = RecordingMontageCatalog.Build(loadedRecording.Manifest, profiles);
        var viewModel = new RecordingReviewViewModel(
            loadedRecording.Reader,
            catalog,
            recordingName: recording.Name,
            projectName: loadedRecording.Manifest.Project.Name);
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
        recordingReviewView ??= new RecordingReviewView();
        recordingReviewView.DataContext = viewModel;
        SetImmersiveChrome(true);
        MainContentHost.Content = recordingReviewView;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            await viewModel.RefreshBackendAsync();
            await viewModel.Projects.RefreshAsync();
            await viewModel.ChannelConfigurations.RefreshAsync();
            await viewModel.MontageConfigurations.RefreshAsync();
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

    private void DisposeRecordingReview()
    {
        recordingReviewViewModel?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        recordingReviewViewModel = null;
        recordingReviewRecording?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        recordingReviewRecording = null;
        if (recordingReviewView is not null)
        {
            recordingReviewView.DataContext = null;
        }
    }
}
