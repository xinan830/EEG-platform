using System.Windows;
using BrainPlatform.Desktop.Modules.Review.Preprocessing;

namespace BrainPlatform.Desktop.Modules.Review.Views.Preprocessing;

public partial class MontageFilterContractDialog : Window
{
    public MontageFilterContractDialog(ReviewPreprocessingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
