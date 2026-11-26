using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Settings.Views;

public partial class BrainImagingHubView : UserControl
{
    private static readonly string[] DefaultTenTwentyLabels =
    [
        "Fp1", "Fp2", "F7", "F3", "Fz", "F4", "F8",
        "T3", "C3", "Cz", "C4", "T4",
        "T5", "P3", "Pz", "P4", "T6",
        "O1", "O2",
    ];

    private static readonly Dictionary<string, (string Category, string Summary)> ModeMetadata = new(StringComparer.Ordinal)
    {
        ["cortex"] = ("地形图与皮层图谱", "高精度 fsaverage 折叠皮层连续电位分布，支持色带与分布场景切换。"),
        ["atlas"] = ("地形图与皮层图谱", "交互式脑区图谱（脑叶 / Desikan-Killiany 68区 / Yeo-7 功能网络），悬停或点击高亮。"),
        ["scalp"] = ("地形图与皮层图谱", "3D 真实头皮表面电位插值分布，面部与颈部自动遮罩隔离。"),
        ["electrode_atlas"] = ("地形图与皮层图谱", "常用 15 套标准/高密度电极模板预览（默认 colin27_1020），支持 2D 平面与 3D 头模联动。"),
        ["connectome"] = ("脑网络与溯源分析", "3D 玻璃脑功能连接弧线与核心枢纽节点，支持阈值过滤与悬停聚光。"),
        ["source"] = ("脑网络与溯源分析", "sLORETA 皮层激活热区 + 3D 等效电流偶极子，点击偶极子可直接跳转至 MRI 三切面定位。"),
        ["tractography"] = ("多模态结构影像", "标准 DTI 方向着色白质纤维束（红=左右 · 绿=前后 · 蓝=上下），支持导入本地 .tck / .trk。"),
        ["mri"] = ("多模态结构影像", "MNI152 0.74mm 高清 T1 MRI 三切面 + 3D 剖切面，支持 FreeSurfer 核团叠加与本地 .nii.gz 导入。"),
    };

    private bool syncingModeFromViewer;

    public BrainImagingHubView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshElectrodeLabels();
    }

    public void RefreshElectrodeLabels()
    {
        IReadOnlyList<string> labels = DefaultTenTwentyLabels;
        if (DataContext is DesktopWorkspaceViewModel workspace)
        {
            var draftLabels = workspace.ChannelConfigurations.DraftRows
                .Where(row => row.IsEegInput && row.IsEnabled && !string.IsNullOrWhiteSpace(row.ElectrodeLabel))
                .Select(row => row.ElectrodeLabel.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (draftLabels.Length >= 4)
            {
                labels = draftLabels;
            }
            else if (workspace.ChannelConfigurations.SelectedProfile is { } profile)
            {
                var savedLabels = profile.Channels
                    .Where(ch => ch.IsSelectedForDisplay && !string.IsNullOrWhiteSpace(ch.ElectrodeLabel))
                    .Select(ch => ch.ElectrodeLabel.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (savedLabels.Length >= 4)
                {
                    labels = savedLabels;
                }
            }
        }

        ImagingViewer.ElectrodeLabels = labels;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is IDesktopNavigation navigation)
        {
            navigation.ShowSettingsView();
        }
    }

    private void OnModeItemChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string mode } || string.IsNullOrWhiteSpace(mode))
        {
            return;
        }

        ApplyHeaderMetadata(mode);
        var isElectrodeAtlas = string.Equals(mode, "electrode_atlas", StringComparison.Ordinal);
        if (ElectrodeAtlasPanel is not null)
        {
            ElectrodeAtlasPanel.Visibility = isElectrodeAtlas ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ImagingViewer is not null)
        {
            ImagingViewer.Visibility = isElectrodeAtlas ? Visibility.Collapsed : Visibility.Visible;
            if (!isElectrodeAtlas && !syncingModeFromViewer)
            {
                ImagingViewer.ExternalMode = mode;
            }
        }
    }

    private void OnViewerActiveModeChanged(object? sender, string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;

        syncingModeFromViewer = true;
        try
        {
            ApplyHeaderMetadata(mode);
            var target = mode switch
            {
                "cortex" => NavCortex,
                "atlas" => NavAtlas,
                "scalp" => NavScalp,
                "connectome" => NavConnectome,
                "source" => NavSource,
                "tractography" => NavTractography,
                "mri" => NavMri,
                _ => null,
            };
            if (target is not null && target.IsChecked != true)
            {
                target.IsChecked = true;
            }
        }
        finally
        {
            syncingModeFromViewer = false;
        }
    }

    private void ApplyHeaderMetadata(string mode)
    {
        if (ActiveCategoryBadge is null || ActiveViewSummary is null) return;
        if (ModeMetadata.TryGetValue(mode, out var meta))
        {
            ActiveCategoryBadge.Text = meta.Category;
            ActiveViewSummary.Text = meta.Summary;
        }
    }
}
