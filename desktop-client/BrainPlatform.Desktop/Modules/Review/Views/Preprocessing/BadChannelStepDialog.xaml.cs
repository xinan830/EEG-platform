using System.Windows;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;

namespace BrainPlatform.Desktop.Modules.Review.Views.Preprocessing;

public partial class BadChannelStepDialog : Window
{
    private readonly ReviewPreprocessingViewModel viewModel;

    public BadChannelStepDialog(ReviewPreprocessingViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnSelectCandidatesClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in viewModel.BadChannelRows)
            row.IsSelected = row.IsBadCandidate;
    }

    private void OnClearSelectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in viewModel.BadChannelRows)
            row.IsSelected = false;
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
