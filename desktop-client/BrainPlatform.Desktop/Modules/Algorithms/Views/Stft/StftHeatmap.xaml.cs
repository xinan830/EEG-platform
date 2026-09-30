using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Stft;
using SciChart.Charting;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Model.DataSeries.Heatmap2DArrayDataSeries;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Stft;

public partial class StftHeatmap : UserControl
{
    private readonly NumericAxis xAxis;
    private readonly NumericAxis yAxis;
    private readonly HeatmapColorPalette palette;
    private readonly FastUniformHeatmapRenderableSeries heatmapSeries;
    private readonly CursorModifier cursor;

    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
        nameof(Preview), typeof(StftPreview), typeof(StftHeatmap), new PropertyMetadata(null, OnPreviewChanged));

    public StftPreview? Preview
    {
        get => (StftPreview?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public StftHeatmap()
    {
        InitializeComponent();
        xAxis = CreateAxis("时间（s）", AxisAlignment.Bottom);
        yAxis = CreateAxis("频率（Hz）", AxisAlignment.Left);
        xAxis.AutoRange = AutoRange.Never;
        yAxis.AutoRange = AutoRange.Never;

        palette = new HeatmapColorPalette
        {
            Precision = 4,
            AllowsHighPrecision = true,
            GradientStops =
            {
                new GradientStop(Color.FromRgb(25, 51, 75), 0.0),
                new GradientStop(Color.FromRgb(40, 127, 146), 0.33),
                new GradientStop(Color.FromRgb(228, 189, 92), 0.67),
                new GradientStop(Color.FromRgb(217, 76, 68), 1.0),
            },
        };
        heatmapSeries = new FastUniformHeatmapRenderableSeries
        {
            ColorMap = palette,
            UseLinearTextureFiltering = true,
            DrawTextInCell = false,
            XAxisId = xAxis.Id,
            YAxisId = yAxis.Id,
        };
        cursor = new CursorModifier
        {
            IsEnabled = true,
            ShowTooltip = true,
            ShowAxisLabels = true,
            ShowTooltipOn = ShowTooltipOptions.MouseOver,
            SnappingMode = CursorSnappingMode.TooltipToSeries,
            ReceiveHandledEvents = true,
        };

        Surface.Background = Brushes.White;
        Surface.ShowLicensingWarnings = false;
        Surface.XAxes.Add(xAxis);
        Surface.YAxes.Add(yAxis);
        Surface.RenderableSeries.Add(heatmapSeries);
        Surface.ChartModifier = cursor;
        ColourMap.ColorPalette = palette;
    }

    private static NumericAxis CreateAxis(string title, AxisAlignment alignment) => new()
    {
        AxisAlignment = alignment,
        AxisTitle = title,
        Width = alignment == AxisAlignment.Left ? 78 : double.NaN,
        FontSize = 12,
        TickTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        DrawMajorBands = false,
        DrawMajorGridLines = true,
        DrawMinorGridLines = false,
    };

    private static void OnPreviewChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((StftHeatmap)sender).UpdatePreview((StftPreview?)args.NewValue);

    private void UpdatePreview(StftPreview? preview)
    {
        heatmapSeries.DataSeries = null;
        if (preview is null || preview.TimesSeconds.Length == 0 || preview.FrequenciesHz.Length == 0)
            return;

        var width = preview.TimesSeconds.Length;
        var height = preview.FrequenciesHz.Length;
        var values = new double[height, width];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y, x] = preview.PowerDb[x][y] ?? double.NaN;

        var xStep = width > 1 ? preview.TimesSeconds[1] - preview.TimesSeconds[0] : 1.0;
        var yStep = height > 1 ? preview.FrequenciesHz[1] - preview.FrequenciesHz[0] : 1.0;
        var dataSeries = new UniformHeatmapDataSeries<double, double, double>(
            values, preview.TimesSeconds[0], xStep, preview.FrequenciesHz[0], yStep, null);

        heatmapSeries.DataSeries = dataSeries;
        palette.Minimum = preview.MinimumDb;
        palette.Maximum = preview.MaximumDb;
        ColourMap.Minimum = preview.MinimumDb;
        ColourMap.Maximum = preview.MaximumDb;
        xAxis.VisibleRange = new DoubleRange(preview.TimesSeconds[0], preview.TimesSeconds[^1]);
        yAxis.VisibleRange = new DoubleRange(preview.FrequenciesHz[0], preview.FrequenciesHz[^1]);
    }
}
