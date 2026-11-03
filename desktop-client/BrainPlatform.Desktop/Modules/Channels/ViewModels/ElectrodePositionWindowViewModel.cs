using System.Collections.Specialized;
using System.ComponentModel;
using BrainPlatform.Desktop.Modules.Channels.Api;
using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Modules.Channels.ViewModels;

public sealed class ElectrodePositionWindowViewModel : ObservableObject, IDisposable
{
    private readonly IElectrodeLocationClient? electrodeLocationClient;
    private ChannelConfigurationDraftRow[] observedRows = [];
    private ChannelConfigurationDraftRow[] savedProfileRows = [];
    private Task? modelLoad;
    private ElectrodeScalpModel3D? model;
    private string? modelError;
    private bool isModelLoading;
    private bool isThreeDimensional;

    public ElectrodePositionWindowViewModel(ChannelConfigurationWorkspaceViewModel channelConfigurations,
        IElectrodeLocationClient? electrodeLocationClient)
    {
        ChannelConfigurations = channelConfigurations ?? throw new ArgumentNullException(nameof(channelConfigurations));
        this.electrodeLocationClient = electrodeLocationClient;
        ChannelConfigurations.DraftRows.CollectionChanged += OnRowsCollectionChanged;
        ChannelConfigurations.PropertyChanged += OnConfigurationPropertyChanged;
        RefreshSavedProfileRows();
        ObserveRows();
    }

    public ChannelConfigurationWorkspaceViewModel ChannelConfigurations { get; }

    public ElectrodeScalpModel3D? Model
    {
        get => model;
        private set
        {
            if (SetProperty(ref model, value))
            {
                RaisePropertyChanged(nameof(ModelStatusText));
                RaisePropertyChanged(nameof(PositionViewHint));
            }
        }
    }

    public string? ModelError
    {
        get => modelError;
        private set
        {
            if (SetProperty(ref modelError, value))
            {
                RaisePropertyChanged(nameof(ModelStatusText));
                RaisePropertyChanged(nameof(PositionViewHint));
            }
        }
    }

    public bool IsModelLoading
    {
        get => isModelLoading;
        private set
        {
            if (SetProperty(ref isModelLoading, value))
            {
                RaisePropertyChanged(nameof(ModelStatusText));
                RaisePropertyChanged(nameof(PositionViewHint));
            }
        }
    }

    public bool IsThreeDimensional
    {
        get => isThreeDimensional;
        set
        {
            if (!SetProperty(ref isThreeDimensional, value)) return;
            RaisePropertyChanged(nameof(IsTwoDimensional));
            RaisePropertyChanged(nameof(PositionViewHint));
        }
    }

    public bool IsTwoDimensional
    {
        get => !IsThreeDimensional;
        set { if (value) IsThreeDimensional = false; }
    }

    public IReadOnlyList<ChannelConfigurationDraftRow> PositionRows =>
        ChannelConfigurations.DraftRows.Count > 0 ? ChannelConfigurations.DraftRows : savedProfileRows;

    public bool HasPositionRows => PositionRows.Count > 0;

    public bool HasEditableDraftRows => ChannelConfigurations.DraftRows.Count > 0;

    public string ModelStatusText => ModelError ?? (IsModelLoading
        ? "正在加载三维头模"
        : Model is null ? "切换到 3D 视图以加载标准模型" : string.Empty);

    public string PositionViewHint
    {
        get
        {
            if (!IsThreeDimensional)
                return "蓝色：标准模板位置 · 橙色：手动 EEG 示意位置 · 紫色：REF · 绿色：GND。仅 2D 位置可编辑。";
            if (!string.IsNullOrWhiteSpace(ModelError)) return ModelError;
            if (IsModelLoading) return "正在加载标准 3D 头模和电极位置。";
            if (Model is null) return ModelStatusText;
            var available = Model.ElectrodeLocations
                .SelectMany(site => new[] { site.Channel, site.CanonicalChannel })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unavailable = EnabledElectrodeLabels.Where(label => !available.Contains(label)).ToArray();
            return unavailable.Length == 0
                ? "标准模板位置只读；未映射通道不从 2D 位置推算。"
                : $"未映射到标准 3D 头皮位置：{string.Join("、", unavailable)}";
        }
    }

    public IReadOnlyList<string> EnabledElectrodeLabels => PositionRows
        .Where(row => row.IsEegInput && row.IsEnabled && !string.IsNullOrWhiteSpace(row.ElectrodeLabel))
        .Select(row => row.ElectrodeLabel.Trim())
        .ToArray();

    public Task LoadModelAsync() => modelLoad ??= LoadModelCoreAsync();

    public void Dispose()
    {
        ChannelConfigurations.DraftRows.CollectionChanged -= OnRowsCollectionChanged;
        ChannelConfigurations.PropertyChanged -= OnConfigurationPropertyChanged;
        UnobserveRows();
    }

    private async Task LoadModelCoreAsync()
    {
        IsModelLoading = true;
        ModelError = null;
        try
        {
            if (electrodeLocationClient is null)
                throw new InvalidOperationException("三维电极位置服务未配置。");
            Model = await electrodeLocationClient.GetScalpModel3DAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ModelError = $"三维头模加载失败：{exception.Message}";
            modelLoad = null;
        }
        finally
        {
            IsModelLoading = false;
        }
    }

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        UnobserveRows();
        ObserveRows();
        RaisePropertyChanged(nameof(PositionRows));
        RaisePropertyChanged(nameof(HasPositionRows));
        RaisePropertyChanged(nameof(HasEditableDraftRows));
        RaisePropertyChanged(nameof(EnabledElectrodeLabels));
        RaisePropertyChanged(nameof(PositionViewHint));
    }

    private void OnConfigurationPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ChannelConfigurationWorkspaceViewModel.SelectedProfile)) return;
        RefreshSavedProfileRows();
        RaisePropertyChanged(nameof(PositionRows));
        RaisePropertyChanged(nameof(HasPositionRows));
        RaisePropertyChanged(nameof(EnabledElectrodeLabels));
        RaisePropertyChanged(nameof(PositionViewHint));
    }

    private void RefreshSavedProfileRows()
    {
        savedProfileRows = ChannelConfigurations.SelectedProfile?.Channels
            .Select(entry => new ChannelConfigurationDraftRow(
                entry.NativeChannelIndex,
                entry.ExpectedKind.ToString(),
                string.Empty,
                string.Empty,
                entry.ElectrodeLabel,
                entry.IsSelectedForDisplay,
                entry.DisplayOrder,
                true,
                entry.ElectrodeLocation,
                entry.ManualMapPosition))
            .ToArray() ?? [];
    }

    private void ObserveRows()
    {
        observedRows = ChannelConfigurations.DraftRows.ToArray();
        foreach (var row in observedRows) row.PropertyChanged += OnDraftRowChanged;
    }

    private void UnobserveRows()
    {
        foreach (var row in observedRows) row.PropertyChanged -= OnDraftRowChanged;
        observedRows = [];
    }

    private void OnDraftRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ChannelConfigurationDraftRow.ElectrodeLabel) or
            nameof(ChannelConfigurationDraftRow.IsEnabled))
        {
            RaisePropertyChanged(nameof(EnabledElectrodeLabels));
            RaisePropertyChanged(nameof(PositionViewHint));
        }
    }
}
