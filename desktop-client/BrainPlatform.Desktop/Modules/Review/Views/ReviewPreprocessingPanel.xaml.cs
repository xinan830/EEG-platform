using System.Windows;
using System.Windows.Controls;
using BrainPlatform.Desktop.Modules.Review.ViewModels;

namespace BrainPlatform.Desktop.Modules.Review.Views;

public partial class ReviewPreprocessingPanel : UserControl
{
    public ReviewPreprocessingPanel()
    {
        InitializeComponent();
    }

    private async void OnOpenIcaReviewClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RecordingReviewViewModel review) return;
        var prep = review.Preprocessing;
        try
        {
            var response = await prep.InspectIcaForDialogAsync();
            var dialog = new IcaReviewDialog(prep, response)
            {
                Owner = Window.GetWindow(this),
            };
            if (dialog.ShowDialog() == true)
            {
                prep.ConfirmIcaSelection(dialog.SelectedExcludedComponents, response.IcaFitFingerprint);
            }
            else prep.CancelIcaReview();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"ICA 成分审查失败：{ex.Message}",
                "ICA 审查",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
