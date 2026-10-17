using BrainPlatform.Desktop.Modules.Algorithms.Api;
using BrainPlatform.Desktop.Modules.Algorithms.Contracts;

namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        IsLoadFailed = false;
        try
        {
            var items = (await client.ListAlgorithmsAsync(CancellationToken.None))
                .OrderBy(item => IsOfficial(item) ? 0 : 1)
                .ThenBy(item => AlgorithmUiRegistry.For(item.Id).DisplayOrder)
                .ThenBy(item => item.DisplayNameZh, StringComparer.CurrentCulture)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            SynchronizeCatalogItems(items);
            if (!string.IsNullOrWhiteSpace(selectedAlgorithmId))
                SelectedAlgorithm = Items.FirstOrDefault(item => string.Equals(item.Id, selectedAlgorithmId, StringComparison.OrdinalIgnoreCase));
            ApplyCatalogFilter();
            StatusText = $"已加载 {Items.Count} 个官方算法。";
        }
        catch (Exception exception)
        {
            IsLoadFailed = true;
            ReportCatalogError(exception);
        }
        finally { IsLoading = false; }
    }

    private void SynchronizeCatalogItems(IReadOnlyList<AlgorithmCatalogItem> incomingItems)
    {
        // Keep collection identity stable so refresh does not flash SelectedItem=null.
        var incomingIds = incomingItems.Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = Items.Count - 1; index >= 0; index--)
        {
            if (!incomingIds.Contains(Items[index].Id))
                Items.RemoveAt(index);
        }

        for (var index = 0; index < incomingItems.Count; index++)
        {
            var incoming = incomingItems[index];
            var existingIndex = FindCatalogItemIndex(incoming.Id);
            if (existingIndex < 0)
            {
                Items.Insert(index, incoming);
                continue;
            }

            if (existingIndex != index)
                Items.Move(existingIndex, index);

            if (!Equals(Items[index], incoming) &&
                !string.Equals(incoming.Id, selectedAlgorithmId, StringComparison.OrdinalIgnoreCase))
            {
                Items[index] = incoming;
            }
        }
    }

    private int FindCatalogItemIndex(string algorithmId)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (string.Equals(Items[index].Id, algorithmId, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return -1;
    }

    private void ApplyCatalogFilter()
    {
        var query = SearchText.Trim();
        var filtered = Items.Where(item =>
            (query.Length == 0 ||
             item.DisplayNameZh.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.Abbreviation.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (SelectedModeFilter == "全部" ||
             (SelectedModeFilter == "静态" && item.Modes.Contains("static")) ||
             (SelectedModeFilter == "动态" && item.Modes.Contains("dynamic"))) &&
            (SelectedAvailabilityFilter == "全部" || item.StatusDisplay == SelectedAvailabilityFilter) &&
            (SelectedTypeFilter == "全部" || item.AlgorithmTypeDisplay == SelectedTypeFilter))
            .ToArray();

        VisibleItems.Clear();
        foreach (var item in filtered)
            VisibleItems.Add(item);

        if (SelectedAlgorithm is not null && !VisibleItems.Contains(SelectedAlgorithm))
            SelectedAlgorithm = null;
        RaisePropertyChanged(nameof(VisibleItems));
    }

    private static bool IsOfficial(AlgorithmCatalogItem item) =>
        string.Equals(item.Source, "official", StringComparison.OrdinalIgnoreCase);

    private void ReportCatalogError(Exception exception)
    {
        StatusText = exception is AlgorithmApiException apiError
            ? $"算法目录加载失败：{apiError.Code}"
            : $"算法目录加载失败：{exception.Message}";
        notifications.PublishError(StatusText);
    }
}
