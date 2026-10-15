using System.Windows.Controls;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views;

public partial class AlgorithmDetailView : UserControl
{
    public AlgorithmDetailView()
    {
        InitializeComponent();
        Loaded += (_, _) => (DataContext as AlgorithmDetailViewModel)?.Catalog.ResumeWindowAnalysis();
        Unloaded += async (_, _) =>
        {
            if (DataContext is AlgorithmDetailViewModel detail) await detail.Catalog.ReleaseWindowAnalysisAsync();
        };
    }
}
