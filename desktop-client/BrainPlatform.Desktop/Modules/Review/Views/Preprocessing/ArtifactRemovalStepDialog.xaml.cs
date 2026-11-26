using System.Windows;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;

namespace BrainPlatform.Desktop.Modules.Review.Views.Preprocessing;

public partial class ArtifactRemovalStepDialog : Window
{
    private readonly ReviewPreprocessingViewModel viewModel;

    public ArtifactRemovalStepDialog(ReviewPreprocessingViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadReferenceChannelsAsync();
    }

    private void OnOpenChannelIdentityClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ChannelIdentityDialog(viewModel)
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    private async void OnOpenIcaReviewClick(object sender, RoutedEventArgs e)
    {
        OpenIcaReviewButton.IsEnabled = false;
        try
        {
            var response = await viewModel.InspectIcaForDialogAsync();
            var dialog = new IcaReviewDialog(viewModel, response)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() == true)
            {
                viewModel.ConfirmIcaSelection(dialog.SelectedExcludedComponents, response.IcaFitFingerprint);
            }
            else
            {
                viewModel.CancelIcaReview();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"ICA 成分审查失败：{ex.Message}",
                "ICA 审查",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            OpenIcaReviewButton.IsEnabled = true;
        }
    }

    private void OnDoneClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
