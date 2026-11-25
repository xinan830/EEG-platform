using System.Windows;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;

namespace BrainPlatform.Desktop.Modules.Review.Views.Preprocessing;

public partial class ArtifactSegmentStepDialog : Window
{
    public ArtifactSegmentStepDialog(ReviewPreprocessingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
