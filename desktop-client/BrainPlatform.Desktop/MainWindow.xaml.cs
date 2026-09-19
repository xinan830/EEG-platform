using System.Windows;
using System.Windows.Controls;
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

        overviewView = new DeviceOverviewView();
        MainContentHost.Content = overviewView;
    }

    public void ShowChannelListView()
    {
        SetAcquisitionChrome(false);
        channelListView ??= new ChannelListView();
        MainContentHost.Content = channelListView;
        SelectNavigation(NavSettingsBtn);
    }

    public void ShowChannelDetailView(bool newProfile = false, bool preserveDraft = false)
    {
        SetAcquisitionChrome(false);
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
        SetAcquisitionChrome(false);
        montageListView ??= new MontageListView();
        MainContentHost.Content = montageListView;
        SelectNavigation(NavSettingsBtn);
    }

    public void ShowMontageDetailView(bool newProfile = false, bool preserveDraft = false)
    {
        SetAcquisitionChrome(false);
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
        SetAcquisitionChrome(false);
        settingsView ??= new SettingsView();
        MainContentHost.Content = settingsView;
        SelectNavigation(NavSettingsBtn);
    }

    public void ShowAcquisitionView()
    {
        SetAcquisitionChrome(true);
        acquisitionView ??= new AcquisitionWorkspaceView();
        MainContentHost.Content = acquisitionView;
        SelectNavigation(NavProjectsBtn);
    }

    public void ShowAcquisitionPreparationView()
    {
        SetAcquisitionChrome(false);
        acquisitionPreparationView ??= new AcquisitionPreparationView();
        MainContentHost.Content = acquisitionPreparationView;
        SelectNavigation(NavProjectsBtn);
    }

    public void ShowProjectListView()
    {
        SetAcquisitionChrome(false);
        projectListView ??= new ProjectListView();
        if (DataContext is DesktopWorkspaceViewModel viewModel)
        {
            viewModel.Projects.RefreshRecordings();
        }
        MainContentHost.Content = projectListView;
        SelectNavigation(NavProjectsBtn);
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
            SetAcquisitionChrome(false);
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
        SetAcquisitionChrome(false);
        MainContentHost.Content = new WorkspaceUnavailableView(title);
    }

    private void SetAcquisitionChrome(bool immersive)
    {
        NavigationColumn.Width = immersive ? new GridLength(0) : new GridLength(72);
        GlobalNavigationHost.Visibility = immersive ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(MainContentHost, immersive ? 0 : 1);
        Grid.SetColumnSpan(MainContentHost, immersive ? 2 : 1);
        Grid.SetColumn(NotificationHost, immersive ? 0 : 1);
        Grid.SetColumnSpan(NotificationHost, immersive ? 2 : 1);
    }
}
