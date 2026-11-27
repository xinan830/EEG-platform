using System.Collections.ObjectModel;
using BrainPlatform.Desktop.Modules.Channels.Domain;

namespace BrainPlatform.Desktop.Modules.Channels.ViewModels;

/// <summary>
/// Sole owner of the channel profile catalogue collection, search/status filters, and pagination.
/// </summary>
internal sealed class ChannelProfileCatalogueView(Func<bool> hasLoadedDevice) : ObservableObject
{
    private string searchText = string.Empty;
    private string deviceModelFilter = "全部型号";
    private string sourceFilter = "全部来源";
    private string statusFilter = "全部状态";
    private int pageSize = 10;
    private int currentPage = 1;

    public event EventHandler? FilteredProfilesUpdated;

    public ObservableCollection<ChannelConfigurationProfile> Profiles { get; } = [];

    public ObservableCollection<ChannelConfigurationProfile> FilteredProfiles { get; } = [];

    public IReadOnlyList<string> SourceFilterOptions { get; } = ["全部来源", "系统预设", "用户自定义"];

    public IReadOnlyList<string> StatusFilterOptions { get; } =
        ["全部状态", "可用", "设备不匹配", "待连接", "被导联引用", "不可修改"];

    public IReadOnlyList<int> PageSizeOptions { get; } = [10, 20, 50];

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

    public IReadOnlyList<string> DeviceModelFilterOptions =>
        ["全部型号", .. Profiles.Select(profile => profile.DeviceModelLabel).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value)];

    public string SearchText
    {
        get => searchText;
        set { if (SetProperty(ref searchText, value)) ApplyFilters(resetToFirstPage: true); }
    }

    public string DeviceModelFilter
    {
        get => deviceModelFilter;
        set { if (SetProperty(ref deviceModelFilter, value)) ApplyFilters(resetToFirstPage: true); }
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

    public void ReplaceProfiles(IEnumerable<ChannelConfigurationProfile> profiles)
    {
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        RaisePropertyChanged(nameof(DeviceModelFilterOptions));
        if (!DeviceModelFilterOptions.Contains(DeviceModelFilter, StringComparer.OrdinalIgnoreCase))
        {
            deviceModelFilter = "全部型号";
            RaisePropertyChanged(nameof(DeviceModelFilter));
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
            profile.DeviceModelLabel.Contains(search, StringComparison.CurrentCultureIgnoreCase));

        if (DeviceModelFilter != "全部型号")
        {
            filtered = filtered.Where(profile => profile.DeviceModelLabel == DeviceModelFilter);
        }

        if (SourceFilter is "系统预设" or "用户自定义")
        {
            var source = SourceFilter == "系统预设"
                ? ChannelConfigurationSource.System
                : ChannelConfigurationSource.User;
            filtered = filtered.Where(profile => profile.Source == source);
        }

        if (StatusFilter != "全部状态")
        {
            filtered = filtered.Where(profile => MatchesStatusFilter(profile, StatusFilter));
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

    private bool MatchesStatusFilter(ChannelConfigurationProfile profile, string status) => status switch
    {
        // “可用”描述的是配置能否用于当前设备；被导联引用只会锁定编辑，
        // 不会让一套仍与设备兼容的配置变得不可用。
        "可用" => hasLoadedDevice() && profile.IsCompatibleWithCurrentDevice,
        "设备不匹配" => hasLoadedDevice() && !profile.IsCompatibleWithCurrentDevice,
        "待连接" => !hasLoadedDevice(),
        "被导联引用" => profile.MontageReferenceCount > 0,
        // “不可修改”是权限筛选：系统默认和被导联引用的配置都属于只读配置。
        "不可修改" => profile.IsSignalLocked,
        _ => true,
    };
}
