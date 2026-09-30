using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrainPlatform.Desktop.Modules.Algorithms.ViewModels.Catalog;
using SciChart.Charting.ChartModifiers;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.Axes.LabelProviders;
using SciChart.Charting.Visuals.Annotations;
using SciChart.Charting.Visuals.PaletteProviders;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;

namespace BrainPlatform.Desktop.Modules.Algorithms.Views.Psd;

public partial class PsdBandShareChart : UserControl
{
    private INotifyCollectionChanged? observedCollection;
    private readonly NumericAxis xAxis = new()
    {
        AxisTitle = "频段",
        AutoRange = AutoRange.Never,
        AutoTicks = false,
        MajorDelta = 1,
        MinorDelta = 1,
        FontSize = 13,
        DrawMajorBands = false,
        DrawMajorGridLines = false,
        DrawMinorGridLines = false,
    };
    private readonly NumericAxis yAxis = new()
    {
        AxisAlignment = AxisAlignment.Left,
        AxisTitle = "相对功率（%）",
        AutoRange = AutoRange.Never,
        VisibleRange = new DoubleRange(0, 1.08),
        TextFormatting = "0%",
        FontSize = 13,
        DrawMajorBands = false,
        DrawMajorGridLines = true,
        DrawMinorGridLines = false,
    };

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IEnumerable), typeof(PsdBandShareChart),
        new PropertyMetadata(null, OnPointsChanged));

    public IEnumerable? Points { get => (IEnumerable?)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    public PsdBandShareChart()
    {
        InitializeComponent();
        Surface.Background = Brushes.White;
        Surface.ShowLicensingWarnings = false;
        Surface.XAxes.Add(xAxis);
        Surface.YAxes.Add(yAxis);
        Surface.ChartModifier = new RolloverModifier
        {
            IsEnabled = true,
            DrawVerticalLine = true,
            ShowTooltipOn = ShowTooltipOptions.MouseOver,
        };
    }

    private static void OnPointsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var chart = (PsdBandShareChart)sender;
        if (chart.observedCollection is not null)
            chart.observedCollection.CollectionChanged -= chart.OnPointsCollectionChanged;
        chart.observedCollection = args.NewValue as INotifyCollectionChanged;
        if (chart.observedCollection is not null)
            chart.observedCollection.CollectionChanged += chart.OnPointsCollectionChanged;
        chart.RebuildSeries();
    }

    private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => RebuildSeries();

    private void RebuildSeries()
    {
        var points = Points?.Cast<AlgorithmListViewModel.PsdBandSharePoint>().ToArray() ?? [];
        Surface.RenderableSeries.Clear();
        Surface.Annotations.Clear();
        xAxis.LabelProvider = new BandLabelProvider(points.Select(point => point.Name).ToArray());
        xAxis.VisibleRange = new DoubleRange(-0.7, Math.Max(4, points.Length - 1) + 0.7);

        var values = new XyDataSeries<double, double> { SeriesName = "频段功率占比" };
        var colors = new List<Brush>();
        foreach (var (point, index) in points.Select((value, index) => (value, index)))
        {
            if (!point.IsAvailable || !double.IsFinite(point.Share)) continue;
            values.Append(index, point.Share);
            var color = (Color)ColorConverter.ConvertFromString(point.Color);
            colors.Add(new SolidColorBrush(color));
            Surface.Annotations.Add(new TextAnnotation
            {
                X1 = index,
                Y1 = Math.Min(1.04, point.Share + 0.015),
                Text = point.ShareText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                HorizontalAnchorPoint = HorizontalAnchorPoint.Center,
                VerticalAnchorPoint = VerticalAnchorPoint.Bottom,
                AnnotationCanvas = AnnotationCanvas.AboveChart,
                IsEditable = false,
            });
        }
        if (values.Count > 0)
            Surface.RenderableSeries.Add(new FastColumnRenderableSeries
            {
                DataSeries = values,
                Fill = colors[0],
                StrokeThickness = 0,
                DataPointWidth = 0.6,
                PaletteProvider = new BandFillPaletteProvider(colors),
            });

        EmptyState.Visibility = Surface.RenderableSeries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var unavailable = points.Where(point => !point.IsAvailable).Select(point => point.Name).ToArray();
        UnavailableBands.Text = unavailable.Length == 0 ? "" : $"不可用：{string.Join("、", unavailable)}";
    }

    private sealed class BandLabelProvider(string[] labels) : NumericLabelProvider
    {
        public override string FormatLabel(IComparable dataValue)
        {
            var value = Convert.ToDouble(dataValue, CultureInfo.InvariantCulture);
            var index = (int)Math.Round(value);
            return Math.Abs(value - index) < 1e-6 && index >= 0 && index < labels.Length
                ? labels[index] : "";
        }
    }

    private sealed class BandFillPaletteProvider(IReadOnlyList<Brush> colors) : IFillPaletteProvider
    {
        public void OnBeginSeriesDraw(IRenderableSeries series) { }

        public Brush OverrideFillBrush(IRenderableSeries series, int index, IPointMetadata metadata) => colors[index];
    }
}
