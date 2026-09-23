using System.Windows;
using System.Windows.Controls;

namespace BrainPlatform.Desktop.Views;

public partial class DeviceOverviewView : UserControl
{
    private bool isCompactLayout;
    private bool layoutInitialized;

    public DeviceOverviewView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyResponsiveLayout(ActualWidth);
    }

    private void OnOverviewRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        // The two-column composition is comfortable above this width. Below
        // it, stacking the visual and details panels prevents clipping and
        // keeps the device facts readable at small laptop resolutions.
        var compact = width > 0 && width < 1_080;
        if (layoutInitialized && compact == isCompactLayout)
        {
            return;
        }

        isCompactLayout = compact;
        layoutInitialized = true;
        OverviewContentGrid.ColumnDefinitions.Clear();
        OverviewContentGrid.RowDefinitions.Clear();

        if (compact)
        {
            OverviewRoot.Margin = new Thickness(16, 18, 16, 18);
            OverviewCard.Padding = new Thickness(24);
            OverviewContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            OverviewContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
            OverviewContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetColumn(OverviewVisualPanel, 0);
            Grid.SetRow(OverviewVisualPanel, 0);
            Grid.SetColumn(OverviewDivider, 0);
            Grid.SetRow(OverviewDivider, 1);
            Grid.SetColumn(OverviewDetailsPanel, 0);
            Grid.SetRow(OverviewDetailsPanel, 2);

            OverviewDivider.Width = double.NaN;
            OverviewDivider.Height = 1;
            OverviewDivider.HorizontalAlignment = HorizontalAlignment.Stretch;
            OverviewDivider.VerticalAlignment = VerticalAlignment.Center;
            OverviewDivider.Margin = new Thickness(12, 0, 12, 0);
            OverviewDetailsPanel.Margin = new Thickness(0);
        }
        else
        {
            OverviewRoot.Margin = new Thickness(32, 28, 32, 28);
            OverviewCard.Padding = new Thickness(56, 40, 56, 40);
            OverviewContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            OverviewContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            OverviewContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });

            Grid.SetColumn(OverviewVisualPanel, 0);
            Grid.SetRow(OverviewVisualPanel, 0);
            Grid.SetColumn(OverviewDivider, 1);
            Grid.SetRow(OverviewDivider, 0);
            Grid.SetColumn(OverviewDetailsPanel, 2);
            Grid.SetRow(OverviewDetailsPanel, 0);

            OverviewDivider.Width = 1;
            OverviewDivider.Height = double.NaN;
            OverviewDivider.HorizontalAlignment = HorizontalAlignment.Stretch;
            OverviewDivider.VerticalAlignment = VerticalAlignment.Stretch;
            OverviewDivider.Margin = new Thickness(0, 16, 0, 16);
            OverviewDetailsPanel.Margin = new Thickness(56, 0, 0, 0);
        }
    }
}
