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
    private readonly List<FastNonUniformHeatmapRenderableSeries> heatmapSegments = [];
    private readonly HashSet<double> renderedTimes = [];
    private readonly CursorModifier cursor;
    private double renderedThroughSeconds = double.NegativeInfinity;
    private double historyStartSeconds = double.NaN;
    private double[]? renderedFrequencies;
    private double minimumDb = double.PositiveInfinity;
    private double maximumDb = double.NegativeInfinity;

    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
        nameof(Preview), typeof(StftPreview), typeof(StftHeatmap), new PropertyMetadata(null, OnPreviewChanged));

    public StftPreview? Preview
    {
        get => (StftPreview?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public static readonly DependencyProperty RetainHistoryProperty = DependencyProperty.Register(
        nameof(RetainHistory), typeof(bool), typeof(StftHeatmap), new PropertyMetadata(true,
            (sender, _) => { var chart = (StftHeatmap)sender; chart.ClearSegments(); chart.UpdatePreview(chart.Preview); }));

    public static readonly DependencyProperty VisibleStartSecondsProperty = DependencyProperty.Register(
        nameof(VisibleStartSeconds), typeof(double?), typeof(StftHeatmap), new PropertyMetadata(null,
            (sender, _) => { var chart = (StftHeatmap)sender; chart.ClearSegments(); chart.UpdatePreview(chart.Preview); }));

    public bool RetainHistory
    {
        get => (bool)GetValue(RetainHistoryProperty);
        set => SetValue(RetainHistoryProperty, value);
    }

    public double? VisibleStartSeconds
    {
        get => (double?)GetValue(VisibleStartSecondsProperty);
        set => SetValue(VisibleStartSecondsProperty, value);
    }

    public StftHeatmap()
    {
        InitializeComponent();
        xAxis = CreateAxis("", AxisAlignment.Bottom);
        xAxis.Height = 42;
        yAxis = CreateAxis("", AxisAlignment.Left);
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
        RecordingTimeAxisPresentation.SetAxis(this, xAxis);
        Surface.YAxes.Add(yAxis);
        Surface.ChartModifier = cursor;
        ColourMap.ColorPalette = palette;
    }

    private static NumericAxis CreateAxis(string title, AxisAlignment alignment) => new()
    {
        AxisAlignment = alignment,
        AxisTitle = title,
        Width = alignment == AxisAlignment.Left ? 32 : double.NaN,
        FontSize = 12,
        TickTextBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        DrawMajorBands = false,
        DrawMajorGridLines = false,
        DrawMinorGridLines = false,
    };

    private static void OnPreviewChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((StftHeatmap)sender).UpdatePreview((StftPreview?)args.NewValue);

    private void UpdatePreview(StftPreview? preview)
    {
        if (preview is null)
        {
            ClearSegments();
            return;
        }
        if (preview.TimesSeconds.Length == 0 || preview.FrequenciesHz.Length == 0)
            return;

        if (!RetainHistory || preview.TimesSeconds[^1] < renderedThroughSeconds - 1e-9)
            ClearSegments();

        if (renderedFrequencies is not null && !renderedFrequencies.SequenceEqual(preview.FrequenciesHz))
            ClearSegments();
        renderedFrequencies = preview.FrequenciesHz.ToArray();
        var newColumns = Enumerable.Range(0, preview.TimesSeconds.Length)
            .Where(index => !renderedTimes.Contains(TimeKey(preview.TimesSeconds[index])) &&
                (VisibleStartSeconds is not { } start || preview.TimesSeconds[index] >= start))
            .ToArray();
        if (newColumns.Length > 0)
            AppendColumns(preview, newColumns);

        minimumDb = Math.Min(minimumDb, preview.MinimumDb);
        maximumDb = Math.Max(maximumDb, preview.MaximumDb);
        palette.Minimum = minimumDb;
        palette.Maximum = maximumDb;
        ColourMap.Minimum = minimumDb;
        ColourMap.Maximum = maximumDb;
        var timeStep = preview.TimesSeconds.Length > 1
            ? preview.TimesSeconds[1] - preview.TimesSeconds[0]
            : 1.0;
        var dataEnd = Math.Max(preview.TimesSeconds[^1], renderedThroughSeconds) + timeStep / 2;
        var visibleStart = VisibleStartSeconds is { } start && double.IsFinite(start)
            ? Math.Max(0, start)
            : double.IsFinite(historyStartSeconds) ? historyStartSeconds : preview.TimesSeconds[0];
        var dataStart = double.IsFinite(historyStartSeconds) ? historyStartSeconds : visibleStart;
        var chartStart = Math.Max(visibleStart, dataStart);
        xAxis.VisibleRange = new DoubleRange(chartStart, Math.Max(chartStart + 1, dataEnd));
        yAxis.VisibleRange = new DoubleRange(preview.FrequenciesHz[0], preview.FrequenciesHz[^1]);
    }

    private void AppendColumns(StftPreview preview, int[] columnIndexes)
    {
        var timeStep = preview.TimesSeconds.Length > 1
            ? preview.TimesSeconds[1] - preview.TimesSeconds[0]
            : 1.0;
        var groups = new List<List<int>>();
        foreach (var index in columnIndexes)
        {
            if (groups.Count == 0 || !AreAdjacent(preview.TimesSeconds[groups[^1][^1]], preview.TimesSeconds[index], timeStep))
            {
                var group = new List<int>();
                var previousIndex = index - 1;
                if (previousIndex >= 0 && renderedTimes.Contains(TimeKey(preview.TimesSeconds[previousIndex])) &&
                    AreAdjacent(preview.TimesSeconds[previousIndex], preview.TimesSeconds[index], timeStep))
                    group.Add(previousIndex);
                groups.Add(group);
            }
            groups[^1].Add(index);
        }

        foreach (var group in groups)
        {
            var height = preview.FrequenciesHz.Length;
            var values = new double[height, group.Count];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < group.Count; x++)
                    values[y, x] = preview.PowerDb[group[x]][y] ?? double.NaN;

            var firstIndex = group[0];
            var xStep = group.Count > 1
                ? preview.TimesSeconds[group[1]] - preview.TimesSeconds[firstIndex]
                : timeStep;
            var yStep = height > 1 ? preview.FrequenciesHz[1] - preview.FrequenciesHz[0] : 1.0;
            var xBoundaries = new double[group.Count + 1];
            xBoundaries[0] = !double.IsFinite(historyStartSeconds) && VisibleStartSeconds is { } start && double.IsFinite(start)
                ? Math.Max(0, start)
                : preview.TimesSeconds[firstIndex] - xStep / 2;
            for (var x = 1; x < group.Count; x++)
                xBoundaries[x] = (preview.TimesSeconds[group[x - 1]] + preview.TimesSeconds[group[x]]) / 2;
            var lastStep = group.Count > 1 ? xStep : timeStep;
            xBoundaries[^1] = preview.TimesSeconds[group[^1]] + lastStep / 2;

            var dataSeries = new NonUniformHeatmapDataSeries<double, double, double>(
                values, index => xBoundaries[index], index => preview.FrequenciesHz[0] + index * yStep);
            var segment = new FastNonUniformHeatmapRenderableSeries
            {
                DataSeries = dataSeries,
                ColorMap = palette,
                UseLinearTextureFiltering = false,
                DrawTextInCell = false,
                XAxisId = xAxis.Id,
                YAxisId = yAxis.Id,
            };
            Surface.RenderableSeries.Add(segment);
            heatmapSegments.Add(segment);
            historyStartSeconds = double.IsFinite(historyStartSeconds)
                ? Math.Min(historyStartSeconds, xBoundaries[0])
                : xBoundaries[0];
        }
        foreach (var index in columnIndexes)
            renderedTimes.Add(TimeKey(preview.TimesSeconds[index]));
        renderedThroughSeconds = Math.Max(renderedThroughSeconds, preview.TimesSeconds[columnIndexes[^1]]);
    }

    private static double TimeKey(double time) => Math.Round(time, 9);

    private static bool AreAdjacent(double left, double right, double timeStep) =>
        right - left <= timeStep * 1.000001;

    private void ClearSegments()
    {
        foreach (var segment in heatmapSegments)
        {
            Surface.RenderableSeries.Remove(segment);
            segment.DataSeries = null;
        }
        heatmapSegments.Clear();
        renderedTimes.Clear();
        renderedThroughSeconds = double.NegativeInfinity;
        historyStartSeconds = double.NaN;
        renderedFrequencies = null;
        minimumDb = double.PositiveInfinity;
        maximumDb = double.NegativeInfinity;
    }
}
