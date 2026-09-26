using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels;
using BrainPlatform.Desktop.ViewModels;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views;

public partial class AlgorithmListView : UserControl
{
    private AlgorithmListViewModel? viewModel;

    public AlgorithmListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        PsdCanvas.SizeChanged += (_, _) => DrawPsd();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (viewModel is not null) viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel = (args.NewValue as DesktopWorkspaceViewModel)?.AlgorithmCatalog;
        if (viewModel is not null) viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DrawPsd();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AlgorithmListViewModel.HasPsdPreview) or nameof(AlgorithmListViewModel.PsdPreviewPoints))
            Dispatcher.InvokeAsync(DrawPsd);
    }

    private void DrawPsd()
    {
        PsdCanvas.Children.Clear();
        var points = viewModel?.PsdPreviewPoints;
        if (points is null || points.Count < 1 || PsdCanvas.ActualWidth <= 1 || PsdCanvas.ActualHeight <= 1) return;
        var validPoints = points.Where(point => point is not null).Select(point => point!).ToArray();
        if (validPoints.Length == 0) return;
        var minX = validPoints.Min(point => point.Frequency);
        var maxX = validPoints.Max(point => point.Frequency);
        var minY = validPoints.Min(point => point.Value);
        var maxY = validPoints.Max(point => point.Value);
        if (maxX <= minX) maxX = minX + 1;
        if (maxY <= minY) maxY = minY + 1;
        Polyline? polyline = null;
        foreach (var point in points)
        {
            if (point is null)
            {
                polyline = null;
                continue;
            }
            polyline ??= new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(22, 119, 255)), StrokeThickness = 1.5, SnapsToDevicePixels = true };
            var x = (point.Frequency - minX) / (maxX - minX) * PsdCanvas.ActualWidth;
            var y = PsdCanvas.ActualHeight - (point.Value - minY) / (maxY - minY) * PsdCanvas.ActualHeight;
            polyline.Points.Add(new Point(x, y));
            if (polyline.Points.Count == 1) PsdCanvas.Children.Add(polyline);
        }
    }
}
