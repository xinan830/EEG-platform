using System.Windows;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;

namespace BrainPlatform.Desktop.Modules.Review.Views.Preprocessing;

public partial class ChannelIdentityDialog : Window
{
    private readonly ReviewPreprocessingViewModel viewModel;

    public ChannelIdentityDialog(ReviewPreprocessingViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        SaveIdentitiesButton.IsEnabled = false;
        CancelIdentitiesButton.IsEnabled = false;
        try
        {
            await viewModel.SaveChannelIdentitiesAsync();
            if (!viewModel.HasUnsavedChannelIdentities)
            {
                DialogResult = true;
                Close();
            }
        }
        finally
        {
            CancelIdentitiesButton.IsEnabled = true;
            SaveIdentitiesButton.IsEnabled = viewModel.HasUnsavedChannelIdentities;
        }
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        CancelIdentitiesButton.IsEnabled = false;
        try
        {
            if (viewModel.HasUnsavedChannelIdentities)
                await viewModel.DiscardUnsavedChannelIdentitiesAsync();
            DialogResult = false;
            Close();
        }
        finally
        {
            CancelIdentitiesButton.IsEnabled = true;
        }
    }
}
