namespace BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;

public sealed partial class AlgorithmListViewModel
{
    internal void SetDynamicResultWindows(IEnumerable<DynamicWindowRow> windows, double initialCursorSeconds)
    {
        ResetDynamicPreview();
        DynamicWindowRows.Clear();
        foreach (var window in windows)
            DynamicWindowRows.Add(window);
        DynamicPreviewCursorSeconds = initialCursorSeconds;
        ReleaseDynamicPreviewRows();
        RaisePropertyChanged(nameof(DynamicWindowRows));
        RaisePropertyChanged(nameof(HasDynamicWindows));
        RaiseDynamicPreviewProperties();
    }
}
