using System.Windows;
using System.Windows.Input;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;

namespace BrainPlatform.Desktop.Modules.Review.Views.Preprocessing;

public partial class PreprocessingPipelineDialog : Window
{
    private readonly ReviewPreprocessingViewModel viewModel;

    public PreprocessingPipelineDialog(ReviewPreprocessingViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.RefreshContextMetadata();
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void OnOpenBadChannelDialogClick(object sender, RoutedEventArgs e)
    {
        var dialog = new BadChannelStepDialog(viewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnOpenMontageFilterDialogClick(object sender, RoutedEventArgs e)
    {
        viewModel.RefreshContextMetadata();
        var dialog = new MontageFilterContractDialog(viewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnOpenArtifactSegmentDialogClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ArtifactSegmentStepDialog(viewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnOpenArtifactRemovalDialogClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ArtifactRemovalStepDialog(viewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
