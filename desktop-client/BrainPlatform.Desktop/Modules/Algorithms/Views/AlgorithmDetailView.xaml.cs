using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views;

public partial class AlgorithmDetailView : UserControl
{
    private AlgorithmListViewModel? catalog;
    private bool lifecycleActive;

    public AlgorithmDetailView()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncLifecycle();
        Unloaded += (_, _) => SuspendCurrentCatalog();
        IsVisibleChanged += (_, _) => SyncLifecycle();
        DataContextChanged += (_, _) => SyncLifecycle();
    }

    private void SyncLifecycle()
    {
        var next = ResolveCatalog(DataContext);
        if (!ReferenceEquals(catalog, next))
        {
            SuspendCurrentCatalog();
            catalog = next;
        }

        if (IsLoaded && IsVisible)
        {
            lifecycleActive = true;
            catalog?.ResumeDetail();
        }
        else
        {
            SuspendCurrentCatalog();
        }
    }

    private void SuspendCurrentCatalog()
    {
        if (!lifecycleActive) return;
        lifecycleActive = false;
        _ = catalog?.SuspendDetailAsync();
    }

    private static AlgorithmListViewModel? ResolveCatalog(object? dataContext) => dataContext switch
    {
        DesktopWorkspaceViewModel workspace => workspace.AlgorithmWorkspace.Catalog,
        AlgorithmDetailViewModel detail => detail.Catalog,
        _ => null,
    };
}
