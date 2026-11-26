using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BrainPlatform.Desktop.Modules.Review.ViewModels;
using BrainPlatform.Desktop.Shared.Waveform;

namespace BrainPlatform.Desktop.Modules.Review.Waveform;

public sealed partial class RecordingReviewSciChartCanvas
{
    private ReviewArtifactSegmentLayer artifactSegments = null!;
    private ReviewPreprocessingOverlayLayer preprocessingOverlay = null!;

    private void InitializePreprocessingLayers()
    {
        artifactSegments = new ReviewArtifactSegmentLayer(surface, chartHost);
        preprocessingOverlay = new ReviewPreprocessingOverlayLayer(surface);
        chartHost.Children.Add(artifactSegments.Canvas);
    }

    private void SubscribePreprocessing(RecordingReviewViewModel? viewModel)
    {
        if (viewModel is not null)
            viewModel.Preprocessing.VisualStateChanged += OnPreprocessingVisualStateChanged;
    }

    private void UnsubscribePreprocessing(RecordingReviewViewModel? viewModel)
    {
        if (viewModel is not null)
            viewModel.Preprocessing.VisualStateChanged -= OnPreprocessingVisualStateChanged;
    }

    private void OnPreprocessingVisualStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshPreprocessingVisuals(DataContext as RecordingReviewViewModel);
            return;
        }
        _ = Dispatcher.InvokeAsync(() => RefreshPreprocessingVisuals(DataContext as RecordingReviewViewModel));
    }

    private static StackPanel CreateChannelLabelCell(string channelName, Color traceColor)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
        };
        panel.Children.Add(new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Visibility = Visibility.Collapsed,
        });
        panel.Children.Add(new TextBlock
        {
            Text = channelName,
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(traceColor),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0),
        });
        return panel;
    }

    private void RefreshPreprocessingVisuals(RecordingReviewViewModel? viewModel)
    {
        artifactSegments.Rebuild(viewModel);
        var plotArea = WaveformPlotLayout.GetPlotArea(surface);
        viewModel?.Preprocessing.UpdatePlotWidth(plotArea.Width);
        preprocessingOverlay.ApplyVisuals(
            viewModel,
            activeLabels,
            renderedFrame?.OutputChannelColors,
            traceSeries.Select(item => item.LineSeries).ToArray(),
            labels,
            plotArea.Height,
            screenScale);
        RefreshSampleMarkers(renderedFrame, viewModel);
    }

    private void UpdatePreprocessingOnViewportChanged(RecordingReviewViewModel? viewModel)
    {
        artifactSegments.UpdatePositions();
    }

    private void ClearPreprocessingVisuals()
    {
        artifactSegments.Clear();
        preprocessingOverlay.Clear();
    }
}
