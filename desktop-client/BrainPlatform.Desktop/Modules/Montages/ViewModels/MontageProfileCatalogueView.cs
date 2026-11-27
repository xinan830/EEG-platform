using System.Collections.ObjectModel;

namespace BrainPlatform.Desktop.Modules.Montages.ViewModels;

/// <summary>
/// Sole owner of the montage profile catalogue, acquisition-available projection,
/// list-page search/filters, and pagination state.
/// </summary>
internal sealed class MontageProfileCatalogueView : ObservableObject
{
    private string searchText = string.Empty;
    private string channelConfigurationFilter = "全部通道配置";
    private string montageMethodFilter = "全部方式";
    private string sourceFilter = "全部来源";
    private string statusFilter = "全部状态";
    private int pageSize = 10;
    private int currentPage = 1;

    public event EventHandler? FilteredProfilesUpdated;

    public ObservableCollection<MontageProfile> Profiles { get; } = [];

    public ObservableCollection<MontageProfile> FilteredProfiles { get; } = [];

    public ObservableCollection<MontageProfile> AvailableForAcquisition { get; } = [];

    public IReadOnlyList<string> SourceFilterOptions { get; } = ["全部来源", "系统默认", "用户创建"];

    public IReadOnlyList<string> StatusFilterOptions { get; } =
        ["全部状态", "可用", "被引用", "已归档", "设备不匹配", "来源已修改", "来源已删除", "配置无效"];

    public IReadOnlyList<string> MontageMethodFilterOptions { get; } =
        ["全部方式", "原始硬件参考", "指定通道", "平均参考", "指定双参考"];

    public IReadOnlyList<int> PageSizeOptions { get; } = [10, 20, 50];

    public IReadOnlyList<string> ChannelConfigurationFilterOptions =>
        ["全部通道配置", .. Profiles
            .Select(profile => profile.ChannelConfigurationName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

    public string SearchText
    {
        get => searchText;
        set { if (SetProperty(ref searchText, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public string ChannelConfigurationFilter
    {
        get => channelConfigurationFilter;
        set { if (SetProperty(ref channelConfigurationFilter, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public string MontageMethodFilter
    {
        get => montageMethodFilter;
        set { if (SetProperty(ref montageMethodFilter, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public string SourceFilter
    {
        get => sourceFilter;
        set { if (SetProperty(ref sourceFilter, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public string StatusFilter
    {
        get => statusFilter;
        set { if (SetProperty(ref statusFilter, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public int PageSize
    {
        get => pageSize;
        set
        {
            var normalized = PageSizeOptions.Contains(value) ? value : 10;
            if (SetProperty(ref pageSize, normalized))
            {
                currentPage = 1;
                RaisePropertyChanged(nameof(CurrentPage));
                ApplyFilters();
            }
        }
    }

    public int CurrentPage
    {
        get => currentPage;
        set
        {
            var normalized = Math.Clamp(value, 1, TotalPages);
            if (SetProperty(ref currentPage, normalized))
            {
                ApplyFilters();
            }
        }
    }

    public int TotalItemCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public bool CanPreviousPage => CurrentPage > 1;

    public bool CanNextPage => CurrentPage < TotalPages;

    public void ReplaceProfiles(IEnumerable<MontageProfile> profiles)
    {
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        AvailableForAcquisition.Clear();
        foreach (var profile in Profiles.Where(profile => profile.ChannelSnapshotStatusLabel == "已同步" && !profile.IsArchived))
        {
            AvailableForAcquisition.Add(profile);
        }

        RaisePropertyChanged(nameof(ChannelConfigurationFilterOptions));
        if (!ChannelConfigurationFilterOptions.Contains(ChannelConfigurationFilter, StringComparer.OrdinalIgnoreCase))
        {
            channelConfigurationFilter = "全部通道配置";
            RaisePropertyChanged(nameof(ChannelConfigurationFilter));
        }

        ApplyFilters();
    }

    public void ApplyFilters(bool resetToFirstPage = false)
    {
        if (resetToFirstPage && currentPage != 1)
        {
            currentPage = 1;
            RaisePropertyChanged(nameof(CurrentPage));
        }

        var search = SearchText.Trim();
        var filtered = Profiles.Where(profile =>
            string.IsNullOrWhiteSpace(search) ||
            profile.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            profile.DisplayDescription.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            profile.ChannelConfigurationName.Contains(search, StringComparison.CurrentCultureIgnoreCase));

        if (ChannelConfigurationFilter != "全部通道配置")
        {
            filtered = filtered.Where(profile =>
                string.Equals(profile.ChannelConfigurationName, ChannelConfigurationFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (MontageMethodFilter != "全部方式")
        {
            filtered = filtered.Where(profile => profile.DerivedChannels.Any(channel =>
                string.Equals(MontageDisplay.TextFor(channel.NegativeKind), MontageMethodFilter, StringComparison.Ordinal)));
        }

        if (SourceFilter is "系统默认" or "用户创建")
        {
            var source = SourceFilter == "系统默认" ? MontageProfileSource.System : MontageProfileSource.User;
            filtered = filtered.Where(profile => profile.Source == source);
        }

        if (StatusFilter != "全部状态")
        {
            filtered = filtered.Where(profile => profile.ListStatusLabel == StatusFilter);
        }

        var results = filtered.ToArray();
        TotalItemCount = results.Length;
        TotalPages = Math.Max(1, (int)Math.Ceiling(results.Length / (double)PageSize));
        if (currentPage > TotalPages)
        {
            currentPage = TotalPages;
            RaisePropertyChanged(nameof(CurrentPage));
        }

        FilteredProfiles.Clear();
        foreach (var profile in results.Skip((CurrentPage - 1) * PageSize).Take(PageSize))
        {
            FilteredProfiles.Add(profile);
        }

        RaisePropertyChanged(nameof(TotalItemCount));
        RaisePropertyChanged(nameof(TotalPages));
        RaisePropertyChanged(nameof(CanPreviousPage));
        RaisePropertyChanged(nameof(CanNextPage));
        FilteredProfilesUpdated?.Invoke(this, EventArgs.Empty);
    }
}
