using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Shared.Controls.Topomap;

namespace BrainPlatform.Desktop.Modules.Settings.Views;

public partial class TemplateElectrodeAtlasPanel : UserControl
{
    private const int AutoHideLabelsChannelThreshold = 64;

    private CancellationTokenSource? loadCts;
    private bool catalogLoaded;
    private bool updatingSelection;

    public TemplateElectrodeAtlasPanel()
    {
        InitializeComponent();
        IsVisibleChanged += OnPanelVisibleChanged;
    }

    private void OnPanelVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && !catalogLoaded)
        {
            _ = LoadCatalogAsync();
        }
    }

    private async Task LoadCatalogAsync()
    {
        CancelPendingLoad();
        var cts = new CancellationTokenSource();
        loadCts = cts;
        SetError(null);
        MontageSummaryBadge.Text = "读取模板列表中...";
        try
        {
            var catalog = await Topomap3DModelProvider.GetMontageCatalogAsync(cts.Token);
            if (cts.IsCancellationRequested) return;

            updatingSelection = true;
            try
            {
                MontageComboBox.ItemsSource = catalog.Montages;
                var defaultItem = catalog.Montages.FirstOrDefault(item => item.IsDefault)
                    ?? catalog.Montages.FirstOrDefault(item => string.Equals(item.Name, catalog.DefaultMontage, StringComparison.Ordinal))
                    ?? catalog.Montages.FirstOrDefault();
                MontageComboBox.SelectedItem = defaultItem;
            }
            finally
            {
                updatingSelection = false;
            }

            catalogLoaded = true;
            if (MontageComboBox.SelectedItem is ElectrodeMontageSummary selected)
            {
                await LoadMontagePreviewAsync(selected.Name, cts.Token);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SetError($"电极模板目录加载失败：{ex.Message}");
            MontageSummaryBadge.Text = "加载失败";
        }
    }

    private async Task LoadMontagePreviewAsync(string montageName, CancellationToken token)
    {
        SetError(null);
        MontageSummaryBadge.Text = $"加载 {montageName} ...";
        try
        {
            var preview = await Topomap3DModelProvider.GetMontagePreviewAsync(montageName, token);
            if (token.IsCancellationRequested) return;

            var showAllLabels = preview.ChannelCount <= AutoHideLabelsChannelThreshold;
            ShowLabelsCheckBox.IsChecked = showAllLabels;
            ApplyLabelVisibility(showAllLabels);

            Map2D.Sites = preview.Sites;
            Map2D.SelectedChannel = null;
            SelectedSiteSummary.Text = "点击 2D 点位可在 3D 头模中聚焦定位";

            Map3D.SelectedElectrodeLabel = null;
            Map3D.CustomElectrodeLocations = preview.Sites
                .Select(site => new ElectrodeLocation3D(
                    site.Channel,
                    site.CanonicalChannel,
                    site.CoordinateGnm,
                    preview.ProjectionSource,
                    false,
                    site.IsScalpFieldSite))
                .ToArray();

            var sourceText = preview.IsDefault ? "系统默认 94 点精度映射" : "等距方位 2D 投影 + 头皮网格 3D 动态贴合";
            MontageSummaryBadge.Text = $"{preview.ChannelCount} 导";
            ProjectionInfoText.Text = $"投影方式：{sourceText}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            SetError($"模板 {montageName} 投影加载失败：{ex.Message}");
            MontageSummaryBadge.Text = "加载失败";
        }
    }

    private void OnMontageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingSelection || MontageComboBox.SelectedItem is not ElectrodeMontageSummary selected) return;
        CancelPendingLoad();
        var cts = new CancellationTokenSource();
        loadCts = cts;
        _ = LoadMontagePreviewAsync(selected.Name, cts.Token);
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (!catalogLoaded)
        {
            _ = LoadCatalogAsync();
            return;
        }
        if (MontageComboBox.SelectedItem is ElectrodeMontageSummary selected)
        {
            CancelPendingLoad();
            var cts = new CancellationTokenSource();
            loadCts = cts;
            _ = LoadMontagePreviewAsync(selected.Name, cts.Token);
        }
    }

    private void OnShowLabelsChanged(object sender, RoutedEventArgs e)
    {
        ApplyLabelVisibility(ShowLabelsCheckBox.IsChecked == true);
    }

    private void ApplyLabelVisibility(bool showAll)
    {
        if (Map2D is not null) Map2D.ShowLabels = showAll;
        if (Map3D is not null) Map3D.ShowElectrodeLabels = showAll;
    }

    private void OnLayoutModeChanged(object sender, RoutedEventArgs e)
    {
        if (Column2D is null || ColumnGap is null || Column3D is null || Pane2D is null || Pane3D is null) return;

        if (Layout2DRadio.IsChecked == true)
        {
            Pane2D.Visibility = Visibility.Visible;
            Pane3D.Visibility = Visibility.Collapsed;
            Column2D.Width = new GridLength(1, GridUnitType.Star);
            ColumnGap.Width = new GridLength(0);
            Column3D.Width = new GridLength(0);
        }
        else if (Layout3DRadio.IsChecked == true)
        {
            Pane2D.Visibility = Visibility.Collapsed;
            Pane3D.Visibility = Visibility.Visible;
            Column2D.Width = new GridLength(0);
            ColumnGap.Width = new GridLength(0);
            Column3D.Width = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            Pane2D.Visibility = Visibility.Visible;
            Pane3D.Visibility = Visibility.Visible;
            Column2D.Width = new GridLength(1, GridUnitType.Star);
            ColumnGap.Width = new GridLength(12);
            Column3D.Width = new GridLength(1.15, GridUnitType.Star);
        }
    }

    private void OnMap2DSiteClicked(object? sender, ElectrodeMontageSite site)
    {
        Map3D.SelectedElectrodeLabel = site.Channel;
        SelectedSiteSummary.Text = $"已选：{site.Channel}  (2D: {site.MapX:0.00}, {site.MapY:0.00})";
    }

    private void SetError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            StatusBanner.Visibility = Visibility.Collapsed;
            StatusMessageText.Text = string.Empty;
        }
        else
        {
            StatusMessageText.Text = message;
            StatusBanner.Visibility = Visibility.Visible;
        }
    }

    private void CancelPendingLoad()
    {
        loadCts?.Cancel();
        loadCts?.Dispose();
        loadCts = null;
    }
}
